using VoltrisOptimizer.Utils;
using VoltrisOptimizer.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;
using System.Runtime.Versioning;

namespace VoltrisOptimizer.Services
{
    public class SystemInfoServiceImpl : ISystemInfoService
    {
        private HardwareCapabilities? _cachedCapabilities;
        private DateTime _lastCacheUpdate = DateTime.MinValue;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
        private readonly VoltrisOptimizer.Services.ILoggingService? _logger;
        private readonly VoltrisOptimizer.Services.Telemetry.ICentralTelemetryHub? _hub;

        private string? _cachedWindowsVersion;
        private string? _cachedWindowsEdition;
        private int? _cachedWindowsBuild;
        private GpuInfo[]? _cachedGpus;
        private DateTime _lastGpusCacheUpdate = DateTime.MinValue;
        private VoltrisOptimizer.Interfaces.DriveInfo[]? _cachedDrives;
        private DateTime _lastDrivesCacheUpdate = DateTime.MinValue;

        public SystemInfoServiceImpl(VoltrisOptimizer.Services.ILoggingService? logger = null, VoltrisOptimizer.Services.Telemetry.ICentralTelemetryHub? hub = null)
        {
            _logger = logger;
            _hub = hub ?? (VoltrisOptimizer.Services.Telemetry.ICentralTelemetryHub?)App.Services?.GetService(typeof(VoltrisOptimizer.Services.Telemetry.ICentralTelemetryHub));
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

        public struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public uint BatteryLifeTime;
            public uint BatteryFullLifeTime;
        }

        public async Task<CpuInfo> GetCpuInfoAsync()
        {
            if (_cachedCapabilities != null && DateTime.Now - _lastCacheUpdate < CacheDuration)
                return _cachedCapabilities.Cpu;

            return await SafeTask.Run(() =>
            {
                var cpuInfo = new CpuInfo();
                
                try
                {
                    if (_hub != null && !string.IsNullOrEmpty(_hub.CpuName))
                    {
                        cpuInfo.Name = _hub.CpuName;
                        cpuInfo.CoreCount = _hub.CpuCores;
                        cpuInfo.ThreadCount = _hub.CpuLogicalProcessors;
                        cpuInfo.MaxClockSpeedMHz = _hub.CpuMaxClockSpeed;
                        cpuInfo.CurrentClockSpeedMHz = _hub.CpuMaxClockSpeed; // Fallback
                        
                        var name = cpuInfo.Name.ToUpperInvariant();
                        cpuInfo.IsHybrid = (cpuInfo.ThreadCount > cpuInfo.CoreCount) || 
                                          (name.Contains("INTEL") && (name.Contains("12") || name.Contains("13") || name.Contains("14")));
                        
                        cpuInfo.Architecture = Environment.Is64BitOperatingSystem ? "x64" : "x86";
                        return cpuInfo;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError("Erro ao obter CPU info do hub", ex);
                }
                
                // Fallback final
                cpuInfo.Name = "Processador Desconhecido";
                cpuInfo.CoreCount = Environment.ProcessorCount;
                cpuInfo.ThreadCount = Environment.ProcessorCount;
                cpuInfo.Architecture = Environment.Is64BitOperatingSystem ? "x64" : "x86";
                
                return cpuInfo;
            });
        }

        public async Task<GpuInfo> GetGpuInfoAsync()
        {
            return await SafeTask.Run(() =>
            {
                var gpuInfo = new GpuInfo();

                try
                {
                    if (_hub != null && !string.IsNullOrEmpty(_hub.GpuName)
                        && !_hub.GpuName.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                    {
                        gpuInfo.Name = _hub.GpuName;
                        gpuInfo.VideoMemoryBytes = (long)_hub.GpuAdapterRam;
                        gpuInfo.Vendor = gpuInfo.Name.Contains("NVIDIA") ? "NVIDIA" : (gpuInfo.Name.Contains("AMD") ? "AMD" : "Intel");
                        gpuInfo.IsDiscrete = !gpuInfo.Name.Contains("Intel", StringComparison.OrdinalIgnoreCase) ||
                                            gpuInfo.VideoMemoryBytes > 512 * 1024 * 1024;

                        gpuInfo.SupportsHags = false;
                        gpuInfo.SupportsVrr = false;
                        return gpuInfo;
                    }
                }
                catch
                {
                }

                // WMI fallback direto — detecta a primeira GPU real
                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM, AdapterCompatibility, DriverVersion FROM Win32_VideoController");
                    foreach (ManagementObject obj in searcher.Get())
                    {
                using var __dispose_obj = obj;
                        var name = obj["Name"]?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(name) && !name.Contains("Basic", StringComparison.OrdinalIgnoreCase))
                        {
                            gpuInfo.Name = name;
                            gpuInfo.VideoMemoryBytes = Convert.ToInt64(obj["AdapterRAM"] ?? 0);
                            gpuInfo.Vendor = obj["AdapterCompatibility"]?.ToString() ?? "";
                            gpuInfo.DriverVersion = obj["DriverVersion"]?.ToString() ?? "";
                            gpuInfo.IsDiscrete = !name.Contains("Intel", StringComparison.OrdinalIgnoreCase) ||
                                                gpuInfo.VideoMemoryBytes > 512 * 1024 * 1024;
                            return gpuInfo;
                        }
                    }
                }
                catch
                {
                }

                // Segundo fallback: WMI multi-GPU (todas as placas)
                try
                {
                    using var searcher2 = new ManagementObjectSearcher("SELECT Name, AdapterRAM, AdapterCompatibility FROM Win32_VideoController");
                    GpuInfo? discrete = null;
                    GpuInfo? integrated = null;
                    foreach (ManagementObject obj in searcher2.Get())
                    {
                using var __dispose_obj = obj;
                        var name = obj["Name"]?.ToString() ?? "";
                        if (string.IsNullOrEmpty(name) || name.Contains("Basic", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var ram = Convert.ToInt64(obj["AdapterRAM"] ?? 0);
                        var isDiscrete = !name.Contains("Intel", StringComparison.OrdinalIgnoreCase) || ram > 512 * 1024 * 1024;
                        var g = new GpuInfo
                        {
                            Name = name,
                            Vendor = obj["AdapterCompatibility"]?.ToString() ?? "",
                            VideoMemoryBytes = ram,
                            IsDiscrete = isDiscrete
                        };

                        if (isDiscrete && discrete == null) discrete = g;
                        else if (!isDiscrete && integrated == null) integrated = g;
                    }

                    if (discrete != null && integrated != null)
                    {
                        gpuInfo.Name = $"{integrated.Name} + {discrete.Name}";
                        gpuInfo.VideoMemoryBytes = discrete.VideoMemoryBytes;
                        gpuInfo.Vendor = discrete.Vendor;
                        gpuInfo.IsDiscrete = true;
                        return gpuInfo;
                    }

                    if (discrete != null) return discrete;
                    if (integrated != null) return integrated;
                }
                catch
                {
                }

                gpuInfo.Name = "Placa Gráfica Desconhecida";
                gpuInfo.Vendor = "Desconhecido";

                return gpuInfo;
            });
        }

        public async Task<GpuInfo[]> GetAllGpusAsync()
        {
            if (_cachedGpus != null && DateTime.Now - _lastGpusCacheUpdate < CacheDuration)
                return _cachedGpus;

            return await SafeTask.Run(() =>
            {
                var gpus = new List<GpuInfo>();
                
                try
                {
                    using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController"))
                    {
                        foreach (ManagementObject obj in searcher.Get())
                        {
                using var __dispose_obj = obj;
                            var gpuInfo = new GpuInfo
                            {
                                Name = obj["Name"]?.ToString() ?? "",
                                DriverVersion = obj["DriverVersion"]?.ToString() ?? "",
                                Vendor = obj["AdapterCompatibility"]?.ToString() ?? "",
                                VideoMemoryBytes = Convert.ToInt64(obj["AdapterRAM"] ?? 0)
                            };
                            
                            gpuInfo.IsDiscrete = !gpuInfo.Name.Contains("Intel", StringComparison.OrdinalIgnoreCase) || 
                                                gpuInfo.VideoMemoryBytes > 512 * 1024 * 1024;
                            
                            gpuInfo.SupportsHags = false;
                            gpuInfo.SupportsVrr = false;
                            
                            if (!string.IsNullOrEmpty(gpuInfo.Name) && !gpuInfo.Name.Contains("Basic", StringComparison.OrdinalIgnoreCase))
                                gpus.Add(gpuInfo);
                        }
                    }
                }
                catch
                {
                    gpus.Add(new GpuInfo
                    {
                        Name = "Placa Gráfica Desconhecida",
                        Vendor = "Desconhecido"
                    });
                }
                
                _cachedGpus = gpus.ToArray();
                _lastGpusCacheUpdate = DateTime.Now;
                return _cachedGpus;
            });
        }

        public async Task<RamInfo> GetRamInfoAsync()
        {
            return await SafeTask.Run(() =>
            {
                var ramInfo = new RamInfo();
                
                try
                {
                    if (_hub != null && _hub.TotalPhysicalMemory > 0)
                    {
                        ramInfo.TotalBytes = (long)_hub.TotalPhysicalMemory;
                        ramInfo.AvailableBytes = (long)_hub.AvailablePhysicalMemory;
                        ramInfo.SpeedMHz = (int)_hub.RamSpeed;
                        ramInfo.MemoryType = "DDR4"; // Padronizado para fallback rápido
                        return ramInfo;
                    }
                }
                catch
                {
                }
                
                ramInfo.TotalBytes = 0;
                ramInfo.AvailableBytes = 0;
                
                return ramInfo;
            });
        }

        public async Task<VoltrisOptimizer.Interfaces.DriveInfo[]> GetDrivesInfoAsync()
        {
            if (_cachedDrives != null && DateTime.Now - _lastDrivesCacheUpdate < CacheDuration)
                return _cachedDrives;

            return await SafeTask.Run(() =>
            {
                var drives = new List<VoltrisOptimizer.Interfaces.DriveInfo>();
                
                try
                {
                    // Pré-carregar mapa de letra de drive → modelo do disco físico
                    var driveModelMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    try
                    {
                        using var diskSearcher = new ManagementObjectSearcher("SELECT DeviceID, Model FROM Win32_DiskDrive");
                        foreach (ManagementObject disk in diskSearcher.Get())
                        {
                using var __dispose_disk = disk;
                            string deviceId = disk["DeviceID"]?.ToString() ?? "";
                            string model = (disk["Model"]?.ToString() ?? "").Trim();

                            using var partSearcher = new ManagementObjectSearcher(
                                $"ASSOCIATORS OF {{Win32_DiskDrive.DeviceID='{deviceId}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition");
                            foreach (ManagementObject partition in partSearcher.Get())
                            {
                using var __dispose_partition = partition;
                                using var logicalSearcher = new ManagementObjectSearcher(
                                    $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partition["DeviceID"]}'}} WHERE AssocClass=Win32_LogicalDiskToPartition");
                                foreach (ManagementObject logical in logicalSearcher.Get())
                                {
                using var __dispose_logical = logical;
                                    string logicalName = logical["Name"]?.ToString()?.TrimEnd('\\', ':') ?? "";
                                    if (!string.IsNullOrEmpty(logicalName) && !string.IsNullOrEmpty(model))
                                        driveModelMap[logicalName] = model;
                                }
                            }
                        }
                    }
                    catch { }

                    foreach (var drive in System.IO.DriveInfo.GetDrives())
                    {
                        if (drive.IsReady)
                        {
                            string letterClean = drive.Name.TrimEnd('\\', ':');
                            driveModelMap.TryGetValue(letterClean, out string? model);

                            var driveInfo = new VoltrisOptimizer.Interfaces.DriveInfo
                            {
                                Letter = drive.Name,
                                Label = drive.VolumeLabel,
                                TotalBytes = drive.TotalSize,
                                FreeBytes = drive.AvailableFreeSpace,
                                FileSystem = drive.DriveFormat,
                                IsSsd = IsDriveSSD(drive.Name, model ?? ""),
                                Model = model ?? ""
                            };
                            
                            drives.Add(driveInfo);
                        }
                    }
                }
                catch
                {
                }
                
                _cachedDrives = drives.ToArray();
                _lastDrivesCacheUpdate = DateTime.Now;
                return _cachedDrives;
            });
        }

        public async Task<NetworkInfo[]> GetNetworkInfoAsync()
        {
            return await SafeTask.Run(() =>
            {
                var networks = new List<NetworkInfo>();
                
                try
                {
                    foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (adapter.OperationalStatus == OperationalStatus.Up)
                        {
                            var networkInfo = new NetworkInfo
                            {
                                Name = adapter.Name,
                                Manufacturer = adapter.Description,
                                // PhysicalAddress.ToString() devolve 12 dígitos hex
                                // SEM separadores ("0A002700000A"). O outro caminho de
                                // leitura do app (identificação de rede) produz
                                // "A2:BD:AA:A8:80:0B". Dois formatos para o mesmo dado
                                // quebram comparação e exibição. Formatamos com ":".
                                MacAddress = FormatMacAddress(adapter.GetPhysicalAddress()),
                                IsEnabled = adapter.OperationalStatus == OperationalStatus.Up,
                                SpeedBps = adapter.Speed
                            };
                            
                            networks.Add(networkInfo);
                        }
                    }
                }
                catch
                {
                }
                
                return networks.ToArray();
            });
        }

        /// <summary>
        /// Normaliza um endereço MAC para o formato canônico "AA:BB:CC:DD:EE:FF".
        /// Tolera entrada já formatada, com traço, ou vazia.
        /// </summary>
        private static string FormatMacAddress(System.Net.NetworkInformation.PhysicalAddress address)
        {
            if (address == null) return "00:00:00:00:00:00";

            var bytes = address.GetAddressBytes();
            if (bytes == null || bytes.Length == 0) return "00:00:00:00:00:00";
            if (bytes.Length == 6) return string.Join(":", bytes.Select(b => b.ToString("X2")));

            // Comprimento inesperado: devolve como o .NET formatou, mas sem quebrar.
            return address.ToString();
        }

        public async Task<HardwareCapabilities> GetHardwareCapabilitiesAsync()
        {
            if (_cachedCapabilities != null && DateTime.Now - _lastCacheUpdate < CacheDuration)
                return _cachedCapabilities;

            var capabilities = new HardwareCapabilities();
            
            try
            {
                capabilities.Cpu = await GetCpuInfoAsync();
                capabilities.Gpu = await GetGpuInfoAsync();
                capabilities.Ram = await GetRamInfoAsync();
                capabilities.Drives = await GetDrivesInfoAsync();
                capabilities.NetworkAdapters = await GetNetworkInfoAsync();
                
                _cachedCapabilities = capabilities;
                _lastCacheUpdate = DateTime.Now;
            }
            catch
            {
            }
            
            return capabilities;
        }

        public async Task<double> GetCpuUsageAsync()
        {
            return await SafeTask.Run(() =>
            {
                try
                {
                    using (var pc = new SafePerformanceCounter("Processor", "% Processor Time", "_Total"))
                    {
                        pc.NextValue();
                        System.Threading.Thread.Sleep(1000);
                        return pc.NextValue();
                    }
                }
                catch
                {
                    return 0;
                }
            });
        }

        public string GetWindowsVersion()
        {
            if (_cachedWindowsVersion != null) return _cachedWindowsVersion;

            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                using var __dispose_obj = obj;
                        string caption = obj["Caption"]?.ToString() ?? "";
                        string version = obj["Version"]?.ToString() ?? "";
                        
                        // Limpar o nome
                        caption = caption.Replace("Microsoft ", "").Trim();
                        
                        // Detectar edição específica (LTSC, Pro, Home, Enterprise, etc)
                        string edition = "";
                        
                        // Verificar se é LTSC
                        if (caption.Contains("LTSC", StringComparison.OrdinalIgnoreCase))
                        {
                            // Extrair ano do LTSC (2019, 2021, etc)
                            if (caption.Contains("2021"))
                                edition = "LTSC 2021";
                            else if (caption.Contains("2019"))
                                edition = "LTSC 2019";
                            else
                                edition = "LTSC";
                        }
                        else if (caption.Contains("Enterprise", StringComparison.OrdinalIgnoreCase))
                        {
                            edition = "Enterprise";
                        }
                        else if (caption.Contains("Pro", StringComparison.OrdinalIgnoreCase))
                        {
                            edition = "Pro";
                        }
                        else if (caption.Contains("Home", StringComparison.OrdinalIgnoreCase))
                        {
                            edition = "Home";
                        }
                        else if (caption.Contains("Education", StringComparison.OrdinalIgnoreCase))
                        {
                            edition = "Education";
                        }
                        
                        // Determinar se é Windows 10 ou 11
                        var parts = version.Split('.');
                        int build = 0;
                        if (parts.Length >= 3 && int.TryParse(parts[2], out build))
                        {
                            string windowsVersion = build >= 22000 ? "Windows 11" : "Windows 10";
                            
                            if (!string.IsNullOrEmpty(edition))
                                return $"{windowsVersion} {edition}";
                            else
                                return windowsVersion;
                        }
                        
                        // Fallback: retornar caption limpo
                        _cachedWindowsVersion = caption;
                        return _cachedWindowsVersion;
                    }
                }
            }
            catch
            {
                _cachedWindowsVersion = Environment.OSVersion.ToString();
                return _cachedWindowsVersion;
            }
            
            _cachedWindowsVersion = "Windows Desconhecido";
            return _cachedWindowsVersion;
        }

        public string GetWindowsEdition()
        {
            if (_cachedWindowsEdition != null) return _cachedWindowsEdition;

            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                using var __dispose_obj = obj;
                        string caption = obj["Caption"]?.ToString() ?? "";
                        
                        // Detectar edição específica
                        if (caption.Contains("LTSC", StringComparison.OrdinalIgnoreCase))
                        {
                            if (caption.Contains("2021"))
                                return "LTSC 2021";
                            else if (caption.Contains("2019"))
                                return "LTSC 2019";
                            else
                                return "LTSC";
                        }
                        else if (caption.Contains("Enterprise", StringComparison.OrdinalIgnoreCase))
                            return "Enterprise";
                        else if (caption.Contains("Pro", StringComparison.OrdinalIgnoreCase))
                            return "Pro";
                        else if (caption.Contains("Home", StringComparison.OrdinalIgnoreCase))
                            return "Home";
                        else if (caption.Contains("Education", StringComparison.OrdinalIgnoreCase))
                            return "Education";
                        
                        _cachedWindowsEdition = "Standard";
                        return _cachedWindowsEdition;
                    }
                }
            }
            catch { }
            
            _cachedWindowsEdition = "Unknown";
            return _cachedWindowsEdition;
        }

        public int GetWindowsBuild()
        {
            if (_cachedWindowsBuild != null) return _cachedWindowsBuild.Value;

            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                using var __dispose_obj = obj;
                        string version = obj["Version"]?.ToString() ?? "";
                        var parts = version.Split('.');
                        if (parts.Length >= 3)
                        {
                            if (int.TryParse(parts[2], out int build))
                            {
                                _cachedWindowsBuild = build;
                                return build;
                            }
                        }
                        break;
                    }
                }
            }
            catch
            {
            }
            
            _cachedWindowsBuild = Environment.OSVersion.Version.Build;
            return _cachedWindowsBuild.Value;
        }

        public bool IsWindows11()
        {
            return GetWindowsBuild() >= 22000;
        }

        public bool Is64BitOperatingSystem()
        {
            return Environment.Is64BitOperatingSystem;
        }

        private bool IsDriveSSD(string driveLetter, string model)
        {
            if (string.IsNullOrEmpty(model))
            {
                // Fallback rápido baseado em tamanho se o WMI falhar
                try
                {
                    var drive = new System.IO.DriveInfo(driveLetter);
                    if (drive.IsReady)
                    {
                        return drive.TotalSize < 2000L * 1024 * 1024 * 1024;
                    }
                }
                catch { }
                return false;
            }

            model = model.ToUpperInvariant();
            return model.Contains("SSD") ||
                   model.Contains("NVME") ||
                   model.Contains("SOLID STATE") ||
                   model.Contains("CRUCIAL") ||
                   model.Contains("SAMSUNG") ||
                   model.Contains("KINGSTON") ||
                   model.Contains("SANDISK") ||
                   model.Contains("WD GREEN") ||
                   model.Contains("WD BLUE") ||
                   (model.Contains("INTEL") && model.Contains("SSD"));
        }

        public async Task<bool> IsRunningAsAdministratorAsync()
        {
            return await SafeTask.Run(() =>
            {
                try
                {
                    var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                    var principal = new System.Security.Principal.WindowsPrincipal(identity);
                    return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                }
                catch
                {
                    return false;
                }
            });
        }

        public async Task<long> GetAvailableDiskSpaceMBAsync()
        {
            return await SafeTask.Run(() =>
            {
                try
                {
                    var drive = new System.IO.DriveInfo(Path.GetPathRoot(Environment.CurrentDirectory));
                    return drive.AvailableFreeSpace / (1024 * 1024);
                }
                catch
                {
                    return 0;
                }
            });
        }

        public async Task<List<string>> CheckConflictingProcessesAsync(List<string> conflictingProcesses)
        {
            return await SafeTask.Run(() =>
            {
                var runningConflictingProcesses = new List<string>();
                try
                {
                    var processes = Process.GetProcesses();
                    foreach (var process in processes)
                    {
                        try
                        {
                            if (conflictingProcesses.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase))
                            {
                                runningConflictingProcesses.Add(process.ProcessName);
                            }
                        }
                        catch
                        {
                        }
                    }
                }
                catch
                {
                }
                return runningConflictingProcesses;
            });
        }

        public async Task<List<string>> CheckSystemIntegrityAsync()
        {
            return await SafeTask.Run(() =>
            {
                var issues = new List<string>();
                return issues;
            });
        }

        public async Task<double> GetGpuUsageAsync()
        {
            return await SafeTask.Run(() => 0.0);
        }

        public async Task<double> GetMemoryUsageAsync()
        {
            var ramInfo = await GetRamInfoAsync();
            return ramInfo.UsagePercent;
        }

        public async Task<double> GetIoActivityAsync()
        {
            return await SafeTask.Run(() => 0.0);
        }

        public async Task<List<object>> GetActiveProcessInfoAsync()
        {
            return await SafeTask.Run(() =>
            {
                var processInfoList = new List<object>();
                try
                {
                    var processes = Process.GetProcesses();
                    foreach (var process in processes)
                    {
                        try
                        {
                            processInfoList.Add(new
                            {
                                Name = process.ProcessName,
                                Id = process.Id,
                                MemoryMB = process.WorkingSet64 / (1024 * 1024),
                                CpuTime = process.TotalProcessorTime.TotalSeconds
                            });
                        }
                        catch
                        {
                        }
                    }
                }
                catch
                {
                }
                return processInfoList;
            });
        }

        public bool IsLaptop()
        {
            if (_hub != null) return _hub.IsLaptop;
            return false;
        }

        public bool IsOnBattery()
        {
            try
            {
                if (GetSystemPowerStatus(out var status))
                {
                    return status.ACLineStatus == 0;
                }
            }
            catch { }
            return false;
        }
    }
}

