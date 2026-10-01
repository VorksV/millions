using System;
using System.Management;
using System.Runtime.InteropServices;
using System.Linq;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Optimization.Engines
{
    /// <summary>
    /// DSL 5.0 - Hardware Detection Engine
    /// Identificação automática de hardware para adaptação de algoritmos.
    /// </summary>
    public class HardwareDetectionEngine : IDisposable
    {
        private readonly ILoggingService _logger;

        public void Dispose() { }

        public string CpuVendor { get; private set; } = "Unknown";
        public int CpuCores { get; private set; }
        public ulong TotalRamBytes { get; private set; }
        public bool IsSsd { get; private set; } = true;
        public bool IsLaptop { get; private set; } = false;

        public HardwareDetectionEngine(ILoggingService logger)
        {
            _logger = logger;
            // OTIMIZAÇÃO CRÍTICA: Não coletar info no construtor pois WMI bloqueia a thread (incluindo UI thread se resolvido via DI lock)
            // A coleta será disparada via InitializeAsync() ou sob demanda em background.
        }

        public async Task InitializeAsync()
        {
            await Task.Run(() => CollectInfo());
        }

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

        private bool IsLaptopViaPowerStatus()
        {
            try
            {
                if (GetSystemPowerStatus(out SYSTEM_POWER_STATUS status))
                {
                    // BatteryFlag: 128 = No battery, 255 = Unknown status
                    return status.BatteryFlag != 128 && status.BatteryFlag != 255;
                }
            }
            catch { }
            return false;
        }

        private void CollectInfo()
        {
            try
            {
                // CPU
                CpuCores = Environment.ProcessorCount;
                
                string vendor = "";
                try
                {
                    vendor = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "VendorIdentifier", "")?.ToString()?.Trim() ?? "";
                }
                catch { }
                
                if (string.IsNullOrEmpty(vendor))
                {
                    if (System.Runtime.Intrinsics.X86.X86Base.IsSupported)
                    {
                        var result = System.Runtime.Intrinsics.X86.X86Base.CpuId(0, 0);
                        byte[] bytes = new byte[12];
                        BitConverter.GetBytes(result.Ebx).CopyTo(bytes, 0);
                        BitConverter.GetBytes(result.Edx).CopyTo(bytes, 4);
                        BitConverter.GetBytes(result.Ecx).CopyTo(bytes, 8);
                        vendor = System.Text.Encoding.ASCII.GetString(bytes);
                    }
                }
                
                if (string.IsNullOrEmpty(vendor))
                {
                    try
                    {
                        using (var searcher = new ManagementObjectSearcher("SELECT Manufacturer FROM Win32_Processor"))
                        {
                            foreach (var obj in searcher.Get())
                            {
                using var __dispose_obj = obj;
                                vendor = obj["Manufacturer"]?.ToString() ?? "Unknown";
                                break;
                            }
                        }
                    }
                    catch { }
                }
                
                CpuVendor = !string.IsNullOrEmpty(vendor) ? vendor : "Unknown";

                // RAM
                var mem = new MEMORYSTATUSEX();
                mem.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
                GlobalMemoryStatusEx(ref mem);
                TotalRamBytes = mem.ullTotalPhys;

                // SSD Detection (Simplified)
                bool detectedSsd = false;
                try
                {
                    using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\Scsi\Scsi Port 0\Scsi Bus 0\Target Id 0\Logical Unit Id 0"))
                    {
                        string id = key?.GetValue("Identifier")?.ToString()?.ToUpper() ?? "";
                        if (id.Contains("SSD") || id.Contains("NVME") || id.Contains("PROD_SSD") || id.Contains("SATA_SSD"))
                        {
                            IsSsd = true;
                            detectedSsd = true;
                        }
                    }
                }
                catch { }

                if (!detectedSsd)
                {
                    try
                    {
                        using (var searcher = new ManagementObjectSearcher("SELECT Model FROM Win32_DiskDrive"))
                        {
                            foreach (var obj in searcher.Get())
                            {
                using var __dispose_obj = obj;
                                string model = obj["Model"]?.ToString()?.ToUpper() ?? "";
                                if (model.Contains("SSD") || model.Contains("NVME"))
                                {
                                    IsSsd = true;
                                    break;
                                }
                            }
                        }
                    }
                    catch { }
                }

                // Laptop Detection
                IsLaptop = IsLaptopViaPowerStatus();
                if (!IsLaptop)
                {
                    try
                    {
                        using (var searcher = new ManagementObjectSearcher("SELECT ChassisTypes FROM Win32_SystemEnclosure"))
                        {
                            foreach (var obj in searcher.Get())
                            {
                using var __dispose_obj = obj;
                                var types = obj["ChassisTypes"] as ushort[];
                                if (types != null && types.Any(t => t >= 8 && t <= 11))
                                {
                                    IsLaptop = true;
                                    break;
                                }
                            }
                        }
                    }
                    catch { }
                }

                _logger.LogInfo($"[DSL 5.0] Hardware: CPU {CpuVendor} ({CpuCores}), RAM: {TotalRamBytes / 1024 / 1024 / 1024}GB, SSD: {IsSsd}, Laptop: {IsLaptop}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Hardware] Erro na detecção: {ex.Message}");
            }
        }

        [DllImport("kernel32.dll")]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }
    }
}
