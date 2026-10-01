using System;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Gamer.OptimizationModules
{
    /// <summary>
    /// Interface base para módulos de otimização gamer temporários
    /// Todos os módulos devem ser idempotentes e 100% reversíveis
    /// </summary>
    public interface IGamerOptimizationModule
    {
        /// <summary>
        /// Nome do módulo
        /// </summary>
        string Name { get; }
        
        /// <summary>
        /// Descrição do módulo
        /// </summary>
        string Description { get; }
        
        /// <summary>
        /// Aplica otimizações do módulo
        /// Deve ser idempotente (pode ser chamado múltiplas vezes com o mesmo resultado)
        /// </summary>
        Task<ModuleApplyResult> ApplyAsync(GamerSessionContext ctx, CancellationToken ct = default);
        
        /// <summary>
        /// Reverte todas as otimizações aplicadas pelo módulo
        /// Deve restaurar o estado exato anterior à aplicação
        /// </summary>
        Task<ModuleRevertResult> RevertAsync(GamerSessionContext ctx, CancellationToken ct = default);
    }
    
    /// <summary>
    /// Resultado da aplicação de um módulo
    /// </summary>
    public class ModuleApplyResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public int ChangesApplied { get; set; }
        public string[] AppliedChanges { get; set; } = Array.Empty<string>();
    }
    
    /// <summary>
    /// Resultado da reversão de um módulo
    /// </summary>
    public class ModuleRevertResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public int ChangesReverted { get; set; }
        public string[] RevertedChanges { get; set; } = Array.Empty<string>();
    }
}

