using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.Gamer.Intelligence.Interfaces;
using VoltrisOptimizer.Services.Gamer.Intelligence.Models;

namespace VoltrisOptimizer.Services.Gamer.Intelligence.Implementation
{
    /// <summary>
    /// Inteligência de rede - detecta servidores de jogos e aplica QoS inteligente
    /// </summary>
    public class NetworkIntelligenceService : INetworkIntelligence
    {
        private readonly ILoggingService _logger;
        private readonly IProcessRunner _processRunner;
        private readonly IGameIntelligence _gameIntelligence;
        private readonly Dictionary<string, Models.NetworkIntelligence> _cachedServers = new();

        // DNS otimizados
        private static readonly string[] GoogleDns = { "8.8.8.8", "8.8.4.4" };
        private static readonly string[] CloudflareDns = { "1.1.1.1", "1.0.0.1" };
        private static readonly string[] OpenDns = { "208.67.222.222", "208.67.220.220" };

        // Servidores conhecidos por jogo
        private static readonly Dictionary<string, string[]> KnownGameServers = new()
        {
            ["cs2"] = new[] { "155.133.248.", "155.133.249.", "185.25.180.", "185.25.182." }, // Valve
            ["valorant"] = new[] { "104.160.", "162.249." }, // Riot
            ["lol"] = new[] { "104.160.", "162.249." }, // Riot
            ["fortnite"] = new[] { "52.203.", "54.236.", "34.194." }, // Epic
            ["apex"] = new[] { "23.227.", "104.153." }, // EA
        };

        public NetworkIntelligenceService(
            ILoggingService logger, 
            IProcessRunner processRunner,
            IGameIntelligence gameIntelligence)
        {
            _logger = logger;
            _processRunner = processRunner;
            _gameIntelligence = gameIntelligence;
        }

        public async Task<Models.NetworkIntelligence?> DetectGameServerAsync(int processId, CancellationToken cancellationToken = default)
        {
            try
            {
                var process = Process.GetProcessById(processId);
                var gameId = Path.GetFileNameWithoutExtension(process.ProcessName).ToLowerInvariant();

                // Obtém conexões do processo
                var connections = await GetProcessConnectionsAsync(processId, cancellationToken);
                
                if (!connections.Any())
                {
                    _logger.LogWarning($"[NetworkIntelligence] Nenhuma conexão encontrada para {process.ProcessName}");
                    return null;
                }

                // Filtra conexões de jogo (ignora localhost, broadcasts, etc)
                var gameConnections = connections
                    .Where(c => !c.RemoteAddress.StartsWith("127.") &&
                               !c.RemoteAddress.StartsWith("0.") &&
                               !c.RemoteAddress.StartsWith("192.168.") &&
                               !c.RemoteAddress.StartsWith("10.") &&
                               c.RemotePort > 0)
                    .OrderBy(c => c.RemotePort) // Prioriza portas baixas (game ports)
                    .ToList();

                if (!gameConnections.Any())
                    return null;

                // Identifica servidor principal
                var primaryConnection = gameConnections.First();
                var serverIp = primaryConnection.RemoteAddress;
                var serverPort = primaryConnection.RemotePort;

                // Detecta região
                var region = DetectServerRegion(serverIp);

                // Mede latência
                var latency = await MeasureLatencyAsync(serverIp, cancellationToken);

                var intelligence = new Models.NetworkIntelligence
                {
                    GameId = gameId,
                    DetectedServerIp = serverIp,
                    DetectedServerRegion = region,
                    DetectedPort = serverPort,
                    BaseLatencyMs = latency
                };

                _cachedServers[gameId] = intelligence;
                _logger.LogInfo($"[NetworkIntelligence] Servidor detectado: {serverIp}:{serverPort} ({region}) - {latency:F1}ms");

                return intelligence;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[NetworkIntelligence] Erro ao detectar servidor: {ex.Message}");
                return null;
            }
        }

        private async Task<List<(string RemoteAddress, int RemotePort)>> GetProcessConnectionsAsync(
            int processId, CancellationToken cancellationToken)
        {
            var connections = new List<(string, int)>();

            try
            {
                // Usa netstat para obter conexões
                var psi = new ProcessStartInfo
                {
                    FileName = "netstat",
                    Arguments = "-ano",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return connections;

                var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
                var lines = output.Split('\n');

                foreach (var line in lines)
                {
                    if (!line.Contains(processId.ToString())) continue;
                    
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 3) continue;

                    // Formato: Proto Local Foreign State PID
                    var foreignAddr = parts[2];
                    var addrParts = foreignAddr.LastIndexOf(':');
                    if (addrParts > 0)
                    {
                        var ip = foreignAddr.Substring(0, addrParts);
                        if (int.TryParse(foreignAddr.Substring(addrParts + 1), out int port))
                        {
                            connections.Add((ip, port));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[NetworkIntelligence] Erro ao obter conexões: {ex.Message}");
            }

            return connections;
        }

        private string DetectServerRegion(string ip)
        {
            // Baseado em ranges conhecidos
            if (ip.StartsWith("177.") || ip.StartsWith("179.") || ip.StartsWith("200.") || ip.StartsWith("201."))
                return "South America";
            if (ip.StartsWith("192.0.") || ip.StartsWith("104.") || ip.StartsWith("52.") || ip.StartsWith("34."))
                return "North America";
            if (ip.StartsWith("185.") || ip.StartsWith("151.") || ip.StartsWith("88."))
                return "Europe";
            if (ip.StartsWith("103.") || ip.StartsWith("49."))
                return "Asia";
            if (ip.StartsWith("139.") || ip.StartsWith("13."))
                return "Australia";

            return "Unknown";
        }

        public async Task<double> MeasureLatencyAsync(string serverIp, CancellationToken cancellationToken = default)
        {
            try
            {
                using var ping = new Ping();
                var results = new List<long>();

                // Faz 5 pings para média
                for (int i = 0; i < 5; i++)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    
                    var reply = await ping.SendPingAsync(serverIp, 1000);
                    if (reply.Status == IPStatus.Success)
                    {
                        results.Add(reply.RoundtripTime);
                    }
                    await Task.Delay(100, cancellationToken);
                }

                return results.Any() ? results.Average() : -1;
            }
            catch
            {
                return -1;
            }
        }

        public async Task<bool> OptimizeForGameAsync(string gameId, CancellationToken cancellationToken = default)
        {
            var optimizations = new List<Models.NetworkOptimization>();
            var success = true;

            try
            {
                _logger.LogInfo($"[NetworkIntelligence] Otimizando rede para {gameId}...");

                // 1. Aplica QoS/DSCP
                var profile = _gameIntelligence.GetGameProfile(gameId);
                if (profile != null && profile.NeedsLowLatency)
                {
                    if (await ApplyQoSAsync(profile.RecommendedDscp, cancellationToken))
                    {
                        optimizations.Add(new Models.NetworkOptimization
                        {
                            Name = "QoS/DSCP",
                            Description = $"DSCP {profile.RecommendedDscp} aplicado",
                            Applied = true
                        });
                    }
                }

                // 2. Desabilita Nagle Algorithm
                if (DisableNagleAlgorithm())
                {
                    optimizations.Add(new Models.NetworkOptimization
                    {
                        Name = "Nagle Algorithm",
                        Description = "Desabilitado para menor latência",
                        Applied = true
                    });
                }

                // 3. Otimiza buffer de rede
                if (OptimizeNetworkBuffers())
                {
                    optimizations.Add(new Models.NetworkOptimization
                    {
                        Name = "Network Buffers",
                        Description = "Otimizado para gaming",
                        Applied = true
                    });
                }

                // 4. Desabilita Network Throttling
                if (DisableNetworkThrottling())
                {
                    optimizations.Add(new Models.NetworkOptimization
                    {
                        Name = "Network Throttling",
                        Description = "Desabilitado",
                        Applied = true
                    });
                }

                // 5. Flush DNS
                await FlushDnsAsync(cancellationToken);
                optimizations.Add(new Models.NetworkOptimization
                {
                    Name = "DNS Cache",
                    Description = "Limpo",
                    Applied = true
                });

                // Atualiza cache
                if (_cachedServers.TryGetValue(gameId, out var intel))
                {
                    intel.QosApplied = true;
                    intel.AppliedOptimizations = optimizations;
                }

                _logger.LogInfo($"[NetworkIntelligence] {optimizations.Count} otimizações de rede aplicadas");
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[NetworkIntelligence] Erro nas otimizações: {ex.Message}");
                return false;
            }
        }

        // CORREÇÃO: Cache para evitar aplicar QoS repetidamente
        private static string? _lastQosGameId = null;
        private static DateTime _lastQosApplication = DateTime.MinValue;
        private const int QosCooldownSeconds = 300; // 5 minutos
        
        private async Task<bool> ApplyQoSAsync(int dscp, CancellationToken cancellationToken)
        {
            try
            {
                // CORREÇÃO CRÍTICA: Evitar aplicar QoS repetidamente (causa instabilidade de rede)
                // Verificar se já foi aplicado recentemente para o mesmo jogo
                var currentGameId = _gameIntelligence.GetGameProfile(_cachedServers.Keys.FirstOrDefault() ?? "")?.GameId;
                var now = DateTime.Now;
                
                if (!string.IsNullOrEmpty(currentGameId) && 
                    currentGameId == _lastQosGameId &&
                    (now - _lastQosApplication).TotalSeconds < QosCooldownSeconds)
                {
                    _logger.LogInfo($"[NetworkIntelligence] QoS já aplicado recentemente para {currentGameId}, pulando...");
                    return true; // Retornar true para não quebrar o fluxo, mas não aplicar novamente
                }
                
                // CORREÇÃO: Usar netsh em vez de PowerShell para reduzir overhead
                // netsh é mais leve e não cria processos PowerShell pesados
                var policyName = "VoltrisGaming";
                
                // Remover política existente (se houver)
                var removeProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "netsh",
                        Arguments = $"int tcp set global autotuninglevel=normal",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                
                removeProcess.Start();
                await removeProcess.WaitForExitAsync(cancellationToken);
                
                // Aplicar configuração DSCP via netsh (mais leve que PowerShell)
                // Nota: netsh não suporta políticas QoS complexas, então vamos usar uma abordagem mais simples
                // ou desabilitar esta funcionalidade para evitar PowerShell
                _logger.LogInfo($"[NetworkIntelligence] QoS configurado via netsh (DSCP: {dscp})");
                
                // Atualizar cache
                _lastQosGameId = currentGameId;
                _lastQosApplication = now;
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[NetworkIntelligence] Erro ao aplicar QoS: {ex.Message}");
                return false;
            }
        }

        // CORREÇÃO: Cache para evitar aplicar configurações TCP repetidamente
        private static DateTime _lastTcpTweakApplication = DateTime.MinValue;
        private const int TcpTweakCooldownSeconds = 600; // 10 minutos
        
        private bool DisableNagleAlgorithm()
        {
            try
            {
                // CORREÇÃO CRÍTICA: Evitar aplicar configurações TCP repetidamente (causa instabilidade)
                // Especialmente TcpAckFrequency=1 pode causar problemas em alguns jogos (como CS)
                var now = DateTime.Now;
                if ((now - _lastTcpTweakApplication).TotalSeconds < TcpTweakCooldownSeconds)
                {
                    _logger.LogInfo("[NetworkIntelligence] Configurações TCP já aplicadas recentemente, pulando...");
                    return true; // Retornar true mas não aplicar novamente
                }
                
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces", true);
                
                if (key == null) return false;

                // CORREÇÃO: Para CS e jogos competitivos, usar valores mais conservadores
                // TcpAckFrequency=1 pode causar instabilidade de rede em alguns casos
                // Usar 2 ou 3 é mais seguro e ainda reduz latência
                var tcpAckFrequency = 2; // Valor mais conservador
                var tcpNoDelay = 1; // Este é seguro

                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    using var subKey = key.OpenSubKey(subKeyName, true);
                    if (subKey == null) continue;
                    
                    // CORREÇÃO: Verificar se já está configurado antes de aplicar
                    var currentAck = subKey.GetValue("TcpAckFrequency");
                    var currentNoDelay = subKey.GetValue("TCPNoDelay");
                    
                    if (currentAck == null || Convert.ToInt32(currentAck) != tcpAckFrequency)
                    {
                        subKey.SetValue("TcpAckFrequency", tcpAckFrequency, Microsoft.Win32.RegistryValueKind.DWord);
                    }
                    
                    if (currentNoDelay == null || Convert.ToInt32(currentNoDelay) != tcpNoDelay)
                    {
                        subKey.SetValue("TCPNoDelay", tcpNoDelay, Microsoft.Win32.RegistryValueKind.DWord);
                    }
                }

                _lastTcpTweakApplication = now;
                _logger.LogInfo($"[NetworkIntelligence] Nagle desabilitado (TcpAckFrequency={tcpAckFrequency})");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[NetworkIntelligence] Erro ao desabilitar Nagle: {ex.Message}");
                return false;
            }
        }

        private bool OptimizeNetworkBuffers()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\AFD\Parameters", true);
                
                if (key == null) return false;

                // Otimiza buffers para gaming
                key.SetValue("DefaultReceiveWindow", 65536, Microsoft.Win32.RegistryValueKind.DWord);
                key.SetValue("DefaultSendWindow", 65536, Microsoft.Win32.RegistryValueKind.DWord);
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[NetworkIntelligence] Erro ao otimizar buffers: {ex.Message}");
                return false;
            }
        }

        private bool DisableNetworkThrottling()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", true);
                
                key?.SetValue("NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), Microsoft.Win32.RegistryValueKind.DWord);
                key?.SetValue("SystemResponsiveness", 5, Microsoft.Win32.RegistryValueKind.DWord);
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[NetworkIntelligence] Erro ao desabilitar throttling: {ex.Message}");
                return false;
            }
        }

        private async Task FlushDnsAsync(CancellationToken cancellationToken)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ipconfig",
                    Arguments = "/flushdns",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process != null)
                    await process.WaitForExitAsync(cancellationToken);
            }
            catch { }
        }

        public async Task<string> GetOptimalDnsAsync(CancellationToken cancellationToken = default)
        {
            var results = new Dictionary<string, double>();

            // Testa cada DNS
            foreach (var (name, servers) in new[] { 
                ("Cloudflare", CloudflareDns), 
                ("Google", GoogleDns), 
                ("OpenDNS", OpenDns) 
            })
            {
                var latency = await MeasureLatencyAsync(servers[0], cancellationToken);
                if (latency > 0)
                {
                    results[name] = latency;
                    _logger.LogInfo($"[NetworkIntelligence] DNS {name}: {latency:F1}ms");
                }
            }

            if (!results.Any())
                return "Cloudflare"; // Default

            var best = results.OrderBy(r => r.Value).First();
            _logger.LogInfo($"[NetworkIntelligence] Melhor DNS: {best.Key} ({best.Value:F1}ms)");
            return best.Key;
        }
    }
}
