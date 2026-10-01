using System.Threading.Tasks;
using VoltrisOptimizer.Services.Gamer.HardwareProfiling.Models;
using VoltrisOptimizer.Services.Gamer.GameCategorization.Models;
using VoltrisOptimizer.Services.Gamer.StrategyFactory.Models;

namespace VoltrisOptimizer.Services.Gamer.StrategyFactory
{
    public interface IOptimizationPipeline
    {
        /// <summary>
        /// Define se este pipeline deve ser ativado baseado no Hardware e no Jogo.
        /// </summary>
        bool CanHandle(AdvancedHardwareProfile hardware, GameProfileAnalysis game);

        /// <summary>
        /// Aplica as regras do pipeline ao Blueprint.
        /// Adiciona ou bloqueia otimizações específicas.
        /// </summary>
        Task ApplyToBlueprintAsync(OptimizationBlueprint blueprint, AdvancedHardwareProfile hardware, GameProfileAnalysis game);
    }
}
