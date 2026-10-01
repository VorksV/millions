using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Scheduling
{
    public enum SchedulerPriority
    {
        Critical = 0,    // Every 50ms-100ms
        Normal = 1,      // Every 1s
        Background = 2,  // Every 5s
        Idle = 3         // Every 30s+
    }

    public interface ICentralizedBackgroundScheduler
    {
        void RegisterTask(string taskId, Action taskAction, SchedulerPriority priority, TimeSpan interval);
        void RegisterTaskAsync(string taskId, Func<CancellationToken, Task> taskAction, SchedulerPriority priority, TimeSpan interval);
        void UnregisterTask(string taskId);
    }

    /// <summary>
    /// Centralized Scheduler designed to drastically reduce the number of thread-pool wakeups.
    /// Em vez de 50 timers diferentes acordando a CPU a todo momento (causando stuttering),
    /// agrupamos todas as tarefas do Voltris por prioridade.
    /// </summary>
    public class CentralizedBackgroundScheduler : ICentralizedBackgroundScheduler, IDisposable
    {
        public static ICentralizedBackgroundScheduler Global { get; set; } = null!;
        private readonly ILoggingService? _logger;
        private readonly ConcurrentDictionary<string, ScheduledTask> _tasks = new();
        
        private CancellationTokenSource? _globalCts;
        private readonly Task[] _workers = new Task[4];
        private bool _disposed;

        public CentralizedBackgroundScheduler(ILoggingService? logger = null)
        {
            Global = this;
            _logger = logger;
            _globalCts = new CancellationTokenSource();
            
            // Inicia 4 loops principais, um para cada prioridade
            _workers[0] = Task.Run(() => WorkerLoopAsync(SchedulerPriority.Critical, 1000, _globalCts.Token));
            _workers[1] = Task.Run(() => WorkerLoopAsync(SchedulerPriority.Normal, 2000, _globalCts.Token));
            _workers[2] = Task.Run(() => WorkerLoopAsync(SchedulerPriority.Background, 5000, _globalCts.Token));
            _workers[3] = Task.Run(() => WorkerLoopAsync(SchedulerPriority.Idle, 30000, _globalCts.Token));
            
            _logger?.LogInfo("[Scheduler] CentralizedBackgroundScheduler inicializado com 4 níveis de prioridade.");
        }

        public void RegisterTask(string taskId, Action taskAction, SchedulerPriority priority, TimeSpan interval)
        {
            if (_disposed) return;
            
            _tasks[taskId] = new ScheduledTask
            {
                Id = taskId,
                SyncAction = taskAction,
                Priority = priority,
                Interval = interval,
                LastRun = DateTime.MinValue,
                IsRunning = false
            };
        }

        public void RegisterTaskAsync(string taskId, Func<CancellationToken, Task> taskAction, SchedulerPriority priority, TimeSpan interval)
        {
            if (_disposed) return;
            
            _tasks[taskId] = new ScheduledTask
            {
                Id = taskId,
                AsyncAction = taskAction,
                Priority = priority,
                Interval = interval,
                LastRun = DateTime.MinValue,
                IsRunning = false
            };
        }

        public void UnregisterTask(string taskId)
        {
            _tasks.TryRemove(taskId, out _);
        }

        private async Task WorkerLoopAsync(SchedulerPriority priority, int baseDelayMs, CancellationToken ct)
        {
            VoltrisOptimizer.Services.Diagnostics.ThreadNameRegistry.Register($"Scheduler-{priority}");
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    // Se não há tarefas registradas para esta prioridade, dorme 30s
                    if (!_tasks.Values.Any(t => t.Priority == priority))
                    {
                        await Task.Delay(30000, ct);
                        continue;
                    }

                    await Task.Delay(baseDelayMs, ct);
                    
                    var now = DateTime.UtcNow;
                    var tasksToRun = _tasks.Values
                        .Where(t => t.Priority == priority && !t.IsRunning && (now - t.LastRun) >= t.Interval)
                        .ToList();

                    if (tasksToRun.Count == 0) continue;

                    using var profiler = VoltrisOptimizer.Services.Diagnostics.CpuSelfProfiler.Instance.BeginSection($"Scheduler.Run.{priority}");

                    foreach (var t in tasksToRun)
                    {
                        t.IsRunning = true;
                    }

                    // Dispara todas as tarefas desse nível concorrentemente, sem bloquear o loop principal
                    _ = Task.Run(async () =>
                    {
                        var tasks = new List<Task>();
                        foreach (var t in tasksToRun)
                        {
                            tasks.Add(Task.Run(async () =>
                            {
                                try
                                {
                                    if (t.AsyncAction != null) await t.AsyncAction(ct);
                                    else if (t.SyncAction != null) t.SyncAction();
                                }
                                catch (Exception ex)
                                {
                                    _logger?.LogWarning($"[Scheduler] Falha na task '{t.Id}': {ex.Message}");
                                }
                                finally
                                {
                                    t.LastRun = DateTime.UtcNow;
                                    t.IsRunning = false;
                                }
                            }));
                        }
                        
                        await Task.WhenAll(tasks);
                    }, ct);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger?.LogError($"[Scheduler] Loop '{priority}' abortado inesperadamente: {ex.Message}");
            }
            finally
            {
                VoltrisOptimizer.Services.Diagnostics.ThreadNameRegistry.Unregister();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _globalCts?.Cancel();
            try { Task.WaitAll(_workers, 2000); } catch { }
            _globalCts?.Dispose();
            _tasks.Clear();
            _logger?.LogInfo("[Scheduler] CentralizedBackgroundScheduler finalizado.");
        }

        private class ScheduledTask
        {
            public string Id { get; set; } = string.Empty;
            public Action? SyncAction { get; set; }
            public Func<CancellationToken, Task>? AsyncAction { get; set; }
            public SchedulerPriority Priority { get; set; }
            public TimeSpan Interval { get; set; }
            public DateTime LastRun { get; set; }
            public bool IsRunning { get; set; }
        }
    }
}

