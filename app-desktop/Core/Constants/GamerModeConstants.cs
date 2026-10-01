namespace VoltrisOptimizer.Core.Constants
{
    /// <summary>
    /// Constantes para o Modo Gamer
    /// </summary>
    public static class GamerModeConstants
    {
        /// <summary>
        /// Intervalos de monitoramento (em milissegundos)
        /// </summary>
        public static class MonitoringIntervals
        {
            // Intervalo do Adaptive Governor (aumentado de 750ms para 10s)
            // Motivo: Reduzir overhead e stuttering
            public const int AdaptiveGovernorInterval = 10000; // 10 segundos
            
            // Intervalo de monitoramento leve
            public const int LightMonitoringInterval = 5000; // 5 segundos
            
            // Intervalo de ping de rede
            public const int NetworkPingInterval = 15000; // 15 segundos
        }
        
        /// <summary>
        /// Configurações de performance
        /// </summary>
        public static class Performance
        {
            // Adaptive Governor desabilitado por padrão
            // Usuário deve habilitar explicitamente se desejar
            public const bool AdaptiveGovernorEnabledByDefault = false;
            
            // Usar monitoramento leve por padrão
            public const bool UseLightweightMonitoring = true;
            
            // Aplicar otimizações estáticas (recomendado)
            public const bool UseStaticOptimizations = true;
        }
        
        /// <summary>
        /// Limites de detecção de stuttering
        /// </summary>
        public static class StutteringThresholds
        {
            // CPU variance para detectar stuttering
            public const double CpuVarianceThreshold = 200; // Aumentado de 150
            
            // Queue length para detectar problema
            public const double QueueLengthThreshold = 3; // Aumentado de 2
            
            // Frame time jitter threshold (ms)
            public const double FrameJitterThreshold = 5.0; // Aumentado de 3.0
            
            // Network jitter threshold (ms)
            public const double NetworkJitterThreshold = 20; // Aumentado de 12
        }
        
        /// <summary>
        /// Cooldowns para evitar thrashing
        /// </summary>
        public static class Cooldowns
        {
            // Cooldown entre mudanças de prioridade (segundos)
            public const int PriorityChangeCooldown = 60; // Aumentado de 10
            
            // Cooldown entre mudanças de rede (segundos)
            public const int NetworkChangeCooldown = 120; // Aumentado de 30
            
            // Cooldown entre ajustes de CPU affinity (segundos)
            public const int CpuAffinityCooldown = 300; // 5 minutos
        }
    }
}
