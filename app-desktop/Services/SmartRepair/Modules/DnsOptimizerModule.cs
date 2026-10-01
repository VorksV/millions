using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.SmartRepair.Architecture;

namespace VoltrisOptimizer.Services.SmartRepair.Modules
{
    public class DnsOptimizerModule : ISmartRepairModule
    {
        private readonly ILoggingService _logger;
        private readonly ISmartRepairEventBus _eventBus;
        private readonly string _correlationId;

        public string ModuleId => "DnsOptimizer";
        public string Category => "Network";

        public string Name => Services.LocalizationService.Instance.GetString("SmartRepair_Module_Dns_Name") ?? "Otimização de DNS";
        public string Description => Services.LocalizationService.Instance.GetString("SmartRepair_Module_Dns_Desc") ?? "Testa latência de servidores DNS e aplica o mais rápido.";

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

        public DnsOptimizerModule(ILoggingService logger, ISmartRepairEventBus eventBus, string correlationId)
        {
            _logger = logger;
            _eventBus = eventBus;
            _correlationId = correlationId;
        }

        private class DnsTarget
        {
            public string ServerName { get; set; } = string.Empty;
            public string PrimaryIp { get; set; } = string.Empty;
            public string SecondaryIp { get; set; } = string.Empty;
            public string PrimaryIpv6 { get; set; } = string.Empty;
            public string SecondaryIpv6 { get; set; } = string.Empty;
            public long LatencyMs { get; set; }
            public long LatencyMsIpv6 { get; set; }
            public string InterfaceName { get; set; } = string.Empty;
            public string InterfaceGuid { get; set; } = string.Empty;
            public bool HasIpv4Connectivity { get; set; }
            public bool HasIpv6Connectivity { get; set; }
            public List<string> CurrentDnsServers { get; set; } = new();
        }

        private static readonly List<(string Name, string Ip, string Ip2, string Ipv6, string Ipv62)> WellKnownServers = new()
        {
            ("Cloudflare", "1.1.1.1", "1.0.0.1", "2606:4700:4700::1111", "2606:4700:4700::1001"),
            ("Google", "8.8.8.8", "8.8.4.4", "2001:4860:4860::8888", "2001:4860:4860::8844"),
            ("Quad9", "9.9.9.9", "149.112.112.112", "2620:fe::fe", "2620:fe::9"),
            ("OpenDNS", "208.67.222.222", "208.67.220.220", "2620:119:35::35", "2620:119:53::53"),
            ("Comodo", "8.26.56.26", "8.20.247.20", "", "")};

        public async Task<ModuleScanResult> ScanAsync(IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleScanResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Dns_StartingScan") ?? "Iniciando scan de conectividade e DNS...", SmartRepairEventType.OperationStarted);

            var dnsTargets = new List<DnsTarget>();
            var sw = Stopwatch.StartNew();

            await Task.Run(async () =>
            {
                var activeInterface = GetActiveInterface();
                if (activeInterface == null)
                {
                    _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Dns_NoInterface") ?? "Nenhuma interface de rede ativa encontrada.", SmartRepairEventType.WarningRaised);
                    return;
                }

                result.Statistics.ItemsScanned = 1;
                var currentDns = activeInterface.GetIPProperties().DnsAddresses.Select(d => d.ToString()).ToList();

                bool hasIpv4 = currentDns.Any(d => !d.Contains(':'));
                bool hasIpv6 = currentDns.Any(d => d.Contains(':'));

                _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Dns_ActiveInterface") ?? "Interface ativa: {0} ({1})", activeInterface.Name, activeInterface.Id), SmartRepairEventType.ProgressChanged);
                _logger.LogInfo($"[DnsOptimizerModule] Interface: {activeInterface.Name}, CurrentDNS: {string.Join(", ", currentDns)}, CorrelationId={_correlationId}");

                int tested = 0;
                foreach (var srv in WellKnownServers)
                {
                    ct.ThrowIfCancellationRequested();
                    tested++;
                    progress?.Report(new RepairProgress { StepPercent = tested * 100 / WellKnownServers.Count, StatusMessage = string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Dns_TestingServer") ?? "Testando {0}...", srv.Name) });

                    var target = new DnsTarget
                    {
                        ServerName = srv.Name,
                        PrimaryIp = srv.Ip,
                        SecondaryIp = srv.Ip2,
                        PrimaryIpv6 = srv.Ipv6,
                        SecondaryIpv6 = srv.Ipv62,
                        InterfaceName = activeInterface.Name,
                        InterfaceGuid = activeInterface.Id,
                        CurrentDnsServers = currentDns,
                        HasIpv4Connectivity = true,
                        HasIpv6Connectivity = !string.IsNullOrEmpty(srv.Ipv6)
                    };

                    target.LatencyMs = await PingDnsAsync(srv.Ip, ct);
                    if (target.HasIpv6Connectivity && !string.IsNullOrEmpty(srv.Ipv6))
                    {
                        target.LatencyMsIpv6 = await PingDnsAsync(srv.Ipv6, ct);
                    }

                    _logger.LogDebug($"[DnsOptimizerModule] DNS {srv.Name}: IPv4={target.LatencyMs}ms, IPv6={target.LatencyMsIpv6}ms");

                    dnsTargets.Add(target);
                    result.Statistics.ItemsFound++;
                }

                dnsTargets = dnsTargets.OrderBy(t => t.LatencyMs >= 0 ? t.LatencyMs : long.MaxValue).ToList();
            }, ct);

            sw.Stop();
            result.FoundItems.AddRange(dnsTargets);

            _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Dns_ScanComplete") ?? "Scan de DNS concluído. {0} servidores testados em {1}ms.", dnsTargets.Count, sw.ElapsedMilliseconds), SmartRepairEventType.OperationFinished);
            return result;
        }

        private static NetworkInterface? GetActiveInterface()
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(x => x.OperationalStatus == OperationalStatus.Up &&
                                     x.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                                     (x.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                                      x.NetworkInterfaceType == NetworkInterfaceType.Wireless80211));
        }

        private static async Task<long> PingDnsAsync(string ip, CancellationToken ct)
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(ip, 3000);
                return reply.Status == IPStatus.Success ? reply.RoundtripTime : -1;
            }
            catch
            {
                return -1;
            }
        }

        public Task<ModuleSimulationResult> SimulateAsync(ModuleScanResult scanResult, CancellationToken ct)
        {
            var sim = new ModuleSimulationResult();
            var targets = scanResult.FoundItems.Cast<DnsTarget>().ToList();

            var best = targets.FirstOrDefault(t => t.LatencyMs >= 0);
            if (best != null)
            {
                _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Dns_BestServer") ?? "Melhor DNS: {0} ({1}) — {2}ms", best.ServerName, best.PrimaryIp, best.LatencyMs), SmartRepairEventType.ProgressChanged);
                sim.ItemsToProcess.Add(best);
                sim.EstimatedStatistics.ItemsFound = 1;
            }

            sim.RiskLevel = RiskLevel.Safe;
            return Task.FromResult(sim);
        }

        public async Task<ModuleExecutionResult> ExecuteAsync(ModuleSimulationResult simResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleExecutionResult();
            var sw = Stopwatch.StartNew();
            var best = simResult.ItemsToProcess.Cast<DnsTarget>().FirstOrDefault();

            if (best == null)
            {
                _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Dns_NoServerToApply") ?? "Nenhum servidor DNS disponível para aplicar.", SmartRepairEventType.WarningRaised);
                result.Success = false;
                return result;
            }

            _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Dns_Applying") ?? "Aplicando DNS {0} ({1}) em {2}...", best.ServerName, best.PrimaryIp, best.InterfaceName), SmartRepairEventType.OperationStarted);
            progress?.Report(new RepairProgress { StepPercent = 10, StatusMessage = string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Dns_Configuring") ?? "Configurando DNS {0}...", best.ServerName) });

            var sb = new StringBuilder();
            sb.AppendLine($"=== DNS Optimization for {best.InterfaceName} ===");
            sb.AppendLine($"Selected: {best.ServerName} ({best.PrimaryIp})");
            sb.AppendLine($"Secondary: {best.SecondaryIp}");
            sb.AppendLine($"Latency: {best.LatencyMs}ms");

            try
            {
                var script = new StringBuilder();
                script.AppendLine($"netsh int ipv4 set dnsservers name=\"{best.InterfaceName}\" static {best.PrimaryIp} primary validate=no");

                if (!string.IsNullOrEmpty(best.SecondaryIp))
                {
                    script.AppendLine($"netsh int ipv4 add dnsservers name=\"{best.InterfaceName}\" {best.SecondaryIp} index=2 validate=no");
                }

                if (best.HasIpv6Connectivity && !string.IsNullOrEmpty(best.PrimaryIpv6))
                {
                    script.AppendLine($"netsh int ipv6 set dnsservers name=\"{best.InterfaceName}\" static {best.PrimaryIpv6} primary validate=no");
                    if (!string.IsNullOrEmpty(best.SecondaryIpv6))
                    {
                        script.AppendLine($"netsh int ipv6 add dnsservers name=\"{best.InterfaceName}\" {best.SecondaryIpv6} index=2 validate=no");
                    }
                }

                var (exitCode, output) = await RunPowerShellScriptAsync(script.ToString(), ct);
                sb.AppendLine($"PowerShell exit code: {exitCode}");
                sb.AppendLine($"Output: {output}");

                progress?.Report(new RepairProgress { StepPercent = 60, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_Dns_Validating") ?? "Validando aplicação do DNS..." });

                var postCheckInterface = GetActiveInterface();
                bool applied = false;
                if (postCheckInterface != null)
                {
                    var dnsAfter = postCheckInterface.GetIPProperties().DnsAddresses.Select(d => d.ToString()).ToList();
                    sb.AppendLine($"DNS after application: {string.Join(", ", dnsAfter)}");
                    applied = dnsAfter.Any(d => d == best.PrimaryIp);
                }

                if (applied)
                {
                    result.FinalStatistics.ItemsRepaired++;
                    _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Dns_Applied") ?? "DNS {0} ({1}) aplicado e validado em {2}.", best.ServerName, best.PrimaryIp, best.InterfaceName), SmartRepairEventType.ProgressChanged);
                }
                else
                {
                    result.FinalStatistics.ItemsIgnored++;
                    _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Dns_ApplyFailed") ?? "DNS pode não ter sido aplicado corretamente em {0}. Verifique manualmente.", best.InterfaceName), SmartRepairEventType.WarningRaised);
                }

                progress?.Report(new RepairProgress { StepPercent = 80, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_Dns_Flushing") ?? "Limpando cache DNS..." });

                await RunHiddenProcessAsync("ipconfig", "/flushdns", ct);
                sb.AppendLine("DNS cache flushed.");

                sw.Stop();
                sb.AppendLine($"Duration: {sw.ElapsedMilliseconds}ms");

                _logger.LogInfo($"[DnsOptimizerModule] Execução concluída. Applied={applied}, ExitCode={exitCode}, Duration={sw.ElapsedMilliseconds}ms, CorrelationId={_correlationId}");
                _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Dns_Done") ?? "Otimização de DNS concluída em {0}ms.", sw.ElapsedMilliseconds), SmartRepairEventType.OperationFinished);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError($"[DnsOptimizerModule] Falha ao aplicar DNS", ex);
                result.Success = false;
                result.Message = ex.Message;
            }

            return result;
        }

        public async Task<ModuleRollbackResult> RollbackAsync(ModuleExecutionResult execResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleRollbackResult();
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Dns_RestoringDhcp") ?? "Restaurando DNS para DHCP...", SmartRepairEventType.RollbackStarted);

            var activeInterface = GetActiveInterface();
            if (activeInterface == null)
            {
                result.Success = false;
                return result;
            }

            try
            {
                var script = $@"
netsh int ipv4 set dnsservers name=""{activeInterface.Name}"" dhcp
netsh int ipv6 set dnsservers name=""{activeInterface.Name}"" dhcp
";
                var (exitCode, _) = await RunPowerShellScriptAsync(script, ct);

                result.Success = exitCode == 0;
                result.ItemsRestored = exitCode == 0 ? 1 : 0;

                _eventBus.PublishMessage(_correlationId, ModuleId, result.Success ? Services.LocalizationService.Instance.GetString("SmartRepair_Module_Dns_Rollback") ?? "DNS restaurado para DHCP." : Services.LocalizationService.Instance.GetString("SmartRepair_Module_Dns_RollbackFailed") ?? "Falha ao restaurar DNS.", SmartRepairEventType.RollbackFinished);
                _logger.LogInfo($"[DnsOptimizerModule] Rollback concluído. Success={result.Success}, CorrelationId={_correlationId}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[DnsOptimizerModule] Falha no rollback DNS", ex);
                result.Success = false;
            }

            return result;
        }

        private async Task<(int ExitCode, string Output)> RunPowerShellScriptAsync(string script, CancellationToken ct)
        {
            var tmp = Path.GetTempFileName() + ".ps1";
            try
            {
                await File.WriteAllTextAsync(tmp, script, Encoding.UTF8, ct);
                using var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{tmp}\"",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                var output = new StringBuilder();
                proc.OutputDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
                proc.ErrorDataReceived += (_, e) => { if (e.Data != null) output.AppendLine($"ERR: {e.Data}"); };
                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                await proc.WaitForExitAsync(ct);
                return (proc.ExitCode, output.ToString());
            }
            finally
            {
                try { File.Delete(tmp); } catch (Exception exTmp) { _logger?.LogWarning($"[DnsOptimizer] Erro ao deletar arquivo temporário: {exTmp.Message}"); }
            }
        }

        private async Task RunHiddenProcessAsync(string exe, string args, CancellationToken ct)
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            var output = new StringBuilder();
            proc.OutputDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) _logger.LogWarning($"[DnsOptimizerModule] {e.Data}"); };
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            await proc.WaitForExitAsync(ct);
        }
    }
}
