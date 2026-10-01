using System;

namespace VoltrisOptimizer.Services.Gamer.Interfaces
{
    /// <summary>
    /// Níveis de saúde térmica do sistema
    /// </summary>
    public enum ThermalHealthState
    {
        /// <summary>
        /// < 90°C - Performance Máxima permitida
        /// </summary>
        Safe,
        
        /// <summary>
        /// 90-97°C - Performance Escalonada (Warning)
        /// </summary>
        Warning,
        
        /// <summary>
        /// >= 98°C - Proteção Ativa (Critical)
        /// </summary>
        Critical
    }

    /// <summary>
    /// Motor de telemetria térmica avançada
    /// </summary>
    public interface IThermalTelemetryEngine : IDisposable
    {
        ThermalHealthState CurrentState { get; }
        double CurrentCpuTemp { get; }
        double CurrentGpuTemp { get; }
        bool IsThrottling { get; }
        
        event EventHandler<ThermalHealthState>? StateChanged;
        
        void Start();
        void Stop();
    }


}
