using System;

namespace VoltrisOptimizer.Services.Thermal.Models
{
    /// <summary>
    /// Extensão das métricas térmicas com indicadores de performance adicional.
    /// </summary>
    public class PerformanceMetrics : ThermalMetrics
    {
        /// <summary>
        /// Latência média de leitura do disco (milissegundos).
        /// </summary>
        public double DiskReadLatencyMs { get; set; }

        /// <summary>
        /// Latência média de escrita do disco (milissegundos).
        /// </summary>
        public double DiskWriteLatencyMs { get; set; }

        /// <summary>
        /// Latência média de DPC (Deferred Procedure Call) em milissegundos.
        /// </summary>
        public double DpcLatencyMs { get; set; }

        /// <summary>
        /// Latência média de ISR (Interrupt Service Routine) em milissegundos.
        /// </summary>
        public double IsrLatencyMs { get; set; }
    }
}
