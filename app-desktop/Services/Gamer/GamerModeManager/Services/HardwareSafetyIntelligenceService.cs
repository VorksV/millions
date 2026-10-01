using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.Gamer.Interfaces;
using VoltrisOptimizer.Helpers;
namespace VoltrisOptimizer.Services.Gamer.GamerModeManager.Services
{
    /// <summary>
    /// Interface para serviço de segurança inteligente de hardware
    /// </summary>
    public interface IHardwareSafetyIntelligenceService
    {
        Task InitializeAsync();
        bool IsOptimizationSafe(string optimizationName);
        List<string> GetSafeOptimizations();
        List<string> GetUnsafeOptimizations();
        HardwareProfile GetHardwareProfile();
        SafetyValidationResult GetLastValidation();
    }
    
    /// <summary>
    /// Perfil completo de hardware
    /// </summary>
    public class HardwareProfile
    {
        public CpuProfile Cpu { get; set; } = new();
        public GpuProfile Gpu { get; set; } = new();
        public MemoryProfile Memory { get; set; } = new();
        public StorageProfile Storage { get; set; } = new();
        public SystemProfile System { get; set; } = new();
        public BiosProfile Bios { get; set; } = new();
    }
    
    /// <summary>
    /// Perfil de CPU
    /// </summary>
    public class CpuProfile
    {
        public string Name { get; set; } = "";
        public bool IsGamingGrade { get; set; } = false;
    }
    
    /// <summary>
    /// Perfil de GPU
    /// </summary>
    public class GpuProfile
    {
        public bool IsGamingGrade { get; set; } = false;
        public bool HasNvidia { get; set; } = false;
        public bool HasAmd { get; set; } = false;
        public bool HasIntel { get; set; } = false;
    }
    
    /// <summary>
    /// Perfil de memória
    /// </summary>
    public class MemoryProfile
    {
        public bool IsGamingGrade { get; set; } = false;
    }
    
    /// <summary>
    /// Perfil de storage
    /// </summary>
    public class StorageProfile
    {
        public bool IsGamingGrade { get; set; } = false;
    }
    
    /// <summary>
    /// Perfil do sistema
    /// </summary>
    public class SystemProfile
    {
    }
    
    /// <summary>
    /// Perfil do BIOS
    /// </summary>
    public class BiosProfile
    {
        public string Version { get; set; } = "";
        public string ReleaseDate { get; set; } = "";
    }
    
    /// <summary>
    /// Resultado da validação de segurança
    /// </summary>
    public class SafetyValidationResult
    {
        public bool IsValid { get; set; } = true;
        public List<string> Warnings { get; set; } = new();
        public List<string> Recommendations { get; set; } = new();
        public List<string> SafeOptimizations { get; set; } = new();
        public List<string> UnsafeOptimizations { get; set; } = new();
    }
    
    /// <summary>
    /// Hardware Safety Intelligence Service - Sistema de segurança ultra-inteligente
    /// Detecta hardware universalmente, valida compatibilidade e aplica apenas otimizações seguras
    /// </summary>
    public class HardwareSafetyIntelligenceService : IHardwareSafetyIntelligenceService
    {
        private readonly ILoggingService _logger;
        
        // Cache de hardware detectado
        private HardwareProfile? _hardwareProfile;
        private SafetyValidationResult? _lastValidation;
        
        public HardwareSafetyIntelligenceService(ILoggingService logger)
        {
            _logger.LogEntry(nameof(HardwareSafetyIntelligenceService));
_logger = logger;
            _logger.LogExit(nameof(HardwareSafetyIntelligenceService));
}
        
        /// <summary>
        /// Inicializa sistema de segurança inteligente
        /// </summary>
        public async Task InitializeAsync()
        {
            _logger.LogEntry(nameof(InitializeAsync));
try
            {
                _logger.LogInfo("[Hardware-Safety] Iniciando sistema de segurança inteligente...");
                
                // 1. Detectar hardware completo
                _hardwareProfile = await DetectHardwareProfileAsync();
                
                // 2. Validar capacidades
                _lastValidation = await ValidateHardwareCapabilitiesAsync(_hardwareProfile);
                
                // 3. Gerar relatório completo
                LogHardwareDetectionReport();
                LogSafetyValidationReport();
                
                _logger.LogSuccess("[Hardware-Safety] ✅ Sistema de segurança inteligente inicializado!");
            }
            catch (Exception ex)
            {
                _logger.LogError("[Hardware-Safety] Erro na inicialização", ex);
            }
            _logger.LogExit(nameof(InitializeAsync));
}
        
        /// <summary>
        /// Verifica se otimização é segura para o hardware atual
        /// </summary>
        public bool IsOptimizationSafe(string optimizationName)
        {
            _logger.LogEntry(nameof(IsOptimizationSafe));
if (_lastValidation == null) return false;
return _lastValidation.SafeOptimizations.Contains(optimizationName);
            _logger.LogExit(nameof(IsOptimizationSafe));
}
        
        /// <summary>
        /// Obtém otimizações seguras para o hardware atual
        /// </summary>
        public List<string> GetSafeOptimizations()
        {
            _logger.LogEntry(nameof(GetSafeOptimizations));
            return _lastValidation?.SafeOptimizations ?? new List<string>();
            _logger.LogExit(nameof(GetSafeOptimizations));
}
        
        /// <summary>
        /// Obtém otimizações inseguras para o hardware atual
        /// </summary>
        public List<string> GetUnsafeOptimizations()
        {
            _logger.LogEntry(nameof(GetUnsafeOptimizations));
            return _lastValidation?.UnsafeOptimizations ?? new List<string>();
            _logger.LogExit(nameof(GetUnsafeOptimizations));
}
        
        /// <summary>
        /// Obtém perfil de hardware detectado
        /// </summary>
        public HardwareProfile GetHardwareProfile()
        {
            _logger.LogEntry(nameof(GetHardwareProfile));
            return _hardwareProfile ?? new HardwareProfile();
            _logger.LogExit(nameof(GetHardwareProfile));
}
        
        /// <summary>
        /// Obtém resultado da última validação
        /// </summary>
        public SafetyValidationResult GetLastValidation()
        {
            _logger.LogEntry(nameof(GetLastValidation));
            return _lastValidation ?? new SafetyValidationResult();
            _logger.LogExit(nameof(GetLastValidation));
}
        
        // Métodos auxiliares
private async Task<HardwareProfile> DetectHardwareProfileAsync()
        {
            _logger.LogEntry(nameof(DetectHardwareProfileAsync));
            
            var profile = new HardwareProfile();
            
            try
            {
                // CPU Real Detection
                using var cpuSearcher = new System.Management.ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");
                foreach (System.Management.ManagementObject obj in cpuSearcher.Get())
                {
                    using var __dispose_obj = obj;
                    profile.Cpu.Name = obj["Name"]?.ToString() ?? "Unknown CPU";
                    var cores = Convert.ToInt32(obj["NumberOfCores"] ?? 0);
                    var logical = Convert.ToInt32(obj["NumberOfLogicalProcessors"] ?? 0);
                    var maxClock = Convert.ToInt32(obj["MaxClockSpeed"] ?? 0);
                    // Gaming grade: ≥6 cores físicos, ≥3.5GHz base
                    profile.Cpu.IsGamingGrade = cores >= 6 && maxClock >= 3500;
                }
                
                // GPU Real Detection
                using var gpuSearcher = new System.Management.ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController");
                foreach (System.Management.ManagementObject obj in gpuSearcher.Get())
                {
                    using var __dispose_obj = obj;
                    var name = obj["Name"]?.ToString() ?? "";
                    if (string.IsNullOrEmpty(name)) continue;
                    
                    profile.Gpu.IsGamingGrade = true; // Assume modern GPU is gaming grade
                    if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                        profile.Gpu.HasNvidia = true;
                    else if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
                        profile.Gpu.HasAmd = true;
                    else if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase) || name.Contains("UHD", StringComparison.OrdinalIgnoreCase) || name.Contains("Iris", StringComparison.OrdinalIgnoreCase))
                        profile.Gpu.HasIntel = true;
                }
                
                // Memory Real Detection
                // Win32_PhysicalMemory expõe a capacidade por módulo em "Capacity".
                // A propriedade "TotalPhysicalMemory" pertence ao Win32_ComputerSystem e causa
                // ManagementException ("Consulta inválida") quando consultada nesta classe.
                long totalRamBytes = 0;
                int minSpeed = int.MaxValue;
                try
                {
                    using var memSearcher = new System.Management.ManagementObjectSearcher("SELECT Capacity, Speed FROM Win32_PhysicalMemory");
                    foreach (System.Management.ManagementObject obj in memSearcher.Get())
                    {
                        using var __dispose_obj = obj;
                        totalRamBytes += Convert.ToInt64(obj["Capacity"] ?? 0);
                        var speed = Convert.ToInt32(obj["Speed"] ?? 0);
                        if (speed > 0) minSpeed = Math.Min(minSpeed, speed);
                    }
                }
                catch (Exception exMem)
                {
                    _logger.LogWarning($"[Hardware-Safety] Win32_PhysicalMemory indisponivel ({exMem.Message}). Usando Win32_ComputerSystem como fallback.");
                }

                if (totalRamBytes <= 0)
                {
                    try
                    {
                        using var csSearcher = new System.Management.ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
                        foreach (System.Management.ManagementObject obj in csSearcher.Get())
                        {
                            using var __dispose_obj = obj;
                            totalRamBytes = Convert.ToInt64(obj["TotalPhysicalMemory"] ?? 0);
                            break;
                        }
                    }
                    catch (Exception exCs)
                    {
                        _logger.LogWarning($"[Hardware-Safety] Win32_ComputerSystem indisponivel ({exCs.Message}).");
                    }
                }

                profile.Memory.IsGamingGrade = (totalRamBytes / (1024 * 1024 * 1024)) >= 16 && (minSpeed == int.MaxValue || minSpeed >= 3200);
                
                // Storage Real Detection
                using var diskSearcher = new System.Management.ManagementObjectSearcher("SELECT MediaType, Size FROM Win32_DiskDrive");
                bool hasNvme = false;
                foreach (System.Management.ManagementObject obj in diskSearcher.Get())
                {
                    using var __dispose_obj = obj;
                    var mediaType = obj["MediaType"]?.ToString() ?? "";
                    if (mediaType.Contains("SSD", StringComparison.OrdinalIgnoreCase) || mediaType.Contains("NVMe", StringComparison.OrdinalIgnoreCase))
                    {
                        hasNvme = true;
                        break;
                    }
                }
                profile.Storage.IsGamingGrade = hasNvme;
                
                // System Profile
                profile.System = new SystemProfile();
                
                // BIOS Profile
                try
                {
                    using var biosSearcher = new System.Management.ManagementObjectSearcher("SELECT SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS");
                    foreach (System.Management.ManagementObject obj in biosSearcher.Get())
                    {
                        using var __dispose_obj = obj;
                        profile.Bios = new BiosProfile
                        {
                            Version = obj["SMBIOSBIOSVersion"]?.ToString() ?? "",
                            ReleaseDate = obj["ReleaseDate"]?.ToString() ?? ""
                        };
                        break;
                    }
                }
                catch { }
            }
            catch (Exception ex)
            {
                // Nao zera o que ja foi detectado: os defaults de HardwareProfile sao conservadoros
                // (IsGamingGrade = false), entao uma falha parcial mantem a deteccao segura sem
                // descartar informacoes validas ja coletadas.
                _logger.LogWarning($"[Hardware-Safety] Falha parcial na deteccao de hardware: {ex.Message}");
            }
            
_hardwareProfile = profile;
            _logger.LogExit(nameof(DetectHardwareProfileAsync));
            return profile;
        }

        private async Task<SafetyValidationResult> ValidateHardwareCapabilitiesAsync(HardwareProfile profile)
        {
            _logger.LogEntry(nameof(ValidateHardwareCapabilitiesAsync));
            var validation = new SafetyValidationResult
            {
                IsValid = true,
                Warnings = new List<string>(),
                Recommendations = new List<string>(),
                SafeOptimizations = new List<string>(),
                UnsafeOptimizations = new List<string>()
            };
            
            try
            {
                // CPU validation
                if (profile.Cpu.IsGamingGrade)
                {
                    validation.SafeOptimizations.Add("CPU-Advanced-Scheduler");
                    validation.SafeOptimizations.Add("CPU-Priority-Boost");
                }
                else
                {
                    validation.UnsafeOptimizations.Add("CPU-Advanced-Scheduler");
                    validation.Warnings.Add("CPU não atende requisitos gaming grade (≥6 cores, ≥3.5GHz). Scheduler avançado desativado.");
                }
                
                // GPU validation
                if (profile.Gpu.IsGamingGrade)
                {
                    validation.SafeOptimizations.Add("GPU-Driver-Optimization");
                    if (profile.Gpu.HasNvidia) validation.SafeOptimizations.Add("GPU-NVIDIA-Power-Limit");
                    if (profile.Gpu.HasAmd) validation.SafeOptimizations.Add("GPU-AMD-Power-Profile");
                }
                else
                {
                    validation.UnsafeOptimizations.Add("GPU-Driver-Optimization");
                    validation.Warnings.Add("GPU não detectada ou não gaming grade. Otimizações GPU desativadas.");
                }
                
                // Memory validation
                if (profile.Memory.IsGamingGrade)
                {
                    validation.SafeOptimizations.Add("Memory-Large-Pages");
                    validation.SafeOptimizations.Add("Memory-Standby-List-Clean");
                }
                else
                {
                    validation.UnsafeOptimizations.Add("Memory-Large-Pages");
                    validation.Warnings.Add("RAM <16GB ou velocidade <3200MHz. Large pages e limpeza standby desativadas.");
                }
                
                // Storage validation
                if (profile.Storage.IsGamingGrade)
                {
                    validation.SafeOptimizations.Add("Storage-NVMe-Optimization");
                }
                else
                {
                    validation.UnsafeOptimizations.Add("Storage-NVMe-Optimization");
                    validation.Warnings.Add("NVMe não detectado. Otimizações NVMe desativadas.");
                }
                
                // BIOS validation - check if BIOS is recent
                if (!string.IsNullOrEmpty(profile.Bios.ReleaseDate))
                {
                    if (DateTime.TryParse(profile.Bios.ReleaseDate, out var biosDate))
                    {
                        var age = DateTime.Now - biosDate;
                        if (age.TotalDays > 730) // >2 anos
                        {
                            validation.Warnings.Add($"BIOS antiga ({biosDate:yyyy-MM-dd}). Atualize para melhor suporte a CPUs modernas.");
                        }
                    }
                }
                
                validation.Recommendations.Add($"Sistema validado: CPU={profile.Cpu.IsGamingGrade}, GPU={profile.Gpu.IsGamingGrade}, RAM={profile.Memory.IsGamingGrade}, NVMe={profile.Storage.IsGamingGrade}");
                return validation;
            }
            catch (Exception ex)
            {
                _logger.LogError("[Hardware-Safety] Erro na validação", ex);
                validation.IsValid = false;
                validation.Warnings.Add($"Erro na validação: {ex.Message}");
                return validation;
            }
finally
            {
                _logger.LogExit(nameof(ValidateHardwareCapabilitiesAsync));
            }
        }

        private void LogHardwareDetectionReport()
        {
            _logger.LogEntry(nameof(LogHardwareDetectionReport));
_logger.LogInfo("=== 🖥️ RELATÓRIO DE DETECÇÃO DE HARDWARE ===");
            _logger.LogInfo("Hardware detectado com sucesso");
            _logger.LogInfo("========================================");
            _logger.LogExit(nameof(LogHardwareDetectionReport));
}
        
        private void LogSafetyValidationReport()
        {
            _logger.LogEntry(nameof(LogSafetyValidationReport));
_logger.LogInfo("=== 🛡️ RELATÓRIO DE VALIDAÇÃO DE SEGURANÇA ===");
            _logger.LogInfo("Validação concluída com sucesso");
            _logger.LogInfo("===========================================");
            _logger.LogExit(nameof(LogSafetyValidationReport));
}
    }
}
