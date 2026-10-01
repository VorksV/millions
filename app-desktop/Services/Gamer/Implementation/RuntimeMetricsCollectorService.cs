using VoltrisOptimizer.Utils;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.Gamer.Interfaces;
using VoltrisOptimizer.Services.Thermal.Models;
using VoltrisOptimizer.Services.Thermal;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer.Implementation
{
    public sealed class RuntimeMetricsCollectorService : IRuntimeMetricsCollector
    {
        private readonly ILoggingService _logger;
        private readonly IGlobalThermalMonitorService _thermalService;
        private readonly LatestPerformanceMetricsProvider _metricsProvider;
        private Timer? _timer;
        private readonly object _lock = new();
        private bool _isRunning;

        private SafePerformanceCounter? _cpuCounter;
        private SafePerformanceCounter? _ramCounter;
        private SafePerformanceCounter? _diskReadCounter;
        private SafePerformanceCounter? _diskWriteCounter;
        private bool _countersAvailable;

        public event EventHandler<PerformanceMetrics>? MetricsUpdated;

public RuntimeMetricsCollectorService(ILoggingService logger, IGlobalThermalMonitorService thermalService, LatestPerformanceMetricsProvider provider)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _thermalService = thermalService ?? throw new ArgumentNullException(nameof(thermalService));
            _metricsProvider = provider ?? throw new ArgumentNullException(nameof(provider));
            _logger.LogDebug("[RuntimeMetrics] >>> ENTER .ctor");
            InitializeCounters();
            _logger.LogDebug("[RuntimeMetrics] <<< EXIT .ctor");
        }

        private void InitializeCounters()
        {
            try
            {
                _cpuCounter = new SafePerformanceCounter("Processor", "% Processor Time", "_Total");
                _cpuCounter.NextValue();
                _ramCounter = new SafePerformanceCounter("Memory", "% Committed Bytes In Use");
                _ramCounter.NextValue();
                _diskReadCounter = new SafePerformanceCounter("PhysicalDisk", "Avg. Disk sec/Read", "_Total");
                _diskReadCounter.NextValue();
                _diskWriteCounter = new SafePerformanceCounter("PhysicalDisk", "Avg. Disk sec/Write", "_Total");
                _diskWriteCounter.NextValue();
                _countersAvailable = true;
                _logger.LogInfo("[RuntimeMetrics] PerformanceCounters inicializados com sucesso");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Performance Counter"))
            {
                _countersAvailable = false;
                _logger.LogWarning($"[RuntimeMetrics] PerformanceCounters indisponíveis (registry corrompido?): {ex.Message}. Serviço continuará com dados térmicos apenas.");
            }
            catch (Exception ex)
            {
                _countersAvailable = false;
                _logger.LogWarning($"[RuntimeMetrics] Erro ao inicializar PerformanceCounters: {ex.Message}. Serviço continuará em modo degradado.");
            }
        }

public void Start()
        {
            _logger.LogDebug("[RuntimeMetrics] >>> ENTER Start");
            lock (_lock)
            {
                if (_isRunning)
                {
                    _logger.LogDebug("[RuntimeMetrics] <<< EXIT Start (Already running)");
                    return;
                }
                _isRunning = true;

                if (_countersAvailable)
                {
                    _cpuCounter!.NextValue();
                    _ramCounter!.NextValue();
                    _diskReadCounter!.NextValue();
                    _diskWriteCounter!.NextValue();
                }

                _timer = new Timer(CollectMetrics, null, TimeSpan.Zero, TimeSpan.FromSeconds(10));
                _logger.LogInfo("[RuntimeMetrics] Coleta iniciada (intervalo 10 s)");
            }
            _logger.LogDebug("[RuntimeMetrics] <<< EXIT Start");
        }

public void Stop()
        {
            _logger.LogDebug("[RuntimeMetrics] >>> ENTER Stop");
            lock (_lock)
            {
                if (!_isRunning)
                {
                    _logger.LogDebug("[RuntimeMetrics] <<< EXIT Stop (Not running)");
                    return;
                }
                _isRunning = false;
                _timer?.Dispose();
                _timer = null;
                _logger.LogInfo("[RuntimeMetrics] Coleta interrompida");
            }
            _logger.LogDebug("[RuntimeMetrics] <<< EXIT Stop");
        }

        private void CollectMetrics(object? state)
        {
            try
            {
                // Copia métrica térmica atual (se houver) como base
                var baseMetrics = _thermalService.CurrentMetrics ?? new ThermalMetrics();
                var perf = new PerformanceMetrics
                {
                    Timestamp = DateTime.Now,
                    CpuTemperature = baseMetrics.CpuTemperature,
                    GpuTemperature = baseMetrics.GpuTemperature,
                    CpuUsage = _countersAvailable ? Math.Round(_cpuCounter!.NextValue(), 1) : 0,
                    GpuUsage = baseMetrics.GpuUsage,
                    RamUsagePercent = _countersAvailable ? Math.Round(_ramCounter!.NextValue(), 1) : 0,
                    GpuVramUsed = baseMetrics.GpuVramUsed,
                    GpuVramTotal = baseMetrics.GpuVramTotal,
                    CpuThrottling = baseMetrics.CpuThrottling,
                    GpuThrottling = baseMetrics.GpuThrottling,
                    IsCpuTemperatureEstimated = baseMetrics.IsCpuTemperatureEstimated,
                    IsGpuTemperatureEstimated = baseMetrics.IsGpuTemperatureEstimated,
                    DiskReadLatencyMs = _countersAvailable ? Math.Round(_diskReadCounter!.NextValue() * 1000, 2) : 0,
                    DiskWriteLatencyMs = _countersAvailable ? Math.Round(_diskWriteCounter!.NextValue() * 1000, 2) : 0,
                    DpcLatencyMs = 0,
                    IsrLatencyMs = 0
                };

                _logger.LogDebug($"[RuntimeMetrics] CPU {perf.CpuUsage}% | RAM {perf.RamUsagePercent}% | DiskRead {perf.DiskReadLatencyMs}ms | DiskWrite {perf.DiskWriteLatencyMs}ms");
                MetricsUpdated?.Invoke(this, perf);
                // Atualiza provider com a métrica mais recente para uso em medições de otimizações
                _metricsProvider.Update(perf);
            }
            catch (Exception ex)
            {
                _logger.LogError("[RuntimeMetrics] Erro ao coletar métricas: " + ex.Message);
            }
        }

public void Dispose()
        {
            _logger.LogDebug("[RuntimeMetrics] >>> ENTER Dispose");
            Stop();
            _cpuCounter?.Dispose();
            _ramCounter?.Dispose();
            _diskReadCounter?.Dispose();
            _diskWriteCounter?.Dispose();
            _logger.LogDebug("[RuntimeMetrics] <<< EXIT Dispose");
        }
    }
}

