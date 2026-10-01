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
    /// CAPTURA DE FPS REAL-TIME
    /// Utiliza telemetria Kernel ETW (Event Tracing for Windows) do DxgKrnl/DXGI.
    /// Seguro e sem falhas de heurística de antivírus.
    /// </summary>
    [Obsolete("Redundante — use EtwFrameTimeMonitor + SystemMetricsCache (Fps, FpsOnePercentLow, FpsAverageFrametimeMs)")]
    public class RealTimeFpsCapture : IDisposable
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

        public event EventHandler<FpsUpdatedEventArgs>? FpsUpdated;
        
        public RealTimeFpsCapture(ILoggingService? logger = null)
        {
            _logger?.LogEntry(nameof(RealTimeFpsCapture));
            _logger = logger;
            _updateTimer = new Timer(EmitMetrics, null, Timeout.Infinite, Timeout.Infinite);
            _logger?.LogExit(nameof(RealTimeFpsCapture));
        }
        
        public async Task<bool> StartAsync()
        {
            _logger?.LogEntry(nameof(StartAsync));
            try
            {
                _logger?.LogInfo("[FPS-CAPTURE] 🎯 Iniciando captura ETW DIRECTX (Real-Time)...");

                lock (_lock)
                {
                    if (_disposed) return false;
                    
                    _currentFps = 0;
                    _averageFps = 0;
                    _totalFrames = 0;
                    _framesInLastSecond = 0;
                    _fpsHistory.Clear();
                }
                
                _cts = new CancellationTokenSource();
                _etwTask = Task.Run(() => SubscribeEtwEvents(_cts.Token), _cts.Token);
                
                // Emite os recálculos a cada segundo para precisão FPS autêntica
                _updateTimer.Change(1000, 1000);
                
                _logger?.LogSuccess("[FPS-CAPTURE] ✅ Thread Kernel ETW Alocada! FPS Real habilitado.");
                _logger?.LogExit(nameof(StartAsync), true);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[FPS-CAPTURE] ❌ Erro Crítico: {ex.Message}", ex);
                _logger?.LogExit(nameof(StartAsync), false);
                return false;
            }
        }
        
        private void SubscribeEtwEvents(CancellationToken ct)
        {
            _logger?.LogEntry(nameof(SubscribeEtwEvents));
            try
            {
                _logger?.LogInfo("[FPS-CAPTURE] Iniciando sessão ETW para captura de frames DXGI/D3D9/DxgKrnl...");

                var sessionName = "Voltris_FpsCapture_" + Process.GetCurrentProcess().Id;
                var activeSessions = TraceEventSession.GetActiveSessionNames();
                if (activeSessions.Contains(sessionName))
                {
                    using (var oldSession = new TraceEventSession(sessionName))
                    {
                        oldSession.Stop();
                        _logger?.LogInfo("[FPS-CAPTURE] Sessão ETW anterior fechada.");
                    }
                }

                using (_session = new TraceEventSession(sessionName))
                {
                    var dxgiGuid = TraceEventProviders.GetEventSourceGuidFromName("Microsoft-Windows-DXGI");
                    var dxgkrnlGuid = TraceEventProviders.GetEventSourceGuidFromName("Microsoft-Windows-DxgKrnl");
                    var d3d9Guid = TraceEventProviders.GetEventSourceGuidFromName("Microsoft-Windows-D3D9");

                    if (dxgiGuid != Guid.Empty)
                    {
                        _session.EnableProvider(dxgiGuid);
                        _logger?.LogInfo("[FPS-CAPTURE] Provider DXGI habilitado.");
                    }
                    if (dxgkrnlGuid != Guid.Empty)
                    {
                        _session.EnableProvider(dxgkrnlGuid);
                        _logger?.LogInfo("[FPS-CAPTURE] Provider DxgKrnl habilitado.");
                    }
                    if (d3d9Guid != Guid.Empty)
                    {
                        _session.EnableProvider(d3d9Guid);
                        _logger?.LogInfo("[FPS-CAPTURE] Provider D3D9 habilitado.");
                    }

                    _session.Source.Dynamic.All += (TraceEvent data) =>
                    {
                        if (ct.IsCancellationRequested) return;

                        try
                        {
                            // DXGIPresent_Start / Present_Start / Flip_Info / PresentHistory
                            if (data.EventName.Contains("Present_Start") || 
                                data.EventName.Contains("Flip") || 
                                data.EventName.Contains("PresentHistory") ||
                                data.EventName.Contains("VSync"))
                            {
                                if (data.ProcessID > 0)
                                {
                                    Interlocked.Increment(ref _totalFrames);
                                    Interlocked.Increment(ref _framesInLastSecond);
                                }
                            }
                        }
                        catch
                        {
                            // Ignorar eventos malformados
                        }
                    };

                    _logger?.LogSuccess("[FPS-CAPTURE] Sessão ETW iniciada. Processando eventos de frame...");
                    _session.Source.Process(); // Bloqueante até Stop() ser chamado
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[FPS-CAPTURE] Erro na captura ETW: {ex.Message}. Usando fallback DWM.");
                // Fallback: sem ETW, o display usará o CompositionTarget.Rendering do overlay window
            }
            _logger?.LogExit(nameof(SubscribeEtwEvents));
        }

        private void EmitMetrics(object? state)
        {
            _logger?.LogEntry(nameof(EmitMetrics));
            try
            {
                if (_disposed) return;
                
                int framesProcessed = Interlocked.Exchange(ref _framesInLastSecond, 0);
                
                lock (_lock)
                {
                    if (framesProcessed > 0 && framesProcessed < 4000)
                    {
                        _currentFps = framesProcessed;
                    }
                    else if (framesProcessed == 0)
                    {
                        // Suavização simples para quedas repentinas 
                        _currentFps = _currentFps * 0.5;
                        if (_currentFps < 1) _currentFps = 0;
                    }

                    if (_currentFps > 0)
                    {
                        _fpsHistory.Enqueue(_currentFps);
                        if (_fpsHistory.Count > 10) _fpsHistory.Dequeue();
                        _averageFps = _fpsHistory.Average();
                    }
                    else
                    {
                        _averageFps = 0; 
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
                _logger?.LogError($"[FPS-CAPTURE] Erro na emissão FPS: {ex.Message}", ex);
            }
            _logger?.LogExit(nameof(EmitMetrics));
        }
        
        public void Stop()
        {
            _logger?.LogEntry(nameof(Stop));
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
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[FPS-CAPTURE] Erro Stop: {ex.Message}", ex);
            }
            _logger?.LogExit(nameof(Stop));
        }
        
        public (double currentFps, double averageFps, long totalFrames) GetMetrics()
        {
            _logger?.LogEntry(nameof(GetMetrics));
            lock (_lock)
            {
                _logger?.LogExit(nameof(GetMetrics));
                return (_currentFps, _averageFps, _totalFrames);
            }
        }
        
        public void Dispose()
        {
            _logger?.LogEntry(nameof(Dispose));
            if (_disposed) { _logger?.LogExit(nameof(Dispose)); return; }
            Stop();
            _updateTimer?.Dispose();
            _disposed = true;
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
