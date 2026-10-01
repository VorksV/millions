using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.Gamer.GameCategorization.Models;
using VoltrisOptimizer.Services.Gamer.HardwareProfiling.Models;
using VoltrisOptimizer.Services.Gamer.Models;

namespace VoltrisOptimizer.Services.Gamer.Pipeline.Models
{
    public class GamerSessionContext
    {
        public string SessionId { get; set; } = Guid.NewGuid().ToString();
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;

        public string? GameExecutable { get; set; }
        public int? GameProcessId { get; set; }
        public string? GameProcessName { get; set; }

        public HardwareProfile Hardware { get; set; } = new();
        public SystemProfile System { get; set; } = new();
        public GameAnalysis Game { get; set; } = new();

        public GoalSet Goals { get; set; } = new();
        public PolicySet Policies { get; set; } = new();

        public List<OptimizationDecision> Decisions { get; set; } = new();
        public List<OptimizationResult> Results { get; set; } = new();
        public List<TelemetrySnapshot> TelemetryHistory { get; set; } = new();

        public GamerOptimizationOptions UserOptions { get; set; } = new();
    }

    public class HardwareProfile
    {
        public string CpuName { get; set; } = "";
        public int PhysicalCores { get; set; }
        public int LogicalCores { get; set; }
        public bool IsHybrid { get; set; }
        public int PerformanceCores { get; set; }
        public int EfficientCores { get; set; }
        public double MaxFrequencyMhz { get; set; }
        public bool HasVCache { get; set; }
        public bool IsThreadDirectorSupported { get; set; }

        public string GpuName { get; set; } = "";
        public string GpuVendor { get; set; } = "";
        public long GpuVideoMemoryMB { get; set; }
        public bool IsDiscreteGpu { get; set; }
        public bool SupportsHags { get; set; }
        public bool SupportsVrr { get; set; }

        public double TotalRamGB { get; set; }
        public double RamSpeedMhz { get; set; }
        public bool IsDualChannel { get; set; }

        public bool IsLaptop { get; set; }
        public bool IsOnBattery { get; set; }
        public string MachineClass { get; set; } = "";

        public string StorageType { get; set; } = "";
    }

    public class SystemProfile
    {
        public string WindowsVersion { get; set; } = "";
        public int BuildNumber { get; set; }
        public string ActivePowerPlan { get; set; } = "";
        public string ActivePowerPlanGuid { get; set; } = "";

        public double CpuUsage { get; set; }
        public double GpuUsage { get; set; }
        public double RamUsagePercent { get; set; }
        public double CpuTemperature { get; set; }
        public double GpuTemperature { get; set; }
        public double AvailableRamGB { get; set; }

        public int RunningProcessCount { get; set; }
        public int CpuQueueLength { get; set; }
        public double CurrentTimerResolutionMs { get; set; }

        public bool GameBarEnabled { get; set; }
        public bool GameDvrEnabled { get; set; }
        public bool HpetEnabled { get; set; }
        public bool CoreParkingEnabled { get; set; }
    }

    public class GameAnalysis
    {
        public string Name { get; set; } = "";
        public string ExecutablePath { get; set; } = "";
        public GameCategory Category { get; set; } = GameCategory.Unknown;
        public GameEngineType Engine { get; set; } = GameEngineType.Unknown;
        public int ConfidenceLevel { get; set; }

        public double CurrentFps { get; set; }
        public double CurrentFrameTimeMs { get; set; }
        public double CpuLoad { get; set; }
        public double GpuLoad { get; set; }
        public double MemoryUsageMB { get; set; }

        public string Api { get; set; } = "";

        public bool IsCpuBound => CpuLoad > 70 && GpuLoad < 50;
        public bool IsGpuBound => GpuLoad > 70 && CpuLoad < 50;
        public bool IsBalanced => Math.Abs(CpuLoad - GpuLoad) < 20;
    }

    public class GoalSet
    {
        public OptimizationPriority PrimaryGoal { get; set; } = OptimizationPriority.FpsStability;
        public OptimizationPriority SecondaryGoal { get; set; } = OptimizationPriority.Latency;
        public bool PrioritizeLatency { get; set; }
        public bool PrioritizeFpsStability { get; set; }
        public bool PrioritizeFramePacing { get; set; }
        public bool PrioritizeMemory { get; set; }
        public bool PrioritizeCpuThroughput { get; set; }
    }

    public enum OptimizationPriority
    {
        FpsStability,
        Latency,
        FramePacing,
        MemoryEfficiency,
        CpuThroughput,
        ThermalSafety,
        BatteryLife
    }

    public class PolicySet
    {
        public double CpuMaxTemp { get; set; } = 90;
        public double GpuMaxTemp { get; set; } = 85;
        public double MaxCpuUsage { get; set; } = 100;
        public double MaxRamUsagePercent { get; set; } = 95;
        public bool AllowProcessPriorityChanges { get; set; } = true;
        public bool AllowCpuAffinityChanges { get; set; } = false;
        public bool AllowGpuChanges { get; set; } = true;
        public bool AllowPowerPlanChanges { get; set; } = true;
        public bool AllowTimerResolutionChanges { get; set; } = true;
        public bool AllowKernelChanges { get; set; } = false;

        public static PolicySet Default() => new();
        public static PolicySet Conservative() => new()
        {
            AllowProcessPriorityChanges = true,
            AllowCpuAffinityChanges = false,
            AllowGpuChanges = false,
            AllowPowerPlanChanges = true,
            AllowTimerResolutionChanges = true,
            AllowKernelChanges = false
        };
        public static PolicySet Safe() => new()
        {
            AllowProcessPriorityChanges = false,
            AllowCpuAffinityChanges = false,
            AllowGpuChanges = false,
            AllowPowerPlanChanges = true,
            AllowTimerResolutionChanges = true,
            AllowKernelChanges = false
        };
    }

    public class OptimizationDecision
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Category { get; set; } = "";
        public double ExpectedBenefitScore { get; set; }
        public double RiskScore { get; set; }
        public bool HasRollback { get; set; } = true;
        public Func<CancellationToken, Task<bool>> ApplyAsync { get; set; } = _ => Task.FromResult(true);
        public Func<CancellationToken, Task<bool>> RollbackAsync { get; set; } = _ => Task.FromResult(true);
        public bool Applied { get; set; }
        public bool Validated { get; set; }
        public double MeasuredImprovementPercent { get; set; }
    }

    public class OptimizationResult
    {
        public string OptimizationId { get; set; } = "";
        public bool Success { get; set; }
        public double MeasuredFpsDelta { get; set; }
        public double MeasuredFrameTimeDelta { get; set; }
        public double MeasuredLatencyDelta { get; set; }
        public bool PassedValidation { get; set; }
        public string? FailureReason { get; set; }
        public DateTime AppliedAt { get; set; }
        public DateTime? RolledBackAt { get; set; }
    }

    public class TelemetrySnapshot
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public double Fps { get; set; }
        public double FrameTimeMs { get; set; }
        public double CpuUsage { get; set; }
        public double GpuUsage { get; set; }
        public double CpuTemperature { get; set; }
        public double GpuTemperature { get; set; }
        public double RamUsageGB { get; set; }
        public double VramUsageMB { get; set; }
        public double InputLatencyMs { get; set; }
        public double CpuClockMhz { get; set; }
        public double GpuClockMhz { get; set; }
    }
}
