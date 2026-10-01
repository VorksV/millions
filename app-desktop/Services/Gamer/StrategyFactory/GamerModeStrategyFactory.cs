using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Gamer.HardwareProfiling.Models;
using VoltrisOptimizer.Services.Gamer.GameCategorization.Models;
using VoltrisOptimizer.Services.Gamer.StrategyFactory.Models;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer.StrategyFactory
{
    public class GamerModeStrategyFactory : IGamerModeStrategyFactory
    {
        private readonly ILoggingService _logger;
        private readonly IEnumerable<IOptimizationPipeline> _pipelines;

        public GamerModeStrategyFactory(ILoggingService logger, IEnumerable<IOptimizationPipeline> pipelines)
        {
            _logger.LogEntry(nameof(GamerModeStrategyFactory));
            _logger = logger;
            _pipelines = pipelines;
            _logger.LogExit(nameof(GamerModeStrategyFactory));
        }

        public async Task<OptimizationBlueprint> GenerateBlueprintAsync(AdvancedHardwareProfile hardware, GameProfileAnalysis game, CancellationToken cancellationToken = default)
        {
            _logger.LogEntry(nameof(GenerateBlueprintAsync));
            _logger.LogInfo($"[StrategyFactory] Gerando Blueprint Adaptativo para {game.GameName} ({game.Category}) no hardware {hardware.MachineClass}");
            
            var blueprint = new OptimizationBlueprint();
            CalculatePerformanceScore(blueprint.Score, hardware);

            // Filtra pipelines que se aplicam a este contexto (Hardware + Jogo)
            var activePipelines = _pipelines.Where(p => p.CanHandle(hardware, game)).ToList();

            foreach (var pipeline in activePipelines)
            {
                _logger.LogDebug($"[StrategyFactory] Aplicando {pipeline.GetType().Name}...");
                await pipeline.ApplyToBlueprintAsync(blueprint, hardware, game);
            }

            // Resolve conflitos (Ex: Pipeline A aprovou, Pipeline B bloqueou -> Bloqueio ganha)
            var toRemove = new List<string>();
            foreach (var approved in blueprint.ApprovedOptimizations)
            {
                if (blueprint.BlockedOptimizations.Contains(approved))
                {
                    _logger.LogWarning($"[StrategyFactory] Conflito resolvido: {approved} foi bloqueado por uma regra superior.");
                    toRemove.Add(approved);
                }
            }

            foreach (var r in toRemove)
                blueprint.ApprovedOptimizations.Remove(r);

            var logDetails = $@"
[STRATEGY FACTORY - OPTIMIZATION BLUEPRINT]
==================================================
Score do Sistema: {blueprint.Score.TotalScore} pontos ({blueprint.Score.Tier})
 - CPU Score: {blueprint.Score.CpuScore} | GPU Score: {blueprint.Score.GpuScore}
 - RAM Score: {blueprint.Score.MemoryScore} | SSD Score: {blueprint.Score.StorageScore}
---
Otimizações APROVADAS ({blueprint.ApprovedOptimizations.Count}):
{string.Join("\n", blueprint.ApprovedOptimizations.Select(o => " [+] " + o))}
---
Otimizações BLOQUEADAS ({blueprint.BlockedOptimizations.Count}):
{string.Join("\n", blueprint.BlockedOptimizations.Select(o => " [-] " + o))}
==================================================";

            _logger.LogSuccess(logDetails);
            _logger.LogExit(nameof(GenerateBlueprintAsync));
            return blueprint;
        }

        private void CalculatePerformanceScore(PerformanceScore score, AdvancedHardwareProfile hardware)
        {
            _logger.LogEntry(nameof(CalculatePerformanceScore));
            // CPU Score
            score.CpuScore = (hardware.Cpu.PhysicalCores * 10) + (int)(hardware.Cpu.MaxFrequencyMHz / 100);
            if (hardware.Cpu.HasVCache) score.CpuScore += 50;
            if (hardware.Cpu.IsThreadDirectorSupported) score.CpuScore += 30;

            // GPU Score
            switch (hardware.Gpu.Tier)
            {
                case GpuTier.Enthusiast: score.GpuScore = 400; break;
                case GpuTier.HighEnd: score.GpuScore = 300; break;
                case GpuTier.MidRange: score.GpuScore = 150; break;
                default: score.GpuScore = 50; break;
            }

            // RAM Score
            score.MemoryScore = (int)(hardware.Memory.TotalCapacityMB / 100);
            if (hardware.Memory.IsDualChannel) score.MemoryScore += 50;

            // Storage Score
            switch (hardware.PrimaryStorage.Tier)
            {
                case StorageTier.NVMeGen5: score.StorageScore = 200; break;
                case StorageTier.NVMeGen4: score.StorageScore = 150; break;
                case StorageTier.NVMeGen3: score.StorageScore = 100; break;
                case StorageTier.SataSSD: score.StorageScore = 50; break;
                default: score.StorageScore = 10; break;
            }
            _logger.LogExit(nameof(CalculatePerformanceScore));
        }
    }
}
