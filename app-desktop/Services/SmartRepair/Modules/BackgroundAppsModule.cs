using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services.SmartRepair.Architecture;

namespace VoltrisOptimizer.Services.SmartRepair.Modules
{
    public class BackgroundAppsModule : ISmartRepairModule
    {
        private readonly ILoggingService _logger;
        private readonly ISmartRepairEventBus _eventBus;
        private readonly string _correlationId;

        public string ModuleId => "BackgroundApps";
        public string Category => "System";

        public string Name => Services.LocalizationService.Instance.GetString("SmartRepair_Module_BackgroundApps_Name") ?? "Aplicativos em Segundo Plano";
        public string Description => Services.LocalizationService.Instance.GetString("SmartRepair_Module_BackgroundApps_Desc") ?? "Restringe execução de aplicativos em segundo plano para economizar recursos.";

        public ModuleCapabilities Capabilities { get; } = new ModuleCapabilities
        {
            SupportsScan = true, SupportsSimulation = true, SupportsExecution = true, SupportsRollback = true,
            SupportsParallelism = false, HasDestructiveOperations = false, MaxRiskLevel = RiskLevel.Safe,
            RequiresAdmin = true
        };

        public IReadOnlyList<string> Dependencies => Array.Empty<string>();

        public BackgroundAppsModule(ILoggingService logger, ISmartRepairEventBus eventBus, string correlationId)
        {
            _logger = logger; _eventBus = eventBus; _correlationId = correlationId;
        }

        public async Task<ModuleScanResult> ScanAsync(IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleScanResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_BackgroundApps_ScanStarting") ?? "Verificando permissão de aplicativos em segundo plano...", SmartRepairEventType.OperationStarted);

            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                if (SystemInformationHelper.IsEnterpriseEdition())
                {
                    string msg = Services.LocalizationService.Instance.GetString("SmartRepair_Module_BackgroundApps_SkippedEnterprise") ?? "Edição Enterprise. Operação ignorada.";
                    _eventBus.PublishMessage(_correlationId, ModuleId, msg, SmartRepairEventType.ProgressChanged);
                    result.Message = msg;
                    return;
                }

                if (!IsGamingProfile() && !SystemInformationHelper.IsOnBattery())
                {
                    string msg = Services.LocalizationService.Instance.GetString("SmartRepair_Module_BackgroundApps_SkippedNotGamer") ?? "Perfil não identificado como gamer. Operação ignorada.";
                    _eventBus.PublishMessage(_correlationId, ModuleId, msg, SmartRepairEventType.ProgressChanged);
                    result.Message = msg;
                    return;
                }

                string scanResult = Services.LocalizationService.Instance.GetString("SmartRepair_Module_BackgroundApps_ScanResult") ?? "Aplicativos em segundo plano podem ser restringidos.";
                result.FoundItems.Add(new { NeedsRestriction = true });
                result.Statistics.ItemsFound = 1;
                _eventBus.PublishMessage(_correlationId, ModuleId, scanResult, SmartRepairEventType.ProgressChanged);
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
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_BackgroundApps_ExecStarting") ?? "Restringindo aplicativos em segundo plano...", SmartRepairEventType.OperationStarted);

            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                if (SystemInformationHelper.IsEnterpriseEdition())
                {
                    string msg = Services.LocalizationService.Instance.GetString("SmartRepair_Module_BackgroundApps_SkippedEnterprise") ?? "Edição Enterprise. Operação ignorada.";
                    _eventBus.PublishMessage(_correlationId, ModuleId, msg, SmartRepairEventType.ProgressChanged);
                    progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = msg });
                    result.Message = msg;
                    return;
                }

                if (!IsGamingProfile() && !SystemInformationHelper.IsOnBattery())
                {
                    string msg = Services.LocalizationService.Instance.GetString("SmartRepair_Module_BackgroundApps_SkippedNotGamer") ?? "Perfil não identificado como gamer. Operação ignorada.";
                    _eventBus.PublishMessage(_correlationId, ModuleId, msg, SmartRepairEventType.ProgressChanged);
                    progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = msg });
                    result.Message = msg;
                    return;
                }

                progress?.Report(new RepairProgress { StepPercent = 50, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_BackgroundApps_ExecProgress") ?? "Aplicando política de aplicativos em segundo plano..." });
                _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_BackgroundApps_ExecProgress") ?? "Aplicando política de aplicativos em segundo plano...", SmartRepairEventType.ProgressChanged);

                _logger.LogDebug($"[{ModuleId}] Criando chave de registro: HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\AppPrivacy");
                using var key = RegistryHelper.CreateKeyPath(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy");
                if (key != null)
                {
                    RegistryHelper.SetValueSafe(key, "LetAppsRunInBackground", 2, RegistryValueKind.DWord);
                    result.FinalStatistics.ItemsRepaired = 1;
                }
                else
                {
                    _logger.LogError($"[{ModuleId}] Falha ao criar chave de política AppPrivacy");
                    result.FinalStatistics.ItemsIgnored = 1;
                }

                progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_BackgroundApps_ExecDone") ?? "Aplicativos em segundo plano restringidos." });
                _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_BackgroundApps_ExecDone") ?? "Aplicativos em segundo plano restringidos com sucesso.", SmartRepairEventType.OperationFinished);
                _logger.LogInfo($"[{ModuleId}] Execução concluída. Repaired={result.FinalStatistics.ItemsRepaired}, Errors={result.FinalStatistics.ErrorCount}, Space={result.FinalStatistics.SpaceRecoveredBytes}B, CorrelationId={_correlationId}");
            }, ct);

            return result;
        }

        public async Task<ModuleRollbackResult> RollbackAsync(ModuleExecutionResult execResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleRollbackResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_BackgroundApps_RollbackStarting") ?? "Revertendo restrição de aplicativos em segundo plano...", SmartRepairEventType.RollbackStarted);

            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(new RepairProgress { StepPercent = 50, StatusMessage = "Removendo política de segundo plano..." });

                _logger.LogDebug($"[{ModuleId}] Abrindo chave de registro para rollback: HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\AppPrivacy");
                using var key = RegistryHelper.OpenKeySafe(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", true);
                if (key != null)
                {
                    try
                    {
                        key.DeleteValue("LetAppsRunInBackground", throwOnMissingValue: false);
                        result.ItemsRestored = 1;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"[{ModuleId}] Erro ao remover LetAppsRunInBackground", ex);
                        result.ItemsFailedToRestore = 1;
                    }
                }
                else
                {
                    result.ItemsFailedToRestore = 1;
                }

                progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_BackgroundApps_RollbackDone") ?? "Restrição de segundo plano revertida." });
                _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_BackgroundApps_RollbackDone") ?? "Restrição de segundo plano revertida.", SmartRepairEventType.RollbackFinished);
                _logger.LogInfo($"[{ModuleId}] Rollback concluído. Restored={result.ItemsRestored}, Failed={result.ItemsFailedToRestore}, CorrelationId={_correlationId}");
            }, ct);

            return result;
        }

        private bool IsGamingProfile()
        {
            try
            {
                _logger.LogDebug($"[{ModuleId}] Verificando chave de registro: HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\GameDVR");
                using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                    .OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR");
                if (key != null)
                {
                    var val = key.GetValue("AppCaptureEnabled");
                    if (val != null && val.ToString() == "1")
                        return true;
                }
            }
            catch (Exception ex) { _logger?.LogWarning($"[BackgroundApps] Erro ao verificar GameDVR: {ex.Message}"); }
            return false;
        }
    }
}
