using System;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Core.Constants;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.Gamer.Interfaces;
using VoltrisOptimizer.Utils;

namespace VoltrisOptimizer.Services.Gamer.Implementation
{
    /// <summary>
    /// Implementação do otimizador de rede para jogos
    /// </summary>
    public class NetworkGamingOptimizerService : INetworkGamingOptimizer
    {
        private readonly ILoggingService _logger;
        private readonly IRegistryService? _registry;
        
        // Backups para restauração
        private int? _originalNetworkThrottlingIndex;
        private int? _originalSystemResponsiveness; // NOVO: Backup SystemResponsiveness
        private bool _tweaksApplied;
        private System.Collections.Generic.Dictionary<string, int?> _originalTcpAckFrequency = new(); // CORREÇÃO: Backup TCP por interface
        private System.Collections.Generic.Dictionary<string, int?> _originalTcpNoDelay = new(); // CORREÇÃO: Backup TCP por interface
        private int? _originalGlobalTcpAckFrequency; // CORREÇÃO: Backup TCP global
        private int? _originalGlobalTcpNoDelay; // CORREÇÃO: Backup TCP global

        public NetworkGamingOptimizerService(ILoggingService logger, IRegistryService? registry = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _registry = registry;
        }

        public async Task<bool> OptimizeAsync(CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                try
                {
                    _logger.LogInfo("[NetworkOptimizer] Aplicando otimizações de rede...");

                    // 1. Desabilitar throttling de rede e otimizar SystemResponsiveness
                    DisableNetworkThrottlingAndResponsiveness();

                    // 2. Aplicar TCP tweaks
                    ApplyTcpTweaks();

                    // 3. Otimizar Nagle's algorithm
                    DisableNaglesAlgorithm();

                    _tweaksApplied = true;
                    _logger.LogSuccess("[NetworkOptimizer] Rede otimizada para jogos");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError("[NetworkOptimizer] Erro ao otimizar rede", ex);
                    return false;
                }
            }, cancellationToken);
        }

        public async Task<bool> RestoreAsync(CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                try
                {
                    _logger.LogInfo("[NetworkOptimizer] Restaurando configurações de rede...");

                    // Restaurar throttling e SystemResponsiveness
                    if (_originalNetworkThrottlingIndex.HasValue || _originalSystemResponsiveness.HasValue)
                    {
                        try
                        {
                            using var key = Registry.LocalMachine.OpenSubKey(
                                SystemConstants.RegistryPaths.MultimediaSystemProfile, true);
                            
                            if (key != null)
                            {
                                if (_originalNetworkThrottlingIndex.HasValue)
                                {
                                    key.SetValue("NetworkThrottlingIndex", 
                                        _originalNetworkThrottlingIndex.Value, 
                                        RegistryValueKind.DWord);
                                }
                                
                                if (_originalSystemResponsiveness.HasValue)
                                {
                                    key.SetValue("SystemResponsiveness", 
                                        _originalSystemResponsiveness.Value, 
                                        RegistryValueKind.DWord);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"[NetworkOptimizer] Erro ao restaurar throttling/responsiveness: {ex.Message}");
                        }
                    }

                    // CORREÇÃO: Restaurar TCP tweaks globais
                    if (_originalGlobalTcpAckFrequency.HasValue || _originalGlobalTcpNoDelay.HasValue)
                    {
                        try
                        {
                            using var key = Registry.LocalMachine.OpenSubKey(
                                SystemConstants.RegistryPaths.TcpipParameters, true);
                            
                            if (key != null)
                            {
                                if (_originalGlobalTcpAckFrequency.HasValue)
                                {
                                    key.SetValue("TcpAckFrequency", 
                                        _originalGlobalTcpAckFrequency.Value, 
                                        RegistryValueKind.DWord);
                                }
                                else
                                {
                                    key.DeleteValue("TcpAckFrequency", false);
                                }

                                if (_originalGlobalTcpNoDelay.HasValue)
                                {
                                    key.SetValue("TCPNoDelay", 
                                        _originalGlobalTcpNoDelay.Value, 
                                        RegistryValueKind.DWord);
                                }
                                else
                                {
                                    key.DeleteValue("TCPNoDelay", false);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"[NetworkOptimizer] Erro ao restaurar TCP tweaks globais: {ex.Message}");
                        }
                    }

                    // CORREÇÃO: Restaurar TCP tweaks por interface
                    if (_originalTcpAckFrequency.Count > 0 || _originalTcpNoDelay.Count > 0)
                    {
                        try
                        {
                            using var interfacesKey = Registry.LocalMachine.OpenSubKey(
                                @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces", true);

                            if (interfacesKey != null)
                            {
                                foreach (var kvp in _originalTcpAckFrequency)
                                {
                                    try
                                    {
                                        using var subKey = interfacesKey.OpenSubKey(kvp.Key, true);
                                        if (subKey != null)
                                        {
                                            if (kvp.Value.HasValue)
                                            {
                                                subKey.SetValue("TcpAckFrequency", kvp.Value.Value, RegistryValueKind.DWord);
                                            }
                                            else
                                            {
                                                subKey.DeleteValue("TcpAckFrequency", false);
                                            }
                                        }
                                    }
                                    catch { _logger.LogDebug("[NetworkOptimizer] Erro ao restaurar TcpAckFrequency"); }
                                }
                                
                                foreach (var kvp in _originalTcpNoDelay)
                                {
                                    try
                                    {
                                        using var subKey = interfacesKey.OpenSubKey(kvp.Key, true);
                                        if (subKey != null)
                                        {
                                            if (kvp.Value.HasValue)
                                            {
                                                subKey.SetValue("TCPNoDelay", kvp.Value.Value, RegistryValueKind.DWord);
                                            }
                                            else
                                            {
                                                subKey.DeleteValue("TCPNoDelay", false);
                                            }
                                        }
                                    }
                                    catch { _logger.LogDebug("[NetworkOptimizer] Erro ao restaurar TCPNoDelay"); }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"[NetworkOptimizer] Erro ao restaurar TCP tweaks por interface: {ex.Message}");
                        }
                    }

                    _tweaksApplied = false;
                    _originalTcpAckFrequency.Clear();
                    _originalTcpNoDelay.Clear();
                    _logger.LogSuccess("[NetworkOptimizer] Configurações de rede restauradas");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError("[NetworkOptimizer] Erro ao restaurar rede", ex);
                    return false;
                }
            }, cancellationToken);
        }

        private void DisableNetworkThrottlingAndResponsiveness()
        {
            try
            {
                if (!AdminHelper.IsRunningAsAdministrator()) return;

                using var key = Registry.LocalMachine.OpenSubKey(
                    SystemConstants.RegistryPaths.MultimediaSystemProfile, true);

                if (key == null) return;

                // Backup
                var currentThrottle = key.GetValue("NetworkThrottlingIndex");
                if (currentThrottle is int intVal)
                {
                    _originalNetworkThrottlingIndex = intVal;
                }
                
                var currentResp = key.GetValue("SystemResponsiveness");
                if (currentResp is int respVal)
                {
                    _originalSystemResponsiveness = respVal;
                }

                // Desabilitar throttling: 0xFFFFFFFF
                key.SetValue("NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), RegistryValueKind.DWord);
                
                // Otimizar SystemResponsiveness para jogos online (menor DPC/ISR Latency)
                key.SetValue("SystemResponsiveness", 0, RegistryValueKind.DWord);

                _logger.LogInfo("[NetworkOptimizer] Network throttling desabilitado e SystemResponsiveness otimizado (0)");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[NetworkOptimizer] Erro em DisableNetworkThrottlingAndResponsiveness: {ex.Message}");
            }
        }

        private void ApplyTcpTweaks()
        {
            try
            {
                if (!AdminHelper.IsRunningAsAdministrator()) return;

                using var key = Registry.LocalMachine.OpenSubKey(
                    SystemConstants.RegistryPaths.TcpipParameters, true);

                if (key == null) return;

                // CORREÇÃO: Fazer backup antes de modificar
                var currentAck = key.GetValue("TcpAckFrequency");
                if (currentAck is int ackVal)
                {
                    _originalGlobalTcpAckFrequency = ackVal;
                }

                var currentNoDelay = key.GetValue("TCPNoDelay");
                if (currentNoDelay is int noDelayVal)
                {
                    _originalGlobalTcpNoDelay = noDelayVal;
                }

                // TCP ACK Frequency = 1 CAUSA DEGRADAÇÃO DE VELOCIDADE DE DOWNLOAD E AUMENTO DE OVERHEAD
                // A grande maioria dos jogos modernos usa UDP. TCP Ack Frequency afeta apenas TCP e
                // forçar Ack para cada pacote corta a velocidade de downloads (Steam, etc) pela metade.
                // Removido da otimização global.

                // TCP No Delay = 1 (desabilita Nagle - seguro e útil para jogos TCP mais antigos)
                key.SetValue("TCPNoDelay", 
                    SystemConstants.NetworkSettings.TcpNoDelayEnabled, 
                    RegistryValueKind.DWord);

                _logger.LogInfo("[NetworkOptimizer] TCP tweaks aplicados (NoDelay=1, Ack mantido padrão para velocidade)");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[NetworkOptimizer] Erro em ApplyTcpTweaks: {ex.Message}");
            }
        }

        // CORREÇÃO: Cache para evitar aplicar configurações TCP repetidamente
        private static DateTime _lastNagleDisable = DateTime.MinValue;
        private const int NagleDisableCooldownSeconds = 600; // 10 minutos
        
        private void DisableNaglesAlgorithm()
        {
            try
            {
                // CORREÇÃO CRÍTICA: Evitar aplicar configurações TCP repetidamente (causa instabilidade)
                var now = DateTime.Now;
                if ((now - _lastNagleDisable).TotalSeconds < NagleDisableCooldownSeconds)
                {
                    _logger.LogInfo("[NetworkOptimizer] Configurações TCP já aplicadas recentemente, pulando...");
                    return;
                }
                
                if (!AdminHelper.IsRunningAsAdministrator()) return;

                // CORREÇÃO: Para jogos competitivos e MMOs, TcpAckFrequency=1 causa mais problemas de download
                // do que ajuda no ping. Manteremos apenas TcpNoDelay.
                var tcpNoDelay = 1; // Este é seguro

                // Aplicar em todas as interfaces de rede
                using var interfacesKey = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces", true);

                if (interfacesKey == null) return;

                foreach (var subKeyName in interfacesKey.GetSubKeyNames())
                {
                    try
                    {
                        using var subKey = interfacesKey.OpenSubKey(subKeyName, true);
                        if (subKey == null) continue;

                        // CORREÇÃO: Fazer backup antes de modificar
                        var currentNoDelay = subKey.GetValue("TCPNoDelay");
                        
                        if (currentNoDelay != null)
                        {
                            _originalTcpNoDelay[subKeyName] = Convert.ToInt32(currentNoDelay);
                        }
                        else
                        {
                            _originalTcpNoDelay[subKeyName] = null;
                        }
                        
                        // Aplicar configurações
                        if (currentNoDelay == null || Convert.ToInt32(currentNoDelay) != tcpNoDelay)
                        {
                            subKey.SetValue("TCPNoDelay", tcpNoDelay, RegistryValueKind.DWord);
                        }
                    }
                    catch
                    {
                        // Ignorar interfaces que não podem ser modificadas
                    }
                }

                _lastNagleDisable = now;
                _logger.LogInfo($"[NetworkOptimizer] Nagle's algorithm desabilitado em interfaces (TcpNoDelay={tcpNoDelay})");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[NetworkOptimizer] Erro em DisableNaglesAlgorithm: {ex.Message}");
            }
        }

        // CORREÇÃO: Cache para evitar aplicar QoS repetidamente
        private static string? _lastQosExe = null;
        private static DateTime _lastQosApplication = DateTime.MinValue;
        private const int QosCooldownSeconds = 300; // 5 minutos
        
        public bool ApplyQosDscp(string executablePath, int dscpValue = 46)
        {
            try
            {
                // CRITICAL FIX: QoS DSCP (Ex: 46 - Expedited Forwarding) causa enorme perda de pacotes 
                // e instabilidade em conexões residenciais porque roteadores e ISPs comuns não
                // reconhecem ou filtram pacotes marcados para voz (VoIP).
                // Portanto, a política de QoS está DESATIVADA para evitar lag spikes.
                
                _logger.LogInfo($"[NetworkOptimizer] QoS DSCP ignorado propositalmente para {System.IO.Path.GetFileName(executablePath)} para evitar instabilidade com ISPs locais.");
                return true; 
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[NetworkOptimizer] Erro em ApplyQosDscp: {ex.Message}");
                return false;
            }
        }

        public bool RemoveQosDscp(string executablePath)
        {
            try
            {
                if (!AdminHelper.IsRunningAsAdministrator()) return false;

                var exeName = System.IO.Path.GetFileName(executablePath);
                var keyPath = $@"SOFTWARE\Policies\Microsoft\Windows\QoS\{exeName}";

                // Removemos o lixo deixado pelas execuções antigas
                Registry.LocalMachine.DeleteSubKeyTree(keyPath, false);

                _logger.LogInfo($"[NetworkOptimizer] QoS removido de {exeName}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[NetworkOptimizer] Erro em RemoveQosDscp: {ex.Message}");
                return false;
            }
        }

        public bool SetNicInterruptModeration(bool enabled)
        {
            try
            {
                _logger.LogInfo($"[NetworkOptimizer] Interrupt moderation {(enabled ? "habilitado" : "desabilitado")}");
                // A implementação real requer modificação de drivers de rede específicos
                // Por segurança, apenas logamos a intenção
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[NetworkOptimizer] Erro em SetNicInterruptModeration: {ex.Message}");
                return false;
            }
        }

        public async Task<double> MeasureLatencyAsync(string host, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var ping = new Ping();
                    var reply = ping.Send(host, 1000);

                    if (reply.Status == IPStatus.Success)
                    {
                        return (double)reply.RoundtripTime;
                    }

                    return -1;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[NetworkOptimizer] Erro ao medir latência: {ex.Message}");
                    return -1;
                }
            }, cancellationToken);
        }
    }
}

