using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Optimization
{
    /// <summary>
    /// Interface para o orquestrador de orçamento de performance.
    /// </summary>
    public interface IPerformanceBudgetOrchestrator
    {
        /// <summary>
        /// Define um orçamento de performance para um jogo específico.
        /// </summary>
        /// <param name="gameId">ID do jogo.</param>
        /// <param name="budget">Orçamento de performance.</param>
        /// <returns>Tarefa assíncrona.</returns>
        Task SetPerformanceBudgetAsync(string gameId, PerformanceBudget budget);

        /// <summary>
        /// Obtém o orçamento de performance para um jogo.
        /// </summary>
        /// <param name="gameId">ID do jogo.</param>
        /// <returns>Orçamento de performance.</returns>
        Task<PerformanceBudget> GetPerformanceBudgetAsync(string gameId);

        /// <summary>
        /// Remove o orçamento de performance para um jogo.
        /// </summary>
        /// <param name="gameId">ID do jogo.</param>
        /// <returns>Tarefa assíncrona.</returns>
        Task RemovePerformanceBudgetAsync(string gameId);

        /// <summary>
        /// Verifica se o consumo atual está dentro do orçamento.
        /// </summary>
        /// <param name="gameId">ID do jogo.</param>
        /// <returns>Resultado da verificação.</returns>
        Task<BudgetCheckResult> CheckBudgetAsync(string gameId);

        /// <summary>
        /// Obtém estatísticas de consumo para um jogo.
        /// </summary>
        /// <param name="gameId">ID do jogo.</param>
        /// <returns>Estatísticas de consumo.</returns>
        Task<ResourceConsumptionStats> GetConsumptionStatsAsync(string gameId);

        /// <summary>
        /// Reinicia as estatísticas de consumo.
        /// </summary>
        /// <param name="gameId">ID do jogo.</param>
        /// <returns>Tarefa assíncrona.</returns>
        Task ResetConsumptionStatsAsync(string gameId);

        /// <summary>
        /// Evento disparado quando o orçamento é excedido.
        /// </summary>
        event EventHandler<BudgetExceededEventArgs> BudgetExceeded;

        /// <summary>
        /// Evento disparado quando as estatísticas são atualizadas.
        /// </summary>
        event EventHandler<BudgetStatsUpdatedEventArgs> StatsUpdated;
    }

    /// <summary>
    /// Orçamento de performance para um jogo.
    /// </summary>
    public class PerformanceBudget
    {
        /// <summary>
        /// ID do jogo.
        /// </summary>
        public string GameId { get; set; }

        /// <summary>
        /// Nome do jogo.
        /// </summary>
        public string GameName { get; set; }

        /// <summary>
        /// Limite de CPU em porcentagem (0-100).
        /// </summary>
        public double CpuLimitPercent { get; set; } = 80.0;

        /// <summary>
        /// Limite de GPU em porcentagem (0-100).
        /// </summary>
        public double GpuLimitPercent { get; set; } = 85.0;

        /// <summary>
        /// Limite de memória em MB.
        /// </summary>
        public long MemoryLimitMB { get; set; } = 4096;

        /// <summary>
        /// Limite de I/O em MB/s.
        /// </summary>
        public double IoLimitMBps { get; set; } = 100.0;

        /// <summary>
        /// Duração do período de orçamento em segundos.
        /// </summary>
        public int PeriodDurationSeconds { get; set; } = 60;

        /// <summary>
        /// Ações a serem tomadas quando o orçamento é excedido.
        /// </summary>
        public List<BudgetExceededAction> ExceededActions { get; set; } = new List<BudgetExceededAction>();

        /// <summary>
        /// Indica se o orçamento está ativo.
        /// </summary>
        public bool IsActive { get; set; } = true;
    }

    /// <summary>
    /// Ações a serem tomadas quando o orçamento é excedido.
    /// </summary>
    public enum BudgetExceededAction
    {
        /// <summary>
        /// Registrar no log.
        /// </summary>
        Log,

        /// <summary>
        /// Mostrar notificação.
        /// </summary>
        Notify,

        /// <summary>
        /// Reduzir prioridade do processo.
        /// </summary>
        ReduceProcessPriority,

        /// <summary>
        /// Limitar recursos do sistema.
        /// </summary>
        ThrottleResources,

        /// <summary>
        /// Encerrar o processo.
        /// </summary>
        TerminateProcess
    }

    /// <summary>
    /// Resultado da verificação de orçamento.
    /// </summary>
    public class BudgetCheckResult
    {
        /// <summary>
        /// ID do jogo.
        /// </summary>
        public string GameId { get; set; }

        /// <summary>
        /// Indica se o orçamento está sendo respeitado.
        /// </summary>
        public bool WithinBudget { get; set; }

        /// <summary>
        /// Recursos que estão excedendo o orçamento.
        /// </summary>
        public List<ExceededResource> ExceededResources { get; set; } = new List<ExceededResource>();

        /// <summary>
        /// Percentual de uso da CPU.
        /// </summary>
        public double CpuUsagePercent { get; set; }

        /// <summary>
        /// Percentual de uso da GPU.
        /// </summary>
        public double GpuUsagePercent { get; set; }

        /// <summary>
        /// Uso de memória em MB.
        /// </summary>
        public long MemoryUsageMB { get; set; }

        /// <summary>
        /// Taxa de I/O em MB/s.
        /// </summary>
        public double IoRateMBps { get; set; }

        /// <summary>
        /// Timestamp da verificação.
        /// </summary>
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Recurso que está excedendo o orçamento.
    /// </summary>
    public class ExceededResource
    {
        /// <summary>
        /// Tipo do recurso.
        /// </summary>
        public ResourceType ResourceType { get; set; }

        /// <summary>
        /// Valor atual do recurso.
        /// </summary>
        public double CurrentValue { get; set; }

        /// <summary>
        /// Limite do recurso.
        /// </summary>
        public double LimitValue { get; set; }

        /// <summary>
        /// Percentual de excedente.
        /// </summary>
        public double ExcessPercent { get; set; }
    }

    /// <summary>
    /// Tipos de recursos do sistema.
    /// </summary>
    public enum ResourceType
    {
        /// <summary>
        /// CPU.
        /// </summary>
        Cpu,

        /// <summary>
        /// GPU.
        /// </summary>
        Gpu,

        /// <summary>
        /// Memória.
        /// </summary>
        Memory,

        /// <summary>
        /// I/O.
        /// </summary>
        Io
    }

    /// <summary>
    /// Estatísticas de consumo de recursos.
    /// </summary>
    public class ResourceConsumptionStats
    {
        /// <summary>
        /// ID do jogo.
        /// </summary>
        public string GameId { get; set; }

        /// <summary>
        /// Período de coleta das estatísticas.
        /// </summary>
        public DateTime PeriodStart { get; set; }

        /// <summary>
        /// Fim do período de coleta.
        /// </summary>
        public DateTime PeriodEnd { get; set; }

        /// <summary>
        /// Média de uso da CPU em porcentagem.
        /// </summary>
        public double AverageCpuUsagePercent { get; set; }

        /// <summary>
        /// Pico de uso da CPU em porcentagem.
        /// </summary>
        public double PeakCpuUsagePercent { get; set; }

        /// <summary>
        /// Média de uso da GPU em porcentagem.
        /// </summary>
        public double AverageGpuUsagePercent { get; set; }

        /// <summary>
        /// Pico de uso da GPU em porcentagem.
        /// </summary>
        public double PeakGpuUsagePercent { get; set; }

        /// <summary>
        /// Média de uso de memória em MB.
        /// </summary>
        public long AverageMemoryUsageMB { get; set; }

        /// <summary>
        /// Pico de uso de memória em MB.
        /// </summary>
        public long PeakMemoryUsageMB { get; set; }

        /// <summary>
        /// Média de taxa de I/O em MB/s.
        /// </summary>
        public double AverageIoRateMBps { get; set; }

        /// <summary>
        /// Pico de taxa de I/O em MB/s.
        /// </summary>
        public double PeakIoRateMBps { get; set; }

        /// <summary>
        /// Número de vezes que o orçamento foi excedido.
        /// </summary>
        public int BudgetExceededCount { get; set; }

        /// <summary>
        /// Tempo total de execução em segundos.
        /// </summary>
        public double TotalExecutionTimeSeconds { get; set; }
    }

    /// <summary>
    /// Argumentos para evento de orçamento excedido.
    /// </summary>
    public class BudgetExceededEventArgs : EventArgs
    {
        /// <summary>
        /// ID do jogo.
        /// </summary>
        public string GameId { get; }

        /// <summary>
        /// Resultado da verificação.
        /// </summary>
        public BudgetCheckResult CheckResult { get; }

        /// <summary>
        /// Inicializa uma nova instância de <see cref="BudgetExceededEventArgs"/>.
        /// </summary>
        /// <param name="gameId">ID do jogo.</param>
        /// <param name="checkResult">Resultado da verificação.</param>
        public BudgetExceededEventArgs(string gameId, BudgetCheckResult checkResult)
        {
            GameId = gameId;
            CheckResult = checkResult;
        }
    }

    /// <summary>
    /// Argumentos para evento de estatísticas atualizadas.
    /// </summary>
    public class BudgetStatsUpdatedEventArgs : EventArgs
    {
        /// <summary>
        /// ID do jogo.
        /// </summary>
        public string GameId { get; }

        /// <summary>
        /// Estatísticas atualizadas.
        /// </summary>
        public ResourceConsumptionStats Stats { get; }

        /// <summary>
        /// Inicializa uma nova instância de <see cref="BudgetStatsUpdatedEventArgs"/>.
        /// </summary>
        /// <param name="gameId">ID do jogo.</param>
        /// <param name="stats">Estatísticas atualizadas.</param>
        public BudgetStatsUpdatedEventArgs(string gameId, ResourceConsumptionStats stats)
        {
            GameId = gameId;
            Stats = stats;
        }
    }
}
