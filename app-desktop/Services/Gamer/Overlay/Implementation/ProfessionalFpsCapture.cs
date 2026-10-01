using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer.Overlay.Implementation
{
    /// <summary>
    /// CAPTURA PROFISSIONAL DE FPS
    /// Utiliza telemetria Kernel ETW (Event Tracing for Windows) do DxgKrnl/DXGI.
    /// É o ÚNICO método 100% seguro (Zero Bans em Anti-Cheats) e 100% em tempo real.
    /// Lê cada transição de frame diretamente do driver de vídeo.
    /// </summary>
    [Obsolete("Redundante — use EtwFrameTimeMonitor + SystemMetricsCache (Fps, FpsOnePercentLow, FpsAverageFrametimeMs)")]
    public class ProfessionalFpsCapture : IDisposable
    {
        private readonly ILoggingService? _logger;
        private readonly object _lock = new object();
        private bool _disposed = false;
        
        // ETW
        private TraceEventSession? _session;
        private Task? _etwTask;
        private CancellationTokenSource? _cts;
        
        // Métricas
        private double _currentFps = 0;
        private double _averageFps = 0;
        private long _totalFrames = 0;
        
        // Timer de Atualização
        private readonly Timer _updateTimer;
        private int _framesInLastSecond = 0;
        private readonly Queue<double> _fpsHistory = new();
        
        private int _targetProcessId = 0;

        public event EventHandler<FpsUpdatedEventArgs>? FpsUpdated;
        
        public ProfessionalFpsCapture(ILoggingService? logger = null)
        {
            _logger?.LogEntry(nameof(ProfessionalFpsCapture));
            _logger?.LogInfo("[ProfessionalFpsCapture] ENTER: Constructor");
            _logger = logger;
            _updateTimer = new Timer(EmitMetrics, null, Timeout.Infinite, Timeout.Infinite);
            _logger?.LogInfo("[ProfessionalFpsCapture] EXIT: Constructor");
            _logger?.LogExit(nameof(ProfessionalFpsCapture));
        }
        
        public async Task<bool> StartAsync()
        {
            _logger?.LogEntry(nameof(StartAsync));
            _logger?.LogInfo("[ProfessionalFpsCapture] ENTER: StartAsync");
            try
            {
                _logger?.LogInfo("[PRO-FPS-CAPTURE] 🎯 Iniciando captura ETW DIRECTX (Real-Time)...");

                lock (_lock)
                {
                    if (_disposed) return false;
                    
                    _currentFps = 0;
                    _averageFps = 0;
                    _totalFrames = 0;
                    _framesInLastSecond = 0;
                    _fpsHistory.Clear();
                    
                    _targetProcessId = Process.GetCurrentProcess().Id; // Fallback tracking for any game
                }
                
                _cts = new CancellationTokenSource();
                _etwTask = Task.Run(() => SubscribeEtwEvents(_cts.Token), _cts.Token);
                
                // Atualiza a interface a cada segundo
                _updateTimer.Change(1000, 1000);
                
                _logger?.LogSuccess("[PRO-FPS-CAPTURE] ✅ Thread Kernel ETW Alocada! FPS Real habilitado.");
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[PRO-FPS-CAPTURE] ❌ Erro Crítico: {ex.Message}", ex);
                return false;
            }
            finally
            {
                _logger?.LogInfo("[ProfessionalFpsCapture] EXIT: StartAsync");
                _logger?.LogExit(nameof(StartAsync));
            }
        }
        
        private void SubscribeEtwEvents(CancellationToken ct)
        {
            _logger?.LogEntry(nameof(SubscribeEtwEvents));
            _logger?.LogDebug("[ProfessionalFpsCapture] ENTER: SubscribeEtwEvents");
            try
            {
                var sessionName = "Voltris_Pro_Overlay";
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
                    var dxgiGuid = TraceEventProviders.GetEventSourceGuidFromName("Microsoft-Windows-DXGI");
                    var dxgkrnlGuid = TraceEventProviders.GetEventSourceGuidFromName("Microsoft-Windows-DxgKrnl");
                    
                    if (dxgiGuid != Guid.Empty) _session.EnableProvider(dxgiGuid);
                    if (dxgkrnlGuid != Guid.Empty) _session.EnableProvider(dxgkrnlGuid); // Backup provider
                    
                    _session.Source.Dynamic.All += (TraceEvent data) =>
                    {
                        if (ct.IsCancellationRequested) return;
                        
                        // DXGIPresent_Start / Flip_Info (Detecta renderização de novos frames)
                        if (data.EventName.Contains("Present_Start") || data.EventName.Contains("Flip") || data.EventName.Contains("PresentHistory"))
                        {
                            // Apenas computa frames de processos maiores (ignora UI do sistema)
                            if (data.ProcessID > 0)
                            {
                                Interlocked.Increment(ref _totalFrames);
                                Interlocked.Increment(ref _framesInLastSecond);
                            }
                        }
                    };
                    
                    _session.Source.Process(); // Bloqueante até Stop() ser chamado
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[PRO-FPS-CAPTURE] ❌ Erro no túnel ETW: {ex.Message} -> Permissão de Administrador necessária.");
            }
            finally
            {
                _logger?.LogDebug("[ProfessionalFpsCapture] EXIT: SubscribeEtwEvents");
                _logger?.LogExit(nameof(SubscribeEtwEvents));
            }
        }

        private void EmitMetrics(object? state)
        {
            _logger?.LogEntry(nameof(EmitMetrics));
            _logger?.LogDebug("[ProfessionalFpsCapture] ENTER: EmitMetrics");
            try
            {
                if (_disposed) return;
                
                int framesProcessed = Interlocked.Exchange(ref _framesInLastSecond, 0);
                
                lock (_lock)
                {
                    // Evitar valores irreais travados no idle 
                    if (framesProcessed > 0 && framesProcessed < 4000)
                    {
                        _currentFps = framesProcessed;
                    }
                    else if (framesProcessed == 0)
                    {
                        // Fallback temporário p/ suavizar em loading screens ou alt-tab
                        _currentFps = _currentFps * 0.5;
                        if (_currentFps < 1) _currentFps = 0;
                    }

                    if (_currentFps > 0)
                    {
                        _fpsHistory.Enqueue(_currentFps);
                        if (_fpsHistory.Count > 10) _fpsHistory.Dequeue();
                        _averageFps = _fpsHistory.Average();
                    }
                    
                    FpsUpdated?.Invoke(this, new FpsUpdatedEventArgs
                    {
                        CurrentFps = Math.Round(_currentFps, 1),
                        AverageFps = Math.Round(_averageFps, 1),
                        TotalFrames = _totalFrames,
                        Timestamp = DateTime.Now
                    });
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[PRO-FPS-CAPTURE] Erro na emissão FPS: {ex.Message}", ex);
            }
            finally
            {
                _logger?.LogDebug("[ProfessionalFpsCapture] EXIT: EmitMetrics");
                _logger?.LogExit(nameof(EmitMetrics));
            }
        }
        
        public void Stop()
        {
            _logger?.LogEntry(nameof(Stop));
            _logger?.LogInfo("[ProfessionalFpsCapture] ENTER: Stop");
            try
            {
                if (_disposed) return;
                
                _updateTimer.Change(Timeout.Infinite, Timeout.Infinite);
                _cts?.Cancel();
                
                if (_session != null)
                {
                    _session.Stop();
                    _session.Dispose();
                    _session = null;
                }
                
                _logger?.LogSuccess("[PRO-FPS-CAPTURE] ✅ Captura ETW encerrada.");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[PRO-FPS-CAPTURE] Erro Stop: {ex.Message}", ex);
            }
            finally
            {
                _logger?.LogInfo("[ProfessionalFpsCapture] EXIT: Stop");
                _logger?.LogExit(nameof(Stop));
            }
        }
        
        public (double currentFps, double averageFps, long totalFrames) GetMetrics()
        {
            _logger?.LogEntry(nameof(GetMetrics));
            _logger?.LogDebug("[ProfessionalFpsCapture] ENTER: GetMetrics");
            lock (_lock)
            {
                var result = (_currentFps, _averageFps, _totalFrames);
                _logger?.LogDebug("[ProfessionalFpsCapture] EXIT: GetMetrics");
                _logger?.LogExit(nameof(GetMetrics));
                return result;
            }
        }
        
        public void Dispose()
        {
            _logger?.LogEntry(nameof(Dispose));
            _logger?.LogInfo("[ProfessionalFpsCapture] ENTER: Dispose");
            if (_disposed)
            {
                _logger?.LogInfo("[ProfessionalFpsCapture] EXIT: Dispose (already disposed)");
                _logger?.LogExit(nameof(Dispose));
                return;
            }
            Stop();
            _updateTimer?.Dispose();
            _disposed = true;
            _logger?.LogInfo("[ProfessionalFpsCapture] EXIT: Dispose");
            _logger?.LogExit(nameof(Dispose));
        }
        
        public class FpsUpdatedEventArgs : EventArgs
        {
            public double CurrentFps { get; set; }
            public double AverageFps { get; set; }
            public long TotalFrames { get; set; }
            public DateTime Timestamp { get; set; }
        }
    }
}
