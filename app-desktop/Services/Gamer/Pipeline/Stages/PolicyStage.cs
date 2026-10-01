using System;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Gamer.Pipeline.Models;

namespace VoltrisOptimizer.Services.Gamer.Pipeline.Stages
{
    public class PolicyStage : IPipelineStage
    {
        private readonly ILoggingService _logger;

        public string Name => "PolicyEvaluation";
        public int Order => 6;

        public PolicyStage(ILoggingService logger)
        {
            _logger = logger;
        }

        public Task ExecuteAsync(GamerSessionContext context, CancellationToken ct)
        {
            _logger.LogEntry(nameof(ExecuteAsync));
            _logger.LogInfo($"[Pipeline] [GO] Estágio 6/11: PolicyEvaluation — avaliando políticas de segurança...");

            var hw = context.Hardware;
            var policies = context.Policies;

            if (hw.IsLaptop && hw.IsOnBattery)
            {
                policies.AllowPowerPlanChanges = true;
                policies.AllowGpuChanges = false;
                policies.AllowKernelChanges = false;
                policies.AllowProcessPriorityChanges = false;
                policies.AllowCpuAffinityChanges = false;
                _logger.LogInfo("[Policy] 💻 Notebook na bateria: políticas restritivas ativadas");
            }

            if (hw.CpuName.Contains("Intel", StringComparison.OrdinalIgnoreCase) && hw.IsHybrid)
            {
                policies.AllowCpuAffinityChanges = true;
                _logger.LogInfo("[Policy] 🔄 CPU híbrida Intel: ajuste de afinidade permitido");
            }

            if (context.Goals.PrimaryGoal == OptimizationPriority.ThermalSafety)
            {
                policies.CpuMaxTemp = 85;
                policies.GpuMaxTemp = 80;
                _logger.LogInfo("[Policy] 🌡 Modo segurança térmica: limites reduzidos (CPU:85°C GPU:80°C)");
            }

            _logger.LogInfo($"[Policy] Políticas finais: PowerPlan={policies.AllowPowerPlanChanges} GPU={policies.AllowGpuChanges} Priority={policies.AllowProcessPriorityChanges} Kernel={policies.AllowKernelChanges} CPUAffinity={policies.AllowCpuAffinityChanges} TimerRes={policies.AllowTimerResolutionChanges}");

            _logger.LogExit(nameof(ExecuteAsync));
            return Task.CompletedTask;
        }
    }
}
