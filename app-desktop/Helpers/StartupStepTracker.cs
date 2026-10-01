using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;

#nullable enable

namespace VoltrisOptimizer.Helpers
{
    /// <summary>
    /// Rastreador profissional de etapas de inicialização.
    /// Detecta travamentos, timeouts, deadlocks e falhas silenciosas no startup.
    /// </summary>
    public sealed class StartupStepTracker : IDisposable
    {
        private readonly ILoggingService? _logger;
        private readonly ConcurrentDictionary<string, StepEntry> _steps = new();
        private readonly Stopwatch _globalSw = Stopwatch.StartNew();
        private readonly Timer _watchdogTimer;
        private readonly object _logLock = new();
        private readonly string _logPath;
        private readonly int _timeoutSeconds;
        private volatile bool _disposed;
        private volatile bool _startupCompleted;

        public static StartupStepTracker? Instance { get; private set; }

        public StartupStepTracker(ILoggingService? logger, int timeoutSeconds = 45)
        {
            _logger = logger;
            _timeoutSeconds = timeoutSeconds;
            var logDir = LogDirectoryResolver.Resolve();
            Directory.CreateDirectory(logDir);
            _logPath = Path.Combine(logDir, $"StartupSteps_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");

            WriteLogLine("[WATCHDOG] StartupStepTracker inicializado");
            WriteLogLine($"[WATCHDOG] Timeout configurado: {timeoutSeconds}s");
            WriteLogLine($"[WATCHDOG] Thread ID: {Thread.CurrentThread.ManagedThreadId}");
            WriteLogLine($"[WATCHDOG] Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");

            // Timer que verifica timeouts a cada 2 segundos
            _watchdogTimer = new Timer(CheckTimeouts, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
            Instance = this;
        }

        /// <summary>
        /// Inicia uma etapa de startup
        /// </summary>
        public void Begin(string stepName, string? details = null)
        {
            if (_disposed) return;

            var entry = new StepEntry
            {
                Name = stepName,
                StartTicks = _globalSw.ElapsedTicks,
                StartMs = _globalSw.ElapsedMilliseconds,
                ThreadId = Thread.CurrentThread.ManagedThreadId,
                Details = details,
                Status = StepStatus.Running
            };

            _steps[stepName] = entry;

            var msg = $"[STARTUP][{_globalSw.ElapsedMilliseconds:D6}ms][Thread:{Thread.CurrentThread.ManagedThreadId:D2}] BEGIN: {stepName}";
            if (!string.IsNullOrEmpty(details))
                msg += $" | {details}";

            WriteLogLine(msg);
            _logger?.LogInfo(msg);
        }

        /// <summary>
        /// Finaliza uma etapa com sucesso
        /// </summary>
        public void End(string stepName, string? result = null)
        {
            if (_disposed) return;

            if (_steps.TryGetValue(stepName, out var entry))
            {
                entry.EndTicks = _globalSw.ElapsedTicks;
                entry.EndMs = _globalSw.ElapsedMilliseconds;
                entry.Status = StepStatus.Completed;
                entry.Result = result;

                var duration = entry.EndMs - entry.StartMs;
                var msg = $"[STARTUP][{_globalSw.ElapsedMilliseconds:D6}ms][Thread:{Thread.CurrentThread.ManagedThreadId:D2}] END: {stepName} | Duration: {duration}ms";
                if (!string.IsNullOrEmpty(result))
                    msg += $" | Result: {result}";

                WriteLogLine(msg);
                _logger?.LogInfo(msg);
            }
            else
            {
                var msg = $"[STARTUP][{_globalSw.ElapsedMilliseconds:D6}ms][Thread:{Thread.CurrentThread.ManagedThreadId:D2}] END (orphan): {stepName}";
                WriteLogLine(msg);
            }
        }

        /// <summary>
        /// Marca uma etapa com erro
        /// </summary>
        public void Fail(string stepName, Exception ex, string? context = null)
        {
            if (_disposed) return;

            if (_steps.TryGetValue(stepName, out var entry))
            {
                entry.EndMs = _globalSw.ElapsedMilliseconds;
                entry.Status = StepStatus.Failed;
                entry.Exception = ex;
            }

            var msg = $"[STARTUP][{_globalSw.ElapsedMilliseconds:D6}ms][Thread:{Thread.CurrentThread.ManagedThreadId:D2}] FAIL: {stepName}";
            if (!string.IsNullOrEmpty(context))
                msg += $" | Context: {context}";
            msg += $"\n  Exception: {ex.GetType().Name}: {ex.Message}";
            if (ex.InnerException != null)
                msg += $"\n  Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
            msg += $"\n  StackTrace: {ex.StackTrace}";

            WriteLogLine(msg);
            _logger?.LogError(msg, ex);

        }

        /// <summary>
        /// Registra um evento arbitrário no startup
        /// </summary>
        public void Event(string eventName, string? details = null)
        {
            if (_disposed) return;

            var msg = $"[STARTUP][{_globalSw.ElapsedMilliseconds:D6}ms][Thread:{Thread.CurrentThread.ManagedThreadId:D2}] EVENT: {eventName}";
            if (!string.IsNullOrEmpty(details))
                msg += $" | {details}";

            WriteLogLine(msg);
            _logger?.LogInfo(msg);
        }

        /// <summary>
        /// Marca o startup como concluído
        /// </summary>
        public void MarkCompleted()
        {
            if (_disposed) return;
            _startupCompleted = true;

            var totalMs = _globalSw.ElapsedMilliseconds;
            var completed = _steps.Values.Count(s => s.Status == StepStatus.Completed);
            var failed = _steps.Values.Count(s => s.Status == StepStatus.Failed);
            var running = _steps.Values.Count(s => s.Status == StepStatus.Running);

            var summary = new StringBuilder();
            summary.AppendLine("╔══════════════════════════════════════════════════════════════╗");
            summary.AppendLine("║           STARTUP COMPLETION REPORT                          ║");
            summary.AppendLine($"║  Total Time: {totalMs}ms");
            summary.AppendLine($"║  Steps Completed: {completed}");
            summary.AppendLine($"║  Steps Failed: {failed}");
            summary.AppendLine($"║  Steps Still Running: {running}");
            summary.AppendLine("╚══════════════════════════════════════════════════════════════╝");

            if (running > 0)
            {
                summary.AppendLine("⚠️ WARNING: Steps still running at completion:");
                foreach (var s in _steps.Values.Where(s => s.Status == StepStatus.Running))
                {
                    var elapsed = totalMs - s.StartMs;
                    summary.AppendLine($"  - {s.Name} (running for {elapsed}ms on thread {s.ThreadId})");
                }
            }

            var summaryStr = summary.ToString();
            WriteLogLine(summaryStr);
            _logger?.LogInfo(summaryStr);

        }

        // Controla rate-limit dos alertas por step (evita inundar o log com o mesmo alerta a cada 2s)
        private readonly ConcurrentDictionary<string, (long LastAlertMs, int AlertCount)> _alertRateLimit = new();

        /// <summary>
        /// Verifica se há etapas travadas (timeout) com backoff exponencial para não poluir logs.
        /// </summary>
        private void CheckTimeouts(object? state)
        {
            if (_disposed || _startupCompleted) return;

            var now = _globalSw.ElapsedMilliseconds;
            var stuckSteps = _steps.Values
                .Where(s => s.Status == StepStatus.Running && (now - s.StartMs) > _timeoutSeconds * 1000)
                .ToList();

            foreach (var step in stuckSteps)
            {
                var elapsedSec = (now - step.StartMs) / 1000.0;

                // Backoff exponencial: primeiro alerta imediato, depois 10s, 30s, 60s...
                // Evita centenas de linhas idênticas para o mesmo step preso.
                var (lastAlertMs, alertCount) = _alertRateLimit.GetOrAdd(step.Name, _ => (0L, 0));
				long backoffMs = alertCount switch
				{
					0 => 0,
					1 => 30_000,
					2 => 60_000,
					_ => 120_000
				};

                if (now - lastAlertMs < backoffMs)
                    continue;

                _alertRateLimit[step.Name] = (now, alertCount + 1);

                var alert = $"[WATCHDOG][{now:D6}ms] ⚠️ STUCK STEP DETECTED: {step.Name}\n" +
                           $"  Running for: {elapsedSec:F1}s (timeout: {_timeoutSeconds}s)\n" +
                           $"  Thread ID: {step.ThreadId}\n" +
                           $"  Started at: {step.StartMs}ms\n" +
                           $"  Details: {step.Details ?? "N/A"}";

                WriteLogLine(alert);
                _logger?.LogWarning(alert);

            }
        }

        /// <summary>
        /// Dump de todas as threads do processo para diagnóstico
        /// </summary>
        public void DumpThreadStates()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("=========== THREAD DUMP ===========");
                var proc = Process.GetCurrentProcess();
                foreach (ProcessThread pt in proc.Threads)
                {
                    try
                    {
                        sb.AppendLine($"  TID={pt.Id} state={pt.ThreadState} waitReason={(pt.ThreadState == System.Diagnostics.ThreadState.Wait ? pt.WaitReason.ToString() : "-")} cpuTime={pt.TotalProcessorTime.TotalMilliseconds:F0}ms");
                    }
                    catch { }
                }
                sb.AppendLine("===================================");
                WriteLogLine(sb.ToString());
            }
            catch (Exception ex)
            {
                WriteLogLine($"[WATCHDOG] Failed to dump threads: {ex.Message}");
            }
        }

        private void WriteLogLine(string line)
        {
            try
            {
                lock (_logLock)
                {
                    File.AppendAllText(_logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {line}{Environment.NewLine}", System.Text.Encoding.UTF8);
                }
            }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _watchdogTimer?.Dispose();
            Instance = null;
        }

        private class StepEntry
        {
            public string Name { get; set; } = "";
            public long StartTicks { get; set; }
            public long StartMs { get; set; }
            public long EndTicks { get; set; }
            public long EndMs { get; set; }
            public int ThreadId { get; set; }
            public string? Details { get; set; }
            public StepStatus Status { get; set; }
            public string? Result { get; set; }
            public Exception? Exception { get; set; }
        }

        private enum StepStatus
        {
            Running,
            Completed,
            Failed
        }
    }
}
