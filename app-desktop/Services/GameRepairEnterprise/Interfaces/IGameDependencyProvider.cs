using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.GameRepairEnterprise.Models;

namespace VoltrisOptimizer.Services.GameRepairEnterprise.Interfaces
{
    public interface IGameDependencyProvider
    {
        string ProviderName { get; }
        
        /// <summary>
        /// Realiza o diagnóstico profundo do componente baseado em múltiplas evidências.
        /// </summary>
        Task<List<GameDependencyDiagnosis>> DiagnoseAsync(CancellationToken ct);
        
        /// <summary>
        /// Executa o pipeline atômico de download e instalação, reportando progresso estruturado.
        /// Retorna verdadeiro se a instalação/processo concluiu sem erros críticos.
        /// </summary>
        Task<GameDependencyRepairResult> RepairAsync(GameDependencyDiagnosis issue, IProgress<(GameDependencyState state, int percentage, string message)> progress, CancellationToken ct);
        
        /// <summary>
        /// Realiza a validação técnica (evidence-based validation) para garantir que o componente está perfeitamente funcional.
        /// Deve ser invocado pós-reparo para certificar o sucesso.
        /// </summary>
        Task<bool> ValidateAsync(GameDependencyDiagnosis issue, CancellationToken ct);
        
        /// <summary>
        /// Desfaz operações granulares no caso de falha severa (Rollback).
        /// </summary>
        Task RollbackAsync(GameDependencyDiagnosis issue, CancellationToken ct);
    }
}
