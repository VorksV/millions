using VoltrisOptimizer.Utils;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Text.Json;
using VoltrisOptimizer.Core;
using VoltrisOptimizer.Core.Body;

namespace VoltrisOptimizer.Services.Benchmark
{
    /// <summary>
    /// Serviço singleton de benchmark e score de performance.
    /// Coleta apenas quando solicitado (sem polling loop próprio).
    /// Reusa SystemMetricsCache para métricas já coletadas.
    /// Zero SafePerformanceCounter, zero WMI adicional no hot path.
    /// </summary>
    public sealed class PerformanceBenchmarkService : IPerformanceBenchmarkService, IAutoStartService
    {
        private readonly ILoggingService _logger;
        private readonly PerformanceScoreEngine _scoreEngine = new();
        private readonly string _baselinePath;
        private StructuralBaseline? _structural;
        private PerformanceSnapshot? _currentSnapshot;
        private PerformanceSnapshot? _startupBaseline;
        private PerformanceSnapshot? _optimizationBefore;
        private OptimizationImpact? _lastImpact;
        private readonly object _lock = new();
        private bool _running;

        public PerformanceSnapshot? CurrentSnapshot
        {
            get { lock (_lock) return _currentSnapshot; }
        }

        public int StructuralScore
        {
            get { lock (_lock) return _structural?.StructuralScore ?? 50; }
        }

        public int DynamicScore { get; private set; }

        public int TotalScore
        {
            get
            {
                lock (_lock)
                    return Math.Min(100, Math.Max(0, StructuralScore + DynamicScore));
            }
        }

        public PerformanceSnapshot? StartupBaseline
        {
            get { lock (_lock) return _startupBaseline; }
        }

        public OptimizationImpact? LastOptimizationImpact
        {
            get { lock (_lock) return _lastImpact; }
        }

        public PerformanceBenchmarkService(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Voltris", "Brain");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            _baselinePath = Path.Combine(dir, "structural_score.json");
        }

        public async Task StartAsync(CancellationToken ct = default)
        {
            if (_running) return;
            _running = true;

            _logger.LogInfo("[BENCHMARK] Inicializando serviço de benchmark...");

            // Carregar baseline estrutural persistido
            await LoadStructuralAsync();

            // Coletar snapshot inicial (baseline da sessão)
            var initialSnap = CollectSnapshot();
            lock (_lock)
            {
                _startupBaseline = initialSnap;
                _currentSnapshot = initialSnap;
            }

            // Atualizar score estrutural com baseline real
            int structScore = _scoreEngine.CalculateStructural(initialSnap);
            lock (_lock)
            {
                if (_structural != null)
                {
                    // Média entre persistido e real para evitar saltos bruscos
                    _structural.StructuralScore = (int)Math.Round(
                        (_structural.StructuralScore * 0.4) + (structScore * 0.6));
                    _structural.LastUpdatedUtc = DateTime.UtcNow;
                }
                else
                {
                    _structural = new StructuralBaseline
                    {
                        StructuralScore = structScore,
                        FirstRunUtc = DateTime.UtcNow,
                        LastUpdatedUtc = DateTime.UtcNow
                    };
                }
            }

            // Score dinâmico inicial
            DynamicScore = _scoreEngine.SmoothDynamic(initialSnap);

            _logger.LogInfo($"[BENCHMARK] Score estrutural: {StructuralScore}/70 | dinâmico: {DynamicScore}/30 | total: {TotalScore}/100");

            // Persistir baseline estrutural
            await SaveStructuralAsync();
        }

        public Task StopAsync()
        {
            _running = false;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Coleta snapshot atual do sistema.
        /// Todas as métricas vêm de APIs nativas ou caches existentes.
        /// Custo total: &lt;10ms. Zero SafePerformanceCounter, zero WMI.
        /// </summary>
        public PerformanceSnapshot CollectSnapshot()
        {
            var cache = SystemMetricsCache.Instance;
            var now = DateTime.UtcNow;

            // Timer Resolution via NtQueryTimerResolution (nativa, ~0.1ms)
            double timerMs = 15.6;
            try
            {
                NativeMethods.NtQueryTimerResolution(out _, out _, out uint current);
                timerMs = current / 10000.0; // 10000ns = 1ms units → ms
            }
            catch { }

            // EPP via PowerArm se disponível, fallback 50
            int epp = 50;
            try
            {
                var powerArm = App.Services?.GetService(typeof(IPowerArm)) as IPowerArm;
                if (powerArm != null)
                    epp = powerArm.CurrentEpp;
            }
            catch { }

            // RAM disponível via SystemMetricsCache (já coletado)
            double availableRamGb = cache.AvailableRamMb / 1024.0;
            double totalRamGb = cache.Hardware.TotalRamGb > 0
                ? cache.Hardware.TotalRamGb
                : 16.0;

            // Processos (leve ~50ms apenas quando solicitado)
            int procCount = 0;
            try
            {
                procCount = Process.GetProcesses().Length;
            }
            catch { procCount = 80; }

            // HAGS via LicenseTokenStore ou cache
            bool hags = false;
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", false);
                hags = (key?.GetValue("HwSchMode") as int?) == 2;
            }
            catch { }

            // Boot uptime
            long bootMs = Environment.TickCount;

            return new PerformanceSnapshot
            {
                TimestampUtc = now,
                TimerResolutionMs = timerMs,
                Epp = epp,
                AvailableRamGb = Math.Round(availableRamGb, 1),
                TotalRamGb = totalRamGb,
                ProcessCount = procCount,
                DiskQueueLength = cache.DiskQueueLength,
                HagsEnabled = hags,
                CpuUsagePercent = (int)Math.Round(cache.CpuPercent),
                GpuUsagePercent = 0, // GPU load via BrainSensorHub (não fazemos polling próprio)
                IsLaptop = cache.Hardware.IsLaptop,
                HasSsd = cache.Hardware.HasSsd,
                BootUptimeMs = bootMs
            };
        }

        public void BeginOptimization()
        {
            var before = CollectSnapshot();
            lock (_lock)
                _optimizationBefore = before;
        }

        public void EndOptimization(long durationMs)
        {
            PerformanceSnapshot before;
            lock (_lock)
            {
                if (_optimizationBefore == null) return;
                before = _optimizationBefore;
                _optimizationBefore = null;
            }

            var after = CollectSnapshot();

            var impact = new OptimizationImpact
            {
                TimerBeforeMs = (int)Math.Round(before.TimerResolutionMs),
                TimerAfterMs = (int)Math.Round(after.TimerResolutionMs),
                EppBefore = before.Epp,
                EppAfter = after.Epp,
                RamBeforeGb = before.AvailableRamGb,
                RamAfterGb = after.AvailableRamGb,
                ProcessesBefore = before.ProcessCount,
                ProcessesAfter = after.ProcessCount,
                HagsChanged = before.HagsEnabled != after.HagsEnabled,
                GamingModeChanged = before.Epp != after.Epp && after.Epp <= 25,
                DurationMs = durationMs
            };

            lock (_lock)
            {
                _lastImpact = impact;
                _currentSnapshot = after;

                // Atualizar score estrutural
                if (_structural != null)
                {
                    int newStruct = _scoreEngine.CalculateStructural(after);
                    _structural.StructuralScore = (int)Math.Round(
                        (_structural.StructuralScore * 0.5) + (newStruct * 0.5));
                    _structural.TotalOptimizationsPerformed++;
                    if (_structural.StructuralScore > _structural.BestStructuralScore)
                        _structural.BestStructuralScore = _structural.StructuralScore;
                    _structural.LastUpdatedUtc = DateTime.UtcNow;
                }
            }

            // Score dinâmico pós-otimização (com smooth)
            DynamicScore = _scoreEngine.SmoothDynamic(after);

            _scoreEngine.ResetSmoothing();

            _logger.LogInfo($"[BENCHMARK] Otimização concluída em {durationMs}ms | " +
                $"Score: {TotalScore}/100 | Ganho estrutural: +{impact.StructuralGain} | " +
                $"Timer: {impact.TimerBeforeMs}→{impact.TimerAfterMs}ms | " +
                $"EPP: {impact.EppBefore}→{impact.EppAfter}");

            // Persistir score estrutural
            _ = SaveStructuralAsync();
        }

        private async Task LoadStructuralAsync()
        {
            try
            {
                if (!File.Exists(_baselinePath)) return;
                var json = await File.ReadAllTextAsync(_baselinePath);
                var data = JsonSerializer.Deserialize<StructuralBaseline>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                if (data != null)
                {
                    lock (_lock) _structural = data;
                    _logger.LogInfo($"[BENCHMARK] Baseline estrutural carregado: score={data.StructuralScore}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[BENCHMARK] Falha ao carregar baseline: {ex.Message}");
            }
        }

        private async Task SaveStructuralAsync()
        {
            try
            {
                StructuralBaseline? data;
                lock (_lock) data = _structural;
                if (data == null) return;

                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                await File.WriteAllTextAsync(_baselinePath, json);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[BENCHMARK] Falha ao salvar baseline: {ex.Message}");
            }
        }

        private static class NativeMethods
        {
            [System.Runtime.InteropServices.DllImport("ntdll.dll")]
            internal static extern int NtQueryTimerResolution(
                out uint minResolution, out uint maxResolution, out uint currentResolution);
        }
    }
}

