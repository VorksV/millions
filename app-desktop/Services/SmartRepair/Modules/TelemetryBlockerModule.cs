using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services.SmartRepair.Architecture;

namespace VoltrisOptimizer.Services.SmartRepair.Modules
{
    public class TelemetryBlockerModule : ISmartRepairModule
    {
        private readonly ILoggingService _logger;
        private readonly ISmartRepairEventBus _eventBus;
        private readonly string _correlationId;

        public string ModuleId => "TelemetryBlocker";
        public string Category => "System";

        public string Name => Services.LocalizationService.Instance.GetString("SmartRepair_Module_Telemetry_Name") ?? "Bloqueio de Telemetria";
        public string Description => Services.LocalizationService.Instance.GetString("SmartRepair_Module_Telemetry_Desc") ?? "Desabilita serviços de telemetria da Microsoft (DiagTrack/dmwappush) e bloqueia coleta de dados.";

        public ModuleCapabilities Capabilities { get; } = new ModuleCapabilities
        {
            SupportsScan = true, SupportsSimulation = true, SupportsExecution = true, SupportsRollback = true,
            SupportsParallelism = false, HasDestructiveOperations = false, MaxRiskLevel = RiskLevel.Safe,
            RequiresAdmin = true
        };

        public IReadOnlyList<string> Dependencies => Array.Empty<string>();

        public TelemetryBlockerModule(ILoggingService logger, ISmartRepairEventBus eventBus, string correlationId)
        {
            _logger = logger; _eventBus = eventBus; _correlationId = correlationId;
        }

        public async Task<ModuleScanResult> ScanAsync(IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleScanResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Telemetry_ScanStarting") ?? "Verificando serviços de telemetria...", SmartRepairEventType.OperationStarted);

            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                if (SystemInformationHelper.IsEnterpriseEdition())
                {
                    string msg = Services.LocalizationService.Instance.GetString("SmartRepair_Module_Telemetry_SkippedEnterprise") ?? "Edição Enterprise. Operação ignorada.";
                    _eventBus.PublishMessage(_correlationId, ModuleId, msg, SmartRepairEventType.ProgressChanged);
                    result.Message = msg;
                    return;
                }

                string scanResult = Services.LocalizationService.Instance.GetString("SmartRepair_Module_Telemetry_ScanResult") ?? "Telemetria desabilitável.";
                result.FoundItems.Add(new { NeedsBlock = true });
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
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Telemetry_ExecStarting") ?? "Desabilitando telemetria...", SmartRepairEventType.OperationStarted);

            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                if (SystemInformationHelper.IsEnterpriseEdition())
                {
                    string msg = Services.LocalizationService.Instance.GetString("SmartRepair_Module_Telemetry_SkippedEnterprise") ?? "Edição Enterprise. Operação ignorada.";
                    _eventBus.PublishMessage(_correlationId, ModuleId, msg, SmartRepairEventType.ProgressChanged);
                    progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = msg });
                    result.Message = msg;
                    return;
                }

                progress?.Report(new RepairProgress { StepPercent = 25, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_Telemetry_ExecService1") ?? "Desabilitando DiagTrack..." });
                _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Telemetry_ExecService1") ?? "Desabilitando DiagTrack...", SmartRepairEventType.ProgressChanged);

                _logger.LogDebug($"[{ModuleId}] Executando: sc config DiagTrack start= disabled");
                ProcessHelper.RunSc("config DiagTrack start= disabled", out _, out _);
                _logger.LogDebug($"[{ModuleId}] Executando: sc stop DiagTrack");
                ProcessHelper.RunSc("stop DiagTrack", out _, out _);

                progress?.Report(new RepairProgress { StepPercent = 50, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_Telemetry_ExecService2") ?? "Desabilitando dmwappushservice..." });
                _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Telemetry_ExecService2") ?? "Desabilitando dmwappushservice...", SmartRepairEventType.ProgressChanged);

                _logger.LogDebug($"[{ModuleId}] Executando: sc config dmwappushservice start= disabled");
                ProcessHelper.RunSc("config dmwappushservice start= disabled", out _, out _);
                _logger.LogDebug($"[{ModuleId}] Executando: sc stop dmwappushservice");
                ProcessHelper.RunSc("stop dmwappushservice", out _, out _);

                progress?.Report(new RepairProgress { StepPercent = 75, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_Telemetry_ExecRegistry") ?? "Aplicando política de telemetria..." });
                _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Telemetry_ExecRegistry") ?? "Aplicando política de telemetria...", SmartRepairEventType.ProgressChanged);

                _logger.LogDebug($"[{ModuleId}] Criando chave de registro: HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection");
                using var key = RegistryHelper.CreateKeyPath(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection");
                if (key != null)
                {
                    RegistryHelper.SetValueSafe(key, "AllowTelemetry", 0, RegistryValueKind.DWord);
                    result.FinalStatistics.ItemsRepaired = 3;
                }
                else
                {
                    _logger.LogError($"[{ModuleId}] Falha ao criar chave de política de telemetria");
                    result.FinalStatistics.ItemsIgnored = 1;
                }

                progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_Telemetry_ExecDone") ?? "Telemetria desabilitada." });
                _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Telemetry_ExecDone") ?? "Telemetria desabilitada com sucesso.", SmartRepairEventType.OperationFinished);
                _logger.LogInfo($"[{ModuleId}] Execução concluída. Repaired={result.FinalStatistics.ItemsRepaired}, Errors={result.FinalStatistics.ErrorCount}, Space={result.FinalStatistics.SpaceRecoveredBytes}B, CorrelationId={_correlationId}");
            }, ct);

            return result;
        }

        public async Task<ModuleRollbackResult> RollbackAsync(ModuleExecutionResult execResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleRollbackResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Telemetry_RollbackStarting") ?? "Revertendo bloqueio de telemetria...", SmartRepairEventType.RollbackStarted);

            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(new RepairProgress { StepPercent = 30, StatusMessage = "Reabilitando DiagTrack..." });

                _logger.LogDebug($"[{ModuleId}] Executando: sc config DiagTrack start= auto");
                ProcessHelper.RunSc("config DiagTrack start= auto", out _, out _);
                _logger.LogDebug($"[{ModuleId}] Executando: sc start DiagTrack");
                ProcessHelper.RunSc("start DiagTrack", out _, out _);

                progress?.Report(new RepairProgress { StepPercent = 60, StatusMessage = "Reabilitando dmwappushservice..." });

                _logger.LogDebug($"[{ModuleId}] Executando: sc config dmwappushservice start= auto");
                ProcessHelper.RunSc("config dmwappushservice start= auto", out _, out _);
                _logger.LogDebug($"[{ModuleId}] Executando: sc start dmwappushservice");
                ProcessHelper.RunSc("start dmwappushservice", out _, out _);

                progress?.Report(new RepairProgress { StepPercent = 90, StatusMessage = "Removendo política de telemetria..." });

                _logger.LogDebug($"[{ModuleId}] Abrindo chave de registro para rollback: HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection");
                using var key = RegistryHelper.OpenKeySafe(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", true);
                if (key != null)
                {
                    try
                    {
                        key.DeleteValue("AllowTelemetry", throwOnMissingValue: false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"[{ModuleId}] Erro ao remover AllowTelemetry", ex);
                    }
                }

                result.ItemsRestored = 3;
                progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_Telemetry_RollbackDone") ?? "Telemetria restaurada." });
                _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Telemetry_RollbackDone") ?? "Telemetria restaurada com sucesso.", SmartRepairEventType.RollbackFinished);
                _logger.LogInfo($"[{ModuleId}] Rollback concluído. Restored={result.ItemsRestored}, Failed={result.ItemsFailedToRestore}, CorrelationId={_correlationId}");
            }, ct);

            return result;
        }
    }
}
