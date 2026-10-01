using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services.SmartRepair.Architecture;

namespace VoltrisOptimizer.Services.SmartRepair.Modules
{
    public class ArpNetBiosResetModule : ISmartRepairModule
    {
        private readonly ILoggingService _logger;
        private readonly ISmartRepairEventBus _eventBus;
        private readonly string _correlationId;

        public string ModuleId => "ArpNetBiosReset";
        public string Category => "Network";
        public string Name => Services.LocalizationService.Instance.GetString("SmartRepair_Module_ArpReset_Name") ?? "Limpar Cache ARP e NetBIOS";
        public string Description => Services.LocalizationService.Instance.GetString("SmartRepair_Module_ArpReset_Desc") ?? "Limpa os caches ARP e NetBIOS para resolver problemas de conectividade de rede.";

        public ModuleCapabilities Capabilities { get; } = new ModuleCapabilities
        {
            SupportsScan = true, SupportsSimulation = true, SupportsExecution = true, SupportsRollback = false,
            SupportsParallelism = false, HasDestructiveOperations = false, MaxRiskLevel = RiskLevel.Safe,
            RequiresAdmin = true
        };

        public IReadOnlyList<string> Dependencies => Array.Empty<string>();

        public ArpNetBiosResetModule(ILoggingService logger, ISmartRepairEventBus eventBus, string correlationId)
        {
            _logger = logger; _eventBus = eventBus; _correlationId = correlationId;
        }

        public Task<ModuleScanResult> ScanAsync(IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleScanResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_ArpReset_ScanStarting") ?? "Verificando caches de rede...", SmartRepairEventType.OperationStarted);
            progress?.Report(new RepairProgress { StepPercent = 50, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_ArpReset_ScanStarting") ?? "Verificando caches de rede..." });

            result.Statistics.ItemsFound = 1;
            result.FoundItems.Add("ArpNetBios");
            result.Message = Services.LocalizationService.Instance.GetString("SmartRepair_Module_ArpReset_ScanResult") ?? "Caches ARP e NetBIOS serão limpos como etapa final de rede.";
            _logger.LogInfo($"[{ModuleId}] Scan concluído. Encontrados={result.Statistics.ItemsFound}, Escaneados={result.Statistics.ItemsScanned}, CorrelationId={_correlationId}");
            progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = result.Message ?? "Scan concluído." });
            _eventBus.PublishMessage(_correlationId, ModuleId, result.Message, SmartRepairEventType.OperationFinished);
            return Task.FromResult(result);
        }

        public Task<ModuleSimulationResult> SimulateAsync(ModuleScanResult scanResult, CancellationToken ct)
        {
            var sim = new ModuleSimulationResult();
            if (scanResult.Statistics.ItemsFound > 0)
            {
                sim.EstimatedStatistics.ItemsFound = 1;
                sim.ItemsToProcess.Add("ArpNetBios");
            }
            sim.RiskLevel = RiskLevel.Safe;
            _logger.LogInfo($"[{ModuleId}] Simulação concluída. Itens a processar={sim.ItemsToProcess.Count}, RiskLevel={sim.RiskLevel}, CorrelationId={_correlationId}");
            return Task.FromResult(sim);
        }

        public async Task<ModuleExecutionResult> ExecuteAsync(ModuleSimulationResult simResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleExecutionResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_ArpReset_ExecStarting") ?? "Limpando caches ARP e NetBIOS...", SmartRepairEventType.OperationStarted);
            progress?.Report(new RepairProgress { StepPercent = 10, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_ArpReset_ExecStarting") ?? "Limpando caches..." });

            try
            {
                progress?.Report(new RepairProgress { StepPercent = 30, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_ArpReset_ExecProgress") ?? "Limpando cache ARP..." });
                _logger.LogDebug($"[{ModuleId}] Executando: arp -d *");
                await ProcessHelper.RunAsync("arp", "-d *", ct);

                progress?.Report(new RepairProgress { StepPercent = 50, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_ArpReset_ExecProgress") ?? "Limpando cache NetBIOS..." });
                _logger.LogDebug($"[{ModuleId}] Executando: nbtstat -R (limpar cache NetBIOS)");
                await ProcessHelper.RunAsync("nbtstat", "-R", ct);

                progress?.Report(new RepairProgress { StepPercent = 70, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_ArpReset_ExecProgress") ?? "Re-registrando NetBIOS..." });
                _logger.LogDebug($"[{ModuleId}] Executando: nbtstat -RR (re-registrar NetBIOS)");
                await ProcessHelper.RunAsync("nbtstat", "-RR", ct);

                progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_ArpReset_ExecDone") ?? "Caches ARP e NetBIOS limpos." });

                result.FinalStatistics.ItemsRepaired = 1;
                result.Message = Services.LocalizationService.Instance.GetString("SmartRepair_Module_ArpReset_ExecDone") ?? "Caches ARP e NetBIOS limpos com sucesso. Pequena perda de conectividade pode ocorrer.";
                _eventBus.PublishMessage(_correlationId, ModuleId, result.Message, SmartRepairEventType.OperationFinished);
                _logger.LogInfo($"[{ModuleId}] Execução concluída. Repaired={result.FinalStatistics.ItemsRepaired}, Errors={result.FinalStatistics.ErrorCount}, Space={result.FinalStatistics.SpaceRecoveredBytes}B, CorrelationId={_correlationId}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ArpNetBiosResetModule] Falha ao limpar caches", ex);
                result.Success = false;
                result.Message = ex.Message;
            }

            return result;
        }

        public Task<ModuleRollbackResult> RollbackAsync(ModuleExecutionResult execResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleRollbackResult
            {
                Success = true,
                ItemsRestored = 0,
                Message = Services.LocalizationService.Instance.GetString("SmartRepair_Module_ArpReset_NoRollback") ?? "Rollback não necessário — caches ARP/NETBIOS se repopulam automaticamente."
            };
            _eventBus.PublishMessage(_correlationId, ModuleId, result.Message, SmartRepairEventType.RollbackFinished);
            _logger.LogInfo($"[{ModuleId}] Rollback concluído. Restored={result.ItemsRestored}, Failed={result.ItemsFailedToRestore}, CorrelationId={_correlationId}");
            progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = "Rollback concluído." });
            return Task.FromResult(result);
        }
    }
}
