using System;
using System.Collections.Generic;

namespace VoltrisOptimizer.Services.Performance.Models
{
    /// <summary>
    /// Estado atual das otimizações de performance
    /// </summary>
    public class PerformanceState
    {
        /// <summary>
        /// Se as otimizações estão ativas
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// Perfil de performance atual
        /// </summary>
        public PerformanceProfile? CurrentProfile { get; set; }

        /// <summary>
        /// Perfil Inteligente que originou este estado
        /// </summary>
        public IntelligentProfileType IntelligentProfile { get; set; }

        /// <summary>
        /// Se o Modo Gamer está ativo
        /// </summary>
        public bool IsGamerModeActive { get; set; }

        /// <summary>
        /// Nome do jogo detectado (se houver)
        /// </summary>
        public string? DetectedGame { get; set; }

        /// <summary>
        /// Temperatura da CPU no momento da ativação
        /// </summary>
        public double CpuTemperature { get; set; }

        /// <summary>
        /// Temperatura da GPU no momento da ativação
        /// </summary>
        public double GpuTemperature { get; set; }

        /// <summary>
        /// Se houve throttling térmico
        /// </summary>
        public bool ThermalThrottlingDetected { get; set; }

        /// <summary>
        /// Timestamp da ativação
        /// </summary>
        public DateTime ActivatedAt { get; set; }

        /// <summary>
        /// Timestamp da última atualização
        /// </summary>
        public DateTime LastUpdated { get; set; }

        /// <summary>
        /// Motivo da ativação
        /// </summary>
        public string ActivationReason { get; set; } = string.Empty;

        /// <summary>
        /// Configurações aplicadas
        /// </summary>
        public Dictionary<string, object> AppliedSettings { get; set; } = new();

        /// <summary>
        /// Estado original antes das otimizações (para rollback)
        /// </summary>
        public PerformanceStateSnapshot? OriginalState { get; set; }
    }

    /// <summary>
    /// Snapshot do estado de performance para rollback
    /// </summary>
    public class PerformanceStateSnapshot
    {
        public Guid ActivePowerScheme { get; set; }
        public Dictionary<string, uint> PowerSettings { get; set; } = new();
        public DateTime CapturedAt { get; set; }
    }

    /// <summary>
    /// Evento de mudança de estado de performance
    /// </summary>
    public class PerformanceStateChangedEventArgs : EventArgs
    {
        public PerformanceState? PreviousState { get; set; }
        public PerformanceState CurrentState { get; set; } = new();
        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>
    /// Alerta de segurança térmica
    /// </summary>
    public class ThermalSafetyAlert
    {
        public ThermalSafetyLevel Level { get; set; }
        public double CpuTemperature { get; set; }
        public double GpuTemperature { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Recommendation { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }

    public enum ThermalSafetyLevel
    {
        Normal,
        Warning,
        Critical
    }
}
