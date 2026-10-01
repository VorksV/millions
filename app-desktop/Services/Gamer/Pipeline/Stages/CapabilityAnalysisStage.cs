using System;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Gamer.Pipeline.Models;

namespace VoltrisOptimizer.Services.Gamer.Pipeline.Stages
{
    public class CapabilityAnalysisStage : IPipelineStage
    {
        private readonly ILoggingService _logger;

        public string Name => "CapabilityAnalysis";
        public int Order => 3;

        public CapabilityAnalysisStage(ILoggingService logger)
        {
            _logger = logger;
        }

        public Task ExecuteAsync(GamerSessionContext context, CancellationToken ct)
        {
            _logger.LogEntry(nameof(ExecuteAsync));
            _logger.LogInfo($"[Pipeline] [GO] Estágio 3/11: CapabilityAnalysis — analisando capacidades do hardware...");

            var hw = context.Hardware;

            string tier = DetermineMachineTier(hw);
            _logger.LogInfo($"[Capability] Tier da máquina: {tier} | Núcleos: {hw.PhysicalCores}P+{hw.EfficientCores}E ({hw.LogicalCores}L)");

            if (hw.IsLaptop || (hw.LogicalCores <= 4 && hw.TotalRamGB <= 8))
            {
                _logger.LogInfo("[Capability] ⚠ Hardware de entrada — modo de baixo consumo ativado");
                context.Policies = PolicySet.Conservative();
            }
            else if (hw.IsLaptop && hw.LogicalCores <= 8 && hw.TotalRamGB <= 16)
            {
                _logger.LogInfo("[Capability] 💻 Notebook gamer de entrada — modo moderado");
                context.Policies = PolicySet.Default();
            }
            else if (hw.LogicalCores >= 12 && hw.GpuVideoMemoryMB >= 8000)
            {
                _logger.LogInfo("[Capability] 🚀 Desktop de alto desempenho — modo completo disponível");
                context.Policies = PolicySet.Default();
            }

            if (!hw.IsDiscreteGpu)
            {
                _logger.LogInfo("[Capability] ⚠ GPU integrada apenas — desativando otimizações de GPU");
                context.Policies.AllowGpuChanges = false;
            }

            if (hw.IsLaptop && context.System.ActivePowerPlan.Contains("Battery", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInfo("[Capability] ⚠ Notebook na bateria — modo conservador");
                context.Policies = PolicySet.Conservative();
                context.Goals.PrimaryGoal = OptimizationPriority.BatteryLife;
            }

            _logger.LogExit(nameof(ExecuteAsync));
            return Task.CompletedTask;
        }

        private static string DetermineMachineTier(HardwareProfile hw)
        {
            if (hw.LogicalCores >= 16 && hw.GpuVideoMemoryMB >= 12000 && hw.TotalRamGB >= 32)
                return "Enthusiast";
            if (hw.LogicalCores >= 8 && hw.GpuVideoMemoryMB >= 6000 && hw.TotalRamGB >= 16)
                return "HighEnd";
            if (hw.LogicalCores >= 6 && hw.GpuVideoMemoryMB >= 4000 && hw.TotalRamGB >= 8)
                return "MidRange";
            if (hw.LogicalCores >= 4 && hw.GpuVideoMemoryMB >= 2000)
                return "Entry";
            return "Budget";
        }
    }
}
