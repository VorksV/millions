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
    public class CortanaBlockerModule : ISmartRepairModule
    {
        private const string RegistryPath = @"SOFTWARE\Policies\Microsoft\Windows\Windows Search";

        private readonly ILoggingService _logger;
        private readonly ISmartRepairEventBus _eventBus;
        private readonly string _correlationId;

        public string ModuleId => "CortanaBlocker";
        public string Category => "System";

        public string Name => Services.LocalizationService.Instance.GetString("SmartRepair_Module_Cortana_Name") ?? "Bloqueio de Cortana";
        public string Description => Services.LocalizationService.Instance.GetString("SmartRepair_Module_Cortana_Desc") ?? "Desabilita Cortana e busca web integrada no Windows Search.";

        public ModuleCapabilities Capabilities { get; } = new ModuleCapabilities
        {
            SupportsScan = true, SupportsSimulation = true, SupportsExecution = true, SupportsRollback = true,
            SupportsParallelism = false, HasDestructiveOperations = false, MaxRiskLevel = RiskLevel.Safe,
            RequiresAdmin = true
        };

        public IReadOnlyList<string> Dependencies => Array.Empty<string>();

        public CortanaBlockerModule(ILoggingService logger, ISmartRepairEventBus eventBus, string correlationId)
        {
            _logger = logger; _eventBus = eventBus; _correlationId = correlationId;
        }

        public async Task<ModuleScanResult> ScanAsync(IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleScanResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Cortana_ScanStarting") ?? "Verificando configuração da Cortana...", SmartRepairEventType.OperationStarted);

            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                if (SystemInformationHelper.IsEnterpriseEdition() && IsParentPolicyLocked())
                {
                    string msg = Services.LocalizationService.Instance.GetString("SmartRepair_Module_Cortana_SkippedEnterprise") ?? "Edição Enterprise com GPO existente. Operação ignorada.";
                    _eventBus.PublishMessage(_correlationId, ModuleId, msg, SmartRepairEventType.ProgressChanged);
                    result.Message = msg;
                    return;
                }

                _logger.LogDebug($"[{ModuleId}] Abrindo chave de registro: HKLM\\{RegistryPath}");
                using var key = RegistryHelper.OpenKeySafe(RegistryHive.LocalMachine, RegistryView.Registry64, RegistryPath, false);
                if (key != null)
                {
                    var allowCortana = RegistryHelper.GetValueString(key, "AllowCortana");
                    var disableWebSearch = RegistryHelper.GetValueString(key, "DisableWebSearch");
                    var connectedSearchUseWeb = RegistryHelper.GetValueString(key, "ConnectedSearchUseWeb");

                    if (allowCortana == "0" && disableWebSearch == "1" && connectedSearchUseWeb == "0")
                    {
                        string msg = Services.LocalizationService.Instance.GetString("SmartRepair_Module_Cortana_ScanAlreadyBlocked") ?? "Cortana já está bloqueada.";
                        _eventBus.PublishMessage(_correlationId, ModuleId, msg, SmartRepairEventType.ProgressChanged);
                        result.Message = msg;
                        return;
                    }
                }

                string needsBlock = Services.LocalizationService.Instance.GetString("SmartRepair_Module_Cortana_ScanNeedsBlock") ?? "Cortana precisa ser bloqueada.";
                result.FoundItems.Add(new { NeedsBlock = true });
                result.Statistics.ItemsFound = 1;
                _eventBus.PublishMessage(_correlationId, ModuleId, needsBlock, SmartRepairEventType.ProgressChanged);
                _logger.LogInfo($"[{ModuleId}] Scan concluído. Encontrados={result.Statistics.ItemsFound}, Escaneados={result.Statistics.ItemsScanned}, CorrelationId={_correlationId}");
                progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = result.Message ?? "Scan concluído." });
                _eventBus.PublishMessage(_correlationId, ModuleId, result.Message ?? "Scan concluído.", SmartRepairEventType.OperationFinished);
            }, ct);

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
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Cortana_ExecStarting") ?? "Aplicando bloqueio da Cortana...", SmartRepairEventType.OperationStarted);

            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                if (SystemInformationHelper.IsEnterpriseEdition() && IsParentPolicyLocked())
                {
                    string msg = Services.LocalizationService.Instance.GetString("SmartRepair_Module_Cortana_SkippedEnterprise") ?? "Edição Enterprise com GPO existente. Operação ignorada.";
                    _eventBus.PublishMessage(_correlationId, ModuleId, msg, SmartRepairEventType.ProgressChanged);
                    progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = msg });
                    result.Message = msg;
                    return;
                }

                progress?.Report(new RepairProgress { StepPercent = 30, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_Cortana_ExecProgress") ?? "Configurando políticas de pesquisa..." });
                _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Cortana_ExecProgress") ?? "Configurando políticas de pesquisa...", SmartRepairEventType.ProgressChanged);

                _logger.LogDebug($"[{ModuleId}] Criando chave de registro: HKLM\\{RegistryPath}");
                using var key = RegistryHelper.CreateKeyPath(RegistryHive.LocalMachine, RegistryView.Registry64, RegistryPath);
                if (key != null)
                {
                    RegistryHelper.SetValueSafe(key, "AllowCortana", 0, RegistryValueKind.DWord);
                    RegistryHelper.SetValueSafe(key, "DisableWebSearch", 1, RegistryValueKind.DWord);
                    RegistryHelper.SetValueSafe(key, "ConnectedSearchUseWeb", 0, RegistryValueKind.DWord);
                    result.FinalStatistics.ItemsRepaired = 3;
                }
                else
                {
                    _logger.LogError($"[{ModuleId}] Falha ao criar chave de registro: {RegistryPath}");
                    result.FinalStatistics.ItemsIgnored = 3;
                }

                progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_Cortana_ExecDone") ?? "Cortana bloqueada com sucesso." });
                _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Cortana_ExecDone") ?? "Cortana bloqueada com sucesso.", SmartRepairEventType.OperationFinished);
                _logger.LogInfo($"[{ModuleId}] Execução concluída. Repaired={result.FinalStatistics.ItemsRepaired}, Errors={result.FinalStatistics.ErrorCount}, Space={result.FinalStatistics.SpaceRecoveredBytes}B, CorrelationId={_correlationId}");
            }, ct);

            return result;
        }

        public async Task<ModuleRollbackResult> RollbackAsync(ModuleExecutionResult execResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleRollbackResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Cortana_RollbackStarting") ?? "Revertendo bloqueio da Cortana...", SmartRepairEventType.RollbackStarted);

            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(new RepairProgress { StepPercent = 50, StatusMessage = "Removendo políticas de bloqueio..." });

                _logger.LogDebug($"[{ModuleId}] Abrindo chave de registro para rollback: HKLM\\{RegistryPath}");
                using var key = RegistryHelper.OpenKeySafe(RegistryHive.LocalMachine, RegistryView.Registry64, RegistryPath, true);
                if (key != null)
                {
                    try
                    {
                        key.DeleteValue("AllowCortana", throwOnMissingValue: false);
                        key.DeleteValue("DisableWebSearch", throwOnMissingValue: false);
                        key.DeleteValue("ConnectedSearchUseWeb", throwOnMissingValue: false);
                        result.ItemsRestored = 3;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"[{ModuleId}] Erro ao remover valores de registro", ex);
                        result.ItemsFailedToRestore = 3;
                    }
                }
                else
                {
                    result.ItemsFailedToRestore = 3;
                }

                progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_Cortana_RollbackDone") ?? "Bloqueio da Cortana revertido." });
                _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Cortana_RollbackDone") ?? "Bloqueio da Cortana revertido.", SmartRepairEventType.RollbackFinished);
                _logger.LogInfo($"[{ModuleId}] Rollback concluído. Restored={result.ItemsRestored}, Failed={result.ItemsFailedToRestore}, CorrelationId={_correlationId}");
            }, ct);
            return result;
        }

        private static bool IsParentPolicyLocked()
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var policiesKey = baseKey.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows", false);
                if (policiesKey == null) return false;
                using var searchKey = policiesKey.OpenSubKey("Windows Search", false);
                return searchKey == null;
            }
            catch
            {
                return false;
            }
        }
    }
}
