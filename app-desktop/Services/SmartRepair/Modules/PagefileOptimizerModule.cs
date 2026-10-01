using System;
using System.Collections.Generic;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services.SmartRepair.Architecture;

namespace VoltrisOptimizer.Services.SmartRepair.Modules
{
    public class PagefileOptimizerModule : ISmartRepairModule
    {
        private readonly ILoggingService _logger;
        private readonly ISmartRepairEventBus _eventBus;
        private readonly string _correlationId;

        public string ModuleId => "PagefileOptimizer";
        public string Category => "System";

        public string Name => Services.LocalizationService.Instance.GetString("SmartRepair_Module_Pagefile_Name") ?? "Pagefile Optimizer";
        public string Description => Services.LocalizationService.Instance.GetString("SmartRepair_Module_Pagefile_Desc") ?? "Otimiza o tamanho do arquivo de paginação com base na RAM disponível.";

        public ModuleCapabilities Capabilities { get; } = new ModuleCapabilities
        {
            SupportsScan = true, SupportsSimulation = true, SupportsExecution = true, SupportsRollback = true,
            SupportsParallelism = false, HasDestructiveOperations = false, MaxRiskLevel = RiskLevel.Moderate,
            RequiresAdmin = true
        };

        public IReadOnlyList<string> Dependencies => Array.Empty<string>();

        private class PagefileInfo
        {
            public ulong TotalRamMb { get; set; }
            public bool IsManaged { get; set; }
            public ulong CurrentInitialMb { get; set; }
            public ulong CurrentMaxMb { get; set; }
            public ulong CommitChargePeakMb { get; set; }
            public bool IsGamingMachine { get; set; }
        }

        public PagefileOptimizerModule(ILoggingService logger, ISmartRepairEventBus eventBus, string correlationId)
        {
            _logger = logger; _eventBus = eventBus; _correlationId = correlationId;
        }

        public async Task<ModuleScanResult> ScanAsync(IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleScanResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Pagefile_ScanStarting") ?? "Verificando configuração do pagefile...", SmartRepairEventType.OperationStarted);

            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    _logger.LogDebug($"[{ModuleId}] Executando consulta WMI...");
                    ulong totalRamMb = 0;
                    using var memSearcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem");
                    foreach (ManagementObject obj in memSearcher.Get())
                    {
                using var __dispose_obj = obj;
                        totalRamMb = Convert.ToUInt64(obj["TotalVisibleMemorySize"] ?? 0) / 1024;
                        break;
                    }

                    ulong initialMb = 0, maxMb = 0;
                    bool isManaged = true;
                    _logger.LogDebug($"[{ModuleId}] Executando consulta WMI...");
                    using var pfSearcher = new ManagementObjectSearcher("SELECT InitialSize, MaximumSize FROM Win32_PageFileSetting");
                    foreach (ManagementObject pf in pfSearcher.Get())
                    {
                using var __dispose_pf = pf;
                        isManaged = false;
                        initialMb = Convert.ToUInt64(pf["InitialSize"] ?? 0);
                        maxMb = Convert.ToUInt64(pf["MaximumSize"] ?? 0);
                        break;
                    }

                    ulong commitPeakMb = 0;
                    _logger.LogDebug($"[{ModuleId}] Executando consulta WMI...");
                    using var csSearcher = new ManagementObjectSearcher("SELECT TotalVirtualMemorySize, FreeVirtualMemory FROM Win32_OperatingSystem");
                    foreach (ManagementObject cs in csSearcher.Get())
                    {
                using var __dispose_cs = cs;
                        ulong totalVirtual = Convert.ToUInt64(cs["TotalVirtualMemorySize"] ?? 0);
                        ulong freeVirtual = Convert.ToUInt64(cs["FreeVirtualMemory"] ?? 0);
                        commitPeakMb = (totalVirtual - freeVirtual) / 1024;
                        break;
                    }

                    bool isGaming = App.GameDetectionService?.HasActiveRunningGameSession == true;

                    result.FoundItems.Add(new PagefileInfo
                    {
                        TotalRamMb = totalRamMb,
                        IsManaged = isManaged,
                        CurrentInitialMb = initialMb,
                        CurrentMaxMb = maxMb,
                        CommitChargePeakMb = commitPeakMb,
                        IsGamingMachine = isGaming
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
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Pagefile_ScanResult") ?? "Configuração do pagefile verificada.", SmartRepairEventType.OperationFinished);
            return result;
        }

        public Task<ModuleSimulationResult> SimulateAsync(ModuleScanResult scanResult, CancellationToken ct)
        {
            var sim = new ModuleSimulationResult();
            sim.RiskLevel = RiskLevel.Moderate;
            sim.EstimatedStatistics.ItemsFound = scanResult.FoundItems.Count;
            foreach (var item in scanResult.FoundItems)
                sim.ItemsToProcess.Add(item);
            _logger.LogInfo($"[{ModuleId}] Simulação concluída. Itens a processar={sim.ItemsToProcess.Count}, RiskLevel={sim.RiskLevel}, CorrelationId={_correlationId}");
            return Task.FromResult(sim);
        }

        public async Task<ModuleExecutionResult> ExecuteAsync(ModuleSimulationResult simResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleExecutionResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Pagefile_ExecStarting") ?? "Otimizando pagefile...", SmartRepairEventType.OperationStarted);

            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                var info = simResult.ItemsToProcess.Count > 0 ? simResult.ItemsToProcess[0] as PagefileInfo : null;
                if (info == null)
                {
                    result.Success = false;
                    result.Message = LocalizationService.Instance.GetString("ScanNoData");
                    return;
                }

                if (info.IsManaged && info.TotalRamMb < 4096)
                {
                    string msg = Services.LocalizationService.Instance.GetString("SmartRepair_Module_Pagefile_SkippedManaged") ?? "Gerenciado pelo sistema. Mantendo configuração atual.";
                    _eventBus.PublishMessage(_correlationId, ModuleId, msg, SmartRepairEventType.ProgressChanged);
                    progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = msg });
                    result.Message = msg;
                    return;
                }

                ulong initialMb, maxMb;
                bool systemManaged = false;

                if (info.TotalRamMb < 4096)
                {
                    systemManaged = true;
                    initialMb = 0; maxMb = 0;
                }
                else if (info.TotalRamMb < 8192)
                {
                    initialMb = 4096; maxMb = 8192;
                }
                else if (info.TotalRamMb < 16384)
                {
                    initialMb = 4096; maxMb = 12288;
                }
                else if (info.TotalRamMb < 32768)
                {
                    initialMb = 2048; maxMb = 16384;
                }
                else if (info.TotalRamMb < 65536)
                {
                    initialMb = 2048; maxMb = 8192;
                }
                else
                {
                    systemManaged = true;
                    initialMb = 0; maxMb = 0;
                }

                if (info.IsGamingMachine && !systemManaged)
                {
                    maxMb *= 2;
                }

                if (SystemInformationHelper.IsOnBattery() && !systemManaged)
                {
                    initialMb = (ulong)(initialMb * 0.75);
                    maxMb = (ulong)(maxMb * 0.75);
                }

                if (!systemManaged)
                {
                    ulong minRequired = info.CommitChargePeakMb + 2048;
                    if (maxMb < minRequired)
                        maxMb = minRequired;
                }

                progress?.Report(new RepairProgress { StepPercent = 30, StatusMessage = "Aplicando nova configuração do pagefile..." });
                _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Pagefile_ExecProgress") ?? "Redimensionando pagefile...", SmartRepairEventType.ProgressChanged);

                try
                {
                    if (systemManaged)
                    {
                        using var cs = new ManagementObject("Win32_ComputerSystem.Name='" + Environment.MachineName + "'");
                        object? p = cs.GetPropertyValue("TotalPhysicalMemory");
                        cs["AutomaticManagedPagefile"] = true;
                        cs.Put();
                    }
                    else
                    {
                        using var cs = new ManagementObject("Win32_ComputerSystem.Name='" + Environment.MachineName + "'");
                        cs["AutomaticManagedPagefile"] = false;
                        cs.Put();

                        using var pfSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_PageFileSetting");
                        bool found = false;
                        foreach (ManagementObject pf in pfSearcher.Get())
                        {
                using var __dispose_pf = pf;
                            pf["InitialSize"] = initialMb;
                            pf["MaximumSize"] = maxMb;
                            pf.Put();
                            found = true;
                            break;
                        }

                        if (!found)
                        {
                            using var pfClass = new ManagementClass("Win32_PageFileSetting");
                            ManagementObject newPf = pfClass.CreateInstance();
                            newPf["Name"] = @"C:\pagefile.sys";
                            newPf["InitialSize"] = initialMb;
                            newPf["MaximumSize"] = maxMb;
                            newPf.Put();
                        }
                    }

                    result.FinalStatistics.ItemsRepaired = 1;
                    progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = "Pagefile otimizado." });
                    _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Pagefile_ExecDone") ?? "Pagefile otimizado com sucesso.", SmartRepairEventType.OperationFinished);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[{ModuleId}] Erro ao configurar pagefile", ex);
                    result.Success = false;
                    result.Message = ex.Message;
                }
            }, ct);

            _logger.LogInfo($"[{ModuleId}] Execução concluída. Repaired={result.FinalStatistics.ItemsRepaired}, Errors={result.FinalStatistics.ErrorCount}, Space={result.FinalStatistics.SpaceRecoveredBytes}B, CorrelationId={_correlationId}");
            return result;
        }

        public async Task<ModuleRollbackResult> RollbackAsync(ModuleExecutionResult execResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleRollbackResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Pagefile_RollbackStarting") ?? "Restaurando pagefile para gerenciado pelo sistema...", SmartRepairEventType.RollbackStarted);

            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(new RepairProgress { StepPercent = 50, StatusMessage = "Restaurando configuração padrão..." });

                try
                {
                    using var cs = new ManagementObject("Win32_ComputerSystem.Name='" + Environment.MachineName + "'");
                    cs["AutomaticManagedPagefile"] = true;
                    cs.Put();
                    result.ItemsRestored = 1;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[{ModuleId}] Erro no rollback do pagefile", ex);
                    result.ItemsFailedToRestore = 1;
                    result.Success = false;
                    result.Message = ex.Message;
                }

                progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = "Pagefile restaurado." });
                _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Pagefile_RollbackDone") ?? "Pagefile restaurado para gerenciado pelo sistema.", SmartRepairEventType.RollbackFinished);
            }, ct);

            _logger.LogInfo($"[{ModuleId}] Rollback concluído. Restored={result.ItemsRestored}, Failed={result.ItemsFailedToRestore}, CorrelationId={_correlationId}");
            progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = "Rollback concluído." });
            return result;
        }
    }
}
