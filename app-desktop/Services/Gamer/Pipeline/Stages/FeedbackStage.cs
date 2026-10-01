using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Gamer.Pipeline.Models;

namespace VoltrisOptimizer.Services.Gamer.Pipeline.Stages
{
    public class FeedbackStage : IPipelineStage
    {
        private readonly ILoggingService _logger;

        public string Name => "Feedback";
        public int Order => 11;

        public FeedbackStage(ILoggingService logger)
        {
            _logger = logger;
        }

        public Task ExecuteAsync(GamerSessionContext context, CancellationToken ct)
        {
            _logger.LogEntry(nameof(ExecuteAsync));
            _logger.LogInfo($"[Pipeline] [GO] Estágio 11/11: Feedback — sessão concluída.");

            _logger.LogSuccess($"""
[Sessão Finalizada]
═══════════════════════════════
Jogo: {context.Game.Name}
Hardware: {context.Hardware.CpuName} | {context.Hardware.GpuName}
Duração: {context.StartedAt:HH:mm:ss}
Otimizações: {context.Decisions.Count(d => d.Applied)} aplicadas, {context.Results.Count(r => r.PassedValidation)} validadas
Placebos evitados: {context.Results.Count(r => !r.PassedValidation)}
═══════════════════════════════
""");

            _logger.LogExit(nameof(ExecuteAsync));
            return Task.CompletedTask;
        }
    }
}
