using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Gamer.OBrain;
using VoltrisOptimizer.Services.Gamer.Pipeline.Models;

namespace VoltrisOptimizer.Services.Gamer.Pipeline.Stages
{
    public class GameAnalysisStage : IPipelineStage
    {
        private readonly IOBrain _obrain;
        private readonly ILoggingService _logger;

        public string Name => "GameAnalysis";
        public int Order => 4;

        public GameAnalysisStage(IOBrain obrain, ILoggingService logger)
        {
            _obrain = obrain;
            _logger = logger;
        }

        public async Task ExecuteAsync(GamerSessionContext context, CancellationToken ct)
        {
            _logger.LogEntry(nameof(ExecuteAsync));
            _logger.LogInfo($"[Pipeline] [GO] Estágio 4/11: GameAnalysis — analisando jogo...");

            if (context.GameExecutable != null)
            {
                context.Game = await _obrain.ProfileGameAsync(context.GameExecutable, ct);
            }
            else if (context.GameProcessName != null)
            {
                context.Game.Name = context.GameProcessName;
            }

            if (context.Game.Category == GameCategorization.Models.GameCategory.Unknown)
            {
                _logger.LogInfo($"[GameAnalysis] ⚠ Categoria não detectada — assumindo AAA");
            }
            _logger.LogExit(nameof(ExecuteAsync));
        }
    }
}
