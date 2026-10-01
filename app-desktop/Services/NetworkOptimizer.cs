using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// Serviço de otimização de rede baseado no PS1
    /// </summary>
    public class NetworkOptimizer : INetworkOptimizer
    {
        private readonly ILoggingService _logger;
        
        public NetworkOptimizer(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<bool> FlushDnsAsync(Action<int>? progressCallback = null)
        {
            return await Task.Run(() =>
            {
                try
                {
                    _logger.LogInfo("Limpando cache DNS...");
                    progressCallback?.Invoke(50);
                    
                    var processInfo = new ProcessStartInfo
                    {
                        FileName = "ipconfig.exe",
                        Arguments = "/flushdns",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true
                    };
                    
                    using (var process = Process.Start(processInfo))
                    {
                        process?.WaitForExit();
                    }
                    
                    _logger.LogSuccess("Cache DNS limpo");
                    RecordHistory("FlushDns", "Cache DNS limpo com sucesso", true);
                    progressCallback?.Invoke(100);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError("Erro ao limpar cache DNS", ex);
                    RecordHistory("FlushDns", $"Erro ao limpar DNS: {ex.Message}", false);
                    progressCallback?.Invoke(100);
                    return false;
                }
            });
        }

        public async Task<bool> ResetWinsockAsync(Action<int>? progressCallback = null)
        {
            return await Task.Run(() =>
            {
                try
                {
                    _logger.LogInfo("Redefinindo Winsock...");
                    progressCallback?.Invoke(50);
                    
                    var processInfo = new ProcessStartInfo
                    {
                        FileName = "netsh.exe",
                        Arguments = "winsock reset",
                        UseShellExecute = true, // Necessário para Verb "runas"
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        Verb = "runas"
                    };
                    
                    using (var process = Process.Start(processInfo))
                    {
                        process?.WaitForExit();
                        if (process?.ExitCode != 0)
                        {
                            _logger.LogWarning($"[Network] Winsock reset retornou código de erro: {process?.ExitCode}");
                        }
                    }
                    
                    _logger.LogSuccess("Winsock redefinido. Reinicie o computador para aplicar.");
                    RecordHistory("ResetWinsock", "Catálogo Winsock redefinido", true);
                    progressCallback?.Invoke(100);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError("Erro ao redefinir Winsock", ex);
                    RecordHistory("ResetWinsock", $"Erro ao redefinir Winsock: {ex.Message}", false);
                    progressCallback?.Invoke(100);
                    return false;
                }
            });
        }

        public async Task<bool> ResetIPStackAsync(Action<int>? progressCallback = null)
        {
            return await Task.Run(() =>
            {
                try
                {
                    _logger.LogInfo("Redefinindo pilha IP...");
                    progressCallback?.Invoke(50);
                    
                    var processInfo = new ProcessStartInfo
                    {
                        FileName = "netsh.exe",
                        Arguments = "int ip reset",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        Verb = "runas"
                    };
                    
                    using (var process = Process.Start(processInfo))
                    {
                        if (process != null)
                        {
                            process.WaitForExit(30000); // 30s timeout
                            
                            // netsh int ip reset retorna 0 para sucesso, 1 para "requer reinicialização"
                            // Código 1 é esperado e significa que o reset foi aplicado mas precisa de reboot
                            if (process.ExitCode != 0 && process.ExitCode != 1)
                            {
                                _logger.LogWarning($"[Network] IP Stack reset retornou código de erro inesperado: {process.ExitCode}");
                            }
                            else if (process.ExitCode == 1)
                            {
                                _logger.LogInfo("[Network] IP Stack reset aplicado — reinicialização necessária para efetivar.");
                            }
                        }
                    }
                    
                    _logger.LogSuccess("Pilha IP redefinida. Reinicie o computador para aplicar.");
                    RecordHistory("ResetIPStack", "Pilha TCP/IP redefinida", true);
                    progressCallback?.Invoke(100);
                    return true;
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Fallback: se UseShellExecute=false com Verb=runas falhar, tentar com UseShellExecute=true
                    try
                    {
                        var fallbackInfo = new ProcessStartInfo
                        {
                            FileName = "netsh.exe",
                            Arguments = "int ip reset",
                            UseShellExecute = true,
                            CreateNoWindow = true,
                            WindowStyle = ProcessWindowStyle.Hidden,
                            Verb = "runas"
                        };
                        
                        using var process = Process.Start(fallbackInfo);
                        process?.WaitForExit(30000);
                        
                        _logger.LogSuccess("Pilha IP redefinida (via elevação UAC). Reinicie o computador para aplicar.");
                        RecordHistory("ResetIPStack", "Pilha TCP/IP redefinida", true);
                        progressCallback?.Invoke(100);
                        return true;
                    }
                    catch (Exception fallbackEx)
                    {
                        _logger.LogError("Erro ao redefinir pilha IP (elevação negada)", fallbackEx);
                        RecordHistory("ResetIPStack", $"Erro ao redefinir IP: {fallbackEx.Message}", false);
                        progressCallback?.Invoke(100);
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError("Erro ao redefinir pilha IP", ex);
                    RecordHistory("ResetIPStack", $"Erro ao redefinir IP: {ex.Message}", false);
                    progressCallback?.Invoke(100);
                    return false;
                }
            });
        }

        public async Task<bool> RenewDhcpAsync(Action<int>? progressCallback = null)
        {
            return await Task.Run(() =>
            {
                try
                {
                    _logger.LogInfo("Renovando concessão DHCP...");
                    progressCallback?.Invoke(30);
                    
                    var releaseInfo = new ProcessStartInfo
                    {
                        FileName = "ipconfig.exe",
                        Arguments = "/release",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    
                    using (var process = Process.Start(releaseInfo))
                    {
                        process?.WaitForExit();
                    }
                    
                    progressCallback?.Invoke(60);
                    
                    var renewInfo = new ProcessStartInfo
                    {
                        FileName = "ipconfig.exe",
                        Arguments = "/renew",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    
                    using (var process = Process.Start(renewInfo))
                    {
                        process?.WaitForExit();
                    }
                    
                    _logger.LogSuccess("DHCP renovado");
                    RecordHistory("RenewDhcp", "Concessão DHCP renovada", true);
                    progressCallback?.Invoke(100);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError("Erro ao renovar DHCP", ex);
                    RecordHistory("RenewDhcp", $"Erro ao renovar DHCP: {ex.Message}", false);
                    progressCallback?.Invoke(100);
                    return false;
                }
            });
        }

        /// <summary>
        /// Otimiza configurações TCP para melhor desempenho de rede
        /// Inclui RSS (Receive-Side Scaling), Auto-Tuning e ECN
        /// </summary>
        public async Task<bool> OptimizeTcpSettingsAsync(Action<int>? progressCallback = null)
        {
            return await Task.Run(() =>
            {
                try
                {
                    _logger.LogInfo("Otimizando configurações TCP...");
                    progressCallback?.Invoke(10);

                    // Habilitar RSS (Receive-Side Scaling) para melhor distribuição de carga
                    RunNetshCommand("int tcp set global rss=enabled");
                    progressCallback?.Invoke(25);

                    // Configurar Auto-Tuning Level para normal (melhor desempenho)
                    RunNetshCommand("int tcp set global autotuninglevel=normal");
                    progressCallback?.Invoke(40);

                    // Habilitar Direct Cache Access
                    RunNetshCommand("int tcp set global dca=enabled");
                    progressCallback?.Invoke(55);

                    // Habilitar ECN Capability (Explicit Congestion Notification)
                    RunNetshCommand("int tcp set global ecncapability=enabled");
                    progressCallback?.Invoke(70);

                    // Habilitar timestamps para melhor controle de congestionamento
                    RunNetshCommand("int tcp set global timestamps=enabled");
                    progressCallback?.Invoke(85);

                    // Configurar Initial RTO para 2000ms (mais agressivo)
                    RunNetshCommand("int tcp set global initialrto=2000");
                    progressCallback?.Invoke(100);

                    _logger.LogSuccess("Configurações TCP otimizadas (RSS, Auto-Tuning, ECN habilitados)");
                    RecordHistory("OptimizeTcpSettings", "Parâmetros TCP globais otimizados", true);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError("Erro ao otimizar configurações TCP", ex);
                    RecordHistory("OptimizeTcpSettings", $"Erro ao otimizar TCP: {ex.Message}", false);
                    progressCallback?.Invoke(100);
                    return false;
                }
            });
        }

        public async Task<bool> SetDnsAsync(string interfaceName, string[] dnsv4, string[] dnsv6)
        {
            return await Task.Run(() =>
            {
                try
                {
                    _logger.LogInfo($"[Network] Aplicando DNS estático em: {interfaceName}");

                    // IPv4 Primary
                    if (dnsv4 != null && dnsv4.Length > 0 && !string.IsNullOrWhiteSpace(dnsv4[0]))
                    {
                        RunNetshCommand($"interface ipv4 set dnsservers \"{interfaceName}\" static {dnsv4[0]} primary");
                    }

                    // IPv4 Secondary
                    if (dnsv4 != null && dnsv4.Length > 1 && !string.IsNullOrWhiteSpace(dnsv4[1]))
                    {
                        RunNetshCommand($"interface ipv4 add dnsservers \"{interfaceName}\" {dnsv4[1]} index=2");
                    }

                    // IPv6 Primary
                    if (dnsv6 != null && dnsv6.Length > 0 && !string.IsNullOrWhiteSpace(dnsv6[0]))
                    {
                        RunNetshCommand($"interface ipv6 set dnsservers \"{interfaceName}\" static {dnsv6[0]} primary");
                    }

                    // IPv6 Secondary
                    if (dnsv6 != null && dnsv6.Length > 1 && !string.IsNullOrWhiteSpace(dnsv6[1]))
                    {
                        RunNetshCommand($"interface ipv6 add dnsservers \"{interfaceName}\" {dnsv6[1]} index=2");
                    }

                    _logger.LogSuccess($"[Network] DNS aplicado com sucesso em {interfaceName}");
                    RecordHistory("SetDns", $"DNS customizado aplicado em {interfaceName}", true);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[Network] Erro ao aplicar DNS em {interfaceName}", ex);
                    RecordHistory("SetDns", $"Erro ao aplicar DNS em {interfaceName}: {ex.Message}", false);
                    return false;
                }
            });
        }

        public async Task<bool> ResetDnsAsync(string interfaceName)
        {
            return await Task.Run(() =>
            {
                try
                {
                    _logger.LogInfo($"[Network] Resetando DNS para DHCP em: {interfaceName}");

                    RunNetshCommand($"interface ipv4 set dnsservers \"{interfaceName}\" dhcp");
                    RunNetshCommand($"interface ipv6 set dnsservers \"{interfaceName}\" dhcp");

                    _logger.LogSuccess($"[Network] DNS de {interfaceName} restaurado para o padrão (DHCP)");
                    RecordHistory("ResetDns", $"DNS de {interfaceName} restaurado para DHCP", true);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[Network] Erro ao resetar DNS em {interfaceName}", ex);
                    RecordHistory("ResetDns", $"Erro ao restaurar DNS para DHCP em {interfaceName}: {ex.Message}", false);
                    return false;
                }
            });
        }

        private void RecordHistory(string action, string description, bool success)
        {
            try
            {
                var entry = new OptimizationHistory
                {
                    ActionType = "Network",
                    Description = description,
                    Timestamp = DateTime.Now,
                    Success = success,
                    Details = new Dictionary<string, object> { { "Action", action } }
                };
                HistoryService.Instance.AddHistoryEntry(entry);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Falha ao gravar histórico: {ex.Message}");
            }
        }

        private bool RunNetshCommand(string arguments)
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = "netsh.exe",
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = Process.Start(processInfo);
                if (process != null)
                {
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit(5000);
                    
                    if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(error))
                    {
                        _logger.LogWarning($"[Network] Falha no comando netsh: {arguments}. Erro: {error.Trim()}");
                    }
                    
                    return process.ExitCode == 0;
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Network] Erro ao executar netsh: {ex.Message}");
                return false;
            }
        }
    }
}

