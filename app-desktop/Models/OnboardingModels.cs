using System;

namespace VoltrisOptimizer.Models
{
    public enum MachineClass
    {
        Unknown,
        Desktop,
        Laptop,
        Workstation,
        Server
    }

    public enum CpuVendor
    {
        Unknown,
        Intel,
        Amd,
        Arm
    }

    public enum StorageType
    {
        Unknown,
        Hdd,
        SataSsd,
        NvmeSsd
    }

    public enum GpuTier
    {
        Unknown,
        Integrated,
        Entry,
        MidRange,
        HighEnd,
        Enthusiast
    }

    public enum SystemMemoryTier
    {
        Low = 0,
        Medium = 1,
        High = 2,
        VeryHigh = 3
    }

    public enum ThermalState
    {
        Cool,
        Warm,
        Hot,
        Throttling
    }

    public enum PowerSource
    {
        AC,
        Battery
    }

    public enum OptimizationContext
    {
        Idle,
        General,
        Gaming,
        Streaming,
        Workstation,
        OnBattery
    }

    public sealed class HardwareProfile
    {
        public MachineClass MachineClass { get; set; } = MachineClass.Unknown;
        public CpuVendor CpuVendor { get; set; } = CpuVendor.Unknown;
        public int CpuCores { get; set; }
        public int CpuLogicalProcessors { get; set; }
        public int CpuBaseClockMhz { get; set; }
        public int CpuMaxTurboMhz { get; set; }
        public int CpuGeneration { get; set; }
        public bool IsEfficientCoreCpu { get; set; }
        public StorageType StorageType { get; set; } = StorageType.Unknown;
        public GpuTier GpuTier { get; set; } = GpuTier.Unknown;
        public double TotalRamGb { get; set; }
        public bool IsIntegratedGpu { get; set; }

        public SystemMemoryTier MemoryTier => TotalRamGb switch
        {
            <= 4 => SystemMemoryTier.Low,
            <= 8 => SystemMemoryTier.Medium,
            <= 24 => SystemMemoryTier.High,
            _ => SystemMemoryTier.VeryHigh
        };

        public bool IsLowEnd =>
            TotalRamGb <= 8 ||
            CpuCores <= 4 ||
            GpuTier == GpuTier.Integrated ||
            GpuTier == GpuTier.Entry;

        public bool IsHighEnd =>
            TotalRamGb >= 32 &&
            CpuCores >= 8 &&
            GpuTier >= GpuTier.HighEnd &&
            StorageType == StorageType.NvmeSsd;
    }

    public sealed class OnboardingRecommendation
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public bool Applied { get; set; }
        public string? DetailBefore { get; set; }
        public string? DetailAfter { get; set; }
        public bool Skipped { get; set; }
        public string? SkipReason { get; set; }
        public TimeSpan Duration { get; set; }
        public bool IsAlreadyOptimized { get; set; }
    }

    public sealed class OnboardingResult
    {
        public int PreviousScore { get; set; }
        public int CurrentScore { get; set; }
        public int ScoreDelta => CurrentScore - PreviousScore;
        public int TotalPointsGained { get; set; }
        public OnboardingRecommendation[] Recommendations { get; set; } = Array.Empty<OnboardingRecommendation>();
        public string Summary { get; set; } = string.Empty;
        public bool HadErrors { get; set; }
    }

    public sealed class OptimizationPolicy
    {
        public int TimerResolutionTicks { get; set; } = 156;
        public int EppValue { get; set; } = 50;
        public bool EnableGamingMode { get; set; }
        public bool AllowMemoryTrim { get; set; } = true;
        public int MemoryTrimTargetCount { get; set; } = 8;
        public bool AggressiveTimer { get; set; }
        public string ProfileName { get; set; } = "Balanced";
        public string Reason { get; set; } = string.Empty;
    }
}
