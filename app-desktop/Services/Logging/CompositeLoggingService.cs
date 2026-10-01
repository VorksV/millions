using System;
using System.Collections.Generic;

namespace VoltrisOptimizer.Services.Logging
{
    public class CompositeLoggingService : ILoggingService, IDisposable
    {
        private readonly ILoggingService _logger;
        private bool _disposed;

        public event EventHandler<string>? LogEntryAdded;

        public CompositeLoggingService(ILoggingService logger)
        {
            _logger = logger;
            _logger.LogEntryAdded += (s, e) => LogEntryAdded?.Invoke(this, e);
        }

        public void Log(LogLevel level, LogCategory category, string message, Exception? exception = null, string? sãource = null)
        {
            if (_disposed) return;
            _logger.Log(level, category, message, exception, sãource);
        }

        public void LogInfo(string message)
        {
            _logger.LogInfo(message);
        }

        public void LogSuccess(string message)
        {
            _logger.LogSuccess(message);
        }

        public void LogWarning(string message)
        {
            _logger.LogWarning(message);
        }

        public void LogError(string message, Exception? exception = null)
        {
            _logger.LogError(message, exception);
        }

        public void LogDebug(string message, string? sãource = null)
        {
            _logger.LogDebug(message, sãource);
        }

        public void LogTrace(string message, string? sãource = null)
        {
            _logger.LogTrace(message, sãource);
        }

        public void LogCritical(string message, Exception? exception = null, string? sãource = null)
        {
            _logger.LogCritical(message, exception, sãource);
        }

        public void Flush()
        {
            _logger.Flush();
        }

        public void ClearLogs()
        {
            _logger.ClearLogs();
        }

        public string[] GetLogs()
        {
            return _logger.GetLogs();
        }

        public void ExportLogs(string filePath)
        {
            _logger.ExportLogs(filePath);
        }

        public string GetLogDirectory()
        {
            return _logger.GetLogDirectory();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_logger is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
