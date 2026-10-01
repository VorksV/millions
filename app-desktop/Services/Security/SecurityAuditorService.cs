using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Security
{
    /// <summary>
    /// 🔥 SERVIÇO DE AUDITORIA DE SEGURANÇA - Para reduzir falsos positivos
    /// Registra todas as ações sensíveis do sistema para transparência total
    /// </summary>
    public class SecurityAuditorService : ISecurityAuditor
    {
        private readonly ILoggingService _logger;
        private readonly string _auditLogPath;
        private readonly object _lockObject = new object();

        public SecurityAuditorService(ILoggingService logger)
        {
            _logger = logger;
            _auditLogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
                                       "VoltrisOptimizer", "Security", "audit.log");
            
            // Garantir que o diretório exista
            Directory.CreateDirectory(Path.GetDirectoryName(_auditLogPath));
        }

        /// <summary>
        /// Registra alteração no sistema com contexto completo
        /// </summary>
        public void LogSystemChange(string operation, string target, string reason, string context = "")
        {
            try
            {
                var auditEntry = new SecurityAuditEntry
                {
                    Timestamp = DateTime.UtcNow,
                    Operation = operation,
                    Target = target,
                    Reason = reason,
                    Context = context,
                    ProcessId = Environment.ProcessId,
                    ProcessName = Environment.ProcessPath ?? "Unknown",
                    UserConsent = "Administrator", // Requer admin para operações sensíveis
                    RiskLevel = DetermineRiskLevel(operation, target)
                };

                WriteAuditEntry(auditEntry);
                
                // Log também no sistema principal
                _logger.LogInfo($"[SecurityAudit] {operation} on {target} - {reason} (Risk: {auditEntry.RiskLevel})");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[SecurityAudit] Failed to log system change: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Registra otimização de processo com detalhes
        /// </summary>
        public void LogProcessOptimization(int processId, string processName, string optimizationType, string oldValue, string newValue)
        {
            try
            {
                var auditEntry = new SecurityAuditEntry
                {
                    Timestamp = DateTime.UtcNow,
                    Operation = "ProcessOptimization",
                    Target = $"PID:{processId} ({processName})",
                    Reason = $"{optimizationType} optimization",
                    Context = $"Changed from {oldValue} to {newValue}",
                    ProcessId = Environment.ProcessId,
                    ProcessName = Environment.ProcessPath ?? "Unknown",
                    UserConsent = "Administrator",
                    RiskLevel = "Medium" // Otimizações são médio risco
                };

                WriteAuditEntry(auditEntry);
                
                _logger.LogInfo($"[SecurityAudit] Process {processId} ({processName}) optimized: {optimizationType} = {newValue}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[SecurityAudit] Failed to log process optimization: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Registra alteração no registro
        /// </summary>
        public void LogRegistryChange(string hive, string keyPath, string valueName, object oldValue, object newValue, string reason)
        {
            try
            {
                var auditEntry = new SecurityAuditEntry
                {
                    Timestamp = DateTime.UtcNow,
                    Operation = "RegistryChange",
                    Target = $"{hive}\\{keyPath}\\{valueName}",
                    Reason = reason,
                    Context = $"Changed from {oldValue} to {newValue}",
                    ProcessId = Environment.ProcessId,
                    ProcessName = Environment.ProcessPath ?? "Unknown",
                    UserConsent = "Administrator",
                    RiskLevel = DetermineRegistryRisk(hive, keyPath)
                };

                WriteAuditEntry(auditEntry);
                
                _logger.LogInfo($"[SecurityAudit] Registry {hive}\\{keyPath}\\{valueName} changed: {oldValue} → {newValue}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[SecurityAudit] Failed to log registry change: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Obtém relatório de auditoria para análise
        /// </summary>
        public async Task<List<SecurityAuditEntry>> GetAuditReportAsync(DateTime? startDate = null, DateTime? endDate = null)
        {
            try
            {
                if (!File.Exists(_auditLogPath))
                    return new List<SecurityAuditEntry>();

                var lines = await File.ReadAllLinesAsync(_auditLogPath);
                var entries = new List<SecurityAuditEntry>();

                foreach (var line in lines)
                {
                    try
                    {
                        var entry = JsonSerializer.Deserialize<SecurityAuditEntry>(line);
                        if (entry != null)
                        {
                            // Filtrar por data se especificado
                            if ((!startDate.HasValue || entry.Timestamp >= startDate.Value) &&
                                (!endDate.HasValue || entry.Timestamp <= endDate.Value))
                            {
                                entries.Add(entry);
                            }
                        }
                    }
                    catch
                    {
                        // Ignorar linhas malformadas
                        continue;
                    }
                }

                return entries.OrderByDescending(e => e.Timestamp).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError($"[SecurityAudit] Failed to get audit report: {ex.Message}", ex);
                return new List<SecurityAuditEntry>();
            }
        }

        /// <summary>
        /// Limpa logs antigos (mantém apenas últimos 30 dias)
        /// </summary>
        public async Task CleanupOldLogsAsync()
        {
            try
            {
                var cutoffDate = DateTime.UtcNow.AddDays(-30);
                var entries = await GetAuditReportAsync();
                
                var validEntries = entries.Where(e => e.Timestamp >= cutoffDate).ToList();
                
                // Reescrever arquivo apenas com entradas válidas
                if (validEntries.Count != entries.Count)
                {
                    lock (_lockObject)
                    {
                        using var writer = new StreamWriter(_auditLogPath, false);
                        foreach (var entry in validEntries)
                        {
                            writer.WriteLine(JsonSerializer.Serialize(entry, VoltrisOptimizer.App.GlobalJsonOptions));
                        }
                    }
                    
                    _logger.LogInfo($"[SecurityAudit] Cleaned up {entries.Count - validEntries.Count} old audit entries");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[SecurityAudit] Failed to cleanup old logs: {ex.Message}", ex);
            }
        }

        private void WriteAuditEntry(SecurityAuditEntry entry)
        {
            lock (_lockObject)
            {
                try
                {
                    File.AppendAllText(_auditLogPath, JsonSerializer.Serialize(entry, VoltrisOptimizer.App.GlobalJsonOptions) + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[SecurityAudit] Failed to write audit entry: {ex.Message}", ex);
                }
            }
        }

        private string DetermineRiskLevel(string operation, string target)
        {
            // Classificar risco baseado na operação
            if (operation.Contains("Priority") || operation.Contains("Realtime"))
                return "High";
            
            if (operation.Contains("Registry") && target.Contains("SYSTEM"))
                return "High";
            
            if (operation.Contains("Process") && operation.Contains("Kill"))
                return "High";
            
            if (operation.Contains("Registry"))
                return "Medium";
            
            if (operation.Contains("Service"))
                return "Medium";
            
            return "Low";
        }

        private string DetermineRegistryRisk(string hive, string keyPath)
        {
            // Riscos baseados no hive e caminho
            if (hive.Contains("LocalMachine") && keyPath.Contains("SYSTEM"))
                return "High";
            
            if (hive.Contains("LocalMachine"))
                return "Medium";
            
            if (hive.Contains("CurrentUser") && keyPath.Contains("Run"))
                return "Medium";
            
            return "Low";
        }
    }

    /// <summary>
    /// Interface para auditoria de segurança
    /// </summary>
    public interface ISecurityAuditor
    {
        void LogSystemChange(string operation, string target, string reason, string context = "");
        void LogProcessOptimization(int processId, string processName, string optimizationType, string oldValue, string newValue);
        void LogRegistryChange(string hive, string keyPath, string valueName, object oldValue, object newValue, string reason);
        Task<List<SecurityAuditEntry>> GetAuditReportAsync(DateTime? startDate = null, DateTime? endDate = null);
        Task CleanupOldLogsAsync();
    }

    /// <summary>
    /// Entrada de auditoria de segurança
    /// </summary>
    public class SecurityAuditEntry
    {
        public DateTime Timestamp { get; set; }
        public string Operation { get; set; }
        public string Target { get; set; }
        public string Reason { get; set; }
        public string Context { get; set; }
        public int ProcessId { get; set; }
        public string ProcessName { get; set; }
        public string UserConsent { get; set; }
        public string RiskLevel { get; set; }
    }
}
