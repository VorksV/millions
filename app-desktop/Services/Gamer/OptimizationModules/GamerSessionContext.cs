using System;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.Gamer.GamerModeManager;

namespace VoltrisOptimizer.Services.Gamer.OptimizationModules
{
    /// <summary>
    /// Contexto da sessão gamer contendo todos os serviços necessários
    /// e o registry de rollback
    /// </summary>
    public class GamerSessionContext
    {
        /// <summary>
        /// ID único da sessão
        /// </summary>
        public string SessionId { get; set; } = Guid.NewGuid().ToString();
        
        /// <summary>
        /// Data/hora de início da sessão
        /// </summary>
        public DateTime StartTime { get; set; } = DateTime.Now;
        
        /// <summary>
        /// Registry de rollback para armazenar snapshots
        /// </summary>
        public RollbackRegistry RollbackRegistry { get; set; } = null!;
        
        /// <summary>
        /// Serviço de logging
        /// </summary>
        public ILoggingService Logger { get; set; } = null!;
        
        /// <summary>
        /// Serviço de power plan (reutilizado)
        /// </summary>
        public IPowerPlanService PowerPlanService { get; set; } = null!;
        
        /// <summary>
        /// Serviço de otimização de processos (reutilizado)
        /// </summary>
        public IProcessOptimizationService ProcessService { get; set; } = null!;
        
        /// <summary>
        /// Serviço de registry (reutilizado)
        /// </summary>
        public IRegistryService RegistryService { get; set; } = null!;

        /// <summary>
        /// ID do processo do jogo alvo (se identificado)
        /// </summary>
        public int? TargetGameProcessId { get; set; }
    }
}

