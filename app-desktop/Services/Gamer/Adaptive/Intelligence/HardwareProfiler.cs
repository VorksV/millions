using System;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.Gamer.Adaptive.Models;
using AdaptiveStorageType = VoltrisOptimizer.Services.Gamer.Adaptive.Models.StorageType;

namespace VoltrisOptimizer.Services.Gamer.Adaptive.Intelligence
{
    /// <summary>
    /// Profileador de hardware - detecta e classifica componentes do sistema
    /// </summary>
    public class HardwareProfiler
    {
        private readonly ILoggingService _logger;
        private static HardwareProfile? _cachedProfile;
        private static DateTime _lastProfileTime = DateTime.MinValue;
        private static readonly TimeSpan CacheExpiry = TimeSpan.FromHours(1); // Cache por 1h
        
        public HardwareProfiler(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        /// <summary>
        /// Profile hardware completo do sistema
        /// </summary>
        public async Task<HardwareProfile> ProfileHardwareAsync()
        {
            // Use cache se disponível e válido
            if (_cachedProfile != null && (DateTime.UtcNow - _lastProfileTime) < CacheExpiry)
            {
                _logger.LogDebug("[HardwareProfiler] Usando cache de hardware profile");
                return _cachedProfile;
            }
            
            _logger.LogInfo("[HardwareProfiler] Detectando hardware do sistema...");
            
            var profile = new HardwareProfile
            {
                DetectedAt = DateTime.UtcNow
            };
            
            try
            {
                // Tentar usar Central Telemetry Hub primeiro (mais rápido)
                if (await TryUseTelemetryHub(profile))
                {
                    _logger.LogInfo("[HardwareProfiler] Hardware detectado via Central Telemetry Hub");
                }
                else
                {
                    // Fallback para WMI
                    _logger.LogInfo("[HardwareProfiler] Fallback para detecção WMI direta...");
                    await ProfileUsingWmiAsync(profile);
                }
                
                // Classificar componentes
                ClassifyHardware(profile);
                
                // Cache result
                _cachedProfile = profile;
                _lastProfileTime = DateTime.UtcNow;
                
                _logger.LogSuccess($"[HardwareProfiler] Hardware profile completo | Tier: {profile.OverallTier}");
                return profile;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[HardwareProfiler] Erro ao detectar hardware: {ex.Message}", ex);
                
                // Return basic fallback profile
                return CreateFallbackProfile();
            }
        }
        
        private async Task<bool> TryUseTelemetryHub(HardwareProfile profile)
        {
            try
            {
                var hub = App.Services?.GetService(typeof(VoltrisOptimizer.Services.Telemetry.ICentralTelemetryHub)) as VoltrisOptimizer.Services.Telemetry.ICentralTelemetryHub;
                if (hub == null) return false;
                
                // CPU
                profile.Cpu.Name = hub.CpuName ?? "Unknown";
                profile.Cpu.PhysicalCores = hub.CpuCores;
                profile.Cpu.LogicalProcessors = hub.CpuLogicalProcessors;
                profile.Cpu.MaxClockMhz = hub.CpuMaxClockSpeed;
                
                // Detect CPU vendor and hybrid
                if (profile.Cpu.Name.Contains("Intel", StringComparison.OrdinalIgnoreCase))
                {
                    profile.Cpu.Vendor = "Intel";
                    profile.Cpu.IsHybrid = profile.Cpu.Name.Contains("12") || profile.Cpu.Name.Contains("13") || 
                                           profile.Cpu.Name.Contains("14") || profile.Cpu.Name.Contains("Ultra");
                }
                else if (profile.Cpu.Name.Contains("AMD", StringComparison.OrdinalIgnoreCase))
                {
                    profile.Cpu.Vendor = "AMD";
                    // Extract Ryzen generation (3000, 5000, 7000, etc.)
                    if (profile.Cpu.Name.Contains("3000")) profile.Cpu.Generation = 3000;
                    else if (profile.Cpu.Name.Contains("5000")) profile.Cpu.Generation = 5000;
                    else if (profile.Cpu.Name.Contains("7000")) profile.Cpu.Generation = 7000;
                }
                
                // GPU
                var gpuName = hub.GpuName ?? "Unknown";
                profile.Gpu.Name = gpuName;
                profile.Gpu.VideoMemoryBytes = (long)hub.GpuAdapterRam;
                
                if (gpuName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                {
                    profile.Gpu.Vendor = "NVIDIA";
                    profile.Gpu.IsDiscrete = profile.Gpu.VideoMemoryBytes > (1024 * 1024 * 1024); // >1GB
                    
                    // Extract generation (10, 20, 30, 40 series)
                    if (gpuName.Contains("GTX 10") || gpuName.Contains("GTX 16")) profile.Gpu.Generation = 10;
                    else if (gpuName.Contains("RTX 20")) profile.Gpu.Generation = 20;
                    else if (gpuName.Contains("RTX 30")) profile.Gpu.Generation = 30;
                    else if (gpuName.Contains("RTX 40")) profile.Gpu.Generation = 40;
                }
                else if (gpuName.Contains("AMD", StringComparison.OrdinalIgnoreCase) || gpuName.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
                {
                    profile.Gpu.Vendor = "AMD";
                    profile.Gpu.IsDiscrete = profile.Gpu.VideoMemoryBytes > (1024 * 1024 * 1024);
                    
                    // Extract generation (5000, 6000, 7000 series)
                    if (gpuName.Contains("RX 5")) profile.Gpu.Generation = 5000;
                    else if (gpuName.Contains("RX 6")) profile.Gpu.Generation = 6000;
                    else if (gpuName.Contains("RX 7")) profile.Gpu.Generation = 7000;
                }
                else
                {
                    profile.Gpu.Vendor = "Intel";
                    profile.Gpu.IsDiscrete = false;
                }
                
                // RAM
                profile.Ram.TotalGb = hub.TotalPhysicalMemory / (1024.0 * 1024.0 * 1024.0);
                
                // Get current RAM usage
                var availableRam = GetAvailableRamGb();
                profile.Ram.UsedGb = profile.Ram.TotalGb - availableRam;
                
                // Storage (basic detection)
                profile.Storage.Type = DetectStorageType();
                profile.Storage.GameDrive = "C:"; // Default
                
                // System
                profile.System.IsLaptop = hub.IsLaptop;
                profile.System.IsOnBattery = GetBatteryStatus();
                profile.System.WindowsVersion = Environment.OSVersion.Version.Major >= 10 ? 
                    (Environment.OSVersion.Version.Build >= 22000 ? "Windows 11" : "Windows 10") : "Windows";
                profile.System.WindowsBuild = Environment.OSVersion.Version.Build;
                
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        private async Task ProfileUsingWmiAsync(HardwareProfile profile)
        {
            await Task.Run(() =>
            {
                try
                {
                    // CPU via WMI
                    using var cpuQuery = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
                    foreach (var cpu in cpuQuery.Get())
                    {
                        profile.Cpu.Name = cpu["Name"]?.ToString() ?? "Unknown";
                        profile.Cpu.PhysicalCores = Convert.ToInt32(cpu["NumberOfCores"] ?? 0);
                        profile.Cpu.LogicalProcessors = Convert.ToInt32(cpu["NumberOfLogicalProcessors"] ?? 0);
                        profile.Cpu.MaxClockMhz = Convert.ToDouble(cpu["MaxClockSpeed"] ?? 0);
                        
                        var name = profile.Cpu.Name.ToLowerInvariant();
                        if (name.Contains("intel"))
                        {
                            profile.Cpu.Vendor = "Intel";
                            profile.Cpu.IsHybrid = name.Contains("12") || name.Contains("13") || name.Contains("14");
                        }
                        else if (name.Contains("amd"))
                        {
                            profile.Cpu.Vendor = "AMD";
                        }
                        break; // First CPU only
                    }
                    
                    // GPU via WMI
                    using var gpuQuery = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
                    foreach (var gpu in gpuQuery.Get())
                    {
                        var name = gpu["Name"]?.ToString() ?? "Unknown";
                        if (name.Contains("Microsoft", StringComparison.OrdinalIgnoreCase)) continue; // Skip software renderers
                        
                        profile.Gpu.Name = name;
                        profile.Gpu.VideoMemoryBytes = Convert.ToInt64(gpu["AdapterRAM"] ?? 0);
                        
                        if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                        {
                            profile.Gpu.Vendor = "NVIDIA";
                            profile.Gpu.IsDiscrete = true;
                        }
                        else if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
                        {
                            profile.Gpu.Vendor = "AMD";
                            profile.Gpu.IsDiscrete = profile.Gpu.VideoMemoryBytes > (512 * 1024 * 1024);
                        }
                        else
                        {
                            profile.Gpu.Vendor = "Intel";
                            profile.Gpu.IsDiscrete = false;
                        }
                        break; // First discrete GPU
                    }
                    
                    // RAM via WMI
                    using var memQuery = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem");
                    foreach (var mem in memQuery.Get())
                    {
                        var totalBytes = Convert.ToInt64(mem["TotalPhysicalMemory"] ?? 0);
                        profile.Ram.TotalGb = totalBytes / (1024.0 * 1024.0 * 1024.0);
                        break;
                    }
                    
                    // Current RAM usage
                    var availableRam = GetAvailableRamGb();
                    profile.Ram.UsedGb = profile.Ram.TotalGb - availableRam;
                    
                    // System info
                    profile.System.IsLaptop = DetectIsLaptop();
                    profile.System.IsOnBattery = GetBatteryStatus();
                    profile.System.WindowsVersion = Environment.OSVersion.Version.Major >= 10 ? 
                        (Environment.OSVersion.Version.Build >= 22000 ? "Windows 11" : "Windows 10") : "Windows";
                    profile.System.WindowsBuild = Environment.OSVersion.Version.Build;
                    
                    // Storage
                    profile.Storage.Type = DetectStorageType();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[HardwareProfiler] WMI query error: {ex.Message}");
                }
            });
        }
        
        private void ClassifyHardware(HardwareProfile profile)
        {
            // Classify CPU
            profile.Cpu.Tier = profile.Cpu.PhysicalCores switch
            {
                <= 4 => CpuTier.ENTRY,
                6 => CpuTier.MID,
                8 => CpuTier.HIGH,
                _ => CpuTier.HIGHEND
            };
            
            // Hybrid CPUs are always HIGH+
            if (profile.Cpu.IsHybrid && profile.Cpu.Tier < CpuTier.HIGH)
                profile.Cpu.Tier = CpuTier.HIGH;
            
            // Classify GPU
            var vramGb = profile.Gpu.VideoMemoryBytes / (1024.0 * 1024.0 * 1024.0);
            profile.Gpu.Tier = profile.Gpu.IsDiscrete switch
            {
                false => GpuTier.INTEGRATED,
                true when vramGb < 2 => GpuTier.ENTRY,
                true when vramGb <= 4 => GpuTier.MID,
                true when vramGb <= 8 => GpuTier.HIGH,
                true => GpuTier.HIGHEND
            };
            
            // Classify System
            profile.System.Type = (profile.System.IsLaptop, profile.System.IsOnBattery) switch
            {
                (true, true) => SystemProfileType.LAPTOP_BATTERY,
                (true, false) => SystemProfileType.LAPTOP_PLUGGED,
                (false, _) when profile.Cpu.Tier <= CpuTier.MID => SystemProfileType.DESKTOP_LOWEND,
                (false, _) when profile.Cpu.Tier == CpuTier.HIGH => SystemProfileType.DESKTOP_MIDRANGE,
                (false, _) => SystemProfileType.DESKTOP_HIGHEND
            };
            
            // Overall tier (lowest common denominator with adjustments)
            var tiers = new[] { 
                (int)profile.Cpu.Tier, 
                (int)profile.Gpu.Tier, 
                profile.Ram.TotalGb < 8 ? 0 : profile.Ram.TotalGb < 16 ? 1 : profile.Ram.TotalGb < 32 ? 2 : 3
            };
            
            var avgTier = (int)Math.Round(tiers.Average());
            profile.OverallTier = (HardwareTier)Math.Clamp(avgTier, 0, 3);
        }
        
        #region Helper Methods
        
        private double GetAvailableRamGb()
        {
            try
            {
                var pc = new PerformanceCounter("Memory", "Available Bytes");
                var availableBytes = pc.NextValue();
                return availableBytes / (1024.0 * 1024.0 * 1024.0);
            }
            catch
            {
                return 4.0; // Fallback
            }
        }
        
        private bool DetectIsLaptop()
        {
            try
            {
                using var query = new ManagementObjectSearcher("SELECT * FROM Win32_SystemEnclosure");
                foreach (var enclosure in query.Get())
                {
                    var chassisTypes = enclosure["ChassisTypes"] as ushort[];
                    if (chassisTypes != null)
                    {
                        // Chassis types: 8=Portable, 9=Laptop, 10=Notebook, 14=Sub Notebook
                        foreach (var type in chassisTypes)
                        {
                            if (type == 8 || type == 9 || type == 10 || type == 14)
                                return true;
                        }
                    }
                }
                return false;
            }
            catch
            {
                return false; // Default to desktop
            }
        }
        
        private bool GetBatteryStatus()
        {
            try
            {
                using var query = new ManagementObjectSearcher("SELECT * FROM Win32_Battery");
                return query.Get().Count > 0; // Has battery = might be on battery
            }
            catch
            {
                return false;
            }
        }
        
        private AdaptiveStorageType DetectStorageType()
        {
            try
            {
                using var query = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive WHERE Size > 0");
                foreach (var drive in query.Get())
                {
                    var model = drive["Model"]?.ToString()?.ToLowerInvariant() ?? "";
                    if (model.Contains("nvme") || model.Contains("pcie"))
                        return AdaptiveStorageType.NVMe;
                    if (model.Contains("ssd") || model.Contains("solid"))
                        return AdaptiveStorageType.SSD;
                }
                return AdaptiveStorageType.HDD; // Default assumption
            }
            catch
            {
                return AdaptiveStorageType.Unknown;
            }
        }
        
        private HardwareProfile CreateFallbackProfile()
        {
            return new HardwareProfile
            {
                Cpu = new CpuProfile { Name = "Unknown CPU", PhysicalCores = 4, LogicalProcessors = 8, Vendor = "Unknown", Tier = CpuTier.MID },
                Gpu = new GpuProfile { Name = "Unknown GPU", Vendor = "Unknown", IsDiscrete = false, Tier = GpuTier.INTEGRATED },
                Ram = new RamProfile { TotalGb = 8, UsedGb = 4 },
                Storage = new StorageProfile { Type = AdaptiveStorageType.Unknown },
                System = new SystemProfile { Type = SystemProfileType.DESKTOP_MIDRANGE, WindowsVersion = "Windows 10" },
                OverallTier = HardwareTier.MID
            };
        }
        
        #endregion
    }
}