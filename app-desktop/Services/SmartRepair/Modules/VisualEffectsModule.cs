using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services.SmartRepair.Architecture;

namespace VoltrisOptimizer.Services.SmartRepair.Modules
{
    public class VisualEffectsModule : ISmartRepairModule
    {
        private readonly ILoggingService _logger;
        private readonly ISmartRepairEventBus _eventBus;
        private readonly string _correlationId;

        private const uint SPI_SETANIMATION = 0x004B;
        private const uint SPI_SETMENUANIMATION = 0x1003;
        private const uint SPI_SETCOMBOBOXANIMATION = 0x1005;
        private const uint SPI_SETLISTBOXSMOOTHSCROLLING = 0x1007;
        private const uint SPI_SETGRADIENTCAPTIONS = 0x1009;
        private const uint SPI_SETUIEFFECTS = 0x103F;
        private const uint SPIF_UPDATEINIFILE = 0x01;
        private const uint SPIF_SENDCHANGE = 0x02;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref int pvParam, uint fWinIni);

        private readonly Dictionary<string, object?> _savedValues = new();

        public string ModuleId => "VisualEffects";
        public string Category => "System";

        public string Name => Services.LocalizationService.Instance.GetString("SmartRepair_Module_VisualFx_Name") ?? "Efeitos Visuais";
        public string Description => Services.LocalizationService.Instance.GetString("SmartRepair_Module_VisualFx_Desc") ?? "Ajusta efeitos visuais do Windows conforme perfil de hardware (RAM, GPU, bateria).";

        public ModuleCapabilities Capabilities { get; } = new ModuleCapabilities
        {
            SupportsScan = true, SupportsSimulation = true, SupportsExecution = true, SupportsRollback = true,
            SupportsParallelism = false, HasDestructiveOperations = false, MaxRiskLevel = RiskLevel.Safe,
            RequiresAdmin = true
        };

        public IReadOnlyList<string> Dependencies => Array.Empty<string>();

        public VisualEffectsModule(ILoggingService logger, ISmartRepairEventBus eventBus, string correlationId)
        {
            _logger = logger; _eventBus = eventBus; _correlationId = correlationId;
        }

        public async Task<ModuleScanResult> ScanAsync(IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleScanResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_VisualFx_ScanStarting") ?? "Analisando perfil de hardware para efeitos visuais...", SmartRepairEventType.OperationStarted);

            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                var profile = DetectHardwareProfile();
                result.FoundItems.Add(profile);
                result.Statistics.ItemsFound = 1;

                string scanResult = Services.LocalizationService.Instance.GetString("SmartRepair_Module_VisualFx_ScanResult") ?? string.Format("Perfil detectado: {0} RAM, GPU {1}.", profile.TotalRamGB, profile.HasIntegratedGpu ? "integrada" : "dedicada");
_eventBus.PublishMessage(_correlationId, ModuleId, scanResult, SmartRepairEventType.ProgressChanged);
                }, ct);
                // Log and report completion of scan
                _logger.LogInfo($"[{ModuleId}] Scan concluído. Encontrados={result.Statistics.ItemsFound}, Escaneados={result.Statistics.ItemsScanned}, CorrelationId={_correlationId}");
                progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_VisualFx_ScanComplete") ?? "Análise de efeitos visuais concluída." });
                _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_VisualFx_ScanComplete") ?? "Análise de efeitos visuais concluída.", SmartRepairEventType.OperationFinished);


            return result;
        }

        public Task<ModuleSimulationResult> SimulateAsync(ModuleScanResult scanResult, CancellationToken ct)
        {
            var sim = new ModuleSimulationResult();
            sim.RiskLevel = RiskLevel.Safe;
            sim.EstimatedStatistics.ItemsFound = scanResult.FoundItems.Count;
            foreach (var item in scanResult.FoundItems)
                sim.ItemsToProcess.Add(item);
            // Log simulation completion
            _logger.LogInfo($"[{ModuleId}] Simulação concluída. Itens a processar={sim.ItemsToProcess.Count}, RiskLevel={sim.RiskLevel}, CorrelationId={_correlationId}");
            return Task.FromResult(sim);
        }

        public async Task<ModuleExecutionResult> ExecuteAsync(ModuleSimulationResult simResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleExecutionResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_VisualFx_ExecStarting") ?? "Aplicando ajustes de efeitos visuais...", SmartRepairEventType.OperationStarted);

            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                var profile = simResult.ItemsToProcess.Count > 0 ? simResult.ItemsToProcess[0] as HardwareProfile : null;
                if (profile == null)
                {
                    result.Success = false;
                    result.Message = LocalizationService.Instance.GetString("ScanNoData");
                    return;
                }

                bool disableAnimations;
                int visualFxSetting;
                int menuShowDelay;

                if (profile.TotalRamGB >= 16 && !profile.HasIntegratedGpu && !SystemInformationHelper.IsOnBattery())
                {
                    string msg = Services.LocalizationService.Instance.GetString("SmartRepair_Module_VisualFx_SkippedHighEnd") ?? "Sistema de alto desempenho. Efeitos visuais mantidos.";
                    _eventBus.PublishMessage(_correlationId, ModuleId, msg, SmartRepairEventType.ProgressChanged);
                    progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = msg });
                    result.Message = msg;
                    return;
                }

                if (profile.TotalRamGB <= 4 || profile.HasIntegratedGpu)
                {
                    visualFxSetting = 2;
                    menuShowDelay = 0;
                    disableAnimations = true;
                }
                else if (profile.TotalRamGB <= 8)
                {
                    visualFxSetting = 2;
                    menuShowDelay = 400;
                    disableAnimations = true;
                }
                else
                {
                    visualFxSetting = 3;
                    menuShowDelay = 400;
                    disableAnimations = false;
                }

                if (SystemInformationHelper.IsOnBattery())
                {
                    disableAnimations = true;
                    if (visualFxSetting == 3)
                        visualFxSetting = 2;
                }

                progress?.Report(new RepairProgress { StepPercent = 25, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_VisualFx_ExecProgress") ?? "Ajustando configurações de efeitos visuais..." });
                _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_VisualFx_ExecProgress") ?? "Ajustando configurações de efeitos visuais...", SmartRepairEventType.ProgressChanged);

                SaveCurrentValues();

                using var fxKey = RegistryHelper.CreateKeyPath(RegistryHive.CurrentUser, RegistryView.Registry64, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects");
                if (fxKey != null)
                {
                    RegistryHelper.SetValueSafe(fxKey, "VisualFXSetting", visualFxSetting, RegistryValueKind.DWord);
                }

                using var desktopKey = RegistryHelper.CreateKeyPath(RegistryHive.CurrentUser, RegistryView.Registry64, @"Control Panel\Desktop");
                if (desktopKey != null)
                {
                    RegistryHelper.SetValueSafe(desktopKey, "MenuShowDelay", menuShowDelay, RegistryValueKind.String);
                }

                using var advancedKey = RegistryHelper.CreateKeyPath(RegistryHive.CurrentUser, RegistryView.Registry64, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
                if (advancedKey != null)
                {
                    RegistryHelper.SetValueSafe(advancedKey, "TaskbarAnimations", disableAnimations ? 0 : 1, RegistryValueKind.DWord);
                }

                int animFlag = disableAnimations ? 0 : 1;
                SystemParametersInfo(SPI_SETANIMATION, 0, ref animFlag, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                SystemParametersInfo(SPI_SETMENUANIMATION, 0, ref animFlag, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                SystemParametersInfo(SPI_SETCOMBOBOXANIMATION, 0, ref animFlag, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                SystemParametersInfo(SPI_SETLISTBOXSMOOTHSCROLLING, 0, ref animFlag, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);

                result.FinalStatistics.ItemsRepaired = 1;
                progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_VisualFx_ExecDone") ?? "Efeitos visuais ajustados." });
                _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_VisualFx_ExecDone") ?? "Efeitos visuais ajustados com sucesso.", SmartRepairEventType.OperationFinished);
            }, ct);

            return result;
        }

        public async Task<ModuleRollbackResult> RollbackAsync(ModuleExecutionResult execResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleRollbackResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_VisualFx_RollbackStarting") ?? "Restaurando efeitos visuais originais...", SmartRepairEventType.RollbackStarted);

            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(new RepairProgress { StepPercent = 50, StatusMessage = "Restaurando valores salvos..." });

                RestoreSavedValues();

                result.ItemsRestored = 1;
                progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_VisualFx_RollbackDone") ?? "Efeitos visuais restaurados." });
                _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_VisualFx_RollbackDone") ?? "Efeitos visuais restaurados.", SmartRepairEventType.RollbackFinished);
            }, ct);
            // Log rollback completion
            _logger.LogInfo($"[{ModuleId}] Rollback concluído. Itens restaurados={result.ItemsRestored}, CorrelationId={_correlationId}");

            return result;
        }

        private void SaveCurrentValues()
        {
            _savedValues.Clear();
            _savedValues["VisualFXSetting"] = ReadRegistryValue(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting");
            _savedValues["MenuShowDelay"] = ReadRegistryValue(RegistryHive.CurrentUser, @"Control Panel\Desktop", "MenuShowDelay");
            _savedValues["TaskbarAnimations"] = ReadRegistryValue(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAnimations");
        }

        private void RestoreSavedValues()
        {
            RestoreRegistryValue(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", _savedValues.GetValueOrDefault("VisualFXSetting"));
            RestoreRegistryValue(RegistryHive.CurrentUser, @"Control Panel\Desktop", "MenuShowDelay", _savedValues.GetValueOrDefault("MenuShowDelay"));
            RestoreRegistryValue(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAnimations", _savedValues.GetValueOrDefault("TaskbarAnimations"));
        }

        private static object? ReadRegistryValue(RegistryHive hive, string path, string name)
        {
            using var key = RegistryHelper.OpenKeySafe(hive, RegistryView.Registry64, path, false);
            return key?.GetValue(name);
        }

        private static void RestoreRegistryValue(RegistryHive hive, string path, string name, object? value)
        {
            if (value == null) return;
            using var key = RegistryHelper.OpenKeySafe(hive, RegistryView.Registry64, path, true);
            if (key != null)
            {
                try { key.SetValue(name, value); } catch { System.Diagnostics.Debug.WriteLine($"[VisualEffects] Erro ao restaurar valor registry {path}\\{name}"); }
            }
        }

        private static HardwareProfile DetectHardwareProfile()
        {
            var profile = new HardwareProfile();

            try
            {
                using var memSearcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem");
                foreach (ManagementObject obj in memSearcher.Get())
                {
                using var __dispose_obj = obj;
                    ulong totalKb = Convert.ToUInt64(obj["TotalVisibleMemorySize"] ?? 0);
                    profile.TotalRamGB = (int)(totalKb / (1024 * 1024));
                    break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VisualEffects] Erro ao obter RAM via WMI: {ex.Message}");
            }

            try
            {
                using var gpuSearcher = new ManagementObjectSearcher("SELECT AdapterRAM, VideoProcessor FROM Win32_VideoController");
                foreach (ManagementObject gpu in gpuSearcher.Get())
                {
                using var __dispose_gpu = gpu;
                    string? proc = gpu["VideoProcessor"]?.ToString();
                    if (proc != null)
                    {
                        bool isIntel = proc.Contains("Intel", StringComparison.OrdinalIgnoreCase);
                        string? name = gpu["Name"]?.ToString() ?? string.Empty;
                        bool isBasic = name.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase);

                        if (isIntel || isBasic)
                            profile.HasIntegratedGpu = true;
                    }

                    object? ramObj = gpu["AdapterRAM"];
                    if (ramObj != null)
                    {
                        ulong ramBytes = Convert.ToUInt64(ramObj);
                        profile.GpuRamMB = (int)(ramBytes / (1024 * 1024));
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VisualEffects] Erro ao obter GPU via WMI: {ex.Message}");
            }

            return profile;
        }

        private class HardwareProfile
        {
            public int TotalRamGB { get; set; }
            public int GpuRamMB { get; set; }
            public bool HasIntegratedGpu { get; set; }
        }
    }
}
