using System;
using System.Collections.Generic;
using System.Linq;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Enterprise.Intelligence
{
    /// <summary>
    /// SAFE ACTION POLICY
    /// Bloqueia ações perigosas e apenas permite otimizações seguras
    /// </summary>
    public sealed class SafeActionPolicy
    {
        private readonly ILoggingService _logger;
        private readonly HashSet<string> _blockedActions;
        private readonly HashSet<string> _restrictedParameters;
        private readonly Dictionary<string, double> _safeThresholds;

        public SafeActionPolicy(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Ações PERIGOSAS que nunca devem ser executadas automaticamente
            _blockedActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // NUNCA executar GC manual automaticamente
                "GarbageCollection",
                "ForceGC",
                "ManualGC",
                "MemoryCleanup",
                
                // NUNCA modificar configurações de sistema críticas
                "RegistryModification",
                "SystemSettingsChange",
                "PowerPlanChange",
                "DriverModification",
                
                // NUNCA interromper processos do sistema
                "ProcessTermination",
                "ServiceStop",
                "SystemProcessKill",
                
                // NUNCA modificar hardware diretamente
                "CpuOverclock",
                "GpuOverclock",
                "MemoryOverclock",
                "FanControl",
                
                // NUNCA operações de disco perigosas
                "DiskDefragmentation",
                "DiskCleanup",
                "SystemFileModification",
                
                // NUNCA operações de rede agressivas
                "NetworkReset",
                "FirewallModification",
                "DnsModification"
            };

            // Parâmetros que devem ser restritos
            _restrictedParameters = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "trim_working_sets",
                "disable_write_caching",
                "modify_system_files",
                "change_registry",
                "terminate_processes",
                "stop_services",
                "modify_drivers",
                "overclock_hardware",
                "delete_files",
                "format_disks"
            };

            // Thresholds seguros para operações
            _safeThresholds = new Dictionary<string, double>
            {
                ["max_priority_change"] = 1, // Apenas 1 nível de mudança
                ["max_threads_affected"] = 5, // Máximo 5 threads
                ["max_processes_modified"] = 3, // Máximo 3 processos
                ["max_memory_pressure"] = 0.8, // Máximo 80% de uso de memória
                ["max_cpu_usage"] = 0.9, // Máximo 90% de CPU
                ["max_execution_time_ms"] = 5000, // Máximo 5 segundos
                ["max_io_operations"] = 10 // Máximo 10 operações IO
            };
        }

        /// <summary>
        /// Validar se uma ação é segura para execução automática
        /// </summary>
        public SafetyValidation ValidateAction(string actionType, Dictionary<string, object> parameters)
        {
            try
            {
                var validation = new SafetyValidation
                {
                    ActionType = actionType,
                    IsSafe = true,
                    Warnings = new List<string>(),
                    BlockedReasons = new List<string>()
                };

                // 1. Verificar se ação está bloqueada
                if (_blockedActions.Contains(actionType))
                {
                    validation.IsSafe = false;
                    validation.BlockedReasons.Add($"Action '{actionType}' is blocked for safety");
                    _logger.LogWarning($"[SafePolicy] Blocked dangerous action: {actionType}");
                    return validation;
                }

                // 2. Verificar parâmetros restritos
                foreach (var param in parameters.Keys)
                {
                    if (_restrictedParameters.Contains(param))
                    {
                        validation.IsSafe = false;
                        validation.BlockedReasons.Add($"Parameter '{param}' is restricted for safety");
                        _logger.LogWarning($"[SafePolicy] Blocked restricted parameter: {param}");
                    }
                }

                // 3. Validar thresholds de segurança
                ValidateThresholds(validation, parameters);

                // 4. Validar contexto de execução
                ValidateExecutionContext(validation, parameters);

                // 5. Log do resultado
                if (validation.IsSafe)
                {
                    _logger.LogInfo($"[SafePolicy] Action validated as safe: {actionType}");
                    if (validation.Warnings.Any())
                    {
                        _logger.LogInfo($"[SafePolicy] Warnings: {string.Join(", ", validation.Warnings)}");
                    }
                }
                else
                {
                    _logger.LogWarning($"[SafePolicy] Action blocked: {actionType} - {string.Join(", ", validation.BlockedReasons)}");
                }

                return validation;
            }
            catch (Exception ex)
            {
                _logger.LogError("[SafePolicy] Error validating action", ex);
                return new SafetyValidation
                {
                    ActionType = actionType,
                    IsSafe = false,
                    BlockedReasons = new List<string> { "Validation error occurred" }
                };
            }
        }

        /// <summary>
        /// Verificar se o contexto atual é seguro para otimizações
        /// </summary>
        public bool IsSafeContext()
        {
            try
            {
                // Verificar se sistema está em estado crítico
                using var process = System.Diagnostics.Process.GetCurrentProcess();
                
                // Se CPU usage muito alto, não executar otimizações
                var cpuUsage = GetCpuUsage();
                if (cpuUsage > _safeThresholds["max_cpu_usage"])
                {
                    _logger.LogWarning($"[SafePolicy] Unsafe context: High CPU usage ({cpuUsage:P1})");
                    return false;
                }

                // Se memória muito alta, não executar otimizações que usam mais memória
                var memoryMB = process.WorkingSet64 / 1024.0 / 1024.0;
                var totalMemory = GC.GetTotalMemory(false) / 1024.0 / 1024.0;
                var memoryPressure = totalMemory / (Environment.WorkingSet / 1024.0 / 1024.0);
                
                if (memoryPressure > _safeThresholds["max_memory_pressure"])
                {
                    _logger.LogWarning($"[SafePolicy] Unsafe context: High memory pressure ({memoryPressure:P1})");
                    return false;
                }

                // Se muitos threads, não executar otimizações que criam mais threads
                if (process.Threads.Count > 100)
                {
                    _logger.LogWarning($"[SafePolicy] Unsafe context: High thread count ({process.Threads.Count})");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("[SafePolicy] Error checking safe context", ex);
                return false; // Em caso de erro, assumir contexto inseguro
            }
        }

        /// <summary>
        /// Obter lista de ações permitidas
        /// </summary>
        public List<string> GetAllowedActions()
        {
            return new List<string>
            {
                "AdjustProcessPriorities",
                "OptimizeThreadUsage",
                "ReduceBackgroundIO",
                "OptimizeGpuSettings",
                "ReduceSystemLatency",
                "OptimizeDiskAccess",
                "GpuOptimization",
                "RebalancePriorities"
            };
        }

        /// <summary>
        /// Obter lista de ações bloqueadas com motivos
        /// </summary>
        public Dictionary<string, string> GetBlockedActionsWithReasons()
        {
            return new Dictionary<string, string>
            {
                ["GarbageCollection"] = "Manual GC can cause performance degradation and memory fragmentation",
                ["MemoryCleanup"] = "Generic memory cleanup increases memory usage without benefits",
                ["RegistryModification"] = "Registry changes can cause system instability",
                ["ProcessTermination"] = "Terminating processes can cause system crashes",
                ["DriverModification"] = "Driver changes can cause blue screens and hardware issues",
                ["DiskDefragmentation"] = "Defragmentation can cause high IO and system slowdown",
                ["NetworkReset"] = "Network reset can disconnect applications and services"
            };
        }

        #region Métodos de Validação

        private void ValidateThresholds(SafetyValidation validation, Dictionary<string, object> parameters)
        {
            // Validar número de processos
            if (parameters.TryGetValue("max_processes_modified", out var maxProcessesObj) && 
                maxProcessesObj is int maxProcesses && 
                maxProcesses > _safeThresholds["max_processes_modified"])
            {
                validation.IsSafe = false;
                validation.BlockedReasons.Add($"Too many processes to modify: {maxProcesses} > {_safeThresholds["max_processes_modified"]}");
            }

            // Validar tempo de execução
            if (parameters.TryGetValue("execution_time_ms", out var execTimeObj) && 
                execTimeObj is double execTime && 
                execTime > _safeThresholds["max_execution_time_ms"])
            {
                validation.Warnings.Add($"Long execution time: {execTime}ms");
            }

            // Validar operações IO
            if (parameters.TryGetValue("io_operations", out var ioOpsObj) && 
                ioOpsObj is int ioOps && 
                ioOps > _safeThresholds["max_io_operations"])
            {
                validation.IsSafe = false;
                validation.BlockedReasons.Add($"Too many IO operations: {ioOps} > {_safeThresholds["max_io_operations"]}");
            }
        }

        private void ValidateExecutionContext(SafetyValidation validation, Dictionary<string, object> parameters)
        {
            // Verificar se está tentando modificar processos críticos
            if (parameters.TryGetValue("target_processes", out var targetProcObj) && 
                targetProcObj is List<string> targetProcesses)
            {
                var criticalProcesses = new[] { "system", "csrss", "winlogon", "dwm", "lsass" };
                
                foreach (var criticalProc in criticalProcesses)
                {
                    if (targetProcesses.Any(p => p.Contains(criticalProc, StringComparison.OrdinalIgnoreCase)))
                    {
                        validation.IsSafe = false;
                        validation.BlockedReasons.Add($"Cannot modify critical system process: {criticalProc}");
                    }
                }
            }

            // Verificar se está tentando reduzir resolução ou qualidade
            if (parameters.TryGetValue("reduce_resolution", out var reduceResObj) && 
                reduceResObj is bool reduceRes && reduceRes)
            {
                validation.Warnings.Add("Resolution reduction detected - user should be notified");
            }

            // Verificar se está tentando desativar features importantes
            if (parameters.TryGetValue("disable_critical_features", out var disableFeatObj) && 
                disableFeatObj is bool disableFeat && disableFeat)
            {
                validation.IsSafe = false;
                validation.BlockedReasons.Add("Cannot disable critical system features");
            }
        }

        private double GetCpuUsage()
        {
            try
            {
                using var process = System.Diagnostics.Process.GetCurrentProcess();
                var startTime = DateTime.UtcNow;
                var startCpuUsage = process.TotalProcessorTime.TotalMilliseconds;

                System.Threading.Thread.Sleep(100);

                var endTime = DateTime.UtcNow;
                var endCpuUsage = process.TotalProcessorTime.TotalMilliseconds;

                var cpuUsedMs = endCpuUsage - startCpuUsage;
                var totalMsPassed = (endTime - startTime).TotalMilliseconds;
                
                var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);
                return cpuUsageTotal;
            }
            catch
            {
                return 0;
            }
        }

        #endregion
    }

    #region Modelos de Validação

    public class SafetyValidation
    {
        public string ActionType { get; set; } = string.Empty;
        public bool IsSafe { get; set; }
        public List<string> Warnings { get; set; } = new();
        public List<string> BlockedReasons { get; set; } = new();
        public DateTime ValidatedAt { get; set; } = DateTime.UtcNow;
    }

    #endregion
}
