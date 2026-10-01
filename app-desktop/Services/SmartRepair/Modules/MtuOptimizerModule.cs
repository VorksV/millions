using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services.SmartRepair.Architecture;

namespace VoltrisOptimizer.Services.SmartRepair.Modules
{
    public class MtuOptimizerModule : ISmartRepairModule
    {
        private readonly ILoggingService _logger;
        private readonly ISmartRepairEventBus _eventBus;
        private readonly string _correlationId;

        public string ModuleId => "MtuOptimizer";
        public string Category => "Network";

        public string Name => Services.LocalizationService.Instance.GetString("SmartRepair_Module_Mtu_Name") ?? "Otimização de MTU";
        public string Description => Services.LocalizationService.Instance.GetString("SmartRepair_Module_Mtu_Desc") ?? "Testa e ajusta o MTU da interface de rede ativa para o valor máximo sem fragmentação.";

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

        public MtuOptimizerModule(ILoggingService logger, ISmartRepairEventBus eventBus, string correlationId)
        {
            _logger = logger;
            _eventBus = eventBus;
            _correlationId = correlationId;
        }

        private class MtuTarget
        {
            public string InterfaceName { get; set; } = string.Empty;
            public int CurrentMtu { get; set; }
            public int OptimalMtu { get; set; }
            public string Gateway { get; set; } = string.Empty;
            public bool ShouldApply { get; set; }
        }

        public async Task<ModuleScanResult> ScanAsync(IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleScanResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Mtu_ScanStarting") ?? "Iniciando teste de MTU...", SmartRepairEventType.OperationStarted);

            await Task.Run(async () =>
            {
                try
                {
                    _logger.LogDebug($"[{ModuleId}] Obtendo gateway padrão...");
                    var gateway = GetDefaultGateway();
                    if (string.IsNullOrEmpty(gateway))
                    {
                        _eventBus.PublishMessage(_correlationId, ModuleId, "Nenhum gateway padrão encontrado.", SmartRepairEventType.WarningRaised);
                        result.Success = false;
                        result.Message = LocalizationService.Instance.GetString("MtuNoDefaultGateway");
                        return;
                    }

                    _logger.LogDebug($"[{ModuleId}] Obtendo informações da interface ativa...");
                    var (name, currentMtu) = GetActiveInterfaceInfo();
                    if (string.IsNullOrEmpty(name))
                    {
                        _eventBus.PublishMessage(_correlationId, ModuleId, "Nenhuma interface ativa encontrada.", SmartRepairEventType.WarningRaised);
                        result.Success = false;
                        result.Message = LocalizationService.Instance.GetString("MtuNoActiveInterface");
                        return;
                    }

                    _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Mtu_ScanTesting") ?? "Testando MTU contra gateway {0}...", gateway), SmartRepairEventType.ProgressChanged);

                    int optimalSize = 1472;
                    for (int size = 1472; size >= 1200; size -= 8)
                    {
                        ct.ThrowIfCancellationRequested();
                        bool success = await PingWithSizeAsync(gateway, size);
                        if (success)
                        {
                            optimalSize = size;
                            break;
                        }
                    }

                    if (optimalSize < 1472)
                    {
                        for (int size = optimalSize + 8; size >= optimalSize; size--)
                        {
                            ct.ThrowIfCancellationRequested();
                            bool success = await PingWithSizeAsync(gateway, size);
                            if (success)
                            {
                                optimalSize = size;
                                break;
                            }
                        }
                    }

                    int optimalMtu = optimalSize + 28;

                    var target = new MtuTarget
                    {
                        InterfaceName = name,
                        CurrentMtu = currentMtu,
                        OptimalMtu = optimalMtu,
                        Gateway = gateway,
                        ShouldApply = optimalMtu > currentMtu
                    };

                    result.FoundItems.Add(target);
                    result.Statistics.ItemsScanned = 1;
                    result.Statistics.ItemsFound = target.ShouldApply ? 1 : 0;

                    _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Mtu_ScanResult") ?? "MTU atual: {0}, MTU ótimo: {1}.", currentMtu, optimalMtu), SmartRepairEventType.ProgressChanged);
                    _logger.LogInfo($"[MtuOptimizerModule] Gateway={gateway}, CurrentMTU={currentMtu}, OptimalMTU={optimalMtu}, Interface={name}");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogError($"[MtuOptimizerModule] Erro no scan", ex);
                    result.Success = false;
                    result.Message = ex.Message;
                }
            }, ct);

            return result;
        }

        private static string GetDefaultGateway()
        {
            try
            {
                using var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell",
                        Arguments = "-NoProfile -Command \"(Get-CimInstance Win32_IP4RouteTable | Where-Object { $_.Destination -eq '0.0.0.0' }).NextHop | Select-Object -First 1\"",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                proc.Start();
                var output = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit(5000);
                return output;
            }
            catch
            {
                return null;
            }
        }

        private static (string Name, int Mtu) GetActiveInterfaceInfo()
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus == OperationalStatus.Up &&
                        ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    {
                        var ipProps = ni.GetIPProperties();
                        if (ipProps.GatewayAddresses.Count > 0)
                        {
                            return (ni.Name, (int)ni.GetIPProperties().GetIPv4Properties().Mtu);
                        }
                    }
                }
            }
            catch { System.Diagnostics.Debug.WriteLine("[MtuOptimizer] Erro ao obter MTU atual"); }

            return (null, 1500);
        }

        private static async Task<bool> PingWithSizeAsync(string host, int size)
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(host, 3000, new byte[size], new PingOptions { DontFragment = true });
                return reply.Status == IPStatus.Success;
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

            foreach (MtuTarget target in scanResult.FoundItems)
            {
                if (target.ShouldApply)
                {
                    sim.ItemsToProcess.Add(target);
                    sim.EstimatedStatistics.ItemsFound = 1;
                }
            }

            return Task.FromResult(sim);
        }

        public async Task<ModuleExecutionResult> ExecuteAsync(ModuleSimulationResult simResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleExecutionResult();

            foreach (MtuTarget target in simResult.ItemsToProcess)
            {
                ct.ThrowIfCancellationRequested();

                if (!target.ShouldApply)
                {
                    _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Mtu_SkippedSameOrLower") ?? "MTU atual já é igual ou superior ao ótimo — ignorando.", SmartRepairEventType.WarningRaised);
                    result.FinalStatistics.ItemsIgnored++;
                    continue;
                }

                _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Mtu_ExecApplying") ?? "Aplicando MTU {0} em {1}...", target.OptimalMtu, target.InterfaceName), SmartRepairEventType.OperationStarted);
                progress?.Report(new RepairProgress { StepPercent = 50, StatusMessage = string.Format("Configurando MTU {0}...", target.OptimalMtu) });

                try
                {
                    bool success = ProcessHelper.RunNetsh($"int ipv4 set subinterface \"{target.InterfaceName}\" mtu={target.OptimalMtu} store=persistent", out string stdOut, out string stdErr);

                    if (success)
                    {
                        result.FinalStatistics.ItemsRepaired++;
                        _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Mtu_ExecDone") ?? "MTU ajustado com sucesso.", SmartRepairEventType.OperationFinished);
                    }
                    else
                    {
                        _eventBus.PublishMessage(_correlationId, ModuleId, $"Falha ao ajustar MTU: {stdErr}", SmartRepairEventType.WarningRaised);
                        result.FinalStatistics.ItemsIgnored++;
                    }

                    progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_Mtu_ExecDone") ?? "Concluído." });
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogError($"[MtuOptimizerModule] Erro na execução", ex);
                    result.Success = false;
                    result.Message = ex.Message;
                }
            }

            return result;
        }

        public async Task<ModuleRollbackResult> RollbackAsync(ModuleExecutionResult execResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var rollback = new ModuleRollbackResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Mtu_RollbackStarting") ?? "Restaurando MTU original...", SmartRepairEventType.RollbackStarted);

            await Task.Run(() =>
            {
                foreach (MtuTarget target in execResult.ProcessedItems)
                {
                    ct.ThrowIfCancellationRequested();
                    progress?.Report(new RepairProgress { StepPercent = 50, StatusMessage = $"Restaurando MTU para {target.CurrentMtu}..." });

                    bool success = ProcessHelper.RunNetsh($"int ipv4 set subinterface \"{target.InterfaceName}\" mtu={target.CurrentMtu} store=persistent", out _, out _);
                    if (success)
                    {
                        rollback.ItemsRestored++;
                    }
                    else
                    {
                        rollback.ItemsFailedToRestore++;
                    }
                }
            }, ct);

            rollback.Success = rollback.ItemsRestored > 0;
            progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_Mtu_RollbackDone") ?? "MTU restaurado." });
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Mtu_RollbackDone") ?? "Rollback de MTU concluído.", SmartRepairEventType.RollbackFinished);
            return rollback;
        }
    }
}
