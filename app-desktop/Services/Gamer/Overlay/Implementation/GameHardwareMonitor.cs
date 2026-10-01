using System;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer.Overlay.Implementation
{
    /// <summary>
    /// STUB - GameHardwareMonitor desativado (V2 Architecture - Process Lasso approach)
    /// Polling loop removido - usar SystemMetricsCache (reativo, zero polling)
    /// </summary>
    public class GameHardwareMonitor : IDisposable
    {
        private readonly ILoggingService? _logger;

        public GameHardwareMonitor(ILoggingService? logger = null)
        {
            _logger?.LogEntry(nameof(GameHardwareMonitor));
            _logger = logger;
            _logger?.LogInfo("[GameHardwareMonitor] ENTER: Constructor");
            _logger?.LogInfo("[GameHardwareMonitor] STUB - V2 Architecture (usar SystemMetricsCache)");
            _logger?.LogInfo("[GameHardwareMonitor] EXIT: Constructor");
            _logger?.LogExit(nameof(GameHardwareMonitor));
        }

        public event EventHandler<GameHardwareUpdatedEventArgs>? HardwareUpdated;

        public Task<bool> StartAsync()
        {
            _logger?.LogEntry(nameof(StartAsync));
            _logger?.LogInfo("[GameHardwareMonitor] ENTER: StartAsync");
            _logger?.LogInfo("[GameHardwareMonitor] EXIT: StartAsync");
            _logger?.LogExit(nameof(StartAsync));
            return Task.FromResult(false);
        }
        public void Stop()
        {
            _logger?.LogEntry(nameof(Stop));
            _logger?.LogInfo("[GameHardwareMonitor] ENTER: Stop");
            _logger?.LogInfo("[GameHardwareMonitor] EXIT: Stop");
            _logger?.LogExit(nameof(Stop));
        }

        public GameHardwareMetrics GetCurrentMetrics()
        {
            _logger?.LogEntry(nameof(GetCurrentMetrics));
            _logger?.LogInfo("[GameHardwareMonitor] ENTER: GetCurrentMetrics");
            var metrics = new GameHardwareMetrics();
            _logger?.LogInfo("[GameHardwareMonitor] EXIT: GetCurrentMetrics");
            _logger?.LogExit(nameof(GetCurrentMetrics));
            return metrics;
        }

        public void Dispose()
        {
            _logger?.LogEntry(nameof(Dispose));
            _logger?.LogInfo("[GameHardwareMonitor] ENTER: Dispose");
            _logger?.LogInfo("[GameHardwareMonitor] EXIT: Dispose");
            _logger?.LogExit(nameof(Dispose));
        }

        public class GameHardwareMetrics
        {
            public float CpuUsage { get; set; }
            public float CpuTemperature { get; set; }
            public float CpuClock { get; set; }
            public float CpuMaxClock { get; set; }
            public float GpuUsage { get; set; }
            public float GpuTemperature { get; set; }
            public float GpuClock { get; set; }
            public float GpuMemoryUsage { get; set; }
            public float GpuDedicatedVram { get; set; }
            public float SystemRamUsage { get; set; }
        }

        public class GameHardwareUpdatedEventArgs : EventArgs
        {
            public GameHardwareMetrics Metrics { get; set; } = new();
        }
    }
}