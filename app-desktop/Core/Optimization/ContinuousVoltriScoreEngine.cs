using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Core.Optimization
{
    /// <summary>
    /// Modelo estruturado do VoltriScore contínuo e multi-dimensional.
    /// Pontuação de 0 a 1000 baseada em medições reais e físicas do hardware.
    /// </summary>
    public sealed class VoltriScoreState
    {
        public int TotalScore { get; init; } // 0 a 1000
        public int CpuScore { get; init; } // 0 a 200
        public int MemoryScore { get; init; } // 0 a 200
        public int ThermalScore { get; init; } // 0 a 200
        public int DiskScore { get; init; } // 0 a 200
        public int LatencyScore { get; init; } // 0 a 200

        public string RatingLabel { get; init; } = "Calculando...";
        public string RatingColorHex { get; init; } = "#31A8FF";
        public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;

        // Métricas brutas registradas
        public double CpuUsagePercent { get; init; }
        public double RamUsagePercent { get; init; }
        public double AvailableRamMb { get; init; }
        public double TotalRamMb { get; init; }
        public double CpuTemperatureC { get; init; }
        public float DiskQueueLength { get; init; }
        public double TimerResolutionMs { get; init; }

        public IReadOnlyList<string> OptimizationSuggestions { get; init; } = Array.Empty<string>();
    }

    /// <summary>
    /// Motor Contínuo de Avaliação em Tempo Real do VoltriScore™ (0 - 1000).
    /// Calcula a pontuação real do sistema com base em telemetria física (sem placebos).
    /// </summary>
    public sealed class ContinuousVoltriScoreEngine : IDisposable
    {
        private static readonly Lazy<ContinuousVoltriScoreEngine> _instance = 
            new(() => new ContinuousVoltriScoreEngine(App.LoggingService));
        public static ContinuousVoltriScoreEngine Instance => _instance.Value;

        private readonly ILoggingService? _logger;
        private readonly object _stateLock = new();
        private CancellationTokenSource? _cts;
        private Task? _monitorTask;
        private volatile bool _isRunning;

        private VoltriScoreState _currentState;
        public VoltriScoreState CurrentState
        {
            get
            {
                lock (_stateLock)
                {
                    return _currentState;
                }
            }
            private set
            {
                lock (_stateLock)
                {
                    _currentState = value;
                }
            }
        }

        public event EventHandler<VoltriScoreState>? ScoreUpdated;

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtQueryTimerResolution(out uint minResolution, out uint maxResolution, out uint currentResolution);

        public ContinuousVoltriScoreEngine(ILoggingService? logger)
        {
            _logger = logger;
            _currentState = new VoltriScoreState
            {
                TotalScore = 750,
                CpuScore = 150,
                MemoryScore = 150,
                ThermalScore = 150,
                DiskScore = 150,
                LatencyScore = 150,
                RatingLabel = "Inicializando...",
                RatingColorHex = "#8B31FF"
            };

            _logger?.LogInfo("[VoltriScoreEngine] Instanciado. Pronto para monitoramento contínuo.");
        }

        public void Start()
        {
            lock (_stateLock)
            {
                if (_isRunning) return;
                _isRunning = true;
                _cts = new CancellationTokenSource();
            }

            _logger?.LogInfo("[VoltriScoreEngine] Iniciando loop de telemetria contínua do VoltriScore...");
            _monitorTask = Task.Run(() => MonitorLoopAsync(_cts.Token));
        }

        public void Stop()
        {
            lock (_stateLock)
            {
                if (!_isRunning) return;
                _isRunning = false;
                _cts?.Cancel();
            }

            _logger?.LogInfo("[VoltriScoreEngine] Monitoramento de VoltriScore finalizado.");
        }

        private async Task MonitorLoopAsync(CancellationToken ct)
        {
            // Delay inicial para dar tempo de outros subsistemas estabilizarem
            await Task.Delay(3000, ct).ConfigureAwait(false);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    EvaluateScoreNow();
                    // Avalia a cada 3 segundos em background sem onerar CPU
                    await Task.Delay(3000, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger?.LogError($"[VoltriScoreEngine] Erro durante ciclo de avaliação do score: {ex.Message}", ex);
                    await Task.Delay(5000, ct).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Realiza a avaliação instantânea e matemática de todas as 5 dimensões do PC.
        /// </summary>
        public VoltriScoreState EvaluateScoreNow()
        {
            var sw = Stopwatch.StartNew();

            // 1. Coleta de Métricas Reais do Sistema
            double cpuPercent = SystemMetricsCache.Instance.CpuPercent;
            double cpuTemp = SystemMetricsCache.Instance.CpuTemperature;
            float diskQueue = SystemMetricsCache.Instance.DiskQueueLength;

            // Memória Física
            double ramUsedPercent = 50.0;
            double availRamMb = 4096.0;
            double totalRamMb = 8192.0;

            var memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
            if (GlobalMemoryStatusEx(ref memStatus))
            {
                ramUsedPercent = memStatus.dwMemoryLoad;
                availRamMb = memStatus.ullAvailPhys / (1024.0 * 1024.0);
                totalRamMb = memStatus.ullTotalPhys / (1024.0 * 1024.0);
            }

            // Timer Resolution
            double timerResMs = 1.0;
            try
            {
                if (NtQueryTimerResolution(out _, out _, out uint curRes) == 0 && curRes > 0)
                {
                    timerResMs = curRes / 10000.0; // 100ns units para ms
                }
            }
            catch
            {
                timerResMs = 1.0;
            }

            // 2. Cálculos das 5 Dimensões (0 a 200 pts cada)

            // Dimensão 1: CPU Efficiency (0 - 200)
            // 0% CPU = 200 pts; 100% CPU = 40 pts
            int cpuScore = (int)Math.Clamp(200.0 - (cpuPercent * 1.6), 30.0, 200.0);

            // Dimensão 2: Memory Performance (0 - 200)
            // 20% RAM usada = 200 pts; 95% RAM usada = 40 pts
            double memScoreCalc = 200.0 - (Math.Max(0, ramUsedPercent - 15.0) * 1.88);
            if (availRamMb < 1500) memScoreCalc -= 30.0;
            int memoryScore = (int)Math.Clamp(memScoreCalc, 30.0, 200.0);

            // Dimensão 3: Thermal & Power Efficiency (0 - 200)
            // <= 50°C = 200 pts; 85°C = 70 pts; > 90°C = 30 pts
            double thermalScoreCalc = 200.0;
            if (cpuTemp > 50.0)
            {
                thermalScoreCalc -= (cpuTemp - 50.0) * 3.5;
            }
            else if (cpuTemp <= 0) // Sensor ausente ou ACPI fallback
            {
                thermalScoreCalc = 175.0; // Valor neutro seguro
            }
            int thermalScore = (int)Math.Clamp(thermalScoreCalc, 30.0, 200.0);

            // Dimensão 4: Disk & I/O Responsiveness (0 - 200)
            // Queue 0.0 = 200 pts; Queue >= 3.0 = 40 pts
            int diskScore = (int)Math.Clamp(200.0 - (diskQueue * 45.0), 40.0, 200.0);

            // Dimensão 5: Latency & Responsiveness (0 - 200)
            // Timer 0.5ms = 200 pts; Timer 1.0ms = 160 pts; Timer 15.6ms = 60 pts
            double latencyScoreCalc = 200.0;
            if (timerResMs <= 0.6)
            {
                latencyScoreCalc = 200.0;
            }
            else if (timerResMs <= 1.0)
            {
                latencyScoreCalc = 170.0;
            }
            else
            {
                latencyScoreCalc = Math.Clamp(170.0 - ((timerResMs - 1.0) * 7.5), 40.0, 160.0);
            }
            int latencyScore = (int)latencyScoreCalc;

            // Total Score (0 - 1000)
            int totalScore = Math.Clamp(cpuScore + memoryScore + thermalScore + diskScore + latencyScore, 100, 1000);

            // Classificação e Cor
            string label;
            string color;
            if (totalScore >= 900)
            {
                label = "Excelente (Ultra Fluido)";
                color = "#00E676"; // Verde Neon
            }
            else if (totalScore >= 780)
            {
                label = "Ótimo (Rápido e Estável)";
                color = "#31A8FF"; // Azul Elétrico
            }
            else if (totalScore >= 620)
            {
                label = "Regular (Pode Melhorar)";
                color = "#FFB300"; // Âmbar
            }
            else
            {
                label = "Degradado (Requer Otimização)";
                color = "#FF5252"; // Vermelho
            }

            // Sugestões Reais Acionáveis
            var suggestions = new List<string>();
            if (memoryScore < 140)
                suggestions.Add($"Memória sob pressão ({ramUsedPercent:F0}% em uso). Execute o Smart Clean para liberar até {(totalRamMb - availRamMb) * 0.35:F0} MB.");
            if (cpuScore < 130)
                suggestions.Add($"Uso de CPU elevado ({cpuPercent:F0}%). Processos em background competindo por tempo de thread.");
            if (thermalScore < 130 && cpuTemp > 75)
                suggestions.Add($"Temperatura da CPU elevada ({cpuTemp:F0}°C). Ative o perfil térmico balanceado para evitar throttling.");
            if (latencyScore < 160 && timerResMs > 1.0)
                suggestions.Add($"Resolução do timer do Windows em {timerResMs:F1}ms. Ative o modo 0.5ms para máxima responsividade de input.");
            if (diskScore < 140)
                suggestions.Add("Fila de disco elevada. Limpeza de arquivos temporários e TRIM recomendados.");

            var newState = new VoltriScoreState
            {
                TotalScore = totalScore,
                CpuScore = cpuScore,
                MemoryScore = memoryScore,
                ThermalScore = thermalScore,
                DiskScore = diskScore,
                LatencyScore = latencyScore,
                RatingLabel = label,
                RatingColorHex = color,
                TimestampUtc = DateTime.UtcNow,
                CpuUsagePercent = cpuPercent,
                RamUsagePercent = ramUsedPercent,
                AvailableRamMb = availRamMb,
                TotalRamMb = totalRamMb,
                CpuTemperatureC = cpuTemp,
                DiskQueueLength = diskQueue,
                TimerResolutionMs = timerResMs,
                OptimizationSuggestions = suggestions
            };

            CurrentState = newState;
            sw.Stop();

            _logger?.LogTrace($"[VoltriScoreEngine] Score Atualizado: {totalScore}/1000 (CPU:{cpuScore} RAM:{memoryScore} Therm:{thermalScore} Disk:{diskScore} Lat:{latencyScore}) em {sw.ElapsedMilliseconds}ms");

            try
            {
                ScoreUpdated?.Invoke(this, newState);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[VoltriScoreEngine] Exceção em subscriber do ScoreUpdated: {ex.Message}");
            }

            return newState;
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
        }
    }
}
