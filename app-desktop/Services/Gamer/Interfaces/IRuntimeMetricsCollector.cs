using System;
using VoltrisOptimizer.Services.Thermal.Models;

namespace VoltrisOptimizer.Services.Gamer.Interfaces
{
    /// <summary>
    /// Coleta métricas de performance em tempo‑real (CPU, RAM, disco, etc.)
    /// </summary>
    public interface IRuntimeMetricsCollector : IDisposable
    {
        /// <summary>
        /// Evento disparado a cada coleta bem‑sucedida.
        /// </summary>
        event EventHandler<PerformanceMetrics>? MetricsUpdated;

        /// <summary>
        /// Inicia a coleta periódica.
        /// </summary>
        void Start();

        /// <summary>
        /// Interrompe a coleta.
        /// </summary>
        void Stop();
    }
}
