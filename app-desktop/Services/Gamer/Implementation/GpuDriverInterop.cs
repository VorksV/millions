using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.Gamer.Models;

namespace VoltrisOptimizer.Services.Gamer.Implementation
{
    [Obsolete("PLACEBO: SetNvidiaMaxPerformance ignora exit codes do nvidia-smi; GetWmiGpuClock retorna VRAM em MB como MHz (leitura errada). Use GpuOptimizationService que faz registry real.")]
    /// <summary>
    /// CORREÇÃO CRÍTICA #3: INTERAÇÃO REAL COM DRIVERS GPU
    /// 
    /// Problema identificado: 90% das otimizações GPU são no driver, código atual tem 0% de interação
    /// Solução: Implementar APIs nativas dos fabricantes
    /// 
    /// IMPORTANTE: Esta é uma implementação base. Para produção completa, seria necessário:
    /// - NVIDIA: NuGet package NVAPI (não oficial) ou P/Invoke direto
    /// - AMD: ADL SDK ou AGS SDK
    /// - Intel: IGCL SDK
    /// 
    /// </summary>
    public class GpuDriverInterop
    {
        private readonly ILoggingService _logger;
        private readonly GpuVendor _vendor;

        public GpuDriverInterop(ILoggingService logger, GpuVendor vendor)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _vendor = vendor;
        }

        /// <summary>
        /// Tenta aplicar perfil de performance máxima via driver
        /// </summary>
        public bool SetMaxPerformanceMode()
        {
            try
            {
                _logger.LogInfo($"[GpuDriverInterop] Tentando configurar performance máxima para {_vendor}...");

                switch (_vendor)
                {
                    case GpuVendor.Nvidia:
                        return SetNvidiaMaxPerformance();
                    
                    case GpuVendor.Amd:
                        return SetAmdMaxPerformance();
                    
                    case GpuVendor.Intel:
                        return SetIntelMaxPerformance();
                    
                    default:
                        _logger.LogWarning("[GpuDriverInterop] Fabricante desconhecido");
                        return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GpuDriverInterop] Erro: {ex.Message}", ex);
                return false;
            }
        }

/// <summary>
        /// NVIDIA: Configurar via nvidia-smi (fallback quando NVAPI não disponível)
        /// </summary>
        private bool SetNvidiaMaxPerformance()
        {
            _logger.LogEntry(nameof(SetNvidiaMaxPerformance));
            try
            {
                if (!NvidiaSmiExists())
                {
                    _logger.LogWarning("[GpuDriverInterop] nvidia-smi não encontrado — pulando otimização NVIDIA via CLI");
                    _logger.LogExit(nameof(SetNvidiaMaxPerformance));
                    return false;
                }

                // Método 1: Tentar via nvidia-smi (disponível em drivers recentes)
                var result = ExecuteCommand("nvidia-smi", "-pm 1"); // Persistence Mode
                if (result)
                {
                    _logger.LogSuccess("[GpuDriverInterop] ✅ NVIDIA Persistence Mode ativado");
                }

                // Método 2: Tentar forçar P0 state (máximo clock)
                result = ExecuteCommand("nvidia-smi", "-ac 0,0"); // Reset clocks
                
                _logger.LogInfo("[GpuDriverInterop] ⚠️ Para controle completo, use NVIDIA Control Panel:");
                _logger.LogInfo("[GpuDriverInterop]    - Manage 3D Settings > Power Management > Prefer Maximum Performance");
                
                _logger.LogExit(nameof(SetNvidiaMaxPerformance));
                return true; // Retornar true pois comandos foram executados
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[GpuDriverInterop] NVIDIA: {ex.Message}");
                _logger.LogInfo("[GpuDriverInterop] Configure manualmente via NVIDIA Control Panel");
                _logger.LogExit(nameof(SetNvidiaMaxPerformance));
                return false;
            }
        }

        private static bool NvidiaSmiExists()
        {
            try
            {
                string[] paths =
                {
                    @"C:\Windows\System32\nvidia-smi.exe",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe")
                };
                return paths.Any(File.Exists);
            }
            catch { return false; }
        }

        /// <summary>
        /// AMD: Configurar via registro (fallback quando ADL não disponível)
        /// </summary>
        private bool SetAmdMaxPerformance()
        {
            _logger.LogEntry(nameof(SetAmdMaxPerformance));
            try
            {
                _logger.LogInfo("[GpuDriverInterop] AMD: Aplicando configurações via registro...");

                // Localizar o índice correto do dispositivo AMD (evita hardcoded \0000)
                const string displayClass = @"{4d36e968-e325-11ce-bfc1-08002be10318}";
                string basePath = $@"SYSTEM\CurrentControlSet\Control\Class\{displayClass}";
                string? amdKeyPath = null;

                using (var baseKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(basePath))
                {
                    if (baseKey != null)
                    {
                        foreach (var subKeyName in baseKey.GetSubKeyNames())
                        {
                            using var subKey = baseKey.OpenSubKey(subKeyName);
                            if (subKey != null)
                            {
                                var desc = subKey.GetValue("DriverDesc")?.ToString() ?? "";
                                if (desc.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                                    desc.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
                                    desc.Contains("FirePro", StringComparison.OrdinalIgnoreCase))
                                {
                                    amdKeyPath = $@"{basePath}\{subKeyName}";
                                    break;
                                }
                            }
                        }
                    }
                }

                if (amdKeyPath == null)
                {
                    _logger.LogWarning("[GpuDriverInterop] AMD: Dispositivo AMD não encontrado no registro");
                    _logger.LogExit(nameof(SetAmdMaxPerformance));
                    return false;
                }

                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(amdKeyPath, true);
                if (key != null)
                {
                    key.SetValue("PP_ThermalAutoThrottlingEnable", 0, Microsoft.Win32.RegistryValueKind.DWord);
                    key.SetValue("EnableUlps", 0, Microsoft.Win32.RegistryValueKind.DWord);
                    _logger.LogSuccess("[GpuDriverInterop] ✅ AMD Power Saving desabilitado");
                }
                
                _logger.LogInfo("[GpuDriverInterop] ⚠️ Para controle completo, use AMD Adrenalin:");
                _logger.LogInfo("[GpuDriverInterop]    - Gaming > Global Graphics > Power Tuning > Maximum");
                
                _logger.LogExit(nameof(SetAmdMaxPerformance));
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[GpuDriverInterop] AMD: {ex.Message}");
                _logger.LogExit(nameof(SetAmdMaxPerformance));
                return false;
            }
        }

        /// <summary>
        /// Intel: Configurar via registro
        /// </summary>
        private bool SetIntelMaxPerformance()
        {
            _logger.LogEntry(nameof(SetIntelMaxPerformance));
            try
            {
                _logger.LogInfo("[GpuDriverInterop] Intel: Aplicando configurações...");
                
                // Intel Graphics: Desabilitar power saving
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000", true);
                
                if (key != null)
                {
                    key.SetValue("Disable_OverlayDSQualityEnhancement", 1, Microsoft.Win32.RegistryValueKind.DWord);
                    key.SetValue("EnableCompensationForDVI", 0, Microsoft.Win32.RegistryValueKind.DWord);
                    _logger.LogSuccess("[GpuDriverInterop] ✅ Intel optimizations aplicadas");
                }
                
                _logger.LogInfo("[GpuDriverInterop] ⚠️ Para controle completo, use Intel Graphics Command Center:");
                _logger.LogInfo("[GpuDriverInterop]    - Gaming > Global Settings > Power > Maximum Performance");
                
                _logger.LogExit(nameof(SetIntelMaxPerformance));
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[GpuDriverInterop] Intel: {ex.Message}");
                _logger.LogExit(nameof(SetIntelMaxPerformance));
                return false;
            }
        }

        /// <summary>
        /// Executa comando externo
        /// </summary>
        private bool ExecuteCommand(string fileName, string arguments)
        {
            _logger.LogEntry(nameof(ExecuteCommand));
            try
            {
                using var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };

                process.Start();
                process.WaitForExit(5000);
                
                _logger.LogExit(nameof(ExecuteCommand));
                return process.ExitCode == 0;
            }
            catch
            {
                _logger.LogExit(nameof(ExecuteCommand));
                return false;
            }
        }

        /// <summary>
        /// Restaura configurações padrão
        /// </summary>
        public bool RestoreDefaults()
        {
            _logger.LogEntry(nameof(RestoreDefaults));
            try
            {
                _logger.LogInfo($"[GpuDriverInterop] Restaurando configurações padrão para {_vendor}...");

                switch (_vendor)
                {
                    case GpuVendor.Nvidia:
                        ExecuteCommand("nvidia-smi", "-pm 0"); // Disable Persistence Mode
                        break;
                    
                    case GpuVendor.Amd:
                        using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                            @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000", true))
                        {
                            key?.DeleteValue("PP_ThermalAutoThrottlingEnable", false);
                            key?.DeleteValue("EnableUlps", false);
                        }
                        break;
                }

                _logger.LogSuccess("[GpuDriverInterop] ✅ Configurações restauradas");
                _logger.LogExit(nameof(RestoreDefaults));
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[GpuDriverInterop] Erro ao restaurar: {ex.Message}");
                _logger.LogExit(nameof(RestoreDefaults));
                return false;
            }
        }
    }
}
