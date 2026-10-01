using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Core.SystemIntelligenceProfiler;

namespace VoltrisOptimizer.Services.SystemIntelligenceProfiler
{
    /// <summary>
    /// SystemIntelligenceProfiler v2 - Simplificado e Integrado
    /// Foco em performance real e decisões inteligentes sem duplicação
    /// </summary>
    public class SystemIntelligenceProfilerService
    {
        private readonly ILoggingService _logger;
        private readonly IServiceProvider _serviceProvider;
        private HardwareProfile? _cachedProfile;
        private DateTime _lastProfileUpdate = DateTime.MinValue;
        private readonly TimeSpan _profileCacheDuration = TimeSpan.FromMinutes(10);

        public SystemIntelligenceProfilerService(ILoggingService logger, IServiceProvider serviceProvider)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        /// <summary>
        /// Garante que o perfil foi carregado (cache inteligente)
        /// </summary>
        public async Task EnsureProfileLoadedAsync()
        {
            if (_cachedProfile == null || DateTime.UtcNow - _lastProfileUpdate > _profileCacheDuration)
            {
                await GenerateHardwareProfileAsync();
            }
        }

        /// <summary>
        /// Gera perfil de hardware com cache e otimizações
        /// </summary>
        public async Task<HardwareProfile> GenerateHardwareProfileAsync()
        {
            // Verificar cache primeiro
            if (_cachedProfile != null && DateTime.UtcNow - _lastProfileUpdate < _profileCacheDuration)
            {
                _logger.LogInfo("[Profiler] Usando perfil em cache");
                return _cachedProfile;
            }

            var stopwatch = Stopwatch.StartNew();
            _logger.LogInfo("[Profiler] Gerando perfil de hardware...");

            try
            {
                var profile = new HardwareProfile();

                // Coleta paralela de informações críticas
                var tasks = new[]
                {
                    Task.Run(() => GetCPUInfo(profile)),
                    Task.Run(() => GetMemoryInfo(profile)),
                    Task.Run(() => GetStorageInfo(profile)),
                    Task.Run(() => GetGPUInfo(profile)),
                    Task.Run(() => GetSystemInfo(profile))
                };

                await Task.WhenAll(tasks);

                // Classifica o final
                ClassifyHardware(profile);

                _cachedProfile = profile;
                _lastProfileUpdate = DateTime.UtcNow;

                stopwatch.Stop();

                _logger.LogSuccess($"[Profiler] Perfil gerado em {stopwatch.ElapsedMilliseconds} ms | Tier: {profile.Tier}");

                return profile;
            }
            catch (Exception ex)
            {
                _logger.LogError("[Profiler] Erro ao gerar perfil", ex);
                return GetFallbackProfile();
            }
        }

        /// <summary>
        /// Calcula scores de performance, estabilidade e risco
        /// </summary>
        public SystemScores CalculateSystemScores(HardwareProfile profile)
        {
            var scores = new SystemScores();

            // Performance Score (0 - 100)
            scores.PerformanceScore = CalculatePerformanceScore(profile);

            // Stability Score (0 - 100)
            scores.StabilityScore = CalculateStabilityScore(profile);

            // Risk Score (0 - 100, invertido: menor = melhor)
            scores.RiskScore = CalculateRiskScore(profile);

            // Overall Score
            scores.OverallScore = (scores.PerformanceScore + scores.StabilityScore + (100 - scores.RiskScore)) / 3;

            _logger.LogInfo($"[Profiler] Scores - Performance: {scores.PerformanceScore}, Stability: {scores.StabilityScore}, Risk: {scores.RiskScore}");

            return scores;
        }

        /// <summary>
        /// Classifica ações como SAFE, CONDITIONAL ou RISKY
        /// </summary>
        public ActionClassification ClassifyAction(string actionName, HardwareProfile profile, SystemScores scores)
        {
            var action = actionName.ToLowerInvariant();

            // Ações sempre seguras
            var safeActions = new[]
            {
                "flushdns", "cleartemp", "optimizestartup", "powerplan_high", "memory_cleanup", "registry_backup"
            };

            if (safeActions.Any(a => action.Contains(a))) return ActionClassification.Safe;

            // Ações condicionais (dependem do hardware)
            var conditionalActions = new[]
            {
                "disable_hibernate", "disable_superfetch", "trim", "defrag", "visual_optimize", "service_optimize"
            };

            if (conditionalActions.Any(a => action.Contains(a)))
            {
                if (action.Contains("disable_hibernate") && profile.TotalRAMGB >= 16)
                    return ActionClassification.Safe; // Seguro em máquinas com muita RAM

                if (action.Contains("trim") && profile.HasSSD)
                    return ActionClassification.Safe; // Seguro em SSDs

                if (action.Contains("defrag") && !profile.HasSSD)
                    return ActionClassification.Safe; // Seguro em HDDs

                return ActionClassification.Conditional;
            }

            // Ações arriscadas
            var riskyActions = new[]
            {
                "disable_spectre", "overclock", "disable_protections", "kernel_tweaks", "registry_deep"
            };

            if (riskyActions.Any(a => action.Contains(a))) return ActionClassification.Risky;

            // Padrão: condicional
            return ActionClassification.Conditional;
        }

        /// <summary>
        /// Recomendações baseadas no perfil e scores
        /// </summary>
        public List<OptimizationRecommendation> GetRecommendations(HardwareProfile profile, SystemScores scores)
        {
            var recommendations = new List<OptimizationRecommendation>();

            // Recomendações baseadas no hardware
            if (profile.TotalRAMGB < 8)
            {
                recommendations.Add(new OptimizationRecommendation
                {
                    Action = "optimize_memory",
                    Priority = "High",
                    Reason = "Sistema com pouca RAM",
                    Impact = LocalizationService.Instance.GetString("ImpactHigh")
                });
            }

            if (profile.HasSSD && !profile.TrimEnabled)
            {
                recommendations.Add(new OptimizationRecommendation
                {
                    Action = "enable_trim",
                    Priority = "High",
                    Reason = "SSD sem TRIM",
                    Impact = LocalizationService.Instance.GetString("ImpactMedium")
                });
            }

            if (profile.IsLaptop && scores.RiskScore > 60)
            {
                recommendations.Add(new OptimizationRecommendation
                {
                    Action = "conservative_mode",
                    Priority = "Medium",
                    Reason = "Notebook com configurações arriscadas",
                    Impact = LocalizationService.Instance.GetString("ImpactLow")
                });
            }

            if (profile.Tier == HardwareTier.High && scores.PerformanceScore < 70)
            {
                recommendations.Add(new OptimizationRecommendation
                {
                    Action = "extreme_optimizations",
                    Priority = "Medium",
                    Reason = "Hardware potente subutilizado",
                    Impact = LocalizationService.Instance.GetString("ImpactHigh")
                });
            }

            return recommendations.OrderByDescending(r => GetPriorityWeight(r.Priority)).ToList();
        }

        #region Hardware Detection Methods

        private void GetCPUInfo(HardwareProfile profile)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");

                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    profile.CPUName = obj["Name"]?.ToString() ?? "Unknown CPU";
                    profile.CPUCores = Convert.ToInt32(obj["NumberOfCores"]);
                    profile.LogicalProcessors = Convert.ToInt32(obj["NumberOfLogicalProcessors"]);
                    break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Profiler] Erro ao detectar CPU: {ex.Message}");
                profile.CPUCores = Environment.ProcessorCount;
                profile.LogicalProcessors = Environment.ProcessorCount;
            }
        }

        private void GetMemoryInfo(HardwareProfile profile)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Capacity FROM Win32_PhysicalMemory");
                long totalCapacity = 0;

                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    totalCapacity += Convert.ToInt64(obj["Capacity"]);
                }

                profile.TotalRAMGB = (int)(totalCapacity / 1024 / 1024 / 1024);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Profiler] Erro ao detectar RAM: {ex.Message}");

                // Fallback via GC
                var gcMemory = GC.GetTotalMemory(false);
                profile.TotalRAMGB = Math.Max(4, (int)(gcMemory / 1024 / 1024 / 1024));
            }
        }

        private void GetStorageInfo(HardwareProfile profile)
        {
            try
            {
                // Detectar SSD vs HDD
                using var searcher = new ManagementObjectSearcher("SELECT Model, MediaType FROM Win32_DiskDrive");

                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    var model = obj["Model"]?.ToString() ?? "";
                    var mediaType = obj["MediaType"]?.ToString() ?? "";

                    profile.DiskModel = model;

                    if (model.Contains("SSD") || mediaType.Contains("Solid State") || model.Contains("NVMe"))
                    {
                        profile.HasSSD = true;
                        profile.HasNVMe = model.Contains("NVMe");
                    }

                    break; // Apenas o primeiro disco (sistema)
                }

                // Verificar TRIM
                try
                {
                    var process = Process.Start(new ProcessStartInfo
                    {
                        FileName = "fsutil",
                        Arguments = "behavior query DisableDeleteNotify",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    });

                    if (process != null)
                    {
                        var output = process.StandardOutput.ReadToEnd();
                        process.WaitForExit();
                        profile.TrimEnabled = output.Contains("DisableDeleteNotify = 0");
                    }
                }
                catch
                {
                    profile.TrimEnabled = false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Profiler] Erro ao detectar armazenamento: {ex.Message}");
            }
        }

        private void GetGPUInfo(HardwareProfile profile)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController");

                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    var name = obj["Name"]?.ToString() ?? "";
                    var ram = obj["AdapterRAM"] as long?;

                    if (!string.IsNullOrEmpty(name) && !name.Contains("Microsoft Basic Display Adapter"))
                    {
                        profile.GPUName = name;
                        profile.HasDedicatedGPU = true;
                        profile.GPURAMMB = ram.HasValue ? (int)(ram.Value / 1024 / 1024) : 0;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Profiler] Erro ao detectar GPU: {ex.Message}");
            }
        }
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public uint BatteryLifeTime;
            public uint BatteryFullLifeTime;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS systemPowerStatus);

        private void GetSystemInfo(HardwareProfile profile)
        {
            try
            {
                // Detectar se laptop
                using var searcher = new ManagementObjectSearcher("SELECT ChassisTypes FROM Win32_SystemEnclosure");

                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    var chassisTypes = obj["ChassisTypes"] as ushort[];

                    if (chassisTypes != null && chassisTypes.Length > 0)
                    {
                        // 8 = Portable, 9 = Laptop, 10 = Notebook, 14 = Sub-Notebook
                        profile.IsLaptop = chassisTypes[0] is 8 or 9 or 10 or 14;
                        break;
                    }
                }

                // Detectar bateria (confirma se laptop)
                try
                {
                    if (GetSystemPowerStatus(out SYSTEM_POWER_STATUS status))
                    {
                        // 128 indica "Sem bateria do sistema" (No system battery)
                        // Qualquer outro valor (exceto 255 se desconhecido) indica presença de bateria, comum em laptops
                        profile.IsLaptop |= status.BatteryFlag != 128 && status.BatteryFlag != 255;
                    }
                }
                catch
                {
                    // Ignore
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Profiler] Erro ao detectar sistema: {ex.Message}");
            }
        }

        #endregion

        #region Classification Methods

        private void ClassifyHardware(HardwareProfile profile)
        {
            // Classifica em tiers
            if (profile.TotalRAMGB >= 32 && profile.CPUCores >= 8 && profile.HasDedicatedGPU && profile.HasNVMe)
            {
                profile.Tier = HardwareTier.Ultra;
            }
            else if (profile.TotalRAMGB >= 16 && profile.CPUCores >= 6 && profile.HasDedicatedGPU && profile.HasSSD)
            {
                profile.Tier = HardwareTier.High;
            }
            else if (profile.TotalRAMGB >= 8 && profile.CPUCores >= 4)
            {
                profile.Tier = HardwareTier.Mid;
            }
            else
            {
                profile.Tier = HardwareTier.Low;
            }

            // Classifica o de uso
            profile.IsGamingPC = profile.HasDedicatedGPU && profile.TotalRAMGB >= 16 && profile.CPUCores >= 6;
            profile.IsWorkstation = profile.TotalRAMGB >= 32 && profile.CPUCores >= 8 && profile.HasDedicatedGPU;
        }

        private int CalculatePerformanceScore(HardwareProfile profile)
        {
            var score = 0;

            // CPU (40%)
            var cpuScore = Math.Min(40, profile.CPUCores * 5);
            score += cpuScore;

            // RAM (30%)
            var ramScore = Math.Min(30, profile.TotalRAMGB * 2);
            score += ramScore;

            // GPU (20%)
            if (profile.HasDedicatedGPU) score += 20;
            else if (profile.GPURAMMB > 0) score += 10;

            // Storage (10%)
            if (profile.HasNVMe) score += 10;
            else if (profile.HasSSD) score += 7;

            return Math.Min(100, score);
        }

        private int CalculateStabilityScore(HardwareProfile profile)
        {
            var score = 100;

            // Penalidades por configurações arriscadas
            if (profile.IsLaptop) score -= 10; // Laptops menos estáveis para overclock
            if (profile.TotalRAMGB < 8) score -= 15; // Pouca RAM pode causar instabilidade
            if (profile.CPUCores < 4) score -= 10; // CPUs fracos

            return Math.Max(0, score);
        }

        private int CalculateRiskScore(HardwareProfile profile)
        {
            var score = 0;

            // Fatores de risco
            if (profile.IsLaptop) score += 20; // Overclock em laptop arriscado
            if (profile.TotalRAMGB < 8) score += 15; // Otimizações agressivas podem crashar
            if (profile.Tier == HardwareTier.Ultra) score += 25; // Hardware caro = maior risco
            if (profile.IsGamingPC) score += 10; // Jogos exigem estabilidade

            return Math.Min(100, score);
        }

        private int GetPriorityWeight(string priority)
        {
            return priority.ToLowerInvariant() switch
            {
                "high" => 3,
                "medium" => 2,
                "low" => 1,
                _ => 0
            };
        }

        private HardwareProfile GetFallbackProfile()
        {
            return new HardwareProfile
            {
                CPUName = "Unknown CPU",
                CPUCores = Environment.ProcessorCount,
                LogicalProcessors = Environment.ProcessorCount,
                TotalRAMGB = 8,
                HasSSD = true,
                HasNVMe = false,
                HasDedicatedGPU = false,
                GPUName = "Integrated GPU",
                IsLaptop = false,
                Tier = HardwareTier.Mid,
                TrimEnabled = false
            };
        }

        #endregion
    }

    #region DTOs

    public class HardwareProfile
    {
        public string CPUName { get; set; } = string.Empty;
        public int CPUCores { get; set; }
        public int LogicalProcessors { get; set; }
        public int TotalRAMGB { get; set; }
        public string DiskModel { get; set; } = string.Empty;
        public bool HasSSD { get; set; }
        public bool HasNVMe { get; set; }
        public bool TrimEnabled { get; set; }
        public string GPUName { get; set; } = string.Empty;
        public bool HasDedicatedGPU { get; set; }
        public int GPURAMMB { get; set; }
        public bool IsLaptop { get; set; }
        public HardwareTier Tier { get; set; }
        public bool IsHighEnd => Tier >= HardwareTier.High;
        public bool IsGamingPC { get; set; }
        public bool IsWorkstation { get; set; }
    }

    public class SystemScores
    {
        public int PerformanceScore { get; set; }
        public int StabilityScore { get; set; }
        public int RiskScore { get; set; }
        public int OverallScore { get; set; }
    }

    public class OptimizationRecommendation
    {
        public string Action { get; set; } = string.Empty;
        public string Priority { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string Impact { get; set; } = string.Empty;
    }

    public enum HardwareTier
    {
        Low,
        Mid,
        High,
        Ultra
    }

    public enum ActionClassification
    {
        Safe,
        Conditional,
        Risky
    }

    #endregion
} 
