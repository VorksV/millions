using System;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Models;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Helpers
{
    public static class ProgressBridge
    {
        public static Progress<T> CreateProgress<T>(
            string operationName,
            bool isPriority = true,
            Action<T>? onReport = null)
        {
            var token = GlobalProgressService.Instance.BeginOperation(operationName, isPriority);
            return new Progress<T>(value =>
            {
                if (value is int intVal)
                {
                    GlobalProgressService.Instance.UpdateProgress(intVal, null);
                }
                else if (value is double dblVal)
                {
                    GlobalProgressService.Instance.UpdateProgress((int)Math.Round(dblVal), null);
                }
                else if (value is ValueTuple<int, string> tuple)
                {
                    GlobalProgressService.Instance.UpdateProgress(tuple.Item1, tuple.Item2);
                }
                else if (value is ValueTuple<double, string> dblTuple)
                {
                    GlobalProgressService.Instance.UpdateProgress((int)Math.Round(dblTuple.Item1), dblTuple.Item2);
                }
                else if (value is string strVal)
                {
                    GlobalProgressService.Instance.UpdateProgress(0, strVal);
                }
                else if (value is OperationProgress opProgress)
                {
                    GlobalProgressService.Instance.UpdateProgress((int)Math.Round(opProgress.Percentage), opProgress.Message);
                }
                else if (value is AnalysisProgress analysisProgress)
                {
                    GlobalProgressService.Instance.UpdateProgress(analysisProgress.PercentComplete, analysisProgress.CurrentItem);
                }
                else if (value is CleanupProgress cleanupProgress)
                {
                    GlobalProgressService.Instance.UpdateProgress(cleanupProgress.PercentComplete, cleanupProgress.CurrentItem);
                }

                onReport?.Invoke(value);
            });
        }

        public static IDisposable BeginOperation(string operationName, bool isPriority = true)
        {
            return GlobalProgressService.Instance.BeginOperation(operationName, isPriority);
        }

        public static void Report(string operationName, int percent, string? message = null)
        {
            GlobalProgressService.Instance.UpdateProgress(percent, message ?? operationName);
        }

        public static void Complete(string? finalMessage = null)
        {
            GlobalProgressService.Instance.CompleteOperation(finalMessage);
        }

        public static async Task<T> TrackAsync<T>(
            string operationName,
            Func<Task<T>> operation,
            bool isPriority = true)
        {
            using var token = GlobalProgressService.Instance.BeginOperation(operationName, isPriority);
            try
            {
                GlobalProgressService.Instance.UpdateProgress(0, $"Iniciando: {operationName}");
                var result = await operation();
                GlobalProgressService.Instance.UpdateProgress(100, $"✓ {operationName} concluído");
                return result;
            }
            catch (Exception ex)
            {
                GlobalProgressService.Instance.UpdateProgress(0, $"✗ {operationName} falhou: {ex.Message}");
                throw;
            }
        }

        public static async Task TrackAsync(
            string operationName,
            Func<Task> operation,
            bool isPriority = true)
        {
            using var token = GlobalProgressService.Instance.BeginOperation(operationName, isPriority);
            try
            {
                GlobalProgressService.Instance.UpdateProgress(0, $"Iniciando: {operationName}");
                await operation();
                GlobalProgressService.Instance.UpdateProgress(100, $"✓ {operationName} concluído");
            }
            catch (Exception ex)
            {
                GlobalProgressService.Instance.UpdateProgress(0, $"✗ {operationName} falhou: {ex.Message}");
                throw;
            }
        }

        public static TrackedProgress<T> WrapExisting<T>(string operationName, IProgress<T>? existing, bool isPriority = true)
        {
            var token = GlobalProgressService.Instance.BeginOperation(operationName, isPriority);
            return new TrackedProgress<T>(token, existing);
        }

        public static bool IsTokenDisposed(OperationToken token)
        {
            return token.IsCompleted;
        }
    }

    /// <summary>
    /// Wrapper de progresso seguro (SaaS-Level) que implementa IDisposable e IProgress.
    /// Garante que o OperationToken associado seja liberado e completado no rodapé assim que a tarefa correspondente é concluída ou destruída.
    /// </summary>
    public sealed class TrackedProgress<T> : IProgress<T>, IDisposable
    {
        private readonly OperationToken _token;
        private readonly IProgress<T>? _existing;
        private int _disposed = 0;

        public TrackedProgress(OperationToken token, IProgress<T>? existing)
        {
            _token = token ?? throw new ArgumentNullException(nameof(token));
            _existing = existing;
        }

        public void Report(T value)
        {
            if (_disposed == 1 || _token.IsCompleted) return;

            try
            {
                // Encaminhar atualizações de progresso para a fila global
                if (value is ValueTuple<int, string> tuple)
                    _token.UpdateProgress(tuple.Item1, tuple.Item2);
                else if (value is OperationProgress op)
                    _token.UpdateProgress((int)Math.Round(op.Percentage), op.Message);
                else if (value is int pct)
                    _token.UpdateProgress(pct, null);
                else if (value is double dbl)
                    _token.UpdateProgress((int)Math.Round(dbl), null);
                else if (value is string msg)
                    _token.UpdateProgress(0, msg);
                else if (value is AnalysisProgress analysisProgress)
                    _token.UpdateProgress(analysisProgress.PercentComplete, analysisProgress.CurrentItem);
                else if (value is CleanupProgress cleanupProgress)
                    _token.UpdateProgress(cleanupProgress.PercentComplete, cleanupProgress.CurrentItem);
            }
            catch
            {
                // Prevenir que falhas de envio de progresso quebrem o fluxo principal
            }

            // Propagar para o manipulador original da UI
            _existing?.Report(value);
        }

        private void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                try
                {
                    _token.Complete();
                }
                catch
                {
                    // Prevenir exceções na destruição do objeto
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~TrackedProgress()
        {
            Dispose(false);
        }
    }
}
