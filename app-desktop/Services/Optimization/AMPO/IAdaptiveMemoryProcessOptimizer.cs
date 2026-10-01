using System;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Optimization.AMPO
{
    /// <summary>
    /// Adaptive Memory & Process Optimizer (AMPO)
    /// Responsável por detectar pressão de memória, prevenir stutter por saturação de commit
    /// e moderar IO interno da aplicação durante sessões de jogo.
    /// Integra com Modo Gamer/DLS sem duplicar comportamentos.
    /// </summary>
    public interface IAdaptiveMemoryProcessOptimizer
    {
        /// <summary>
        /// Indica se o watcher está ativo
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        /// Habilita/Desabilita o módulo AMPO
        /// </summary>
        bool Enabled { get; set; }

        /// <summary>
        /// Inicia o watcher do AMPO (modo gamer)
        /// </summary>
        Task StartWatcherAsync(int? gameProcessId = null, CancellationToken ct = default);

        /// <summary>
        /// Para o watcher do AMPO
        /// </summary>
        Task StopWatcherAsync(CancellationToken ct = default);

        /// <summary>
        /// Executa uma verificação de pressão de memória e aplica otimizações seguras
        /// apenas quando necessário e sem afetar processos críticos.
        /// </summary>
        void CheckAndOptimizeSafely();
    }
}

