using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Gamer.OBrain;
using VoltrisOptimizer.Services.Gamer.Pipeline.Models;

namespace VoltrisOptimizer.Services.Gamer.Pipeline.Stages
{
    public class PerceptionStage : IPipelineStage
    {
        private readonly IOBrain _obrain;
        private readonly ILoggingService _logger;

        public string Name => "Perception";
        public int Order => 1;

        public PerceptionStage(IOBrain obrain, ILoggingService logger)
        {
            _obrain = obrain;
            _logger = logger;
        }

        public async Task ExecuteAsync(GamerSessionContext context, CancellationToken ct)
        {
            _logger.LogEntry(nameof(ExecuteAsync));
            _logger.LogInfo($"[Pipeline] [GO] Estágio 1/11: Perception — observando ambiente...");
            context.Hardware = await _obrain.ProfileHardwareAsync(ct);
            context.System = await _obrain.ProfileSystemAsync(ct);
            _logger.LogExit(nameof(ExecuteAsync));
        }
    }
}
