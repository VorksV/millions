using System;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Helpers
{
    /// <summary>
    /// Helper para operações assíncronas, substituindo Thread.Sleep bloqueante
    /// </summary>
    public static class AsyncHelper
    {
        /// <summary>
        /// Aguarda de forma assíncrona (substitui Thread.Sleep)
        /// </summary>
        public static Task DelayAsync(int milliseconds, CancellationToken cancellationToken = default)
        {
            if (milliseconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(milliseconds), "Delay must be non-negative");
            }

            return Task.Delay(milliseconds, cancellationToken);
        }

        /// <summary>
        /// Aguarda de forma assíncrona com TimeSpan
        /// </summary>
        public static Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
        {
            if (delay < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(delay), "Delay must be non-negative");
            }

            return Task.Delay(delay, cancellationToken);
        }

        /// <summary>
        /// Executa uma ação com retry exponencial backoff
        /// </summary>
        public static async Task<T> RetryAsync<T>(
            Func<Task<T>> operation,
            int maxRetries = 3,
            int initialDelayMs = 100,
            CancellationToken cancellationToken = default)
        {
            int retryCount = 0;
            while (true)
            {
                try
                {
                    return await operation().ConfigureAwait(false);
                }
                catch (Exception) when (retryCount < maxRetries)
                {
                    retryCount++;
                    var delay = initialDelayMs * (int)Math.Pow(2, retryCount - 1);
                    await DelayAsync(delay, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Executa uma ação com retry exponencial backoff (sem retorno)
        /// </summary>
        public static async Task RetryAsync(
            Func<Task> operation,
            int maxRetries = 3,
            int initialDelayMs = 100,
            CancellationToken cancellationToken = default)
        {
            int retryCount = 0;
            while (true)
            {
                try
                {
                    await operation().ConfigureAwait(false);
                    return;
                }
                catch (Exception) when (retryCount < maxRetries)
                {
                    retryCount++;
                    var delay = initialDelayMs * (int)Math.Pow(2, retryCount - 1);
                    await DelayAsync(delay, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Executa uma operação com timeout
        /// </summary>
        public static async Task<T> WithTimeoutAsync<T>(
            Func<Task<T>> operation,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"Operation timed out after {timeout.TotalSeconds} seconds");
            }
        }

        /// <summary>
        /// Executa uma operação com timeout (sem retorno)
        /// </summary>
        public static async Task WithTimeoutAsync(
            Func<Task> operation,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            try
            {
                await operation().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"Operation timed out after {timeout.TotalSeconds} seconds");
            }
        }

        /// <summary>
        /// Executa múltiplas tasks em paralelo e aguarda todas
        /// </summary>
        public static async Task WhenAllAsync(params Task[] tasks)
        {
            if (tasks == null || tasks.Length == 0)
            {
                return;
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        /// <summary>
        /// Executa múltiplas tasks em paralelo e aguarda a primeira a completar
        /// </summary>
        public static async Task<Task> WhenAnyAsync(params Task[] tasks)
        {
            if (tasks == null || tasks.Length == 0)
            {
                throw new ArgumentException("At least one task is required", nameof(tasks));
            }

            return await Task.WhenAny(tasks).ConfigureAwait(false);
        }

        /// <summary>
        /// Cria um CancellationTokenSource com timeout
        /// </summary>
        public static CancellationTokenSource CreateTimeoutToken(TimeSpan timeout)
        {
            return new CancellationTokenSource(timeout);
        }

        /// <summary>
        /// Cria um CancellationTokenSource com timeout em milissegundos
        /// </summary>
        public static CancellationTokenSource CreateTimeoutToken(int milliseconds)
        {
            return new CancellationTokenSource(milliseconds);
        }

        /// <summary>
        /// Executa uma Task em background com tratamento automático de exceções não observadas
        /// Ideal para fire-and-forget seguro
        /// </summary>
        public static async void FireAndForgetSafe(this Task task, Action<Exception>? onError = null)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception ex) when (!System.Diagnostics.Debugger.IsAttached)
            {
                onError?.Invoke(ex);
            }
        }

        /// <summary>
        /// Cria uma Task com tratamento embutido para exceções não observadas
        /// Previne o erro [UNOBSERVED TASK EXCEPTION]
        /// </summary>
        public static Task SafeTaskRun(Func<Task> action, CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                try
                {
                    await action().ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Cancelamento esperado - não tratar como erro
                }
                catch (Exception ex)
                {
                    // Logar mas não relançar - previne UnobservedTaskException
                    System.Diagnostics.Debug.WriteLine($"[SafeTaskRun] Exceção não observada capturada: {ex.Message}");
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Cria uma Task com retorno e tratamento embutido para exceções não observadas
        /// </summary>
        public static Task<T> SafeTaskRun<T>(Func<Task<T>> action, T defaultValue = default!, CancellationToken cancellationToken = default)
        {
            return Task.Run(async () =>
            {
                try
                {
                    return await action().ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return defaultValue;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SafeTaskRun<T>] Exceção não observada capturada: {ex.Message}");
                    return defaultValue;
                }
            }, cancellationToken);
        }
    }
}
