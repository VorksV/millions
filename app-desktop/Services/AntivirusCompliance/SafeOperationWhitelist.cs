using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.AntivirusCompliance
{
    /// <summary>
    /// WHITELIST DE OPERAÇÕES SEGURAS - Prevenção de Falso Positivo
    /// Define operações legítimas que não devem ser interpretadas como malware
    /// </summary>
    public class SafeOperationWhitelist
    {
        private readonly ILoggingService _logger;
        
        // WHITELIST de comandos legítimos
        private readonly Dictionary<string, SafeCommand> _safeCommands = new()
        {
            ["powercfg.exe"] = new SafeCommand
            {
                Name = "powercfg.exe",
                Purpose = "Gerenciamento de energia do Windows",
                // [FIX:UNICO-DONO-DE-ENERGIA] `/setactive` removido daqui também,
                // e não só da lista de operações validada mais abaixo. Esta
                // entrada é a definição declarativa do que é "comando seguro",
                // e mantê-la aqui diria que trocar o plano é seguro — o que é
                // verdade para o Windows e falso para o Perfil Inteligente.
                SafeArgs = new[] { "/list", "/query" },
                RiskLevel = CommandRiskLevel.Low,
                Description = "Ferramenta oficial da Microsoft para gerenciar planos de energia"
            },
            ["sc.exe"] = new SafeCommand
            {
                Name = "sc.exe",
                Purpose = "Gerenciamento de serviços do Windows",
                SafeArgs = new[] { "config", "query", "start" },
                RiskLevel = CommandRiskLevel.Medium,
                Description = "Ferramenta oficial da Microsoft para gerenciar serviços",
                WhitelistedServices = new[] { "SysMain", "DiagTrack", "WSearch" }
            }
        };

        // WHITELIST de operações de memória seguras
        private readonly HashSet<string> _safeMemoryOperations = new()
        {
            "EmptyWorkingSet_Optimization",
            "Memory_Clean_Standard",
            "WorkingSetTrim_Safe"
        };

        // WHITELIST de processos que podem ser otimizados
        private readonly HashSet<string> _optimizableProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "chrome", "firefox", "msedge", "opera", "brave",
            "code", "notepad++", "sublime_text", "vscode",
            "spotify", "discord", "telegram", "slack",
            "steam", "epic", "origin", "uplay",
            "office", "winword", "excel", "powerpnt",
            "explorer", "taskmgr", "regedit"
        };

        // BLACKLIST de processos que NUNCA devem ser tocados
        private readonly HashSet<string> _protectedProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "system", "csrss", "winlogon", "lsass", "smss", "services",
            "dwm", "wininit", "svchost", "spoolsv", "audiosrv",
            "plugplay", "rpcss", "dcomlaunch", "eventlog", "themes"
        };

        public SafeOperationWhitelist(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Valida se uma operação de memória é segura
        /// </summary>
        public bool IsSafeMemoryOperation(string operationName, string targetProcessName)
        {
            try
            {
                _logger.LogInfo($"[WHITELIST] Validando operação de memória: {operationName} -> {targetProcessName}");

                // 1. Verificar se a operação está na whitelist
                if (!_safeMemoryOperations.Contains(operationName))
                {
                    _logger.LogWarning($"[WHITELIST] Operação não whitelistada: {operationName}");
                    return false;
                }

                // 2. Verificar se o processo alvo é seguro
                if (_protectedProcesses.Contains(targetProcessName))
                {
                    _logger.LogError($"[WHITELIST] Processo protegido: {targetProcessName}");
                    return false;
                }

                // 3. Permitir processos otimizáveis ou desconhecidos (permissivo)
                // Se não está na blacklist de protegidos, permite otimização
                if (!_optimizableProcesses.Contains(targetProcessName))
                {
                    _logger.LogInfo($"[WHITELIST] Processo desconhecido (permitido): {targetProcessName}");
                    // Permitir processo desconhecido desde que não seja protegido
                    // Isso permite otimizar mais processos do usuário
                }

                _logger.LogSuccess($"[WHITELIST] Operação aprovada: {operationName} -> {targetProcessName}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[WHITELIST] Erro ao validar operação de memória", ex);
                return false;
            }
        }

        /// <summary>
        /// Valida se um comando é seguro para execução
        /// </summary>
        public async Task<CommandValidationResult> ValidateCommandAsync(string fileName, string arguments)
        {
            var result = new CommandValidationResult { FileName = fileName, Arguments = arguments };

            try
            {
                _logger.LogInfo($"[WHITELIST] Validando comando: {fileName} {arguments}");

                // 1. Verificar se o comando está na whitelist
                if (!_safeCommands.TryGetValue(fileName, out var safeCommand))
                {
                    result.IsSafe = false;
                    result.Reason = $"Comando não whitelistado: {fileName}";
                    result.RiskLevel = CommandRiskLevel.High;
                    _logger.LogWarning($"[WHITELIST] {result.Reason}");
                    return result;
                }

                // 2. Validar argumentos
                var firstArg = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (firstArg != null && !safeCommand.SafeArgs.Contains(firstArg))
                {
                    result.IsSafe = false;
                    result.Reason = $"Argumento não seguro: {firstArg} para {fileName}";
                    result.RiskLevel = CommandRiskLevel.High;
                    _logger.LogWarning($"[WHITELIST] {result.Reason}");
                    return result;
                }

                // 3. Validações específicas por comando
                switch (fileName.ToLowerInvariant())
                {
                    case "sc.exe":
                        await ValidateServiceCommandAsync(result, safeCommand, arguments);
                        break;
                    case "powercfg.exe":
                        await ValidatePowerCommandAsync(result, safeCommand, arguments);
                        break;
                }

                if (result.IsSafe)
                {
                    _logger.LogSuccess($"[WHITELIST] Comando aprovado: {fileName} {arguments}");
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[WHITELIST] Erro ao validar comando", ex);
                result.IsSafe = false;
                result.Reason = $"Erro na validação: {ex.Message}";
                result.RiskLevel = CommandRiskLevel.Critical;
                return result;
            }
        }

        private async Task ValidateServiceCommandAsync(CommandValidationResult result, SafeCommand safeCommand, string arguments)
        {
            await Task.Run(() =>
            {
                var parts = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                
                if (parts.Length >= 3 && parts[0] == "config")
                {
                    var serviceName = parts[1];
                    
                    // Verificar se o serviço está na whitelist
                    if (!safeCommand.WhitelistedServices.Contains(serviceName))
                    {
                        result.IsSafe = false;
                        result.Reason = $"Serviço não whitelistado: {serviceName}";
                        result.RiskLevel = CommandRiskLevel.High;
                        _logger.LogWarning($"[WHITELIST] {result.Reason}");
                        return;
                    }

                    // Verificar se é apenas config (não delete, stop, etc.)
                    if (parts[2] != "start=")
                    {
                        result.IsSafe = false;
                        result.Reason = $"Operação de serviço não permitida: {parts[2]}";
                        result.RiskLevel = CommandRiskLevel.Medium;
                        _logger.LogWarning($"[WHITELIST] {result.Reason}");
                        return;
                    }
                }

                result.IsSafe = true;
                result.RiskLevel = CommandRiskLevel.Low;
            });
        }

        private async Task ValidatePowerCommandAsync(CommandValidationResult result, SafeCommand safeCommand, string arguments)
        {
            await Task.Run(() =>
            {
                var parts = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                
                // Permitir apenas operações seguras de energia
                //
                // [FIX:UNICO-DONO-DE-ENERGIA] `/setactive` saiu da lista.
                //
                // A lista_existia para proteger a máquina de comandos
                // destrutivos, e `/setactive` estava nela porque não altera
                // nenhum valor: apenas escolhe um plano. O raciocínio estava
                // certo sobre o COMANDO e errado sobre o EFEITO.
                //
                // Enquanto `/setactive` estiver autorizado aqui, qualquer
                // chamador da whitelist pode trocar o plano de energia por fora
                // do Perfil Inteligente — e a validação não teria como detectar,
                // porque o comando é de fato seguro para o Windows. É o mesmo
                // erro de categoria de `SetTurboBoostPolicy`: a checagem
                // responde "isto quebra o sistema?" quando a pergunta que
                // importa é "isto briga com o dono da energia?".
                //
                // Restam as operações de LEITURA (/list, /query) e de
                // backup/restauro (/export, /import), que não escolhem plano em
                // tempo de execução. A troca de plano passa a existir em um
                // único lugar: ProfilePowerApplier, via ProfilePowerAuthority.
                var safeOperations = new[] { "/list", "/query", "/export", "/import" };
                var operation = parts.FirstOrDefault();

                if (operation == null || !safeOperations.Contains(operation))
                {
                    result.IsSafe = false;
                    result.Reason =
                        operation == "/setactive"
                            ? "Operação de energia não permitida: /setactive. A troca de plano é do " +
                              "Perfil Inteligente (ProfilePowerAuthority). Use RequestProfileApply."
                            : $"Operação de energia não permitida: {operation}";
                    result.RiskLevel = CommandRiskLevel.Medium;
                    _logger.LogWarning($"[WHITELIST] {result.Reason}");
                    return;
                }

                result.IsSafe = true;
                result.RiskLevel = CommandRiskLevel.Low;
            });
        }

        private bool IsValidPowerPlan(string planGuid)
        {
            // GUIDs conhecidos de planos de energia seguros
            var safePlans = new[]
            {
                "381b4222-f694-41f0-9685-ff5bb260df2e", // Balanced
                "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", // High Performance
                "a1841308-3541-4fab-bc81-f51556320b46", // Power Saver
                "ded574b5-9a2e-4b8c-a643-0789a06b8378"  // Ultimate Performance
            };

            return safePlans.Any(p => p.Equals(planGuid, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gera relatório de compliance para antivírus
        /// </summary>
        public async Task<ComplianceReport> GenerateComplianceReportAsync()
        {
            return await Task.Run(() =>
            {
                var report = new ComplianceReport
                {
                    GeneratedAt = DateTime.UtcNow,
                    TotalSafeCommands = _safeCommands.Count,
                    TotalSafeMemoryOperations = _safeMemoryOperations.Count,
                    TotalOptimizableProcesses = _optimizableProcesses.Count,
                    TotalProtectedProcesses = _protectedProcesses.Count
                };

                _logger.LogInfo($"[WHITELIST] Relatório de compliance gerado: {report}");

                return report;
            });
        }
    }

    #region Data Classes

    public class SafeCommand
    {
        public string Name { get; set; } = "";
        public string Purpose { get; set; } = "";
        public string[] SafeArgs { get; set; } = Array.Empty<string>();
        public CommandRiskLevel RiskLevel { get; set; }
        public string Description { get; set; } = "";
        public string[] WhitelistedServices { get; set; } = Array.Empty<string>();
    }

    public class CommandValidationResult
    {
        public string FileName { get; set; } = "";
        public string Arguments { get; set; } = "";
        public bool IsSafe { get; set; }
        public string Reason { get; set; } = "";
        public CommandRiskLevel RiskLevel { get; set; }
    }

    public class ComplianceReport
    {
        public DateTime GeneratedAt { get; set; }
        public int TotalSafeCommands { get; set; }
        public int TotalSafeMemoryOperations { get; set; }
        public int TotalOptimizableProcesses { get; set; }
        public int TotalProtectedProcesses { get; set; }

        public override string ToString()
        {
            return $"Compliance Report - Generated: {GeneratedAt:yyyy-MM-dd HH:mm:ss} | " +
                   $"Safe Commands: {TotalSafeCommands} | Safe Memory Ops: {TotalSafeMemoryOperations} | " +
                   $"Optimizable Processes: {TotalOptimizableProcesses} | Protected: {TotalProtectedProcesses}";
        }
    }

    public enum CommandRiskLevel
    {
        Low,
        Medium,
        High,
        Critical
    }

    #endregion
}
