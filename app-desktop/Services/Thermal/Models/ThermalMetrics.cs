using System;

namespace VoltrisOptimizer.Services.Thermal.Models
{
    /// <summary>
    /// Métricas térmicas do sistema
    /// </summary>
    public class ThermalMetrics
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public double CpuTemperature { get; set; }
        public double GpuTemperature { get; set; }
        public double CpuUsage { get; set; }
        public double GpuUsage { get; set; }
        public double RamUsagePercent { get; set; }
        public double GpuVramUsed { get; set; }
        public double GpuVramTotal { get; set; }
        public bool CpuThrottling { get; set; }
        public bool GpuThrottling { get; set; }

        public ThermalMetrics()
        {
            // Sem logging no construtor - evita flood de log em alocações frequentes
        }
        
        /// <summary>
        /// Indica se a temperatura da CPU é estimada (calculada) ou real (lida de sensor)
        /// </summary>
        public bool IsCpuTemperatureEstimated { get; set; }

        /// <summary>
        /// Indica se a temperatura da GPU é estimada (calculada com base na CPU) ou real
        /// </summary>
        public bool IsGpuTemperatureEstimated { get; set; }
        
        /// <summary>
        /// Indica se as métricas são válidas (pelo menos CPU disponível e não NaN)
        /// </summary>
        public bool IsValid => !double.IsNaN(CpuTemperature) && CpuTemperature > 0;
    }
}
