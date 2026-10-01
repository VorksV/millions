using System;
using System.Collections.Generic;
using System.Linq;

namespace VoltrisOptimizer.Core.Validation
{
    /// <summary>
    /// Calcula métricas estatísticas avançadas (Média, Variância, Desvio Padrão, Percentis)
    /// utilizando algoritmos eficientes (Welford para rodadas contínuas ou processamento batch).
    /// </summary>
    public sealed class StatisticalMetrics
    {
        public int Count { get; private set; }
        public double Mean { get; private set; }
        public double Min { get; private set; } = double.MaxValue;
        public double Max { get; private set; } = double.MinValue;
        
        // M2 = sum of squares of differences from the current mean (Welford's algorithm)
        private double _m2;

        public double Variance => Count > 1 ? _m2 / (Count - 1) : 0.0;
        public double StandardDeviation => Math.Sqrt(Variance);

        private readonly List<double> _samplesForPercentile;

        public StatisticalMetrics(bool storeSamplesForPercentile = false)
        {
            if (storeSamplesForPercentile)
            {
                // Limite razoável para evitar vazamento de memória em loops infinitos
                _samplesForPercentile = new List<double>(1000); 
            }
        }

        public void AddSample(double value)
        {
            Count++;
            
            if (value < Min) Min = value;
            if (value > Max) Max = value;

            // Algoritmo de Welford para Média e Variância
            double delta = value - Mean;
            Mean += delta / Count;
            double delta2 = value - Mean;
            _m2 += delta * delta2;

            _samplesForPercentile?.Add(value);
        }

        public double GetPercentile(double percentile)
        {
            if (_samplesForPercentile == null || _samplesForPercentile.Count == 0)
                return 0.0;

            if (percentile < 0.0 || percentile > 1.0)
                throw new ArgumentOutOfRangeException(nameof(percentile), "Percentile must be between 0.0 and 1.0");

            var sorted = _samplesForPercentile.OrderBy(x => x).ToList();
            
            double position = (sorted.Count - 1) * percentile;
            int leftIndex = (int)Math.Floor(position);
            int rightIndex = (int)Math.Ceiling(position);
            
            if (leftIndex == rightIndex)
                return sorted[leftIndex];

            double fraction = position - leftIndex;
            return sorted[leftIndex] + (sorted[rightIndex] - sorted[leftIndex]) * fraction;
        }
    }
}
