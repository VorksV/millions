using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.Gamer.Adaptive.Models;

namespace VoltrisOptimizer.Services.Gamer.Adaptive.Execution
{
    /// <summary>
    /// Interface para módulos de otimização atômicos
    /// </summary>
    public interface IOptimizationModule
    {
        string Name { get; }
        OptimizationType Type { get; }
        RiskLevel Risk { get; }
        
        /// <summary>
        /// Verifica se pode aplicar esta otimização
        /// </summary>
        bool CanApply(OptimizationDecision decision, ExecutionPlan plan);
        
        /// <summary>
        /// Aplica a otimização de forma atômica
        /// </summary>
        Task<OptimizationResult> ApplyAsync(OptimizationDecision decision, ExecutionPlan plan, CancellationToken ct = default);
        
        /// <summary>
        /// Reverte a otimização de forma atômica
        /// </summary>
        Task<OptimizationResult> RevertAsync(ExecutionPlan plan, CancellationToken ct = default);
        
        /// <summary>
        /// Valida se a otimização está aplicada corretamente
        /// </summary>
        Task<bool> ValidateAsync(ExecutionPlan plan, CancellationToken ct = default);
    }
    
    /// <summary>
    /// Resultado de operação de otimização
    /// </summary>
    public class OptimizationResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public int ChangesApplied { get; set; }
        public int ChangesReverted { get; set; }
        
        /// <summary>
        /// Dados de rollback para reverter esta operação
        /// </summary>
        public Dictionary<string, object> RollbackData { get; set; } = new();
        
        /// <summary>
        /// Métricas coletadas durante aplicação
        /// </summary>
        public Dictionary<string, object> Metrics { get; set; } = new();
    }
}