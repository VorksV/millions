using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Vortice.DXGI;
using Vortice.Direct3D11;
using Vortice.Direct3D;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Monitoring
{
    public class DesktopDuplicationFPSMonitor : IDisposable
    {
        private readonly ILoggingService _logger;
        private DispatcherTimer? _uiTimer;
        private CancellationTokenSource? _cts;
        private Task? _captureTask;
        private bool _disposed;
        private double _currentFps;
        private readonly object _lock = new();
        private long _lastFrameTicks;
        private double _smoothedFrameTimeMs;

        public event EventHandler<double>? FpsUpdated;
        public double CurrentFPS
        {
            get { lock (_lock) return _currentFps; }
        }
        public bool IsRunning { get; private set; }

        public DesktopDuplicationFPSMonitor(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task StartAsync()
        {
            if (IsRunning) return Task.CompletedTask;
            _cts = new CancellationTokenSource();
            IsRunning = true;

            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _uiTimer.Tick += OnUiTimerTick;
            _uiTimer.Start();

            _captureTask = Task.Run(async () => await RunCaptureLoopAsync(_cts.Token), _cts.Token);
            _logger.LogInfo("[DesktopDuplicationFPSMonitor] Iniciado.");
            return Task.CompletedTask;
        }

        private async Task RunCaptureLoopAsync(CancellationToken ct)
        {
            try
            {
                var factoryResult = DXGI.CreateDXGIFactory1<IDXGIFactory1>(out var factory);
                if (factoryResult.Failure || factory == null)
                {
                    _logger.LogError($"[DesktopDuplicationFPSMonitor] CreateDXGIFactory1 falhou: 0x{factoryResult.Code:X8}");
                    return;
                }
                using var _factory = factory;

                factory.EnumAdapters1(0, out var adapter);
                using var _adapter = adapter;

                var deviceResult = D3D11.D3D11CreateDevice(
                    adapter,
                    DriverType.Unknown,
                    DeviceCreationFlags.BgraSupport,
                    new[] { FeatureLevel.Level_11_0 },
                    out var device);

                if (deviceResult.Failure || device == null)
                {
                    _logger.LogError($"[DesktopDuplicationFPSMonitor] D3D11CreateDevice falhou: 0x{deviceResult.Code:X8}");
                    return;
                }
                using var _device = device;

                adapter.EnumOutputs(0, out var output);
                using var _output = output;

                var output1 = output.QueryInterface<IDXGIOutput1>();
                using var _output1 = output1;

                _logger.LogSuccess("[DesktopDuplicationFPSMonitor] Dispositivo D3D11 criado. Iniciando duplicação...");

                var duplication = output1.DuplicateOutput(device);
                using var _duplication = duplication;

                _logger.LogSuccess("[DesktopDuplicationFPSMonitor] Desktop Duplication iniciado com sucesso!");

                int frameCount = 0;
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                long lastFpsUpdate = 0;
                int consecutiveFailures = 0;
                bool hasCapturedAnyFrame = false;
                long loopIterations = 0;

                while (!ct.IsCancellationRequested)
                {
                    loopIterations++;
                    try
                    {
                        var acquireResult = duplication.AcquireNextFrame(500, out var frameInfo, out var desktopResource);

                        if (acquireResult.Success && desktopResource != null)
                        {
                            if (!hasCapturedAnyFrame)
                            {
                                hasCapturedAnyFrame = true;
                                _logger.LogSuccess($"[DesktopDuplicationFPSMonitor] PRIMEIRO FRAME CAPTURADO! loopIteration={loopIterations}");
                            }

                            frameCount++;
                            consecutiveFailures = 0;

                            long now = stopwatch.ElapsedMilliseconds;
                            long delta = now - lastFpsUpdate;

                            if (delta >= 1000)
                            {
                                double fps = frameCount * 1000.0 / delta;
                                _logger.LogInfo($"[DesktopDuplicationFPSMonitor] FPS calculado={fps:F1} frames={frameCount} delta={delta}ms");
                                lock (_lock)
                                {
                                    _currentFps = fps;
                                }
                                frameCount = 0;
                                lastFpsUpdate = now;
                            }

                            duplication.ReleaseFrame();
                            desktopResource.Dispose();
                        }
                        else if ((uint)acquireResult.Code == 0x887A0027)
                        {
                            if (loopIterations == 1)
                                _logger.LogInfo("[DesktopDuplicationFPSMonitor] Aguardando primeiro frame (timeout é normal)...");
                        }
                        else
                        {
                            consecutiveFailures++;
                            if (consecutiveFailures % 50 == 0)
                            {
                                _logger.LogWarning($"[DesktopDuplicationFPSMonitor] Falhas consecutivas: {consecutiveFailures} | Code: 0x{acquireResult.Code:X8}");
                            }
                        }
                    }
                    catch (Exception ex) when ((uint)ex.HResult == 0x887A0027)
                    {
                        if (loopIterations == 1)
                            _logger.LogInfo("[DesktopDuplicationFPSMonitor] Timeout na primeira iteração (normal sem jogo ativo)");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"[DesktopDuplicationFPSMonitor] Erro no frame: {ex.Message}");
                        await Task.Delay(100).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[DesktopDuplicationFPSMonitor] Erro fatal: {ex.Message}");
            }
            finally
            {
                IsRunning = false;
            }
        }

        private void OnUiTimerTick(object? sender, EventArgs e)
        {
            double fps;
            lock (_lock)
            {
                fps = Math.Round(_currentFps, 1);
                if (fps < 0) fps = 0;
            }
            if (fps > 0)
                _logger.Log(LogLevel.Debug, LogCategory.General, $"[DesktopDuplicationFPSMonitor] UI Timer: disparando FpsUpdated com fps={fps:F1}");
            FpsUpdated?.Invoke(this, fps);
        }

        public async Task StopAsync()
        {
            IsRunning = false;
            _cts?.Cancel();
            _uiTimer?.Stop();
            try
            {
                if (_captureTask != null)
                    await _captureTask.WaitAsync(TimeSpan.FromSeconds(1));
            }
            catch (Exception ex) { _logger?.LogWarning($"[DesktopDuplicationFPSMonitor] Erro ao parar capture task: {ex.Message}"); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _ = StopAsync();
            _cts?.Dispose();
            _disposed = true;
        }
    }
}
