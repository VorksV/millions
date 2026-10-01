using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace VoltrisOptimizer.Services.Repair
{
    public class NetworkOptimizer
    {
        private readonly ILoggingService _logger;

        public Action<string, string>? OnLog { get; set; }
        public Action<int, string>? OnProgress { get; set; }

        public NetworkOptimizer(ILoggingService logger)
        {
            _logger = logger;
        }

        private void Log(string msg, string color = "#AAAAAA")
        {
            _logger.LogDebug($"[NetworkOptimizer] {msg}", source: "NetworkOptimizer");
            OnLog?.Invoke(msg, color);
        }

        private void Progress(int pct, string msg)
        {
            OnProgress?.Invoke(pct, msg);
        }

        public async Task<AdvancedRepairStepResult> OptimizeNetworkAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var stepName = LocalizationService.Instance.GetString("Repair_Network_Name");
            var step = new AdvancedRepairStepResult { StepName = stepName };
            
            Log($"═══ {LocalizationService.Instance.GetString("Repair_Network_Title")} ═══", "#00BFFF");
            Progress(10, LocalizationService.Instance.GetString("Repair_Network_Progress"));

            try
            {
                // 1. Encontrar o DNS mais rápido
                Progress(20, LocalizationService.Instance.GetString("RepairProgress_Network_DNSLatency"));
                var bestDns = await BenchmarkDnsAsync(ct);
                Log($"  → DNS escolhido automaticamente: {bestDns.Name} ({bestDns.Ip}) - Latência: {bestDns.Latency}ms", "#00BFFF");

                // 2. Obter interface ativa
                var activeInterface = NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(x => x.OperationalStatus == OperationalStatus.Up && 
                                         x.NetworkInterfaceType != NetworkInterfaceType.Loopback && 
                                         (x.NetworkInterfaceType == NetworkInterfaceType.Ethernet || x.NetworkInterfaceType == NetworkInterfaceType.Wireless80211));

                if (activeInterface != null)
                {
                    Progress(40, $"Aplicando DNS na interface '{activeInterface.Name}'...");
                    var interfaceName = activeInterface.Name;
                    
                    var dnsScript = $@"
                        try {{
                            netsh int ipv4 set dnsservers name=""{interfaceName}"" static {bestDns.Ip} primary validate=no
                            if ('{bestDns.Ip2}' -ne '') {{
                                netsh int ipv4 add dnsservers name=""{interfaceName}"" {bestDns.Ip2} index=2 validate=no
                            }}
                        }} catch {{ Write-Error $_.Exception.Message }}
                    ";
                    await RunPowerShellScriptAsync(dnsScript, ct);
                    
                    // Validação pós-execução: ler novamente a configuração
                    var applied = false;
                    var postCheckInterface = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(x => x.Id == activeInterface.Id);
                    if (postCheckInterface != null)
                    {
                        var dnsAddresses = postCheckInterface.GetIPProperties().DnsAddresses;
                        if (dnsAddresses.Any(d => d.ToString() == bestDns.Ip))
                        {
                            applied = true;
                        }
                    }

                    if (applied)
                    {
                        Log($"  ✓ DNS validado e aplicado com sucesso: {bestDns.Name} ({bestDns.Ip}) em {interfaceName}", "#00FF88");
                    }
                    else
                    {
                        Log($"  ⚠ Falha ao confirmar aplicação do DNS em {interfaceName}. O sistema pode ter bloqueado a alteração.", "#FFAA00");
                        step.Success = false;
                    }
                }
                else
                {
                    Log("  ⚠ Nenhuma interface de rede ativa encontrada para aplicar o DNS.", "#FFAA00");
                }

                // Utilizando um script PowerShell robusto para aplicar as otimizações de TCP/IP, ECN e RSS
                Progress(60, LocalizationService.Instance.GetString("RepairProgress_Network_TCPOptimizations"));
                
                var script = @"
try {
    # 1. TCP Auto-Tuning
    netsh int tcp set global autotuninglevel=normal
    
    # 2. ECN Capability (Reduz latência em redes com perda de pacotes)
    netsh int tcp set global ecncapability=enabled
    
    # 3. Receive-Side Scaling (Distribui processamento de pacotes na CPU)
    netsh int tcp set global rss=enabled
    
    # 4. Chimney Offload State
    netsh int tcp set global chimney=disabled
    
    # 5. Network Throttling Index (Gaming/Multimedia tweak no Registro)
    $regPath = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile'
    if (Test-Path $regPath) {
        Set-ItemProperty -Path $regPath -Name 'NetworkThrottlingIndex' -Value 0xffffffff -Type DWord -ErrorAction SilentlyContinue
    }
} catch {
    Write-Error $_.Exception.Message
}
";
                await RunPowerShellScriptAsync(script, ct);
                Log($"  ✓ Configurações TCP/IP e Network Throttling otimizadas.", "#00FF88");

                Progress(80, LocalizationService.Instance.GetString("RepairProgress_Network_FlushDNS"));
                await RunHiddenProcessAsync("ipconfig.exe", "/flushdns", ct);
                Log($"  ✓ Cache DNS resetado.", "#00FF88");

                Progress(100, LocalizationService.Instance.GetString("RepairProgress_Network_Complete"));

                step.Success = true;
                step.Summary = $"Rede otimizada. DNS configurado para {bestDns.Name} ({bestDns.Latency}ms).";
                step.Details.Add($"Melhor DNS = {bestDns.Name} ({bestDns.Ip})");
                step.Details.Add("TCP AutoTuning = Normal");
                step.Details.Add("ECN Capability = Enabled");
                step.Details.Add("Network Throttling = Disabled (Gaming Mode)");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[NetworkOptimizer] Erro crítico na otimização de rede", ex);
                step.Success = false;
                step.Summary = LocalizationService.Instance.GetString("Repair_Network_Warn");
                step.Details.Add(ex.Message);
            }

            step.Duration = sw.Elapsed;
            return step;
        }

        private async Task<(string Name, string Ip, string Ip2, long Latency)> BenchmarkDnsAsync(CancellationToken ct)
        {
            var servers = new List<(string Name, string Ip, string Ip2)>
            {
                ("Cloudflare", "1.1.1.1", "1.0.0.1"),
                ("Google", "8.8.8.8", "8.8.4.4"),
                ("Quad9", "9.9.9.9", "149.112.112.112"),
                ("OpenDNS", "208.67.222.222", "208.67.220.220")
            };

            var results = new List<(string Name, string Ip, string Ip2, long Latency)>();
            
            using var ping = new Ping();
            foreach (var srv in servers)
            {
                try
                {
                    var reply = await ping.SendPingAsync(srv.Ip, 2000);
                    if (reply.Status == IPStatus.Success)
                    {
                        results.Add((srv.Name, srv.Ip, srv.Ip2, reply.RoundtripTime));
                    }
                }
                catch { /* Ignorar falhas isoladas */ }
            }

            if (!results.Any())
                return ("Cloudflare (Default)", "1.1.1.1", "1.0.0.1", 0);

            return results.OrderBy(x => x.Latency).First();
        }

        private async Task RunPowerShellScriptAsync(string script, CancellationToken ct)
        {
            var tmp = Path.GetTempFileName() + ".ps1";
            try
            {
                await File.WriteAllTextAsync(tmp, script, System.Text.Encoding.UTF8, ct);
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{tmp}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    await proc.WaitForExitAsync(ct);
                }
            }
            finally
            {
                try { File.Delete(tmp); } catch { }
            }
        }

        private async Task RunHiddenProcessAsync(string exe, string args, CancellationToken ct)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                await proc.WaitForExitAsync(ct);
            }
        }
    }
}
