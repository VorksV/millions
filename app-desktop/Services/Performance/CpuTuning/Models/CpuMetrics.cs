using System;
using System.Linq;

namespace VoltrisOptimizer.Services.Performance.CpuTuning.Models
{
    /// <summary>
    /// Real-time CPU performance metrics
    /// </summary>
    public class CpuMetrics
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        
        // Temperature (per core and package)
        public double[] CoreTemperatures { get; set; } = Array.Empty<double>();
        public double PackageTemperature { get; set; }
        public double MaxCoreTemperature => CoreTemperatures.Length > 0 ? CoreTemperatures.Max() : 0;
        
        // Frequency
        public double[] CoreFrequencies { get; set; } = Array.Empty<double>();
        public double AverageFrequency => CoreFrequencies.Length > 0 ? CoreFrequencies.Average() : 0;
        public double MaxFrequency => CoreFrequencies.Length > 0 ? CoreFrequencies.Max() : 0;
        
        // Power
        public double PackagePower { get; set; } // Watts
        public double Voltage { get; set; } // Volts
        
        // Temperature/Thermal
        public double Temperature { get; set; }
        public double ThermalHeadroom { get; set; }
        
        // Power/EPP States
        public int CurrentPl1 { get; set; }
        public int CurrentPl2 { get; set; }
        public double CurrentTau { get; set; }
        public int CurrentEpp { get; set; }
        
        // Usage
        public double CpuUsage => CpuUsagePercent;
        public double GpuUsage => GpuUsagePercent;
        public double CpuUsagePercent { get; set; }
        public double GpuUsagePercent { get; set; }
        public double RamUsagePercent { get; set; }
        public double VramUsagePercent { get; set; }
        
        // Throttling flags
        public bool IsThermalThrottling { get; set; }
        public bool IsSiliconThrottling { get; set; } // Direct MSR
        public bool IsPowerLimitThrottling { get; set; }
        public bool IsCurrentLimitThrottling { get; set; } // EDP limit
        
        public double DistanceToTjMax { get; set; }
        public ThermalCondition ThermalCondition { get; set; }
        
        // Turbo state
        public bool IsTurboActive { get; set; }
        public double TurboTimePercent { get; set; }
        
        // Frame time (if available)
        public double AverageFrameTime { get; set; } // milliseconds
    }
}
