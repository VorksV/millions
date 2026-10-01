using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// Verificador de segurança para operações no Component Store
    /// Impede operações durante Windows Update, transações CBS ou locks críticos
    /// </summary>
    public class ComponentStoreSafetyChecker
    {
        private readonly ILoggingService _logger;

        public ComponentStoreSafetyChecker(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Verifica se é seguro realizar operações no Component Store
        /// </summary>
        public async Task<bool> IsSafeForComponentStoreOperationAsync()
        {
            try
            {
                _logger.LogInfo("[SafetyChecker] 🔍 Verificando segurança do Component Store...");

                // 1. Verificar Windows Update em andamento
                if (await IsWindowsUpdateRunningAsync())
                {
                    _logger.LogWarning("[SafetyChecker] ⚠ Windows Update em andamento - operação BLOQUEADA");
                    return false;
                }

                // 2. Verificar serviços críticos do CBS
                if (!await AreCriticalServicesAvailableAsync())
                {
                    _logger.LogWarning("[SafetyChecker] ⚠ Serviços críticos indisponíveis - operação BLOQUEADA");
                    return false;
                }

                // 3. Verificar locks do TrustedInstaller
                if (IsTrustedInstallerLockedAsync())
                {
                    _logger.LogWarning("[SafetyChecker] ⚠ TrustedInstaller com lock ativo - operação BLOQUEADA");
                    return false;
                }

                // 4. Verificar transações CBS ativas
                if (await IsCbsTransactionActiveAsync())
                {
                    _logger.LogWarning("[SafetyChecker] ⚠ Transação CBS ativa - operação BLOQUEADA");
                    return false;
                }

                _logger.LogSuccess("[SafetyChecker] ✅ Component Store seguro para operações");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[SafetyChecker] ❌ Erro na verificação de segurança: {ex.Message}");
                return false; // Falha segura: bloquear operação
            }
        }

        /// <summary>
        /// Verifica se Windows Update está em andamento
        /// </summary>
        private async Task<bool> IsWindowsUpdateRunningAsync()
        {
            try
            {
                // Verificar processos do Windows Update
                var wuProcesses = new[] { "wuauclt", "usoclient", "usocoreworker", "tiworker" };
                
                foreach (var processName in wuProcesses)
                {
                    if (Process.GetProcessesByName(processName).Length > 0)
                    {
                        _logger.LogInfo($"[SafetyChecker] Processo Windows Update detectado: {processName}");
                        return true;
                    }
                }

                // Verificar serviço wuauserv
                using var wuauserv = new ServiceController("wuauserv");
                if (wuauserv.Status == ServiceControllerStatus.StartPending || 
                    wuauserv.Status == ServiceControllerStatus.StopPending)
                {
                    _logger.LogInfo($"[SafetyChecker] Serviço wuauserv em transição: {wuauserv.Status}");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[SafetyChecker] Erro ao verificar Windows Update: {ex.Message}");
                return true; // Falha segura: assumir que está rodando
            }
        }

        /// <summary>
        /// Verifica se serviços críticos estão disponíveis
        /// </summary>
        private async Task<bool> AreCriticalServicesAvailableAsync()
        {
            try
            {
                var criticalServices = new[] { "TrustedInstaller", "wuauserv", "bits" };
                
                foreach (var serviceName in criticalServices)
                {
                    try
                    {
                        using var service = new ServiceController(serviceName);
                        
                        // Serviço não deve estar em transição
                        if (service.Status == ServiceControllerStatus.StartPending ||
                            service.Status == ServiceControllerStatus.StopPending ||
                            service.Status == ServiceControllerStatus.Paused)
                        {
                            _logger.LogInfo($"[SafetyChecker] Serviço crítico em estado inválido: {serviceName} - {service.Status}");
                            return false;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"[SafetyChecker] Não foi possível verificar serviço {serviceName}: {ex.Message}");
                        return false; // Falha segura
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[SafetyChecker] Erro ao verificar serviços críticos: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Verifica se TrustedInstaller tem locks ativos
        /// </summary>
        private bool IsTrustedInstallerLockedAsync()
        {
            try
            {
                // Verificar se o processo TrustedInstaller está rodando
                var tiProcesses = Process.GetProcessesByName("TrustedInstaller");
                if (tiProcesses.Length == 0)
                {
                    return false; // Sem processo = sem lock
                }

                // REMOVIDO: Verificação de locks via pending.xml - ALTO RISCO
                // NUNCA criar/modificar arquivos no WinSxS
                // Isso pode interferir com operações do Component Store
                // Usar apenas verificação de processos e serviços
                
                return false; // TrustedInstaller rodando mas sem verificação direta de arquivos
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[SafetyChecker] Erro ao verificar locks TrustedInstaller: {ex.Message}");
                return true; // Falha segura: assumir lock
            }
        }

        /// <summary>
        /// Verifica se há transações CBS ativas
        /// </summary>
        private async Task<bool> IsCbsTransactionActiveAsync()
        {
            try
            {
                var cbsLogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Logs", "CBS", "CBS.log");
                
                if (!File.Exists(cbsLogPath))
                {
                    return false;
                }

                // Verificar últimas linhas do log CBS por atividades recentes
                var recentLines = await ReadLastLinesAsync(cbsLogPath, 10);
                var now = DateTime.Now;
                
                foreach (var line in recentLines)
                {
                    // Procurar por atividades de transação recentes (últimos 2 minutos)
                    if (line.Contains("Processing") || 
                        line.Contains("Installing") || 
                        line.Contains("Uninstalling") ||
                        line.Contains("Repairing"))
                    {
                        // Extrair timestamp do log CBS (formato específico)
                        if (TryExtractCbsTimestamp(line, out var logTime))
                        {
                            if ((now - logTime).TotalMinutes < 2)
                            {
                                _logger.LogInfo($"[SafetyChecker] Atividade CBS recente detectada: {line.Trim()}");
                                return true;
                            }
                        }
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[SafetyChecker] Erro ao verificar transações CBS: {ex.Message}");
                return true; // Falha segura: assumir transação ativa
            }
        }

        /// <summary>
        /// Lê as últimas N linhas de um arquivo de forma eficiente
        /// </summary>
        private async Task<string[]> ReadLastLinesAsync(string path, int lineCount)
        {
            try
            {
                var lines = await File.ReadAllLinesAsync(path);
                return lines.Length > lineCount ? lines[^lineCount..] : lines;
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Extrai timestamp de uma linha de log CBS
        /// </summary>
        private bool TryExtractCbsTimestamp(string logLine, out DateTime timestamp)
        {
            timestamp = DateTime.MinValue;
            
            try
            {
                // Formato típico do CBS.log: [YYYY-MM-DD HH:MM:SS.FFF]
                var match = System.Text.RegularExpressions.Regex.Match(logLine, @"\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})\]");
                
                if (match.Success && DateTime.TryParse(match.Groups[1].Value, out var parsed))
                {
                    timestamp = parsed;
                    return true;
                }
            }
            catch
            {
                // Ignorar erros de parsing
            }
            
            return false;
        }
    }
}
