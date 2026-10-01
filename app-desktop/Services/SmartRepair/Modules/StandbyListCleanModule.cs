using System;
using System.Collections.Generic;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.SmartRepair.Architecture;

namespace VoltrisOptimizer.Services.SmartRepair.Modules
{
    public class StandbyListCleanModule : ISmartRepairModule
    {
        private readonly ILoggingService _logger;
        private readonly ISmartRepairEventBus _eventBus;
        private readonly string _correlationId;

        public string ModuleId => "StandbyListClean";
        public string Category => "Memory";

        public string Name => Services.LocalizationService.Instance.GetString("SmartRepair_Module_StandbyList_Name") ?? "Standby List Clean";
        public string Description => Services.LocalizationService.Instance.GetString("SmartRepair_Module_StandbyList_Desc") ?? "Purga automaticamente a lista de standby da RAM quando necessário.";

        public ModuleCapabilities Capabilities { get; } = new ModuleCapabilities
        {
            SupportsScan = true, SupportsSimulation = true, SupportsExecution = true, SupportsRollback = true,
            SupportsParallelism = false, HasDestructiveOperations = false, MaxRiskLevel = RiskLevel.Safe,
            RequiresAdmin = true
        };

        public IReadOnlyList<string> Dependencies => Array.Empty<string>();

        [DllImport("ntdll.dll", SetLastError = true)]
        static extern int NtSetSystemInformation(int infoClass, IntPtr info, int length);

        private const int SystemMemoryListPurge = 0x50;

        private class RamStats
        {
            public ulong TotalBytes { get; set; }
            public ulong FreeBytes { get; set; }
            public double FreePercent => TotalBytes > 0 ? (double)FreeBytes / TotalBytes * 100.0 : 0;
        }

        public StandbyListCleanModule(ILoggingService logger, ISmartRepairEventBus eventBus, string correlationId)
        {
            _logger = logger; _eventBus = eventBus; _correlationId = correlationId;
        }

        public async Task<ModuleScanResult> ScanAsync(IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleScanResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_StandbyList_ScanStarting") ?? "Verificando estatísticas de memória RAM...", SmartRepairEventType.OperationStarted);

            await Task.Run(() =>
            {
                try
                {
                    _logger.LogDebug($"[{ModuleId}] Executando consulta WMI...");
                    using var searcher = new ManagementObjectSearcher("SELECT FreePhysicalMemory, TotalVisibleMemorySize FROM Win32_OperatingSystem");
                    foreach (ManagementObject obj in searcher.Get())
                    {
                using var __dispose_obj = obj;
                        var totalKb = (ulong)(obj["TotalVisibleMemorySize"] ?? 0);
                        var freeKb = (ulong)(obj["FreePhysicalMemory"] ?? 0);
                        var stats = new RamStats { TotalBytes = totalKb * 1024, FreeBytes = freeKb * 1024 };
                        result.FoundItems.Add(stats);
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[{ModuleId}] Erro ao consultar WMI", ex);
                }
            }, ct);

            result.Statistics.ItemsFound = result.FoundItems.Count;
            result.Statistics.ItemsScanned = result.FoundItems.Count;

            _logger.LogInfo($"[{ModuleId}] Scan concluído. Encontrados={result.Statistics.ItemsFound}, Escaneados={result.Statistics.ItemsScanned}, CorrelationId={_correlationId}");
            progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = result.Message ?? "Scan concluído." });
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_StandbyList_ScanResult") ?? "Estatísticas de RAM coletadas.", SmartRepairEventType.OperationFinished);
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
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_StandbyList_ExecStarting") ?? "Iniciando purga da lista de standby...", SmartRepairEventType.OperationStarted);

            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                var stats = simResult.ItemsToProcess.Count > 0 ? simResult.ItemsToProcess[0] as RamStats : null;
                if (stats == null)
                {
                    result.Success = false;
                    result.Message = LocalizationService.Instance.GetString("MemoryNoData");
                    return;
                }

                ulong totalMb = stats.TotalBytes / (1024 * 1024);
                ulong freeMb = stats.FreeBytes / (1024 * 1024);
                double freePct = stats.FreePercent;

                if (totalMb < 4096)
                {
                    string msg = Services.LocalizationService.Instance.GetString("SmartRepair_Module_StandbyList_SkippedLowRam") ?? "RAM total inferior a 4GB. Operação ignorada.";
                    _eventBus.PublishMessage(_correlationId, ModuleId, msg, SmartRepairEventType.ProgressChanged);
                    progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = msg });
                    result.Message = msg;
                    return;
                }

                if (App.GameDetectionService?.HasActiveRunningGameSession == true)
                {
                    string msg = Services.LocalizationService.Instance.GetString("SmartRepair_Module_StandbyList_SkippedGame") ?? "Jogo em execução. Operação ignorada.";
                    _eventBus.PublishMessage(_correlationId, ModuleId, msg, SmartRepairEventType.ProgressChanged);
                    progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = msg });
                    result.Message = msg;
                    return;
                }

                if (SystemInformationHelper.IsOnBattery())
                {
                    string msg = Services.LocalizationService.Instance.GetString("SmartRepair_Module_StandbyList_SkippedBattery") ?? "Notebook em bateria. Operação ignorada.";
                    _eventBus.PublishMessage(_correlationId, ModuleId, msg, SmartRepairEventType.ProgressChanged);
                    progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = msg });
                    result.Message = msg;
                    return;
                }

                if (SystemInformationHelper.IsEnterpriseEdition())
                {
                    string msg = "Edição Enterprise detectada. Operação ignorada.";
                    _eventBus.PublishMessage(_correlationId, ModuleId, msg, SmartRepairEventType.ProgressChanged);
                    progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = msg });
                    result.Message = msg;
                    return;
                }

                if (freePct >= 20.0)
                {
                    string msg = $"Memória livre suficiente ({freePct:F1}%). Purga não necessária.";
                    _eventBus.PublishMessage(_correlationId, ModuleId, msg, SmartRepairEventType.ProgressChanged);
                    progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = msg });
                    result.Message = msg;
                    result.FinalStatistics.ItemsIgnored++;
                    return;
                }

                progress?.Report(new RepairProgress { StepPercent = 50, StatusMessage = "Purgando lista de standby..." });
                _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_StandbyList_ExecProgress") ?? "Limpando lista de standby via NtSetSystemInformation...", SmartRepairEventType.ProgressChanged);

                int ret = NtSetSystemInformation(SystemMemoryListPurge, IntPtr.Zero, 0);
                if (ret == 0)
                {
                    result.FinalStatistics.ItemsRepaired = 1;
                    _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_StandbyList_Done") ?? "Lista de standby purgada com sucesso.", SmartRepairEventType.OperationFinished);
                }
                else
                {
                    result.Success = false;
                    result.Message = $"NtSetSystemInformation falhou (código: {ret})";
                    _logger.LogError($"[{ModuleId}] NtSetSystemInformation retornou {ret}");
                }

                progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = result.Message });

            }, ct);

            _logger.LogInfo($"[{ModuleId}] Execução concluída. Repaired={result.FinalStatistics.ItemsRepaired}, Errors={result.FinalStatistics.ErrorCount}, Space={result.FinalStatistics.SpaceRecoveredBytes}B, CorrelationId={_correlationId}");
            return result;
        }

        public Task<ModuleRollbackResult> RollbackAsync(ModuleExecutionResult execResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            string msg = Services.LocalizationService.Instance.GetString("SmartRepair_Module_StandbyList_NoRollback") ?? "Standby List re-popula naturalmente. Rollback não necessário.";
            _eventBus.PublishMessage(_correlationId, ModuleId, msg, SmartRepairEventType.ProgressChanged);
            var rolResult = new ModuleRollbackResult { Success = true, Message = msg };
            _logger.LogInfo($"[{ModuleId}] Rollback concluído. Restored={rolResult.ItemsRestored}, Failed={rolResult.ItemsFailedToRestore}, CorrelationId={_correlationId}");
            progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = "Rollback concluído." });
            return Task.FromResult(rolResult);
        }
    }

    internal static class SystemInformationHelper
    {
        public static bool IsOnBattery()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT BatteryStatus FROM Win32_Battery");
                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    int status = Convert.ToInt32(obj["BatteryStatus"]);
                    return status == 1;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StandbyListClean] Erro ao verificar bateria: {ex.Message}");
            }
            return false;
        }

        public static bool IsEnterpriseEdition()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Caption FROM Win32_OperatingSystem");
                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    string? caption = obj["Caption"]?.ToString();
                    if (caption != null && caption.Contains("Enterprise", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StandbyListClean] Erro ao verificar edição Windows: {ex.Message}");
            }
            return false;
        }
    }
}
