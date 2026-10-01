using System;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Gamer.GameCategorization.Models;
using VoltrisOptimizer.Services.Gamer.Pipeline.Models;

namespace VoltrisOptimizer.Services.Gamer.Pipeline.Stages
{
    public class GoalResolutionStage : IPipelineStage
    {
        private readonly ILoggingService _logger;

        public string Name => "GoalResolution";
        public int Order => 5;

        public GoalResolutionStage(ILoggingService logger)
        {
            _logger = logger;
        }

        public Task ExecuteAsync(GamerSessionContext context, CancellationToken ct)
        {
            _logger.LogEntry(nameof(ExecuteAsync));
            _logger.LogInfo($"[Pipeline] [GO] Estágio 5/11: GoalResolution — resolvendo objetivos...");

            var goals = context.Goals;
            var game = context.Game;
            var hw = context.Hardware;

            switch (game.Category)
            {
                case GameCategory.Competitive:
                    goals.PrimaryGoal = OptimizationPriority.Latency;
                    goals.SecondaryGoal = OptimizationPriority.FramePacing;
                    goals.PrioritizeLatency = true;
                    _logger.LogInfo("[Goals] 🎯 FPS Competitivo → prioridade: LATÊNCIA + FRAME PACING");
                    break;

                case GameCategory.AAA:
                    goals.PrimaryGoal = OptimizationPriority.FpsStability;
                    goals.SecondaryGoal = OptimizationPriority.ThermalSafety;
                    goals.PrioritizeFpsStability = true;
                    _logger.LogInfo("[Goals] 🎯 AAA → prioridade: ESTABILIDADE FPS + SEGURANÇA TÉRMICA");
                    break;

                case GameCategory.Simulation:
                    goals.PrimaryGoal = OptimizationPriority.CpuThroughput;
                    goals.SecondaryGoal = OptimizationPriority.MemoryEfficiency;
                    goals.PrioritizeCpuThroughput = true;
                    _logger.LogInfo("[Goals] 🎯 Simulação → prioridade: THROUGHPUT CPU + MEMÓRIA");
                    break;

                case GameCategory.Lightweight:
                    goals.PrimaryGoal = OptimizationPriority.BatteryLife;
                    goals.SecondaryGoal = OptimizationPriority.ThermalSafety;
                    _logger.LogInfo("[Goals] 🎯 Jogo leve → prioridade: BATERIA + SEGURANÇA TÉRMICA");
                    break;

                default:
                    if (hw.IsLaptop && context.System.ActivePowerPlan.Contains("Battery", StringComparison.OrdinalIgnoreCase))
                    {
                        goals.PrimaryGoal = OptimizationPriority.BatteryLife;
                    }
                    else if (game.Engine == GameEngineType.Source2 || game.Engine == GameEngineType.Frostbite)
                    {
                        goals.PrimaryGoal = OptimizationPriority.Latency;
                        goals.SecondaryGoal = OptimizationPriority.FramePacing;
                    }
                    else
                    {
                        goals.PrimaryGoal = OptimizationPriority.FpsStability;
                        goals.SecondaryGoal = OptimizationPriority.Latency;
                    }
                    _logger.LogInfo($"[Goals] 🎯 Desconhecido → prioridade: {goals.PrimaryGoal}");
                    break;
            }

            _logger.LogExit(nameof(ExecuteAsync));
            return Task.CompletedTask;
        }
    }
}
