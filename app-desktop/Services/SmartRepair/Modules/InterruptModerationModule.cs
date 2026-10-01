using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services.SmartRepair.Architecture;

namespace VoltrisOptimizer.Services.SmartRepair.Modules
{
    public class InterruptModerationModule : ISmartRepairModule
    {
        private readonly ILoggingService _logger;
        private readonly ISmartRepairEventBus _eventBus;
        private readonly string _correlationId;
        private readonly Dictionary<string, int> _originalValues = new();
        private const string NetClassGuid = @"{4d36e972-e325-11ce-bfc1-08002be10318}";

        public string ModuleId => "InterruptModeration";
        public string Category => "Network";

        public string Name => Services.LocalizationService.Instance.GetString("SmartRepair_Module_Interrupt_Name") ?? "Interrupt Moderation";
        public string Description => Services.LocalizationService.Instance.GetString("SmartRepair_Module_Interrupt_Desc") ?? "Ajusta o Interrupt Moderation de adaptadores Ethernet para reduzir latência de rede.";

        public ModuleCapabilities Capabilities { get; } = new ModuleCapabilities
        {
            SupportsScan = true,
            SupportsSimulation = true,
            SupportsExecution = true,
            SupportsRollback = true,
            SupportsParallelism = false,
            HasDestructiveOperations = false,
            MaxRiskLevel = RiskLevel.Safe,
            RequiresAdmin = true
        };

        public IReadOnlyList<string> Dependencies => Array.Empty<string>();

        public InterruptModerationModule(ILoggingService logger, ISmartRepairEventBus eventBus, string correlationId)
        {
            _logger = logger;
            _eventBus = eventBus;
            _correlationId = correlationId;
        }

        public Task<ModuleScanResult> ScanAsync(IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleScanResult();
            _eventBus.PublishMessage(_correlationId, ModuleId,
                Services.LocalizationService.Instance.GetString("SmartRepair_Module_Interrupt_ScanStarting") ?? "Verificando adaptadores de rede e configurações de Interrupt Moderation...",
                SmartRepairEventType.OperationStarted);

            var adapters = EnumerateEthernetAdapters();
            foreach (var adp in adapters)
            {
                result.FoundItems.Add(adp);
                result.Statistics.ItemsFound++;
                result.Statistics.ItemsScanned++;
            }

            _eventBus.PublishMessage(_correlationId, ModuleId,
                string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Interrupt_ScanResult") ?? "{0} adaptador(es) Ethernet encontrado(s).", adapters.Count),
                SmartRepairEventType.OperationFinished);
            _logger.LogInfo($"[{ModuleId}] Scan concluído. Encontrados={result.Statistics.ItemsFound}, Escaneados={result.Statistics.ItemsScanned}, CorrelationId={_correlationId}");
            progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = result.Message ?? "Scan concluído." });
            return Task.FromResult(result);
        }

        public Task<ModuleSimulationResult> SimulateAsync(ModuleScanResult scanResult, CancellationToken ct)
        {
            var sim = new ModuleSimulationResult();
            sim.RiskLevel = RiskLevel.Safe;
            sim.EstimatedStatistics.ItemsFound = scanResult.FoundItems.Count;
            foreach (var item in scanResult.FoundItems)
                sim.ItemsToProcess.Add(item);
            _logger.LogInfo($"[{ModuleId}] Simulação concluída. Itens a processar={sim.ItemsToProcess.Count}, RiskLevel={sim.RiskLevel}, CorrelationId={_correlationId}");
            return Task.FromResult(sim);
        }

        public async Task<ModuleExecutionResult> ExecuteAsync(ModuleSimulationResult simResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleExecutionResult();
            _eventBus.PublishMessage(_correlationId, ModuleId,
                Services.LocalizationService.Instance.GetString("SmartRepair_Module_Interrupt_ExecStarting") ?? "Aplicando configurações de Interrupt Moderation...",
                SmartRepairEventType.OperationStarted);

            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                if (SystemInformationHelper.IsEnterpriseEdition())
                {
                    string msg = Services.LocalizationService.Instance.GetString("SmartRepair_Module_Interrupt_SkippedEnterprise") ?? "Edição Enterprise. Operação ignorada.";
                    _eventBus.PublishMessage(_correlationId, ModuleId, msg, SmartRepairEventType.ProgressChanged);
                    progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = msg });
                    result.Message = msg;
                    return;
                }

                if (SystemInformationHelper.IsOnBattery())
                {
                    string msg = Services.LocalizationService.Instance.GetString("SmartRepair_Module_Interrupt_SkippedBattery") ?? "Notebook em bateria. Operação ignorada.";
                    _eventBus.PublishMessage(_correlationId, ModuleId, msg, SmartRepairEventType.ProgressChanged);
                    progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = msg });
                    result.Message = msg;
                    return;
                }

                var adapters = simResult.ItemsToProcess.Cast<NetworkAdapterInfo>().ToList();
                if (adapters.Count == 0)
                {
                    progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = "Nenhum adaptador Ethernet disponível." });
                    return;
                }

                int total = adapters.Count;
                int current = 0;

                _logger.LogDebug($"[{ModuleId}] Processando {total} adaptador(es)...");
                foreach (var adp in adapters)
                {
                    ct.ThrowIfCancellationRequested();
                    current++;

                    if (IsWifiAdapter(adp.Name))
                    {
                        string msg = Services.LocalizationService.Instance.GetString("SmartRepair_Module_Interrupt_SkippedWifi") ?? string.Format("WiFi ignorado: {0}", adp.Name);
                        _eventBus.PublishMessage(_correlationId, ModuleId, msg, SmartRepairEventType.ProgressChanged);
                        result.FinalStatistics.ItemsIgnored++;
                        continue;
                    }

                    int percent = (int)((current / (double)total) * 100);
                    string statusText = string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Interrupt_ExecProgress") ?? "Configurando {0} ({1}/{2})...", adp.Name, current, total);
                    _eventBus.PublishMessage(_correlationId, ModuleId, statusText, SmartRepairEventType.ProgressChanged);
                    progress?.Report(new RepairProgress { StepPercent = percent, StatusMessage = statusText });

                    _logger.LogDebug($"[{ModuleId}] Acessando registro do adaptador {adp.Name}...");
                    using var key = RegistryHelper.OpenKeySafe(RegistryHive.LocalMachine, RegistryView.Registry64,
                        $@"SYSTEM\CurrentControlSet\Control\Class\{NetClassGuid}\{adp.SubKey}", false);

                    bool hasInterruptModeration = false;
                    foreach (string valName in new[] { "*InterruptModeration", "InterruptModeration", "*Rss" })
                    {
                        string? currentVal = RegistryHelper.GetValueString(key, valName);
                        if (currentVal != null && int.TryParse(currentVal, out int val))
                        {
                            _originalValues[$"{adp.SubKey}\\{valName}"] = val;
                            hasInterruptModeration = true;
                        }
                    }

                    if (!hasInterruptModeration)
                    {
                        result.FinalStatistics.ItemsIgnored++;
                        continue;
                    }

                    using var writeKey = RegistryHelper.OpenKeySafe(RegistryHive.LocalMachine, RegistryView.Registry64,
                        $@"SYSTEM\CurrentControlSet\Control\Class\{NetClassGuid}\{adp.SubKey}", true);

                    RegistryHelper.SetValueSafe(writeKey, "*InterruptModeration", 0, RegistryValueKind.DWord);
                    result.FinalStatistics.ItemsRepaired++;
                }

                progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage =
                    Services.LocalizationService.Instance.GetString("SmartRepair_Module_Interrupt_ExecDone") ?? "Interrupt Moderation configurado." });
                _eventBus.PublishMessage(_correlationId, ModuleId,
                    Services.LocalizationService.Instance.GetString("SmartRepair_Module_Interrupt_ExecDone") ?? "Interrupt Moderation ajustado para baixa latência.",
                    SmartRepairEventType.OperationFinished);
            }, ct);

            _logger.LogInfo($"[{ModuleId}] Execução concluída. Repaired={result.FinalStatistics.ItemsRepaired}, Errors={result.FinalStatistics.ErrorCount}, Space={result.FinalStatistics.SpaceRecoveredBytes}B, CorrelationId={_correlationId}");
            return result;
        }

        public async Task<ModuleRollbackResult> RollbackAsync(ModuleExecutionResult execResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleRollbackResult();
            _eventBus.PublishMessage(_correlationId, ModuleId,
                Services.LocalizationService.Instance.GetString("SmartRepair_Module_Interrupt_RollbackStarting") ?? "Restaurando configurações originais de Interrupt Moderation...",
                SmartRepairEventType.RollbackStarted);

            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(new RepairProgress { StepPercent = 50, StatusMessage = "Restaurando valores originais..." });

                int restored = 0;
                int failed = 0;

                foreach (var kvp in _originalValues)
                {
                    string[] parts = kvp.Key.Split('\\');
                    if (parts.Length < 2) { failed++; continue; }

                    string subKey = parts[0];
                    string valName = parts[1];

                    using var key = RegistryHelper.OpenKeySafe(RegistryHive.LocalMachine, RegistryView.Registry64,
                        $@"SYSTEM\CurrentControlSet\Control\Class\{NetClassGuid}\{subKey}", true);

                    if (RegistryHelper.SetValueSafe(key, valName, kvp.Value, RegistryValueKind.DWord))
                        restored++;
                    else
                        failed++;
                }

                result.ItemsRestored = restored;
                result.ItemsFailedToRestore = failed;
                result.Success = failed == 0;

                progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage =
                    Services.LocalizationService.Instance.GetString("SmartRepair_Module_Interrupt_RollbackDone") ?? "Valores originais restaurados." });
                _eventBus.PublishMessage(_correlationId, ModuleId,
                    Services.LocalizationService.Instance.GetString("SmartRepair_Module_Interrupt_RollbackDone") ?? "Interrupt Moderation restaurado.",
                    SmartRepairEventType.RollbackFinished);
            }, ct);

            _logger.LogInfo($"[{ModuleId}] Rollback concluído. Restored={result.ItemsRestored}, Failed={result.ItemsFailedToRestore}, CorrelationId={_correlationId}");
            progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = "Rollback concluído." });
            return result;
        }

        private static List<NetworkAdapterInfo> EnumerateEthernetAdapters()
        {
            var list = new List<NetworkAdapterInfo>();
            using var baseKey = RegistryHelper.OpenKeySafe(RegistryHive.LocalMachine, RegistryView.Registry64,
                $@"SYSTEM\CurrentControlSet\Control\Class\{NetClassGuid}", false);
            if (baseKey == null) return list;

            string[] subKeys;
            try
            {
                subKeys = baseKey.GetSubKeyNames();
            }
            catch
            {
                return list;
            }

            foreach (string subKeyName in subKeys)
            {
                try
                {
                    using var subKey = baseKey.OpenSubKey(subKeyName);
                    if (subKey == null) continue;

                    string? desc = subKey.GetValue("DriverDesc")?.ToString();
                    string? netCfgId = subKey.GetValue("NetCfgInstanceId")?.ToString();
                    string? connectionName = subKey.GetValue("*IfType")?.ToString();
                    string? busType = subKey.GetValue("BusType")?.ToString();

                    if (string.IsNullOrEmpty(desc)) continue;

                    int ifType = 0;
                    if (!string.IsNullOrEmpty(connectionName))
                    {
                        int.TryParse(connectionName, out ifType);
                    }

                    bool isPhysical = ifType == 6 || string.IsNullOrEmpty(connectionName);
                    bool isWifi = IsWifiAdapter(desc);

                    if (isPhysical && !isWifi)
                    {
                        list.Add(new NetworkAdapterInfo
                        {
                            Name = desc,
                            SubKey = subKeyName,
                            NetCfgInstanceId = netCfgId ?? ""
                        });
                    }
                }
                catch
                {
                    // Ignore inaccessible or corrupted keys
                }
            }

            return list;
        }

        private static bool IsWifiAdapter(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string lower = name.ToLowerInvariant();
            return lower.Contains("wireless") ||
                   lower.Contains("wi-fi") ||
                   lower.Contains("802.11") ||
                   lower.Contains("wlan") ||
                   lower.Contains("wi fi");
        }

        private class NetworkAdapterInfo
        {
            public string Name { get; set; } = string.Empty;
            public string SubKey { get; set; } = string.Empty;
            public string NetCfgInstanceId { get; set; } = string.Empty;
        }
    }
}
