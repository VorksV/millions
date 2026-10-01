using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.SmartRepair.Architecture;



namespace VoltrisOptimizer.Services.SmartRepair.Modules
{
    public class GpuOptimizerModule : ISmartRepairModule
    {
        private readonly ILoggingService _logger;
        private readonly ISmartRepairEventBus _eventBus;
        private readonly string _correlationId;

        private const string GpuClassGuid = "{4d36e968-e325-11ce-bfc1-08002be10318}";
        private const string NvidiaBasePath = @"SYSTEM\CurrentControlSet\Control\Class\" + GpuClassGuid + @"\0000";
        private const string AmdBasePath = @"SYSTEM\CurrentControlSet\Control\Class\" + GpuClassGuid + @"\0000\DAL";

        public string ModuleId => "GpuOptimizer";
        public string Category => "Hardware";

        public string Name => Services.LocalizationService.Instance.GetString("SmartRepair_Module_Gpu_Name") ?? "Otimização de Placa de Vídeo (GPU)";
        public string Description => Services.LocalizationService.Instance.GetString("SmartRepair_Module_Gpu_Desc") ?? "Aplica configurações de desempenho otimizadas para GPUs NVIDIA e AMD.";

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

        public GpuOptimizerModule(ILoggingService logger, ISmartRepairEventBus eventBus, string correlationId)
        {
            _logger = logger;
            _eventBus = eventBus;
            _correlationId = correlationId;
        }

        private enum GpuVendor { None, Nvidia, Amd, Intel }

        private class GpuInfo
        {
            public GpuVendor Vendor { get; set; }
            public string AdapterName { get; set; } = string.Empty;
            public string AdapterRam { get; set; } = string.Empty;
            public string DriverVersion { get; set; } = string.Empty;
            public float TemperatureCelsius { get; set; } = -1;
            public bool IsLaptop { get; set; }
            public bool IsOnBattery { get; set; }
            public string DeviceInstancePath { get; set; } = string.Empty;
        }

        public async Task<ModuleScanResult> ScanAsync(IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleScanResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Gpu_ScanStarting") ?? "Verificando adaptadores de vídeo...", SmartRepairEventType.OperationStarted);

            await Task.Run(() =>
            {
                try
                {
                    _logger.LogDebug($"[GpuOptimizer] Iniciando detecção WMI de adaptadores de vídeo...");
                    using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
                    var gpus = searcher.Get().Cast<ManagementObject>().ToList();

                    foreach (var gpu in gpus)
                    {
                        ct.ThrowIfCancellationRequested();
                        result.Statistics.ItemsScanned++;

                        var name = gpu["Name"]?.ToString() ?? "";
                        var ram = gpu["AdapterRAM"]?.ToString() ?? "";
                        var driver = gpu["DriverVersion"]?.ToString() ?? "";
                        var pnpId = gpu["PNPDeviceID"]?.ToString() ?? "";

                        GpuVendor vendor = GpuVendor.None;
                        if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("Quadro", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("NVS", StringComparison.OrdinalIgnoreCase))
                        {
                            vendor = GpuVendor.Nvidia;
                        }
                        else if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                                 name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
                                 name.Contains("FirePro", StringComparison.OrdinalIgnoreCase) ||
                                 name.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
                        {
                            vendor = GpuVendor.Amd;
                        }
                        else if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase) ||
                                 name.Contains("HD Graphics", StringComparison.OrdinalIgnoreCase) ||
                                 name.Contains("UHD Graphics", StringComparison.OrdinalIgnoreCase) ||
                                 name.Contains("Iris", StringComparison.OrdinalIgnoreCase))
                        {
                            vendor = GpuVendor.Intel;
                        }

                        // Check battery/power status via WMI
                            bool onBattery = IsOnBattery();

                        var info = new GpuInfo
                        {
                            Vendor = vendor,
                            AdapterName = name,
                            AdapterRam = ram,
                            DriverVersion = driver,
                            DeviceInstancePath = pnpId,
                            IsLaptop = DetectIsLaptop(),
                            IsOnBattery = onBattery
                        };

                        // Read GPU temperature
                        info.TemperatureCelsius = GetGpuTemperature();

                        result.FoundItems.Add(info);

                        if (vendor == GpuVendor.Nvidia)
                        {
                            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Gpu_ScanNvidia") ?? string.Format("NVIDIA detectada: {0}", name), SmartRepairEventType.ProgressChanged);
                            result.Statistics.ItemsFound++;
                        }
                        else if (vendor == GpuVendor.Amd)
                        {
                            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Gpu_ScanAmd") ?? string.Format("AMD detectada: {0}", name), SmartRepairEventType.ProgressChanged);
                            result.Statistics.ItemsFound++;
                        }
                        else if (vendor == GpuVendor.Intel)
                        {
                            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Gpu_ScanIntegrated") ?? string.Format("GPU Integrada Intel detectada: {0}", name), SmartRepairEventType.ProgressChanged);
                        }
                    }

                    if (result.Statistics.ItemsFound == 0)
                    {
                        _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Gpu_ScanNone") ?? "Nenhuma GPU NVIDIA ou AMD dedicada encontrada.", SmartRepairEventType.OperationFinished);
                    }

                    progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = result.Message ?? "Scan concluído." });

                    _logger.LogInfo($"[GpuOptimizer] Scan concluído. GPUs={result.FoundItems.Count}, Compatible={result.Statistics.ItemsFound}, CorrelationId={_correlationId}");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogError($"[GpuOptimizer] Erro no scan de GPU", ex);
                    result.Success = false;
                    result.Message = ex.Message;
                }
            }, ct);

            return result;
        }

        private static bool DetectIsLaptop()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem");
                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    var chassis = obj["PCSystemType"]?.ToString();
                    return chassis == "2"; // 2 = Laptop/Mobile
                }
            }
            catch { System.Diagnostics.Debug.WriteLine("[GpuOptimizer] Erro ao detectar laptop"); }
            return false;
        }

        private static bool IsOnBattery()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Battery WHERE BatteryStatus > 0 AND BatteryStatus != 2");
                // BatteryStatus 1 = discharging, 2 = AC power connected
                foreach (ManagementObject bat in searcher.Get())
                {
                using var __dispose_bat = bat;
                    var batStatus = Convert.ToInt32(bat["BatteryStatus"]);
                    if (batStatus == 1) return true; // discharging
                }
            }
            catch { System.Diagnostics.Debug.WriteLine("[GpuOptimizer] Erro ao verificar bateria"); }
            return false;
        }

        private static float GetGpuTemperature()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM MSAcpi_ThermalZoneTemperature");
                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    var temp = obj["CurrentTemperature"] as uint?;
                    if (temp.HasValue)
                    {
                        // Temperature in tenths of Kelvin, convert to Celsius
                        return (temp.Value / 10.0f) - 273.15f;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GpuOptimizer] Erro ao ler temperatura GPU via WMI: {ex.Message}");
            }

            // Fallback: try OpenHardwareMonitor WMI if present
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\OpenHardwareMonitor", "SELECT * FROM Sensor WHERE SensorType = 'Temperature'");
                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    var name = obj["Name"]?.ToString() ?? "";
                    if (name.Contains("GPU", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = obj["Value"] as double?;
                        if (val.HasValue) return (float)val.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GpuOptimizer] Erro ao ler temperatura GPU via OpenHardwareMonitor: {ex.Message}");
            }

            return -1;
        }

        public Task<ModuleSimulationResult> SimulateAsync(ModuleScanResult scanResult, CancellationToken ct)
        {
            var sim = new ModuleSimulationResult();
            var gpus = scanResult.FoundItems.Cast<GpuInfo>().ToList();

            sim.EstimatedStatistics.ItemsFound = gpus.Count;
            sim.RiskLevel = RiskLevel.Moderate;

            foreach (var gpu in gpus)
            {
                if (gpu.Vendor == GpuVendor.Intel || gpu.Vendor == GpuVendor.None)
                    continue;
                {
                    _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Gpu_SkippedIntegrated") ?? string.Format("GPU Intel integrada ignorada: {0}", gpu.AdapterName), SmartRepairEventType.ProgressChanged);
                    continue;
                }

                if (gpu.IsLaptop && gpu.IsOnBattery)
                {
                    _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Gpu_SkippedBattery") ?? "GPU otimização ignorada — notebook em bateria. Execute conectado à tomada.", SmartRepairEventType.ProgressChanged);
                    continue;
                }

                if (gpu.TemperatureCelsius > 85)
                {
                    _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Gpu_SkippedHotTemp") ?? string.Format("GPU muito quente ({0}°C) — otimização ignorada para evitar danos.", gpu.TemperatureCelsius.ToString("F0")), SmartRepairEventType.WarningRaised);
                    continue;
                }

                sim.ItemsToProcess.Add(gpu);
            }

            sim.EstimatedStatistics.ItemsFound = sim.ItemsToProcess.Count;

            _logger.LogInfo($"[GpuOptimizer] Simulação concluída. Itens={sim.ItemsToProcess.Count}, Risk={sim.RiskLevel}");

            return Task.FromResult(sim);
        }

        public async Task<ModuleExecutionResult> ExecuteAsync(ModuleSimulationResult simResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleExecutionResult();
            var gpus = simResult.ItemsToProcess.Cast<GpuInfo>().ToList();

            if (gpus.Count == 0)
            {
                _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Gpu_ExecDone") ?? "Nenhuma GPU compatível para otimizar.", SmartRepairEventType.OperationFinished);
                return result;
            }

            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Gpu_ExecStarting") ?? "Aplicando configurações de desempenho da GPU...", SmartRepairEventType.OperationStarted);

            int total = gpus.Count;
            int current = 0;

            foreach (var gpu in gpus)
            {
                ct.ThrowIfCancellationRequested();
                current++;

                var percent = current * 100 / total;
                progress?.Report(new RepairProgress { StepPercent = percent, StatusMessage = string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Gpu_ExecProgress") ?? "Otimizando {0}...", gpu.AdapterName) });

                try
                {
                    if (gpu.Vendor == GpuVendor.Nvidia)
                    {
                        ApplyNvidiaSettings(gpu, result);
                    }
                    else if (gpu.Vendor == GpuVendor.Amd)
                    {
                        ApplyAmdSettings(gpu, result);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogError($"[GpuOptimizer] Falha ao otimizar {gpu.AdapterName}", ex);
                    result.FinalStatistics.ErrorCount++;
                    result.FailedItems.Add(gpu.AdapterName);
                }
            }

            progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_Gpu_ExecDone") ?? "Otimização de GPU concluída." });
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Gpu_ExecDone") ?? "Otimização de GPU concluída.", SmartRepairEventType.OperationFinished);
            _logger.LogInfo($"[GpuOptimizer] Execução concluída. Repaired={result.FinalStatistics.ItemsRepaired}, ErrorCount={result.FinalStatistics.ErrorCount}, SpaceRecoveredBytes={result.FinalStatistics.SpaceRecoveredBytes}, CorrelationId={_correlationId}");

            return result;
        }

        private void ApplyNvidiaSettings(GpuInfo gpu, ModuleExecutionResult result)
        {
            _logger.LogDebug($"[GpuOptimizer] Aplicando configurações NVIDIA para {gpu.AdapterName}...");
            using var key = RegistryHelper.OpenKeySafe(RegistryHive.LocalMachine, RegistryView.Registry64, NvidiaBasePath, true);
            if (key == null)
            {
                _logger.LogWarning($"[GpuOptimizer] NVIDIA registry key not found: {NvidiaBasePath}");
                result.FinalStatistics.ItemsIgnored++;
                return;
            }

            // Save current values for rollback
            var oldProfileName = RegistryHelper.GetValueString(key, "ProfileName") ?? "";
            var oldPowerMode = RegistryHelper.GetValueString(key, "PowerManagementMode") ?? "";

            RegistryHelper.SetValueSafe(key, "ProfileName", "Global", RegistryValueKind.String);
            RegistryHelper.SetValueSafe(key, "PowerManagementMode", "PreferMaximumPerformance", RegistryValueKind.String);

            // Additional NVIDIA optimizations via registry
            RegistryHelper.SetValueSafe(key, "PreferredRefreshRate", "0", RegistryValueKind.DWord);
            RegistryHelper.SetValueSafe(key, "DisableGpuPreferredPowerProfile", "0", RegistryValueKind.DWord);

            result.FinalStatistics.ItemsRepaired++;
            _eventBus.PublishMessage(_correlationId, ModuleId, string.Format("Configurações NVIDIA aplicadas: {0}", gpu.AdapterName), SmartRepairEventType.ProgressChanged);
            _logger.LogInfo($"[GpuOptimizer] NVIDIA settings applied for {gpu.AdapterName}");
        }

        private void ApplyAmdSettings(GpuInfo gpu, ModuleExecutionResult result)
        {
            _logger.LogDebug($"[GpuOptimizer] Aplicando configurações AMD para {gpu.AdapterName}...");
            using var key = RegistryHelper.OpenKeySafe(RegistryHive.LocalMachine, RegistryView.Registry64, AmdBasePath, true);
            if (key == null)
            {
                _logger.LogWarning($"[GpuOptimizer] AMD DAL registry key not found: {AmdBasePath}");
                result.FinalStatistics.ItemsIgnored++;
                return;
            }

            RegistryHelper.SetValueSafe(key, "PowerPlayMethod", "Performance", RegistryValueKind.String);
            RegistryHelper.SetValueSafe(key, "PerformanceLevel", "High", RegistryValueKind.String);
            RegistryHelper.SetValueSafe(key, "PowerSavingMethod", "0", RegistryValueKind.DWord);
            RegistryHelper.SetValueSafe(key, "EnableUlps", "0", RegistryValueKind.DWord);
            RegistryHelper.SetValueSafe(key, "KMD_DeLagEnabled", "1", RegistryValueKind.DWord);

            result.FinalStatistics.ItemsRepaired++;
            _eventBus.PublishMessage(_correlationId, ModuleId, string.Format("Configurações AMD aplicadas: {0}", gpu.AdapterName), SmartRepairEventType.ProgressChanged);
            _logger.LogInfo($"[GpuOptimizer] AMD settings applied for {gpu.AdapterName}");
        }

        public async Task<ModuleRollbackResult> RollbackAsync(ModuleExecutionResult execResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleRollbackResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Gpu_RollbackStarting") ?? "Restaurando configurações padrão da GPU...", SmartRepairEventType.RollbackStarted);

            try
            {
                // Restore NVIDIA defaults
                using (var nvKey = RegistryHelper.OpenKeySafe(RegistryHive.LocalMachine, RegistryView.Registry64, NvidiaBasePath, true))
                {
                    if (nvKey != null)
                    {
                        RegistryHelper.SetValueSafe(nvKey, "PowerManagementMode", "Optimal Power", RegistryValueKind.String);
                        RegistryHelper.SetValueSafe(nvKey, "ProfileName", "Global", RegistryValueKind.String);
                        result.ItemsRestored++;
                    }
                }

                // Restore AMD defaults
                using (var amdKey = RegistryHelper.OpenKeySafe(RegistryHive.LocalMachine, RegistryView.Registry64, AmdBasePath, true))
                {
                    if (amdKey != null)
                    {
                        RegistryHelper.SetValueSafe(amdKey, "PowerPlayMethod", "Balanced", RegistryValueKind.String);
                        RegistryHelper.SetValueSafe(amdKey, "PerformanceLevel", "Automatic", RegistryValueKind.String);
                        RegistryHelper.SetValueSafe(amdKey, "PowerSavingMethod", "1", RegistryValueKind.DWord);
                        result.ItemsRestored++;
                    }
                }

                result.Success = true;
                _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Gpu_RollbackDone") ?? "Configurações da GPU restauradas para o padrão.", SmartRepairEventType.RollbackFinished);
                _logger.LogInfo($"[GpuOptimizer] Rollback concluído. ItemsRestored={result.ItemsRestored}, ItemsFailedToRestore={result.ItemsFailedToRestore}, CorrelationId={_correlationId}");
                progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = "Rollback concluído." });
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GpuOptimizer] Rollback error", ex);
                result.Success = false;
                result.Message = ex.Message;
            }

            return result;
        }
    }
}
