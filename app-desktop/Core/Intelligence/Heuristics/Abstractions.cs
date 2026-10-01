using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Core.Intelligence.Heuristics.Models
{
    public enum RulePriority { Lowest = 0, Low = 1, Normal = 2, High = 3, Critical = 4 }
    public enum RuleSeverity { Info = 0, Warning = 1, Severe = 2, Blocking = 3 }
    public enum RuleCategory { HardwareCapability = 0, RealTimeTelemetry = 1, UserPreference = 2, HistoricalData = 3, Security = 4 }
    public enum DecisionRecommendation { DoNothing = 0, Monitor = 1, PauseTemporarily = 2, Enable = 3, DisablePermanently = 4 }

    public class RuleResult
    {
        public string RuleName { get; init; } = string.Empty;
        public double ScoreMultiplier { get; init; } = 1.0;
        public double ScoreAdditive { get; init; } = 0.0;
        public int ConfidenceImpact { get; init; } = 0;
        public string Reason { get; init; } = string.Empty;
        public bool IsVeto { get; init; } = false; // Veto forçará DoNothing independentemente do score
        public bool IsCriticalEnforcement { get; init; } = false; // Forçará a ação (ex: disco mecânico a 100%)
        public RulePriority Priority { get; init; } = RulePriority.Normal;
        public RuleSeverity Severity { get; init; } = RuleSeverity.Info;
        public RuleCategory Category { get; init; } = RuleCategory.HardwareCapability;
        public TimeSpan EvaluationTime { get; init; } = TimeSpan.Zero;

        public static RuleResult Neutral(string ruleName) => new RuleResult { RuleName = ruleName, ScoreMultiplier = 1.0 };
    }

    public class DecisionAuditTrail
    {
        public TimeSpan TotalEvaluationTime { get; init; }
        public int EvaluatedRulesCount { get; init; }
        public IReadOnlyList<RuleResult>? ExecutedRules { get; init; }
        public IReadOnlyList<string>? ConflictResolutions { get; init; }
        // JSON reduzido contendo o hardware/telemetria no momento da decisão para futuro Machine Learning
        public string ExecutionContextSnapshot { get; init; } = string.Empty; 
    }

    public class OptimizationDecision
    {
        public string OptimizationTarget { get; init; } = string.Empty;
        public double FinalScore { get; init; }
        public int FinalConfidence { get; init; }
        public DecisionRecommendation Recommendation { get; init; }
        public DecisionAuditTrail? AuditTrail { get; init; }
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
        
        public bool IsActionable => Recommendation == DecisionRecommendation.PauseTemporarily || Recommendation == DecisionRecommendation.DisablePermanently;
    }
}

namespace VoltrisOptimizer.Core.Intelligence.Heuristics.Abstractions
{
    public class HeuristicsContext
    {
        // Snapshot em readonly para evitar manipulação por regras
        public VoltrisOptimizer.Core.Body.SystemHealthState? HealthState { get; init; }
        public VoltrisOptimizer.Services.Hardware.HardwareTelemetryMetrics? Telemetry { get; init; }
        public VoltrisOptimizer.Services.SystemIntelligenceProfiler.HardwareProfile? HardwareProfile { get; init; }
        public string TargetOptimization { get; init; } = string.Empty;
    }

    public interface IHeuristicRule
    {
        string RuleName { get; }
        string TargetOptimization { get; } 
        
        // Uso de ValueTask para zero allocation caso a regra seja puramente em RAM e síncrona
        ValueTask<Models.RuleResult> EvaluateAsync(HeuristicsContext context, CancellationToken cancellationToken = default);
    }

    public interface IHeuristicsPipeline
    {
        Task<Models.OptimizationDecision> EvaluateOptimizationAsync(string targetOptimization, HeuristicsContext context, CancellationToken cancellationToken = default);
    }
}
