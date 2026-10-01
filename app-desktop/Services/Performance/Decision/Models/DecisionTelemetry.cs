using System;

namespace VoltrisOptimizer.Services.Performance.Decision.Models
{
    /// <summary>
    /// Telemetry model for decision tracing (structure only).
    /// NO active collection, NO network calls, NO side effects.
    /// Future use: tracking decision effectiveness.
    /// </summary>
    public sealed class DecisionTelemetry
    {
        // Trace Identifiers
        public string TraceId { get; }
        public string DecisionId { get; }
        public string ContextId { get; }

        // Timestamps
        public DateTime DecisionMadeAt { get; }
        public DateTime? ExecutionStartedAt { get; set; }
        public DateTime? ExecutionCompletedAt { get; set; }

        // Source Information
        public string SourceEngine { get; }
        public string SourceVersion { get; }

        // Decision Details
        public DecisionType DecisionType { get; }
        public DecisionIntensity Intensity { get; }
        public DecisionConfidence Confidence { get; }

        // Execution Results (to be filled later)
        public bool? WasExecuted { get; set; }
        public bool? WasSuccessful { get; set; }
        public string? ExecutionError { get; set; }

        // Performance Impact (to be measured later)
        public double? CpuImpactPercent { get; set; }
        public double? MemoryImpactPercent { get; set; }
        public double? ResponseTimeImpactMs { get; set; }

        public DecisionTelemetry(
            string decisionId,
            string contextId,
            DateTime decisionMadeAt,
            string sourceEngine,
            DecisionType decisionType,
            DecisionIntensity intensity,
            DecisionConfidence confidence)
        {
            TraceId = Guid.NewGuid().ToString("N");
            DecisionId = decisionId ?? throw new ArgumentNullException(nameof(decisionId));
            ContextId = contextId ?? throw new ArgumentNullException(nameof(contextId));
            DecisionMadeAt = decisionMadeAt;
            SourceEngine = sourceEngine ?? "Unknown";
            SourceVersion = "1.0.0"; // TODO: Get from assembly version
            DecisionType = decisionType;
            Intensity = intensity;
            Confidence = confidence;
        }

        /// <summary>
        /// Creates telemetry from a decision
        /// </summary>
        public static DecisionTelemetry FromDecision(PerformanceDecision decision, string sourceEngine = "RuleBasedEngine")
        {
            return new DecisionTelemetry(
                decision.DecisionId,
                decision.ContextId,
                decision.DecidedAt,
                sourceEngine,
                decision.Type,
                decision.Intensity,
                decision.Confidence);
        }

        public override string ToString() =>
            $"Telemetry[{TraceId}] {DecisionType} | Executed:{WasExecuted} Success:{WasSuccessful}";
    }
}
