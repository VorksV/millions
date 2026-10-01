using System;
using System.IO;

namespace VoltrisUninstaller.Core
{
    /// <summary>
    /// Implementação simples de logger
    /// </summary>
    public class Logger : ILogger, IDisposable
    {
        private readonly string _logPath;
        private readonly object _lock = new object();

        public Logger(string logPath)
        {
            _logPath = logPath;
            var dir = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        public void LogInfo(string message) => WriteLog("INFO", message);
        public void LogWarning(string message) => WriteLog("WARN", message);
        public void LogError(string message, Exception? ex = null)
        {
            WriteLog("ERROR", message);
            if (ex != null)
            {
                WriteLog("ERROR", $"Exception: {ex.GetType().Name}: {ex.Message}");
                WriteLog("ERROR", $"Stack: {ex.StackTrace}");
            }
        }
        public void LogDebug(string message) => WriteLog("DEBUG", message);

        private void WriteLog(string level, string message)
        {
            lock (_lock)
            {
                try
                {
                    var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}][{level}] {message}";
                    File.AppendAllText(_logPath, logEntry + Environment.NewLine);
                }
                catch { }
            }
        }

        public void Dispose() { }
    }
}
