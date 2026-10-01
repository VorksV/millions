using System;
using System.Collections.Generic;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services.SmartRepair.Architecture;

namespace VoltrisOptimizer.Services.SmartRepair.Modules
{
    public class SysMainConfigModule : ISmartRepairModule
    {
        private readonly ILoggingService _logger;
        private readonly ISmartRepairEventBus _eventBus;
        private readonly string _correlationId;

        public string ModuleId => "SysMainConfig";
        public string Category => "System";

        public string Name => Services.LocalizationService.Instance.GetString("SmartRepair_Module_SysMain_Name") ?? "SysMain Configuration";
        public string Description => Services.LocalizationService.Instance.GetString("SmartRepair_Module_SysMain_Desc") ?? "Desabilita o serviço SysMain (Superfetch) em sistemas com SSD e RAM suficiente.";

        public ModuleCapabilities Capabilities { get; } = new ModuleCapabilities
        {
            SupportsScan = true, SupportsSimulation = true, SupportsExecution = true, SupportsRollback = true,
            SupportsParallelism = false, HasDestructiveOperations = false, MaxRiskLevel = RiskLevel.Safe,
            RequiresAdmin = true
        };

        public IReadOnlyList<string> Dependencies => Array.Empty<string>();

        private class SysMainInfo
        {
            public bool IsRunning { get; set; }
            public ulong TotalRamMb { get; set; }
            public bool HasSsd { get; set; }
            public int BuildNumber { get; set; }
        }

        public SysMainConfigModule(ILoggingService logger, ISmartRepairEventBus eventBus, string correlationId)
        {
            _logger = logger; _eventBus = eventBus; _correlationId = correlationId;
        }

        public async Task<ModuleScanResult> ScanAsync(IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleScanResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_SysMain_ScanStarting") ?? "Verificando configuração do SysMain...", SmartRepairEventType.OperationStarted);

            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    _logger.LogDebug($"[{ModuleId}] Executando consulta WMI...");
                    using var svcSearcher = new ManagementObjectSearcher("SELECT State FROM Win32_Service WHERE Name = 'SysMain'");
                    bool isRunning = false;
                    foreach (ManagementObject svc in svcSearcher.Get())
                    {
                using var __dispose_svc = svc;
                        isRunning = "Running".Equals(svc["State"]?.ToString(), StringComparison.OrdinalIgnoreCase);
                        break;
                    }

                    ulong totalRamMb = 0;
                    using var memSearcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem");
                    foreach (ManagementObject obj in memSearcher.Get())
                    {
                using var __dispose_obj = obj;
                        totalRamMb = Convert.ToUInt64(obj["TotalVisibleMemorySize"] ?? 0) / 1024;
                        break;
                    }

                    bool hasSsd = false;
                    _logger.LogDebug($"[{ModuleId}] Executando consulta WMI...");
                    using var diskSearcher = new ManagementObjectSearcher("SELECT MediaType FROM Win32_DiskDrive");
                    foreach (ManagementObject disk in diskSearcher.Get())
                    {
                using var __dispose_disk = disk;
                        object? mediaType = disk["MediaType"];
                        if (mediaType != null)
                        {
                            string mt = mediaType.ToString() ?? "";
                            if (mt.Contains("SSD", StringComparison.OrdinalIgnoreCase) ||
                                mt.Contains("Solid State", StringComparison.OrdinalIgnoreCase) ||
                                mt.Contains("NVMe", StringComparison.OrdinalIgnoreCase))
                            {
                                hasSsd = true;
                                break;
                            }
                        }
                    }

                    if (!hasSsd)
                    {
                        _logger.LogDebug($"[{ModuleId}] Executando consulta WMI...");
                        using var psSearcher = new ManagementObjectSearcher("SELECT MediaType FROM MSFT_PhysicalDisk");
                        try
                        {
                            foreach (ManagementObject disk in psSearcher.Get())
                            {
                using var __dispose_disk = disk;
                                ushort mt = Convert.ToUInt16(disk["MediaType"]);
                                if (mt == 4)
                                {
                                    hasSsd = true;
                                    break;
                                }
                            }
                        }
                        catch (Exception exDisk) { _logger?.LogWarning($"[SysMainConfig] Erro ao verificar tipo de mídia do disco: {exDisk.Message}"); }
                    }

                    int build = Environment.OSVersion.Version.Build;

                    result.FoundItems.Add(new SysMainInfo
                    {
                        IsRunning = isRunning,
                        TotalRamMb = totalRamMb,
                        HasSsd = hasSsd,
                        BuildNumber = build
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[{ModuleId}] Erro no scan", ex);
                }
            }, ct);

            result.Statistics.ItemsFound = result.FoundItems.Count;
            result.Statistics.ItemsScanned = result.FoundItems.Count;

            _logger.LogInfo($"[{ModuleId}] Scan concluído. Encontrados={result.Statistics.ItemsFound}, Escaneados={result.Statistics.ItemsScanned}, CorrelationId={_correlationId}");
            progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = result.Message ?? "Scan concluído." });
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_SysMain_ScanDisabled") ?? "Verificação concluída.", SmartRepairEventType.OperationFinished);
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
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_SysMain_ExecStarting") ?? "Configurando SysMain...", SmartRepairEventType.OperationStarted);

            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                var info = simResult.ItemsToProcess.Count > 0 ? simResult.ItemsToProcess[0] as SysMainInfo : null;
                if (info == null)
                {
                    result.Success = false;
                    result.Message = LocalizationService.Instance.GetString("ScanNoData");
                    return;
                }

                if (SystemInformationHelper.IsEnterpriseEdition())
                {
                    string msg = Services.LocalizationService.Instance.GetString("SmartRepair_Module_SysMain_SkippedEnterprise") ?? "Edição Enterprise. Operação ignorada.";
                    _eventBus.PublishMessage(_correlationId, ModuleId, msg, SmartRepairEventType.ProgressChanged);
                    progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = msg });
                    result.Message = msg;
                    return;
                }

                if (SystemInformationHelper.IsOnBattery())
                {
                    string msg = Services.LocalizationService.Instance.GetString("SmartRepair_Module_SysMain_SkippedBattery") ?? "Notebook em bateria. Operação ignorada.";
                    _eventBus.PublishMessage(_correlationId, ModuleId, msg, SmartRepairEventType.ProgressChanged);
                    progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = msg });
                    result.Message = msg;
                    return;
                }

                if (info.TotalRamMb < 8192)
                {
                    string msg = Services.LocalizationService.Instance.GetString("SmartRepair_Module_SysMain_SkippedLowRam") ?? "RAM inferior a 8GB. Operação ignorada.";
                    _eventBus.PublishMessage(_correlationId, ModuleId, msg, SmartRepairEventType.ProgressChanged);
                    progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = msg });
                    result.Message = msg;
                    return;
                }

                if (info.TotalRamMb >= 8192 && info.TotalRamMb < 16384 && !info.HasSsd)
                {
                    string msg = Services.LocalizationService.Instance.GetString("SmartRepair_Module_SysMain_SkippedNoSsd") ?? "8-16GB RAM sem SSD. Operação ignorada.";
                    _eventBus.PublishMessage(_correlationId, ModuleId, msg, SmartRepairEventType.ProgressChanged);
                    progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = msg });
                    result.Message = msg;
                    return;
                }

                if (!info.HasSsd && info.TotalRamMb < 16384)
                {
                    string msg = Services.LocalizationService.Instance.GetString("SmartRepair_Module_SysMain_SkippedNoSsd") ?? "Sem SSD detectado. Operação ignorada.";
                    _eventBus.PublishMessage(_correlationId, ModuleId, msg, SmartRepairEventType.ProgressChanged);
                    progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = msg });
                    result.Message = msg;
                    return;
                }

                progress?.Report(new RepairProgress { StepPercent = 30, StatusMessage = "Desabilitando serviço SysMain..." });
                _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_SysMain_ExecDisabling") ?? "Desabilitando SysMain...", SmartRepairEventType.ProgressChanged);

                _logger.LogDebug($"[{ModuleId}] Executando: sc config SysMain start= disabled");
                if (!ProcessHelper.RunSc("config SysMain start= disabled", out _, out _))
                {
                    _logger.LogError($"[{ModuleId}] Falha ao configurar SysMain como disabled");
                }

                _logger.LogDebug($"[{ModuleId}] Executando: sc stop SysMain");
                if (!ProcessHelper.RunSc("stop SysMain", out _, out _))
                {
                    _logger.LogWarning($"[{ModuleId}] SysMain pode já estar parado");
                }

                result.FinalStatistics.ItemsRepaired = 1;
                progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = "SysMain configurado." });
                _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_SysMain_ExecDone") ?? "SysMain desabilitado com sucesso.", SmartRepairEventType.OperationFinished);
            }, ct);

            _logger.LogInfo($"[{ModuleId}] Execução concluída. Repaired={result.FinalStatistics.ItemsRepaired}, Errors={result.FinalStatistics.ErrorCount}, Space={result.FinalStatistics.SpaceRecoveredBytes}B, CorrelationId={_correlationId}");
            return result;
        }

        public async Task<ModuleRollbackResult> RollbackAsync(ModuleExecutionResult execResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleRollbackResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_SysMain_RollbackStarting") ?? "Restaurando SysMain...", SmartRepairEventType.RollbackStarted);

            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(new RepairProgress { StepPercent = 50, StatusMessage = "Reabilitando SysMain..." });

                _logger.LogDebug($"[{ModuleId}] Executando: sc config SysMain start= auto");
                if (!ProcessHelper.RunSc("config SysMain start= auto", out _, out _))
                {
                    _logger.LogError($"[{ModuleId}] Falha ao reabilitar SysMain");
                }

                _logger.LogDebug($"[{ModuleId}] Executando: sc start SysMain");
                if (!ProcessHelper.RunSc("start SysMain", out _, out _))
                {
                    _logger.LogWarning($"[{ModuleId}] SysMain pode não ter iniciado");
                }

                result.ItemsRestored = 1;
                progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = "SysMain restaurado." });
                _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_SysMain_RollbackDone") ?? "SysMain reabilitado com sucesso.", SmartRepairEventType.RollbackFinished);
            }, ct);

            _logger.LogInfo($"[{ModuleId}] Rollback concluído. Restored={result.ItemsRestored}, Failed={result.ItemsFailedToRestore}, CorrelationId={_correlationId}");
            progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = "Rollback concluído." });
            return result;
        }
    }
}
