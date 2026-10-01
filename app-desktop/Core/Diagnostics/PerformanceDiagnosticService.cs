using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Core.Diagnostics
{
    public sealed class PerformanceDiagnosticService : IDisposable
    {
        private static readonly Lazy<PerformanceDiagnosticService> _instance = new(() => new PerformanceDiagnosticService());
        public static PerformanceDiagnosticService Instance => _instance.Value;

        private readonly ConcurrentDictionary<string, ServiceMetrics> _serviceMetrics = new();
        private readonly ConcurrentBag<TimerInfo> _trackedTimers = new();
        private readonly ConcurrentBag<EventSubscriptionInfo> _trackedEvents = new();
        private readonly ConcurrentBag<string> _activeMonitors = new();
        private readonly ConcurrentBag<string> _pollingLoops = new();
        private readonly ConcurrentDictionary<int, TaskInfo> _activeTasks = new();
        private readonly ConcurrentDictionary<string, long> _serviceInitTimes = new();
        private readonly List<DiagnosticSnapshot> _snapshotHistory = new();
        private readonly object _snapshotLock = new();
        private readonly Timer _samplingTimer;
        private readonly Stopwatch _processCpuStopwatch = Stopwatch.StartNew();
        private TimeSpan _lastProcessCpuTime;
        private bool _disposed;

        private const string DIAGNOSTIC_SOURCE = "PERF-DIAG";

        private PerformanceDiagnosticService()
        {
            _samplingTimer = new Timer(Sample, null, 5000, 5000);
        }

        public IDisposable TrackCpu(string source, [CallerFilePath] string filePath = "", [CallerLineNumber] int line = 0)
        {
            var metrics = _serviceMetrics.GetOrAdd(source, _ => new ServiceMetrics());
            metrics.CallCount++;
            var sw = Stopwatch.StartNew();
            return new CpuTrackerDisposable(sw, metrics, source, filePath, line);
        }

        public void RecordServiceInitTime(string serviceName, long elapsedMs)
        {
            _serviceInitTimes[serviceName] = elapsedMs;
        }

        public TimerInfo RegisterTimer(string name, string type, int intervalMs, string stackTrace = "")
        {
            var info = new TimerInfo
            {
                Name = name,
                Type = type,
                IntervalMs = intervalMs,
                CreatedAt = DateTime.UtcNow,
                CreatedByStackTrace = stackTrace
            };
            _trackedTimers.Add(info);
            return info;
        }

        public void MarkTimerTick(TimerInfo info)
        {
            info.TotalTicks++;
        }

        public EventSubscriptionInfo RegisterEvent(string eventSource, string handlerMethod, string subscriberClass, bool isLambda)
        {
            var info = new EventSubscriptionInfo
            {
                EventSource = eventSource,
                HandlerMethod = handlerMethod,
                SubscriberClass = subscriberClass,
                IsLambda = isLambda,
                SubscribedAt = DateTime.UtcNow
            };
            _trackedEvents.Add(info);
            return info;
        }

        public void RegisterMonitor(string monitorName)
        {
            _activeMonitors.Add(monitorName);
        }

        public void RegisterPollingLoop(string loopDescription, string filePath = "", int lineNumber = 0)
        {
            _pollingLoops.Add($"{loopDescription} ({filePath}:{lineNumber})");
        }

        public void TrackTask(Task task, string creationStackTrace = "")
        {
            _activeTasks[task.Id] = new TaskInfo
            {
                Id = task.Id,
                Status = task.Status.ToString(),
                CreationStackTrace = creationStackTrace,
                IsCompleted = task.IsCompleted,
                IsFaulted = task.IsFaulted,
                IsCanceled = task.IsCanceled
            };
        }

        public DiagnosticSnapshot TakeSnapshot()
        {
            var process = Process.GetCurrentProcess();
            process.Refresh();

            var cpuSample = new CpuSample
            {
                Source = "GLOBAL",
                CpuPercent = GetProcessCpuPercent(process),
                Timestamp = DateTime.UtcNow
            };

            var memSample = new MemorySample
            {
                WorkingSetMb = process.WorkingSet64 / 1024 / 1024,
                PrivateBytesMb = process.PrivateMemorySize64 / 1024 / 1024,
                ManagedHeapMb = GC.GetTotalMemory(false) / 1024 / 1024,
                Gen0Collections = GC.CollectionCount(0),
                Gen1Collections = GC.CollectionCount(1),
                Gen2Collections = GC.CollectionCount(2),
                Timestamp = DateTime.UtcNow
            };

            int workerThreads, completionPortThreads;
            ThreadPool.GetAvailableThreads(out workerThreads, out completionPortThreads);
            int maxWorkerThreads, maxCompletionPortThreads;
            ThreadPool.GetMaxThreads(out maxWorkerThreads, out maxCompletionPortThreads);

            var threadSample = new ThreadSample
            {
                TotalThreads = process.Threads.Count,
                ThreadPoolThreads = maxWorkerThreads - workerThreads,
                ActiveTasks = _activeTasks.Values.Where(t => !t.IsCompleted).ToList(),
                Timestamp = DateTime.UtcNow
            };

            var activeTimers = _trackedTimers.Where(t => t.IsRunning).ToList();
            var activeEvents = _trackedEvents.ToList();

            snapshot:
            var snapshot = new DiagnosticSnapshot
            {
                Timestamp = DateTime.UtcNow,
                Cpu = cpuSample,
                Memory = memSample,
                Threads = threadSample,
                Timers = activeTimers,
                EventSubscriptions = activeEvents,
                ServiceInitTimesMs = new Dictionary<string, long>(_serviceInitTimes),
                ActiveMonitors = _activeMonitors.ToList(),
                PollingLoops = _pollingLoops.ToList()
            };

            lock (_snapshotLock)
            {
                _snapshotHistory.Add(snapshot);
                if (_snapshotHistory.Count > 100)
                    _snapshotHistory.RemoveAt(0);
            }

            return snapshot;
        }

        private double GetProcessCpuPercent(Process process)
        {
            try
            {
                var currentCpuTime = process.TotalProcessorTime;
                var elapsed = _processCpuStopwatch.Elapsed;
                if (elapsed.TotalSeconds < 1) return 0;
                var cpuDelta = (currentCpuTime - _lastProcessCpuTime).TotalMilliseconds;
                var cpuPercent = (cpuDelta / elapsed.TotalMilliseconds) * 100.0 / Environment.ProcessorCount;
                _lastProcessCpuTime = currentCpuTime;
                _processCpuStopwatch.Restart();
                return Math.Round(cpuPercent, 1);
            }
            catch
            {
                return 0;
            }
        }

        public DiagnosticReport GenerateReport()
        {
            var latest = _snapshotHistory.LastOrDefault();
            if (latest == null) return new DiagnosticReport();

            var allServiceMetrics = _serviceMetrics.ToArray();
            var heaviestServices = allServiceMetrics
                .Where(s => s.Value.TotalCpuMs > 0)
                .OrderByDescending(s => s.Value.TotalCpuMs)
                .Take(5)
                .Select(s => $"{s.Key}: {s.Value.TotalCpuMs}ms CPU, {s.Value.CallCount} chamadas")
                .ToList();

            var timerBurden = _trackedTimers
                .OrderByDescending(t => t.TotalTicks)
                .Take(5)
                .Select(t => $"{t.Name} ({t.Type}): {t.IntervalMs}ms, {t.TotalTicks} ticks, criado em {t.CreatedByStackTrace}")
                .ToList();

            var eventBurden = _trackedEvents
                .GroupBy(e => e.SubscriberClass)
                .Select(g => $"{g.Key}: {g.Count()} subscriptions ({(g.Count(x => x.IsLambda) > 0 ? $"{g.Count(x => x.IsLambda)} lambdas" : "named methods")})")
                .ToList();

            var fastTimers = _trackedTimers
                .Where(t => t.IntervalMs < 500 && t.IsRunning)
                .Select(t => $"  ⚠ {t.Name}: {t.IntervalMs}ms - alta frequência!")
                .ToList();

            return new DiagnosticReport
            {
                GeneratedAt = DateTime.UtcNow,
                EstimatedCpuHeadroomPercent = latest.Cpu?.CpuPercent ?? 0,
                EstimatedRamReductionMb = latest.Memory?.ManagedHeapMb ?? 0,
                Evidence = new List<string>
                {
                    $"Consumo de CPU: {latest.Cpu?.CpuPercent}%",
                    $"Working Set: {latest.Memory?.WorkingSetMb}MB",
                    $"Managed Heap: {latest.Memory?.ManagedHeapMb}MB",
                    $"Threads totais: {latest.Threads?.TotalThreads}",
                    $"ThreadPool threads: {latest.Threads?.ThreadPoolThreads}",
                    $"Timers ativos: {_trackedTimers.Count(t => t.IsRunning)}",
                    $"Services monitorados: {allServiceMetrics.Length}",
                    $"Polling loops detectados: {_pollingLoops.Count}",
                    $"Active monitors: {_activeMonitors.Count}",
                    $"Timer de alta frequencia (<500ms): {fastTimers.Count}"
                },
                FixesApplied = new List<string>(),
                IntermittentExplanation = ""
            };
        }

        private void Sample(object? state)
        {
            if (_disposed) return;
            TakeSnapshot();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _samplingTimer.Dispose();
        }

        private sealed class ServiceMetrics
        {
            public long CallCount;
            public long TotalCpuMs;
        }

        private sealed class CpuTrackerDisposable : IDisposable
        {
            private readonly Stopwatch _sw;
            private readonly ServiceMetrics _metrics;
            private readonly string _source;

            public CpuTrackerDisposable(Stopwatch sw, ServiceMetrics metrics, string source, string filePath, int line)
            {
                _sw = sw;
                _metrics = metrics;
                _source = source;
            }

            public void Dispose()
            {
                _sw.Stop();
                Interlocked.Add(ref _metrics.TotalCpuMs, _sw.ElapsedMilliseconds);
            }
        }
    }
}
