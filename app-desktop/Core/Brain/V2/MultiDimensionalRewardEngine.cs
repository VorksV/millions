using System;
using VoltrisOptimizer.Core.Configuration;

namespace VoltrisOptimizer.Core.Brain.V2
{
    /// <summary>
    /// Calcula a recompensa para a IA utilizando pesos configuráveis
    /// de múltiplos parâmetros, ensinando a IA o que é "Desempenho Real" em vez de apenas "Mais FPS".
    /// </summary>
    public sealed class MultiDimensionalRewardEngine
    {
        private static readonly Lazy<MultiDimensionalRewardEngine> _instance = new(() => new MultiDimensionalRewardEngine());
        public static MultiDimensionalRewardEngine Instance => _instance.Value;

        // Pesos configuráveis sem necessidade de recompilar (Requisito #7)
        public double WeightFps { get; set; } = 0.30;
        public double WeightFrameTime { get; set; } = 0.25;
        public double WeightOnePercentLow { get; set; } = 0.15;
        public double WeightInputLatency { get; set; } = 0.10;
        public double WeightDpcLatency { get; set; } = 0.10;
        public double WeightPowerEfficiency { get; set; } = 0.05;
        public double WeightThermalHeadroom { get; set; } = 0.05;

        private MultiDimensionalRewardEngine() { }

        /// <summary>
        /// Retorna um valor normalizado de recompensa. Valores altos indicam sucesso total da otimização.
        /// (Nota: FrameTime e Latency são invertidos, pois menor é melhor).
        /// </summary>
        public double CalculateReward(
            double deltaFps, 
            double deltaFrameTime, 
            double deltaOnePercentLow,
            double deltaInputLatency,
            double deltaDpcLatency,
            double deltaPower,
            double deltaThermal)
        {
            if (!VoltrisFeatureFlags.Instance.UseNewRewardEngine)
            {
                // Se legado, a recompensa é apenas a diferença de FPS
                return deltaFps;
            }

            double reward = 0.0;

            // Delta positivo é bom para FPS e 1% Low
            reward += deltaFps * WeightFps;
            reward += deltaOnePercentLow * WeightOnePercentLow;

            // Delta negativo é bom para Latência e Temperatura (portanto invertemos o sinal)
            reward += (-deltaFrameTime) * WeightFrameTime;
            reward += (-deltaInputLatency) * WeightInputLatency;
            reward += (-deltaDpcLatency) * WeightDpcLatency;
            reward += (-deltaPower) * WeightPowerEfficiency;
            reward += (-deltaThermal) * WeightThermalHeadroom;

            return reward;
        }
    }
}
