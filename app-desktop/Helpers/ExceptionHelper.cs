using System;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Helpers
{
    /// <summary>
    /// Helper para tratamento de exceções de forma padronizada
    /// Substitui catches vazios por tratamento explícito
    /// </summary>
    public static class ExceptionHelper
    {
        private static Services.ILoggingService? _logger;
        
        /// <summary>
        /// Inicializa o helper com o serviço de logging
        /// </summary>
        public static void Initialize(Services.ILoggingService? logger)
        {
            _logger = logger;
        }
        
        /// <summary>
        /// Executa uma ação ignorando exceções específicas
        /// Útil para operações de cleanup onde falha é aceitável
        /// </summary>
        public static void TryIgnore(Action action, bool logWarning = false, string? context = null)
        {
            try
            {
                action();
            }
            catch (Exception ex) when (IsIgnorableException(ex))
            {
                if (logWarning)
                {
                    _logger?.LogWarning($"[TryIgnore] {context ?? "Operação"}: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Executa uma ação e retorna valor default se falhar
        /// </summary>
        public static T? TryOrDefault<T>(Func<T> action, T? defaultValue = default, bool logWarning = false, string? context = null)
        {
            try
            {
                return action();
            }
            catch (Exception ex) when (IsIgnorableException(ex))
            {
                if (logWarning)
                {
                    _logger?.LogWarning($"[TryOrDefault] {context ?? "Operação"}: {ex.Message}");
                }
                return defaultValue;
            }
        }
        
        /// <summary>
        /// Executa uma ação assíncrona ignorando exceções específicas
        /// </summary>
        public static async Task TryIgnoreAsync(Func<Task> action, bool logWarning = false, string? context = null)
        {
            try
            {
                await action();
            }
            catch (Exception ex) when (IsIgnorableException(ex))
            {
                if (logWarning)
                {
                    _logger?.LogWarning($"[TryIgnoreAsync] {context ?? "Operação"}: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Executa uma ação assíncrona e retorna valor default se falhar
        /// </summary>
        public static async Task<T?> TryOrDefaultAsync<T>(Func<Task<T>> action, T? defaultValue = default, bool logWarning = false, string? context = null)
        {
            try
            {
                return await action();
            }
            catch (Exception ex) when (IsIgnorableException(ex))
            {
                if (logWarning)
                {
                    _logger?.LogWarning($"[TryOrDefaultAsync] {context ?? "Operação"}: {ex.Message}");
                }
                return defaultValue;
            }
        }
        
        /// <summary>
        /// Tenta executar com retry
        /// </summary>
        public static bool TryWithRetry(Action action, int maxAttempts = 3, int delayMs = 100, bool logWarning = true)
        {
            for (int i = 0; i < maxAttempts; i++)
            {
                try
                {
                    action();
                    return true;
                }
                catch (Exception ex)
                {
                    if (i == maxAttempts - 1)
                    {
                        if (logWarning)
                        {
                            _logger?.LogWarning($"[TryWithRetry] Falhou após {maxAttempts} tentativas: {ex.Message}");
                        }
                        return false;
                    }
                    
                    System.Threading.Thread.Sleep(delayMs);
                }
            }
            return false;
        }
        
        /// <summary>
        /// Tenta matar um processo de forma segura
        /// </summary>
        public static void TryKillProcess(System.Diagnostics.Process? process)
        {
            if (process == null) return;
            
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch (InvalidOperationException)
            {
                // Processo já terminou - ignorar
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Não tem permissão para matar - ignorar
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[TryKillProcess] Não foi possível encerrar processo: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Tenta fazer dispose de forma segura
        /// </summary>
        public static void TryDispose(IDisposable? disposable)
        {
            if (disposable == null) return;
            
            try
            {
                disposable.Dispose();
            }
            catch
            {
                // Dispose nunca deve lançar exceção, mas se lançar, ignorar
            }
        }
        
        /// <summary>
        /// Tenta deletar arquivo de forma segura
        /// </summary>
        public static bool TryDeleteFile(string path, bool logWarning = false)
        {
            try
            {
                if (System.IO.File.Exists(path))
                {
                    System.IO.File.Delete(path);
                    return true;
                }
            }
            catch (Exception ex) when (ex is System.IO.IOException || 
                                       ex is UnauthorizedAccessException ||
                                       ex is System.IO.PathTooLongException)
            {
                if (logWarning)
                {
                    _logger?.LogWarning($"[TryDeleteFile] Não foi possível deletar {path}: {ex.Message}");
                }
            }
            return false;
        }
        
        /// <summary>
        /// Tenta deletar diretório de forma segura
        /// </summary>
        public static bool TryDeleteDirectory(string path, bool recursive = true, bool logWarning = false)
        {
            try
            {
                if (System.IO.Directory.Exists(path))
                {
                    System.IO.Directory.Delete(path, recursive);
                    return true;
                }
            }
            catch (Exception ex) when (ex is System.IO.IOException || 
                                       ex is UnauthorizedAccessException ||
                                       ex is System.IO.PathTooLongException)
            {
                if (logWarning)
                {
                    _logger?.LogWarning($"[TryDeleteDirectory] Não foi possível deletar {path}: {ex.Message}");
                }
            }
            return false;
        }
        
        /// <summary>
        /// Verifica se a exceção é do tipo que pode ser ignorada
        /// </summary>
        private static bool IsIgnorableException(Exception ex)
        {
            return ex is System.IO.IOException ||
                   ex is UnauthorizedAccessException ||
                   ex is System.IO.PathTooLongException ||
                   ex is InvalidOperationException ||
                   ex is System.ComponentModel.Win32Exception ||
                   ex is ArgumentException ||
                   ex is TimeoutException;
        }

        /// <summary>
        /// Executa uma função assíncrona com tratamento completo para UnobservedTaskException
        /// </summary>
        public static async Task SafeExecuteAsync(Func<Task> action, string context = "Operação", bool logError = true)
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (System.Threading.CancellationToken.None.IsCancellationRequested)
            {
                // Cancelamento esperado
            }
            catch (Exception ex) when (IsIgnorableException(ex))
            {
                if (logError)
                {
                    _logger?.LogWarning($"[SafeExecuteAsync] {context}: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                if (logError)
                {
                    _logger?.LogError($"[SafeExecuteAsync] {context} falhou", ex);
                }
            }
        }

        /// <summary>
        /// Executa uma função assíncrona com retorno e tratamento completo
        /// </summary>
        public static async Task<T?> SafeExecuteAsync<T>(Func<Task<T>> action, T defaultValue = default!, string context = "Operação", bool logError = true)
        {
            try
            {
                return await action().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return defaultValue;
            }
            catch (Exception ex) when (IsIgnorableException(ex))
            {
                if (logError)
                {
                    _logger?.LogWarning($"[SafeExecuteAsync<T>] {context}: {ex.Message}");
                }
                return defaultValue;
            }
            catch (Exception ex)
            {
                if (logError)
                {
                    _logger?.LogError($"[SafeExecuteAsync<T>] {context} falhou", ex);
                }
                return defaultValue;
            }
        }
    }
}

