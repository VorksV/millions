using VoltrisOptimizer.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.SystemSafety
{
    /// <summary>
    /// CRITICAL: System Safety Validator - Prevenção de BSOD e instabilidade
    /// Validações obrigatórias antes de qualquer otimização do sistema
    /// </summary>
    public class SystemSafetyValidator
    {
        private readonly ILoggingService _logger;
        private readonly Dictionary<string, DateTime> _lastOptimizationTimes = new();
        private readonly object _lock = new();
        private bool _perfCountersWorking = true;

        // Blacklist de otimizações perigosas
        private readonly HashSet<string> _dangerousOptimizations = new(StringComparer.OrdinalIgnoreCase)
        {
            "EmptyWorkingSet_Multiple",
            "Registry_Kernel_Modification",
            "Service_Critical_Disable",
            "Driver_Force_Install",
            "Memory_Aggressive_Compaction",
            "CPU_Affinity_System_Processes",
            "Power_Kernel_Modification"
        };

        // Serviços críticos que NUNCA devem ser modificados
        private readonly HashSet<string> _criticalServices = new(StringComparer.OrdinalIgnoreCase)
        {
            "csrss", "winlogon", "lsass", "smss", "services", "dwm", "explorer",
            "System", "svchost", "spoolsv", "nvcontainer", "audiosrv", "PlugPlay",
            "RpcSs", "DcomLaunch", "EventLog", "Themes", "WinDefend", "SecurityHealthService"
        };

        // Chaves de registro críticas
        private readonly HashSet<string> _criticalRegistryPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PagingFiles",
            @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\LargeSystemCache",
            @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\kernel",
            @"HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl",
            @"HKLM\SYSTEM\CurrentControlSet\Control\FileSystem",
            @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\TcpMaxDataRetransmissions"
        };

        public SystemSafetyValidator(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// VALIDAÇÃO OBRIGATória antes de qualquer otimização
        /// </summary>
        public async Task<SafetyValidationResult> ValidateOptimizationAsync(string optimizationName, string category)
        {
            var result = new SafetyValidationResult { OptimizationName = optimizationName };

            try
            {
                _logger.LogInfo($"[SAFETY] Iniciando validação crítica: {optimizationName}");

                // 1. Verificar se está na blacklist
                if (IsDangerousOptimization(optimizationName))
                {
                    result.IsSafe = false;
                    result.Reason = $"Otimização '{optimizationName}' está na blacklist de operações perigosas";
                    result.RiskLevel = RiskLevel.Critical;
                    _logger.LogError($"[SAFETY] BLOCKED: {result.Reason}");
                    return result;
                }

                // 2. Verificar cooldown (prevenir execução muito frequente)
                if (!await ValidateCooldownAsync(optimizationName))
                {
                    result.IsSafe = false;
                    result.Reason = $"Otimização '{optimizationName}' executada muito recentemente";
                    result.RiskLevel = RiskLevel.High;
                    _logger.LogWarning($"[SAFETY] BLOCKED: {result.Reason}");
                    return result;
                }

                // 3. Verificar estabilidade do sistema
                var stabilityCheck = await ValidateSystemStabilityAsync();
                if (!stabilityCheck.IsStable)
                {
                    result.IsSafe = false;
                    result.Reason = $"Sistema instável: {stabilityCheck.Reason}";
                    result.RiskLevel = RiskLevel.High;
                    _logger.LogError($"[SAFETY] BLOCKED: {result.Reason}");
                    return result;
                }

                // 4. Verificar uso de recursos
                var resourceCheck = await ValidateResourceUsageAsync();
                if (resourceCheck.Usage > 90)
                {
                    result.IsSafe = false;
                    result.Reason = $"Uso de recursos muito alto: {resourceCheck.Usage}%";
                    result.RiskLevel = RiskLevel.Medium;
                    _logger.LogWarning($"[SAFETY] BLOCKED: {result.Reason}");
                    return result;
                }

                // 5. Validações específicas por categoria
                switch (category.ToLowerInvariant())
                {
                    case "memory":
                        await ValidateMemoryOptimization(result);
                        break;
                    case "services":
                        await ValidateServiceOptimization(result);
                        break;
                    case "registry":
                        await ValidateRegistryOptimization(result);
                        break;
                    case "power":
                        await ValidatePowerOptimization(result);
                        break;
                }

                if (!result.IsSafe)
                {
                    _logger.LogWarning($"[SAFETY] BLOCKED: {result.Reason}");
                    return result;
                }

                // 6. Verificar se há otimizações concorrentes
                if (HasConcurrentOptimizations(optimizationName))
                {
                    result.IsSafe = false;
                    result.Reason = $"Otimização concorrente detectada para '{optimizationName}'";
                    result.RiskLevel = RiskLevel.High;
                    _logger.LogWarning($"[SAFETY] BLOCKED: {result.Reason}");
                    return result;
                }

                result.IsSafe = true;
                result.RiskLevel = RiskLevel.Safe;
                _logger.LogSuccess($"[SAFETY] APPROVED: {optimizationName} validada com sucesso");

                // Registrar tempo da otimização
                lock (_lock)
                {
                    _lastOptimizationTimes[optimizationName] = DateTime.UtcNow;
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[SAFETY] Erro durante validação de {optimizationName}", ex);
                result.IsSafe = false;
                result.Reason = $"Erro na validação: {ex.Message}";
                result.RiskLevel = RiskLevel.Critical;
                return result;
            }
        }

        private bool IsDangerousOptimization(string optimizationName)
        {
            return _dangerousOptimizations.Contains(optimizationName) ||
                   optimizationName.Contains("Multiple", StringComparison.OrdinalIgnoreCase) ||
                   optimizationName.Contains("Force", StringComparison.OrdinalIgnoreCase) ||
                   optimizationName.Contains("Aggressive", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<bool> ValidateCooldownAsync(string optimizationName)
        {
            lock (_lock)
            {
                if (_lastOptimizationTimes.TryGetValue(optimizationName, out var lastTime))
                {
                    var timeSinceLast = DateTime.UtcNow - lastTime;
                    var minCooldown = GetMinCooldown(optimizationName);
                    
                    if (timeSinceLast.TotalSeconds < minCooldown)
                    {
                        _logger.LogWarning($"[SAFETY] Cooldown: {optimizationName} executada há {timeSinceLast.TotalSeconds:F0}s (mínimo: {minCooldown}s)");
                        return false;
                    }
                }
            }
            return true;
        }

        private int GetMinCooldown(string optimizationName)
        {
            return optimizationName.ToLowerInvariant() switch
            {
                var name when name.Contains("memory") => 30, // 30 segundos para otimizações de memória
                var name when name.Contains("service") => 60, // 1 minuto para serviços
                var name when name.Contains("registry") => 120, // 2 minutos para registro
                var name when name.Contains("power") => 180, // 3 minutos para energia
                _ => 60 // 1 minuto padrão
            };
        }

        private async Task<SystemStabilityResult> ValidateSystemStabilityAsync()
        {
            try
            {
                // Verificar se há processos críticos com alto uso de CPU
                var criticalProcesses = new[] { "System", "csrss", "winlogon", "lsass" };
                foreach (var procName in criticalProcesses)
                {
                    var processes = Process.GetProcessesByName(procName);
                    foreach (var proc in processes)
                    {
                        try
                        {
                            if (proc.TotalProcessorTime.TotalMilliseconds > 10000) // > 10 segundos de CPU time
                            {
                                return new SystemStabilityResult
                                {
                                    IsStable = false,
                                    Reason = $"Processo crítico {procName} com alto uso de CPU"
                                };
                            }
                        }
                        finally { proc.Dispose(); }
                    }
                }

                // Verificar se há BSODs recentes (via Event Viewer)
                var recentCrashes = await CheckRecentSystemCrashesAsync();
                if (recentCrashes > 0)
                {
                    return new SystemStabilityResult
                    {
                        IsStable = false,
                        Reason = $"Detectados {recentCrashes} crashes de sistema recentes"
                    };
                }

                return new SystemStabilityResult { IsStable = true };
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[SAFETY] Erro ao verificar estabilidade: {ex.Message}");
                return new SystemStabilityResult { IsStable = true }; // Assumir estável se não for possível verificar
            }
        }

        private async Task<int> CheckRecentSystemCrashesAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Verificar eventos de BSOD nas últimas 24 horas
                    var eventLog = new EventLog("System");
                    var yesterday = DateTime.Now.AddDays(-1);
                    var crashCount = 0;

                    foreach (EventLogEntry entry in eventLog.Entries)
                    {
                        if (entry.TimeGenerated > yesterday &&
                            (entry.EntryType == EventLogEntryType.Error) &&
                            (entry.Message.Contains("BugCheck") || entry.Message.Contains("BSOD")))
                        {
                            crashCount++;
                        }
                    }

                    return crashCount;
                }
                catch
                {
                    return 0; // Retornar 0 se não for possível verificar
                }
            });
        }

        private async Task<ResourceUsageResult> ValidateResourceUsageAsync()
        {
            return await Task.Run(() =>
            {
                if (!_perfCountersWorking) return new ResourceUsageResult { Usage = 0 };
                try
                {
                    using var cpuCounter = new SafePerformanceCounter("Processor", "% Processor Time", "_Total");
                    using var memCounter = new SafePerformanceCounter("Memory", "Available MBytes");
                    
                    cpuCounter.NextValue();
                    var cpuUsage = cpuCounter.NextValue();
                    var availableMem = memCounter.NextValue();
                    var totalMem = GC.GetTotalMemory(false) / (1024 * 1024);
                    var memUsage = ((totalMem - availableMem) / totalMem) * 100;

                    var maxUsage = Math.Max(cpuUsage, memUsage);
                    _logger.LogInfo($"[SAFETY] Uso atual: CPU={cpuUsage:F1}%, Memória={memUsage:F1}%");

                    return new ResourceUsageResult { Usage = maxUsage };
                }
                catch (InvalidOperationException)
                {
                    _perfCountersWorking = false;
                    _logger.LogDebug("[SAFETY] PerformanceCounters inoperantes. Fallback para uso de recursos ativado.");
                    return new ResourceUsageResult { Usage = 0 };
                }
                catch
                {
                    return new ResourceUsageResult { Usage = 0 }; // Assumir uso baixo se não for possível verificar
                }
            });
        }

        private async Task ValidateMemoryOptimization(SafetyValidationResult result)
        {
            // Verificar se há múltiplas chamadas de EmptyWorkingSet
            if (result.OptimizationName.Contains("EmptyWorkingSet"))
            {
                lock (_lock)
                {
                    if (_lastOptimizationTimes.ContainsKey("EmptyWorkingSet"))
                    {
                        var lastTime = _lastOptimizationTimes["EmptyWorkingSet"];
                        if (DateTime.UtcNow - lastTime < TimeSpan.FromSeconds(30))
                        {
                            result.IsSafe = false;
                            result.Reason = "Múltiplas chamadas EmptyWorkingSet detectadas - risco de BSOD";
                            result.RiskLevel = RiskLevel.Critical;
                            return;
                        }
                    }
                }
            }

            // Verificar uso de memória
            var memoryUsage = GC.GetTotalMemory(false) / (1024 * 1024);
            if (memoryUsage > 8192) // > 8GB
            {
                result.IsSafe = false;
                result.Reason = $"Uso de memória muito alto: {memoryUsage}MB";
                result.RiskLevel = RiskLevel.Medium;
                return;
            }

            _logger.LogInfo("[SAFETY] Validação de memória aprovada");
        }

        private async Task ValidateServiceOptimization(SafetyValidationResult result)
        {
            // Verificar se está tentando modificar serviço crítico
            foreach (var criticalService in _criticalServices)
            {
                if (result.OptimizationName.Contains(criticalService, StringComparison.OrdinalIgnoreCase))
                {
                    result.IsSafe = false;
                    result.Reason = $"Tentativa de modificar serviço crítico: {criticalService}";
                    result.RiskLevel = RiskLevel.Critical;
                    return;
                }
            }

            _logger.LogInfo("[SAFETY] Validação de serviços aprovada");
        }

        private async Task ValidateRegistryOptimization(SafetyValidationResult result)
        {
            // Verificar se está tentando modificar chave crítica
            foreach (var criticalPath in _criticalRegistryPaths)
            {
                if (result.OptimizationName.Contains(criticalPath, StringComparison.OrdinalIgnoreCase))
                {
                    result.IsSafe = false;
                    result.Reason = $"Tentativa de modificar chave crítica: {criticalPath}";
                    result.RiskLevel = RiskLevel.Critical;
                    return;
                }
            }

            _logger.LogInfo("[SAFETY] Validação de registro aprovada");
        }

        private async Task ValidatePowerOptimization(SafetyValidationResult result)
        {
            // Verificar se está tentando modificar configurações críticas de energia
            if (result.OptimizationName.Contains("kernel", StringComparison.OrdinalIgnoreCase) ||
                result.OptimizationName.Contains("processor", StringComparison.OrdinalIgnoreCase))
            {
                result.IsSafe = false;
                result.Reason = "Tentativa de modificar configurações críticas de energia do kernel";
                result.RiskLevel = RiskLevel.Critical;
                return;
            }

            _logger.LogInfo("[SAFETY] Validação de energia aprovada");
        }

        private bool HasConcurrentOptimizations(string optimizationName)
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                var concurrentCount = 0;

                foreach (var kvp in _lastOptimizationTimes)
                {
                    if (now - kvp.Value < TimeSpan.FromSeconds(10)) // Otimizações nos últimos 10 segundos
                    {
                        concurrentCount++;
                    }
                }

                return concurrentCount > 1;
            }
        }
    }

    #region Result Classes

    public class SafetyValidationResult
    {
        public string OptimizationName { get; set; } = "";
        public bool IsSafe { get; set; }
        public string Reason { get; set; } = "";
        public RiskLevel RiskLevel { get; set; }
    }

    public class SystemStabilityResult
    {
        public bool IsStable { get; set; }
        public string Reason { get; set; } = "";
    }

    public class ResourceUsageResult
    {
        public double Usage { get; set; }
    }

    public enum RiskLevel
    {
        Safe,
        Low,
        Medium,
        High,
        Critical
    }

    #endregion
}

