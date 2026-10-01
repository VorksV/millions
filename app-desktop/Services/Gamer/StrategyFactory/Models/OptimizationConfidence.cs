using System;

namespace VoltrisOptimizer.Services.Gamer.StrategyFactory.Models
{
    public class OptimizationConfidence
    {
        public string OptimizationId { get; set; } = string.Empty;
        
        /// <summary>
        /// Confiança da otimização de 0 a 100%. Começa em 50% por padrão.
        /// </summary>
        public int ConfidenceScore { get; set; } = 50;
        
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        
        /// <summary>
        /// Média de variação de FPS (Delta) causada por este Tweak nesta máquina (+5%, -2%, etc)
        /// </summary>
        public double AverageFpsDeltaPercent { get; set; }
        
        /// <summary>
        /// Média de variação no Frametime (Delta) causada por este Tweak
        /// </summary>
        public double AverageFrametimeDeltaPercent { get; set; }

        public bool IsRecommended => ConfidenceScore >= 70;
        public bool IsBlocked => ConfidenceScore <= 30;
    }
}
