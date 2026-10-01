using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.Logging;
using VoltrisOptimizer.Core.Validation;

namespace VoltrisOptimizer.Core.Execution
{
    /// <summary>
    /// Fila de execução de otimizações. 
    /// Processa ações sequencialmente (ou com concorrência controlada) garantindo ausência de Race Conditions.
    /// </summary>
    public sealed class ExecutionQueue : IDisposable
    {
        private readonly BlockingCollection<OptimizationAction> _queue;
        private readonly CancellationTokenSource _cts = new();

        public ExecutionQueue()
        {
            // Usando ConcurrentQueue interna para FIFO. 
            // Uma fila de prioridade real exigiria uma estrutura customizada (PriorityQueue do .NET 6+ encapsulada de forma thread-safe).
            // Para simplificar a fase inicial, usaremos BlockingCollection, mas a lógica de prioridade será governada no Scheduler.
            _queue = new BlockingCollection<OptimizationAction>(new ConcurrentQueue<OptimizationAction>());
            
            _ = Task.Run(() => ProcessQueueAsync(_cts.Token), _cts.Token);
        }

        public void Enqueue(OptimizationAction action)
        {
            if (!_queue.IsAddingCompleted)
            {
                _queue.Add(action);
            }
        }

        private async Task ProcessQueueAsync(CancellationToken token)
        {
            foreach (var action in _queue.GetConsumingEnumerable(token))
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                bool success = false;
                bool rollbackExecuted = false;
                Exception? error = null;

                try
                {
                    if (await action.CheckPreconditionsAsync())
                    {
                        await action.TakeSnapshotAsync();
                        
                        var preMetrics = await action.CollectPreMetricsAsync();
                        
                        await action.ApplyAsync();
                        
                        var postMetrics = await action.CollectPostMetricsAsync();
                        
                        success = true;

                        // Validação baseada em evidências
                        if (preMetrics != null && postMetrics != null)
                        {
                            bool evidencePassed = EvidenceValidator.Instance.ValidateOptimization(preMetrics, postMetrics);
                            if (!evidencePassed)
                            {
                                await AtomicRollbackManager.Instance.ExecuteRollbackAsync(action, "Evidência reprovou a otimização (Stutter / Regressão)");
                                rollbackExecuted = true;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    error = ex;
                    try
                    {
                        await action.RollbackAsync();
                        rollbackExecuted = true;
                    }
                    catch (Exception rollbackEx)
                    {
                        ProfessionalLogger.Instance.LogError("ExecutionQueue", $"Rollback failed for {action.ActionName}", rollbackEx);
                    }
                }
                finally
                {
                    stopwatch.Stop();
                    // O log profissional recebe os dados da transação
                    ProfessionalLogger.Instance.LogOptimizationTransaction(
                        engine: "OptimizationScheduler",
                        feature: action.ActionName,
                        stateBefore: action.GetStateBefore(),
                        stateAfter: action.GetStateAfter(),
                        metricsBefore: "OK", 
                        metricsAfter: "OK",
                        duration: stopwatch.Elapsed,
                        success: success,
                        rollbackExecuted: rollbackExecuted,
                        exception: error
                    );
                }
            }
        }

        public void Dispose()
        {
            _queue.CompleteAdding();
            _cts.Cancel();
            _queue.Dispose();
            _cts.Dispose();
        }
    }
}
