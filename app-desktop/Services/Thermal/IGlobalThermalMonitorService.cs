using System;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.Thermal.Models;

namespace VoltrisOptimizer.Services.Thermal
{
    /// <summary>
    /// Interface para serviços de monitoramento térmico
    /// </summary>
    public interface IThermalMonitorService : IDisposable
    {
        event EventHandler<ThermalMetrics>? MetricsUpdated;
        ThermalMetrics? CurrentMetrics { get; }
        
        // Métodos de compatibilidade (Helper)
        double GetCpuTemperature();
        double GetGpuTemperature();
        double GetGpuUsage();
        bool IsCpuThrottling();
        bool IsGpuThrottling();
        
        Task<ThermalMetrics> GetCurrentMetricsAsync();
    }

    /// <summary>
    /// Interface para o serviço global de monitoramento térmico (Versão Extendida)
    /// </summary>
    public interface IGlobalThermalMonitorService : IThermalMonitorService
    {
        event EventHandler<ThermalAlert>? AlertGenerated;
        bool IsMonitoring { get; }
        Task StartMonitoringAsync();
        Task StopMonitoringAsync();
        void SuppressAlertsFor(TimeSpan duration);
    }
}
