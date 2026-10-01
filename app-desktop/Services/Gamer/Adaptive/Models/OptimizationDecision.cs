using System.Collections.Generic;
using System.Linq;

namespace VoltrisOptimizer.Services.Gamer.Adaptive.Models
{
    /// <summary>
    /// Decisão sobre uma otimização específica
    /// </summary>
    public class OptimizationDecision
    {
        public OptimizationType Type { get; set; }
        public OptimizationAction Action { get; set; } = OptimizationAction.SKIP;
        public string Reason { get; set; } = string.Empty;
        public int Priority { get; set; } = 0; // Higher = apply first
        public RiskLevel Risk { get; set; } = RiskLevel.LOW;
        public Dictionary<string, object> Parameters { get; set; } = new();
    }

    /// <summary>
    /// Plano de execução completo
    /// </summary>
    public class ExecutionPlan
    {
        public string SessionId { get; set; } = string.Empty;
        public HardwareProfile Hardware { get; set; } = new();
        public GameProfile Game { get; set; } = new();
        public List<OptimizationDecision> Decisions { get; set; } = new();
        
        public int ApplyCount => Decisions.Count(d => d.Action == OptimizationAction.APPLY);
        public int SkipCount => Decisions.Count(d => d.Action == OptimizationAction.SKIP);
        public int ConditionalCount => Decisions.Count(d => d.Action == OptimizationAction.CONDITIONAL);
        
        public Dictionary<string, object> Context { get; set; } = new();
    }

    public enum OptimizationType
    {
        TimerResolution,
        PowerPlan,
        CpuAffinity,
        MemoryOptimization,
        GpuHags,
        GpuLowLatency,
        GpuPowerMode,
        NetworkOptimization,
        WindowsServiceOptimization,
        SchedulerSuspension,
        ProcessPriority,
        DisplayOptimization,
        AudioOptimization
    }

    public enum OptimizationAction
    {
        APPLY,          // Apply this optimization
        SKIP,           // Skip this optimization
        CONDITIONAL     // Apply conditionally (with monitoring)
    }

    public enum RiskLevel
    {
        LOW,            // Safe, reversible, tested
        MEDIUM,         // Generally safe but may have side effects
        HIGH            // Risky, requires special handling
    }
}
