using System;

namespace VoltrisOptimizer.Services.Adaptive
{
    /// <summary>
    /// Nível de intensidade de otimização
    /// </summary>

    
    /// <summary>
    /// Flags de serviços habilitados
    /// </summary>
    [Flags]
    public enum ServiceFlags
    {
        None      = 0,
        Essential = 1 << 0,  // Apenas serviços críticos
        Thermal   = 1 << 1,  // Gerenciamento térmico
        Memory    = 1 << 2,  // Gerenciamento de memória
        Latency   = 1 << 3,  // Otimização de latência
        All       = Essential | Thermal | Memory | Latency
    }
    
    /// <summary>
    /// Configuração de um estado específico
    /// </summary>
    public class StateConfiguration
    {
        /// <summary>
        /// Frequência de monitoramento em milissegundos
        /// </summary>
        public int MonitoringFrequencyMs { get; set; }
        
        /// <summary>
        /// Intensidade das otimizações
        /// </summary>
        public OptimizationLevel OptimizationIntensity { get; set; }
        
        /// <summary>
        /// Serviços habilitados neste estado
        /// </summary>
        public ServiceFlags EnabledServices { get; set; }
        
        /// <summary>
        /// Descrição legível do estado
        /// </summary>
        public string Description { get; set; } = string.Empty;
        
        /// <summary>
        /// Verifica se um serviço específico está habilitado
        /// </summary>
        public bool IsServiceEnabled(ServiceFlags service)
        {
            return (EnabledServices & service) == service;
        }
        
        public override string ToString()
        {
            return $"{OptimizationIntensity} ({MonitoringFrequencyMs}ms) - {Description}";
        }
    }
}
