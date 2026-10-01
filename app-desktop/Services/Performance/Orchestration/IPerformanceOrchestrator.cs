using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.Performance.Decision.Models;
using VoltrisOptimizer.Services.Performance.Orchestration.Models;

namespace VoltrisOptimizer.Services.Performance.Orchestration
{
    /// <summary>
    /// Interface for performance orchestrator.
    /// Coordinates decision execution across existing services.
    /// </summary>
    public interface IPerformanceOrchestrator
    {
        /// <summary>
        /// Executes a performance decision by delegating to existing services.
        /// Includes stability window and deduplication checks.
        /// </summary>
        /// <param name="decision">The decision to execute</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Result of orchestration</returns>
        Task<OrchestrationResult> ExecuteDecisionAsync(
            PerformanceDecision decision,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Reverts all active optimizations.
        /// </summary>
        Task<OrchestrationResult> RevertAllAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets current orchestration state (for diagnostics).
        /// </summary>
        string GetCurrentState();
    }
}
