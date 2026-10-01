using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services.SmartRepair.Architecture;

namespace VoltrisOptimizer.Services.SmartRepair.Modules
{
    public class NetPowerSavingModule : ISmartRepairModule
    {
        private readonly ILoggingService _logger;
        private readonly ISmartRepairEventBus _eventBus;
        private readonly string _correlationId;

        public string ModuleId => "NetPowerSaving";
        public string Category => "Network";
        public string Name => Services.LocalizationService.Instance.GetString("SmartRepair_Module_NetPower_Name") ?? "Desabilitar Economia de Energia da Rede";
        public string Description => Services.LocalizationService.Instance.GetString("SmartRepair_Module_NetPower_Desc") ?? "Desabilita o gerenciamento de energia em adaptadores Ethernet para evitar desconexões.";

        public ModuleCapabilities Capabilities { get; } = new ModuleCapabilities
        {
            SupportsScan = true, SupportsSimulation = true, SupportsExecution = true, SupportsRollback = true,
            SupportsParallelism = false, HasDestructiveOperations = false, MaxRiskLevel = RiskLevel.Safe,
            RequiresAdmin = true
        };

        public IReadOnlyList<string> Dependencies => Array.Empty<string>();

        public NetPowerSavingModule(ILoggingService logger, ISmartRepairEventBus eventBus, string correlationId)
        {
            _logger = logger; _eventBus = eventBus; _correlationId = correlationId;
        }

        public Task<ModuleScanResult> ScanAsync(IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleScanResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_NetPower_ScanStarting") ?? "Verificando adaptadores de rede e energia...", SmartRepairEventType.OperationStarted);
            progress?.Report(new RepairProgress { StepPercent = 30, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_NetPower_ScanStarting") ?? "Verificando adaptadores de rede e energia..." });

            _logger.LogDebug($"[{ModuleId}] Consultando status da bateria via WMI...");
            var (hasBattery, onBattery) = GetBatteryStatus();

            if (hasBattery && onBattery)
            {
                result.Statistics.ItemsIgnored = 1;
                result.Message = Services.LocalizationService.Instance.GetString("SmartRepair_Module_NetPower_SkippedBattery") ?? "Notebook em bateria. Mantendo economia de energia para preservar carga.";
                _eventBus.PublishMessage(_correlationId, ModuleId, result.Message, SmartRepairEventType.OperationFinished);
                return Task.FromResult(result);
            }

            var ethernetAdapters = GetActiveEthernetAdapters();

            if (ethernetAdapters.Count == 0)
            {
                result.Statistics.ItemsIgnored = 1;
                result.Message = Services.LocalizationService.Instance.GetString("SmartRepair_Module_NetPower_SkippedWifi") ?? "Nenhum adaptador Ethernet ativo encontrado. Apenas WiFi detectado.";
                _eventBus.PublishMessage(_correlationId, ModuleId, result.Message, SmartRepairEventType.OperationFinished);
                return Task.FromResult(result);
            }

            result.Statistics.ItemsFound = ethernetAdapters.Count;
            result.FoundItems.AddRange(ethernetAdapters);
            result.Message = Services.LocalizationService.Instance.GetString("SmartRepair_Module_NetPower_ScanResult") ?? $"{ethernetAdapters.Count} adaptador(es) Ethernet encontrado(s).";
            _logger.LogInfo($"[{ModuleId}] Scan concluído. Encontrados={result.Statistics.ItemsFound}, Escaneados={result.Statistics.ItemsScanned}, CorrelationId={_correlationId}");
            progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = result.Message ?? "Scan concluído." });
            _eventBus.PublishMessage(_correlationId, ModuleId, result.Message, SmartRepairEventType.OperationFinished);
            return Task.FromResult(result);
        }

        private static (bool HasBattery, bool OnBattery) GetBatteryStatus()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT BatteryStatus FROM Win32_Battery");
                var list = searcher.Get().Cast<ManagementObject>().ToList();
                if (list.Count == 0) return (false, false);
                return (true, list.Any(b => Convert.ToUInt16(b["BatteryStatus"]) == 1));
            }
            catch { return (false, false); }
        }

        private static List<string> GetActiveEthernetAdapters()
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                                 ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet &&
                                 !ni.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) &&
                                 !ni.Description.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase))
                    .Select(ni => ni.Name)
                    .ToList();
            }
            catch { return new List<string>(); }
        }

        public Task<ModuleSimulationResult> SimulateAsync(ModuleScanResult scanResult, CancellationToken ct)
        {
            var sim = new ModuleSimulationResult();
            if (scanResult.Statistics.ItemsFound > 0)
            {
                sim.EstimatedStatistics.ItemsFound = scanResult.Statistics.ItemsFound;
                sim.ItemsToProcess.AddRange(scanResult.FoundItems);
            }
            sim.RiskLevel = RiskLevel.Safe;
            _logger.LogInfo($"[{ModuleId}] Simulação concluída. Itens a processar={sim.ItemsToProcess.Count}, RiskLevel={sim.RiskLevel}, CorrelationId={_correlationId}");
            return Task.FromResult(sim);
        }

        public async Task<ModuleExecutionResult> ExecuteAsync(ModuleSimulationResult simResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleExecutionResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_NetPower_ExecStarting") ?? "Desabilitando economia de energia em adaptadores Ethernet...", SmartRepairEventType.OperationStarted);
            progress?.Report(new RepairProgress { StepPercent = 20, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_NetPower_ExecStarting") ?? "Desabilitando economia de energia..." });

            try
            {
                var script = new StringBuilder();
                script.AppendLine("$adapters = Get-NetAdapter -Physical | Where-Object { $_.Status -eq 'Up' -and $_.InterfaceDescription -notmatch 'Wireless|WiFi|WLAN|Bluetooth|Virtual' }");
                script.AppendLine("$count = 0");
                script.AppendLine("foreach ($a in $adapters) {");
                script.AppendLine("    try {");
                script.AppendLine("        Disable-NetAdapterPowerManagement -Name $a.Name -ErrorAction Stop");
                script.AppendLine("        $count++");
                script.AppendLine("    } catch { }");
                script.AppendLine("}");
                script.AppendLine("Write-Output \"Disabled on $count adapter(s)\"");

                _logger.LogDebug($"[{ModuleId}] Executando PowerShell: Disable-NetAdapterPowerManagement nos adaptadores Ethernet");
                await RunPowerShellScriptAsync(script.ToString(), ct);

                progress?.Report(new RepairProgress { StepPercent = 80, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_NetPower_ExecProgress") ?? "Aplicando configurações..." });

                result.FinalStatistics.ItemsRepaired = simResult.EstimatedStatistics.ItemsFound;
                result.Message = Services.LocalizationService.Instance.GetString("SmartRepair_Module_NetPower_ExecDone") ?? "Gerenciamento de energia desabilitado nos adaptadores Ethernet.";
                _eventBus.PublishMessage(_correlationId, ModuleId, result.Message, SmartRepairEventType.OperationFinished);
                _logger.LogInfo($"[{ModuleId}] Execução concluída. Repaired={result.FinalStatistics.ItemsRepaired}, Errors={result.FinalStatistics.ErrorCount}, Space={result.FinalStatistics.SpaceRecoveredBytes}B, CorrelationId={_correlationId}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[NetPowerSavingModule] Falha ao desabilitar economia de energia", ex);
                result.Success = false;
                result.Message = ex.Message;
            }

            return result;
        }

        public async Task<ModuleRollbackResult> RollbackAsync(ModuleExecutionResult execResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleRollbackResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_NetPower_RollbackStarting") ?? "Reabilitando economia de energia nos adaptadores Ethernet...", SmartRepairEventType.RollbackStarted);

            try
            {
                var script = new StringBuilder();
                script.AppendLine("$adapters = Get-NetAdapter -Physical | Where-Object { $_.Status -eq 'Up' -and $_.InterfaceDescription -notmatch 'Wireless|WiFi|WLAN|Bluetooth|Virtual' }");
                script.AppendLine("foreach ($a in $adapters) {");
                script.AppendLine("    try { Enable-NetAdapterPowerManagement -Name $a.Name -ErrorAction Stop } catch { }");
                script.AppendLine("}");

                _logger.LogDebug($"[{ModuleId}] Executando PowerShell: Enable-NetAdapterPowerManagement nos adaptadores Ethernet");
                await RunPowerShellScriptAsync(script.ToString(), ct);

                result.Success = true;
                result.ItemsRestored = 1;
                _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_NetPower_RollbackDone") ?? "Economia de energia reabilitada.", SmartRepairEventType.RollbackFinished);
                _logger.LogInfo($"[{ModuleId}] Rollback concluído. Restored={result.ItemsRestored}, Failed={result.ItemsFailedToRestore}, CorrelationId={_correlationId}");
                progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = "Rollback concluído." });
            }
            catch (Exception ex)
            {
                _logger.LogError($"[NetPowerSavingModule] Falha no rollback de energia", ex);
                result.Success = false;
            }

            return result;
        }

        private async Task RunPowerShellScriptAsync(string script, CancellationToken ct)
        {
            var tmp = Path.GetTempFileName() + ".ps1";
            try
            {
                await File.WriteAllTextAsync(tmp, script, Encoding.UTF8, ct);
                await ProcessHelper.RunAsync("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{tmp}\"", ct, 60000);
            }
            finally
            {
                try { File.Delete(tmp); } catch (Exception exTmp) { _logger?.LogWarning($"[NetPowerSaving] Erro ao deletar arquivo temporário: {exTmp.Message}"); }
            }
        }
    }
}
