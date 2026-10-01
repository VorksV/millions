using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services.SmartRepair.Architecture;

namespace VoltrisOptimizer.Services.SmartRepair.Modules
{
    public class NtfsMetaFileDefragModule : ISmartRepairModule
    {
        private readonly ILoggingService _logger;
        private readonly ISmartRepairEventBus _eventBus;
        private readonly string _correlationId;

        public string ModuleId => "NtfsMetaFileDefrag";
        public string Category => "Disk";

        public string Name => Services.LocalizationService.Instance.GetString("SmartRepair_Module_NtfsMeta_Name") ?? "Desfragmentação de Metadados NTFS";
        public string Description => Services.LocalizationService.Instance.GetString("SmartRepair_Module_NtfsMeta_Desc") ?? "Consolida arquivos de metadados NTFS (MFT) em HDDs para melhor performance.";

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

        public NtfsMetaFileDefragModule(ILoggingService logger, ISmartRepairEventBus eventBus, string correlationId)
        {
            _logger = logger;
            _eventBus = eventBus;
            _correlationId = correlationId;
        }

        private class NtfsMetaTarget
        {
            public bool IsHdd { get; set; }
            public double FreeSpacePercent { get; set; }
            public bool HasEnoughFreeSpace { get; set; }
            public bool IsEligible { get; set; }
        }

        public async Task<ModuleScanResult> ScanAsync(IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleScanResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_NtfsMeta_ScanStarting") ?? "Verificando tipo de disco e espaço livre...", SmartRepairEventType.OperationStarted);

            await Task.Run(() =>
            {
                try
                {
                    var target = new NtfsMetaTarget();

                    _logger.LogDebug($"[{ModuleId}] Executando consulta WMI: SELECT MediaType FROM Win32_DiskDrive...");
                    using var searcher = new ManagementObjectSearcher("SELECT MediaType FROM Win32_DiskDrive");
                    foreach (ManagementObject obj in searcher.Get())
                    {
                using var __dispose_obj = obj;
                        var mediaType = obj["MediaType"]?.ToString() ?? "";
                        target.IsHdd = !mediaType.Contains("SSD", StringComparison.OrdinalIgnoreCase) &&
                                       !mediaType.Contains("NVMe", StringComparison.OrdinalIgnoreCase) &&
                                       mediaType.Contains("Fixed", StringComparison.OrdinalIgnoreCase);

                        if (!target.IsHdd && string.IsNullOrEmpty(mediaType))
                            target.IsHdd = true;
                    }

                    _logger.LogDebug($"[{ModuleId}] Obtendo informações da unidade C:...");
                    var drive = new DriveInfo("C:");
                    if (drive.IsReady)
                    {
                        long freeBytes = drive.AvailableFreeSpace;
                        long totalBytes = drive.TotalSize;
                        target.FreeSpacePercent = (double)freeBytes / totalBytes * 100;
                        target.HasEnoughFreeSpace = target.FreeSpacePercent > 15;
                    }

                    target.IsEligible = target.IsHdd && target.HasEnoughFreeSpace;

                    result.FoundItems.Add(target);
                    result.Statistics.ItemsScanned = 1;

                    if (!target.IsHdd)
                    {
                        _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_NtfsMeta_ScanSsdSkipped") ?? "SSD/NVMe detectado — não é necessário desfragmentar metadados.", SmartRepairEventType.WarningRaised);
                    }
                    else if (!target.HasEnoughFreeSpace)
                    {
                        _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_NtfsMeta_ScanSpaceLow") ?? "Espaço livre insuficiente: {0:F1}% (mínimo 15%).", target.FreeSpacePercent), SmartRepairEventType.WarningRaised);
                    }
                    else
                    {
                        result.Statistics.ItemsFound = 1;
                        _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_NtfsMeta_ScanReady") ?? "HDD elegível para consolidação de metadados NTFS.", SmartRepairEventType.ProgressChanged);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[NtfsMetaFileDefragModule] Erro no scan", ex);
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

            foreach (NtfsMetaTarget target in scanResult.FoundItems)
            {
                if (target.IsEligible)
                {
                    sim.ItemsToProcess.Add(target);
                    sim.EstimatedStatistics.ItemsFound = 1;
                }
            }

            _logger.LogInfo($"[{ModuleId}] Simulação concluída. Itens a processar={sim.ItemsToProcess.Count}, RiskLevel={sim.RiskLevel}, CorrelationId={_correlationId}");
            return Task.FromResult(sim);
        }

        public async Task<ModuleExecutionResult> ExecuteAsync(ModuleSimulationResult simResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleExecutionResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_NtfsMeta_ExecStarting") ?? "Consolidando metadados NTFS em C:...", SmartRepairEventType.OperationStarted);

            foreach (NtfsMetaTarget target in simResult.ItemsToProcess)
            {
                ct.ThrowIfCancellationRequested();

                if (!target.IsEligible) continue;

                progress?.Report(new RepairProgress { StepPercent = 30, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_NtfsMeta_ExecProgress") ?? "Executando defrag.exe /K..." });

                try
                {
                    _logger.LogDebug($"[{ModuleId}] Executando: defrag.exe C: /K /U /V...");
                    bool success = ProcessHelper.Run("defrag.exe", "C: /K /U /V", out string stdOut, out string stdErr, 300000);

                    if (success)
                    {
                        result.FinalStatistics.ItemsRepaired++;
                        _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_NtfsMeta_ExecDone") ?? "Metadados NTFS consolidados com sucesso.", SmartRepairEventType.OperationFinished);
                    }
                    else
                    {
                        _eventBus.PublishMessage(_correlationId, ModuleId, $"defrag falhou: {stdErr}", SmartRepairEventType.WarningRaised);
                        result.FinalStatistics.ItemsIgnored++;
                    }

                    progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_NtfsMeta_ExecDone") ?? "Concluído." });
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogError($"[NtfsMetaFileDefragModule] Erro na execução", ex);
                    result.Success = false;
                    result.Message = ex.Message;
                }
            }

            _logger.LogInfo($"[{ModuleId}] Execução concluída. Repaired={result.FinalStatistics.ItemsRepaired}, Errors={result.FinalStatistics.ErrorCount}, Space={result.FinalStatistics.SpaceRecoveredBytes}B, CorrelationId={_correlationId}");
            return result;
        }

        public Task<ModuleRollbackResult> RollbackAsync(ModuleExecutionResult execResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_NtfsMeta_NoRollback") ?? "Rollback não necessário — desfragmentação de metadados é segura.", SmartRepairEventType.RollbackStarted);
            var rollbackResult = new ModuleRollbackResult
            {
                Success = true,
                Message = Services.LocalizationService.Instance.GetString("SmartRepair_Module_NtfsMeta_NoRollback") ?? "Rollback não necessário."
            };
            _logger.LogInfo($"[{ModuleId}] Rollback concluído. Restored={rollbackResult.ItemsRestored}, Failed={rollbackResult.ItemsFailedToRestore}, CorrelationId={_correlationId}");
            progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = "Rollback concluído." });
            _eventBus.PublishMessage(_correlationId, ModuleId, rollbackResult.Message, SmartRepairEventType.RollbackFinished);
            return Task.FromResult(rollbackResult);
        }
    }
}
