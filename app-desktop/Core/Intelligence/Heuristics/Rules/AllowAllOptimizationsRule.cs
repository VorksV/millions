using System;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.Intelligence.Heuristics.Abstractions;
using VoltrisOptimizer.Core.Intelligence.Heuristics.Models;

namespace VoltrisOptimizer.Core.Intelligence.Heuristics.Rules;

/// <summary>
/// Wildcard heuristic rule that grants a modest positive score to any optimization target.
/// This prevents the heuristic engine from defaulting to a DoNothing recommendation
/// (which the brain interprets as a veto) when no specific rules exist for an action.
/// </summary>
public sealed class AllowAllOptimizationsRule : IHeuristicRule
{
    public string RuleName => "AllowAllOptimizations";
    // Apply to any target optimization
    public string TargetOptimization => "*";

    public ValueTask<RuleResult> EvaluateAsync(HeuristicsContext context, CancellationToken cancellationToken = default)
    {
        // No heavy computation – immediate result.
        var result = new RuleResult
        {
            RuleName = RuleName,
            // Small positive score ensures finalRecommendation = Monitor (score 30 < 50)
            ScoreAdditive = 30,
            ConfidenceImpact = 0,
            Reason = "Default allow‑all heuristic – no veto for unknown optimizations.",
            Priority = RulePriority.Low,
            Severity = RuleSeverity.Info,
            Category = RuleCategory.UserPreference,
            EvaluationTime = TimeSpan.Zero
        };
        return ValueTask.FromResult(result);
    }
}
