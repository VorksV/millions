using System;
using System.Collections.Generic;

namespace VoltrisOptimizer.Services.Performance.Decision.Models
{
    /// <summary>
    /// Read-only decision output from the decision engine.
    /// Describes WHAT should be done, not HOW to do it.
    /// NO execution logic, NO side effects.
    /// </summary>
    public sealed class PerformanceDecision
    {
        // Decision Metadata
        public string DecisionId { get; }
        public DateTime DecidedAt { get; }
        public string ContextId { get; }

        // Decision Type
        public DecisionType Type { get; }
        public DecisionIntensity Intensity { get; }
        public DecisionConfidence Confidence { get; }

        // Reasoning
        public string PrimaryReason { get; }
        public IReadOnlyList<string> ContributingFactors { get; }

        // Recommended Execution Targets
        public OptimizationTargets AllowedTargets { get; }

        // Priority & Timing
        public DecisionPriority Priority { get; }
        public TimeSpan? SuggestedDuration { get; }

        // Metadata for telemetry (future use)
        public IReadOnlyDictionary<string, object>? Metadata { get; }

        public PerformanceDecision(
            string contextId,
            DecisionType type,
            DecisionIntensity intensity,
            DecisionConfidence confidence,
            string primaryReason,
            IReadOnlyList<string> contributingFactors,
            OptimizationTargets allowedTargets,
            DecisionPriority priority = DecisionPriority.Normal,
            TimeSpan? suggestedDuration = null,
            IReadOnlyDictionary<string, object>? metadata = null)
        {
            if (string.IsNullOrWhiteSpace(contextId))
                throw new ArgumentException("Context ID cannot be empty", nameof(contextId));
            if (string.IsNullOrWhiteSpace(primaryReason))
                throw new ArgumentException("Primary reason cannot be empty", nameof(primaryReason));

            DecisionId = Guid.NewGuid().ToString("N");
            DecidedAt = DateTime.UtcNow;
            ContextId = contextId;
            Type = type;
            Intensity = intensity;
            Confidence = confidence;
            PrimaryReason = primaryReason;
            ContributingFactors = contributingFactors ?? Array.Empty<string>();
            AllowedTargets = allowedTargets ?? new OptimizationTargets();
            Priority = priority;
            SuggestedDuration = suggestedDuration;
            Metadata = metadata;
        }

        /// <summary>
        /// Creates a "no action needed" decision
        /// </summary>
        public static PerformanceDecision NoAction(string contextId, string reason) =>
            new PerformanceDecision(
                contextId,
                DecisionType.NoAction,
                DecisionIntensity.None,
                DecisionConfidence.High,
                reason,
                Array.Empty<string>(),
                new OptimizationTargets { AllowAny = false },
                DecisionPriority.Low);

        public override string ToString() =>
            $"Decision[{DecisionId}] {Type} ({Intensity}) | Confidence:{Confidence} | {PrimaryReason}";
    }


    /// <summary>
    /// Type of decision made
    /// </summary>
    public enum DecisionType
    {
        NoAction,               // System is optimal, do nothing
        ApplySystemTweaks,      // Apply registry/service optimizations
        EnableGamerMode,        // Activate gaming optimizations
        EnableDynamicBalancing, // Start dynamic load stabilizer
        ReduceIntensity,        // Scale back optimizations
        Revert,                 // Rollback optimizations
        Hybrid                  // Combination of multiple strategies
    }

    /// <summary>
    /// Intensity of optimization to apply
    /// </summary>
    public enum DecisionIntensity
    {
        None,       // No optimization
        Light,      // Conservative, minimal impact
        Moderate,   // Balanced approach
        Aggressive, // Maximum performance, higher risk
        Custom      // Specific configuration
    }

    /// <summary>
    /// Confidence level in the decision
    /// </summary>
    public enum DecisionConfidence
    {
        Low,        // 0-40% confidence
        Medium,     // 41-70% confidence
        High,       // 71-100% confidence
        Certain     // 100% confidence (deterministic)
    }

    /// <summary>
    /// Priority of the decision
    /// </summary>
    public enum DecisionPriority
    {
        Low,        // Can be deferred
        Normal,     // Standard priority
        High,       // Should be applied soon
        Critical    // Immediate action recommended
    }

    /// <summary>
    /// Explicit toggles for the execution layer.
    /// What is authorized by the Intelligence Layer.
    /// </summary>
    public sealed class OptimizationTargets
    {
        public bool AllowAny { get; set; } = true;
        
        // Resource Toggles
        public bool CpuPriority { get; set; }
        public bool CpuAffinity { get; set; }
        public bool IoPriority { get; set; }
        public bool MemoryPriority { get; set; }
        
        // System Toggles
        public bool ServiceQoS { get; set; }
        public bool LaunchBoost { get; set; }
        public bool TimerResolution { get; set; }
        
        // Dynamic Policies
        public bool EnableDynamicBalancing { get; set; }
        public bool EnableThermalThrottlingProtection { get; set; }
    }
}
