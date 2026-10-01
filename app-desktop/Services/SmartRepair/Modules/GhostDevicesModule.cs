using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.SmartRepair.Architecture;

namespace VoltrisOptimizer.Services.SmartRepair.Modules
{
    public class GhostDevicesModule : ISmartRepairModule
    {
        private readonly ILoggingService _logger;
        private readonly ISmartRepairEventBus _eventBus;
        private readonly string _correlationId;

        private static readonly HashSet<string> EssentialDeviceClasses = new(StringComparer.OrdinalIgnoreCase)
        {
            "Keyboard", "Mouse", "Video", "Disk", "Storage", "System", "Processor", "USB"
        };

        private static readonly string[] EssentialDeviceKeywords =
        {
            "Standard PS/2", "HID", "Keyboard", "Mouse", "Touchpad", "TouchPad",
            "Display adapter", "Video Controller", "Graphics", "Disk drive",
            "Storage controller", "Volume", "System board", "System timer",
            "System speaker", "System CMOS", "Motherboard resources"
        };

        public string ModuleId => "GhostDevices";
        public string Category => "Hardware";

        public string Name => Services.LocalizationService.Instance.GetString("SmartRepair_Module_GhostDevices_Name") ?? "Remoção de Dispositivos Fantasmas";
        public string Description => Services.LocalizationService.Instance.GetString("SmartRepair_Module_GhostDevices_Desc") ?? "Remove dispositivos ocultos/fantasmas do gerenciador de dispositivos que não estão mais presentes no sistema.";

        public ModuleCapabilities Capabilities { get; } = new ModuleCapabilities
        {
            SupportsScan = true,
            SupportsSimulation = true,
            SupportsExecution = true,
            SupportsRollback = true,
            SupportsParallelism = false,
            HasDestructiveOperations = false,
            MaxRiskLevel = RiskLevel.Moderate,
            RequiresAdmin = true
        };

        public IReadOnlyList<string> Dependencies => Array.Empty<string>();

        public GhostDevicesModule(ILoggingService logger, ISmartRepairEventBus eventBus, string correlationId)
        {
            _logger = logger;
            _eventBus = eventBus;
            _correlationId = correlationId;
        }

        private class GhostDevice
        {
            public string InstanceId { get; set; } = string.Empty;
            public string DeviceName { get; set; } = string.Empty;
            public string ClassName { get; set; } = string.Empty;
            public string PublishedName { get; set; } = string.Empty;
            public string DriverProvider { get; set; } = string.Empty;
            public string DriverVersion { get; set; } = string.Empty;
            public bool IsPresent { get; set; }
            public string Status { get; set; } = string.Empty;
            public string ProblemCode { get; set; } = string.Empty;
        }

        public async Task<ModuleScanResult> ScanAsync(IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleScanResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_GhostDevices_ScanStarting") ?? "Verificando dispositivos fantasmas no sistema...", SmartRepairEventType.OperationStarted);

            // Skip enterprise profiles
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem");
                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    var domain = obj["Domain"]?.ToString() ?? "";
                    if (domain.Contains(".local", StringComparison.OrdinalIgnoreCase) || IsEnterpriseEnvironment())
                    {
                        _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_GhostDevices_SkippedEnterprise") ?? "Ambiente corporativo detectado — módulo ignorado.", SmartRepairEventType.WarningRaised);
                        result.Message = Services.LocalizationService.Instance.GetString("SmartRepair_Module_GhostDevices_SkippedEnterprise");
                        return result;
                    }
                }
            }
            catch (Exception exDomain) { _logger?.LogWarning($"[GhostDevices] Erro ao verificar domínio: {exDomain.Message}"); }

            await Task.Run(() =>
            {
                try
                {
                    var ghosts = new List<GhostDevice>();

                    _logger.LogDebug($"[GhostDevices] Iniciando enumeração WMI de dispositivos PnP...");

                    // Use WMI to enumerate devices with problems or not present
                    using var devSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity");
                    foreach (ManagementObject dev in devSearcher.Get())
                    {
                using var __dispose_dev = dev;
                        ct.ThrowIfCancellationRequested();
                        result.Statistics.ItemsScanned++;

                        var deviceId = dev["DeviceID"]?.ToString() ?? "";
                        var name = dev["Name"]?.ToString() ?? "";
                        var className = dev["Name"]?.ToString() ?? "";
                        var status = dev["Status"]?.ToString() ?? "";
                        var configManagerErrorCode = dev["ConfigManagerErrorCode"]?.ToString() ?? "";
                        var isPresent = false;
                        bool? present = dev["Present"] as bool?;
                        if (present.HasValue)
                            isPresent = present.Value;

                        if (string.IsNullOrEmpty(deviceId)) continue;

                        // A ghost device is not present (IsPresent=false) OR has error code
                        // OR is a non-present device in device tree
                        bool isGhost = false;

                        if (configManagerErrorCode == "28" || // drivers not installed
                            configManagerErrorCode == "31" || // device not working properly
                            configManagerErrorCode == "45")   // device not connected to computer
                        {
                            isGhost = true;
                        }

                        if (!isPresent && !string.IsNullOrEmpty(name))
                            isGhost = true;

                        if (!isGhost) continue;

                        // Check if it's an essential device
                        if (IsEssentialDevice(name, className, deviceId))
                        {
                            _logger.LogDebug($"[GhostDevices] Skipping essential device: {name} [{className}]");
                            continue;
                        }

                        // Extract published name from deviceId
                        var pubName = ExtractPublishedName(deviceId);

                        ghosts.Add(new GhostDevice
                        {
                            InstanceId = deviceId,
                            DeviceName = name,
                            ClassName = className,
                            PublishedName = pubName,
                            Status = status,
                            ProblemCode = configManagerErrorCode,
                            IsPresent = isPresent
                        });
                    }

                    result.Statistics.ItemsFound = ghosts.Count;
                    result.FoundItems.AddRange(ghosts);

                    progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = result.Message ?? "Scan concluído." });

                    _logger.LogInfo($"[GhostDevices] Scan concluído: {ghosts.Count} dispositivos fantasmas encontrados, {result.Statistics.ItemsScanned} escaneados, CorrelationId={_correlationId}");

                    if (ghosts.Count > 0)
                    {
                        _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_GhostDevices_ScanFound") ?? "{0} dispositivo(s) fantasma(s) encontrado(s).", ghosts.Count), SmartRepairEventType.OperationFinished);
                    }
                    else
                    {
                        _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_GhostDevices_ScanNone") ?? "Nenhum dispositivo fantasma encontrado.", SmartRepairEventType.OperationFinished);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogError($"[GhostDevices] Erro no scan", ex);
                    result.Success = false;
                    result.Message = ex.Message;
                }
            }, ct);

            return result;
        }

        private static bool IsEssentialDevice(string name, string className, string deviceId)
        {
            if (EssentialDeviceClasses.Contains(className))
                return true;

            foreach (var kw in EssentialDeviceKeywords)
            {
                if (name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            // Check deviceId for critical hardware IDs
            if (deviceId.Contains("PCI\\VEN_8086", StringComparison.OrdinalIgnoreCase) &&
                (deviceId.Contains("DEV_0A", StringComparison.OrdinalIgnoreCase) ||
                 deviceId.Contains("DEV_01", StringComparison.OrdinalIgnoreCase)))
                return true;

            return false;
        }

        private static string ExtractPublishedName(string instanceId)
        {
            // pnputil uses the published name which is extracted from the INF
            // We extract the hardware ID part
            var parts = instanceId.Split('\\');
            return parts.Length > 0 ? parts[^1] : instanceId;
        }

        private static bool IsEnterpriseEnvironment()
        {
            try
            {
                using var key = RegistryHelper.OpenKeySafe(
                    Microsoft.Win32.RegistryHive.LocalMachine,
                    Microsoft.Win32.RegistryView.Registry64,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", false);

                var val = RegistryHelper.GetValueString(key, "EnableLUA");
                return val == "0";
            }
            catch { return false; }
        }

        public Task<ModuleSimulationResult> SimulateAsync(ModuleScanResult scanResult, CancellationToken ct)
        {
            var sim = new ModuleSimulationResult();
            var ghosts = scanResult.FoundItems.Cast<GhostDevice>().ToList();

            sim.EstimatedStatistics.ItemsFound = ghosts.Count;
            sim.RiskLevel = RiskLevel.Moderate;

            foreach (var ghost in ghosts)
            {
                sim.ItemsToProcess.Add(ghost);
                _eventBus.PublishMessage(_correlationId, ModuleId, string.Format("Dispositivo fantasma: {0} ({1})", ghost.DeviceName, ghost.ClassName), SmartRepairEventType.ProgressChanged);
            }

            _logger.LogInfo($"[GhostDevices] Simulação concluída. Itens={sim.ItemsToProcess.Count}, Risk={sim.RiskLevel}");

            return Task.FromResult(sim);
        }

        public async Task<ModuleExecutionResult> ExecuteAsync(ModuleSimulationResult simResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleExecutionResult();
            var ghosts = simResult.ItemsToProcess.Cast<GhostDevice>().ToList();

            if (ghosts.Count == 0)
            {
                _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_GhostDevices_ExecDone") ?? "Nenhum dispositivo fantasma para remover.", SmartRepairEventType.OperationFinished);
                return result;
            }

            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_GhostDevices_ExecStarting") ?? "Removendo dispositivos fantasmas...", SmartRepairEventType.OperationStarted);
            progress?.Report(new RepairProgress { StepPercent = 0, StatusMessage = "Preparando remoção..." });

            // Create restore point before removal
            try
            {
                _logger.LogDebug($"[GhostDevices] Criando ponto de restauração antes da remoção...");
                ProcessHelper.Run("powershell", $"-NoProfile -Command \"Checkpoint-Computer -Description 'VoltrisGhostDevices_{DateTime.Now:yyyyMMdd_HHmmss}' -RestorePointType MODIFY_SETTINGS\"", out _, out _);
                _logger.LogInfo("[GhostDevices] Restore point created before device removal.");
            }
            catch (Exception exRestore) { _logger?.LogWarning($"[GhostDevices] Erro ao criar restore point: {exRestore.Message}"); }

            int total = ghosts.Count;
            int current = 0;

            foreach (var ghost in ghosts)
            {
                ct.ThrowIfCancellationRequested();
                current++;

                var percent = current * 90 / total;
                progress?.Report(new RepairProgress { StepPercent = percent, StatusMessage = string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_GhostDevices_ExecProgress") ?? "Removendo {0}...", ghost.DeviceName) });
                _eventBus.PublishMessage(_correlationId, ModuleId, string.Format("Removendo dispositivo fantasma: {0}", ghost.DeviceName), SmartRepairEventType.ProgressChanged);

                try
                {
                    // Export driver backup before removal
                    if (!string.IsNullOrEmpty(ghost.PublishedName))
                    {
                        _logger.LogDebug($"[GhostDevices] Exportando driver para {ghost.DeviceName}...");
                        ProcessHelper.Run("pnputil", $"/export-driver \"{ghost.PublishedName}\"", out var exportOut, out var exportErr);
                        _logger.LogDebug($"[GhostDevices] Export driver for {ghost.DeviceName}: {exportOut} {exportErr}");
                    }

                    // Remove ghost device
                    _logger.LogDebug($"[GhostDevices] Removendo dispositivo {ghost.DeviceName} (InstanceId={ghost.InstanceId})...");
                    ProcessHelper.Run("pnputil", $"-d \"{ghost.InstanceId}\"", out var removeOut, out var removeErr);

                    if (removeErr.Contains("success", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(removeErr))
                    {
                        result.FinalStatistics.ItemsRepaired++;
                        _eventBus.PublishMessage(_correlationId, ModuleId, string.Format("{0} removido.", ghost.DeviceName), SmartRepairEventType.ProgressChanged);
                    }
                    else
                    {
                        // Try force removal
                        _logger.LogDebug($"[GhostDevices] Tentativa de remoção forçada para {ghost.DeviceName}...");
                        ProcessHelper.Run("pnputil", $"-f -d \"{ghost.InstanceId}\"", out var forceOut, out var forceErr);
                        if (string.IsNullOrEmpty(forceErr) || forceErr.Contains("success", StringComparison.OrdinalIgnoreCase))
                        {
                            result.FinalStatistics.ItemsRepaired++;
                        }
                        else
                        {
                            result.FailedItems.Add(ghost);
                            result.FinalStatistics.ItemsIgnored++;
                            _logger.LogWarning($"[GhostDevices] Failed to remove {ghost.DeviceName}: {forceErr}");
                        }
                    }

                    result.ProcessedItems.Add(ghost);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogError($"[GhostDevices] Error removing {ghost.DeviceName}", ex);
                    result.FailedItems.Add(ghost);
                    result.FinalStatistics.ErrorCount++;
                }
            }

            progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_GhostDevices_ExecDone") ?? "Remoção de dispositivos fantasmas concluída." });
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_GhostDevices_ExecDone") ?? "Remoção de dispositivos fantasmas concluída.", SmartRepairEventType.OperationFinished);
            _logger.LogInfo($"[GhostDevices] Execução concluída. Repaired={result.FinalStatistics.ItemsRepaired}, Failed={result.FailedItems.Count}, ErrorCount={result.FinalStatistics.ErrorCount}, SpaceRecoveredBytes={result.FinalStatistics.SpaceRecoveredBytes}, CorrelationId={_correlationId}");

            return result;
        }

        public async Task<ModuleRollbackResult> RollbackAsync(ModuleExecutionResult execResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleRollbackResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_GhostDevices_RollbackStarting") ?? "Iniciando rollback — restaurando dispositivos via ponto de restauração...", SmartRepairEventType.RollbackStarted);
            progress?.Report(new RepairProgress { StepPercent = 0, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_GhostDevices_RollbackStarting") ?? "Restaurando ponto de restauração..." });

            try
            {
                // Use System Restore to rollback
                ProcessHelper.Run("powershell", "-NoProfile -Command \"Get-ComputerRestorePoint | Sort-Object -Property CreationTime -Descending | Select-Object -First 1 | ForEach-Object { Restore-Computer -RestorePoint $_ -Confirm:$false }\"", out var rpOut, out var rpErr);

                // Re-add exported drivers
                var failedDevices = execResult.FailedItems;
                foreach (var dev in execResult.ProcessedItems)
                {
                    ct.ThrowIfCancellationRequested();
                    if (dev is GhostDevice ghost && !string.IsNullOrEmpty(ghost.PublishedName))
                    {
                        ProcessHelper.Run("pnputil", $"/add-driver \"{ghost.PublishedName}.inf\"", out var addOut, out var addErr);
                        if (string.IsNullOrEmpty(addErr) || addErr.Contains("success", StringComparison.OrdinalIgnoreCase))
                        {
                            result.ItemsRestored++;
                        }
                        else
                        {
                            result.ItemsFailedToRestore++;
                        }
                    }
                }

                result.Success = true;
                _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_GhostDevices_RollbackDone") ?? "Rollback concluído. Dispositivos restaurados.", SmartRepairEventType.RollbackFinished);
                _logger.LogInfo($"[GhostDevices] Rollback concluído. Restored={result.ItemsRestored}, Failed={result.ItemsFailedToRestore}, CorrelationId={_correlationId}");
                progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = "Rollback concluído." });
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GhostDevices] Rollback error", ex);
                result.Success = false;
                result.Message = ex.Message;
            }

            return result;
        }
    }
}
