using System;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// Níveis de log disponíveis
    /// </summary>
    public enum LogLevel
    {
        Trace = 0,
        Debug = 1,
        Info = 2,
        Success = 3,
        Warning = 4,
        Error = 5,
        Critical = 6,
        AI_DECISION = 7
    }

    /// <summary>
    /// Categorias de log para filtragem
    /// </summary>
    public enum LogCategory
    {
        General,
        License,
        Voice,
        Gamer,
        Cleanup,
        Performance,
        Network,
        System,
        Security,
        UI,
        Intelligence,
        Scheduler,
        Optimization
    }

    /// <summary>
    /// Interface para o serviço de logging profissional
    /// </summary>
    public interface ILoggingService : IDisposable
    {
        /// <summary>
        /// Registra uma mensagem de informação
        /// </summary>
        void LogInfo(string message);

        /// <summary>
        /// Registra uma mensagem de sucesso
        /// </summary>
        void LogSuccess(string message);

        /// <summary>
        /// Registra uma mensagem de aviso
        /// </summary>
        void LogWarning(string message);

        /// <summary>
        /// Registra uma mensagem de erro
        /// </summary>
        void LogError(string message, Exception? exception = null);

        /// <summary>
        /// Registra uma mensagem de debug
        /// </summary>
        void LogDebug(string message, string? source = null);

        /// <summary>
        /// Registra uma mensagem de trace
        /// </summary>
        void LogTrace(string message, string? source = null);

        /// <summary>
        /// Registra uma mensagem crítica
        /// </summary>
        void LogCritical(string message, Exception? exception = null, string? source = null);


        /// <summary>
        /// Método de log genérico com categoria e nível
        /// </summary>
        void Log(LogLevel level, LogCategory category, string message, Exception? exception = null, string? source = null);

        /// <summary>
        /// Evento disparado quando uma nova entrada de log é criada
        /// </summary>
        event EventHandler<string>? LogEntryAdded;

        /// <summary>
        /// Força flush síncrono de todos os logs pendentes para o disco
        /// </summary>
        void Flush();

        /// <summary>
        /// Limpa todos os logs
        /// </summary>
        void ClearLogs();

        /// <summary>
        /// Exporta os logs para um arquivo
        /// </summary>
        void ExportLogs(string filePath);

        /// <summary>
        /// Obtém todos os logs do arquivo atual
        /// </summary>
        string[] GetLogs();

        /// <summary>
        /// Obtém o caminho do diretório de logs
        /// </summary>
        string GetLogDirectory();
    }
}
