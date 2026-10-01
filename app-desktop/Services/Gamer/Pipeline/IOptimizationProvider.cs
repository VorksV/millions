using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.Gamer.Pipeline.Models;

namespace VoltrisOptimizer.Services.Gamer.Pipeline
{
    public interface IOptimizationProvider
    {
        string Id { get; }
        string DisplayName { get; }
        string Category { get; }
        double BaseBenefitScore { get; }
        double BaseRiskScore { get; }
        bool HasRollback { get; }

        bool CanHandle(GamerSessionContext context);
        double CalculateBenefitScore(GamerSessionContext context);
        Task<bool> ApplyAsync(CancellationToken ct);
        Task<bool> RollbackAsync(CancellationToken ct);
    }
}
