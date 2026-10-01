using System;

namespace VoltrisOptimizer.Services.Thermal.Models
{
    /// <summary>
    /// Representa um alerta térmico
    /// </summary>
    public class ThermalAlert
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public ThermalAlertLevel Level { get; set; }
        public string Component { get; set; } = string.Empty;
        public double Temperature { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Recommendation { get; set; } = string.Empty;
    }

    public enum ThermalAlertLevel
    {
        Info,
        Warning,
        Critical
    }
}
