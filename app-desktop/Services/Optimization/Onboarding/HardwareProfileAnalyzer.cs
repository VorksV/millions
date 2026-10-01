using System;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;
using OnbModel = VoltrisOptimizer.Models;

namespace VoltrisOptimizer.Services.Optimization.Onboarding
{
    public sealed class HardwareProfileAnalyzer
    {
        private readonly ILoggingService _logger;

        public HardwareProfileAnalyzer(ILoggingService logger)
        {
            _logger = logger;
        }

        public async Task<OnbModel.HardwareProfile> AnalyzeAsync()
        {
            _logger?.LogInfo("[HardwareProfile] Iniciando análise de hardware...");
            var sw = Stopwatch.StartNew();
            var profile = new OnbModel.HardwareProfile();

            try
            {
                await Task.Run(() =>
                {
                    try { DetectMachineClass(profile); } catch { }
                    try { DetectCpu(profile); } catch { }
                    try { DetectMemory(profile); } catch { }
                    try { DetectStorage(profile); } catch { }
                    try { DetectGpu(profile); } catch { }
                });

                _logger?.LogInfo($"[HardwareProfile] Análise concluída em {sw.ElapsedMilliseconds}ms:" +
                    $" {profile.MachineClass}, {profile.CpuVendor} Gen{profile.CpuGeneration}," +
                    $" {profile.CpuCores}C/{profile.CpuLogicalProcessors}T," +
                    $" {profile.TotalRamGb:F1}GB RAM, {profile.StorageType}," +
                    $" GPU={profile.GpuTier}");

                _logger?.LogDebug($"[HardwareProfile] IsLowEnd={profile.IsLowEnd}, IsHighEnd={profile.IsHighEnd}," +
                    $" MemoryTier={profile.MemoryTier}, IsEfficientCoreCpu={profile.IsEfficientCoreCpu}");

                return profile;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[HardwareProfile] Erro na análise: {ex.Message}");
                return profile;
            }
        }

        private void DetectMachineClass(OnbModel.HardwareProfile profile)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT ChassisTypes FROM Win32_SystemEnclosure");
                foreach (var obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    var types = obj["ChassisTypes"] as ushort[];
                    if (types != null && types.Length > 0)
                    {
                        profile.MachineClass = types[0] switch
                        {
                            8 or 9 or 10 or 14 => OnbModel.MachineClass.Laptop,
                            3 or 4 or 5 or 6 or 7 or 15 or 16 => OnbModel.MachineClass.Desktop,
                            17 or 23 => OnbModel.MachineClass.Workstation,
                            _ => OnbModel.MachineClass.Desktop
                        };
                        return;
                    }
                }
            }
            catch
            {
                profile.MachineClass = OnbModel.MachineClass.Desktop;
            }
        }

        private void DetectCpu(OnbModel.HardwareProfile profile)
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");
            foreach (var obj in searcher.Get())
            {
                using var __dispose_obj = obj;
                var name = obj["Name"]?.ToString() ?? "";
                profile.CpuCores = Convert.ToInt32(obj["NumberOfCores"]);
                profile.CpuLogicalProcessors = Convert.ToInt32(obj["NumberOfLogicalProcessors"]);
                profile.CpuMaxTurboMhz = Convert.ToInt32(obj["MaxClockSpeed"]);

                if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase))
                {
                    profile.CpuVendor = OnbModel.CpuVendor.Intel;
                    profile.IsEfficientCoreCpu = HasIntelEfficientCores(name);
                    profile.CpuGeneration = ExtractIntelGeneration(name);
                    profile.CpuBaseClockMhz = profile.CpuMaxTurboMhz / 2;
                }
                else if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase))
                {
                    profile.CpuVendor = OnbModel.CpuVendor.Amd;
                    profile.IsEfficientCoreCpu = false;
                    profile.CpuGeneration = ExtractAmdGeneration(name);
                    profile.CpuBaseClockMhz = profile.CpuMaxTurboMhz / 2;
                }
                break;
            }
        }

        private bool HasIntelEfficientCores(string cpuName)
        {
            return cpuName.Contains("12th") || cpuName.Contains("13th") ||
                   cpuName.Contains("14th") || cpuName.Contains("Core Ultra") ||
                   cpuName.Contains("Series 1") || cpuName.Contains("Series 2");
        }

        private int ExtractIntelGeneration(string cpuName)
        {
            if (cpuName.Contains("14th")) return 14;
            if (cpuName.Contains("13th")) return 13;
            if (cpuName.Contains("12th")) return 12;
            if (cpuName.Contains("11th")) return 11;
            if (cpuName.Contains("10th")) return 10;
            if (cpuName.Contains("9th")) return 9;
            if (cpuName.Contains("8th")) return 8;
            if (cpuName.Contains("7th")) return 7;
            if (cpuName.Contains("6th")) return 6;
            if (cpuName.Contains("Core Ultra")) return 15;
            return 0;
        }

        private int ExtractAmdGeneration(string cpuName)
        {
            if (cpuName.Contains("Ryzen 9")) return 9;
            if (cpuName.Contains("Ryzen 7")) return 7;
            if (cpuName.Contains("Ryzen 5")) return 5;
            if (cpuName.Contains("Ryzen 3")) return 3;
            return 0;
        }

        private void DetectMemory(OnbModel.HardwareProfile profile)
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Capacity FROM Win32_PhysicalMemory");
            long total = 0;
            foreach (var obj in searcher.Get())
                total += Convert.ToInt64(obj["Capacity"]);
            profile.TotalRamGb = total / (1024.0 * 1024.0 * 1024.0);
        }

        private void DetectStorage(OnbModel.HardwareProfile profile)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Model, MediaType, InterfaceType FROM Win32_DiskDrive");
                foreach (var obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    var model = obj["Model"]?.ToString() ?? "";
                    var mediaType = obj["MediaType"]?.ToString() ?? "";
                    var interfaceType = obj["InterfaceType"]?.ToString() ?? "";

                    if (model.Contains("NVMe") || interfaceType.Contains("NVMe"))
                    {
                        if ((int)profile.StorageType < 3)
                            profile.StorageType = OnbModel.StorageType.NvmeSsd;
                    }
                    else if (interfaceType.Contains("SATA") || model.Contains("SSD"))
                    {
                        if ((int)profile.StorageType < 2)
                            profile.StorageType = OnbModel.StorageType.SataSsd;
                    }
                    else if (!mediaType.Contains("SSD") && !model.Contains("SSD"))
                    {
                        if (profile.StorageType == OnbModel.StorageType.Unknown)
                            profile.StorageType = OnbModel.StorageType.Hdd;
                    }
                }

                if (profile.StorageType == OnbModel.StorageType.Unknown)
                {
                    var cDrive = new System.IO.DriveInfo("C");
                    profile.StorageType = cDrive.DriveType switch
                    {
                        System.IO.DriveType.Fixed => OnbModel.StorageType.Hdd,
                        _ => OnbModel.StorageType.Unknown
                    };
                }
            }
            catch
            {
                profile.StorageType = OnbModel.StorageType.Unknown;
            }
        }

        private void DetectGpu(OnbModel.HardwareProfile profile)
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, AdapterRAM FROM Win32_VideoController");
            int gpuCount = 0;
            long maxRam = 0;
            string? gpuName = null;

            foreach (var obj in searcher.Get())
            {
                using var __dispose_obj = obj;
                gpuCount++;
                var name = obj["Name"]?.ToString() ?? "";
                long ram = obj["AdapterRAM"] != null ? Convert.ToInt64(obj["AdapterRAM"]) : 0;

                if (ram > maxRam)
                {
                    maxRam = ram;
                    gpuName = name;
                }
            }

            if (gpuName == null) return;

            profile.IsIntegratedGpu = gpuName.Contains("Intel") ||
                                       gpuName.Contains("AMD") && gpuCount == 1 ||
                                       gpuName.Contains("Microsoft Basic");

            if (maxRam >= 8L * 1024 * 1024 * 1024)
                profile.GpuTier = OnbModel.GpuTier.Enthusiast;
            else if (maxRam >= 4L * 1024 * 1024 * 1024)
                profile.GpuTier = OnbModel.GpuTier.HighEnd;
            else if (maxRam >= 2L * 1024 * 1024 * 1024)
                profile.GpuTier = OnbModel.GpuTier.MidRange;
            else if (maxRam >= 1L * 1024 * 1024 * 1024)
                profile.GpuTier = OnbModel.GpuTier.Entry;
            else
                profile.GpuTier = OnbModel.GpuTier.Integrated;
        }
    }
}
