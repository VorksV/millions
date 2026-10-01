using System.Threading.Tasks;
using VoltrisOptimizer.Services.Gamer.HardwareProfiling.Models;
using VoltrisOptimizer.Services.Gamer.GameCategorization.Models;
using VoltrisOptimizer.Services.Gamer.StrategyFactory.Models;

namespace VoltrisOptimizer.Services.Gamer.StrategyFactory.Pipelines
{
    public class NvidiaPipeline : IOptimizationPipeline
    {
        public bool CanHandle(AdvancedHardwareProfile hardware, GameProfileAnalysis game)
        {
            return hardware.Gpu.Vendor == GpuVendor.NVIDIA;
        }

        public Task ApplyToBlueprintAsync(OptimizationBlueprint blueprint, AdvancedHardwareProfile hardware, GameProfileAnalysis game)
        {
            return Task.Run(() =>
            {
                // MPO Disable (Multiplane Overlay) é altamente recomendado para NVIDIA série 30/40 para reduzir stuttering em Fullscreen Windowed
                if (hardware.Gpu.Tier >= GpuTier.MidRange)
                {
                    blueprint.ApprovedOptimizations.Add("DISABLE_MPO");
                }

                // HAGS (Hardware-Accelerated GPU Scheduling)
                // HAGS beneficia muito Cyberpunk e AAA (Geração de quadros, DLSS3), 
                // mas PODE adicionar Input Lag em jogos competitivos (CS2, Valorant).
                if (game.Category == GameCategory.AAA)
                {
                    if (hardware.Gpu.SupportsHags)
                        blueprint.ApprovedOptimizations.Add("ENABLE_HAGS");
                }
                else if (game.Category == GameCategory.Competitive)
                {
                    // Bloqueio explícito do HAGS para garantir 0.1% lows estáveis e menor input lag
                    blueprint.BlockedOptimizations.Add("ENABLE_HAGS");
                }

                // Reflex / Low Latency Mode (Via Driver Profile/Nvidia Inspector futuramente)
                if (game.Category == GameCategory.Competitive)
                {
                    blueprint.ApprovedOptimizations.Add("NVIDIA_LOW_LATENCY_ULTRA");
                }
            });
        }
    }
}
