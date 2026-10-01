using System;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using VoltrisOptimizer.Services.Performance.CpuTuning.Models;

namespace VoltrisOptimizer.Services.Performance.CpuTuning
{
    /// <summary>
    /// Detects CPU vendor, generation, platform type, supported features, and locked limits
    /// </summary>
    public class HardwareCapabilityDetector : IHardwareCapabilityDetector
    {
        private readonly ILoggingService _logger;
        private readonly VoltrisOptimizer.Services.Performance.CpuTuning.Core.Interfaces.IHardwareBackend _backend;
        private readonly System.Threading.Tasks.Task _detectionTask;
        private CpuTuningCapabilities _cachedCapabilities;
        
        public HardwareCapabilityDetector(ILoggingService logger, VoltrisOptimizer.Services.Performance.CpuTuning.Core.Interfaces.IHardwareBackend backend)
        {
            _logger = logger;
            _backend = backend;

            // Safe defaults para evitar qualquer atraso no startup/ViewModels
            _cachedCapabilities = new CpuTuningCapabilities
            {
                CpuModel = "CPU Genérica",
                Vendor = CpuVendor.Intel,
                CoreCount = Math.Max(2, Environment.ProcessorCount / 2),
                ThreadCount = Environment.ProcessorCount,
                Platform = PlatformType.Desktop,
                Classification = MachineClass.MidRange,
                TjMax = 95.0,
                TjSafeMargin = 10.0,
                BaseTdp = 65,
                SupportsSpeedShift = false,
                SupportsTurboBoost = false,
                SupportsCStates = true,
                SupportsClockModulation = true,
                SupportsProchotOffset = false,
                SupportsTdpControl = false,
                SupportsPStateControl = true
            };

            // Iniciar detecção assíncrona em background
            _detectionTask = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var caps = DetectCapabilitiesInternal();
                    lock (this)
                    {
                        _cachedCapabilities = caps;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[CPU_Tuning] Falha na detecção assíncrona: {ex.Message}");
                }
            });
        }

        public CpuTuningCapabilities Capabilities => GetCapabilities();

        public CpuTuningCapabilities DetectCapabilities()
        {
            // Força sincronização apenas se chamado explicitamente
            try
            {
                _detectionTask.Wait(3000); // Evitar travamentos infinitos com timeout de 3s
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[CPU_Tuning] Timeout ou falha aguardando detecção: {ex.Message}");
            }
            return GetCapabilities();
        }
        
        public CpuTuningCapabilities GetCapabilities()
        {
            lock (this)
            {
                return _cachedCapabilities;
            }
        }
        
        public bool IsSettingLocked(string settingName)
        {
            var caps = GetCapabilities();
            return caps.LockedSettings.Contains(settingName);
        }

        private CpuTuningCapabilities DetectCapabilitiesInternal()
        {
            _logger.LogInfo("[CPU_Tuning] Starting hardware capability detection...");
            
            var capabilities = new CpuTuningCapabilities();
            
            try
            {
                // Detect CPU vendor and model
                DetectCpuInfo(capabilities);
                
                // Detect platform type (laptop vs desktop)
                DetectPlatformType(capabilities);
                
                // Detect supported features
                DetectSupportedFeatures(capabilities);
                
                // Detect locked limits
                DetectLockedLimits(capabilities);
                
                // Classify machine tier
                ClassifyMachine(capabilities);
                
                // Calculate thermal limits
                CalculateThermalLimits(capabilities);

                // Apply per-CPU gamer presets (valores equivalentes a ThrottleStop quando conhecidos/testados)
                ApplyPerCpuGamerOverrides(capabilities);
                
                _logger.LogInfo($"[CPU_Tuning] Detection complete: {capabilities.Vendor} {capabilities.CpuModel}, " +
                               $"Platform: {capabilities.Platform}, Class: {capabilities.Classification}");
                
                return capabilities;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[CPU_Tuning] Hardware detection failed: {ex.Message}");
                
                // Return safe defaults
                capabilities.Classification = MachineClass.EnterpriseRestricted;
                return capabilities;
            }
        }
        
        private void DetectCpuInfo(CpuTuningCapabilities caps)
        {
            try
            {
                // 1. Tentar ler do Registro (Ultra-rápido, <1ms)
                string cpuName = Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", "")?.ToString()?.Trim() ?? "";
                string vendorId = Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "VendorIdentifier", "")?.ToString()?.Trim() ?? "";
                
                if (!string.IsNullOrEmpty(cpuName))
                {
                    caps.CpuModel = cpuName;
                    
                    if (vendorId.Contains("Intel", StringComparison.OrdinalIgnoreCase) || cpuName.Contains("Intel", StringComparison.OrdinalIgnoreCase))
                    {
                        caps.Vendor = CpuVendor.Intel;
                        caps.CpuGeneration = DetectIntelGeneration(cpuName);
                    }
                    else if (vendorId.Contains("AMD", StringComparison.OrdinalIgnoreCase) || cpuName.Contains("AMD", StringComparison.OrdinalIgnoreCase))
                    {
                        caps.Vendor = CpuVendor.AMD;
                        caps.CpuGeneration = DetectAmdGeneration(cpuName);
                    }
                    
                    caps.ThreadCount = Environment.ProcessorCount;
                    // Estimar número de cores físicos
                    caps.CoreCount = GetPhysicalCoreCountFallback(caps.ThreadCount);
                    
                    // Base TDP estimation
                    caps.BaseTdp = EstimateBaseTdp(caps.CoreCount, 3000, caps.Platform, cpuName);
                    
                    _logger.LogInfo($"[CPU_Tuning] CPU detectada via Registro: {caps.CpuModel} ({caps.CoreCount} Cores, {caps.ThreadCount} Threads)");
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[CPU_Tuning] Falha ao ler CPU info via Registro: {ex.Message}");
            }

            // 2. Fallback para WMI se o registro falhar por algum motivo
            try
            {
                _logger.LogInfo("[CPU_Tuning] Iniciando WMI fallback para CPU...");
                using var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");
                foreach (ManagementObject obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    string name = obj["Name"]?.ToString() ?? "";
                    caps.CpuModel = name;
                    
                    if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase))
                    {
                        caps.Vendor = CpuVendor.Intel;
                        caps.CpuGeneration = DetectIntelGeneration(name);
                    }
                    else if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase))
                    {
                        caps.Vendor = CpuVendor.AMD;
                        caps.CpuGeneration = DetectAmdGeneration(name);
                    }
                    
                    caps.CoreCount = Convert.ToInt32(obj["NumberOfCores"] ?? 0);
                    caps.ThreadCount = Convert.ToInt32(obj["NumberOfLogicalProcessors"] ?? 0);
                    
                    uint maxClockSpeed = Convert.ToUInt32(obj["MaxClockSpeed"] ?? 0);
                    caps.BaseTdp = EstimateBaseTdp(caps.CoreCount, maxClockSpeed, caps.Platform, name);
                    break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[CPU_Tuning] CPU info WMI fallback failed: {ex.Message}");
            }
        }

        private int GetPhysicalCoreCountFallback(int threadCount)
        {
            if (threadCount <= 2) return threadCount;
            if (threadCount == 6) return 6;
            if (threadCount % 2 == 0) return threadCount / 2;
            return threadCount;
        }
        
        private int DetectIntelGeneration(string cpuName)
        {
            // PROFISSIONAL: Arrow Lake S (Core Ultra 200S) e Lunar Lake (Core Ultra 200V) = gen 15
            // Meteor Lake (Core Ultra 100) = gen 14 mobile
            if (cpuName.Contains("Core Ultra 2", StringComparison.OrdinalIgnoreCase) ||
                cpuName.Contains("Ultra 200", StringComparison.OrdinalIgnoreCase))
                return 15; // Arrow Lake S / Lunar Lake

            if (cpuName.Contains("Core Ultra", StringComparison.OrdinalIgnoreCase))
                return 14; // Meteor Lake (Core Ultra 100 series)

            if (cpuName.Contains("i9-14") || cpuName.Contains("i7-14") || cpuName.Contains("i5-14") || cpuName.Contains("i3-14")) return 14;
            if (cpuName.Contains("i9-13") || cpuName.Contains("i7-13") || cpuName.Contains("i5-13") || cpuName.Contains("i3-13")) return 13;
            if (cpuName.Contains("i9-12") || cpuName.Contains("i7-12") || cpuName.Contains("i5-12") || cpuName.Contains("i3-12")) return 12;
            if (cpuName.Contains("i9-11") || cpuName.Contains("i7-11") || cpuName.Contains("i5-11") || cpuName.Contains("i3-11")) return 11;
            if (cpuName.Contains("i9-10") || cpuName.Contains("i7-10") || cpuName.Contains("i5-10") || cpuName.Contains("i3-10")) return 10;
            if (cpuName.Contains("i9-9") || cpuName.Contains("i7-9") || cpuName.Contains("i5-9") || cpuName.Contains("i3-9")) return 9;
            if (cpuName.Contains("i7-8") || cpuName.Contains("i5-8") || cpuName.Contains("i3-8")) return 8;
            if (cpuName.Contains("i7-7") || cpuName.Contains("i5-7") || cpuName.Contains("i3-7")) return 7;
            if (cpuName.Contains("i7-6") || cpuName.Contains("i5-6") || cpuName.Contains("i3-6")) return 6;
            
            return 0; // Unknown/Older
        }
        
        private int DetectAmdGeneration(string cpuName)
        {
            // PROFISSIONAL: Zen 5 (Ryzen 9000 series) — Granite Ridge / Strix Point
            if (cpuName.Contains("Ryzen 9 9") || cpuName.Contains("Ryzen 7 9") || 
                cpuName.Contains("Ryzen 5 9") || cpuName.Contains("Ryzen 3 9")) return 5; // Zen 5

            // Zen 4 (Ryzen 7000 series)
            if (cpuName.Contains("Ryzen 9 7") || cpuName.Contains("Ryzen 7 7") || 
                cpuName.Contains("Ryzen 5 7") || cpuName.Contains("Ryzen 3 7")) return 4; // Zen 4

            // Zen 3 (Ryzen 5000 series)
            if (cpuName.Contains("Ryzen 9 5") || cpuName.Contains("Ryzen 7 5") || 
                cpuName.Contains("Ryzen 5 5") || cpuName.Contains("Ryzen 3 5")) return 3; // Zen 3

            // Zen 2 (Ryzen 3000 series)
            if (cpuName.Contains("Ryzen 9 3") || cpuName.Contains("Ryzen 7 3") || 
                cpuName.Contains("Ryzen 5 3") || cpuName.Contains("Ryzen 3 3")) return 2; // Zen 2

            // Zen 1/+ (Ryzen 1000/2000 series)
            if (cpuName.Contains("Ryzen 7 2") || cpuName.Contains("Ryzen 5 2") || cpuName.Contains("Ryzen 3 2") ||
                cpuName.Contains("Ryzen 7 1") || cpuName.Contains("Ryzen 5 1") || cpuName.Contains("Ryzen 3 1")) return 1;
            
            return 0; 
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

        private void DetectPlatformType(CpuTuningCapabilities caps)
        {
            try
            {
                bool hasBattery = false;
                if (GetSystemPowerStatus(out SYSTEM_POWER_STATUS status))
                {
                    hasBattery = status.BatteryFlag != 128 && status.BatteryFlag != 255;
                }
                
                caps.Platform = hasBattery ? PlatformType.Laptop : PlatformType.Desktop;
                
                _logger.LogInfo($"[CPU_Tuning] Platform detected: {caps.Platform}");
            }
            catch
            {
                // If detection fails, assume desktop (safer)
                caps.Platform = PlatformType.Desktop;
            }
        }
        
        private void DetectSupportedFeatures(CpuTuningCapabilities caps)
        {
            // Intel Speed Shift (HWP) - supported from 6th gen onwards
            if (caps.Vendor == CpuVendor.Intel && caps.CpuGeneration >= 6)
            {
                caps.SupportsSpeedShift = true;
            }
            
            // Turbo Boost - supported on most modern CPUs
            if (caps.Vendor == CpuVendor.Intel && caps.CpuGeneration >= 6)
            {
                caps.SupportsTurboBoost = true;
            }
            else if (caps.Vendor == CpuVendor.AMD && caps.CpuGeneration >= 1)
            {
                caps.SupportsTurboBoost = true; // Precision Boost
            }
            
            // C-States - universally supported
            caps.SupportsCStates = true;
            
            // Clock Modulation - supported on most CPUs
            caps.SupportsClockModulation = true;
            
            // PROCHOT offset - Intel only, varies by platform
            if (caps.Vendor == CpuVendor.Intel && caps.Platform == PlatformType.Desktop)
            {
                caps.SupportsProchotOffset = true;
            }
            
            // TDP Control - supported on laptops and advanced desktops
            caps.SupportsTdpControl = (caps.Platform == PlatformType.Laptop || 
                                      caps.Classification >= MachineClass.HighEnd);
                                      
            // P-State control - universally supported via power settings
            caps.SupportsPStateControl = true;
            
            _logger.LogInfo($"[CPU_Tuning] Features: SpeedShift={caps.SupportsSpeedShift}, " +
                           $"Turbo={caps.SupportsTurboBoost}, CStates={caps.SupportsCStates}, " +
                           $"TDPControl={caps.SupportsTdpControl}");
        }
        
        private void DetectLockedLimits(CpuTuningCapabilities caps)
        {
            // On laptops, most OEMs lock power limits
            if (caps.Platform == PlatformType.Laptop)
            {
                _logger.LogInfo("[CPU_Tuning] Laptop detected - checking MSR lock status...");
            }
            
            if (_backend.IsDriverLoaded())
            {
                if (_backend.ReadMsr(0x610, out ulong limitMsr))
                {
                    bool isLocked = (limitMsr & (1UL << 63)) != 0;
                    if (isLocked)
                    {
                        caps.LockedSettings.Add("PL1");
                        caps.LockedSettings.Add("PL2");
                        caps.LockedSettings.Add("Tau");
                        _logger.LogWarning("[CPU_Tuning] MSR 0x610 is hardware locked (Bit 63 active). Power limits can only be read.");
                    }
                }
                
                // Read REAL Base TDP from MSR 0x614 (Package Power Info)
                if (_backend.ReadMsr(0x614, out ulong info))
                {
                    // Bits 14:0 is Thermal Design Power (TDP)
                    // This is the absolute factory base.
                    double pUnit = 0.125; // Default fallback
                    // Note: LowLevelService already handles units, but for detection we want a clean score
                    caps.BaseTdp = (int)((info & 0x7FFF) * pUnit);
                    _logger.LogInfo($"[CPU_Tuning] Factory Base TDP detected: {caps.BaseTdp}W");
                }
            }
            
            // Check registry for locked indicators (some OEMs set flags)
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power");
                if (key != null)
                {
                    var locked = key.GetValue("CsEnabled");
                    if (locked != null && locked.ToString() == "0")
                    {
                        caps.LockedSettings.Add("CStates");
                    }
                }
            }
            catch { }
        }
        
        private void ClassifyMachine(CpuTuningCapabilities caps)
        {
            if (IsEnterpriseManaged())
            {
                caps.Classification = MachineClass.EnterpriseRestricted;
                return;
            }
            
            double score = 0;
            
            // Core count score (0-40)
            score += Math.Min(caps.CoreCount * 2.5, 40);
            
            // Generation score (0-30)
            if (caps.Vendor == CpuVendor.Intel)
                score += Math.Min(caps.CpuGeneration * 2, 30);
            else if (caps.Vendor == CpuVendor.AMD)
                score += Math.Min(caps.CpuGeneration * 7, 30);
                
            // Clock speed score (0-20)
            if (caps.BaseTdp >= 125) score += 20;
            else if (caps.BaseTdp >= 95) score += 15;
            else if (caps.BaseTdp >= 65) score += 10;
            else if (caps.BaseTdp >= 45) score += 5;
            
            // Laptop penalty (-10)
            if (caps.Platform == PlatformType.Laptop) score -= 10;
            
            // Final classification
            if (score >= 75) caps.Classification = MachineClass.Enthusiast;
            else if (score >= 55) caps.Classification = MachineClass.HighEnd;
            else if (score >= 35) caps.Classification = MachineClass.MidRange;
            else if (score >= 20) caps.Classification = MachineClass.Entry;
            else caps.Classification = MachineClass.LowEnd;
            
            _logger.LogInfo($"[CPU_Tuning] Machine classified as: {caps.Classification} (score: {score})");
        }
        
        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int NetGetJoinInformation(string? lpServer, out IntPtr lpNameBuffer, out int BufferType);

        [DllImport("netapi32.dll")]
        private static extern int NetApiBufferFree(IntPtr Buffer);

        private bool IsDomainJoined()
        {
            try
            {
                IntPtr nameBuffer = IntPtr.Zero;
                int bufferType = 0; // NetJoinUnknown
                int result = NetGetJoinInformation(null, out nameBuffer, out bufferType);
                if (result == 0 && nameBuffer != IntPtr.Zero)
                {
                    NetApiBufferFree(nameBuffer);
                    return bufferType == 3; // NetJoinDomain = 3
                }
            }
            catch { }
            return false;
        }

        private bool IsEnterpriseManaged()
        {
            if (IsDomainJoined())
            {
                _logger.LogInfo("[CPU_Tuning] Machine detected as domain-joined (Enterprise Restricted)");
                return true;
            }

            try
            {
                // Check for enterprise group policy restrictions
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Power\PowerSettings");
                if (key != null)
                {
                    _logger.LogInfo("[CPU_Tuning] Group policy power restrictions present (Enterprise Restricted)");
                    return true;
                }
            }
            catch { }
            
            return false;
        }
        
        private void CalculateThermalLimits(CpuTuningCapabilities caps)
        {
            // PROFISSIONAL: TjMax correto por vendor/geração (datasheet oficial)
            if (caps.Vendor == CpuVendor.Intel)
            {
                if (caps.CpuGeneration >= 10)
                    caps.TjMax = 100.0; // Modern Intel (10th gen+)
                else
                    caps.TjMax = 95.0; // Older Intel
            }
            else if (caps.Vendor == CpuVendor.AMD)
            {
                // CORREÇÃO: Zen 4 (Ryzen 7000) e Zen 5 (Ryzen 9000) têm TjMax = 95°C (desktop) 
                // mas Tctl offset pode reportar até 100°C. Usamos 95°C como limite real.
                if (caps.CpuGeneration >= 4)
                    caps.TjMax = 95.0; // Zen 4/5 — TjMax real = 95°C
                else if (caps.CpuGeneration >= 3)
                    caps.TjMax = 90.0; // Zen 3
                else
                    caps.TjMax = 85.0; // Zen 1/2
            }
            
            // Safety margin based on machine class
            caps.TjSafeMargin = caps.Classification switch
            {
                MachineClass.LowEnd => 15.0,
                MachineClass.Entry => 12.0,
                MachineClass.MidRange => 10.0,
                MachineClass.HighEnd => 8.0,
                MachineClass.Enthusiast => 7.0,
                _ => 10.0
            };
            
            _logger.LogInfo($"[CPU_Tuning] Thermal limits: TjMax={caps.TjMax}°C, TjSafe={caps.TjSafe}°C");
        }
        
        private int EstimateBaseTdp(int coreCount, uint maxClockMhz, PlatformType platform, string modelName)
        {
            // PROFISSIONAL: Sufixos Intel completos
            if (modelName.Contains("KS", StringComparison.OrdinalIgnoreCase)) return 150; // Intel KS (Special Edition)
            if (modelName.Contains("KF", StringComparison.OrdinalIgnoreCase) || 
                modelName.Contains(" K", StringComparison.OrdinalIgnoreCase)) return 125; // Intel K/KF (Unlocked)
            if (modelName.Contains("F", StringComparison.OrdinalIgnoreCase) && 
                !modelName.Contains("KF", StringComparison.OrdinalIgnoreCase) &&
                platform == PlatformType.Desktop) return 65; // Intel F (no iGPU, same TDP as non-F)
            if (modelName.Contains("HX", StringComparison.OrdinalIgnoreCase)) return 55; // Intel HX (Mobile Enthusiast)
            if (modelName.Contains(" H", StringComparison.OrdinalIgnoreCase)) return 45; // Intel H (Mobile Performance)
            if (modelName.Contains(" P", StringComparison.OrdinalIgnoreCase)) return 28; // Intel P (Mobile Balanced)
            if (modelName.Contains(" U", StringComparison.OrdinalIgnoreCase)) return 15; // Intel U (Ultrabook)

            // PROFISSIONAL: Sufixos AMD completos
            if (modelName.Contains("X3D", StringComparison.OrdinalIgnoreCase)) return 120; // AMD X3D (3D V-Cache)
            if (modelName.Contains("XT", StringComparison.OrdinalIgnoreCase)) return 105; // AMD XT (Refresh)
            if (modelName.Contains("X", StringComparison.OrdinalIgnoreCase) && 
                !modelName.Contains("HX", StringComparison.OrdinalIgnoreCase) &&
                !modelName.Contains("XT", StringComparison.OrdinalIgnoreCase) &&
                !modelName.Contains("X3D", StringComparison.OrdinalIgnoreCase) &&
                platform == PlatformType.Desktop) return 105; // AMD X (Desktop Performance)
            
            if (platform == PlatformType.Laptop) return 28;
            return 65; // Desktop padrão
        }

        private void ApplyPerCpuGamerOverrides(CpuTuningCapabilities caps)
        {
            try
            {
                if (caps.Vendor == CpuVendor.Intel && caps.Platform == PlatformType.Laptop)
                {
                    // Exemplo prático: Intel i5-1135G7 (Tiger Lake-U 28W) como no print do ThrottleStop
                    if (caps.CpuModel.Contains("i5-1135G7", StringComparison.OrdinalIgnoreCase))
                    {
                        caps.GamerPl1Override = 62;
                        caps.GamerPl2Override = 68;
                        caps.GamerTauOverride = 28.0;
                        _logger.LogInfo("[CPU_Tuning] Gamer preset applied for i5-1135G7: PL1=62W, PL2=68W, Tau=28s");
                    }
                }
            }
            catch { }
        }
    }
}
