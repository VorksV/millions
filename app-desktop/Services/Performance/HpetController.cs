using System;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace VoltrisOptimizer.Services.Performance
{
    /// <summary>
    /// Controlador do HPET (High Precision Event Timer)
    /// 
    /// POLÍTICA INTELIGENTE DE HARDWARE:
    /// - Hardware LEGADO (Intel pré-8ª gen / AMD pré-Ryzen 1000): desativar HPET tem impacto real
    ///   pois o TSC nesses chips não é invariante e o Windows usa HPET como fallback.
    /// - Hardware MODERNO (Intel 8ª gen+ / Ryzen+): TSC invariante é o clock source padrão.
    ///   Desativar o dispositivo via pnputil pode causar instabilidade sem benefício.
    ///
    /// Referências: Intel SDM Vol.3 §17.17, Microsoft KB2619234, Windows Internals 7th ed.
    /// </summary>
    public class HpetController : IDisposable
    {
        private readonly ILoggingService _logger;
        private bool _wasHpetEnabled = false;
        private bool _hpetStateChanged = false;
        private const string HPET_DEVICE_ID = "ACPI\\PNP0103";

        // Cache da decisão de hardware — calculado uma vez no construtor
        private readonly bool _hpetRelevantForThisHardware;

        public HpetController(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _hpetRelevantForThisHardware = IsHpetRelevantForCurrentHardware();
        }

        /// <summary>
        /// Detecta se desativar HPET tem impacto real neste hardware.
        /// Retorna true apenas para hardware legado onde o TSC não é invariante.
        /// </summary>
        private bool IsHpetRelevantForCurrentHardware()
        {
            try
            {
                string cpuName = "";

                using var searcher = new ManagementObjectSearcher("SELECT Name, Family, Stepping FROM Win32_Processor");
                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    cpuName = obj["Name"]?.ToString()?.ToUpperInvariant() ?? "";
                    // Family não está disponível diretamente via WMI de forma confiável — usar nome
                    break;
                }

                // Intel: TSC invariante garantido a partir de Nehalem (2008) / Sandy Bridge (2011).
                // Na prática, qualquer CPU Intel com geração >= 4 (Haswell 2013+) tem TSC invariante
                // e o Windows usa TSC como clock source. HPET é irrelevante.
                // Gerações identificadas pelo nome: "i3/i5/i7/i9-XYYY" onde X = geração.
                if (cpuName.Contains("INTEL"))
                {
                    // Detectar geração pelo padrão "i[3579]-[geração]XXX" ou "CORE [geração]"
                    int gen = DetectIntelGeneration(cpuName);
                    if (gen >= 4)
                    {
                        _logger.LogInfo($"[HPET] Intel geração {gen} detectada — TSC invariante, HPET irrelevante. Tweak ignorado.");
                        return false;
                    }
                    if (gen > 0)
                    {
                        _logger.LogInfo($"[HPET] Intel geração {gen} (legado) — HPET pode ter impacto.");
                        return true;
                    }
                    // Não conseguiu detectar geração — assumir moderno (seguro)
                    _logger.LogInfo("[HPET] Intel sem geração identificada — assumindo moderno, HPET ignorado.");
                    return false;
                }

                // AMD: TSC invariante garantido a partir de Ryzen (Zen 1, 2017).
                // Bulldozer/Piledriver/Steamroller (2011-2014) NÃO têm TSC invariante.
                if (cpuName.Contains("AMD"))
                {
                    bool isZenOrNewer = cpuName.Contains("RYZEN") || cpuName.Contains("EPYC") ||
                                       cpuName.Contains("THREADRIPPER") || cpuName.Contains("ZEN");
                    if (isZenOrNewer)
                    {
                        _logger.LogInfo("[HPET] AMD Ryzen/Zen detectado — TSC invariante, HPET irrelevante. Tweak ignorado.");
                        return false;
                    }
                    // AMD legado (FX, A-series, Phenom) — HPET pode ter impacto
                    _logger.LogInfo("[HPET] AMD legado detectado — HPET pode ter impacto.");
                    return true;
                }

                // Hardware desconhecido — não arriscar, ignorar HPET
                _logger.LogInfo($"[HPET] CPU não identificada ({cpuName}) — HPET ignorado por segurança.");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[HPET] Erro na detecção de hardware: {ex.Message} — HPET ignorado.");
                return false;
            }
        }

        /// <summary>
        /// Extrai a geração de uma CPU Intel pelo nome (ex: "Core i7-8700K" → 8).
        /// </summary>
        private static int DetectIntelGeneration(string cpuName)
        {
            // Padrão: "i[3579]-[1-4 dígitos]" — primeiro dígito(s) = geração
            // Ex: i7-8700 → gen 8 | i9-13900 → gen 13 | i5-2500 → gen 2
            var match = System.Text.RegularExpressions.Regex.Match(
                cpuName, @"I[3579]-(\d{1})(\d{3})");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int gen))
                return gen;

            // Padrão de 5 dígitos: i9-10900 → gen 10
            var match2 = System.Text.RegularExpressions.Regex.Match(
                cpuName, @"I[3579]-(\d{2})(\d{3})");
            if (match2.Success && int.TryParse(match2.Groups[1].Value, out int gen2))
                return gen2;

            // Core Ultra (gen 12+): "CORE ULTRA"
            if (cpuName.Contains("CORE ULTRA")) return 12;

            return 0; // Não identificado
        }

        /// <summary>
        /// Desativa o HPET para reduzir latência em jogos.
        /// Em hardware moderno, retorna true sem fazer nada (tweak sem efeito).
        /// </summary>
        public bool DisableHpet()
        {
            // Hardware moderno: TSC invariante, HPET não é o clock source ativo — skip silencioso.
            if (!_hpetRelevantForThisHardware)
            {
                _logger.LogInfo("[HPET] Hardware moderno detectado — tweak HPET ignorado (sem impacto real neste sistema).");
                return true;
            }

            try
            {
                _logger.LogInfo("[HPET] 🔧 Hardware legado — verificando estado do HPET...");

                _wasHpetEnabled = IsHpetEnabled();
                _logger.LogInfo($"[HPET] Estado atual: {(_wasHpetEnabled ? "Ativado" : "Desativado")}");

                if (!_wasHpetEnabled)
                {
                    _logger.LogInfo("[HPET] HPET já está desativado, nenhuma ação necessária");
                    return true;
                }

                bool deviceResult = DisableHpetDevice();

                if (deviceResult)
                {
                    _hpetStateChanged = true;
                    _logger.LogSuccess("[HPET] ✅ HPET desativado via DeviceManager (hardware legado confirmado)");
                    return true;
                }
                else
                {
                    _logger.LogWarning("[HPET] ⚠️ Não foi possível desativar HPET");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[HPET] ❌ Erro ao desativar HPET: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Restaura o HPET ao estado original
        /// </summary>
        public bool RestoreHpet()
        {
            try
            {
                if (!_hpetStateChanged)
                {
                    _logger.LogInfo("[HPET] Nenhuma mudança foi feita, nada a restaurar");
                    return true;
                }
                
                if (!_wasHpetEnabled)
                {
                    _logger.LogInfo("[HPET] HPET estava desativado originalmente, mantendo desativado");
                    return true;
                }
                
                _logger.LogInfo("[HPET] 🔄 Restaurando HPET ao estado original...");
                
                // Reativar dispositivo no Device Manager
                _logger.LogInfo("[HPET] Reativando dispositivo HPET no Device Manager...");
                bool deviceResult = EnableHpetDevice();
                
                if (deviceResult)
                {
                    _hpetStateChanged = false;
                    _logger.LogSuccess("[HPET] ✅ HPET restaurado ao estado original");
                    return true;
                }
                else
                {
                    _logger.LogWarning("[HPET] ⚠️ Não foi possível restaurar HPET completamente");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[HPET] ❌ Erro ao restaurar HPET: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Verifica se o HPET está ativado via WMI (Device Manager)
        /// </summary>
        private bool IsHpetEnabled()
        {
            try
            {
                // Verificar dispositivo no Device Manager via WMI
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE DeviceID LIKE '%PNP0103%'"))
                {
                    foreach (ManagementObject device in searcher.Get())
                    {
                using var __dispose_device = device;
                        var status = device["Status"]?.ToString();
                        var configManagerErrorCode = device["ConfigManagerErrorCode"]?.ToString();
                        
                        // Se Status = "OK" e ConfigManagerErrorCode = "0", dispositivo está ativado
                        if (status == "OK" && configManagerErrorCode == "0")
                        {
                            return true;
                        }
                    }
                }
                
                return false;
            }
            catch
            {
                // Fallback: verificar no Registry se o dispositivo existe
                try
                {
                    using var hpetKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                        @"SYSTEM\CurrentControlSet\Services\Hpets\Parameters");
                    if (hpetKey != null)
                    {
                        _logger.LogInfo("[HPET] HPET encontrado no Registry - dispositivo existe");
                        return true;
                    }
                }
                catch { }
                
                // Se não encontrou nem no WMI nem no Registry, assume que não existe
                _logger.LogInfo("[HPET] HPET não encontrado no WMI nem Registry - dispositivo não existe");
                return false;
            }
        }



        /// <summary>
        /// Desativa dispositivo HPET no Device Manager via pnputil (método confiável).
        /// Win32_PnPEntity.InvokeMethod("Disable") retorna null em muitos sistemas — NullRef garantido.
        /// </summary>
        private bool DisableHpetDevice()
        {
            try
            {
                string? deviceId = GetHpetDeviceId();
                if (deviceId == null)
                {
                    _logger.LogInfo("[HPET] Dispositivo HPET não encontrado no Device Manager.");
                    return false;
                }

                _logger.LogInfo($"[HPET] Tentando desativar dispositivo: {deviceId}");

                var psi = new ProcessStartInfo("pnputil",
                    $"/disable-device \"{deviceId}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    _logger.LogWarning("[HPET] Não foi possível iniciar pnputil.");
                    return false;
                }

                var stdout = proc.StandardOutput.ReadToEnd();
                var stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit(10000);

                _logger.LogInfo($"[HPET] pnputil disable — ExitCode={proc.ExitCode} stdout={stdout.Trim()} stderr={stderr.Trim()}");

                if (proc.ExitCode == 0 || proc.ExitCode == 3010) // 3010 = Reboot required
                {
                    _logger.LogSuccess("[HPET] ✅ Dispositivo HPET desativado com sucesso no Device Manager.");
                    return true;
                }
                else if (proc.ExitCode == -536870389) // ERROR_NO_SUCH_DEVINST or similar
                {
                    _logger.LogWarning($"[HPET] pnputil falhou ao encontrar a instância '{deviceId}'. Tentando BCD...");
                    return DisableHpetBcd();
                }
                else
                {
                    _logger.LogWarning($"[HPET] ⚠️ pnputil retornou ExitCode={proc.ExitCode} — erro inesperado.");
                    _logger.LogWarning($"[HPET] ⚠️ Stdout: {stdout.Trim()}");
                    _logger.LogWarning($"[HPET] ⚠️ Stderr: {stderr.Trim()}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[HPET] Erro ao desativar dispositivo: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Reativa dispositivo HPET no Device Manager via pnputil.
        /// </summary>
        private bool EnableHpetDevice()
        {
            try
            {
                string? deviceId = GetHpetDeviceId();
                if (deviceId == null) deviceId = HPET_DEVICE_ID; // fallback

                _logger.LogInfo($"[HPET] Reativando dispositivo HPET via pnputil ({deviceId})...");

                var psi = new ProcessStartInfo("pnputil",
                    $"/enable-device \"{deviceId}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return false;

                var stdout = proc.StandardOutput.ReadToEnd();
                var stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit(10000);

                _logger.LogInfo($"[HPET] pnputil enable — ExitCode={proc.ExitCode} stdout={stdout.Trim()} stderr={stderr.Trim()}");

                if (proc.ExitCode == 0 || proc.ExitCode == 3010)
                {
                    _logger.LogSuccess("[HPET] ✅ Dispositivo HPET reativado com sucesso.");
                    return true;
                }
                else if (proc.ExitCode == -536870389)
                {
                    _logger.LogWarning("[HPET] falhou no pnputil, restaurando via BCD...");
                    return EnableHpetBcd();
                }
                else
                {
                    _logger.LogWarning($"[HPET] ⚠️ pnputil enable retornou ExitCode={proc.ExitCode}.");
                    _logger.LogWarning($"[HPET] ⚠️ Stdout: {stdout.Trim()}");
                    _logger.LogWarning($"[HPET] ⚠️ Stderr: {stderr.Trim()}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[HPET] Erro ao reativar dispositivo: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Retorna o DeviceID exato do HPET (ex: ACPI\PNP0103\4&2b1d03c6&0).
        /// </summary>
        private string? GetHpetDeviceId()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT DeviceID FROM Win32_PnPEntity WHERE DeviceID LIKE '%PNP0103%'");
                using var collection = searcher.Get();
                foreach (ManagementObject device in collection)
                {
                    if (device == null) continue;
                    return device["DeviceID"]?.ToString();
                }
                return null;
            }
            catch
            {
                return null;
            }
        }


        private bool DisableHpetBcd()
        {
            try
            {
                _logger.LogInfo("[HPET] Desativando via BCD (bcdedit /deletevalue useplatformclock)...");
                var psi = new ProcessStartInfo("bcdedit", "/deletevalue useplatformclock")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var proc = Process.Start(psi);
                if (proc == null) return false;
                proc.WaitForExit(5000);
                
                if (proc.ExitCode == 0 || proc.StandardError.ReadToEnd().Contains("não foi encontrado") || proc.StandardError.ReadToEnd().Contains("not found"))
                {
                    _logger.LogSuccess("[HPET] ✅ BCD configurado para não usar HPET.");
                    return true;
                }
                
                _logger.LogWarning($"[HPET] Falha no BCD ExitCode={proc.ExitCode}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[HPET] Exceção no BCD Disable: {ex.Message}");
                return false;
            }
        }

        private bool EnableHpetBcd()
        {
            try
            {
		_logger.LogInfo("[HPET] Restaurando relogio padrao do Windows (bcdedit /deletevalue useplatformclock)...");

		// BUG CORRIGIDO: antes isto rodava "bcdedit /set useplatformclock true",
		// que REFORCA o Platform Clock. Esse e o ajuste que causa queda
		// comprovada de desempenho: a comunidade reporta 165 fps constantes
		// caindo para menos de 100, com quedas de 3 fps, e o problema so
		// desaparece ao DELETAR o valor.
		//
		// O estado saudavel e o padrao do Windows: TSC sem desync, que e o que
		// o jogo espera. "Reativar HPET" deveria, portanto, restaurar o
		// padrao — nunca forcar o relogio de plataforma.
		var psi = new ProcessStartInfo("bcdedit", "/deletevalue useplatformclock")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var proc = Process.Start(psi);
                if (proc == null) return false;
                proc.WaitForExit(5000);
                
                if (proc.ExitCode == 0)
                {
                    _logger.LogSuccess("[HPET] ✅ BCD configurado para usar HPET.");
                    return true;
                }
                
                _logger.LogWarning($"[HPET] Falha no BCD Enable ExitCode={proc.ExitCode}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[HPET] Exceção no BCD Enable: {ex.Message}");
                return false;
            }
        }

        public void Dispose()
        {
            // Garantir restauração ao descartar
            if (_hpetStateChanged)
            {
                _logger.LogWarning("[HPET] Dispose chamado com mudanças pendentes, restaurando...");
                RestoreHpet();
            }
        }
    }
}
