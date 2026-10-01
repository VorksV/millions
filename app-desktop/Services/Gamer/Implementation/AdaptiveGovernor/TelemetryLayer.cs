using VoltrisOptimizer.Utils;
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Monitoring.Interfaces;
using VoltrisOptimizer.Services.Gamer.GamerModeManager;
using VoltrisOptimizer.Services.Thermal;
using VoltrisOptimizer.Services.Gamer.Overlay.Implementation;

namespace VoltrisOptimizer.Services.Gamer.Implementation.AdaptiveGovernor
{
    /// <summary>
    /// Camada de telemetria para coleta de métricas reais do sistema
    /// SOMENTE LEITURA - não modifica nada
    /// </summary>
    internal class TelemetryLayer : IDisposable
    {
        private readonly ILoggingService _logger;
        private readonly Process? _gameProcess;
        private readonly TelemetryService? _telemetryService;
        private readonly IEtwFrameTimeMonitor? _fpsMonitor;
        private FrameMetrics? _latestFpsMetrics;
        private readonly IThermalMonitorService? _thermalMonitor;
        
        // NativeSystemMetrics para CPU/RAM (substitui PerformanceCounters pesados)
        private readonly VoltrisOptimizer.Helpers.NativeSystemMetrics _nativeMetrics = new();
        
        // Cache de frequência da CPU para evitar WMI lento no hot path
        private double _cachedCpuFreqCurrent;
        private double _cachedCpuFreqMax;
        private DateTime _lastFreqCheck = DateTime.MinValue;
        private readonly TimeSpan _freqCheckInterval = TimeSpan.FromSeconds(5);
        
        private bool _disposed = false;
        
        public TelemetryLayer(
            ILoggingService logger,
            Process? gameProcess,
            TelemetryService? telemetryService,
            IEtwFrameTimeMonitor? fpsMonitor,
            IThermalMonitorService? thermalMonitor)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _gameProcess = gameProcess;
            _telemetryService = telemetryService;
            _fpsMonitor = fpsMonitor;
            if (_fpsMonitor != null)
                _fpsMonitor.MetricsUpdated += OnFpsMetricsUpdated;
            _thermalMonitor = thermalMonitor;
            
            _logger.LogEntry(nameof(TelemetryLayer));
            InitializeCounters();
            _logger.LogExit(nameof(TelemetryLayer));
        }

        private void InitializeCounters()
        {
            _logger.LogEntry(nameof(InitializeCounters));
            try
            {
                // CPU/RAM: NativeSystemMetrics (warm-up)
                _nativeMetrics.GetCpuUsage();
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[TelemetryLayer] Erro ao inicializar contadores: {ex.Message}");
            }
            _logger.LogExit(nameof(InitializeCounters));
        }
        
        /// <summary>
        /// Coleta todas as métricas do sistema em tempo real
        /// </summary>
        public async Task<AdaptiveSystemMetrics> CollectMetricsAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogEntry(nameof(CollectMetricsAsync));
            var metrics = new AdaptiveSystemMetrics
            {
                Timestamp = DateTime.UtcNow
            };
            
            try
            {
                // Coletar métricas em paralelo quando possível
                var cpuTask = Task.Run(() => CollectCpuMetrics(metrics), cancellationToken);
                var ramTask = Task.Run(() => CollectRamMetrics(metrics), cancellationToken);
                var gpuTask = CollectGpuMetricsAsync(metrics, cancellationToken);
                var fpsTask = CollectFpsMetricsAsync(metrics, cancellationToken);
                var processTask = CollectProcessMetricsAsync(metrics, cancellationToken);
                
                await Task.WhenAll(cpuTask, ramTask, gpuTask, fpsTask, processTask);
            }
            catch (OperationCanceledException)
            {
                // Cancelamento normal durante shutdown — não é erro
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[TelemetryLayer] Erro ao coletar métricas: {ex.Message}");
            }
            
            _logger.LogExit(nameof(CollectMetricsAsync));
            return metrics;
        }
        
        private void CollectCpuMetrics(AdaptiveSystemMetrics metrics)
        {
            _logger.LogEntry(nameof(CollectCpuMetrics));
            try
            {
                // CPU Usage Total (NativeSystemMetrics - GetSystemTimes)
                metrics.CpuUsagePercent = _nativeMetrics.GetCpuUsage();
                
                // CPU Clock via WMI - OTIMIZADO: Apenas a cada 5 segundos
                // WMI é extremamente lento e causa stutter se chamado no hot path
                if (DateTime.UtcNow - _lastFreqCheck > _freqCheckInterval)
                {
                    try
                    {
                        using var searcher = new System.Management.ManagementObjectSearcher(
                            "SELECT CurrentClockSpeed, MaxClockSpeed FROM Win32_Processor");
                        foreach (System.Management.ManagementObject obj in searcher.Get())
                        {
                            var current = obj["CurrentClockSpeed"];
                            var max = obj["MaxClockSpeed"];
                            if (current != null) _cachedCpuFreqCurrent = Convert.ToDouble(current);
                            if (max != null) _cachedCpuFreqMax = Convert.ToDouble(max);
                            break;
                        }
                        _lastFreqCheck = DateTime.UtcNow;
                    }
                    catch { }
                }
                
                metrics.CpuFreqCurrentMhz = _cachedCpuFreqCurrent > 0 ? _cachedCpuFreqCurrent : 0;
                metrics.CpuFreqMaxMhz = _cachedCpuFreqMax;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[TelemetryLayer] Erro ao coletar métricas de CPU: {ex.Message}");
            }
            _logger.LogExit(nameof(CollectCpuMetrics));
        }
        
        private void CollectRamMetrics(AdaptiveSystemMetrics metrics)
        {
            _logger.LogEntry(nameof(CollectRamMetrics));
            try
            {
                var memInfo = _nativeMetrics.GetMemoryUsage();
                metrics.RamUsagePercent = memInfo.UsagePercent;
                metrics.RamUsedGb = memInfo.UsedGb;
                metrics.RamTotalGb = memInfo.TotalGb;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[TelemetryLayer] Erro ao coletar métricas de RAM: {ex.Message}");
            }
            _logger.LogExit(nameof(CollectRamMetrics));
        }
        
        private async Task CollectGpuMetricsAsync(AdaptiveSystemMetrics metrics, CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(CollectGpuMetricsAsync));
            try
            {
                // Usar TelemetryService se disponível (mais confiável)
                if (_telemetryService != null)
                {
                    var telemetryData = await _telemetryService.CollectMetricsAsync(cancellationToken);
                    metrics.GpuUsagePercent = telemetryData.GpuUsagePercent;
                    metrics.GpuTemperature = telemetryData.GpuTemperature ?? 0;
                    metrics.VramUsagePercent = telemetryData.VramUsagePercent;
                }
                // Fallback para GlobalThermalMonitorService
                else if (_thermalMonitor != null)
                {
                    var thermalMetrics = await _thermalMonitor.GetCurrentMetricsAsync();
                    metrics.GpuUsagePercent = thermalMetrics.GpuUsage;
                    metrics.GpuTemperature = thermalMetrics.GpuTemperature > 0 ? thermalMetrics.GpuTemperature : 0;
                    
                    if (thermalMetrics.GpuVramTotal > 0)
                    {
                        metrics.VramUsagePercent = (thermalMetrics.GpuVramUsed / thermalMetrics.GpuVramTotal) * 100;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[TelemetryLayer] Erro ao coletar métricas de GPU: {ex.Message}");
            }
            _logger.LogExit(nameof(CollectGpuMetricsAsync));
        }
        
        private async Task CollectFpsMetricsAsync(AdaptiveSystemMetrics metrics, CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(CollectFpsMetricsAsync));
            try
            {
                if (_latestFpsMetrics != null)
                {
                    metrics.Fps = _latestFpsMetrics.CurrentFps;
                    metrics.FrameTimeMs = _latestFpsMetrics.CurrentFps > 0 ? 1000.0 / _latestFpsMetrics.CurrentFps : 0;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[TelemetryLayer] Erro ao coletar métricas de FPS: {ex.Message}");
            }
            _logger.LogExit(nameof(CollectFpsMetricsAsync));
        }
        
        private async Task CollectProcessMetricsAsync(AdaptiveSystemMetrics metrics, CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(CollectProcessMetricsAsync));
            if (_gameProcess == null)
            {
                _logger.LogExit(nameof(CollectProcessMetricsAsync), "no process");
                return;
            }

            try
            {
                if (_gameProcess.HasExited)
                {
                    _logger.LogExit(nameof(CollectProcessMetricsAsync), "exited");
                    return;
                }

                metrics.GameMemoryMb = _gameProcess.WorkingSet64 / (1024 * 1024);

                double totalProcessorMs = _gameProcess.TotalProcessorTime.TotalMilliseconds;
                if (totalProcessorMs > 0)
                {
                    double uptimeMs = (DateTime.UtcNow - _gameProcess.StartTime.ToUniversalTime()).TotalMilliseconds;
                    metrics.GameCpuPercent = uptimeMs > 0 
                        ? Math.Min(100.0, (totalProcessorMs / uptimeMs) * 100.0 * Environment.ProcessorCount)
                        : 0;
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                _logger.LogDebug("[TelemetryLayer] Acesso negado ao processo do jogo (possível processo com privilégios maiores)");
            }
            catch (InvalidOperationException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[TelemetryLayer] Erro ao coletar métricas de processo: {ex.Message}");
            }
            _logger.LogExit(nameof(CollectProcessMetricsAsync));
        }
        
        private void OnFpsMetricsUpdated(object? sender, FrameMetrics metrics)
        {
            _logger.LogEntry(nameof(OnFpsMetricsUpdated));
            _latestFpsMetrics = metrics;
            _logger.LogExit(nameof(OnFpsMetricsUpdated));
        }

        public void Dispose()
        {
            _logger.LogEntry(nameof(Dispose));
            if (_disposed)
            {
                _logger.LogExit(nameof(Dispose), "already disposed");
                return;
            }
            
            if (_fpsMonitor != null)
            {
                _fpsMonitor.MetricsUpdated -= OnFpsMetricsUpdated;
            }
            
            _disposed = true;
            _logger.LogExit(nameof(Dispose));
        }
    }
    
    /// <summary>
    /// Métricas do sistema coletadas pela TelemetryLayer
    /// </summary>
    public class AdaptiveSystemMetrics
    {
        public DateTime Timestamp { get; set; }
        
        // CPU
        public double CpuUsagePercent { get; set; }
        public double CpuFreqCurrentMhz { get; set; }
        public double CpuFreqMaxMhz { get; set; }
        public double ProcessorQueueLength { get; set; }
        public double DpcPercent { get; set; }
        public double InterruptPercent { get; set; }
        public double PageFaultsPerSec { get; set; }
        
        // RAM
        public double RamUsagePercent { get; set; }
        public double RamUsedGb { get; set; }
        public double RamTotalGb { get; set; }
        
        // GPU
        public double GpuUsagePercent { get; set; }
        public double GpuTemperature { get; set; }
        public double VramUsagePercent { get; set; }
        
        // FPS
        public double Fps { get; set; }
        public double FrameTimeMs { get; set; }
        
        // Processo do Jogo
        public double GameCpuPercent { get; set; }
        public double GameMemoryMb { get; set; }
    }
}
