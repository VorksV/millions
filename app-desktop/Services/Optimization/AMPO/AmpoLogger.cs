using System;
using System.IO;

namespace VoltrisOptimizer.Services.Optimization.AMPO
{
    /// <summary>
    /// Logger exclusivo do AMPO, separado do DLS e dos demais logs gerais.
    /// </summary>
    public class AmpoLogger
    {
        private readonly string _logPath;
        private readonly object _lock = new();

        public AmpoLogger()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "VoltrisOptimizer", "logs");
            Directory.CreateDirectory(dir);
            _logPath = Path.Combine(dir, "ampo.log");
        }

        public void Log(string message)
        {
            lock (_lock)
            {
                try
                {
                    File.AppendAllText(_logPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + Environment.NewLine);
                }
                catch { }
            }
        }
    }
}

