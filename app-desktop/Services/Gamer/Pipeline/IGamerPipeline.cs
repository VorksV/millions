using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.Gamer.Models;
using VoltrisOptimizer.Services.Gamer.Pipeline.Models;

namespace VoltrisOptimizer.Services.Gamer.Pipeline
{
    public interface IGamerPipeline
    {
        Task<GamerSessionContext> ExecuteAsync(
            string? gameExecutable,
            GamerOptimizationOptions userOptions,
            CancellationToken ct = default);
    }

    public interface IPipelineStage
    {
        string Name { get; }
        int Order { get; }
        Task ExecuteAsync(GamerSessionContext context, CancellationToken ct);
    }
}
