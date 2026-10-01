using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Diagnostics.Tracing.Parsers;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Optimization.Telemetry
{
    /// <summary>
    /// Frame-Time Telemetry.
    /// Captura DXGI Present() de forma não intrusiva via ETW.
    /// Sem injeção de DLLs (não causa bans em AntiCheats).
    /// </summary>
    public class DxgiFrameTimeMonitor : IDisposable
    {
        private readonly ILoggingService _logger;
        private TraceEventSession? _session;
        private Task? _monitorTask;
        private CancellationTokenSource? _cts;

        // Metada Frame Pacing (PID -> Ticks passados)
        private readonly ConcurrentDictionary<int, long> _lastPresentTick = new();

        // 1000 ultimos frames de historico (PID -> Média de Frame Time em ms)
        public ConcurrentDictionary<int, double> AverageFrameTime { get; } = new();

        public DxgiFrameTimeMonitor(ILoggingService logger)
        {
            _logger = logger;
        }

        public void StartMonitoring()
        {
            if (_session != null) return;
            _cts = new CancellationTokenSource();
            _monitorTask = Task.Run(() => RunEtwSession(_cts.Token), _cts.Token);
            _logger.LogInfo("[DxgiFrameTimeMonitor] Telemetria de Frame-Pacing DXGI Iniciada.");
        }

        private void RunEtwSession(CancellationToken ct)
        {
            try
            {
                string sessionName = "VoltrisDXGITelemetry";
                var activeSessions = TraceEventSession.GetActiveSessionNames();
                if (activeSessions.Contains(sessionName))
                {
                    using (var oldSession = new TraceEventSession(sessionName))
                    {
                        oldSession.Stop();
                    }
                }

                using (_session = new TraceEventSession(sessionName))
                {
                    // GUID Nativo do Windows DXGI Provider
                    var dxgiGuid = TraceEventProviders.GetEventSourceGuidFromName("Microsoft-Windows-DXGI");

                    if (dxgiGuid == Guid.Empty)
                    {
                        _logger.LogWarning("[DxgiFrameTimeMonitor] Provider DXGI não localizado na máquina atual.");
                        return;
                    }

                    _session.EnableProvider(dxgiGuid);

                    _session.Source.Dynamic.All += delegate (TraceEvent data)
                    {
                        if (ct.IsCancellationRequested) return;

                        // EventName: DXGIPresent_Start (ocorre a cada frame gerado e enviado pra VSYNC/FlipQueue).
                        if (data.EventName.StartsWith("DXGIPresent", StringComparison.OrdinalIgnoreCase))
                        {
                            int pid = data.ProcessID;
                            if (pid > 0)
                            {
                                long nowTicks = data.TimeStamp.Ticks;

                                if (_lastPresentTick.TryGetValue(pid, out long lastTicks))
                                {
                                    double frameTimeMs = TimeSpan.FromTicks(nowTicks - lastTicks).TotalMilliseconds;
                                    
                                    // Média Móvel Exponencial simples
                                    AverageFrameTime.AddOrUpdate(pid, frameTimeMs, (_, old) => (old * 0.9) + (frameTimeMs * 0.1));
                                }

                                _lastPresentTick[pid] = nowTicks;
                            }
                        }
                    };

                    _session.Source.Process();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[DxgiFrameTimeMonitor] Falha de leitura DXGI: {ex.Message}");
            }
        }

        public void StopMonitoring()
        {
            _cts?.Cancel();
            _session?.Stop();
            _session?.Dispose();
            _session = null;
        }

        public void Dispose()
        {
            StopMonitoring();
        }
    }
}
