namespace VoltrisOptimizer.Core.Constants
{
    /// <summary>
    /// Constantes de timeout para operações assíncronas
    /// </summary>
    public static class TimeoutConstants
    {
        // Timeouts de operações de limpeza (em segundos)
        public const int CleanupOperationTimeout = 15;
        public const int SystemCleanupTimeout = 60;
        
        // Timeouts de operações de rede (em segundos)
        public const int NetworkOperationTimeout = 10;
        public const int NetworkResetTimeout = 20;
        public const int TcpOptimizationTimeout = 15;
        
        // Timeouts de operações de performance (em segundos)
        public const int PowerPlanChangeTimeout = 10;
        public const int ServiceOptimizationTimeout = 15;
        public const int MemoryOptimizationTimeout = 20;
        
        // Timeouts de modo gamer (em segundos)
        public const int GamerModeActivationTimeout = 30;
        public const int GamerModeDeactivationTimeout = 30;
        
        // Timeouts de perfil inteligente (em segundos)
        public const int ProfilerAuditTimeout = 30;
        public const int ProfilerApplyTimeout = 60;
        public const int ProfilerActionTimeout = 15;
        
        // Timeouts de processos (em milissegundos)
        public const int ProcessWaitTimeout = 5000;
        public const int ProcessKillTimeout = 3000;
        
        // Intervalos de monitoramento (em milissegundos)
        public const int GameDetectionInterval = 5000;
        public const int ResourceMonitoringInterval = 1000;
        public const int AdaptiveGovernorInterval = 500;
    }
}
