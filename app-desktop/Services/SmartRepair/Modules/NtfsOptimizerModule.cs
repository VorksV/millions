using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services.SmartRepair.Architecture;

namespace VoltrisOptimizer.Services.SmartRepair.Modules
{
    public class NtfsOptimizerModule : ISmartRepairModule
    {
        private readonly ILoggingService _logger;
        private readonly ISmartRepairEventBus _eventBus;
        private readonly string _correlationId;

        public string ModuleId => "NtfsOptimizer";
        public string Category => "Disk";

        public string Name => Services.LocalizationService.Instance.GetString("SmartRepair_Module_Ntfs_Name") ?? "Otimização NTFS";
        public string Description => Services.LocalizationService.Instance.GetString("SmartRepair_Module_Ntfs_Desc") ?? "Desabilita atualizações de último acesso e nomes 8.3 no NTFS para maior performance.";

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

        public NtfsOptimizerModule(ILoggingService logger, ISmartRepairEventBus eventBus, string correlationId)
        {
            _logger = logger;
            _eventBus = eventBus;
            _correlationId = correlationId;
        }

        private class NtfsOptimizationTarget
        {
            public bool DisableLastAccess { get; set; }
            public bool Disable8Dot3 { get; set; }
            public bool IsEnterpriseWithAuditing { get; set; }
            public bool HasLegacy16BitApps { get; set; }
        }

        public async Task<ModuleScanResult> ScanAsync(IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleScanResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Ntfs_ScanStarting") ?? "Analisando configurações NTFS...", SmartRepairEventType.OperationStarted);

            await Task.Run(() =>
            {
                try
                {
                    var target = new NtfsOptimizationTarget();

                    var productName = RegistryHelper.GetValueString(
                        RegistryHelper.OpenKeySafe(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", false),
                        "ProductName") ?? "";

                    target.IsEnterpriseWithAuditing = productName.IndexOf("Enterprise", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        AuditPoliciesConfigured();

                    target.HasLegacy16BitApps = CheckLegacy16BitApps();

                    target.DisableLastAccess = !target.IsEnterpriseWithAuditing;
                    target.Disable8Dot3 = !target.HasLegacy16BitApps;

                    result.FoundItems.Add(target);
                    result.Statistics.ItemsScanned = 1;

                    if (target.DisableLastAccess || target.Disable8Dot3)
                        result.Statistics.ItemsFound = 1;

                    _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Ntfs_ScanResult") ?? "Análise NTFS concluída.", SmartRepairEventType.OperationFinished);
                    _logger.LogInfo($"[{ModuleId}] Scan concluído. Encontrados={result.Statistics.ItemsFound}, Escaneados={result.Statistics.ItemsScanned}, CorrelationId={_correlationId}");
                    progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = result.Message ?? "Scan concluído." });
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[NtfsOptimizerModule] Erro no scan", ex);
                    result.Success = false;
                    result.Message = ex.Message;
                }
            }, ct);

            return result;
        }

        private static bool AuditPoliciesConfigured()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SECURITY\Policy\PolAdtEv");
                return key != null && key.GetValueNames().Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool CheckLegacy16BitApps()
        {
            try
            {
                var apps = Directory.GetFiles(Environment.GetFolderPath(Environment.SpecialFolder.System), "*.exe")
                    .Where(f => f.EndsWith("_16.exe", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".com", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (apps.Count > 0) return true;

                if (Directory.Exists(@"C:\Windows\SysWOW64"))
                {
                    var ntvdm = Directory.GetFiles(@"C:\Windows\SysWOW64", "ntvdm*").Any();
                    if (ntvdm) return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public Task<ModuleSimulationResult> SimulateAsync(ModuleScanResult scanResult, CancellationToken ct)
        {
            var sim = new ModuleSimulationResult();
            sim.RiskLevel = RiskLevel.Safe;

            foreach (NtfsOptimizationTarget target in scanResult.FoundItems)
            {
                sim.ItemsToProcess.Add(target);
                sim.EstimatedStatistics.ItemsFound = 1;
            }

            _logger.LogInfo($"[{ModuleId}] Simulação concluída. Itens a processar={sim.ItemsToProcess.Count}, RiskLevel={sim.RiskLevel}, CorrelationId={_correlationId}");
            return Task.FromResult(sim);
        }

        public async Task<ModuleExecutionResult> ExecuteAsync(ModuleSimulationResult simResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleExecutionResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Ntfs_ExecStarting") ?? "Aplicando otimizações NTFS...", SmartRepairEventType.OperationStarted);

            foreach (NtfsOptimizationTarget target in simResult.ItemsToProcess)
            {
                ct.ThrowIfCancellationRequested();

                if (target.DisableLastAccess)
                {
                    progress?.Report(new RepairProgress { StepPercent = 30, StatusMessage = "Desabilitando NtfsDisableLastAccessUpdate..." });
                    _eventBus.PublishMessage(_correlationId, ModuleId, "Desabilitando NtfsDisableLastAccessUpdate...", SmartRepairEventType.ProgressChanged);

                    _logger.LogDebug($"[{ModuleId}] Executando: fsutil behavior set disablelastaccess 1...");
                    bool lastAccessOk = ProcessHelper.Run("fsutil", "behavior set disablelastaccess 1", out _, out _);
                    if (lastAccessOk)
                    {
                        result.FinalStatistics.ItemsRepaired++;
                        _logger.LogInfo("[NtfsOptimizerModule] disablelastaccess ativado");
                    }
                }

                if (target.Disable8Dot3)
                {
                    progress?.Report(new RepairProgress { StepPercent = 60, StatusMessage = "Desabilitando NtfsDisable8dot3NameCreation..." });
                    _eventBus.PublishMessage(_correlationId, ModuleId, "Desabilitando NtfsDisable8dot3NameCreation...", SmartRepairEventType.ProgressChanged);

                    _logger.LogDebug($"[{ModuleId}] Executando: fsutil behavior set disable8dot3 1...");
                    bool dot3Ok = ProcessHelper.Run("fsutil", "behavior set disable8dot3 1", out _, out _);
                    if (dot3Ok)
                    {
                        result.FinalStatistics.ItemsRepaired++;
                        _logger.LogInfo("[NtfsOptimizerModule] disable8dot3 ativado");
                    }
                }

                progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_Ntfs_ExecDone") ?? "Otimizações NTFS aplicadas." });
            }

            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Ntfs_ExecDone") ?? "Otimizações NTFS concluídas.", SmartRepairEventType.OperationFinished);
            _logger.LogInfo($"[{ModuleId}] Execução concluída. Repaired={result.FinalStatistics.ItemsRepaired}, Errors={result.FinalStatistics.ErrorCount}, Space={result.FinalStatistics.SpaceRecoveredBytes}B, CorrelationId={_correlationId}");
            return result;
        }

        public async Task<ModuleRollbackResult> RollbackAsync(ModuleExecutionResult execResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var rollback = new ModuleRollbackResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Ntfs_RollbackStarting") ?? "Revertendo otimizações NTFS...", SmartRepairEventType.RollbackStarted);

            await Task.Run(() =>
            {
                progress?.Report(new RepairProgress { StepPercent = 30, StatusMessage = "Restaurando NtfsDisableLastAccessUpdate..." });
                _logger.LogDebug($"[{ModuleId}] Executando: fsutil behavior set disablelastaccess 0...");
                bool lastAccessOk = ProcessHelper.Run("fsutil", "behavior set disablelastaccess 0", out _, out _);
                if (lastAccessOk) rollback.ItemsRestored++;

                progress?.Report(new RepairProgress { StepPercent = 60, StatusMessage = "Restaurando NtfsDisable8dot3NameCreation..." });
                _logger.LogDebug($"[{ModuleId}] Executando: fsutil behavior set disable8dot3 0...");
                bool dot3Ok = ProcessHelper.Run("fsutil", "behavior set disable8dot3 0", out _, out _);
                if (dot3Ok) rollback.ItemsRestored++;
            }, ct);

            rollback.Success = rollback.ItemsRestored > 0;
            progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_Ntfs_RollbackDone") ?? "Otimizações NTFS revertidas." });
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Ntfs_RollbackDone") ?? "Rollback NTFS concluído.", SmartRepairEventType.RollbackFinished);
            _logger.LogInfo($"[{ModuleId}] Rollback concluído. Restored={rollback.ItemsRestored}, Failed={rollback.ItemsFailedToRestore}, CorrelationId={_correlationId}");
            return rollback;
        }
    }
}
