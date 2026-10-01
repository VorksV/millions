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
    public class RemoteDiffCompressionModule : ISmartRepairModule
    {
        private const string FeatureName = "Remote Differential Compression";

        private readonly ILoggingService _logger;
        private readonly ISmartRepairEventBus _eventBus;
        private readonly string _correlationId;

        public string ModuleId => "RemoteDiffCompression";
        public string Category => "Network";
        public string Name => Services.LocalizationService.Instance.GetString("SmartRepair_Module_RemoteDiff_Name") ?? "Desabilitar Remote Differential Compression";
        public string Description => Services.LocalizationService.Instance.GetString("SmartRepair_Module_RemoteDiff_Desc") ?? "Desabilita o recurso Remote Differential Compression para reduzir overhead de rede.";

        public ModuleCapabilities Capabilities { get; } = new ModuleCapabilities
        {
            SupportsScan = true, SupportsSimulation = true, SupportsExecution = true, SupportsRollback = true,
            SupportsParallelism = false, HasDestructiveOperations = false, MaxRiskLevel = RiskLevel.Safe,
            RequiresAdmin = true
        };

        public IReadOnlyList<string> Dependencies => Array.Empty<string>();

        public RemoteDiffCompressionModule(ILoggingService logger, ISmartRepairEventBus eventBus, string correlationId)
        {
            _logger = logger; _eventBus = eventBus; _correlationId = correlationId;
        }

        public async Task<ModuleScanResult> ScanAsync(IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleScanResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_RemoteDiff_ScanStarting") ?? "Verificando Remote Differential Compression...", SmartRepairEventType.OperationStarted);
            progress?.Report(new RepairProgress { StepPercent = 20, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_RemoteDiff_ScanStarting") ?? "Verificando Remote Differential Compression..." });

            if (IsEnterpriseEdition())
            {
                result.Statistics.ItemsIgnored = 1;
                result.Message = Services.LocalizationService.Instance.GetString("SmartRepair_Module_RemoteDiff_SkippedEnterprise") ?? "Sistema Enterprise com BranchCache. Ignorando.";
                _eventBus.PublishMessage(_correlationId, ModuleId, result.Message, SmartRepairEventType.OperationFinished);
                return result;
            }

            var featureResult = await ProcessHelper.RunAsync("dism", $"/online /Get-FeatureInfo /FeatureName:\"{FeatureName}\"", ct);
            bool isEnabled = featureResult.Success && featureResult.StdOut.Contains("State : Enabled", StringComparison.OrdinalIgnoreCase);

            if (!isEnabled)
            {
                result.Message = Services.LocalizationService.Instance.GetString("SmartRepair_Module_RemoteDiff_ScanAlreadyDisabled") ?? "Remote Differential Compression já está desabilitado.";
                _eventBus.PublishMessage(_correlationId, ModuleId, result.Message, SmartRepairEventType.OperationFinished);
                return result;
            }

            result.Statistics.ItemsFound = 1;
            result.FoundItems.Add(FeatureName);
            result.Message = Services.LocalizationService.Instance.GetString("SmartRepair_Module_RemoteDiff_ScanEnabled") ?? "Remote Differential Compression está habilitado. Desabilitar.";
            _eventBus.PublishMessage(_correlationId, ModuleId, result.Message, SmartRepairEventType.OperationFinished);
            return result;
        }

        private static bool IsEnterpriseEdition()
        {
            using var key = RegistryHelper.OpenKeySafe(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", false);
            var editionId = RegistryHelper.GetValueString(key, "EditionID");
            return editionId != null && editionId.Contains("Enterprise", StringComparison.OrdinalIgnoreCase);
        }

        public Task<ModuleSimulationResult> SimulateAsync(ModuleScanResult scanResult, CancellationToken ct)
        {
            var sim = new ModuleSimulationResult();
            if (scanResult.Statistics.ItemsFound > 0)
            {
                sim.EstimatedStatistics.ItemsFound = 1;
                sim.ItemsToProcess.Add(FeatureName);
            }
            sim.RiskLevel = RiskLevel.Safe;
            return Task.FromResult(sim);
        }

        public async Task<ModuleExecutionResult> ExecuteAsync(ModuleSimulationResult simResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleExecutionResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_RemoteDiff_ExecStarting") ?? "Desabilitando Remote Differential Compression...", SmartRepairEventType.OperationStarted);
            progress?.Report(new RepairProgress { StepPercent = 30, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_RemoteDiff_ExecProgress") ?? "Executando DISM..." });

            try
            {
                var dismResult = await ProcessHelper.RunAsync("dism", $"/online /Disable-Feature /FeatureName:\"{FeatureName}\" /Quiet /NoRestart", ct);

                progress?.Report(new RepairProgress { StepPercent = 90, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_RemoteDiff_ExecDone") ?? "Remote Differential Compression desabilitado." });

                if (dismResult.Success)
                {
                    result.FinalStatistics.ItemsRepaired = 1;
                    result.Message = Services.LocalizationService.Instance.GetString("SmartRepair_Module_RemoteDiff_ExecDone") ?? "Remote Differential Compression desabilitado com sucesso.";
                }
                else
                {
                    result.Success = false;
                    result.Message = $"DISM exit code: {dismResult.ExitCode}";
                }

                _eventBus.PublishMessage(_correlationId, ModuleId, result.Message, SmartRepairEventType.OperationFinished);
                _logger.LogInfo($"[RemoteDiffCompressionModule] Execução concluída. Success={dismResult.Success}, ExitCode={dismResult.ExitCode}, CorrelationId={_correlationId}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[RemoteDiffCompressionModule] Falha ao desabilitar Remote Differential Compression", ex);
                result.Success = false;
                result.Message = ex.Message;
            }

            return result;
        }

        public async Task<ModuleRollbackResult> RollbackAsync(ModuleExecutionResult execResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleRollbackResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_RemoteDiff_RollbackStarting") ?? "Reabilitando Remote Differential Compression...", SmartRepairEventType.RollbackStarted);

            try
            {
                var dismResult = await ProcessHelper.RunAsync("dism", $"/online /Enable-Feature /FeatureName:\"{FeatureName}\" /Quiet /NoRestart", ct);

                result.Success = dismResult.Success;
                result.ItemsRestored = dismResult.Success ? 1 : 0;
                _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_RemoteDiff_RollbackDone") ?? "Remote Differential Compression reabilitado.", SmartRepairEventType.RollbackFinished);
                _logger.LogInfo($"[RemoteDiffCompressionModule] Rollback concluído. Success={dismResult.Success}, CorrelationId={_correlationId}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[RemoteDiffCompressionModule] Falha no rollback Remote Differential Compression", ex);
                result.Success = false;
            }

            return result;
        }
    }
}
