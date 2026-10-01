using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services.SmartRepair.Architecture;

namespace VoltrisOptimizer.Services.SmartRepair.Modules
{
    public class PrioritySchedulerModule : ISmartRepairModule
    {
        private readonly ILoggingService _logger;
        private readonly ISmartRepairEventBus _eventBus;
        private readonly string _correlationId;
        private int? _originalValue;

        public string ModuleId => "PriorityScheduler";
        public string Category => "CPU";

        public string Name => Services.LocalizationService.Instance.GetString("SmartRepair_Module_Priority_Name") ?? "Priority Scheduler";
        public string Description => Services.LocalizationService.Instance.GetString("SmartRepair_Module_Priority_Desc") ?? "Ajusta o Win32PrioritySeparation para melhor desempenho em jogos e aplicativos.";

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

        public PrioritySchedulerModule(ILoggingService logger, ISmartRepairEventBus eventBus, string correlationId)
        {
            _logger = logger;
            _eventBus = eventBus;
            _correlationId = correlationId;
        }

        public Task<ModuleScanResult> ScanAsync(IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleScanResult();
            _eventBus.PublishMessage(_correlationId, ModuleId,
                Services.LocalizationService.Instance.GetString("SmartRepair_Module_Priority_ScanStarting") ?? "Verificando configuração do escalonador de prioridade...",
                SmartRepairEventType.OperationStarted);

            _logger.LogDebug($"[{ModuleId}] Acessando registro em SYSTEM\\CurrentControlSet\\Control\\PriorityControl...");
            using var key = RegistryHelper.OpenKeySafe(RegistryHive.LocalMachine, RegistryView.Registry64,
                @"SYSTEM\CurrentControlSet\Control\PriorityControl", false);
            string? currentValue = RegistryHelper.GetValueString(key, "Win32PrioritySeparation");

            result.FoundItems.Add(currentValue ?? "0");
            result.Statistics.ItemsFound = 1;
            result.Statistics.ItemsScanned = 1;

            _logger.LogInfo($"[{ModuleId}] Scan concluído. Encontrados={result.Statistics.ItemsFound}, Escaneados={result.Statistics.ItemsScanned}, CorrelationId={_correlationId}");
            progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = result.Message ?? "Scan concluído." });
            _eventBus.PublishMessage(_correlationId, ModuleId,
                string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Priority_ScanResult") ?? "Valor atual Win32PrioritySeparation: {0}", currentValue ?? "não definido"),
                SmartRepairEventType.OperationFinished);
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
                Services.LocalizationService.Instance.GetString("SmartRepair_Module_Priority_ExecStarting") ?? "Aplicando configuração do escalonador de prioridade...",
                SmartRepairEventType.OperationStarted);

            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                if (SystemInformationHelper.IsEnterpriseEdition())
                {
                    string msg = Services.LocalizationService.Instance.GetString("SmartRepair_Module_Priority_SkippedEnterprise") ?? "Edição Enterprise detectada. Operação ignorada.";
                    _eventBus.PublishMessage(_correlationId, ModuleId, msg, SmartRepairEventType.ProgressChanged);
                    progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = msg });
                    result.Message = msg;
                    return;
                }

                _logger.LogDebug($"[{ModuleId}] Acessando registro em SYSTEM\\CurrentControlSet\\Control\\PriorityControl...");
                progress?.Report(new RepairProgress { StepPercent = 30, StatusMessage = "Lendo valor original do registro..." });
                using var readKey = RegistryHelper.OpenKeySafe(RegistryHive.LocalMachine, RegistryView.Registry64,
                    @"SYSTEM\CurrentControlSet\Control\PriorityControl", false);
                string? originalStr = RegistryHelper.GetValueString(readKey, "Win32PrioritySeparation");
                if (int.TryParse(originalStr, out int orig))
                    _originalValue = orig;

                progress?.Report(new RepairProgress { StepPercent = 60, StatusMessage =
                    Services.LocalizationService.Instance.GetString("SmartRepair_Module_Priority_ExecProgress") ?? "Aplicando novo valor (0x26)..." });
                using var writeKey = RegistryHelper.OpenKeySafe(RegistryHive.LocalMachine, RegistryView.Registry64,
                    @"SYSTEM\CurrentControlSet\Control\PriorityControl", true);
                RegistryHelper.SetValueSafe(writeKey, "Win32PrioritySeparation", 38, RegistryValueKind.DWord);

                result.FinalStatistics.ItemsRepaired = 1;
                progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage =
                    Services.LocalizationService.Instance.GetString("SmartRepair_Module_Priority_ExecDone") ?? "Win32PrioritySeparation ajustado para 38 (0x26)." });
                _eventBus.PublishMessage(_correlationId, ModuleId,
                    Services.LocalizationService.Instance.GetString("SmartRepair_Module_Priority_ExecDone") ?? "Win32PrioritySeparation ajustado para 38 (0x26).",
                    SmartRepairEventType.OperationFinished);
            }, ct);

            _logger.LogInfo($"[{ModuleId}] Execução concluída. Repaired={result.FinalStatistics.ItemsRepaired}, Errors={result.FinalStatistics.ErrorCount}, Space={result.FinalStatistics.SpaceRecoveredBytes}B, CorrelationId={_correlationId}");
            return result;
        }

        public async Task<ModuleRollbackResult> RollbackAsync(ModuleExecutionResult execResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleRollbackResult();
            _eventBus.PublishMessage(_correlationId, ModuleId,
                Services.LocalizationService.Instance.GetString("SmartRepair_Module_Priority_RollbackStarting") ?? "Restaurando valor original do Win32PrioritySeparation...",
                SmartRepairEventType.RollbackStarted);

            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(new RepairProgress { StepPercent = 50, StatusMessage = "Restaurando valor original..." });

                using var key = RegistryHelper.OpenKeySafe(RegistryHive.LocalMachine, RegistryView.Registry64,
                    @"SYSTEM\CurrentControlSet\Control\PriorityControl", true);
                if (_originalValue.HasValue)
                {
                    RegistryHelper.SetValueSafe(key, "Win32PrioritySeparation", _originalValue.Value, RegistryValueKind.DWord);
                    result.ItemsRestored = 1;
                }
                else
                {
                    result.ItemsFailedToRestore = 1;
                }

                progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage =
                    Services.LocalizationService.Instance.GetString("SmartRepair_Module_Priority_RollbackDone") ?? "Valor original restaurado." });
                _eventBus.PublishMessage(_correlationId, ModuleId,
                    Services.LocalizationService.Instance.GetString("SmartRepair_Module_Priority_RollbackDone") ?? "Valor original do Win32PrioritySeparation restaurado.",
                    SmartRepairEventType.RollbackFinished);
            }, ct);

            _logger.LogInfo($"[{ModuleId}] Rollback concluído. Restored={result.ItemsRestored}, Failed={result.ItemsFailedToRestore}, CorrelationId={_correlationId}");
            progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = "Rollback concluído." });
            return result;
        }
    }
}
