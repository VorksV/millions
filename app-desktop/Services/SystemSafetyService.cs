using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core;
using VoltrisOptimizer.Core.Constants;
using VoltrisOptimizer.Interfaces;
using Microsoft.Win32;
using VoltrisOptimizer.Services.Power;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// Tipo de operação perigosa
    /// </summary>
    public enum DangerousOperationType
    {
        RegistryModification,
        ServiceControl,
        NetworkStackReset,
        DriverTweaks,
        SystemFileModification,
        ProcessTermination
    }

    /// <summary>
    /// Resultado da verificação de segurança
    /// </summary>
    public class SafetyCheckResult
    {
        public bool IsAllowed { get; set; }
        public string Reason { get; set; } = string.Empty;
        public DangerousOperationType OperationType { get; set; }
        public string[] Warnings { get; set; } = Array.Empty<string>();
        public bool RequiresReboot { get; set; }
        public bool RequiresConfirmation { get; set; }
    }

    /// <summary>
    /// Ponto de restauração do sistema
    /// </summary>
    public class SystemRestorePoint
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public string Description { get; set; } = string.Empty;
        public List<RegistryBackup> RegistryBackups { get; set; } = new();
        public Dictionary<string, object?> ServiceStates { get; set; } = new();
        public string? PowerPlanGuid { get; set; }
    }

    /// <summary>
    /// Serviço de segurança do sistema
    /// Fornece validações e proteções antes de operações perigosas
    /// </summary>
    public class SystemSafetyService
    {
        private readonly ILoggingService? _logger;
        private readonly IRegistryService? _registryService;
        private readonly string _restorePointsPath;
        private readonly List<SystemRestorePoint> _restorePoints = new();
        private SystemRestorePoint? _currentRestorePoint;

        public SystemSafetyService(ILoggingService? logger = null, IRegistryService? registryService = null)
        {
            _logger = logger;
            _registryService = registryService;
            _restorePointsPath = Path.Combine(
                Helpers.FileSystemHelper.GetApplicationBasePath(),
                "Backups",
                "restore_points.json"
            );
            LoadRestorePoints();
        }

        /// <summary>
        /// Verifica se uma operação de registro é segura
        /// </summary>
        public SafetyCheckResult CheckRegistryOperation(string keyPath, string valueName, object? newValue)
        {
            var result = new SafetyCheckResult
            {
                OperationType = DangerousOperationType.RegistryModification,
                IsAllowed = true
            };

            var warnings = new List<string>();

            // Verificar chaves críticas do sistema
            var criticalPaths = new[]
            {
                @"SYSTEM\CurrentControlSet\Services\",
                @"SYSTEM\CurrentControlSet\Control\Session Manager\",
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon",
                @"SECURITY\",
                @"SAM\"
            };

            foreach (var criticalPath in criticalPaths)
            {
                if (keyPath.Contains(criticalPath, StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add($"Modificação em área crítica do sistema: {criticalPath}");
                    result.RequiresConfirmation = true;
                }
            }

            // Verificar operações específicas perigosas
            if (keyPath.Contains("GraphicsDrivers", StringComparison.OrdinalIgnoreCase))
            {
                if (valueName.Equals("TdrLevel", StringComparison.OrdinalIgnoreCase) && 
                    newValue is int tdrLevel && tdrLevel == 0)
                {
                    warnings.Add("⚠️ AVISO: Desabilitar TDR pode causar travamentos irrecuperáveis da GPU.");
                    warnings.Add("O sistema não poderá se recuperar automaticamente de travamentos de GPU.");
                    result.RequiresConfirmation = true;
                    result.RequiresReboot = true;
                }
            }

            if (keyPath.Contains("PriorityControl", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add("Modificação de prioridade de processos pode afetar a estabilidade do sistema.");
            }

            result.Warnings = warnings.ToArray();
            result.Reason = warnings.Count > 0 
                ? string.Join(" | ", warnings) 
                : "Operação considerada segura.";

            _logger?.LogInfo($"[Safety] Verificação de registro: {keyPath}\\{valueName} - Permitido: {result.IsAllowed}");

            return result;
        }

        /// <summary>
        /// Verifica se uma operação de serviço é segura
        /// </summary>
        public SafetyCheckResult CheckServiceOperation(string serviceName, bool isStopOperation)
        {
            var result = new SafetyCheckResult
            {
                OperationType = DangerousOperationType.ServiceControl,
                IsAllowed = true
            };

            var warnings = new List<string>();

            // Verificar serviços críticos
            if (SystemConstants.WindowsServices.Critical.Contains(serviceName, StringComparer.OrdinalIgnoreCase))
            {
                result.IsAllowed = false;
                result.Reason = $"O serviço '{serviceName}' é crítico para o funcionamento do Windows e não pode ser modificado.";
                _logger?.LogWarning($"[Safety] Bloqueada tentativa de modificar serviço crítico: {serviceName}");
                return result;
            }

            // Avisos para serviços importantes mas não críticos
            if (serviceName.Equals("Spooler", StringComparison.OrdinalIgnoreCase) && isStopOperation)
            {
                warnings.Add("Parar o Spooler desabilitará a impressão.");
            }

            if (serviceName.Equals("WSearch", StringComparison.OrdinalIgnoreCase) && isStopOperation)
            {
                warnings.Add("Parar o Windows Search pode tornar a busca mais lenta.");
            }

            if (serviceName.Equals("SysMain", StringComparison.OrdinalIgnoreCase) && isStopOperation)
            {
                warnings.Add("Parar o Superfetch pode aumentar o tempo de carregamento de aplicativos frequentes.");
            }

            result.Warnings = warnings.ToArray();
            result.Reason = warnings.Count > 0 
                ? string.Join(" | ", warnings) 
                : "Operação considerada segura.";

            return result;
        }

        /// <summary>
        /// Verifica se uma operação de reset de rede é segura
        /// </summary>
        public SafetyCheckResult CheckNetworkReset(bool isWinsockReset, bool isIpStackReset)
        {
            var result = new SafetyCheckResult
            {
                OperationType = DangerousOperationType.NetworkStackReset,
                IsAllowed = true,
                RequiresReboot = isWinsockReset || isIpStackReset
            };

            var warnings = new List<string>();

            if (isWinsockReset)
            {
                warnings.Add("⚠️ Reset do Winsock requer reinicialização do computador.");
                warnings.Add("Conexões de rede ativas serão perdidas.");
                warnings.Add("Configurações de VPN podem precisar ser reconfiguradas.");
                result.RequiresConfirmation = true;
            }

            if (isIpStackReset)
            {
                warnings.Add("⚠️ Reset da pilha IP requer reinicialização do computador.");
                warnings.Add("Configurações de IP estático podem ser perdidas.");
                result.RequiresConfirmation = true;
            }

            result.Warnings = warnings.ToArray();
            result.Reason = warnings.Count > 0 
                ? string.Join(" | ", warnings) 
                : "Operação considerada segura.";

            return result;
        }

        /// <summary>
        /// Verifica se o término de um processo é seguro
        /// </summary>
        public SafetyCheckResult CheckProcessTermination(string processName)
        {
            var result = new SafetyCheckResult
            {
                OperationType = DangerousOperationType.ProcessTermination,
                IsAllowed = true
            };

            // Verificar processos do sistema
            if (SystemConstants.ProtectedProcesses.System.Contains(processName, StringComparer.OrdinalIgnoreCase))
            {
                result.IsAllowed = false;
                result.Reason = $"O processo '{processName}' é um processo do sistema e não pode ser encerrado.";
                _logger?.LogWarning($"[Safety] Bloqueada tentativa de encerrar processo do sistema: {processName}");
                return result;
            }

            // Verificar processo do Voltris
            if (SystemConstants.ProtectedProcesses.Voltris.Contains(processName, StringComparer.OrdinalIgnoreCase))
            {
                result.IsAllowed = false;
                result.Reason = "Não é possível encerrar o próprio processo do Voltris Optimizer.";
                return result;
            }

            // Avisos para launchers de jogos
            if (SystemConstants.ProtectedProcesses.Launchers.Contains(processName, StringComparer.OrdinalIgnoreCase))
            {
                result.Warnings = new[] { $"Encerrar {processName} pode afetar jogos em execução." };
                result.RequiresConfirmation = true;
            }

            return result;
        }

        /// <summary>
        /// Cria um ponto de restauração antes de operações perigosas
        /// </summary>
        public async Task<SystemRestorePoint> CreateRestorePointAsync(string description)
        {
            var restorePoint = new SystemRestorePoint
            {
                Description = description,
                CreatedAt = DateTime.Now
            };

            try
            {
                // Capturar plano de energia atual
                restorePoint.PowerPlanGuid = await GetCurrentPowerPlanGuidAsync().ConfigureAwait(false);

                // Capturar estado dos serviços configuráveis
                foreach (var serviceName in SystemConstants.WindowsServices.GamingPausable
                    .Concat(SystemConstants.WindowsServices.SecondaryPausable))
                {
                    try
                    {
                        using var sc = new System.ServiceProcess.ServiceController(serviceName);
                        restorePoint.ServiceStates[serviceName] = sc.Status.ToString();
                    }
                    catch
                    {
                        // Serviço pode não existir
                    }
                }

                _currentRestorePoint = restorePoint;
                _restorePoints.Add(restorePoint);
                SaveRestorePoints();

                _logger?.LogSuccess($"[Safety] Ponto de restauração criado: {description} (ID: {restorePoint.Id})");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[Safety] Erro ao criar ponto de restauração: {ex.Message}", ex);
            }

            return restorePoint;
        }

        /// <summary>
        /// Adiciona backup de registro ao ponto de restauração atual
        /// </summary>
        public void AddRegistryBackupToCurrentPoint(RegistryBackup backup)
        {
            if (_currentRestorePoint != null)
            {
                _currentRestorePoint.RegistryBackups.Add(backup);
                SaveRestorePoints();
            }
        }

        /// <summary>
        /// Lista todos os pontos de restauração
        /// </summary>
        public IReadOnlyList<SystemRestorePoint> GetRestorePoints()
        {
            return _restorePoints.AsReadOnly();
        }

        /// <summary>
        /// Restaura um ponto de restauração específico
        /// </summary>
        public async Task<bool> RestoreFromPointAsync(string restorePointId)
        {
            var point = _restorePoints.FirstOrDefault(p => p.Id == restorePointId);
            if (point == null)
            {
                _logger?.LogWarning($"[Safety] Ponto de restauração não encontrado: {restorePointId}");
                return false;
            }

            _logger?.LogInfo($"[Safety] Iniciando restauração do ponto: {point.Description}");

            bool success = true;

            // Restaurar backups de registro
            if (_registryService != null)
            {
                foreach (var backup in point.RegistryBackups.AsEnumerable().Reverse())
                {
                    var result = _registryService.RestoreValue(backup);
                    if (!result.Success)
                    {
                        _logger?.LogWarning($"[Safety] Falha ao restaurar: {backup.KeyPath}\\{backup.ValueName}");
                        success = false;
                    }
                }
            }

            // Restaurar plano de energia
            if (!string.IsNullOrEmpty(point.PowerPlanGuid))
            {
                try
                {
                    if (Guid.TryParse(point.PowerPlanGuid, out Guid targetGuid))
                    {
                        ProfilePowerAuthority.RequestProfileApply("SystemSafety", "pedido legado de troca de plano", _logger);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning($"[Safety] Falha ao restaurar plano de energia: {ex.Message}");
                    success = false;
                }
            }

            // Restaurar estado dos serviços
            foreach (var kvp in point.ServiceStates)
            {
                try
                {
                    using var sc = new System.ServiceProcess.ServiceController(kvp.Key);
                    var targetStatus = kvp.Value?.ToString();

                    if (targetStatus == "Running" && sc.Status != System.ServiceProcess.ServiceControllerStatus.Running)
                    {
                        sc.Start();
                        sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
                    }
                    else if (targetStatus == "Stopped" && sc.Status != System.ServiceProcess.ServiceControllerStatus.Stopped)
                    {
                        sc.Stop();
                        sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning($"[Safety] Falha ao restaurar serviço {kvp.Key}: {ex.Message}");
                }
            }

            _logger?.LogInfo($"[Safety] Restauração concluída. Sucesso: {success}");
            return success;
        }

        /// <summary>
        /// Remove pontos de restauração antigos
        /// </summary>
        public void CleanupOldRestorePoints(int maxAgeDays = 7, int maxCount = 10)
        {
            var cutoffDate = DateTime.Now.AddDays(-maxAgeDays);

            var toRemove = _restorePoints
                .Where(p => p.CreatedAt < cutoffDate)
                .OrderBy(p => p.CreatedAt)
                .ToList();

            // Manter no máximo maxCount pontos
            while (_restorePoints.Count > maxCount && toRemove.Count < _restorePoints.Count - maxCount)
            {
                var oldest = _restorePoints.Except(toRemove).OrderBy(p => p.CreatedAt).FirstOrDefault();
                if (oldest != null)
                    toRemove.Add(oldest);
            }

            foreach (var point in toRemove)
            {
                _restorePoints.Remove(point);
            }

            if (toRemove.Count > 0)
            {
                SaveRestorePoints();
                _logger?.LogInfo($"[Safety] {toRemove.Count} pontos de restauração antigos removidos");
            }
        }

        private async Task<string?> GetCurrentPowerPlanGuidAsync()
        {
            try
            {
                IntPtr activeGuidPtr;
                uint res = VoltrisOptimizer.Utils.Win32.PowerNativeMethods.PowerGetActiveScheme(IntPtr.Zero, out activeGuidPtr);
                if (res == 0 && activeGuidPtr != IntPtr.Zero)
                {
                    Guid activeGuid = System.Runtime.InteropServices.Marshal.PtrToStructure<Guid>(activeGuidPtr);
                    VoltrisOptimizer.Utils.Win32.PowerNativeMethods.LocalFree(activeGuidPtr);
                    return activeGuid.ToString("D");
                }
            }
            catch
            {
                // Ignorar erros
            }

            return null;
        }

        private void LoadRestorePoints()
        {
            try
            {
                if (File.Exists(_restorePointsPath))
                {
                    var json = File.ReadAllText(_restorePointsPath);
                    var loaded = JsonSerializer.Deserialize<List<SystemRestorePoint>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                    if (loaded != null)
                    {
                        _restorePoints.Clear();
                        _restorePoints.AddRange(loaded);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[Safety] Erro ao carregar pontos de restauração: {ex.Message}");
            }
        }

        private void SaveRestorePoints()
        {
            try
            {
                var directory = Path.GetDirectoryName(_restorePointsPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(_restorePoints, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                File.WriteAllText(_restorePointsPath, json);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[Safety] Erro ao salvar pontos de restauração: {ex.Message}");
            }
        }
    }
}

