using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.SystemChanges;

namespace VoltrisOptimizer.Services.Optimization
{
    /// <summary>
    /// Implementação do orquestrador de orçamento de performance.
    /// </summary>
    public class PerformanceBudgetOrchestrator : IPerformanceBudgetOrchestrator
    {
        private readonly ISystemInfoService _systemInfoService;
        private readonly IProcessRunner _processRunner;
        private readonly Dictionary<string, PerformanceBudget> _budgets;
        private readonly ConcurrentDictionary<string, ResourceConsumptionStats> _consumptionStats;
        private readonly ConcurrentDictionary<string, DateTime> _lastCheckTimes;
        private readonly object _lockObject = new object();
        
        private CancellationTokenSource _cancellationTokenSource;
        private Task _monitoringTask;
        private bool _isMonitoring = false;
        
        private const int MonitoringIntervalMs = 5000; // 5 segundos (otimizado para reduzir CPU)

        /// <summary>
        /// Evento disparado quando o orçamento é excedido.
        /// </summary>
        public event EventHandler<BudgetExceededEventArgs> BudgetExceeded;

        /// <summary>
        /// Evento disparado quando as estatísticas são atualizadas.
        /// </summary>
        public event EventHandler<BudgetStatsUpdatedEventArgs> StatsUpdated;

        /// <summary>
        /// Inicializa uma nova instância de <see cref="PerformanceBudgetOrchestrator"/>.
        /// </summary>
        /// <param name="systemInfoService">Serviço de informações do sistema.</param>
        /// <param name="processRunner">Executor de processos.</param>
        public PerformanceBudgetOrchestrator(ISystemInfoService systemInfoService, IProcessRunner processRunner)
        {
            _systemInfoService = systemInfoService ?? throw new ArgumentNullException(nameof(systemInfoService));
            _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
            _budgets = new Dictionary<string, PerformanceBudget>();
            _consumptionStats = new ConcurrentDictionary<string, ResourceConsumptionStats>();
            _lastCheckTimes = new ConcurrentDictionary<string, DateTime>();
        }

        /// <summary>
        /// Define um orçamento de performance para um jogo específico.
        /// </summary>
        /// <param name="gameId">ID do jogo.</param>
        /// <param name="budget">Orçamento de performance.</param>
        /// <returns>Tarefa assíncrona.</returns>
        public async Task SetPerformanceBudgetAsync(string gameId, PerformanceBudget budget)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("O ID do jogo não pode ser vazio.", nameof(gameId));
            
            if (budget == null)
                throw new ArgumentNullException(nameof(budget));

            lock (_lockObject)
            {
                _budgets[gameId] = budget;
            }
            
            // Inicializa as estatísticas para este jogo
            var stats = new ResourceConsumptionStats
            {
                GameId = gameId,
                PeriodStart = DateTime.UtcNow,
                PeriodEnd = DateTime.UtcNow.AddSeconds(budget.PeriodDurationSeconds)
            };
            
            _consumptionStats[gameId] = stats;
            _lastCheckTimes[gameId] = DateTime.UtcNow;
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// Obtém o orçamento de performance para um jogo.
        /// </summary>
        /// <param name="gameId">ID do jogo.</param>
        /// <returns>Orçamento de performance.</returns>
        public async Task<PerformanceBudget> GetPerformanceBudgetAsync(string gameId)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("O ID do jogo não pode ser vazio.", nameof(gameId));

            lock (_lockObject)
            {
                _budgets.TryGetValue(gameId, out var budget);
                return budget?.Clone();
            }
        }

        /// <summary>
        /// Remove o orçamento de performance para um jogo.
        /// </summary>
        /// <param name="gameId">ID do jogo.</param>
        /// <returns>Tarefa assíncrona.</returns>
        public async Task RemovePerformanceBudgetAsync(string gameId)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("O ID do jogo não pode ser vazio.", nameof(gameId));

            lock (_lockObject)
            {
                _budgets.Remove(gameId);
            }
            
            _consumptionStats.TryRemove(gameId, out _);
            _lastCheckTimes.TryRemove(gameId, out _);
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// Verifica se o consumo atual está dentro do orçamento.
        /// </summary>
        /// <param name="gameId">ID do jogo.</param>
        /// <returns>Resultado da verificação.</returns>
        public async Task<BudgetCheckResult> CheckBudgetAsync(string gameId)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("O ID do jogo não pode ser vazio.", nameof(gameId));

            // Obtém o orçamento para este jogo
            PerformanceBudget budget;
            lock (_lockObject)
            {
                if (!_budgets.TryGetValue(gameId, out budget))
                {
                    return new BudgetCheckResult
                    {
                        GameId = gameId,
                        WithinBudget = true,
                        Timestamp = DateTime.UtcNow
                    };
                }
            }
            
            // Coleta informações de consumo atuais
            // Todos os métodos retornam double
            var cpuUsage = await _systemInfoService.GetCpuUsageAsync(); // porcentagem
            var gpuUsage = await _systemInfoService.GetGpuUsageAsync(); // porcentagem
            var memoryUsage = await _systemInfoService.GetMemoryUsageAsync(); // MB
            var ioRate = await _systemInfoService.GetIoActivityAsync(); // MB/s
            
            // Verifica se algum recurso está excedendo o orçamento
            var exceededResources = new List<ExceededResource>();
            
            if (cpuUsage > budget.CpuLimitPercent)
            {
                exceededResources.Add(new ExceededResource
                {
                    ResourceType = ResourceType.Cpu,
                    CurrentValue = cpuUsage,
                    LimitValue = budget.CpuLimitPercent,
                    ExcessPercent = (cpuUsage - budget.CpuLimitPercent) / budget.CpuLimitPercent * 100
                });
            }
            
            if (gpuUsage > budget.GpuLimitPercent)
            {
                exceededResources.Add(new ExceededResource
                {
                    ResourceType = ResourceType.Gpu,
                    CurrentValue = gpuUsage,
                    LimitValue = budget.GpuLimitPercent,
                    ExcessPercent = (gpuUsage - budget.GpuLimitPercent) / budget.GpuLimitPercent * 100
                });
            }
            
            if (memoryUsage > budget.MemoryLimitMB)
            {
                exceededResources.Add(new ExceededResource
                {
                    ResourceType = ResourceType.Memory,
                    CurrentValue = memoryUsage,
                    LimitValue = budget.MemoryLimitMB,
                    ExcessPercent = (memoryUsage - budget.MemoryLimitMB) / budget.MemoryLimitMB * 100
                });
            }
            
            if (ioRate > budget.IoLimitMBps)
            {
                exceededResources.Add(new ExceededResource
                {
                    ResourceType = ResourceType.Io,
                    CurrentValue = ioRate,
                    LimitValue = budget.IoLimitMBps,
                    ExcessPercent = (ioRate - budget.IoLimitMBps) / budget.IoLimitMBps * 100
                });
            }
            
            var withinBudget = exceededResources.Count == 0;
            
            var result = new BudgetCheckResult
            {
                GameId = gameId,
                WithinBudget = withinBudget,
                ExceededResources = exceededResources,
                CpuUsagePercent = cpuUsage,
                GpuUsagePercent = gpuUsage,
                MemoryUsageMB = (long)memoryUsage,
                IoRateMBps = ioRate,
                Timestamp = DateTime.UtcNow
            };
            
            // Atualiza as estatísticas
            await UpdateConsumptionStatsAsync(gameId, result);
            
            // Se o orçamento foi excedido, dispara o evento
            if (!withinBudget)
            {
                OnBudgetExceeded(gameId, result);
                
                // Executa as ações configuradas
                await ExecuteBudgetExceededActionsAsync(gameId, budget, result);
            }
            
            return result;
        }

        /// <summary>
        /// Obtém estatísticas de consumo para um jogo.
        /// </summary>
        /// <param name="gameId">ID do jogo.</param>
        /// <returns>Estatísticas de consumo.</returns>
        public async Task<ResourceConsumptionStats> GetConsumptionStatsAsync(string gameId)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("O ID do jogo não pode ser vazio.", nameof(gameId));

            _consumptionStats.TryGetValue(gameId, out var stats);
            return stats?.Clone();
        }

        /// <summary>
        /// Reinicia as estatísticas de consumo.
        /// </summary>
        /// <param name="gameId">ID do jogo.</param>
        /// <returns>Tarefa assíncrona.</returns>
        public async Task ResetConsumptionStatsAsync(string gameId)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("O ID do jogo não pode ser vazio.", nameof(gameId));

            // Obtém o orçamento para determinar a duração do período
            PerformanceBudget budget;
            lock (_lockObject)
            {
                _budgets.TryGetValue(gameId, out budget);
            }
            
            var periodDuration = budget?.PeriodDurationSeconds ?? 60;
            
            var newStats = new ResourceConsumptionStats
            {
                GameId = gameId,
                PeriodStart = DateTime.UtcNow,
                PeriodEnd = DateTime.UtcNow.AddSeconds(periodDuration)
            };
            
            _consumptionStats[gameId] = newStats;
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// Inicia o monitoramento contínuo de orçamentos.
        /// </summary>
        /// <returns>Tarefa assíncrona.</returns>
        public async Task StartMonitoringAsync()
        {
            lock (_lockObject)
            {
                if (_isMonitoring)
                    return;

                _isMonitoring = true;
                _cancellationTokenSource = new CancellationTokenSource();
            }

            // Inicia a tarefa de monitoramento
            _monitoringTask = MonitorBudgetsAsync(_cancellationTokenSource.Token);
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// Para o monitoramento contínuo de orçamentos.
        /// </summary>
        /// <returns>Tarefa assíncrona.</returns>
        public async Task StopMonitoringAsync()
        {
            lock (_lockObject)
            {
                if (!_isMonitoring)
                    return;

                _isMonitoring = false;
                _cancellationTokenSource?.Cancel();
            }

            // Aguarda o término da tarefa de monitoramento
            if (_monitoringTask != null)
            {
                try
                {
                    await _monitoringTask;
                }
                catch (OperationCanceledException)
                {
                    // Esperado quando cancelamos a tarefa
                }
            }
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// Monitora continuamente os orçamentos de performance.
        /// </summary>
        /// <param name="cancellationToken">Token de cancelamento.</param>
        /// <returns>Tarefa assíncrona.</returns>
        private async Task MonitorBudgetsAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _isMonitoring)
            {
                try
                {
                    // Verifica todos os orçamentos ativos
                    List<string> gameIds;
                    lock (_lockObject)
                    {
                        gameIds = _budgets.Where(kvp => kvp.Value.IsActive)
                                          .Select(kvp => kvp.Key)
                                          .ToList();
                    }
                    
                    foreach (var gameId in gameIds)
                    {
                        await CheckBudgetAsync(gameId);
                    }
                    
                    // Aguarda o intervalo de monitoramento
                    await Task.Delay(MonitoringIntervalMs, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Tarefa cancelada, sair do loop
                    break;
                }
                catch (Exception ex)
                {
                    // Log de erro mas continua executando
                    System.Diagnostics.Debug.WriteLine($"Erro no monitoramento de orçamentos: {ex.Message}");
                    
                    // Aguarda mesmo em caso de erro para evitar loops muito rápidos
                    try
                    {
                        await Task.Delay(MonitoringIntervalMs, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Atualiza as estatísticas de consumo.
        /// </summary>
        /// <param name="gameId">ID do jogo.</param>
        /// <param name="checkResult">Resultado da verificação.</param>
        /// <returns>Tarefa assíncrona.</returns>
        private async Task UpdateConsumptionStatsAsync(string gameId, BudgetCheckResult checkResult)
        {
            if (!_consumptionStats.TryGetValue(gameId, out var stats))
            {
                stats = new ResourceConsumptionStats
                {
                    GameId = gameId,
                    PeriodStart = DateTime.UtcNow,
                    PeriodEnd = DateTime.UtcNow.AddMinutes(1)
                };
                _consumptionStats[gameId] = stats;
            }
            
            // Atualiza as médias e picos
            stats.AverageCpuUsagePercent = (stats.AverageCpuUsagePercent + checkResult.CpuUsagePercent) / 2;
            stats.PeakCpuUsagePercent = Math.Max(stats.PeakCpuUsagePercent, checkResult.CpuUsagePercent);
            
            stats.AverageGpuUsagePercent = (stats.AverageGpuUsagePercent + checkResult.GpuUsagePercent) / 2;
            stats.PeakGpuUsagePercent = Math.Max(stats.PeakGpuUsagePercent, checkResult.GpuUsagePercent);
            
            stats.AverageMemoryUsageMB = (stats.AverageMemoryUsageMB + checkResult.MemoryUsageMB) / 2;
            stats.PeakMemoryUsageMB = Math.Max(stats.PeakMemoryUsageMB, checkResult.MemoryUsageMB);
            
            stats.AverageIoRateMBps = (stats.AverageIoRateMBps + checkResult.IoRateMBps) / 2;
            stats.PeakIoRateMBps = Math.Max(stats.PeakIoRateMBps, checkResult.IoRateMBps);
            
            // Incrementa contador se orçamento foi excedido
            if (!checkResult.WithinBudget)
            {
                stats.BudgetExceededCount++;
            }
            
            // Atualiza o período se necessário
            if (DateTime.UtcNow > stats.PeriodEnd)
            {
                stats.PeriodStart = stats.PeriodEnd;
                stats.PeriodEnd = DateTime.UtcNow.AddMinutes(1);
            }
            
            stats.TotalExecutionTimeSeconds += MonitoringIntervalMs / 1000.0;
            
            // Dispara evento de atualização de estatísticas
            OnStatsUpdated(gameId, stats.Clone());
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// Executa as ações configuradas quando o orçamento é excedido.
        /// </summary>
        /// <param name="gameId">ID do jogo.</param>
        /// <param name="budget">Orçamento.</param>
        /// <param name="checkResult">Resultado da verificação.</param>
        /// <returns>Tarefa assíncrona.</returns>
        private async Task ExecuteBudgetExceededActionsAsync(string gameId, PerformanceBudget budget, BudgetCheckResult checkResult)
        {
            foreach (var action in budget.ExceededActions)
            {
                switch (action)
                {
                    case BudgetExceededAction.Log:
                        System.Diagnostics.Debug.WriteLine($"Orçamento excedido para jogo {gameId}: " +
                            $"CPU={checkResult.CpuUsagePercent:F1}% (limite={budget.CpuLimitPercent}%), " +
                            $"GPU={checkResult.GpuUsagePercent:F1}% (limite={budget.GpuLimitPercent}%)");
                        break;
                        
                    case BudgetExceededAction.Notify:
                        // Em uma implementação real, enviaria uma notificação para o usuário
                        break;
                        
                    case BudgetExceededAction.ReduceProcessPriority:
                        await ReduceProcessPriorityAsync(gameId);
                        break;
                        
                    case BudgetExceededAction.ThrottleResources:
                        await ThrottleResourcesAsync(gameId, budget, checkResult);
                        break;
                        
                    case BudgetExceededAction.TerminateProcess:
                        await TerminateProcessAsync(gameId);
                        break;
                }
            }
        }

        /// <summary>
        /// Reduz a prioridade do processo do jogo.
        /// </summary>
        /// <param name="gameId">ID do jogo.</param>
        /// <returns>Tarefa assíncrona.</returns>
        private async Task ReduceProcessPriorityAsync(string gameId)
        {
            // Em uma implementação real, identificaria o processo do jogo e reduziria sua prioridade
            // Por exemplo: Process.GetProcessesByName(gameId) e então process.PriorityClass = ProcessPriorityClass.BelowNormal
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// Limita os recursos do sistema para o jogo.
        /// </summary>
        /// <param name="gameId">ID do jogo.</param>
        /// <param name="budget">Orçamento.</param>
        /// <param name="checkResult">Resultado da verificação.</param>
        /// <returns>Tarefa assíncrona.</returns>
        private async Task ThrottleResourcesAsync(string gameId, PerformanceBudget budget, BudgetCheckResult checkResult)
        {
            // Em uma implementação real, aplicaria limites de recursos usando APIs do sistema
            // Por exemplo, usando Job Objects no Windows para limitar CPU e memória
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// Encerra o processo do jogo.
        /// </summary>
        /// <param name="gameId">ID do jogo.</param>
        /// <returns>Tarefa assíncrona.</returns>
        private async Task TerminateProcessAsync(string gameId)
        {
            // Em uma implementação real, identificaria e encerraria o processo do jogo
            // Por exemplo: Process.GetProcessesByName(gameId) e então process.Kill()
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// Dispara o evento de orçamento excedido.
        /// </summary>
        /// <param name="gameId">ID do jogo.</param>
        /// <param name="result">Resultado da verificação.</param>
        protected virtual void OnBudgetExceeded(string gameId, BudgetCheckResult result)
        {
            BudgetExceeded?.Invoke(this, new BudgetExceededEventArgs(gameId, result));
        }

        /// <summary>
        /// Dispara o evento de estatísticas atualizadas.
        /// </summary>
        /// <param name="gameId">ID do jogo.</param>
        /// <param name="stats">Estatísticas atualizadas.</param>
        protected virtual void OnStatsUpdated(string gameId, ResourceConsumptionStats stats)
        {
            StatsUpdated?.Invoke(this, new BudgetStatsUpdatedEventArgs(gameId, stats));
        }
    }

    /// <summary>
    /// Extensões para clonagem de objetos.
    /// </summary>
    public static class PerformanceBudgetExtensions
    {
        /// <summary>
        /// Cria uma cópia do orçamento de performance.
        /// </summary>
        /// <param name="budget">Orçamento a ser copiado.</param>
        /// <returns>Cópia do orçamento.</returns>
        public static PerformanceBudget Clone(this PerformanceBudget budget)
        {
            if (budget == null)
                return null;
            
            return new PerformanceBudget
            {
                GameId = budget.GameId,
                GameName = budget.GameName,
                CpuLimitPercent = budget.CpuLimitPercent,
                GpuLimitPercent = budget.GpuLimitPercent,
                MemoryLimitMB = budget.MemoryLimitMB,
                IoLimitMBps = budget.IoLimitMBps,
                PeriodDurationSeconds = budget.PeriodDurationSeconds,
                ExceededActions = new List<BudgetExceededAction>(budget.ExceededActions),
                IsActive = budget.IsActive
            };
        }
        
        /// <summary>
        /// Cria uma cópia das estatísticas de consumo.
        /// </summary>
        /// <param name="stats">Estatísticas a serem copiadas.</param>
        /// <returns>Cópia das estatísticas.</returns>
        public static ResourceConsumptionStats Clone(this ResourceConsumptionStats stats)
        {
            if (stats == null)
                return null;
            
            return new ResourceConsumptionStats
            {
                GameId = stats.GameId,
                PeriodStart = stats.PeriodStart,
                PeriodEnd = stats.PeriodEnd,
                AverageCpuUsagePercent = stats.AverageCpuUsagePercent,
                PeakCpuUsagePercent = stats.PeakCpuUsagePercent,
                AverageGpuUsagePercent = stats.AverageGpuUsagePercent,
                PeakGpuUsagePercent = stats.PeakGpuUsagePercent,
                AverageMemoryUsageMB = stats.AverageMemoryUsageMB,
                PeakMemoryUsageMB = stats.PeakMemoryUsageMB,
                AverageIoRateMBps = stats.AverageIoRateMBps,
                PeakIoRateMBps = stats.PeakIoRateMBps,
                BudgetExceededCount = stats.BudgetExceededCount,
                TotalExecutionTimeSeconds = stats.TotalExecutionTimeSeconds
            };
        }
    }
}
