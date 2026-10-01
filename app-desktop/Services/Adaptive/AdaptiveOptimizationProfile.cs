using System;
using VoltrisOptimizer.Core.SystemIntelligenceProfiler.Models;

namespace VoltrisOptimizer.Services.Adaptive
{
    /// <summary>
    /// Adaptive optimization profile that calculates optimal intensity based on hardware capabilities
    /// Provides hardware-aware optimization profiles that prevent performance regressions
    /// </summary>
    public class AdaptiveOptimizationProfile
    {
        /// <summary>
        /// Calculates optimal optimization intensity based on hardware profile and system load
        /// </summary>
        public OptimizationIntensity CalculateOptimalIntensity(
            HardwareProfile hardware,
            SystemLoad currentLoad,
            UserPreference preference)
        {
            if (hardware == null)
                throw new ArgumentNullException(nameof(hardware));

            var intensity = new OptimizationIntensity();

            // CPU-based adjustments
            ConfigureCpuOptimization(intensity, hardware, currentLoad, preference);
            
            // Memory-based adjustments
            ConfigureMemoryOptimization(intensity, hardware, currentLoad, preference);
            
            // Storage-based adjustments
            ConfigureStorageOptimization(intensity, hardware, preference);
            
            // Apply user preferences as final override
            ApplyUserPreferences(intensity, preference);

            // Validate and constrain values
            ConstrainIntensityValues(intensity);

            return intensity;
        }

        /// <summary>
        /// Configures CPU optimization based on hardware capabilities
        /// </summary>
        private void ConfigureCpuOptimization(
            OptimizationIntensity intensity,
            HardwareProfile hardware,
            SystemLoad currentLoad,
            UserPreference preference)
        {
            var cpuScore = hardware.HardwareScore;
            var coreCount = hardware.Cpu?.LogicalCores ?? 0;
            var currentCpuUsage = currentLoad?.CpuUsagePercent ?? 0;

            // Base CPU optimization level
            if (cpuScore < 3.0)
            {
                intensity.CpuOptimization = OptimizationLevel.Conservative;
                intensity.SchedulingAggressiveness = AggressivenessLevel.Low;
                intensity.BackgroundProcessPriority = ProcessPriority.Low;
            }
            else if (cpuScore < 5.0)
            {
                intensity.CpuOptimization = OptimizationLevel.Moderate;
                intensity.SchedulingAggressiveness = AggressivenessLevel.Medium;
                intensity.BackgroundProcessPriority = ProcessPriority.Normal;
            }
            else if (cpuScore < 7.0)
            {
                intensity.CpuOptimization = OptimizationLevel.Aggressive;
                intensity.SchedulingAggressiveness = AggressivenessLevel.High;
                intensity.BackgroundProcessPriority = ProcessPriority.BelowNormal;
            }
            else
            {
                intensity.CpuOptimization = OptimizationLevel.Maximum;
                intensity.SchedulingAggressiveness = AggressivenessLevel.VeryHigh;
                intensity.BackgroundProcessPriority = ProcessPriority.Idle;
            }

            // Adjust for current load
            if (currentCpuUsage > 80)
            {
                // System is under heavy load - reduce aggressiveness
                ReduceAggressiveness(intensity);
            }
            else if (currentCpuUsage < 20 && coreCount > 4)
            {
                // System has spare capacity - can be more aggressive
                IncreaseAggressiveness(intensity);
            }

            // NUMA and multi-core considerations
            if (coreCount >= 8)
            {
                intensity.EnableNumaOptimization = true;
                intensity.ProcessAffinityStrategy = AffinityStrategy.SmartDistribution;
            }
            else if (coreCount >= 4)
            {
                intensity.ProcessAffinityStrategy = AffinityStrategy.Balanced;
            }
            else
            {
                intensity.ProcessAffinityStrategy = AffinityStrategy.None;
            }
        }

        /// <summary>
        /// Configures memory optimization based on available RAM
        /// </summary>
        private void ConfigureMemoryOptimization(
            OptimizationIntensity intensity,
            HardwareProfile hardware,
            SystemLoad currentLoad,
            UserPreference preference)
        {
            var ramGb = (hardware.Ram?.TotalMb ?? 0) / 1024.0;
            var availableRamPercent = currentLoad?.MemoryAvailablePercent ?? 50;

            if (ramGb < 4)
            {
                intensity.MemoryOptimization = OptimizationLevel.Light;
                intensity.CacheAggressiveness = AggressivenessLevel.Low;
                intensity.VirtualMemoryStrategy = VirtualMemoryStrategy.Conservative;
                intensity.TrimWorkingSetAggressiveness = AggressivenessLevel.High;
            }
            else if (ramGb < 8)
            {
                intensity.MemoryOptimization = OptimizationLevel.Moderate;
                intensity.CacheAggressiveness = AggressivenessLevel.Medium;
                intensity.VirtualMemoryStrategy = VirtualMemoryStrategy.Balanced;
                intensity.TrimWorkingSetAggressiveness = AggressivenessLevel.Medium;
            }
            else if (ramGb < 16)
            {
                intensity.MemoryOptimization = OptimizationLevel.Aggressive;
                intensity.CacheAggressiveness = AggressivenessLevel.High;
                intensity.VirtualMemoryStrategy = VirtualMemoryStrategy.Aggressive;
                intensity.TrimWorkingSetAggressiveness = AggressivenessLevel.Low;
            }
            else
            {
                intensity.MemoryOptimization = OptimizationLevel.Maximum;
                intensity.CacheAggressiveness = AggressivenessLevel.VeryHigh;
                intensity.VirtualMemoryStrategy = VirtualMemoryStrategy.Maximum;
                intensity.TrimWorkingSetAggressiveness = AggressivenessLevel.None;
            }

            // Adjust for current memory pressure
            if (availableRamPercent < 15)
            {
                // Low memory - increase trimming aggressiveness
                intensity.TrimWorkingSetAggressiveness = AggressivenessLevel.VeryHigh;
                intensity.CacheAggressiveness = AggressivenessLevel.Low;
            }
            else if (availableRamPercent > 50)
            {
                // Plenty of memory - can be more aggressive with caching
                intensity.CacheAggressiveness = AggressivenessLevel.VeryHigh;
                intensity.TrimWorkingSetAggressiveness = AggressivenessLevel.None;
            }
        }

        /// <summary>
        /// Configures storage optimization based on drive type
        /// </summary>
        private void ConfigureStorageOptimization(
            OptimizationIntensity intensity,
            HardwareProfile hardware,
            UserPreference preference)
        {
            var storageType = hardware.Storage?.Type ?? "Unknown";
            var isSsd = storageType.Contains("SSD", StringComparison.OrdinalIgnoreCase) ||
                       storageType.Contains("NVMe", StringComparison.OrdinalIgnoreCase);

            if (isSsd)
            {
                intensity.StorageOptimization = OptimizationLevel.Aggressive;
                intensity.DiskIoScheduling = IoSchedulingStrategy.Aggressive;
                intensity.WriteCachingStrategy = WriteCachingStrategy.Enabled;
                intensity.DefragStrategy = DefragStrategy.None; // SSDs don't need defrag
            }
            else
            {
                intensity.StorageOptimization = OptimizationLevel.Conservative;
                intensity.DiskIoScheduling = IoSchedulingStrategy.Conservative;
                intensity.WriteCachingStrategy = WriteCachingStrategy.Conditional;
                intensity.DefragStrategy = DefragStrategy.Scheduled;
            }

            // Storage space considerations
            var freeSpaceGb = hardware.Storage?.FreeGb ?? 0;
            var totalSpaceGb = hardware.Storage?.TotalGb ?? 1;
            var freeSpacePercent = (double)freeSpaceGb / totalSpaceGb * 100;

            if (freeSpacePercent < 10)
            {
                // Low disk space - reduce cache sizes
                intensity.CacheAggressiveness = AggressivenessLevel.Low;
                intensity.WriteCachingStrategy = WriteCachingStrategy.Disabled;
            }
        }

        /// <summary>
        /// Applies user preferences as final override
        /// </summary>
        private void ApplyUserPreferences(OptimizationIntensity intensity, UserPreference preference)
        {
            if (preference == null) return;

            // Override specific settings based on user preference
            switch (preference.OptimizationPreference)
            {
                case UserOptimizationPreference.Performance:
                    intensity.CpuOptimization = OptimizationLevel.Maximum;
                    intensity.MemoryOptimization = OptimizationLevel.Maximum;
                    intensity.SchedulingAggressiveness = AggressivenessLevel.VeryHigh;
                    break;

                case UserOptimizationPreference.BatteryLife:
                    intensity.CpuOptimization = OptimizationLevel.Conservative;
                    intensity.BackgroundProcessPriority = ProcessPriority.Idle;
                    intensity.CacheAggressiveness = AggressivenessLevel.Low;
                    break;

                case UserOptimizationPreference.Balanced:
                    // Keep calculated values
                    break;

                case UserOptimizationPreference.QuietOperation:
                    intensity.BackgroundProcessPriority = ProcessPriority.Idle;
                    intensity.SchedulingAggressiveness = AggressivenessLevel.Low;
                    break;
            }

            // Respect user's risk tolerance
            if (preference.RiskTolerance == RiskTolerance.Low)
            {
                // Reduce all aggressiveness levels
                ReduceAggressiveness(intensity);
                intensity.CpuOptimization = OptimizationLevel.Conservative;
                intensity.MemoryOptimization = OptimizationLevel.Light;
            }
        }

        /// <summary>
        /// Reduces aggressiveness levels to prevent system instability
        /// </summary>
        private void ReduceAggressiveness(OptimizationIntensity intensity)
        {
            intensity.SchedulingAggressiveness = DecreaseAggressiveness(intensity.SchedulingAggressiveness);
            intensity.CacheAggressiveness = DecreaseAggressiveness(intensity.CacheAggressiveness);
            intensity.TrimWorkingSetAggressiveness = DecreaseAggressiveness(intensity.TrimWorkingSetAggressiveness);
        }

        /// <summary>
        /// Increases aggressiveness levels for better performance
        /// </summary>
        private void IncreaseAggressiveness(OptimizationIntensity intensity)
        {
            intensity.SchedulingAggressiveness = IncreaseAggressiveness(intensity.SchedulingAggressiveness);
            intensity.CacheAggressiveness = IncreaseAggressiveness(intensity.CacheAggressiveness);
        }

        /// <summary>
        /// Constrains intensity values to valid ranges
        /// </summary>
        private void ConstrainIntensityValues(OptimizationIntensity intensity)
        {
            // Ensure enum values are within valid ranges
            if (!Enum.IsDefined(typeof(OptimizationLevel), intensity.CpuOptimization))
                intensity.CpuOptimization = OptimizationLevel.Moderate;

            if (!Enum.IsDefined(typeof(AggressivenessLevel), intensity.SchedulingAggressiveness))
                intensity.SchedulingAggressiveness = AggressivenessLevel.Medium;

            // Ensure logical consistency
            if (intensity.CpuOptimization == OptimizationLevel.Conservative)
            {
                intensity.SchedulingAggressiveness = AggressivenessLevel.Low;
                intensity.BackgroundProcessPriority = ProcessPriority.Idle;
            }
        }

        /// <summary>
        /// Decreases aggressiveness level by one step
        /// </summary>
        private AggressivenessLevel DecreaseAggressiveness(AggressivenessLevel level)
        {
            return level switch
            {
                AggressivenessLevel.VeryHigh => AggressivenessLevel.High,
                AggressivenessLevel.High => AggressivenessLevel.Medium,
                AggressivenessLevel.Medium => AggressivenessLevel.Low,
                AggressivenessLevel.Low => AggressivenessLevel.None,
                _ => AggressivenessLevel.None
            };
        }

        /// <summary>
        /// Increases aggressiveness level by one step
        /// </summary>
        private AggressivenessLevel IncreaseAggressiveness(AggressivenessLevel level)
        {
            return level switch
            {
                AggressivenessLevel.None => AggressivenessLevel.Low,
                AggressivenessLevel.Low => AggressivenessLevel.Medium,
                AggressivenessLevel.Medium => AggressivenessLevel.High,
                AggressivenessLevel.High => AggressivenessLevel.VeryHigh,
                _ => AggressivenessLevel.VeryHigh
            };
        }

        /// <summary>
        /// Gets hardware classification description
        /// </summary>
        public string GetHardwareDescription(HardwareProfile hardware)
        {
            if (hardware == null) return "Unknown hardware";

            return hardware.Classification switch
            {
                HardwareClass.LowEnd => $"Budget system ({hardware.HardwareScore:F1}/10) - Conservative optimizations recommended",
                HardwareClass.MidRange => $"Standard system ({hardware.HardwareScore:F1}/10) - Balanced optimizations suitable",
                HardwareClass.HighEnd => $"High-performance system ({hardware.HardwareScore:F1}/10) - Aggressive optimizations possible",
                HardwareClass.Workstation => $"Workstation-class ({hardware.HardwareScore:F1}/10) - Maximum optimizations recommended",
                HardwareClass.Server => $"Server-class ({hardware.HardwareScore:F1}/10) - Enterprise optimizations enabled",
                _ => $"Hardware score: {hardware.HardwareScore:F1}/10"
            };
        }

        /// <summary>
        /// Evaluates if optimization is safe for given hardware
        /// </summary>
        public bool IsOptimizationSafe(VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization optimization, HardwareProfile hardware)
        {
            if (optimization == null || hardware == null) return false;

            // Check hardware requirements
            if (optimization.RequiresSSD && 
                !(hardware.Storage?.Type?.Contains("SSD", StringComparison.OrdinalIgnoreCase) == true))
                return false;

            if (optimization.MinRamGb > 0 && 
                (hardware.Ram?.TotalMb ?? 0) < optimization.MinRamGb * 1024)
                return false;

            if (optimization.MinCpuCores > 0 && 
                (hardware.Cpu?.LogicalCores ?? 0) < optimization.MinCpuCores)
                return false;

            // Check hardware classification suitability
            if (hardware.Classification <= HardwareClass.LowEnd && 
                (optimization.MinRamGb > 4 || optimization.MinCpuCores > 4))
                return false;

            return true;
        }
    }

    /// <summary>
    /// Intensity levels for different optimization aspects
    /// </summary>
    public class OptimizationIntensity
    {
        public OptimizationLevel CpuOptimization { get; set; } = OptimizationLevel.Moderate;
        public OptimizationLevel MemoryOptimization { get; set; } = OptimizationLevel.Moderate;
        public OptimizationLevel StorageOptimization { get; set; } = OptimizationLevel.Moderate;
        
        public AggressivenessLevel SchedulingAggressiveness { get; set; } = AggressivenessLevel.Medium;
        public AggressivenessLevel CacheAggressiveness { get; set; } = AggressivenessLevel.Medium;
        public AggressivenessLevel TrimWorkingSetAggressiveness { get; set; } = AggressivenessLevel.Medium;
        
        public ProcessPriority BackgroundProcessPriority { get; set; } = ProcessPriority.Normal;
        public AffinityStrategy ProcessAffinityStrategy { get; set; } = AffinityStrategy.Balanced;
        public VirtualMemoryStrategy VirtualMemoryStrategy { get; set; } = VirtualMemoryStrategy.Balanced;
        public IoSchedulingStrategy DiskIoScheduling { get; set; } = IoSchedulingStrategy.Balanced;
        public WriteCachingStrategy WriteCachingStrategy { get; set; } = WriteCachingStrategy.Conditional;
        public DefragStrategy DefragStrategy { get; set; } = DefragStrategy.Scheduled;
        
        public bool EnableNumaOptimization { get; set; } = false;
    }

    /// <summary>
    /// User preference settings
    /// </summary>
    public class UserPreference
    {
        public UserOptimizationPreference OptimizationPreference { get; set; } = UserOptimizationPreference.Balanced;
        public RiskTolerance RiskTolerance { get; set; } = RiskTolerance.Medium;
        public bool PreferStability { get; set; } = true;
        public bool AllowExperimental { get; set; } = false;
    }

    /// <summary>
    /// Current system load information
    /// </summary>
    public class SystemLoad
    {
        public double CpuUsagePercent { get; set; }
        public double MemoryUsagePercent { get; set; }
        public double MemoryAvailablePercent => 100 - MemoryUsagePercent;
        public double DiskIoPercent { get; set; }
        public double NetworkUsagePercent { get; set; }
        public bool IsUnderHeavyLoad => CpuUsagePercent > 80 || MemoryAvailablePercent < 15;
    }

    // Enums
    public enum OptimizationLevel
    {
        None,
        Light,
        Conservative,
        Moderate,
        Aggressive,
        Maximum,
        MemoryOptimized,
        ThermalSafe
    }

    public enum AggressivenessLevel
    {
        None,
        Low,
        Medium,
        High,
        VeryHigh
    }

    public enum ProcessPriority
    {
        Idle,
        Low,
        BelowNormal,
        Normal,
        AboveNormal,
        High
    }

    public enum AffinityStrategy
    {
        None,
        Balanced,
        SmartDistribution,
        DedicatedCores
    }

    public enum VirtualMemoryStrategy
    {
        Conservative,
        Balanced,
        Aggressive,
        Maximum
    }

    public enum IoSchedulingStrategy
    {
        Conservative,
        Balanced,
        Aggressive
    }

    public enum WriteCachingStrategy
    {
        Disabled,
        Conditional,
        Enabled
    }

    public enum DefragStrategy
    {
        None,
        Scheduled,
        Automatic
    }

    public enum UserOptimizationPreference
    {
        Performance,
        BatteryLife,
        Balanced,
        QuietOperation
    }

    public enum RiskTolerance
    {
        Low,
        Medium,
        High
    }
}
