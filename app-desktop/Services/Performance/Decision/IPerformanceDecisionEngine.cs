using VoltrisOptimizer.Services.Performance.Decision.Models;

namespace VoltrisOptimizer.Services.Performance.Decision
{
    /// <summary>
    /// Decision engine interface - evaluates context and produces decisions.
    /// NO execution, NO side effects, NO state mutation.
    /// Pure function: Context → Decision
    /// </summary>
    public interface IPerformanceDecisionEngine
    {
        /// <summary>
        /// Evaluates the current performance context and produces a decision.
        /// This method is deterministic and stateless.
        /// </summary>
        /// <param name="context">Current system state snapshot</param>
        /// <returns>Decision describing recommended actions</returns>
        PerformanceDecision Decide(PerformanceContext context);
    }
}
