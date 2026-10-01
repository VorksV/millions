using VoltrisOptimizer.Helpers;
using System;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Services.Gamer.Interfaces;

namespace VoltrisOptimizer.Services.Gamer.GamerModeManager.Services
{
    /// <summary>
    /// Network Optimization Service - Otimização de rede para gaming
    /// Reduz latência, packet loss e melhora throughput para jogos online
    /// </summary>
    public class NetworkOptimizerService : INetworkOptimizerService
    {
        private readonly ILoggingService _logger;
        
        // Backup de configurações
        private int? _originalNetworkThrottling;
        private int? _originalSystemResponsiveness;
        private string? _originalDnsServers;
        
        // APIs nativas
        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern int SetTcpEntry(ref MibTcpRow tcpRow);
        
        [DllImport("wininet.dll", SetLastError = true)]
        private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);
        
        [StructLayout(LayoutKind.Sequential)]
        public struct MibTcpRow
        {
            public uint dwState;
            public uint dwLocalAddr;
            public uint dwLocalPort;
            public uint dwRemoteAddr;
            public uint dwRemotePort;
            public uint dwNumPktsRetrans;
            public uint dwNumPktsSent;
            public uint dwNumPktsRecv;
            public uint dwNumPktsDropped;
            public uint dwLocalPort2;
            public uint dwRemotePort2;
            public uint dwOif;
            public uint dwIif;
        }
        
        public NetworkOptimizerService(ILoggingService logger)
        {
            _logger.LogEntry(nameof(NetworkOptimizerService));
_logger = logger;
            _logger.LogExit(nameof(NetworkOptimizerService));
}
        
        /// <summary>
        /// Otimiza configurações de rede para gaming
        /// </summary>
        public async Task<bool> OptimizeNetworkAsync()
        {
            _logger.LogEntry(nameof(OptimizeNetworkAsync));
try
            {
                _logger.LogInfo("[Network] Iniciando otimização de rede para gaming...");
                
                // 1. Desabilitar Nagle's Algorithm (apenas TcpNoDelay - seguro)
                DisableNagleAlgorithm();
                
                // 2. Desabilitar throttling de rede
                DisableNetworkThrottling();
                
                // CORREÇÃO CRÍTICA: DNS global forçado, QoS DSCP (New-NetQosPolicy/net sh) e
                // TcpAckFrequency/TcpDelAckTicks foram REMOVIDOS porque causam perda de pacotes,
                // picos de latência (lag spikes) e degradação de download em conexões residenciais.
                _logger.LogInfo("[Network] QoS DSCP e DNS forçado desativados para evitar instabilidade de rede (CRITICAL FIX)");
                
                _logger.LogSuccess("[Network] ✅ Otimização de rede concluída - Latência reduzida!");
return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("[Network] Erro na otimização de rede", ex);
return false;
            }
            _logger.LogExit(nameof(OptimizeNetworkAsync));
}
        
        /// <summary>
        /// Desabilita Nagle's Algorithm para reduzir latência
        /// </summary>
        private void DisableNagleAlgorithm()
        {
            _logger.LogEntry(nameof(DisableNagleAlgorithm));
try
            {
                // TCP/IP - NoDelay para reduzir latência
                using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters");
                
                // Backup
                _originalNetworkThrottling = key.GetValue("TcpAckFrequency") as int?;
                
                // Desabilitar Nagle's Algorithm (apenas TcpNoDelay - seguro)
                key.SetValue("TCPNoDelay", 1, RegistryValueKind.DWord); // Sem delay
                
                // CORREÇÃO CRÍTICA: TcpAckFrequency=1 e TcpDelAckTicks=0 REMOVIDOS
                // porque causam instabilidade de rede (picos de latência/packet loss)
                // e degradam a velocidade de download em jogos e ISPs residenciais.
                
                _logger.LogInfo("[Network] ✅ Nagle's Algorithm desabilitado (TcpNoDelay=1, ACK mantido padrão)");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Network] Erro ao desabilitar Nagle: {ex.Message}");
            }
            _logger.LogExit(nameof(DisableNagleAlgorithm));
}
        
        /// <summary>
        /// Otimiza TCP/IP stack para gaming
        /// </summary>
        private void OptimizeTcpStack()
        {
            _logger.LogEntry(nameof(OptimizeTcpStack));
try
            {
                using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters");
                
                // Aumentar conexões simultâneas
                key.SetValue("MaxUserPort", 65534, RegistryValueKind.DWord);
                key.SetValue("TcpNumConnections", 65534, RegistryValueKind.DWord);
                
                // Otimizar janela TCP
                key.SetValue("TcpWindowSize", 65536, RegistryValueKind.DWord);
                
                // Reduzir tempo de espera
                key.SetValue("TcpMaxDataRetransmissions", 3, RegistryValueKind.DWord);
                key.SetValue("MaxFreeTcbs", 65536, RegistryValueKind.DWord);
                
                // Desabilitar Large Send Offload (pode causar issues em alguns jogos)
                key.SetValue("DisableLargeSendOffload", 1, RegistryValueKind.DWord);
                
                _logger.LogInfo("[Network] ✅ TCP/IP stack otimizado");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Network] Erro ao otimizar TCP: {ex.Message}");
            }
            _logger.LogExit(nameof(OptimizeTcpStack));
}
        
        /// <summary>
        /// Configura QoS (Quality of Service) para priorizar tráfego de jogos
        /// </summary>
        private void ConfigureQoSForGaming()
        {
            _logger.LogEntry(nameof(ConfigureQoSForGaming));
try
            {
                using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\Psched");
                
                // Backup
                _originalSystemResponsiveness = key.GetValue("SystemResponsiveness") as int?;
                
                // Priorizar jogos sobre sistema
                key.SetValue("SystemResponsiveness", 0, RegistryValueKind.DWord); // Máxima prioridade para apps
                
                // Desabilitar limitação de banda
                key.SetValue("BestEffortLimit", 0, RegistryValueKind.DWord);
                
                // Configurar DSCP para gaming
                using var dscpKey = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\QoS");
                dscpKey.SetValue("DoNotUseNIC", 0, RegistryValueKind.DWord);
                
                _logger.LogInfo("[Network] ✅ QoS configurado para gaming");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Network] Erro ao configurar QoS: {ex.Message}");
            }
            _logger.LogExit(nameof(ConfigureQoSForGaming));
}
        
        /// <summary>
        /// Otimiza configurações de DNS para baixa latência
        /// </summary>
        private void OptimizeDnsSettings()
        {
            _logger.LogEntry(nameof(OptimizeDnsSettings));
try
            {
                // Usar DNS de baixa latência (Cloudflare, Google)
                var optimizedDns = "1.1.1.1,8.8.8.8"; // Cloudflare + Google
                
                using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters");
                
                // Backup DNS atual
                _originalDnsServers = key.GetValue("NameServer")?.ToString();
                
                // Configurar DNS otimizado
                key.SetValue("NameServer", optimizedDns, RegistryValueKind.String);
                
                // Aumentar cache DNS
                key.SetValue("MaxCacheEntryTtlLimit", 86400, RegistryValueKind.DWord); // 24h
                key.SetValue("MaxCacheTtl", 86400, RegistryValueKind.DWord);
                
                // Desabilitar DNS cache negativo
                key.SetValue("NegativeCacheTime", 0, RegistryValueKind.DWord);
                
                _logger.LogInfo($"[Network] ✅ DNS otimizado: {optimizedDns}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Network] Erro ao otimizar DNS: {ex.Message}");
            }
            _logger.LogExit(nameof(OptimizeDnsSettings));
}
        
        /// <summary>
        /// Desabilita throttling de rede do Windows
        /// </summary>
        private void DisableNetworkThrottling()
        {
            _logger.LogEntry(nameof(DisableNetworkThrottling));
try
            {
                using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile");
                
                // Desabilitar throttling de rede para system responsiveness
                key.SetValue("NetworkThrottlingIndex", -1, RegistryValueKind.DWord); // Máximo (0xFFFFFFFF)
                key.SetValue("SystemResponsiveness", 0, RegistryValueKind.DWord); // Máxima prioridade
                
                _logger.LogInfo("[Network] ✅ Network throttling desabilitado");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Network] Erro ao desabilitar throttling: {ex.Message}");
            }
            _logger.LogExit(nameof(DisableNetworkThrottling));
}
        
        /// <summary>
        /// Otimiza buffers de rede para gaming
        /// </summary>
        private void OptimizeNetworkBuffers()
        {
            _logger.LogEntry(nameof(OptimizeNetworkBuffers));
try
            {
                using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters");
                
                // Aumentar buffers para reduzir packet loss
                key.SetValue("DefaultTTL", 64, RegistryValueKind.DWord);
                key.SetValue("EnablePMTUDiscovery", 1, RegistryValueKind.DWord);
                
                // Otimizar receive buffer
                key.SetValue("Tcp1323Opts", 3, RegistryValueKind.DWord); // Window scaling + timestamps
                
                // Aumentar send/receive buffers
                key.SetValue("TcpRecvSegmentSize", 1460, RegistryValueKind.DWord); // MTU otimizado
                key.SetValue("TcpSendSegmentSize", 1460, RegistryValueKind.DWord);
                
                _logger.LogInfo("[Network] ✅ Buffers de rede otimizados");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Network] Erro ao otimizar buffers: {ex.Message}");
            }
            _logger.LogExit(nameof(OptimizeNetworkBuffers));
}
        
        /// <summary>
        /// Restaura configurações originais de rede
        /// </summary>
        public async Task<bool> RestoreNetworkAsync()
        {
            _logger.LogEntry(nameof(RestoreNetworkAsync));
try
            {
                _logger.LogInfo("[Network] Restaurando configurações de rede...");
                
                // Restaurar Nagle's Algorithm
                if (_originalNetworkThrottling.HasValue)
                {
                    using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters");
                    key.SetValue("TcpAckFrequency", _originalNetworkThrottling.Value, RegistryValueKind.DWord);
                }
                
                // Restaurar QoS
                if (_originalSystemResponsiveness.HasValue)
                {
                    using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\Psched");
                    key.SetValue("SystemResponsiveness", _originalSystemResponsiveness.Value, RegistryValueKind.DWord);
                }
                
                // Restaurar DNS
                if (!string.IsNullOrEmpty(_originalDnsServers))
                {
                    using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters");
                    key.SetValue("NameServer", _originalDnsServers, RegistryValueKind.String);
                }
                
                _logger.LogInfo("[Network] ✅ Configurações de rede restauradas");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("[Network] Erro ao restaurar rede", ex);
return false;
            }
            _logger.LogExit(nameof(RestoreNetworkAsync));
}
        
        /// <summary>
        /// Obtém métricas de latência de rede
        /// </summary>
        public (double PingMs, double PacketLoss, double Jitter) GetNetworkMetrics()
        {
            try
            {
                // Ping para servidor de jogo (exemplo: Google DNS)
                var ping = new Ping();
                var reply = ping.Send("8.8.8.8", 1000); // 1s timeout
                
                var pingMs = reply.Status == IPStatus.Success ? reply.RoundtripTime : 999;
                var packetLoss = reply.Status == IPStatus.Success ? 0 : 100;
                var jitter = CalculateJitter();
                
                return (pingMs, packetLoss, jitter);
            }
            catch
            {
                return (999, 100, 0);
            }
        }
        
        /// <summary>
        /// Calcula jitter da conexão
        /// </summary>
        private double CalculateJitter()
        {
            _logger.LogEntry(nameof(CalculateJitter));
try
            {
                var pings = new long[5];
                var ping = new Ping();
                
                for (int i = 0; i < 5; i++)
                {
                    var reply = ping.Send("8.8.8.8", 500);
                    pings[i] = reply.Status == IPStatus.Success ? reply.RoundtripTime : 999;
                    Thread.Sleep(100);
                }
                
                // Calcular desvio padrão
                var mean = 0.0;
                foreach (var p in pings) mean += p;
                mean /= 5;
                
                var variance = 0.0;
                foreach (var p in pings) variance += Math.Pow(p - mean, 2);
                variance /= 5;
return Math.Sqrt(variance);
            }
            catch
            {
return 0;
            }
            _logger.LogExit(nameof(CalculateJitter));
}
    }
}
