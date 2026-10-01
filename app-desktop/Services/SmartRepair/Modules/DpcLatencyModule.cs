using System;
using System.Collections.Generic;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.SmartRepair.Architecture;

namespace VoltrisOptimizer.Services.SmartRepair.Modules
{
    public class DpcLatencyModule : ISmartRepairModule
    {
        private readonly ILoggingService _logger;
        private readonly ISmartRepairEventBus _eventBus;
        private readonly string _correlationId;

        public string ModuleId => "DpcLatency";
        public string Category => "CPU";

        public string Name => Services.LocalizationService.Instance.GetString("SmartRepair_Module_DpcLatency_Name") ?? "DPC Latency Analyzer";
        public string Description => Services.LocalizationService.Instance.GetString("SmartRepair_Module_DpcLatency_Desc") ?? "Analisa latência de DPC e ISR do sistema para diagnóstico de desempenho.";

        public ModuleCapabilities Capabilities { get; } = new ModuleCapabilities
        {
            SupportsScan = true,
            SupportsSimulation = false,
            SupportsExecution = false,
            SupportsRollback = false,
            SupportsParallelism = false,
            HasDestructiveOperations = false,
            MaxRiskLevel = RiskLevel.Safe,
            RequiresAdmin = false
        };

        public IReadOnlyList<string> Dependencies => Array.Empty<string>();

        public DpcLatencyModule(ILoggingService logger, ISmartRepairEventBus eventBus, string correlationId)
        {
            _logger = logger;
            _eventBus = eventBus;
            _correlationId = correlationId;
        }

        public Task<ModuleScanResult> ScanAsync(IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleScanResult();
            _eventBus.PublishMessage(_correlationId, ModuleId,
                Services.LocalizationService.Instance.GetString("SmartRepair_Module_DpcLatency_ScanStarting") ?? "Coletando informações de DPC e interrupções do sistema...",
                SmartRepairEventType.OperationStarted);

            var report = new DpcReport();

            try
            {
                _logger.LogDebug($"[{ModuleId}] Executando consulta WMI...");
                using var osSearcher = new ManagementObjectSearcher("SELECT LastBootUpTime, TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
                foreach (ManagementObject obj in osSearcher.Get())
                {
                using var __dispose_obj = obj;
                    string? bootTime = obj["LastBootUpTime"]?.ToString();
                    if (!string.IsNullOrEmpty(bootTime))
                    {
                        if (ManagementDateTimeConverter.ToDateTime(bootTime) is DateTime dt)
                        {
                            report.Uptime = DateTime.Now - dt;
                        }
                    }
                    break;
                }

                _logger.LogDebug($"[{ModuleId}] Executando consulta WMI...");
                using var perfSearcher = new ManagementObjectSearcher("SELECT Name, PercentProcessorTime, InterruptsPerSec, DPCsQueuedPerSec FROM Win32_PerfFormattedData_Counters_ProcessorInformation WHERE Name = '_Total'");
                foreach (ManagementObject obj in perfSearcher.Get())
                {
                using var __dispose_obj = obj;
                    report.InterruptsPerSec = obj["InterruptsPerSec"]?.ToString() ?? "N/A";
                    report.DpcQueuedPerSec = obj["DPCsQueuedPerSec"]?.ToString() ?? "N/A";
                    break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[{ModuleId}] WMI query falhou: {ex.Message}");
            }

            result.FoundItems.Add(report);
            result.Statistics.ItemsFound = 1;
            result.Statistics.ItemsScanned = 1;

            _logger.LogInfo($"[{ModuleId}] Scan concluído. Encontrados={result.Statistics.ItemsFound}, Escaneados={result.Statistics.ItemsScanned}, CorrelationId={_correlationId}");
            progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = result.Message ?? "Scan concluído." });
            _eventBus.PublishMessage(_correlationId, ModuleId,
                string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_DpcLatency_ScanResult") ?? "Diagnóstico DPC concluído. Uptime: {0:dd\\.hh\\:mm\\:ss}, Interrupções/s: {1}, DPC/s: {2}.",
                    report.Uptime, report.InterruptsPerSec, report.DpcQueuedPerSec),
                SmartRepairEventType.OperationFinished);
            return Task.FromResult(result);
        }

        public Task<ModuleSimulationResult> SimulateAsync(ModuleScanResult scanResult, CancellationToken ct)
        {
            var sim = new ModuleSimulationResult();
            sim.RiskLevel = RiskLevel.Safe;
            foreach (var item in scanResult.FoundItems)
                sim.ItemsToProcess.Add(item);
            sim.EstimatedStatistics.ItemsFound = scanResult.FoundItems.Count;
            _logger.LogInfo($"[{ModuleId}] Simulação concluída. Itens a processar={sim.ItemsToProcess.Count}, RiskLevel={sim.RiskLevel}, CorrelationId={_correlationId}");
            return Task.FromResult(sim);
        }

        public Task<ModuleExecutionResult> ExecuteAsync(ModuleSimulationResult simResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            throw new NotSupportedException(Services.LocalizationService.Instance.GetString("SmartRepair_Module_DpcLatency_NoExecute") ?? "DPC Latency é apenas um módulo de diagnóstico. Nenhuma execução disponível.");
        }

        public Task<ModuleRollbackResult> RollbackAsync(ModuleExecutionResult execResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            throw new NotSupportedException(Services.LocalizationService.Instance.GetString("SmartRepair_Module_DpcLatency_NoRollback") ?? "DPC Latency é apenas um módulo de diagnóstico. Nenhum rollback disponível.");
        }

        private class DpcReport
        {
            public TimeSpan Uptime { get; set; }
            public string InterruptsPerSec { get; set; } = "N/A";
            public string DpcQueuedPerSec { get; set; } = "N/A";
        }
    }
}
