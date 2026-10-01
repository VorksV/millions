using System;
using VoltrisOptimizer.Services.Performance.Models;

namespace VoltrisOptimizer.Services.Performance.Decision.Models
{
    /// <summary>
    /// Read-only snapshot of current system state for decision-making.
    /// NO external calls, NO heavy computation, NO decision logic.
    /// </summary>
    public sealed class PerformanceContext
    {
        // Hardware Profile (from existing detection)
        public PerformanceSystemProfile HardwareProfile { get; }
        public HardwareTier HardwareTier { get; }

        // Intelligence & Scoring
        public MachineClassification Classification { get; }
        public double GlobalPerformanceScore { get; } // 0.0 to 1.0

        // Current Usage State
        public UsageScenario CurrentScenario { get; }
        public double CpuUsagePercent { get; }
        public double MemoryUsagePercent { get; }
        public double DiskUsagePercent { get; }
        public double GpuUsagePercent { get; }

        // System State
        public PowerState PowerState { get; }
        public bool IsLaptop { get; }
        public bool IsOnBattery { get; }
        public int ActiveProcessCount { get; }

        // Context Metadata
        public DateTime CapturedAt { get; }
        public string ContextId { get; }

        // Optional: Active Application Context
        public string? ActiveApplicationName { get; }
        public int? ActiveApplicationPid { get; }
        public bool IsGameRunning { get; }

        public PerformanceContext(
            PerformanceSystemProfile hardwareProfile,
            HardwareTier hardwareTier,
            MachineClassification classification,
            double performanceScore,
            UsageScenario currentScenario,
            double cpuUsagePercent,
            double memoryUsagePercent,
            double diskUsagePercent,
            double gpuUsagePercent,
            PowerState powerState,
            bool isLaptop,
            bool isOnBattery,
            int activeProcessCount,
            string? activeApplicationName = null,
            int? activeApplicationPid = null,
            bool isGameRunning = false)
        {
            HardwareProfile = hardwareProfile ?? throw new ArgumentNullException(nameof(hardwareProfile));
            HardwareTier = hardwareTier;
            Classification = classification;
            GlobalPerformanceScore = Math.Clamp(performanceScore, 0.0, 1.0);
            CurrentScenario = currentScenario;
            CpuUsagePercent = Math.Clamp(cpuUsagePercent, 0, 100);
            MemoryUsagePercent = Math.Clamp(memoryUsagePercent, 0, 100);
            DiskUsagePercent = Math.Clamp(diskUsagePercent, 0, 100);
            GpuUsagePercent = Math.Clamp(gpuUsagePercent, 0, 100);
            PowerState = powerState;
            IsLaptop = isLaptop;
            IsOnBattery = isOnBattery;
            ActiveProcessCount = Math.Max(0, activeProcessCount);
            ActiveApplicationName = activeApplicationName;
            ActiveApplicationPid = activeApplicationPid;
            IsGameRunning = isGameRunning;
            CapturedAt = DateTime.UtcNow;
            ContextId = Guid.NewGuid().ToString("N");
        }

        /// <summary>
        /// Quick check if system is under heavy load
        /// </summary>
        public bool IsUnderHeavyLoad => CpuUsagePercent > 80 || MemoryUsagePercent > 85 || DiskUsagePercent > 90;

        /// <summary>
        /// Quick check if system is idle
        /// </summary>
        public bool IsIdle => CpuUsagePercent < 10 && DiskUsagePercent < 5 && !IsGameRunning;

        public override string ToString() =>
            $"Context[{ContextId}] {CurrentScenario} | CPU:{CpuUsagePercent:F1}% RAM:{MemoryUsagePercent:F1}% | {Classification}";
    }

    /// <summary>
    /// Detected usage scenario
    /// </summary>
    public enum UsageScenario
    {
        Idle,           // Low activity, background tasks only
        Browsing,       // Web browsing, light productivity
        Productivity,   // Office work, development
        Gaming,         // Game detected and running
        Rendering,      // Video/3D rendering, heavy compute
        Unknown         // Cannot determine
    }

    /// <summary>
    /// System power state
    /// </summary>
    public enum PowerState
    {
        AC,             // Plugged in
        Battery,        // On battery
        BatterySaver,   // Battery saver mode active
        Unknown
    }
}
