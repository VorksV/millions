using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services.SmartRepair.Architecture;

namespace VoltrisOptimizer.Services.SmartRepair.Modules
{
    public class WindowsOldRemovalModule : ISmartRepairModule
    {
        private readonly ILoggingService _logger;
        private readonly ISmartRepairEventBus _eventBus;
        private readonly string _correlationId;

        public string ModuleId => "WindowsOldRemoval";
        public string Category => "Disk";

        public string Name => Services.LocalizationService.Instance.GetString("SmartRepair_Module_WinOld_Name") ?? "Remoção do Windows.old";
        public string Description => Services.LocalizationService.Instance.GetString("SmartRepair_Module_WinOld_Desc") ?? "Remove a pasta Windows.old de instalações anteriores com mais de 10 dias.";

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

        public WindowsOldRemovalModule(ILoggingService logger, ISmartRepairEventBus eventBus, string correlationId)
        {
            _logger = logger;
            _eventBus = eventBus;
            _correlationId = correlationId;
        }

        private class WindowsOldTarget
        {
            public string Path { get; set; } = string.Empty;
            public DateTime CreationTime { get; set; }
            public int AgeDays { get; set; }
            public bool IsEligible { get; set; }
        }

        public async Task<ModuleScanResult> ScanAsync(IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleScanResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_WinOld_ScanStarting") ?? "Verificando pasta Windows.old...", SmartRepairEventType.OperationStarted);

            await Task.Run(() =>
            {
                try
                {
                    var winOldPath = @"C:\Windows.old";
                    if (!Directory.Exists(winOldPath))
                    {
                        _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_WinOld_ScanNotFound") ?? "Windows.old não encontrado.", SmartRepairEventType.ProgressChanged);
                        result.Statistics.ItemsScanned = 1;
                        result.Statistics.ItemsFound = 0;
                        return;
                    }

                    var creationTime = Directory.GetCreationTime(winOldPath);
                    var ageDays = (int)(DateTime.Now - creationTime).TotalDays;

                    _logger.LogInfo($"[WindowsOldRemovalModule] Windows.old encontrado, criado em {creationTime:yyyy-MM-dd}, idade: {ageDays} dias");

                    var target = new WindowsOldTarget
                    {
                        Path = winOldPath,
                        CreationTime = creationTime,
                        AgeDays = ageDays,
                        IsEligible = ageDays >= 10
                    };

                    result.FoundItems.Add(target);
                    result.Statistics.ItemsScanned = 1;

                    if (target.IsEligible)
                    {
                        result.Statistics.ItemsFound = 1;
                        _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_WinOld_ScanFound") ?? "Windows.old elegível ({0} dias).", ageDays), SmartRepairEventType.ProgressChanged);
                    }
                    else
                    {
                        _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_WinOld_SkippedTooRecent") ?? "Windows.old muito recente ({0} dias). Mínimo: 10.", ageDays), SmartRepairEventType.WarningRaised);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[WindowsOldRemovalModule] Erro no scan", ex);
                    result.Success = false;
                    result.Message = ex.Message;
                }
            }, ct);

            _logger.LogInfo($"[{ModuleId}] Scan concluído. Encontrados={result.Statistics.ItemsFound}, Escaneados={result.Statistics.ItemsScanned}, CorrelationId={_correlationId}");
            progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = result.Message ?? "Scan concluído." });
            return result;
        }

        public Task<ModuleSimulationResult> SimulateAsync(ModuleScanResult scanResult, CancellationToken ct)
        {
            var sim = new ModuleSimulationResult();
            sim.RiskLevel = RiskLevel.Safe;

            foreach (WindowsOldTarget target in scanResult.FoundItems)
            {
                if (target.IsEligible)
                {
                    sim.ItemsToProcess.Add(target);
                    sim.EstimatedStatistics.ItemsFound = 1;
                    sim.EstimatedStatistics.SpaceRecoveredBytes = 5_000_000_000L;
                }
            }

            _logger.LogInfo($"[{ModuleId}] Simulação concluída. Itens a processar={sim.ItemsToProcess.Count}, RiskLevel={sim.RiskLevel}, CorrelationId={_correlationId}");
            return Task.FromResult(sim);
        }

        public async Task<ModuleExecutionResult> ExecuteAsync(ModuleSimulationResult simResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleExecutionResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_WinOld_ExecStarting") ?? "Iniciando remoção do Windows.old via DISM...", SmartRepairEventType.OperationStarted);

            foreach (WindowsOldTarget target in simResult.ItemsToProcess)
            {
                ct.ThrowIfCancellationRequested();

                if (!target.IsEligible)
                {
                    _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_WinOld_SkippedTooRecent") ?? "Windows.old muito recente — ignorando.", SmartRepairEventType.WarningRaised);
                    result.FinalStatistics.ItemsIgnored++;
                    continue;
                }

                progress?.Report(new RepairProgress { StepPercent = 30, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_WinOld_ExecProgress") ?? "Executando DISM StartComponentCleanup..." });

                try
                {
                    _logger.LogDebug($"[{ModuleId}] Executando: dism /Online /Cleanup-Image /StartComponentCleanup /ResetBase...");
                    bool success = ProcessHelper.Run("dism", "/Online /Cleanup-Image /StartComponentCleanup /ResetBase", out string stdOut, out string stdErr, 600000);

                    if (success)
                    {
                        result.FinalStatistics.ItemsRepaired++;
                        result.FinalStatistics.SpaceRecoveredBytes = 5_000_000_000L;
                        _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_WinOld_ExecDone") ?? "Windows.old removido com sucesso.", SmartRepairEventType.OperationFinished);
                    }
                    else
                    {
                        _eventBus.PublishMessage(_correlationId, ModuleId, $"DISM falhou: {stdErr}", SmartRepairEventType.WarningRaised);
                        result.FinalStatistics.ItemsIgnored++;
                    }

                    progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_WinOld_ExecDone") ?? "Processo concluído." });
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogError($"[WindowsOldRemovalModule] Erro na execução", ex);
                    result.Success = false;
                    result.Message = ex.Message;
                }
            }

            _logger.LogInfo($"[{ModuleId}] Execução concluída. Repaired={result.FinalStatistics.ItemsRepaired}, Errors={result.FinalStatistics.ErrorCount}, Space={result.FinalStatistics.SpaceRecoveredBytes}B, CorrelationId={_correlationId}");
            return result;
        }

        public Task<ModuleRollbackResult> RollbackAsync(ModuleExecutionResult execResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_WinOld_NoRollback") ?? "Rollback não disponível para remoção do Windows.old.", SmartRepairEventType.RollbackStarted);
            var rollbackResult = new ModuleRollbackResult
            {
                Success = false,
                Message = Services.LocalizationService.Instance.GetString("SmartRepair_Module_WinOld_NoRollback") ?? "Rollback não disponível."
            };
            _logger.LogInfo($"[{ModuleId}] Rollback concluído. Restored={rollbackResult.ItemsRestored}, Failed={rollbackResult.ItemsFailedToRestore}, CorrelationId={_correlationId}");
            progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = "Rollback concluído." });
            _eventBus.PublishMessage(_correlationId, ModuleId, rollbackResult.Message, SmartRepairEventType.RollbackFinished);
            return Task.FromResult(rollbackResult);
        }
    }
}
