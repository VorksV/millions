using System;
using System.Diagnostics;
using System.Net.NetworkInformation;
using Microsoft.Win32;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Optimization.Engines
{
    /// <summary>
    /// Pilar 3 da Ultra-Performance: Absolute Network & TCP Offload Engine
    /// Atua dinamicamente sobre o Network Stack do Windows para jogos competitivos (CS2, Valorant, etc)
    /// Garantindo o menor Ping e menor Bufferbloat humanamente possíveis.
    /// </summary>
    public class CompetitiveNetworkEngine
    {
        private readonly ILoggingService _logger;

        private const string TCPIP_PARAMS = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
        private const string MULTIMEDIA_PROFILE = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
        
        public CompetitiveNetworkEngine(ILoggingService logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Aplica a injeção do algoritmo TCP No-Delay e Nagle Disable na interface de rede ativa.
        /// Isso força pacotes a serem enviados IMEDIATAMENTE em vez de fazer "batching" de pacotes (que aumenta a latência/ping).
        /// </summary>
        public void ApplyGamingNetworkProfile()
        {
            try
            {
                string activeInterfaceId = GetActiveNetworkInterfaceId();
                if (!string.IsNullOrEmpty(activeInterfaceId))
                {
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey($@"{TCPIP_PARAMS}\{activeInterfaceId}", true))
                    {
                        if (key != null)
                        {
                            // TCPNoDelay: 1 = Ativado (pacotes saem na hora)
                            // CORREÇÃO CRÍTICA: TcpAckFrequency e TcpDelAckTicks REMOVIDOS —
                            // forçar ACK/del-ack imediato causa picos de latência e perda de
                            // pacotes em ISPs residenciais (jogos usam UDP de qualquer forma).
                            key.SetValue("TCPNoDelay", 1, RegistryValueKind.DWord);

                            _logger.LogInfo($"[CompetitiveNetwork] Algoritmo Nagle desativado para a interface ativa ({activeInterfaceId}). Latência mínima TCP habilitada.");
                        }
                    }
                }

                // Otimização do SystemProfile do Kernel
                using (RegistryKey mmKey = Registry.LocalMachine.OpenSubKey(MULTIMEDIA_PROFILE, true))
                {
                    if (mmKey != null)
                    {
                        // NetworkThrottlingIndex = 0xFFFFFFFF (Desativa limitação de rede para multimídia)
                        mmKey.SetValue("NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), RegistryValueKind.DWord);
                        // SystemResponsiveness = 0 (100% dos recursos dedicados a aplicações foreground)
                        mmKey.SetValue("SystemResponsiveness", 0, RegistryValueKind.DWord);
                        
                        _logger.LogInfo("[CompetitiveNetwork] Network Throttling desativado. 100% de prioridade de rede liberada para Foreground (Jogos).");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[CompetitiveNetwork] Falha ao aplicar profile de rede: {ex.Message}");
            }
        }

        public void RestoreStandardNetworkProfile()
        {
            // O Windows padrão usa NetworkThrottlingIndex = 10, SystemResponsiveness = 20, TcpAckFrequency = n/a
            try
            {
                using (RegistryKey mmKey = Registry.LocalMachine.OpenSubKey(MULTIMEDIA_PROFILE, true))
                {
                    if (mmKey != null)
                    {
                        mmKey.SetValue("NetworkThrottlingIndex", 10, RegistryValueKind.DWord);
                        mmKey.SetValue("SystemResponsiveness", 20, RegistryValueKind.DWord);
                    }
                }
                
                string activeInterfaceId = GetActiveNetworkInterfaceId();
                if (!string.IsNullOrEmpty(activeInterfaceId))
                {
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey($@"{TCPIP_PARAMS}\{activeInterfaceId}", true))
                    {
                        if (key != null)
                        {
                            key.DeleteValue("TCPNoDelay", false);
                            key.DeleteValue("TcpAckFrequency", false);
                            key.DeleteValue("TcpDelAckTicks", false);
                        }
                    }
                }

                _logger.LogInfo("[CompetitiveNetwork] Configurações de rede retornadas ao padrão da Microsoft.");
            }
            catch { }
        }

        private string GetActiveNetworkInterfaceId()
        {
            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    // Achar interface conectada e do tipo Ethernet/Wi-Fi
                    if (nic.OperationalStatus == OperationalStatus.Up && 
                        (nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet || nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211))
                    {
                        // Evitar pseudo-interfaces virtuais
                        if (nic.Description.ToLower().Contains("virtual") || nic.Description.ToLower().Contains("pseudo"))
                            continue;

                        return nic.Id;
                    }
                }
            }
            catch { }
            return string.Empty;
        }
    }
}
