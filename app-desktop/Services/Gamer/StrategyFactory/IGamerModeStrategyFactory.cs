using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.Gamer.HardwareProfiling.Models;
using VoltrisOptimizer.Services.Gamer.GameCategorization.Models;
using VoltrisOptimizer.Services.Gamer.StrategyFactory.Models;

namespace VoltrisOptimizer.Services.Gamer.StrategyFactory
{
    public interface IGamerModeStrategyFactory
    {
        Task<OptimizationBlueprint> GenerateBlueprintAsync(AdvancedHardwareProfile hardware, GameProfileAnalysis game, CancellationToken cancellationToken = default);
    }
}
