using System;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Gamer.Pipeline.Models;

namespace VoltrisOptimizer.Services.Gamer.Pipeline.Stages
{
    public class ContextStage : IPipelineStage
    {
        private readonly ILoggingService _logger;

        public string Name => "Context";
        public int Order => 2;

        public ContextStage(ILoggingService logger)
        {
            _logger = logger;
        }

        public Task ExecuteAsync(GamerSessionContext context, CancellationToken ct)
        {
            _logger.LogEntry(nameof(ExecuteAsync));
            _logger.LogInfo($"[Pipeline] [GO] Estágio 2/11: Context — montando contexto de execução...");

            context.GameProcessName ??= context.GameExecutable != null
                ? System.IO.Path.GetFileNameWithoutExtension(context.GameExecutable)
                : null;

            _logger.LogInfo($"[Context] Hardware: {context.Hardware.CpuName} | {context.Hardware.GpuName} | {context.Hardware.TotalRamGB:F0}GB RAM");
            _logger.LogInfo($"[Context] Sistema: {context.System.WindowsVersion} | Plano de energia: {context.System.ActivePowerPlan}");
            _logger.LogInfo($"[Context] Jogo: {context.GameProcessName ?? "N/A"} | Categoria: {context.Game.Category} | Engine: {context.Game.Engine}");

            _logger.LogExit(nameof(ExecuteAsync));
            return Task.CompletedTask;
        }
    }
}
