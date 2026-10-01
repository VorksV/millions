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
    public class WindowsFeaturesModule : ISmartRepairModule
    {
        private readonly ILoggingService _logger;
        private readonly ISmartRepairEventBus _eventBus;
        private readonly string _correlationId;

        private static readonly string[] Features = { "Internet-Explorer-Optional", "XPS-Foundation-XPS-Viewer", "WorkFolders-Client", "Printing-XPSServices-Features" };

        public string ModuleId => "WindowsFeatures";
        public string Category => "System";

        public string Name => Services.LocalizationService.Instance.GetString("SmartRepair_Module_WinFeatures_Name") ?? "Recursos do Windows";
        public string Description => Services.LocalizationService.Instance.GetString("SmartRepair_Module_WinFeatures_Desc") ?? "Desabilita recursos opcionais do Windows não utilizados (IE, XPS, WorkFolders, XPS Services).";

        public ModuleCapabilities Capabilities { get; } = new ModuleCapabilities
        {
            SupportsScan = true, SupportsSimulation = true, SupportsExecution = true, SupportsRollback = true,
            SupportsParallelism = false, HasDestructiveOperations = false, MaxRiskLevel = RiskLevel.Safe,
            RequiresAdmin = true
        };

        public IReadOnlyList<string> Dependencies => Array.Empty<string>();

        public WindowsFeaturesModule(ILoggingService logger, ISmartRepairEventBus eventBus, string correlationId)
        {
            _logger = logger; _eventBus = eventBus; _correlationId = correlationId;
        }

        public async Task<ModuleScanResult> ScanAsync(IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleScanResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_WinFeatures_ScanStarting") ?? "Verificando recursos opcionais do Windows...", SmartRepairEventType.OperationStarted);

            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                bool isEdgeDefault = IsEdgeDefaultBrowser();
                bool isEnterprise = SystemInformationHelper.IsEnterpriseEdition();

                foreach (var feature in Features)
                {
                    ct.ThrowIfCancellationRequested();

                    if (feature == "Internet-Explorer-Optional" && !isEdgeDefault)
                        continue;

                    if (feature == "WorkFolders-Client" && isEnterprise)
                        continue;

                    if (IsFeatureEnabled(feature))
                    {
                        result.FoundItems.Add(feature);
                        result.Statistics.ItemsFound++;
                    }
                }

                string scanResult = Services.LocalizationService.Instance.GetString("SmartRepair_Module_WinFeatures_ScanResult") ?? string.Format("{0} recurso(s) encontrado(s) para desabilitar.", result.Statistics.ItemsFound);
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
            var features = simResult.ItemsToProcess.Cast<string>().ToList();

            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_WinFeatures_ExecStarting") ?? "Desabilitando recursos do Windows...", SmartRepairEventType.OperationStarted);

            int total = features.Count;
            int current = 0;

            foreach (var feature in features)
            {
                ct.ThrowIfCancellationRequested();
                current++;

                int percent = (int)((double)current / total * 100);
                progress?.Report(new RepairProgress { StepPercent = percent, StatusMessage = string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_WinFeatures_ExecProgress") ?? "Desabilitando {0}...", feature) });
                _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_WinFeatures_ExecProgress") ?? "Desabilitando {0}...", feature), SmartRepairEventType.ProgressChanged);

                _logger.LogDebug($"[{ModuleId}] Executando: dism /online /Disable-Feature /FeatureName:\"{feature}\" /Remove /NoRestart");
                var pr = await ProcessHelper.RunAsync("dism", $"/online /Disable-Feature /FeatureName:\"{feature}\" /Remove /NoRestart", ct, 120000);
                if (pr.Success)
                {
                    result.FinalStatistics.ItemsRepaired++;
                    result.ProcessedItems.Add(feature);
                }
                else
                {
                    _logger.LogWarning($"[{ModuleId}] Falha ao desabilitar feature {feature}: {pr.StdErr}");
                    result.FinalStatistics.ItemsIgnored++;
                    result.FailedItems.Add(feature);
                }
            }

            progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_WinFeatures_ExecDone") ?? "Recursos do Windows desabilitados." });
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_WinFeatures_ExecDone") ?? "Recursos do Windows desabilitados com sucesso.", SmartRepairEventType.OperationFinished);
            _logger.LogInfo($"[{ModuleId}] Execução concluída. Repaired={result.FinalStatistics.ItemsRepaired}, Errors={result.FinalStatistics.ErrorCount}, Space={result.FinalStatistics.SpaceRecoveredBytes}B, CorrelationId={_correlationId}");
            return result;
        }

        public async Task<ModuleRollbackResult> RollbackAsync(ModuleExecutionResult execResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleRollbackResult();
            var features = execResult.ProcessedItems.Cast<string>().ToList();

            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_WinFeatures_RollbackStarting") ?? "Restaurando recursos do Windows...", SmartRepairEventType.RollbackStarted);

            int total = features.Count;
            int current = 0;

            _logger.LogDebug($"[{ModuleId}] Revertendo {total} recurso(s) do Windows...");
            foreach (var feature in features)
            {
                ct.ThrowIfCancellationRequested();
                current++;

                int percent = (int)((double)current / total * 100);
                progress?.Report(new RepairProgress { StepPercent = percent, StatusMessage = string.Format("Reabilitando {0}...", feature) });
                _eventBus.PublishMessage(_correlationId, ModuleId, string.Format("Reabilitando {0}...", feature), SmartRepairEventType.ProgressChanged);

                _logger.LogDebug($"[{ModuleId}] Executando: dism /online /Enable-Feature /FeatureName:\"{feature}\" /NoRestart");
                var pr = await ProcessHelper.RunAsync("dism", $"/online /Enable-Feature /FeatureName:\"{feature}\" /NoRestart", ct, 120000);
                if (pr.Success)
                    result.ItemsRestored++;
                else
                    result.ItemsFailedToRestore++;
            }

            progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_WinFeatures_RollbackDone") ?? "Recursos do Windows restaurados." });
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_WinFeatures_RollbackDone") ?? "Recursos do Windows restaurados.", SmartRepairEventType.RollbackFinished);
            _logger.LogInfo($"[{ModuleId}] Rollback concluído. Restored={result.ItemsRestored}, Failed={result.ItemsFailedToRestore}, CorrelationId={_correlationId}");
            return result;
        }

        private bool IsFeatureEnabled(string featureName)
        {
            try
            {
                _logger.LogDebug($"[{ModuleId}] Verificando feature: dism /online /Get-FeatureInfo /FeatureName:\"{featureName}\"");
                var pr = ProcessHelper.Run("dism", $"/online /Get-FeatureInfo /FeatureName:\"{featureName}\"", out string stdOut, out _, 30000);
                if (pr)
                {
                    return stdOut.Contains("State : Enabled", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex) { _logger?.LogWarning($"[WindowsFeatures] Erro ao verificar feature {featureName}: {ex.Message}"); }
            return false;
        }

        private bool IsEdgeDefaultBrowser()
        {
            try
            {
                _logger.LogDebug($"[{ModuleId}] Verificando navegador padrão no registro: HKCR\\http\\shell\\open\\command");
                using var key = Registry.ClassesRoot.OpenSubKey(@"http\shell\open\command");
                if (key == null) return false;
                var val = key.GetValue(string.Empty)?.ToString() ?? string.Empty;
                return val.Contains("msedge.exe", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }
}
