using System;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Helpers
{
    public static class SafeTask
    {
        private static ILoggingService? Logger => _logger ??= TryGetLogger();
        private static ILoggingService? _logger;

        private static ILoggingService? TryGetLogger()
        {
            try { return (ILoggingService?)System.Windows.Application.Current?.Properties["LoggingService"]; }
            catch { return null; }
        }

        public static void FireAndForget(this Task task, ILoggingService? logger = null)
        {
            task.ContinueWith(t =>
            {
                var log = logger ?? Logger;
                log?.LogError($"[SafeTask] Unobserved exception in FireAndForget: {t.Exception?.Flatten().InnerException?.Message}", t.Exception);
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        public static Task Run(Action action, CancellationToken cancellationToken = default, ILoggingService? logger = null)
        {
            return Task.Run(() =>
            {
                try
                {
                    action();
                }
                catch (OperationCanceledException) { /* Ignored */ }
                catch (Exception ex)
                {
                    var log = logger ?? Logger;
                    log?.LogError($"[SafeTask] Unhandled exception in SafeTask.Run: {ex.Message}", ex);
                    throw; 
                }
            }, cancellationToken);
        }

        public static Task Run(Func<Task> function, CancellationToken cancellationToken = default, ILoggingService? logger = null)
        {
            return Task.Run(async () =>
            {
                try
                {
                    await function();
                }
                catch (OperationCanceledException) { /* Ignored */ }
                catch (Exception ex)
                {
                    var log = logger ?? Logger;
                    log?.LogError($"[SafeTask] Unhandled exception in async SafeTask.Run: {ex.Message}", ex);
                    throw;
                }
            }, cancellationToken);
        }

        public static Task<TResult> Run<TResult>(Func<TResult> function, CancellationToken cancellationToken = default, ILoggingService? logger = null)
        {
            return Task.Run(() =>
            {
                try
                {
                    return function();
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    var log = logger ?? Logger;
                    log?.LogError($"[SafeTask] Unhandled exception in SafeTask.Run<{typeof(TResult).Name}>: {ex.Message}", ex);
                    throw; 
                }
            }, cancellationToken);
        }

        public static Task<TResult> Run<TResult>(Func<Task<TResult>> function, CancellationToken cancellationToken = default, ILoggingService? logger = null)
        {
            return Task.Run(async () =>
            {
                try
                {
                    return await function();
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    var log = logger ?? Logger;
                    log?.LogError($"[SafeTask] Unhandled exception in async SafeTask.Run<{typeof(TResult).Name}>: {ex.Message}", ex);
                    throw;
                }
            }, cancellationToken);
        }
    }
}
