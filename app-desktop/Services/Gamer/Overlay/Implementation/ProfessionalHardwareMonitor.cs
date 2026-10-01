using System;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer.Overlay.Implementation
{
    /// <summary>
    /// STUB - ProfessionalHardwareMonitor desativado (V2 Architecture - Process Lasso approach)
    /// Polling loop removido - usar SystemMetricsCache (reativo, zero polling)
    /// </summary>
    public class ProfessionalHardwareMonitor : IDisposable
    {
        private readonly ILoggingService? _logger;

        public ProfessionalHardwareMonitor(ILoggingService? logger = null)
        {
            _logger?.LogEntry(nameof(ProfessionalHardwareMonitor));
            _logger = logger;
            _logger?.LogInfo("[ProfessionalHardwareMonitor] STUB - V2 Architecture (usar SystemMetricsCache)");
            _logger?.LogExit(nameof(ProfessionalHardwareMonitor));
        }

        public ProfessionalHardwareMetrics GetCurrentMetrics()
        {
            _logger?.LogEntry(nameof(GetCurrentMetrics));
            _logger?.LogExit(nameof(GetCurrentMetrics));
            return new ProfessionalHardwareMetrics();
        }

        public void Dispose()
        {
            _logger?.LogEntry(nameof(Dispose));
            _logger?.LogExit(nameof(Dispose));
        }

        public class ProfessionalHardwareMetrics
        {
            public float CpuUsage { get; set; }
            public float CpuTemperature { get; set; }
            public float CpuClock { get; set; }
            public float GpuUsage { get; set; }
            public float GpuTemperature { get; set; }
            public float GpuClock { get; set; }
            public float GpuMemoryUsage { get; set; }
            public float SystemRamUsage { get; set; }
        }
    }
}