using System;
using System.Linq;
using System.Management;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Telemetry
{
    public class HardwareInfo
    {
        public string CpuName { get; set; } = "";
        public string GpuName { get; set; } = "";
        public double TotalRamGb { get; set; }
        public string RamType { get; set; } = "";
        public string DiskModel { get; set; } = "";
        public string DiskType { get; set; } = "";
        public string OsVersion { get; set; } = "";
        public string Architecture { get; set; } = "";
    }

    public static class HardwareHelper
    {
        private static ILoggingService? _logger => App.LoggingService;

        public static async Task<HardwareInfo> GetCurrentSessionHardwareAsync()
        {
            var info = new HardwareInfo();
            _logger?.LogInfo("[HARDWARE] Coletando informações de hardware...");

            info.CpuName = QueryCpuName();
            info.GpuName = QueryGpuName();
            info.TotalRamGb = QueryTotalRamGb();
            info.RamType = QueryRamType();
            (info.DiskModel, info.DiskType) = QueryDiskInfo();
            (info.OsVersion, info.Architecture) = GetOsInfo();

            _logger?.LogInfo($"[HARDWARE] CPU: {info.CpuName}");
            _logger?.LogInfo($"[HARDWARE] GPU: {info.GpuName}");
            _logger?.LogInfo($"[HARDWARE] RAM: {info.TotalRamGb:F1} GB ({info.RamType})");
            _logger?.LogInfo($"[HARDWARE] Disk: {info.DiskModel} ({info.DiskType})");
            _logger?.LogInfo($"[HARDWARE] OS: {info.OsVersion} ({info.Architecture})");

            return info;
        }

        private static string QueryCpuName()
        {
            // 1. Tentar ler do Registro (muito mais rápido, evita WMI COM calls e freezes)
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                if (key != null)
                {
                    var name = key.GetValue("ProcessorNameString")?.ToString();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        name = System.Text.RegularExpressions.Regex.Replace(name, @"\s+", " ").Trim();
                        _logger?.LogInfo($"[HARDWARE] CPU detectada via Registro: {name}");
                        return name;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[HARDWARE] Falha ao ler CPU do Registro: {ex.Message}");
            }

            // Fallback secundário para WMI
            try
            {
                var obj = VoltrisOptimizer.Utils.WmiHelper.QueryFirstSafe("SELECT Name FROM Win32_Processor");
                if (obj != null)
                {
                    var name = obj["Name"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        name = System.Text.RegularExpressions.Regex.Replace(name, @"\s+", " ").Trim();
                        _logger?.LogInfo($"[HARDWARE] CPU detectada via WmiHelper: {name}");
                        return name;
                    }
                }
                _logger?.LogWarning("[HARDWARE] Nenhuma CPU encontrada via WmiHelper");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[HARDWARE] Falha ao consultar CPU via WMI: {ex.Message}");
            }
            return "Desconhecido";
        }

        private static string QueryGpuName()
        {
            // 1. Tentar ler do Registro (muito mais rápido, evita WMI COM/STA freezes)
            try
            {
                using var classKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
                if (classKey != null)
                {
                    string? bestGpu = null;
                    foreach (var subKeyName in classKey.GetSubKeyNames())
                    {
                        if (subKeyName.Length == 4 && int.TryParse(subKeyName, out _))
                        {
                            using var subKey = classKey.OpenSubKey(subKeyName);
                            if (subKey != null)
                            {
                                var driverDesc = subKey.GetValue("DriverDesc")?.ToString();
                                if (!string.IsNullOrWhiteSpace(driverDesc))
                                {
                                    if (driverDesc.Contains("Basic Display", StringComparison.OrdinalIgnoreCase) ||
                                        driverDesc.Contains("Render-Only", StringComparison.OrdinalIgnoreCase))
                                    {
                                        bestGpu ??= driverDesc;
                                    }
                                    else
                                    {
                                        // Achou uma GPU dedicada (AMD, NVIDIA, Intel Graphics) Preferencial
                                        _logger?.LogInfo($"[HARDWARE] GPU dedicada detectada via Registro ({subKeyName}): {driverDesc}");
                                        return driverDesc;
                                    }
                                }
                            }
                        }
                    }
                    if (bestGpu != null)
                    {
                        _logger?.LogInfo($"[HARDWARE] GPU básica detectada via Registro: {bestGpu}");
                        return bestGpu;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[HARDWARE] Falha ao ler GPU do Registro: {ex.Message}");
            }

            // Fallback secundário para WMI
            try
            {
                var obj = VoltrisOptimizer.Utils.WmiHelper.QueryFirstSafe("SELECT Name FROM Win32_VideoController");
                if (obj != null)
                {
                    var name = obj["Name"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        _logger?.LogInfo($"[HARDWARE] GPU detectada via WmiHelper: {name}");
                        return name;
                    }
                }
                _logger?.LogWarning("[HARDWARE] Nenhuma GPU encontrada via WmiHelper");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[HARDWARE] Falha ao consultar GPU via WMI: {ex.Message}");
            }
            return "Desconhecido";
        }

        private static double QueryTotalRamGb()
        {
            // 1. Tentar obter RAM total via API nativa (muito mais rápido, evita WMI)
            try
            {
                if (NativeMemoryHelper.GetTotalPhysicalMemoryMb(out var totalMb) && totalMb > 0)
                {
                    var gb = totalMb / 1024.0;
                    _logger?.LogInfo($"[HARDWARE] RAM detectada via API nativa: {gb:F1} GB");
                    return Math.Round(gb, 1);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[HARDWARE] Falha ao ler RAM via API nativa: {ex.Message}");
            }

            // Fallback secundário para WMI
            try
            {
                var obj = VoltrisOptimizer.Utils.WmiHelper.QueryFirstSafe("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
                if (obj != null)
                {
                    var val = obj["TotalPhysicalMemory"];
                    if (val != null && val != DBNull.Value)
                    {
                        var bytes = Convert.ToInt64(val);
                        if (bytes > 0)
                        {
                            var gb = bytes / (1024.0 * 1024 * 1024);
                            _logger?.LogInfo($"[HARDWARE] RAM detectada via WMI: {gb:F1} GB ({bytes} bytes)");
                            return Math.Round(gb, 1);
                        }
                    }
                }
                _logger?.LogWarning("[HARDWARE] TotalPhysicalMemory retornou null ou inválido");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[HARDWARE] Falha ao consultar RAM via WMI: {ex.Message}");
            }

            return 0.0;
        }

        private static string QueryRamType()
        {
            try
            {
                var obj = VoltrisOptimizer.Utils.WmiHelper.QueryFirstSafe("SELECT SMBIOSMemoryType FROM Win32_PhysicalMemory");
                if (obj != null)
                {
                    var val = obj["SMBIOSMemoryType"];
                    if (val != null && val != DBNull.Value)
                    {
                        int typeCode = Convert.ToInt32(val);
                        var type = typeCode switch
                        {
                            34 => "DDR5",
                            26 => "DDR4",
                            24 => "DDR3",
                            20 => "DDR2",
                            _ => "DDR4"
                        };
                        _logger?.LogInfo($"[HARDWARE] RAM type: {type} (code={typeCode})");
                        return type;
                    }
                }
                _logger?.LogWarning("[HARDWARE] SMBIOSMemoryType não encontrado");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[HARDWARE] Falha ao consultar tipo de RAM: {ex.Message}");
            }
            return "DDR4";
        }

        private static (string model, string type) QueryDiskInfo()
        {
            try
            {
                var obj = VoltrisOptimizer.Utils.WmiHelper.QueryFirstSafe("SELECT Model, MediaType FROM Win32_DiskDrive WHERE Index=0");
                if (obj != null)
                {
                    var model = obj["Model"]?.ToString() ?? "Desconhecido";
                    var mediaType = obj["MediaType"]?.ToString() ?? "";

                    string diskType;
                    if (model.Contains("NVMe", StringComparison.OrdinalIgnoreCase))
                        diskType = "NVMe";
                    else if (mediaType.Contains("Solid State", StringComparison.OrdinalIgnoreCase)
                             || model.Contains("SSD", StringComparison.OrdinalIgnoreCase))
                        diskType = "SSD";
                    else
                        diskType = "HDD";

                    _logger?.LogInfo($"[HARDWARE] Disco: {model} ({diskType})");
                    return (model, diskType);
                }
                _logger?.LogWarning("[HARDWARE] Nenhum disco encontrado via WmiHelper");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[HARDWARE] Falha ao consultar disco: {ex.Message}");
            }
            return ("Desconhecido", "HDD");
        }

        private static (string osVersion, string architecture) GetOsInfo()
        {
            try
            {
                var arch = Environment.Is64BitOperatingSystem ? "x64" : "x86";
                var os = Environment.OSVersion;

                // Detectar versão amigável do Windows
                string friendlyName;
                try
                {
                    using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                    if (key != null)
                    {
                        var productName = key.GetValue("ProductName")?.ToString() ?? "Windows";
                        var displayVersion = key.GetValue("DisplayVersion")?.ToString() ?? "";
                        var currentBuild = key.GetValue("CurrentBuild")?.ToString() ?? "";

                        // Windows 11 tem build >= 22000 mas productName ainda diz "Windows 10"
                        if (productName.Contains("Windows 10") && int.TryParse(currentBuild, out var build) && build >= 22000)
                            productName = productName.Replace("Windows 10", "Windows 11");

                        friendlyName = !string.IsNullOrEmpty(displayVersion)
                            ? $"{productName} {displayVersion}"
                            : productName;
                    }
                    else
                    {
                        friendlyName = os.Version.Major >= 10 ? "Windows 10/11" : "Windows";
                    }
                }
                catch
                {
                    friendlyName = os.VersionString;
                }

                _logger?.LogInfo($"[HARDWARE] SO detectado: {friendlyName} ({arch})");
                return (friendlyName, arch);
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[HARDWARE] Falha ao detectar SO: {ex.Message}");
                return ("Windows", "x64");
            }
        }
    }

    internal static class NativeMemoryHelper
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
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

        public static bool GetTotalPhysicalMemoryMb(out ulong totalMb)
        {
            var status = new MEMORYSTATUSEX();
            status.dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (GlobalMemoryStatusEx(ref status))
            {
                totalMb = status.ullTotalPhys / (1024 * 1024);
                return true;
            }
            totalMb = 0;
            return false;
        }
    }
}
