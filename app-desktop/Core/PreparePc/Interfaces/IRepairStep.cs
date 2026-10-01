using System;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Core.PreparePc.Interfaces
{
    /// <summary>
    /// Modo de execução do Prepare PC
    /// </summary>
    public enum PreparePcMode
    {
        /// <summary>
        /// Apenas simula as ações, não executa nada
        /// </summary>
        DryRun,
        
        /// <summary>
        /// Executa apenas ações SAFE, simula as condicionais
        /// </summary>
        Recommended,
        
        /// <summary>
        /// Executa SAFE + pergunta para cada condicional + bloqueia arriscadas sem duplo consentimento
        /// </summary>
        Full
    }
    
    /// <summary>
    /// Categoria de risco de uma ação
    /// </summary>
    public enum RiskCategory
    {
        /// <summary>
        /// Seguro - pode executar sem confirmação extra
        /// </summary>
        Safe,
        
        /// <summary>
        /// Condicional - requer confirmação em modo Full
        /// </summary>
        Conditional,
        
        /// <summary>
        /// Arriscado - requer dupla confirmação
        /// </summary>
        Risky
    }
    
    /// <summary>
    /// Status de execução de um step
    /// </summary>
    public enum StepStatus
    {
        Pending,
        Running,
        Completed,
        Skipped,
        Failed,
        Cancelled,
        RolledBack
    }
    
    /// <summary>
    /// Resultado de execução de um step
    /// </summary>
    public class StepResult
    {
        public bool Success { get; set; }
        public StepStatus Status { get; set; }
        public string Message { get; set; } = string.Empty;
        public string[] Logs { get; set; } = Array.Empty<string>();
        public string? BackupPath { get; set; }
        public TimeSpan Duration { get; set; }
        public Exception? Error { get; set; }
        public bool RequiresReboot { get; set; }
        public bool CanRollback { get; set; }
        
        public static StepResult Succeeded(string message, string? backupPath = null) => new()
        {
            Success = true,
            Status = StepStatus.Completed,
            Message = message,
            BackupPath = backupPath,
            CanRollback = backupPath != null
        };
        
        public static StepResult Skipped(string reason) => new()
        {
            Success = true,
            Status = StepStatus.Skipped,
            Message = reason
        };
        
        public static StepResult Failed(string message, Exception? ex = null) => new()
        {
            Success = false,
            Status = StepStatus.Failed,
            Message = message,
            Error = ex
        };
        
        public static StepResult Cancelled() => new()
        {
            Success = false,
            Status = StepStatus.Cancelled,
            Message = "Operação cancelada pelo usuário"
        };
    }
    
    /// <summary>
    /// Progresso de um step
    /// </summary>
    public class StepProgress
    {
        public string StepName { get; set; } = string.Empty;
        public int PercentComplete { get; set; }
        public string CurrentAction { get; set; } = string.Empty;
        public string[] RecentLogs { get; set; } = Array.Empty<string>();
    }
    
    /// <summary>
    /// Interface para um step de reparo/otimização
    /// </summary>
    public interface IRepairStep
    {
        /// <summary>
        /// Nome do step para exibição
        /// </summary>
        string Name { get; }
        
        /// <summary>
        /// Descrição do que o step faz
        /// </summary>
        string Description { get; }
        
        /// <summary>
        /// Categoria de risco
        /// </summary>
        RiskCategory Risk { get; }
        
        /// <summary>
        /// Tempo estimado em segundos
        /// </summary>
        int EstimatedTimeSeconds { get; }
        
        /// <summary>
        /// Ícone para UI
        /// </summary>
        string Icon { get; }
        
        /// <summary>
        /// Ordem de execução (menor = primeiro)
        /// </summary>
        int Order { get; }
        
        /// <summary>
        /// Se este step pode ser revertido
        /// </summary>
        bool CanRollback { get; }
        
        /// <summary>
        /// Verifica se o step pode ser executado no sistema atual
        /// </summary>
        Task<(bool CanRun, string Reason)> CanExecuteAsync(CancellationToken ct = default);
        
        /// <summary>
        /// Executa o step
        /// </summary>
        Task<StepResult> ExecuteAsync(
            PreparePcMode mode, 
            IProgress<StepProgress>? progress = null,
            CancellationToken ct = default);
        
        /// <summary>
        /// Reverte as ações do step usando o backup
        /// </summary>
        Task<StepResult> RollbackAsync(string backupPath, CancellationToken ct = default);
    }
    
    /// <summary>
    /// Interface para o gerenciador de rollback
    /// </summary>
    public interface IRollbackManager
    {
        /// <summary>
        /// Registra uma ação para rollback
        /// </summary>
        void RegisterAction(string stepName, string backupPath, DateTime timestamp);
        
        /// <summary>
        /// Obtém todas as ações registradas
        /// </summary>
        RollbackAction[] GetActions();
        
        /// <summary>
        /// Executa rollback de todas as ações em ordem inversa
        /// </summary>
        Task<bool> RollbackAllAsync(IProgress<StepProgress>? progress = null, CancellationToken ct = default);
        
        /// <summary>
        /// Executa rollback de uma ação específica
        /// </summary>
        Task<bool> RollbackActionAsync(string stepName, CancellationToken ct = default);
        
        /// <summary>
        /// Limpa backups antigos (mais de X dias)
        /// </summary>
        Task CleanOldBackupsAsync(int daysToKeep = 7);
    }
    
    /// <summary>
    /// Ação de rollback registrada
    /// </summary>
    public class RollbackAction
    {
        public string StepName { get; set; } = string.Empty;
        public string BackupPath { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string Type { get; set; } = string.Empty;
        public bool IsRolledBack { get; set; }
    }
}

