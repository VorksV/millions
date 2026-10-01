using System;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Gamer.HardwareProfiling.Models;

namespace VoltrisOptimizer.Services.Gamer.HardwareProfiling
{
    public class AdvancedHardwareProfilerService : IAdvancedHardwareProfiler
    {
        private readonly ILoggingService _logger;
        private AdvancedHardwareProfile? _cachedProfile;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public AdvancedHardwareProfilerService(ILoggingService logger)
        {
            _logger = logger;
        }

        public async Task<AdvancedHardwareProfile> GetCurrentProfileAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInfo("[HardwareProfiler.GetCurrentProfileAsync] Entry");
            if (_cachedProfile != null)
            {
                _logger.LogInfo("[HardwareProfiler.GetCurrentProfileAsync] Exit (cached)");
                return _cachedProfile;
            }

            await _lock.WaitAsync(cancellationToken);
            try
            {
                if (_cachedProfile != null)
                {
                    _logger.LogInfo("[HardwareProfiler.GetCurrentProfileAsync] Exit (cached after lock)");
                    return _cachedProfile;
                }
                
                _logger.LogInfo("[HardwareProfiler] Iniciando detecção de topologia física...");
                var profile = new AdvancedHardwareProfile();
                
                profile.IsWindows11 = Environment.OSVersion.Version.Build >= 22000;
                
                await DetectCpuTopologyAsync(profile.Cpu);
                await DetectMemoryAsync(profile.Memory);
                await DetectGpuArchitectureAsync(profile.Gpu);
                await DetectStorageAsync(profile.PrimaryStorage);
                
                ClassifyMachine(profile);
                
                _cachedProfile = profile;
                
                var logDetails = $@"
[HARDWARE PROFILER - TELEMETRIA ESTRUTURADA]
==================================================
Classificação: {profile.MachineClass}
SO: {(profile.IsWindows11 ? "Windows 11" : "Windows 10")}
---
CPU: {profile.Cpu.Name}
 - Topologia: {profile.Cpu.PhysicalCores} Físicos / {profile.Cpu.LogicalCores} Lógicos
 - P-Cores: {profile.Cpu.PCores} | E-Cores: {profile.Cpu.ECores}
 - Recursos: {(profile.Cpu.HasVCache ? "V-Cache (X3D) " : "")}{(profile.Cpu.IsThreadDirectorSupported ? "Thread Director " : "")}
 - CCDs: {profile.Cpu.CCDCount} | Frequência Max: {profile.Cpu.MaxFrequencyMHz}MHz
---
GPU: {profile.Gpu.Name} ({profile.Gpu.Vendor})
 - Tier: {profile.Gpu.Tier}
 - VRAM: {profile.Gpu.VramTotalMB} MB
 - Integrada: {profile.Gpu.IsIntegrated}
 - Suporta HAGS: {profile.Gpu.SupportsHags}
---
Memória RAM: {profile.Memory.TotalCapacityMB} MB
 - Frequência: {profile.Memory.SpeedMHz} MHz
 - Dual Channel: {profile.Memory.IsDualChannel}
---
Armazenamento Primário: {profile.PrimaryStorage.DriveLetter}
 - Tipo: {profile.PrimaryStorage.Tier}
==================================================";
                
                _logger.LogInfo(logDetails);
                
                _logger.LogInfo("[HardwareProfiler.GetCurrentProfileAsync] Exit");
                return _cachedProfile;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[HardwareProfiler] Falha crítica na detecção: {ex.Message}");
                _logger.LogInfo("[HardwareProfiler.GetCurrentProfileAsync] Exit (error)");
                return new AdvancedHardwareProfile { MachineClass = MachineClass.BasicDesktop }; // Fallback seguro
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task RefreshProfileAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInfo("[HardwareProfiler.RefreshProfileAsync] Entry");
            await _lock.WaitAsync(cancellationToken);
            try
            {
                _cachedProfile = null;
            }
            finally
            {
                _lock.Release();
            }
            await GetCurrentProfileAsync(cancellationToken);
            _logger.LogInfo("[HardwareProfiler.RefreshProfileAsync] Exit");
        }

        private Task DetectCpuTopologyAsync(CpuTopologyInfo cpu)
        {
            _logger.LogInfo("[HardwareProfiler.DetectCpuTopologyAsync] Entry");
            return Task.Run(() =>
            {
                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, L3CacheSize FROM Win32_Processor");
                    foreach (var item in searcher.Get().Cast<ManagementBaseObject>())
                    {
                        cpu.Name = item["Name"]?.ToString() ?? "Unknown CPU";
                        cpu.PhysicalCores = Convert.ToInt32(item["NumberOfCores"] ?? 1);
                        cpu.LogicalCores = Convert.ToInt32(item["NumberOfLogicalProcessors"] ?? 1);
                        cpu.MaxFrequencyMHz = Convert.ToUInt32(item["MaxClockSpeed"] ?? 1000);
                        cpu.HasHyperThreading = cpu.LogicalCores > cpu.PhysicalCores;
                        
                        // Detecção de Intel Hybrid (Thread Director)
                        if (cpu.IsIntel)
                        {
                            bool is12thGenOrNewer = cpu.Name.Contains("-12") || cpu.Name.Contains("-13") || cpu.Name.Contains("-14") || cpu.Name.Contains("Core Ultra");
                            if (is12thGenOrNewer)
                            {
                                cpu.IsThreadDirectorSupported = true;
                                // Heurística básica de P/E Cores (Ex: 14 cores, 20 threads = 6P + 8E)
                                // P-cores = (Logical - Physical) -> Assumindo que apenas P-Cores tem HT.
                                int pCores = cpu.LogicalCores - cpu.PhysicalCores;
                                if (pCores > 0 && pCores <= cpu.PhysicalCores)
                                {
                                    cpu.PCores = pCores;
                                    cpu.ECores = cpu.PhysicalCores - pCores;
                                }
                                else
                                {
                                    cpu.PCores = cpu.PhysicalCores;
                                    cpu.ECores = 0;
                                }
                            }
                            else
                            {
                                cpu.PCores = cpu.PhysicalCores;
                                cpu.ECores = 0;
                            }
                        }
                        else if (cpu.IsAmd)
                        {
                            cpu.PCores = cpu.PhysicalCores;
                            cpu.ECores = 0;
                            
                            // Detecção de V-Cache (X3D) e CCDs
                            cpu.HasVCache = cpu.Name.Contains("X3D", StringComparison.OrdinalIgnoreCase);
                            
                            // AMD Ryzen 9 geralmente tem 2 CCDs
                            if (cpu.Name.Contains("Ryzen 9") || cpu.PhysicalCores >= 12)
                            {
                                cpu.CCDCount = 2;
                                cpu.IsNuma = true; // Em termos de latência cross-CCD
                            }
                            else
                            {
                                cpu.CCDCount = 1;
                            }
                        }
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[HardwareProfiler] Erro lendo Win32_Processor: {ex.Message}");
                }
            });
        }

        private Task DetectMemoryAsync(MemoryProfile memory)
        {
            _logger.LogInfo("[HardwareProfiler.DetectMemoryAsync] Entry");
            return Task.Run(() =>
            {
                try
                {
                    ulong totalBytes = 0;
                    uint speed = 0;
                    int dimmCount = 0;
                    
                    using var searcher = new ManagementObjectSearcher("SELECT Capacity, Speed FROM Win32_PhysicalMemory");
                    foreach (var item in searcher.Get().Cast<ManagementBaseObject>())
                    {
                        totalBytes += Convert.ToUInt64(item["Capacity"] ?? 0);
                        var s = Convert.ToUInt32(item["Speed"] ?? 0);
                        if (s > speed) speed = s;
                        dimmCount++;
                    }
                    
                    memory.TotalCapacityMB = totalBytes / (1024 * 1024);
                    memory.SpeedMHz = speed;
                    memory.IsDualChannel = dimmCount >= 2;
                    _logger.LogInfo("[HardwareProfiler.DetectMemoryAsync] Exit");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[HardwareProfiler] Erro lendo RAM: {ex.Message}");
                    memory.TotalCapacityMB = 8192;
                    _logger.LogInfo("[HardwareProfiler.DetectMemoryAsync] Exit (error)");
                }
            });
        }

        private Task DetectGpuArchitectureAsync(GpuArchitectureInfo gpu)
        {
            _logger.LogInfo("[HardwareProfiler.DetectGpuArchitectureAsync] Entry");
            return Task.Run(() =>
            {
                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController");
                    ulong bestRam = 0;
                    
                    foreach (var item in searcher.Get().Cast<ManagementBaseObject>())
                    {
                        var name = item["Name"]?.ToString() ?? "";
                        var ram = Convert.ToUInt64(item["AdapterRAM"] ?? 0);
                        
                        // Preferir GPU dedicada em sistemas com Optimus/Hybrid
                        if (ram > bestRam || (name.Contains("NVIDIA") || name.Contains("AMD Radeon RX")))
                        {
                            bestRam = ram;
                            gpu.Name = name;
                            gpu.VramTotalMB = ram / (1024 * 1024);
                            
                            var upperName = name.ToUpperInvariant();
                            if (upperName.Contains("NVIDIA") || upperName.Contains("GEFORCE")) gpu.Vendor = GpuVendor.NVIDIA;
                            else if (upperName.Contains("AMD") || upperName.Contains("RADEON")) gpu.Vendor = GpuVendor.AMD;
                            else if (upperName.Contains("ARC")) gpu.Vendor = GpuVendor.IntelArc;
                            else gpu.Vendor = GpuVendor.IntelIntegrated;
                            
                            gpu.IsIntegrated = upperName.Contains("UHD") || upperName.Contains("IRIS") || upperName.Contains("RADEON GRAPHICS");
                            
                            // Determinar Tier (Heurística baseada em VRAM e Nomenclatura)
                            if (gpu.IsIntegrated) gpu.Tier = GpuTier.Entry;
                            else if (gpu.VramTotalMB >= 16000 || upperName.Contains("RTX 4090") || upperName.Contains("RTX 4080") || upperName.Contains("7900 XTX")) gpu.Tier = GpuTier.Enthusiast;
                            else if (gpu.VramTotalMB >= 8000) gpu.Tier = GpuTier.HighEnd;
                            else if (gpu.VramTotalMB >= 4000) gpu.Tier = GpuTier.MidRange;
                            else gpu.Tier = GpuTier.Entry;
                        }
                    }
                    
                    // Hardware-Accelerated GPU Scheduling Check (HAGS)
                    try
                    {
                        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
                        if (key != null)
                        {
                            var hwSchMode = key.GetValue("HwSchMode");
                            gpu.SupportsHags = hwSchMode != null; // Se a chave existir no driver, a GPU suporta
                        }
                    }
                    catch { }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[HardwareProfiler] Erro lendo GPU: {ex.Message}");
                    gpu.Name = "Unknown GPU";
                }
                _logger.LogInfo("[HardwareProfiler.DetectGpuArchitectureAsync] Exit");
            });
        }

        private Task DetectStorageAsync(StorageProfile storage)
        {
            _logger.LogInfo("[HardwareProfiler.DetectStorageAsync] Entry");
            return Task.Run(() =>
            {
                try
                {
                    storage.IsSystemDrive = true;
                    
                    using var searcher = new ManagementObjectSearcher("SELECT Model, InterfaceType FROM Win32_DiskDrive");
                    foreach (var item in searcher.Get().Cast<ManagementBaseObject>())
                    {
                        var model = item["Model"]?.ToString()?.ToUpperInvariant() ?? "";
                        var interfaceType = item["InterfaceType"]?.ToString()?.ToUpperInvariant() ?? "";
                        
                        if (model.Contains("NVME") || interfaceType.Contains("NVME"))
                        {
                            if (model.Contains("GEN4") || model.Contains("SN850") || model.Contains("980 PRO") || model.Contains("990 PRO"))
                                storage.Tier = StorageTier.NVMeGen4;
                            else if (model.Contains("GEN5") || model.Contains("T700"))
                                storage.Tier = StorageTier.NVMeGen5;
                            else
                                storage.Tier = StorageTier.NVMeGen3;
                        }
                        else if (model.Contains("SSD") || interfaceType.Contains("SCSI"))
                        {
                            storage.Tier = StorageTier.SataSSD;
                        }
                        else
                        {
                            storage.Tier = StorageTier.HDD;
                        }
                        break;
                    }
                    _logger.LogInfo("[HardwareProfiler.DetectStorageAsync] Exit");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[HardwareProfiler] Erro lendo Storage: {ex.Message}");
                    storage.Tier = StorageTier.SataSSD;
                    _logger.LogInfo("[HardwareProfiler.DetectStorageAsync] Exit (error)");
                }
            });
        }

        private void ClassifyMachine(AdvancedHardwareProfile p)
        {
            _logger.LogInfo("[HardwareProfiler.ClassifyMachine] Entry");
            bool isLaptop = false;
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT ChassisTypes FROM Win32_SystemEnclosure");
                foreach (var item in searcher.Get().Cast<ManagementBaseObject>())
                {
                    var chassis = item["ChassisTypes"] as ushort[];
                    if (chassis != null && chassis.Length > 0)
                    {
                        var t = chassis[0];
                        if (t == 8 || t == 9 || t == 10 || t == 11 || t == 14 || t == 31)
                            isLaptop = true;
                    }
                }
            }
            catch { }

            int score = 0;
            if (p.Cpu.PhysicalCores >= 8) score += 2;
            else if (p.Cpu.PhysicalCores >= 6) score += 1;

            if (p.Memory.TotalCapacityMB >= 16000) score += 2;
            else if (p.Memory.TotalCapacityMB >= 8000) score += 1;

            if (p.Gpu.Tier == GpuTier.Enthusiast || p.Gpu.Tier == GpuTier.HighEnd) score += 3;
            else if (p.Gpu.Tier == GpuTier.MidRange) score += 1;
            
            if (p.PrimaryStorage.Tier >= StorageTier.NVMeGen3) score += 1;

            if (isLaptop)
            {
                if (score >= 6) p.MachineClass = MachineClass.GamerNotebook;
                else if (score >= 3) p.MachineClass = MachineClass.MidNotebook;
                else p.MachineClass = MachineClass.BasicNotebook;
            }
            else
            {
                if (score >= 7) p.MachineClass = MachineClass.GamerDesktop;
                else if (score >= 5) p.MachineClass = MachineClass.AdvancedDesktop;
                else if (score >= 3) p.MachineClass = MachineClass.MidDesktop;
                else p.MachineClass = MachineClass.BasicDesktop;
            }
            _logger.LogInfo("[HardwareProfiler.ClassifyMachine] Exit");
        }
    }
}
