using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Interfaces
{
    /// <summary>
    /// Resultado da execução de um processo
    /// </summary>
    public class ProcessRunResult
    {
        public bool Success { get; set; }
        public int ExitCode { get; set; }
        public string StandardOutput { get; set; } = string.Empty;
        public string StandardError { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
    }

    /// <summary>
    /// Interface para execução de processos externos
    /// Abstrai a criação e execução de processos para facilitar testes e manutenção
    /// </summary>
    public interface IProcessRunner
    {
        /// <summary>
        /// Executa um processo de forma síncrona
        /// </summary>
        /// <param name="fileName">Nome do executável</param>
        /// <param name="arguments">Argumentos da linha de comando</param>
        /// <param name="timeoutMs">Timeout em milissegundos (0 = sem timeout)</param>
        /// <returns>Resultado da execução</returns>
        ProcessRunResult Run(string fileName, string arguments, int timeoutMs = 30000);

        /// <summary>
        /// Executa um processo de forma assíncrona
        /// </summary>
        /// <param name="fileName">Nome do executável</param>
        /// <param name="arguments">Argumentos da linha de comando</param>
        /// <param name="timeoutMs">Timeout em milissegundos (0 = sem timeout)</param>
        /// <param name="cancellationToken">Token de cancelamento</param>
        /// <returns>Resultado da execução</returns>
        Task<ProcessRunResult> RunAsync(string fileName, string arguments, int timeoutMs = 30000, CancellationToken cancellationToken = default);

        /// <summary>
        /// Executa um processo com elevação de privilégios (UAC)
        /// </summary>
        /// <param name="fileName">Nome do executável</param>
        /// <param name="arguments">Argumentos da linha de comando</param>
        /// <returns>Resultado da execução</returns>
        ProcessRunResult RunElevated(string fileName, string arguments);

        /// <summary>
        /// Verifica se um processo está em execução
        /// </summary>
        /// <param name="processName">Nome do processo (sem extensão)</param>
        /// <returns>True se o processo está rodando</returns>
        bool IsProcessRunning(string processName);

        /// <summary>
        /// Obtém processos por nome
        /// </summary>
        /// <param name="processName">Nome do processo (sem extensão)</param>
        /// <returns>Array de processos encontrados</returns>
        Process[] GetProcessesByName(string processName);

        /// <summary>
        /// Tenta encerrar um processo graciosamente
        /// </summary>
        /// <param name="processName">Nome do processo</param>
        /// <param name="waitTimeMs">Tempo de espera antes de forçar</param>
        /// <returns>True se o processo foi encerrado</returns>
        bool TryKillProcess(string processName, int waitTimeMs = 2000);
    }
}

