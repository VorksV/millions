using System.Collections.Generic;

namespace VoltrisOptimizer.Services.Gamer.StrategyFactory.Models
{
    public class OptimizationBlueprint
    {
        public PerformanceScore Score { get; set; } = new();
        
        /// <summary>
        /// Lista das otimizações que devem ser aplicadas (IDs únicos, ex: "HAGS_ENABLE", "DISABLE_MPO", "HIGH_PRIORITY")
        /// </summary>
        public HashSet<string> ApprovedOptimizations { get; set; } = new();

        /// <summary>
        /// Lista de otimizações explicitamente bloqueadas para esta máquina por histórico negativo
        /// </summary>
        public HashSet<string> BlockedOptimizations { get; set; } = new();
    }
}
