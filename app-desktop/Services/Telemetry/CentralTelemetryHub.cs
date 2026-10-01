using System;
using System.Management;
using VoltrisOptimizer.Core;
using VoltrisOptimizer.Services.Hardware;

namespace VoltrisOptimizer.Services.Telemetry
{
    public class CentralTelemetryHub : ICentralTelemetryHub
    {
        private readonly ILoggingService _logger;

        // --- STATIC WMI DATA (Cached via Lazy<T> for the lifetime of the application) ---

        private readonly Lazy<CpuInfo> _cpuInfo = new Lazy<CpuInfo>(() =>
        {
            var info = new CpuInfo { Name = "Unknown", Cores = 0, Threads = 0, MaxClockSpeed = 0 };
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");
                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    info.Name = obj["Name"]?.ToString() ?? "Unknown";
                    if (uint.TryParse(obj["NumberOfCores"]?.ToString(), out var cores)) info.Cores = (int)cores;
                    if (uint.TryParse(obj["NumberOfLogicalProcessors"]?.ToString(), out var threads)) info.Threads = (int)threads;
                    if (uint.TryParse(obj["MaxClockSpeed"]?.ToString(), out var clock)) info.MaxClockSpeed = clock;
                    break;
                }
            }
            catch { }
            return info;
        });

        private readonly Lazy<GpuInfo> _gpuInfo = new Lazy<GpuInfo>(() =>
        {
            var info = new GpuInfo { Name = "Unknown", AdapterRam = 0 };
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController");
                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    info.Name = obj["Name"]?.ToString() ?? "Unknown";
                    if (ulong.TryParse(obj["AdapterRAM"]?.ToString(), out var ram)) info.AdapterRam = ram;
                    break; // Pegamos apenas a principal para o hub genérico
                }
            }
            catch { }
            return info;
        });

        private readonly Lazy<RamInfo> _ramInfo = new Lazy<RamInfo>(() =>
        {
            var info = new RamInfo { Speed = 0, TotalCapacity = 0 };
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Capacity, Speed FROM Win32_PhysicalMemory");
                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    if (ulong.TryParse(obj["Capacity"]?.ToString(), out var cap)) info.TotalCapacity += cap;
                    if (info.Speed == 0 && uint.TryParse(obj["Speed"]?.ToString(), out var speed)) info.Speed = speed;
                }
            }
            catch { }

            // Fallback de capacidade total
            if (info.TotalCapacity == 0)
            {
                try
                {
                    using var searcher2 = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
                    foreach (ManagementObject obj in searcher2.Get())
                    {
                using var __dispose_obj = obj;
                        if (ulong.TryParse(obj["TotalPhysicalMemory"]?.ToString(), out var cap)) info.TotalCapacity = cap;
                        break;
                    }
                }
                catch { }
            }
            return info;
        });

        private readonly Lazy<OsInfo> _osInfo = new Lazy<OsInfo>(() =>
        {
            var info = new OsInfo { Caption = "Windows", IsLaptop = false, ChassisType = 3 }; // 3 = Desktop padrão
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Caption FROM Win32_OperatingSystem");
                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    info.Caption = obj["Caption"]?.ToString() ?? "Windows";
                    break;
                }
            }
            catch { }

            try
            {
                using var searcher2 = new ManagementObjectSearcher("SELECT ChassisTypes FROM Win32_SystemEnclosure");
                foreach (ManagementObject obj in searcher2.Get())
                {
                using var __dispose_obj = obj;
                    if (obj["ChassisTypes"] is ushort[] types && types.Length > 0)
                    {
                        info.ChassisType = types[0];
                        // Notebook, Laptop, Portable
                        if (types[0] == 9 || types[0] == 10 || types[0] == 8 || types[0] == 14) 
                            info.IsLaptop = true;
                    }
                    break;
                }
            }
            catch { }
            return info;
        });


        public CentralTelemetryHub(ILoggingService logger)
        {
            _logger = logger;
        }

        // --- ICentralTelemetryHub IMPL ---

        public string CpuName => _cpuInfo.Value.Name;
        public int CpuCores => _cpuInfo.Value.Cores;
        public int CpuLogicalProcessors => _cpuInfo.Value.Threads;
        public uint CpuMaxClockSpeed => _cpuInfo.Value.MaxClockSpeed;

        public string GpuName => _gpuInfo.Value.Name;
        public ulong GpuAdapterRam => _gpuInfo.Value.AdapterRam;

        public ulong TotalPhysicalMemory => _ramInfo.Value.TotalCapacity;
        public uint RamSpeed => _ramInfo.Value.Speed;

        public string OsCaption => _osInfo.Value.Caption;
        public int ChassisType => _osInfo.Value.ChassisType;
        public bool IsLaptop => _osInfo.Value.IsLaptop;

        // Dynamic Telemetry delegada para o SystemMetricsCache (já otimizado, sem leaks)
        public double CpuUsagePercent => SystemMetricsCache.Instance.CpuPercent;
        public double DiskUsagePercent => SystemMetricsCache.Instance.DiskUsagePercent;
        public double GpuUsagePercent => SystemMetricsCache.Instance.GpuUsagePercent;
        public ulong AvailablePhysicalMemory => (ulong)(SystemMetricsCache.Instance.AvailableRamMb * 1024 * 1024);

        public void StartDynamicTelemetry()
        {
            SystemMetricsCache.Instance.Start();
        }

        public void StopDynamicTelemetry()
        {
            SystemMetricsCache.Instance.Stop();
        }

        // --- Helper Models ---
        private class CpuInfo { public string Name { get; set; } = ""; public int Cores { get; set; } public int Threads { get; set; } public uint MaxClockSpeed { get; set; } }
        private class GpuInfo { public string Name { get; set; } = ""; public ulong AdapterRam { get; set; } }
        private class RamInfo { public uint Speed { get; set; } public ulong TotalCapacity { get; set; } }
        private class OsInfo { public string Caption { get; set; } = ""; public bool IsLaptop { get; set; } public int ChassisType { get; set; } }
    }
}
