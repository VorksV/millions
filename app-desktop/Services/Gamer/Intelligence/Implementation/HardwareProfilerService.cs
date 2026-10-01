using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.Gamer.Intelligence.Interfaces;
using VoltrisOptimizer.Services.Gamer.Intelligence.Models;
using GamerPowerProfile = VoltrisOptimizer.Services.Gamer.Intelligence.Models.PowerProfile;

namespace VoltrisOptimizer.Services.Gamer.Intelligence.Implementation
{
    public class HardwareProfilerService : IHardwareProfiler
    {
        private readonly ILoggingService _logger;
        private readonly IRegistryService _registry;
        private HardwareProfile? _currentProfile;
        private DateTime _lastAnalyzeTime = DateTime.MinValue;
        private readonly object _lock = new();

        public HardwareProfile CurrentProfile => _currentProfile ?? new HardwareProfile();
        public event EventHandler<HardwareProfile>? ProfileUpdated;

        private static readonly Dictionary<string, int> CpuBenchmarks = new()
        {
            { "ryzen 9 7950", 95 }, { "ryzen 9 5900", 85 }, { "ryzen 7 7800", 82 },
            { "ryzen 7 5800", 75 }, { "ryzen 5 7600", 70 }, { "ryzen 5 5600", 65 },
            { "i9-14900", 98 }, { "i9-13900", 95 }, { "i7-14700", 88 },
            { "i7-13700", 85 }, { "i5-14600", 78 }, { "i5-13600", 75 },
            { "i5-12400", 62 }, { "i3-12100", 48 }, { "pentium", 25 }, { "celeron", 18 }
        };

        private static readonly Dictionary<string, int> GpuBenchmarks = new()
        {
            { "4090", 100 }, { "4080", 92 }, { "4070 ti", 82 }, { "4070", 75 },
            { "4060", 58 }, { "3080", 78 }, { "3070", 68 }, { "3060", 55 },
            { "2080", 62 }, { "2070", 55 }, { "1080 ti", 55 }, { "1060", 35 },
            { "7900 xtx", 95 }, { "7800 xt", 75 }, { "6800 xt", 72 },
            { "uhd 770", 15 }, { "iris xe", 18 }, { "vega 8", 14 }
        };

        public HardwareProfilerService(ILoggingService logger, IRegistryService registry)
        {
            _logger = logger;
            _registry = registry;
            _logger.LogEntry(nameof(HardwareProfilerService));
            _logger.LogExit(nameof(HardwareProfilerService));
        }

        public async Task<HardwareProfile> AnalyzeAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogEntry(nameof(AnalyzeAsync));
            if (_currentProfile != null && (DateTime.UtcNow - _lastAnalyzeTime).TotalHours < 24)
            {
                _logger.LogExit(nameof(AnalyzeAsync), "cached");
                return _currentProfile;
            }
            {
                return _currentProfile;
            }

            return await Task.Run(() =>
            {
                var profile = new HardwareProfile
                {
                    Cpu = AnalyzeCpu(),
                    Gpu = AnalyzeGpu(),
                    Ram = AnalyzeRam(),
                    Storage = AnalyzeStorage(),
                    Power = AnalyzePower()
                };

                profile.DeviceType = DetectDeviceType(profile);
                profile.PerformanceScore = CalculateOverallScore(profile);
                profile.Classification = ClassifyHardware(profile);

                lock (_lock) 
                { 
                    _currentProfile = profile; 
                    _lastAnalyzeTime = DateTime.UtcNow;
                }
                _logger.LogInfo($"[HardwareProfiler] {profile.Classification} (Score: {profile.PerformanceScore})");
                ProfileUpdated?.Invoke(this, profile);
                _logger.LogExit(nameof(AnalyzeAsync), profile.Classification);
                return profile;
            }, cancellationToken);
        }

        private CpuProfile AnalyzeCpu()
        {
            var cpu = new CpuProfile();
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    cpu.Name = obj["Name"]?.ToString()?.Trim() ?? "";
                    cpu.Cores = Convert.ToInt32(obj["NumberOfCores"] ?? 0);
                    cpu.Threads = Convert.ToInt32(obj["NumberOfLogicalProcessors"] ?? 0);
                    cpu.BaseClock = Convert.ToDouble(obj["MaxClockSpeed"] ?? 0);
                    var name = cpu.Name.ToLowerInvariant();
                    cpu.Vendor = name.Contains("intel") ? "Intel" : name.Contains("amd") ? "AMD" : "Unknown";
                    cpu.IsHybrid = name.Contains("12th") || name.Contains("13th") || name.Contains("14th");
                    cpu.CpuScore = CalculateScore(name, CpuBenchmarks, cpu.Cores * 4);
                    break;
                }
            }
            catch (Exception ex) { _logger.LogError($"[HardwareProfiler] CPU error: {ex.Message}"); }
            return cpu;
        }

        private GpuProfile AnalyzeGpu()
        {
            var gpu = new GpuProfile();
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    var name = obj["Name"]?.ToString()?.Trim() ?? "";
                    var vram = Convert.ToInt64(obj["AdapterRAM"] ?? 0);
                    var nameLower = name.ToLowerInvariant();
                    
                    bool isIntegrated = nameLower.Contains("uhd") || nameLower.Contains("iris") || 
                                       nameLower.Contains("vega") || nameLower.Contains("radeon graphics");
                    
                    if (!isIntegrated || gpu.Name == "")
                    {
                        gpu.Name = name;
                        gpu.VramBytes = vram;
                        gpu.IsDiscrete = !isIntegrated;
                        gpu.Vendor = nameLower.Contains("nvidia") ? "NVIDIA" : 
                                    nameLower.Contains("amd") ? "AMD" : "Intel";
                        gpu.SupportsRayTracing = nameLower.Contains("rtx") || nameLower.Contains("rx 6") || nameLower.Contains("rx 7");
                        gpu.SupportsDLSS = nameLower.Contains("rtx");
                        gpu.GpuScore = CalculateScore(nameLower, GpuBenchmarks, gpu.VramMb / 100);
                    }
                    if (isIntegrated) { gpu.HasIntegrated = true; gpu.IntegratedName = name; }
                }
            }
            catch (Exception ex) { _logger.LogError($"[HardwareProfiler] GPU error: {ex.Message}"); }
            return gpu;
        }

        private int CalculateScore(string name, Dictionary<string, int> benchmarks, int fallback)
        {
            foreach (var b in benchmarks)
                if (name.Contains(b.Key)) return b.Value;
            return Math.Clamp(fallback, 10, 90);
        }

        private RamProfile AnalyzeRam()
        {
            var ram = new RamProfile();
            try
            {
                MEMORYSTATUSEX memInfo = new() { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
                if (GlobalMemoryStatusEx(ref memInfo))
                    ram.TotalGb = (int)(memInfo.ullTotalPhys / (1024 * 1024 * 1024));

                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PhysicalMemory");
                int modules = 0;
                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    modules++;
                    ram.Speed = Convert.ToInt32(obj["Speed"] ?? 0);
                    var smbios = Convert.ToInt32(obj["SMBIOSMemoryType"] ?? 0);
                    ram.Type = smbios == 34 ? "DDR5" : smbios == 26 ? "DDR4" : "DDR4";
                }
                ram.Channels = modules >= 2 ? 2 : 1;
                ram.RamScore = ram.TotalGb >= 32 ? 90 : ram.TotalGb >= 16 ? 70 : ram.TotalGb >= 8 ? 45 : 25;
            }
            catch (Exception ex) { _logger.LogError($"[HardwareProfiler] RAM error: {ex.Message}"); }
            return ram;
        }

        private StorageProfile AnalyzeStorage()
        {
            var storage = new StorageProfile();
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");
                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    var model = obj["Model"]?.ToString()?.ToLowerInvariant() ?? "";
                    if (model.Contains("nvme")) { storage.HasNvme = true; storage.SystemDriveType = "NVMe"; }
                    else if (model.Contains("ssd")) { storage.HasSsd = true; storage.SystemDriveType = "SSD"; }
                    else if (storage.SystemDriveType == "") storage.SystemDriveType = "HDD";
                }
                storage.StorageScore = storage.HasNvme ? 95 : storage.HasSsd ? 70 : 30;
            }
            catch (Exception ex) { _logger.LogError($"[HardwareProfiler] Storage error: {ex.Message}"); }
            return storage;
        }

        private GamerPowerProfile AnalyzePower()
        {
            var power = new GamerPowerProfile();
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_SystemEnclosure");
                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    var types = (ushort[]?)obj["ChassisTypes"];
                    if (types != null) power.IsLaptop = types.Any(t => t == 8 || t == 9 || t == 10 || t == 14);
                }
                
                if (GetSystemPowerStatus(out SYSTEM_POWER_STATUS status))
                {
                    // Se detectamos que é laptop ou se a bateria está presente:
                    bool hasBattery = status.BatteryFlag != 128 && status.BatteryFlag != 255;
                    power.IsLaptop = power.IsLaptop || hasBattery;
                    
                    if (power.IsLaptop)
                    {
                        power.BatteryPercent = status.BatteryLifePercent != 255 ? status.BatteryLifePercent : 100;
                        power.IsOnBattery = status.ACLineStatus == 0; // ACLineStatus == 0 significa "Offline" (na bateria)
                    }
                }
            }
            catch (Exception ex) { _logger.LogError($"[HardwareProfiler] Power error: {ex.Message}"); }
            return power;
        }

        private DeviceType DetectDeviceType(HardwareProfile p) =>
            p.Power.IsLaptop ? DeviceType.Laptop :
            p.Cpu.Cores >= 16 && p.Ram.TotalGb >= 64 ? DeviceType.Workstation : DeviceType.Desktop;

        private int CalculateOverallScore(HardwareProfile p)
        {
            double score = p.Cpu.CpuScore * 0.35 + p.Gpu.GpuScore * 0.40 + p.Ram.RamScore * 0.15 + p.Storage.StorageScore * 0.10;
            if (!p.Gpu.IsDiscrete) score *= 0.7;
            if (p.Power.IsLaptop && p.Power.IsOnBattery) score *= 0.85;
            return (int)Math.Clamp(score, 0, 100);
        }

        private HardwareClass ClassifyHardware(HardwareProfile p) => p.PerformanceScore switch
        {
            >= 90 => HardwareClass.Enthusiast,
            >= 75 => HardwareClass.Ultra,
            >= 60 => HardwareClass.High,
            >= 40 => HardwareClass.Medium,
            >= 25 => HardwareClass.Low,
            _ => HardwareClass.UltraLow
        };

        public OptimizationStrategy GetRecommendedStrategy(string? gameId = null)
        {
            _logger.LogEntry(nameof(GetRecommendedStrategy), ("gameId", gameId));
            var p = _currentProfile ?? new HardwareProfile();
            var s = new OptimizationStrategy { TargetClass = p.Classification, GameId = gameId ?? "" };
            
            (s.CpuOptimizationLevel, s.GpuOptimizationLevel, s.NetworkOptimizationLevel, s.MemoryOptimizationLevel) = p.Classification switch
            {
                HardwareClass.Enthusiast or HardwareClass.Ultra => (3, 3, 3, 2),
                HardwareClass.High => (3, 2, 3, 2),
                HardwareClass.Medium => (2, 2, 2, 2),
                _ => (1, 1, 2, 3)
            };
            
            s.EnableTimerResolution = p.Classification >= HardwareClass.Medium;
            s.EnableTdrTweaks = p.Classification >= HardwareClass.High;
            s.EnableQoS = true;
            s.EnableAdaptiveGovernor = true;
            s.EnableThermalMonitor = p.IsLaptop;
            
            _logger.LogExit(nameof(GetRecommendedStrategy), s.TargetClass);
            return s;
        }

        public bool CanApplyOptimization(string name)
        {
            _logger.LogEntry(nameof(CanApplyOptimization), ("name", name));
            var result = name.ToLowerInvariant() switch
        {
            "tdr" => (_currentProfile?.Classification ?? HardwareClass.Unknown) >= HardwareClass.High,
            "hags" => _currentProfile?.Gpu.SupportsHAGS == true,
            "dlss" => _currentProfile?.Gpu.SupportsDLSS == true,
            _ => true
        };
            _logger.LogExit(nameof(CanApplyOptimization), result);
            return result;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength; public uint dwMemoryLoad;
            public ulong ullTotalPhys; public ulong ullAvailPhys;
            public ulong ullTotalPageFile; public ulong ullAvailPageFile;
            public ulong ullTotalVirtual; public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public uint BatteryLifeTime;
            public uint BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS systemPowerStatus);
    }
}

