using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Diagnostics.Tracing;
using VoltrisOptimizer.Core;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Monitoring.Interfaces;

namespace VoltrisOptimizer.Services.Monitoring.Implementation
{
    public class EtwFrameTimeMonitor : IEtwFrameTimeMonitor
    {
        private readonly ILoggingService _logger;
        private TraceEventSession? _session;
        private Task? _etwTask;
        private CancellationTokenSource? _cts;
        private Timer? _uiTimer;
        private bool _disposed;
        
        public event EventHandler<FrameMetrics>? MetricsUpdated;
        public bool IsRunning { get; private set; }

        // Provider GUIDs
        private static readonly Guid DxgiProviderGuid = new Guid("ca11c036-0102-4a2d-a6ad-f03cfed5d3c9");
        private static readonly Guid D3d9ProviderGuid = new Guid("783aca0a-790e-4d7f-8451-aa850511c6b9");
        private static readonly Guid DxgKrnlProviderGuid = new Guid("802ec45a-1e99-4b83-9920-87c98277ba9d");

        // Tracking data
        private readonly ConcurrentDictionary<int, ProcessFrameTracker> _processTrackers = new();
        private int _activeGamePid = 0;
        private string _activeGameName = string.Empty;
        private double _lastActiveGameUpdateMs = 0;

        public EtwFrameTimeMonitor(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task StartAsync()
        {
            if (IsRunning) return Task.CompletedTask;
            
            _cts = new CancellationTokenSource();
            _processTrackers.Clear();
            _activeGamePid = 0;

            try
            {
                // Fallback check: Se já existir uma sessão do Voltris, mata antes
                string sessionName = "VoltrisEtwFrameMonitorSession";
                var activeSessions = TraceEventSession.GetActiveSessionNames();
                if (activeSessions.Contains(sessionName))
                {
                    using (var oldSession = new TraceEventSession(sessionName))
                    {
                        oldSession.Stop();
                    }
                }

                _session = new TraceEventSession(sessionName);
                _session.EnableProvider(DxgiProviderGuid, TraceEventLevel.Verbose);
                _session.EnableProvider(D3d9ProviderGuid, TraceEventLevel.Verbose);
                // DxgKrnl é obrigatório para capturar jogos Vulkan, DX12 Exclusive e MPO Flips.
                _session.EnableProvider(DxgKrnlProviderGuid, TraceEventLevel.Verbose);

                _session.Source.Dynamic.All += OnTraceEvent;

                _etwTask = Task.Run(() =>
                {
                    try
                    {
                        _logger.LogInfo("[EtwFrameTimeMonitor] Iniciando Process() do ETW...");
                        _session.Source.Process();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"[EtwFrameTimeMonitor] Erro fatal no ETW Source.Process: {ex.Message}");
                    }
                });

                _uiTimer = new Timer(OnUiTimerTick, null, 1000, 1000); // UI atualiza a cada 1000ms (reduzido de 250ms para evitar CPU wakeups excessivos)
                IsRunning = true;
                _logger.LogSuccess("[EtwFrameTimeMonitor] ETW Session iniciada com sucesso. Monitoramento Passivo ATIVO.");
            }
            catch (UnauthorizedAccessException)
            {
                _logger.LogError("[EtwFrameTimeMonitor] Falha de privilégios. ETW exige Execução como Administrador.");
                IsRunning = false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[EtwFrameTimeMonitor] Falha ao iniciar sessão ETW: {ex.Message}");
                IsRunning = false;
            }

            return Task.CompletedTask;
        }

        private void OnTraceEvent(TraceEvent data)
        {
            if (_cts?.IsCancellationRequested == true) return;

            string providerName = data.ProviderName;
            string eventName = data.EventName;

            bool isDxgi = providerName.IndexOf("DXGI", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isD3d9 = providerName.IndexOf("D3D9", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isDxgKrnl = providerName.IndexOf("DxgKrnl", StringComparison.OrdinalIgnoreCase) >= 0;

            // Filtros restritos: Exigimos o sufixo "Start" para os Presents.
            // Se aceitarmos tudo (Stop, Info), um único frame pode gerar 2 a 4 eventos ETW em tempos diferentes,
            // burlando a janela de deduplicação (0.5ms) e dobrando ou triplicando o FPS real (ex: 41 -> 110 FPS).
            bool isDxgiFrame = isDxgi && eventName.Contains("Present", StringComparison.OrdinalIgnoreCase) && eventName.Contains("Start", StringComparison.OrdinalIgnoreCase);
            bool isD3d9Frame = isD3d9 && eventName.Contains("Present", StringComparison.OrdinalIgnoreCase) && eventName.Contains("Start", StringComparison.OrdinalIgnoreCase);
            bool isDxgKrnlFrame = isDxgKrnl && (
                (eventName.Contains("Present", StringComparison.OrdinalIgnoreCase) && eventName.Contains("Start", StringComparison.OrdinalIgnoreCase)) ||
                (eventName.Contains("Flip", StringComparison.OrdinalIgnoreCase) && eventName.Contains("Info", StringComparison.OrdinalIgnoreCase)) ||
                (eventName.Contains("Blit", StringComparison.OrdinalIgnoreCase) && eventName.Contains("Info", StringComparison.OrdinalIgnoreCase)));

            if (!isDxgiFrame && !isD3d9Frame && !isDxgKrnlFrame) return;

            int pid = data.ProcessID;
            if (pid <= 0) return; // Processos inválidos ou system idle

            // Pega ou cria o tracker do processo
            var tracker = _processTrackers.GetOrAdd(pid, p => new ProcessFrameTracker(p));

            var now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency * 1000.0;

            // State Machine Dinâmica para rastrear a API principal (Impede double-counting de DXGI + DxgKrnl)
            // Priority: DXGI > D3D9 > DxgKrnl
            // Se a API superior não renderizou nada no último segundo, permite o fallback (Vulkan usa DxgKrnl).
            if (isDxgiFrame) 
            {
                tracker.ActiveApi = "DXGI";
                tracker.LastDxgiFrameMs = now;
            }
            else if (isD3d9Frame) 
            {
                if (now - tracker.LastDxgiFrameMs > 1000) tracker.ActiveApi = "D3D9";
            }
            else if (isDxgKrnlFrame) 
            {
                if (now - tracker.LastDxgiFrameMs > 1000 && tracker.ActiveApi != "D3D9") tracker.ActiveApi = "DxgKrnl";
            }

            // Só conta o frame se ele vier da API principal atual associada ao jogo
            if (tracker.ActiveApi == "DXGI" && !isDxgiFrame) return;
            if (tracker.ActiveApi == "D3D9" && !isD3d9Frame) return;
            if (tracker.ActiveApi == "DxgKrnl" && !isDxgKrnlFrame) return;

            tracker.AddFrame(data.TimeStampRelativeMSec);

            // A detecção de processo principal foi movida para o OnUiTimerTick
            // utilizando a lógica de Foreground Window.
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        // Whitelist de processos do sistema que nunca devem ser considerados como "jogo ativo"
        private static readonly HashSet<string> SystemProcessBlacklist = new(StringComparer.OrdinalIgnoreCase)
        {
            "dwm", "explorer", "Taskmgr", "csrss", "lsass", "svchost",
            "SearchHost", "ShellExperienceHost", "StartMenuExperienceHost",
            "TextInputHost", "WindowsTerminal", "powershell", "cmd",
            "devenv", "msedge", "chrome", "firefox", "Code",
            "Antigravity IDE", "antigravity", "SystemSettings",
            "ApplicationFrameHost", "RuntimeBroker", "SecurityHealthSystray",
            "CompPkgSrv", "WmiPrvSE", "conhost", "sihost",
            "fontdrvhost", "dllhost", "mstsc", "Widgets"
        };

        private void OnUiTimerTick(object? state)
        {
            if (!IsRunning) return;

            // 1. Atualizar o Jogo Ativo baseado na janela em Foreground
            IntPtr fgWindow = GetForegroundWindow();
            if (fgWindow != IntPtr.Zero)
            {
                GetWindowThreadProcessId(fgWindow, out uint fgPid);
                int currentForegroundPid = (int)fgPid;

                // Se o processo em foreground tem um tracker ativo e está gerando frames recentemente
                if (_processTrackers.TryGetValue(currentForegroundPid, out var fgTracker) && fgTracker.RecentFrameCount >= 5)
                {
                    if (_activeGamePid != currentForegroundPid)
                    {
                        try
                        {
                            var process = Process.GetProcessById(currentForegroundPid);
                            string processName = process.ProcessName;
                            
                            // Apenas aceita se não for processo de sistema
                            if (!SystemProcessBlacklist.Contains(processName))
                            {
                                _activeGameName = processName;
                                _activeGamePid = currentForegroundPid;
                                _logger.LogInfo($"[EtwFrameTimeMonitor] ✅ Jogo em Foreground Detectado: {_activeGameName} (PID: {_activeGamePid})");
                            }
                        }
                        catch
                        {
                            // Processo pode ter acabado de fechar ou ser inacessível
                        }
                    }
                }
            }

            // CORREÇÃO: Se não há jogo ativo, garantir que cache seja zerado
            if (_activeGamePid == 0)
            {
                SystemMetricsCache.Instance.UpdateFps(0, 0, 0, false);
                return;
            }

            // Cleanup trackings antigos para liberar memória
            CleanupInactiveTrackers();

            if (_processTrackers.TryGetValue(_activeGamePid, out var tracker))
            {
                var metrics = tracker.CalculateMetrics();
                metrics.DetectedGameProcessId = _activeGamePid;
                metrics.DetectedGameName = _activeGameName;

                // Escrever no SystemMetricsCache (Single Source of Truth)
                SystemMetricsCache.Instance.UpdateFps(
                    metrics.CurrentFps,
                    metrics.OnePercentLowFps,
                    metrics.AverageFrametimeMs,
                    metrics.IsStuttering
                );

                if (metrics.CurrentFps > 0)
                {
                    MetricsUpdated?.Invoke(this, metrics);
                }
            }
            else
            {
                // CORREÇÃO: Tracker não existe mais - zerar jogo ativo e limpar cache
                if (_activeGamePid != 0)
                {
                    _logger.LogInfo($"[EtwFrameTimeMonitor] Tracker de PID {_activeGamePid} ({_activeGameName}) removido. Zerando jogo ativo.");
                    _activeGamePid = 0;
                    _activeGameName = string.Empty;
                }
                
                // Garantir que cache reflete FPS indisponível
                SystemMetricsCache.Instance.UpdateFps(0, 0, 0, false);
            }

            // Backup: calcular métricas para TODOS os jogos em segundo plano
            // (mantém dados prontos para troca instantânea de jogo ativo)
            foreach (var kvp in _processTrackers)
            {
                if (kvp.Key == _activeGamePid) continue;
                if (kvp.Value.RecentFrameCount >= 10)
                {
                    kvp.Value.CalculateMetrics(); // mantém cache interno aquecido
                }
            }
        }

        private void CleanupInactiveTrackers()
        {
            var now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency * 1000.0;
            foreach (var kvp in _processTrackers)
            {
                if (now - kvp.Value.LastActivityMs > 5000 && kvp.Key != _activeGamePid)
                {
                    _processTrackers.TryRemove(kvp.Key, out _);
                }
            }
        }

        public void Stop()
        {
            if (!IsRunning) return;
            IsRunning = false;

            _cts?.Cancel();
            _uiTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _uiTimer?.Dispose();

            if (_session != null)
            {
                _logger.LogInfo("[EtwFrameTimeMonitor] Finalizando sessão ETW...");
                _session.Source.StopProcessing();
                _session.Dispose();
                _session = null;
            }

            try { _etwTask?.Wait(2000); } catch (Exception exEtW) { _logger?.LogWarning($"[EtwFrameTimeMonitor] Erro ao aguardar task ETW: {exEtW.Message}"); }
            _processTrackers.Clear();
            _logger.LogSuccess("[EtwFrameTimeMonitor] Parado com sucesso.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            Stop();
            _cts?.Dispose();
            _disposed = true;
        }
    }

    internal class ProcessFrameTracker
    {
        public int Pid { get; }
        public string ActiveApi { get; set; } = string.Empty;
        public double LastDxgiFrameMs { get; set; } = 0;
        private readonly List<double> _frameTimestampsMs = new();
        private readonly object _lock = new();
        public double LastActivityMs { get; private set; }
        public int RecentFrameCount => _frameTimestampsMs.Count;
        
        private double _maxTimestamp = 0;

        public ProcessFrameTracker(int pid)
        {
            Pid = pid;
        }

        public void AddFrame(double timestampMs)
        {
            lock (_lock)
            {
                // Apenas armazena os timestamps. A deduplicação real ocorrerá na 
                // fase de cálculo após a ordenação, garantindo que eventos ETW 
                // fora de ordem sejam tratados corretamente.
                if (timestampMs > _maxTimestamp) _maxTimestamp = timestampMs;
                
                _frameTimestampsMs.Add(timestampMs);
                LastActivityMs = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency * 1000.0;
                
                // Prune inicial apenas para não deixar a lista crescer infinitamente
                _frameTimestampsMs.RemoveAll(t => _maxTimestamp - t > 1500.0);
            }
        }

        public FrameMetrics CalculateMetrics()
        {
            lock (_lock)
            {
                var currentActivityMs = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency * 1000.0;
                var inactiveTime = currentActivityMs - LastActivityMs;
                
                // Se o jogo parou de renderizar por mais de 1 segundo, zera o FPS.
                if (inactiveTime > 1000.0)
                {
                    _frameTimestampsMs.Clear();
                    return new FrameMetrics { CurrentFps = 0 };
                }

                if (_frameTimestampsMs.Count < 2)
                    return new FrameMetrics { CurrentFps = 0 };

                // 1. Clonar e ordenar para resolver ETWs fora de ordem
                var timestamps = _frameTimestampsMs.ToList();
                timestamps.Sort();

                // 2. Prune exato para a janela de 1000ms a partir do frame mais recente
                double latestTimestamp = timestamps.Last();
                timestamps.RemoveAll(t => latestTimestamp - t > 1000.0);

                if (timestamps.Count < 2) 
                    return new FrameMetrics { CurrentFps = 0 };

                // 3. Deduplicação Profissional (Similar ao PresentMon)
                // Se múltiplos eventos (Start/Stop/Info ou Present/Flip) ocorrerem para o 
                // mesmo frame, eles estarão muito próximos (< 0.5ms). Removemos os duplicados.
                var cleanTimestamps = new List<double>(timestamps.Count);
                cleanTimestamps.Add(timestamps[0]);
                for (int i = 1; i < timestamps.Count; i++)
                {
                    if (timestamps[i] - cleanTimestamps.Last() >= 0.5)
                    {
                        cleanTimestamps.Add(timestamps[i]);
                    }
                }

                if (cleanTimestamps.Count < 2) 
                    return new FrameMetrics { CurrentFps = 0 };

                double currentFps = cleanTimestamps.Count;
                
                List<double> frameTimes = new List<double>(cleanTimestamps.Count - 1);
                for (int i = 1; i < cleanTimestamps.Count; i++)
                {
                    frameTimes.Add(cleanTimestamps[i] - cleanTimestamps[i - 1]);
                }

                frameTimes.Sort();
                double averageFrametime = frameTimes.Average();
                
                // 1% Low (pega os maiores tempos de quadro - fim da lista)
                int onePercentIndex = Math.Max(0, (int)(frameTimes.Count * 0.99));
                double onePercentWorstFrametime = frameTimes[onePercentIndex];
                double onePercentLowFps = 1000.0 / Math.Max(onePercentWorstFrametime, 0.1);

                // Detecção de Stutter: Se o 1% low for 2x mais lento que a média
                bool isStuttering = averageFrametime > 0 && (onePercentWorstFrametime / averageFrametime) > 2.0;

                return new FrameMetrics
                {
                    CurrentFps = Math.Round(currentFps, 1),
                    AverageFrametimeMs = Math.Round(averageFrametime, 2),
                    OnePercentLowFps = Math.Round(onePercentLowFps, 1),
                    IsStuttering = isStuttering
                };
            }
        }
    }
}
