using System;
using System.IO;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Shield
{
    public class SecurityLogService
    {
        private readonly ILoggingService _logger;
        private readonly string _logDirectory;
        private readonly object _lockObject = new object();
        
        public SecurityLogService(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // AppData\Voltris\Logs
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _logDirectory = Path.Combine(appData, "Voltris", "Logs");
            
            EnsureLogDirectoryExists();
        }
        
        public void LogSecurityEvent(string module, string eventType, string details)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                var logEntry = $"[{timestamp}] Module: {module} | Event: {eventType} | Details: {details}";
                
                // Log para o sistema principal
                _logger.LogInfo($"[Security] {logEntry}");
                
                // Log para arquivo específico de segurança
                Task.Run(() =>
                {
                    try { WriteToSecurityLog(logEntry); }
                    catch { }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError("[SecurityLog] Erro ao registrar evento", ex);
            }
        }
        
        public void LogThreatDetected(string module, string threatType, string threatDetails)
        {
            LogSecurityEvent(module, $"THREAT_DETECTED: {threatType}", threatDetails);
        }
        
        public void LogScanCompleted(string module, int itemsScanned, int threatsFound)
        {
            LogSecurityEvent(module, "SCAN_COMPLETED", $"Scanned: {itemsScanned} items, Found: {threatsFound} threats");
        }
        
        public void LogDeviceEvent(string eventType, string deviceInfo)
        {
            LogSecurityEvent("NetworkGuardian", eventType, deviceInfo);
        }
        
        public void LogStartupChange(string action, string programName, string path)
        {
            LogSecurityEvent("StartupMonitor", action, $"{programName} - {path}");
        }
        
        public void LogRansomwareAlert(string processName, string suspiciousActivity)
        {
            LogSecurityEvent("RansomwareDetector", "SUSPICIOUS_ACTIVITY", $"Process: {processName} | Activity: {suspiciousActivity}");
        }
        
        private void WriteToSecurityLog(string logEntry)
        {
            try
            {
                lock (_lockObject)
                {
                    var logFile = Path.Combine(_logDirectory, $"security_{DateTime.Now:yyyy-MM-dd}.log");
                    File.AppendAllText(logFile, logEntry + Environment.NewLine);
                }
            }
            catch
            {
                // Ignorar erros de escrita
            }
        }
        
        private void EnsureLogDirectoryExists()
        {
            try
            {
                if (!Directory.Exists(_logDirectory))
                {
                    Directory.CreateDirectory(_logDirectory);
                }
                
                // Rotação de logs: remover arquivos com mais de 30 dias
                CleanOldLogs(30);
            }
            catch (Exception ex)
            {
                _logger.LogError("[SecurityLog] Erro ao criar diretório de logs", ex);
            }
        }
        
        private void CleanOldLogs(int maxAgeDays)
        {
            try
            {
                if (!Directory.Exists(_logDirectory)) return;
                
                var cutoff = DateTime.Now.AddDays(-maxAgeDays);
                var logFiles = Directory.GetFiles(_logDirectory, "security_*.log");
                
                foreach (var file in logFiles)
                {
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        if (fileInfo.LastWriteTime < cutoff)
                        {
                            fileInfo.Delete();
                            _logger.LogInfo($"[SecurityLog] Log antigo removido: {fileInfo.Name}");
                        }
                    }
                    catch
                    {
                        // Ignorar erros ao deletar logs individuais
                    }
                }
            }
            catch
            {
                // Ignorar erros na limpeza
            }
        }
        
        public string GetLogDirectory()
        {
            return _logDirectory;
        }
    }
}
