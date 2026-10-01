using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services.SmartRepair.Architecture;

namespace VoltrisOptimizer.Services.SmartRepair.Modules
{
    public class QoSBandwidthModule : ISmartRepairModule
    {
        private const string RegistryPath = @"SOFTWARE\Policies\Microsoft\Windows\Psched";
        private const string ValueName = "NonBestEffortLimit";

        private readonly ILoggingService _logger;
        private readonly ISmartRepairEventBus _eventBus;
        private readonly string _correlationId;
        private string? _originalValue;

        public string ModuleId => "QoSBandwidth";
        public string Category => "Network";
        public string Name => Services.LocalizationService.Instance.GetString("SmartRepair_Module_QoS_Name") ?? "Remover Limitação de Banda QoS";
        public string Description => Services.LocalizationService.Instance.GetString("SmartRepair_Module_QoS_Desc") ?? "Remove a reserva padrão de 20% de banda do QoS para liberar toda a banda da rede.";

        public ModuleCapabilities Capabilities { get; } = new ModuleCapabilities
        {
            SupportsScan = true, SupportsSimulation = true, SupportsExecution = true, SupportsRollback = true,
            SupportsParallelism = false, HasDestructiveOperations = false, MaxRiskLevel = RiskLevel.Safe,
            RequiresAdmin = true
        };

        public IReadOnlyList<string> Dependencies => Array.Empty<string>();

        public QoSBandwidthModule(ILoggingService logger, ISmartRepairEventBus eventBus, string correlationId)
        {
            _logger = logger; _eventBus = eventBus; _correlationId = correlationId;
        }

        public Task<ModuleScanResult> ScanAsync(IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleScanResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_QoS_ScanStarting") ?? "Verificando configuração de QoS...", SmartRepairEventType.OperationStarted);
            progress?.Report(new RepairProgress { StepPercent = 50, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_QoS_ScanStarting") ?? "Verificando configuração de QoS..." });

            using var key = RegistryHelper.OpenKeySafe(RegistryHive.LocalMachine, RegistryView.Registry64, RegistryPath, false);
            var currentValue = key != null ? RegistryHelper.GetValueString(key, ValueName) : null;

            if (currentValue == "0")
            {
                result.Message = Services.LocalizationService.Instance.GetString("SmartRepair_Module_QoS_ScanAlreadyApplied") ?? "QoS já está configurado para 0% de reserva.";
                _eventBus.PublishMessage(_correlationId, ModuleId, result.Message, SmartRepairEventType.OperationFinished);
                return Task.FromResult(result);
            }

            if (currentValue != null)
            {
                result.Statistics.ItemsIgnored = 1;
                result.Message = Services.LocalizationService.Instance.GetString("SmartRepair_Module_QoS_SkippedEnterprise") ?? "Limitação de banda gerenciada por GPO. Ignorando.";
                _eventBus.PublishMessage(_correlationId, ModuleId, result.Message, SmartRepairEventType.OperationFinished);
                return Task.FromResult(result);
            }

            result.Statistics.ItemsFound = 1;
            result.FoundItems.Add(ValueName);
            result.Message = Services.LocalizationService.Instance.GetString("SmartRepair_Module_QoS_ScanNeedsApply") ?? "Reserva de 20% de banda ativa. Aplicar remoção.";
            _eventBus.PublishMessage(_correlationId, ModuleId, result.Message, SmartRepairEventType.OperationFinished);
            return Task.FromResult(result);
        }

        public Task<ModuleSimulationResult> SimulateAsync(ModuleScanResult scanResult, CancellationToken ct)
        {
            var sim = new ModuleSimulationResult();
            if (scanResult.Statistics.ItemsFound > 0)
            {
                sim.EstimatedStatistics.ItemsFound = 1;
                sim.ItemsToProcess.Add(ValueName);
            }
            sim.RiskLevel = RiskLevel.Safe;
            return Task.FromResult(sim);
        }

        public Task<ModuleExecutionResult> ExecuteAsync(ModuleSimulationResult simResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleExecutionResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_QoS_ExecStarting") ?? "Removendo limitação de banda QoS...", SmartRepairEventType.OperationStarted);
            progress?.Report(new RepairProgress { StepPercent = 30, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_QoS_ExecStarting") ?? "Removendo limitação de banda QoS..." });

            try
            {
                using var key = RegistryHelper.OpenKeySafe(RegistryHive.LocalMachine, RegistryView.Registry64, RegistryPath, true);
                if (key != null)
                {
                    _originalValue = RegistryHelper.GetValueString(key, ValueName);
                }

                var writeKey = RegistryHelper.CreateKeyPath(RegistryHive.LocalMachine, RegistryView.Registry64, RegistryPath);
                if (writeKey != null)
                {
                    RegistryHelper.SetValueSafe(writeKey, ValueName, 0, RegistryValueKind.DWord);
                    writeKey.Dispose();
                }

                progress?.Report(new RepairProgress { StepPercent = 80, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_QoS_ExecDone") ?? "Limitação de banda removida." });

                result.FinalStatistics.ItemsRepaired = 1;
                result.Message = Services.LocalizationService.Instance.GetString("SmartRepair_Module_QoS_ExecDone") ?? "Reserva de 20% de banda removida com sucesso.";
                _eventBus.PublishMessage(_correlationId, ModuleId, result.Message, SmartRepairEventType.OperationFinished);
                _logger.LogInfo($"[QoSBandwidthModule] QoS NonBestEffortLimit set to 0. CorrelationId={_correlationId}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[QoSBandwidthModule] Falha ao aplicar QoS", ex);
                result.Success = false;
                result.Message = ex.Message;
            }

            return Task.FromResult(result);
        }

        public Task<ModuleRollbackResult> RollbackAsync(ModuleExecutionResult execResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleRollbackResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_QoS_RollbackStarting") ?? "Restaurando configuração original de QoS...", SmartRepairEventType.RollbackStarted);

            try
            {
                using var key = RegistryHelper.OpenKeySafe(RegistryHive.LocalMachine, RegistryView.Registry64, RegistryPath, true);
                if (key != null)
                {
                    if (!string.IsNullOrEmpty(_originalValue) && int.TryParse(_originalValue, out var originalInt))
                    {
                        RegistryHelper.SetValueSafe(key, ValueName, originalInt, RegistryValueKind.DWord);
                    }
                    else
                    {
                        using var subKey = RegistryHelper.OpenKeySafe(RegistryHive.LocalMachine, RegistryView.Registry64, RegistryPath, true);
                        if (subKey != null)
                        {
                            subKey.DeleteValue(ValueName, false);
                        }
                    }
                }

                result.Success = true;
                result.ItemsRestored = 1;
                _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_QoS_RollbackDone") ?? "Configuração de QoS restaurada.", SmartRepairEventType.RollbackFinished);
                _logger.LogInfo($"[QoSBandwidthModule] Rollback concluído. CorrelationId={_correlationId}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[QoSBandwidthModule] Falha no rollback QoS", ex);
                result.Success = false;
            }

            return Task.FromResult(result);
        }
    }
}
