using System;
using System.IO;

namespace VoltrisOptimizer.Services
{
    public static class LanguageLogger
    {
        private static readonly object _lock = new object();
        private static readonly string LogFilePath = Path.Combine(LogDirectoryResolver.Resolve(), "language.log");

        public static void Log(string message)
        {
            try
            {
                var logDir = Path.GetDirectoryName(LogFilePath);
                if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                lock (_lock)
                {
                    File.AppendAllText(LogFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
                }
            }
            catch
            {
                // Silencioso - não deve falhar o aplicativo se a gravação de logs falhar
            }
        }
    }
}
