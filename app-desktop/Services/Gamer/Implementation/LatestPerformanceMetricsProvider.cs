using System;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Thermal.Models;

namespace VoltrisOptimizer.Services.Gamer.Implementation
{
    /// <summary>
    /// Armazena a última métrica de performance coletada. Thread‑safe para acesso concorrente.
    /// </summary>
    public class LatestPerformanceMetricsProvider
    {
        private PerformanceMetrics? _latest;
        private readonly object _lock = new();

        public void Update(PerformanceMetrics metrics)
        {
            lock (_lock)
            {
                _latest = metrics;
            }
        }

        public PerformanceMetrics? GetLatest()
        {
            lock (_lock)
            {
                return _latest;
            }
        }
    }
}
