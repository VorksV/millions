using System;

namespace VoltrisOptimizer.Services.Logging
{
    /// <summary>
    /// Configurações de logging seguras por ambiente
    /// Define níveis de segurança para produção vs desenvolvimento
    /// </summary>
    public static class SecureLoggingConfiguration
    {
        /// <summary>
        /// Ambiente de execução atual
        /// </summary>
        public enum LoggingEnvironment
        {
            Development,
            Testing,
            Production
        }

        /// <summary>
        /// Detecta o ambiente atual com base em configurações
        /// </summary>
        public static LoggingEnvironment CurrentEnvironment
        {
            get
            {
#if DEBUG
                return LoggingEnvironment.Development;
#else
                // Verificar se está em modo de teste
                if (AppDomain.CurrentDomain.FriendlyName.Contains("Test") || 
                    Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Testing")
                {
                    return LoggingEnvironment.Testing;
                }
                
                return LoggingEnvironment.Production;
#endif
            }
        }

        /// <summary>
        /// Obtém configuração de logging segura para o ambiente atual
        /// </summary>
        public static LoggingConfiguration GetSecureConfiguration()
        {
            return CurrentEnvironment switch
            {
                LoggingEnvironment.Development => GetDevelopmentConfiguration(),
                LoggingEnvironment.Testing => GetTestingConfiguration(),
                LoggingEnvironment.Production => GetProductionConfiguration(),
                _ => GetProductionConfiguration() // Default para segurança
            };
        }

        /// <summary>
        /// Configuração para ambiente de desenvolvimento
        /// - Logs detalhados permitidos
        /// - Informações sensíveis mascaradas mas identificáveis
        /// - Debug e Trace habilitados
        /// </summary>
        private static LoggingConfiguration GetDevelopmentConfiguration()
        {
            return new LoggingConfiguration
            {
                MinimumLevel = LogLevel.Trace, // Tudo em desenvolvimento
                MaxFileSizeBytes = 10 * 1024 * 1024, // 10MB
                MaxFileCount = 5,
                MaxArchivedFileCount = 10,
                DaysToKeepUnarchived = 3,
                EnableAsyncLogging = true,
                FlushIntervalMs = 1000, // Mais lento para debugging
                IncludeStackTraceOnError = true,
                FileNameFormat = "voltris_dev_{0:yyyy-MM-dd_HH-mm}.log"
            };
        }

        /// <summary>
        /// Configuração para ambiente de teste
        /// - Logs moderados
        /// - Informações sensíveis completamente mascaradas
        /// - Debug habilitado, Trace desabilitado
        /// </summary>
        private static LoggingConfiguration GetTestingConfiguration()
        {
            return new LoggingConfiguration
            {
                MinimumLevel = LogLevel.Debug, // Debug e acima
                MaxFileSizeBytes = 5 * 1024 * 1024, // 5MB
                MaxFileCount = 3,
                MaxArchivedFileCount = 5,
                DaysToKeepUnarchived = 2,
                EnableAsyncLogging = true,
                FlushIntervalMs = 500,
                IncludeStackTraceOnError = true,
                FileNameFormat = "voltris_test_{0:yyyy-MM-dd}.log"
            };
        }

        /// <summary>
        /// Configuração para ambiente de produção
        /// - Logs mínimos essenciais
        /// - Informações sensíveis completamente removidas
        /// - Apenas Warning, Error e Critical
        /// - Máxima segurança e performance
        /// </summary>
        private static LoggingConfiguration GetProductionConfiguration()
        {
            return new LoggingConfiguration
            {
                MinimumLevel = LogLevel.Warning, // Apenas importantes
                MaxFileSizeBytes = 2 * 1024 * 1024, // 2MB
                MaxFileCount = 2,
                MaxArchivedFileCount = 3,
                DaysToKeepUnarchived = 1, // Mantém apenas 1 dia
                EnableAsyncLogging = true,
                FlushIntervalMs = 200, // Rápido para performance
                IncludeStackTraceOnError = false, // Sem stack trace em produção
                FileNameFormat = "voltris_{0:yyyy-MM-dd}.log" // Sem identificação de ambiente
            };
        }

        /// <summary>
        /// Verifica se uma categoria de log é permitida no ambiente atual
        /// </summary>
        /// <param name="category">Categoria do log</param>
        /// <returns>True se permitida</returns>
        public static bool IsCategoryAllowed(LogCategory category)
        {
            return CurrentEnvironment switch
            {
                LoggingEnvironment.Development => true, // Tudo permitido
                LoggingEnvironment.Testing => true, // Todas permitidas em teste
                LoggingEnvironment.Production => category switch
                {
                    // Apenas categorias críticas em produção
                    LogCategory.Security => true,
                    LogCategory.License => true,
                    LogCategory.System => true,
                    _ => false
                },
                _ => false
            };
        }

        /// <summary>
        /// Verifica se uma fonte de log é permitida no ambiente atual
        /// </summary>
        /// <param name="source">Fonte do log</param>
        /// <returns>True se permitida</returns>
        public static bool IsSourceAllowed(string source)
        {
            if (string.IsNullOrEmpty(source))
                return CurrentEnvironment != LoggingEnvironment.Production;

            return CurrentEnvironment switch
            {
                LoggingEnvironment.Development => true,
                LoggingEnvironment.Testing => !source.Contains("Trace", StringComparison.OrdinalIgnoreCase),
                LoggingEnvironment.Production => source switch
                {
                    var s when s.Contains("SECURITY", StringComparison.OrdinalIgnoreCase) => true,
                    var s when s.Contains("LICENSE", StringComparison.OrdinalIgnoreCase) => true,
                    var s when s.Contains("SYSTEM", StringComparison.OrdinalIgnoreCase) => true,
                    var s when s.Contains("CRITICAL", StringComparison.OrdinalIgnoreCase) => true,
                    _ => false
                },
                _ => false
            };
        }

        /// <summary>
        /// Obtém o nível de mascaramento para o ambiente atual
        /// </summary>
        public static MaskingLevel GetMaskingLevel()
        {
            return CurrentEnvironment switch
            {
                LoggingEnvironment.Development => MaskingLevel.Partial, // Identificável
                LoggingEnvironment.Testing => MaskingLevel.Full, // Completamente mascarado
                LoggingEnvironment.Production => MaskingLevel.Remove, // Removido completamente
                _ => MaskingLevel.Remove
            };
        }

        /// <summary>
        /// Níveis de mascaramento de informações sensíveis
        /// </summary>
        public enum MaskingLevel
        {
            /// <summary>
            /// Mascara parcialmente, mantendo identificação (ex: "abc123...")
            /// </summary>
            Partial,

            /// <summary>
            /// Mascara completamente (ex: "***[MASCARADO]***")
            /// </summary>
            Full,

            /// <summary>
            /// Remove completamente a informação (ex: "***[REMOVIDO]***")
            /// </summary>
            Remove
        }

        /// <summary>
        /// Aplica mascaramento apropriado ao ambiente atual
        /// </summary>
        /// <param name="message">Mensagem original</param>
        /// <returns>Mensagem mascarada conforme ambiente</returns>
        public static string ApplyEnvironmentMasking(string message)
        {
            if (string.IsNullOrEmpty(message))
                return message;

            var maskingLevel = GetMaskingLevel();

            return maskingLevel switch
            {
                MaskingLevel.Partial => SecurityMaskingService.MaskSensitiveData(message),
                MaskingLevel.Full => SecurityMaskingService.MaskSensitiveData(message),
                MaskingLevel.Remove => SecurityMaskingService.RemoveSensitiveData(message),
                _ => SecurityMaskingService.RemoveSensitiveData(message)
            };
        }

        /// <summary>
        /// Verifica se o logging de telemetria está permitido
        /// </summary>
        public static bool IsTelemetryLoggingAllowed()
        {
            return CurrentEnvironment switch
            {
                LoggingEnvironment.Development => true,
                LoggingEnvironment.Testing => false,
                LoggingEnvironment.Production => false, // Sem telemetria em produção
                _ => false
            };
        }

        /// <summary>
        /// Obtém configurações de notificação de erros por ambiente
        /// </summary>
        public static ErrorNotificationSettings GetErrorNotificationSettings()
        {
            return CurrentEnvironment switch
            {
                LoggingEnvironment.Development => new ErrorNotificationSettings
                {
                    EnableConsoleNotifications = true,
                    MinErrorLevel = LogLevel.Debug
                },
                LoggingEnvironment.Testing => new ErrorNotificationSettings
                {
                    EnableConsoleNotifications = true,
                    MinErrorLevel = LogLevel.Error
                },
                LoggingEnvironment.Production => new ErrorNotificationSettings
                {
                    EnableConsoleNotifications = false,
                    MinErrorLevel = LogLevel.Critical // Apenas críticos em produção
                },
                _ => new ErrorNotificationSettings
                {
                    EnableConsoleNotifications = false,
                    MinErrorLevel = LogLevel.Critical
                }
            };
        }
    }

    /// <summary>
    /// Configurações de notificação de erros
    /// </summary>
    public class ErrorNotificationSettings
    {
        public bool EnableConsoleNotifications { get; set; }
        public LogLevel MinErrorLevel { get; set; }
    }
}
