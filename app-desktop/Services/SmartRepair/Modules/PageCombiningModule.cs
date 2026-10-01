using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services.SmartRepair.Architecture;

namespace VoltrisOptimizer.Services.SmartRepair.Modules
{
    public class PageCombiningModule : ISmartRepairModule
    {
        private readonly ILoggingService _logger;
        private readonly ISmartRepairEventBus _eventBus;
        private readonly string _correlationId;

        public string ModuleId => "PageCombining";
        public string Category => "Memory";

        public string Name => Services.LocalizationService.Instance.GetString("SmartRepair_Module_PageCombine_Name") ?? "Page Combining";
        public string Description => Services.LocalizationService.Instance.GetString("SmartRepair_Module_PageCombine_Desc") ?? "Gerencia o Page Combining do sistema para otimizar uso de memória.";

        public ModuleCapabilities Capabilities { get; } = new ModuleCapabilities
        {
            SupportsScan = true, SupportsSimulation = true, SupportsExecution = true, SupportsRollback = true,
            SupportsParallelism = false, HasDestructiveOperations = false, MaxRiskLevel = RiskLevel.Safe,
            RequiresAdmin = true
        };

        public IReadOnlyList<string> Dependencies => Array.Empty<string>();

        private const string RegistryPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";
        private const string ValueName = "EnablePageCombining";

        private class PageCombineInfo
        {
            public int? CurrentValue { get; set; }
            public ulong TotalRamMb { get; set; }
            public int CpuCoreCount { get; set; }
            public int RecommendedValue { get; set; }
        }

        public PageCombiningModule(ILoggingService logger, ISmartRepairEventBus eventBus, string correlationId)
        {
            _logger = logger; _eventBus = eventBus; _correlationId = correlationId;
        }

        public async Task<ModuleScanResult> ScanAsync(IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleScanResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_PageCombine_ScanStarting") ?? "Verificando configuração de Page Combining...", SmartRepairEventType.OperationStarted);

            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                _logger.LogDebug($"[{ModuleId}] Acessando registro em {RegistryPath}...");
                int? currentValue = null;
                using var key = RegistryHelper.OpenKeySafe(RegistryHive.LocalMachine, RegistryView.Registry64, RegistryPath, false);
                if (key != null)
                {
                    var val = key.GetValue(ValueName);
                    if (val != null)
                        currentValue = Convert.ToInt32(val);
                }

                ulong totalRamMb = 0;
                try
                {
                    _logger.LogDebug($"[{ModuleId}] Executando consulta WMI...");
                    using var memSearcher = new System.Management.ManagementObjectSearcher("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem");
                    foreach (System.Management.ManagementObject obj in memSearcher.Get())
                    {
                        totalRamMb = Convert.ToUInt64(obj["TotalVisibleMemorySize"] ?? 0) / 1024;
                        break;
                    }
                }
                catch (Exception exWmi) { _logger?.LogWarning($"[PageCombining] Erro ao obter RAM total via WMI: {exWmi.Message}"); }

                int cores = Environment.ProcessorCount;

                int recommended;
                if (totalRamMb <= 8192)
                    recommended = 1;
                else if (totalRamMb < 16384 && cores >= 4)
                    recommended = 1;
                else if (totalRamMb >= 16384)
                    recommended = 0;
                else if (cores < 4 && totalRamMb < 8192)
                    recommended = 1;
                else
                    recommended = 0;

                result.FoundItems.Add(new PageCombineInfo
                {
                    CurrentValue = currentValue,
                    TotalRamMb = totalRamMb,
                    CpuCoreCount = cores,
                    RecommendedValue = recommended
                });
            }, ct);

            result.Statistics.ItemsFound = result.FoundItems.Count;
            result.Statistics.ItemsScanned = result.FoundItems.Count;

            _logger.LogInfo($"[{ModuleId}] Scan concluído. Encontrados={result.Statistics.ItemsFound}, Escaneados={result.Statistics.ItemsScanned}, CorrelationId={_correlationId}");
            progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = result.Message ?? "Scan concluído." });
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_PageCombine_ScanResult") ?? "Configuração de Page Combining verificada.", SmartRepairEventType.OperationFinished);
            return result;
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
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_PageCombine_ExecStarting") ?? "Aplicando configuração de Page Combining...", SmartRepairEventType.OperationStarted);

            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                var info = simResult.ItemsToProcess.Count > 0 ? simResult.ItemsToProcess[0] as PageCombineInfo : null;
                if (info == null)
                {
                    result.Success = false;
                    result.Message = LocalizationService.Instance.GetString("ScanNoData");
                    return;
                }

                int originalValue = info.CurrentValue ?? 0;
                result.ProcessedItems.Add(originalValue);

                using var key = RegistryHelper.CreateKeyPath(RegistryHive.LocalMachine, RegistryView.Registry64, RegistryPath);
                if (key == null)
                {
                    result.Success = false;
                    result.Message = LocalizationService.Instance.GetString("RegistryOpenFailed");
                    return;
                }

                int targetValue = info.RecommendedValue;

                if (targetValue == 1)
                    _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_PageCombine_ExecEnabling") ?? "Ativando Page Combining...", SmartRepairEventType.ProgressChanged);
                else
                    _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_PageCombine_ExecDisabling") ?? "Desativando Page Combining...", SmartRepairEventType.ProgressChanged);

                progress?.Report(new RepairProgress { StepPercent = 50, StatusMessage = $"Aplicando valor {targetValue}..." });

                if (RegistryHelper.SetValueSafe(key, ValueName, targetValue, RegistryValueKind.DWord))
                {
                    result.FinalStatistics.ItemsRepaired = 1;
                    progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = "Page Combining configurado." });
                    _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_PageCombine_ExecDone") ?? "Page Combining configurado com sucesso.", SmartRepairEventType.OperationFinished);
                }
                else
                {
                    result.Success = false;
                    result.Message = LocalizationService.Instance.GetString("RegistryWriteFailed");
                }
            }, ct);

            _logger.LogInfo($"[{ModuleId}] Execução concluída. Repaired={result.FinalStatistics.ItemsRepaired}, Errors={result.FinalStatistics.ErrorCount}, Space={result.FinalStatistics.SpaceRecoveredBytes}B, CorrelationId={_correlationId}");
            return result;
        }

        public async Task<ModuleRollbackResult> RollbackAsync(ModuleExecutionResult execResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleRollbackResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_PageCombine_RollbackStarting") ?? "Restaurando valor original de Page Combining...", SmartRepairEventType.RollbackStarted);

            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(new RepairProgress { StepPercent = 50, StatusMessage = "Restaurando..." });

                try
                {
                    using var key = RegistryHelper.OpenKeySafe(RegistryHive.LocalMachine, RegistryView.Registry64, RegistryPath, true);

                    if (execResult.ProcessedItems.Count > 0 && execResult.ProcessedItems[0] is int originalValue)
                    {
                        if (RegistryHelper.SetValueSafe(key, ValueName, originalValue, RegistryValueKind.DWord))
                        {
                            result.ItemsRestored = 1;
                        }
                        else
                        {
                            result.ItemsFailedToRestore = 1;
                        }
                    }
                    else
                    {
                        if (key != null && key.GetValue(ValueName) != null)
                        {
                            key.DeleteValue(ValueName, false);
                            result.ItemsRestored = 1;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[{ModuleId}] Erro no rollback", ex);
                    result.ItemsFailedToRestore = 1;
                    result.Success = false;
                    result.Message = ex.Message;
                }

                progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = "Page Combining restaurado." });
                _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_PageCombine_RollbackDone") ?? "Valor original de Page Combining restaurado.", SmartRepairEventType.RollbackFinished);
            }, ct);

            _logger.LogInfo($"[{ModuleId}] Rollback concluído. Restored={result.ItemsRestored}, Failed={result.ItemsFailedToRestore}, CorrelationId={_correlationId}");
            progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = "Rollback concluído." });
            return result;
        }
    }
}
