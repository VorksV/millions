using System;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Cleanup.Interfaces
{
    public interface ICleanupModule
    {
        /// <summary>
        /// Nome descritivo do módulo de limpeza
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Realiza a análise estimando quantos bytes podem ser liberados
        /// </summary>
        Task<long> AnalyzeAsync(CancellationToken ct);

        /// <summary>
        /// Realiza a limpeza efetiva dos arquivos/recursos e retorna o espaço real liberado
        /// </summary>
        Task<long> CleanAsync(IProgress<string> progress, CancellationToken ct);
    }
}
