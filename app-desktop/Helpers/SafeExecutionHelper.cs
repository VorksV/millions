using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace VoltrisOptimizer.Helpers
{
    /// <summary>
    /// Helper para execução segura de código com tratamento adequado de exceções
    /// </summary>
    public static class SafeExecutionHelper
    {
        /// <summary>
        /// Executa uma ação de forma segura, logando exceções adequadamente
        /// </summary>
        public static void SafeExecute(
            Action action,
            Services.ILoggingService? logger = null,
            string? operationName = null,
            [CallerMemberName] string callerName = "",
            [CallerFilePath] string callerFile = "",
            [CallerLineNumber] int callerLine = 0)
        {
            var _safeOp = operationName ?? callerName;
            logger?.LogEntry(_safeOp);
            try
            {
                action?.Invoke();
                logger?.LogExit(_safeOp);
            }
            catch (Exception ex)
            {
                var context = operationName ?? $"{callerName} at {System.IO.Path.GetFileName(callerFile)}:{callerLine}";
                logger?.LogError($"Error in {context}: {ex.Message}", ex);
                logger?.LogEvent("SafeExecution", $"{_safeOp} failed: {ex.Message}");
                
                // Reportar para telemetria se disponível
                try
                {
                    var telemetry = App.Services?.GetService<Services.Telemetry.TelemetryService>();
                    _ = telemetry?.TrackExceptionAsync(ex, context);
                }
                catch
                {
                    // Ignorar erros ao reportar telemetria
                }
            }
        }

        /// <summary>
        /// Executa uma ação assíncrona de forma segura, logando exceções adequadamente
        /// </summary>
        public static async Task SafeExecuteAsync(
            Func<Task> action,
            Services.ILoggingService? logger = null,
            string? operationName = null,
            [CallerMemberName] string callerName = "",
            [CallerFilePath] string callerFile = "",
            [CallerLineNumber] int callerLine = 0)
        {
            var _safeOp = operationName ?? callerName;
            logger?.LogEntry(_safeOp);
            try
            {
                if (action != null)
                {
                    await action().ConfigureAwait(false);
                    logger?.LogExit(_safeOp);
                }
            }
            catch (Exception ex)
            {
                var context = operationName ?? $"{callerName} at {System.IO.Path.GetFileName(callerFile)}:{callerLine}";
                logger?.LogError($"Error in {context}: {ex.Message}", ex);
                logger?.LogEvent("SafeExecution", $"{_safeOp} failed: {ex.Message}");
                
                // Reportar para telemetria se disponível
                try
                {
                    var telemetry = App.Services?.GetService<Services.Telemetry.TelemetryService>();
                    if (telemetry != null)
                    {
                        await telemetry.TrackExceptionAsync(ex, context).ConfigureAwait(false);
                    }
                }
                catch
                {
                    // Ignorar erros ao reportar telemetria
                }
            }
        }

        /// <summary>
        /// Executa uma função de forma segura, retornando um valor padrão em caso de erro
        /// </summary>
        public static T SafeExecute<T>(
            Func<T> func,
            T defaultValue,
            Services.ILoggingService? logger = null,
            string? operationName = null,
            [CallerMemberName] string callerName = "",
            [CallerFilePath] string callerFile = "",
            [CallerLineNumber] int callerLine = 0)
        {
            var _safeOp = operationName ?? callerName;
            logger?.LogEntry(_safeOp);
            try
            {
                var result = func != null ? func() : defaultValue;
                logger?.LogExit(_safeOp, result);
                return result;
            }
            catch (Exception ex)
            {
                var context = operationName ?? $"{callerName} at {System.IO.Path.GetFileName(callerFile)}:{callerLine}";
                logger?.LogError($"Error in {context}: {ex.Message}", ex);
                logger?.LogEvent("SafeExecution", $"{_safeOp} failed: {ex.Message}");
                
                // Reportar para telemetria se disponível
                try
                {
                    var telemetry = App.Services?.GetService<Services.Telemetry.TelemetryService>();
                    _ = telemetry?.TrackExceptionAsync(ex, context);
                }
                catch
                {
                    // Ignorar erros ao reportar telemetria
                }
                
                return defaultValue;
            }
        }

        /// <summary>
        /// Executa uma função assíncrona de forma segura, retornando um valor padrão em caso de erro
        /// </summary>
        public static async Task<T> SafeExecuteAsync<T>(
            Func<Task<T>> func,
            T defaultValue,
            Services.ILoggingService? logger = null,
            string? operationName = null,
            [CallerMemberName] string callerName = "",
            [CallerFilePath] string callerFile = "",
            [CallerLineNumber] int callerLine = 0)
        {
            var _safeOp = operationName ?? callerName;
            logger?.LogEntry(_safeOp);
            try
            {
                var result = func != null ? await func().ConfigureAwait(false) : defaultValue;
                logger?.LogExit(_safeOp, result);
                return result;
            }
            catch (Exception ex)
            {
                var context = operationName ?? $"{callerName} at {System.IO.Path.GetFileName(callerFile)}:{callerLine}";
                logger?.LogError($"Error in {context}: {ex.Message}", ex);
                logger?.LogEvent("SafeExecution", $"{_safeOp} failed: {ex.Message}");
                
                // Reportar para telemetria se disponível
                try
                {
                    var telemetry = App.Services?.GetService<Services.Telemetry.TelemetryService>();
                    if (telemetry != null)
                    {
                        await telemetry.TrackExceptionAsync(ex, context).ConfigureAwait(false);
                    }
                }
                catch
                {
                    // Ignorar erros ao reportar telemetria
                }
                
                return defaultValue;
            }
        }

        /// <summary>
        /// Valida argumentos e lança exceção apropriada se inválido
        /// </summary>
        public static void ValidateArgument<T>(T? value, string paramName) where T : class
        {
            if (value == null)
            {
                throw new ArgumentNullException(paramName, $"Parameter '{paramName}' cannot be null");
            }
        }

        /// <summary>
        /// Valida string e lança exceção se nula ou vazia
        /// </summary>
        public static void ValidateString(string? value, string paramName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException($"Parameter '{paramName}' cannot be null or empty", paramName);
            }
        }

        /// <summary>
        /// Valida caminho de arquivo
        /// </summary>
        public static void ValidateFilePath(string? path, string paramName, bool mustExist = false)
        {
            ValidateString(path, paramName);
            
            // Validar caracteres inválidos
            if (path!.IndexOfAny(System.IO.Path.GetInvalidPathChars()) >= 0)
            {
                throw new ArgumentException($"Parameter '{paramName}' contains invalid path characters", paramName);
            }

            if (mustExist && !System.IO.File.Exists(path))
            {
                throw new System.IO.FileNotFoundException($"File not found: {path}", path);
            }
        }

        /// <summary>
        /// Valida caminho de diretório
        /// </summary>
        public static void ValidateDirectoryPath(string? path, string paramName, bool mustExist = false)
        {
            ValidateString(path, paramName);
            
            // Validar caracteres inválidos
            if (path!.IndexOfAny(System.IO.Path.GetInvalidPathChars()) >= 0)
            {
                throw new ArgumentException($"Parameter '{paramName}' contains invalid path characters", paramName);
            }

            if (mustExist && !System.IO.Directory.Exists(path))
            {
                throw new System.IO.DirectoryNotFoundException($"Directory not found: {path}");
            }
        }
    }
}
