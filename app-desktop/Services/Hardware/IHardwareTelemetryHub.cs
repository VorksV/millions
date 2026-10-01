using System;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Hardware
{
    public class HardwareTelemetryMetrics
    {
        public double CpuTemperature { get; set; } = double.NaN;
        public double CpuLoad { get; set; } = 0;
        public double CpuClock { get; set; } = 0;
        public int CpuCores { get; set; } = 0;
        
        public double GpuTemperature { get; set; } = double.NaN;
        public double GpuLoad { get; set; } = 0;
        public double GpuClock { get; set; } = 0;
        public double GpuVramUsed { get; set; } = 0;
        public double GpuVramTotal { get; set; } = 0;
        public string GpuName { get; set; } = "Unknown GPU";
        
        public double SystemRamUsedGB { get; set; } = 0;
        
        public DateTime Timestamp { get; set; } = DateTime.MinValue;
        public bool IsValid => Timestamp != DateTime.MinValue && (CpuTemperature > 0 || CpuLoad > 0);
    }

    public interface IHardwareTelemetryHub : IDisposable
    {
        /// <summary>
        /// Retrieves the latest cached metrics. Will not trigger a hardware poll if cache is valid.
        /// </summary>
        HardwareTelemetryMetrics GetLatestMetrics();
        
        /// <summary>
        /// Forces an immediate hardware poll and returns fresh metrics.
        /// Use sparingly to avoid CPU overhead.
        /// </summary>
        Task<HardwareTelemetryMetrics> ForcePollAsync();
        
        /// <summary>
        /// Event fired when metrics are updated by the background polling task.
        /// </summary>
        event EventHandler<HardwareTelemetryMetrics>? MetricsUpdated;
        
        /// <summary>
        /// Starts the background polling process. Safe to call multiple times.
        /// </summary>
        Task StartMonitoringAsync();
        
        /// <summary>
        /// Stops the background polling process.
        /// </summary>
        Task StopMonitoringAsync();
    }
}
