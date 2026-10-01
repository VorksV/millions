using System;
using System.Collections.Generic;

namespace VoltrisOptimizer.Services.Gamer.Audit
{
    public enum AuditCategory
    {
        CPU,
        GPU,
        RAM,
        Network,
        System,
        Process,
        Service,
        Registry,
        Game,
        Hardware,
        AI,
        Pipeline,
        Energy,
        Overlay,
        Explorer,
        DWM,
        GameMode,
        GameDVR,
        AdaptiveEngine,
        FluidityEngine,
        Telemetry,
        Audio
    }

    public enum AuditResult
    {
        SUCCESS,
        FAILED,
        IGNORED,
        NOT_SUPPORTED,
        BLOCKED_BY_WINDOWS,
        BLOCKED_BY_DRIVER,
        REVERTED_BY_WINDOWS,
        SKIPPED,
        PARTIAL
    }

    public enum ValidationStatus
    {
        NOT_VALIDATED,
        VALIDATED,
        VALIDATION_FAILED,
        NO_EFFECT,
        REVERTED
    }

    public class OptimizationAuditEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public AuditCategory Category { get; set; }
        public string Service { get; set; } = string.Empty;
        public string Class { get; set; } = string.Empty;
        public string Method { get; set; } = string.Empty;
        public string Optimization { get; set; } = string.Empty;
        public string SubStep { get; set; } = string.Empty;
        public int ThreadId { get; set; }
        public string SessionId { get; set; } = string.Empty;

        public AuditSnapshot Before { get; set; } = new();
        public AuditSnapshot After { get; set; } = new();
        public string ExpectedValue { get; set; } = string.Empty;
        public string ActualValue { get; set; } = string.Empty;

        public AuditResult Result { get; set; } = AuditResult.SUCCESS;
        public ValidationStatus Validation { get; set; } = ValidationStatus.NOT_VALIDATED;
        public long ExecutionTimeMs { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Exception { get; set; }
        public string? StackTrace { get; set; }

        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    public class AuditSnapshot
    {
        public string? PowerPlan { get; set; }
        public string? PowerPlanGuid { get; set; }
        public string? CoreParking { get; set; }
        public string? EPP { get; set; }
        public string? TimerResolution { get; set; }
        public string? HPET { get; set; }
        public string? HAGS { get; set; }
        public string? VRR { get; set; }
        public string? TDR { get; set; }
        public string? PowerMode { get; set; }
        public string? RSS { get; set; }
        public string? RSC { get; set; }
        public string? MTU { get; set; }
        public string? QoS { get; set; }
        public string? DSCP { get; set; }
        public string? InterruptModeration { get; set; }
        public string? TCPAutoTuning { get; set; }
        public string? TCPNoDelay { get; set; }
        public string? NetworkThrottlingIndex { get; set; }
        public string? GameMode { get; set; }
        public string? GameDVR { get; set; }
        public string? Wallpaper { get; set; }
        public string? VisualEffects { get; set; }
        public string? Telemetry { get; set; }
        public string? Explorer { get; set; }
        public string? DWM { get; set; }
        public string? MemoryFree { get; set; }
        public string? StandbyList { get; set; }
        public string? CacheSize { get; set; }
        public string? Scheduler { get; set; }
        public string? Priority { get; set; }
        public string? Affinity { get; set; }

        public Dictionary<string, string> Additional { get; set; } = new();
    }

    public class ServiceAuditEntry
    {
        public string ServiceName { get; set; } = string.Empty;
        public string? PreviousState { get; set; }
        public string? NewState { get; set; }
        public string? PreviousStartupType { get; set; }
        public string? NewStartupType { get; set; }
        public AuditResult Result { get; set; }
    }

    public class ProcessAuditEntry
    {
        public string ProcessName { get; set; } = string.Empty;
        public int ProcessId { get; set; }
        public double CpuUsage { get; set; }
        public double RamUsageMb { get; set; }
        public bool WasIgnored { get; set; }
        public bool WasProtected { get; set; }
        public bool WasTerminated { get; set; }
        public bool WasSuspended { get; set; }
        public string? Reason { get; set; }
    }

    public class RegistryAuditEntry
    {
        public string RegistryPath { get; set; } = string.Empty;
        public string ValueName { get; set; } = string.Empty;
        public string? PreviousValue { get; set; }
        public string? NewValue { get; set; }
        public string ValueType { get; set; } = string.Empty;
        public AuditResult Result { get; set; }
    }

    public class GameAuditEntry
    {
        public string GameName { get; set; } = string.Empty;
        public string ExecutablePath { get; set; } = string.Empty;
        public int ProcessId { get; set; }
        public string? DetectedEngine { get; set; }
        public string? Launcher { get; set; }
        public string? ProfileUsed { get; set; }
    }

    public class HardwareAuditEntry
    {
        public string Cpu { get; set; } = string.Empty;
        public string Gpu { get; set; } = string.Empty;
        public int RamGb { get; set; }
        public int VramMb { get; set; }
        public string Disk { get; set; } = string.Empty;
        public bool IsSsd { get; set; }
        public string Monitor { get; set; } = string.Empty;
        public string WindowsVersion { get; set; } = string.Empty;
        public string WindowsBuild { get; set; } = string.Empty;
        public string DriverVersion { get; set; } = string.Empty;
    }

    public class AIAuditEntry
    {
        public string ProfileChosen { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public List<string> OptimizationsEnabled { get; set; } = new();
        public List<string> OptimizationsDisabled { get; set; } = new();
    }

    public class PipelineStageAuditEntry
    {
        public string StageName { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public long DurationMs => (long)(EndTime - StartTime).TotalMilliseconds;
        public AuditResult Result { get; set; }
        public string? Exception { get; set; }
    }

    public class ConsolidatedAuditReport
    {
        public string ReportId { get; set; } = Guid.NewGuid().ToString("N")[..12];
        public DateTime GeneratedAt { get; set; } = DateTime.Now;
        public DateTime SessionStart { get; set; }
        public DateTime SessionEnd { get; set; }
        public TimeSpan SessionDuration => SessionEnd - SessionStart;
        public string? GameName { get; set; }
        public int? GameProcessId { get; set; }

        public List<OptimizationAuditEntry> Optimizations { get; set; } = new();
        public List<ServiceAuditEntry> Services { get; set; } = new();
        public List<ProcessAuditEntry> Processes { get; set; } = new();
        public List<RegistryAuditEntry> RegistryChanges { get; set; } = new();
        public List<GameAuditEntry> Games { get; set; } = new();
        public HardwareAuditEntry? Hardware { get; set; }
        public List<AIAuditEntry> AIEntries { get; set; } = new();
        public List<PipelineStageAuditEntry> PipelineStages { get; set; } = new();

        public AuditStatistics Stats { get; set; } = new();
    }

    public class AuditStatistics
    {
        public int TotalOptimizations { get; set; }
        public int Successful { get; set; }
        public int Failed { get; set; }
        public int Ignored { get; set; }
        public int NotSupported { get; set; }
        public int BlockedByWindows { get; set; }
        public int BlockedByDriver { get; set; }
        public int Reverted { get; set; }
        public int Validated { get; set; }
        public int ValidationFailed { get; set; }
        public double SuccessRate => TotalOptimizations > 0 ? (double)Successful / TotalOptimizations * 100 : 0;
        public double ValidationRate => TotalOptimizations > 0 ? (double)Validated / TotalOptimizations * 100 : 0;
        public double EffectivenessRate => TotalOptimizations > 0
            ? (double)(Successful + Validated) / (TotalOptimizations * 2) * 100
            : 0;

        public Dictionary<AuditCategory, CategoryStats> ByCategory { get; set; } = new();
    }

    public class CategoryStats
    {
        public AuditCategory Category { get; set; }
        public int Total { get; set; }
        public int Success { get; set; }
        public int Failed { get; set; }
        public int Validated { get; set; }
        public long TotalExecutionTimeMs { get; set; }
        public double AverageExecutionTimeMs => Total > 0 ? (double)TotalExecutionTimeMs / Total : 0;
    }
}
