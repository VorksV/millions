using System;
using System.Diagnostics;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Utils;
using PerformanceOptimizer = VoltrisOptimizer.Services.VoltrisPerformanceOptimizer;

namespace VoltrisOptimizer.Core.SystemIntelligenceProfiler
{
    public class AuditCollector : IAuditCollector
    {
        public async Task<AuditData> CollectAsync(CancellationToken ct)
        {
            var data = new AuditData();
            var startTime = DateTime.UtcNow;

            // Timeout global de 12s - suficiente para todas as tarefas paralelas
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(12));

            try
            {
                try { App.LoggingService?.LogInfo("[AuditCollector] Iniciando coleta paralela"); } catch { }

                var tCpu = Task.Run(() =>
                {
                    try
                    {
                        data.Cpu.Model = SystemInfoService.GetProcessorName();
                        data.Cpu.LogicalCores = Environment.ProcessorCount;
                        CollectCpuDetails(data);
                    }
                    catch (Exception ex) { try { App.LoggingService?.LogWarning("[AuditCollector] CPU: " + ex.Message); } catch { } }
                }, cts.Token);

                var tRam = Task.Run(() =>
                {
                    try
                    {
                        var memInfo = new PerformanceCounter("Memory", "Available MBytes");
                        data.Ram.AvailableMb = (long)memInfo.NextValue();
                        data.Ram.TotalMb = (long)(SystemInfoService.GetTotalRAMBytes() / (1024 * 1024));
                        data.Ram.SpeedMhz = GetRamSpeedMhz();
                        data.Ram.SwapUsageMb = GetSwapUsageMb();
                        data.Ram.Type = InferRamType(data.Ram.SpeedMhz);
                    }
                    catch (Exception ex) { try { App.LoggingService?.LogWarning("[AuditCollector] RAM: " + ex.Message); } catch { } }
                }, cts.Token);

                var tGpu = Task.Run(() =>
                {
                    try { CollectGpuDetails(data); }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) { try { App.LoggingService?.LogWarning("[AuditCollector] GPU: " + ex.Message); } catch { } }
                }, cts.Token);

                var tStorage = Task.Run(() =>
                {
                    try { CollectStorageDetails(data); }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) { try { App.LoggingService?.LogWarning("[AuditCollector] Storage: " + ex.Message); } catch { } }
                }, cts.Token);

                var tNic = Task.Run(() =>
                {
                    try { CollectNicDetails(data); }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) { try { App.LoggingService?.LogWarning("[AuditCollector] NIC: " + ex.Message); } catch { } }
                }, cts.Token);

                var tDisplay = Task.Run(() =>
                {
                    try { CollectDisplayDetails(data); }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) { try { App.LoggingService?.LogWarning("[AuditCollector] Display: " + ex.Message); } catch { } }
                }, cts.Token);

                var tBattery = Task.Run(() =>
                {
                    try
                    {
                        data.Battery.Present = DetectBattery();
                        if (data.Battery.Present)
                        {
                            data.Battery.EstimatedChargePercent = GetBatteryPercent();
                            data.Battery.Status = GetBatteryStatus();
                        }
                    }
                    catch (Exception ex) { try { App.LoggingService?.LogWarning("[AuditCollector] Battery: " + ex.Message); } catch { } }
                }, cts.Token);

                var tWindows = Task.Run(() =>
                {
                    try
                    {
                        data.Windows.Build = GetWindowsBuild();
                        data.Windows.Version = SystemInfoService.GetOSVersion();
                    }
                    catch (Exception ex) { try { App.LoggingService?.LogWarning("[AuditCollector] Windows: " + ex.Message); } catch { } }
                }, cts.Token);

                var tDpc = Task.Run(() =>
                {
                    try { data.DpcLatency = SampleDpcLatency(); }
                    catch (Exception ex) { try { App.LoggingService?.LogWarning("[AuditCollector] DPC: " + ex.Message); } catch { } }
                }, cts.Token);

                var tPower = Task.Run(() =>
                {
                    try { data.CurrentPowerPlan = GetActivePowerPlan(); }
                    catch (Exception ex) { try { App.LoggingService?.LogWarning("[AuditCollector] Power: " + ex.Message); } catch { } }
                }, cts.Token);

                var tThermal = Task.Run(() =>
                {
                    try
                    {
                        data.CpuTemp = GetCpuTemperature();
                        data.GpuTemp = GetGpuTemperature();
                    }
                    catch (Exception ex) { try { App.LoggingService?.LogWarning("[AuditCollector] Thermal: " + ex.Message); } catch { } }
                }, cts.Token);

                await Task.WhenAll(tCpu, tRam, tGpu, tStorage, tNic, tDisplay, tBattery, tWindows, tDpc, tPower, tThermal).ConfigureAwait(false);

                try { ClassifyEnvironment(data); }
                catch (Exception ex) { try { App.LoggingService?.LogWarning("[AuditCollector] Classify: " + ex.Message); } catch { } }

                try { App.LoggingService?.LogSuccess($"[AuditCollector] Coleta concluída em {(DateTime.UtcNow - startTime).TotalSeconds:F1}s. Tier: {data.PerfTier} | Thermal: {data.ThermalStatus}"); } catch { }
            }
            catch (OperationCanceledException)
            {
                try { App.LoggingService?.LogWarning("[AuditCollector] Coleta cancelada/timeout"); } catch { }
            }
            catch (Exception ex)
            {
                try { App.LoggingService?.LogError("[AuditCollector] Erro fatal: " + ex.Message, ex); } catch { }
                throw;
            }

            return data;
        }

        public AuditCollector()
        {
            try
            {
                App.LoggingService?.LogInfo("[AuditCollector] Construtor chamado");
            }
            catch (Exception)
            {
            }
        }

        private static void CollectCpuDetails(AuditData data)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");
                searcher.Options.Timeout = TimeSpan.FromSeconds(10);
                using var results = searcher.Get();
                foreach (ManagementObject obj in results)
                {
                    data.Cpu.PhysicalCores = Convert.ToInt32(obj["NumberOfCores"] ?? 0);
                    data.Cpu.LogicalCores = Convert.ToInt32(obj["NumberOfLogicalProcessors"] ?? Environment.ProcessorCount);
                    data.Cpu.MaxFrequencyMhz = Convert.ToDouble(obj["MaxClockSpeed"] ?? 0);
                    break;
                }
            }
            catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.OutOfMemory) { }
            catch (Exception ex) { try { App.LoggingService?.LogWarning("[AuditCollector] CPU detalhes: " + ex.Message); } catch { } }

            try
            {
                data.Cpu.HyperThreading = data.Cpu.LogicalCores > data.Cpu.PhysicalCores;
            }
            catch (Exception ex) { try { App.LoggingService?.LogWarning("[AuditCollector] CPU recursos: " + ex.Message); } catch { } }
        }

        private static int GetRamSpeedMhz()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Speed FROM Win32_PhysicalMemory");
                searcher.Options.Timeout = TimeSpan.FromSeconds(10);
                int max = 0;
                using var results = searcher.Get();
                foreach (ManagementObject obj in results)
                {
                    int spd = Convert.ToInt32(obj["Speed"] ?? 0);
                    if (spd > max) max = spd;
                }
                return max;
            }
            catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.OutOfMemory) { return 0; }
            catch (Exception ex) { try { App.LoggingService?.LogWarning("[AuditCollector] RAM velocidade: " + ex.Message); } catch { } return 0; }
        }

        private static string InferRamType(int speedMhz)
        {
            if (speedMhz == 0) return "Unknown";
            if (speedMhz >= 4800) return "DDR5";
            if (speedMhz >= 2133) return "DDR4";
            if (speedMhz >= 800) return "DDR3";
            return "DDR2/Older";
        }

        private static long GetSwapUsageMb()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT AllocatedBaseSize, CurrentUsage FROM Win32_PageFileUsage");
                searcher.Options.Timeout = TimeSpan.FromSeconds(10);
                long usageMb = 0;
                using var results = searcher.Get();
                foreach (ManagementObject obj in results)
                {
                    usageMb += Convert.ToInt64(obj["CurrentUsage"] ?? 0);
                }
                return usageMb;
            }
            catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.OutOfMemory) { return 0; }
            catch (Exception ex) { try { App.LoggingService?.LogWarning("[AuditCollector] Pagefile uso: " + ex.Message); } catch { } return 0; }
        }

        private static string GetActivePowerPlan()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes", false);
                return key?.GetValue("ActivePowerScheme")?.ToString() ?? "";
            }
            catch { return ""; }
        }

        private static void CollectGpuDetails(AuditData data)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM, DriverVersion FROM Win32_VideoController");
                searcher.Options.Timeout = TimeSpan.FromSeconds(5);
                // searcher.Options.EnhancedOptions = new ManagementOptions { Timeout = TimeSpan.FromSeconds(5) }; // Removido: incompatível com .NET 8
                using var results = searcher.Get();
                foreach (ManagementObject obj in results)
                {
                    var name = (obj["Name"]?.ToString() ?? "").Trim();
                    var ramBytes = Convert.ToInt64(obj["AdapterRAM"] ?? 0);
                    data.Gpu.Model = name;
                    data.Gpu.VramMb = (long)(ramBytes / (1024 * 1024));
                    data.Gpu.DriverVersion = obj["DriverVersion"]?.ToString() ?? "";
                    var lower = name.ToLowerInvariant();
                    data.Gpu.Vendor = lower.Contains("nvidia") ? "NVIDIA" : lower.Contains("amd") || lower.Contains("radeon") ? "AMD" : lower.Contains("intel") ? "Intel" : "Unknown";
                    data.Gpu.IsIntegrated = data.Gpu.Vendor == "Intel" || lower.Contains("intel");
                    break;
                }
            }
            catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.OutOfMemory) { }
            catch (ManagementException ex) when (ex.Message.Contains("Timed out") || ex.Message.Contains("timeout")) 
            { 
                try { App.LoggingService?.LogWarning("[AuditCollector] GPU detalhes: Timeout na consulta WMI"); } catch { } 
            }
            catch (Exception ex) { try { App.LoggingService?.LogWarning("[AuditCollector] GPU detalhes: " + ex.Message); } catch { } }

            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\Dwm", false);
                var val = key?.GetValue("HwSchMode");
                data.Gpu.HagsSupported = val != null && data.Windows.Build >= 19041;
            }
            catch (Exception ex) 
            { 
                data.Gpu.HagsSupported = false; 
                try { App.LoggingService?.LogWarning("[AuditCollector] GPU HAGS: " + ex.Message); } catch { } 
            }
        }

        private static void CollectStorageDetails(AuditData data)
        {
            try
            {
                data.Storage.FreeSpaceMb = SystemInfoService.GetFreeDiskSpaceBytes() / (1024 * 1024);
                data.Storage.TotalSpaceMb = SystemInfoService.GetTotalDiskSpaceBytes() / (1024 * 1024);
                
                using var searcher = new ManagementObjectSearcher("SELECT Model, MediaType, InterfaceType FROM Win32_DiskDrive");
                searcher.Options.Timeout = TimeSpan.FromSeconds(10);
                using var drives = searcher.Get();
                foreach (ManagementObject obj in drives)
                {
                    var model = obj["Model"]?.ToString() ?? "";
                    var media = obj["MediaType"]?.ToString() ?? "";
                    var iface = obj["InterfaceType"]?.ToString() ?? "";
                    data.Storage.SystemDiskModel = model;
                    var m = (model + " " + media + " " + iface).ToLowerInvariant();
                    if (m.Contains("nvme")) data.Storage.SystemDiskType = "NVMe";
                    else if (m.Contains("ssd")) data.Storage.SystemDiskType = "SSD";
                    else data.Storage.SystemDiskType = "HDD";
                    break;
                }
            }
            catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.OutOfMemory) { }
            catch (Exception ex) { try { App.LoggingService?.LogWarning("[AuditCollector] Storage detalhes: " + ex.Message); } catch { } }

            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT PredictFailure FROM MSStorageDriver_FailurePredictStatus");
                searcher.Options.Timeout = TimeSpan.FromSeconds(10);
                bool found = false;
                using var smartResults = searcher.Get();
                foreach (ManagementObject obj in smartResults)
                {
                    var predict = Convert.ToInt32(obj["PredictFailure"] ?? 0);
                    data.Storage.SmartOk = predict == 0;
                    found = true;
                    break;
                }
                
                if (!found)
                {
                    data.Storage.SmartOk = true;
                }
            }
            catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.InvalidNamespace 
                                                  || ex.ErrorCode == ManagementStatus.NotFound
                                                  || ex.Message.Contains("Acesso negado")
                                                  || ex.Message.Contains("Sem suporte"))
            {
                data.Storage.SmartOk = true; 
            }
            catch (Exception) 
            { 
                data.Storage.SmartOk = true; 
            }
        }

        private static void CollectNicDetails(AuditData data)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name, Manufacturer FROM Win32_NetworkAdapter WHERE NetEnabled = TRUE OR PhysicalAdapter = TRUE");
                searcher.Options.Timeout = TimeSpan.FromSeconds(5);
                // searcher.Options.EnhancedOptions = new ManagementOptions { Timeout = TimeSpan.FromSeconds(5) }; // Removido: incompatível com .NET 8
                using var nics = searcher.Get();
                foreach (ManagementObject obj in nics)
                {
                    data.Nic.Vendor = obj["Manufacturer"]?.ToString() ?? obj["Name"]?.ToString() ?? "";
                    break;
                }
            }
            catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.OutOfMemory) { }
            catch (ManagementException ex) when (ex.Message.Contains("Timed out") || ex.Message.Contains("timeout")) 
            { 
                try { App.LoggingService?.LogWarning("[AuditCollector] NIC detalhes: Timeout na consulta WMI"); } catch { } 
            }
            catch (Exception ex) { try { App.LoggingService?.LogWarning("[AuditCollector] NIC detalhes: " + ex.Message); } catch { } }
            
            try
            {
                var output = RunCommand("netsh int tcp show global");
                if (!string.IsNullOrEmpty(output))
                {
                    data.Nic.SupportsRss = output.IndexOf("Receive-Side Scaling State", StringComparison.OrdinalIgnoreCase) >= 0 && output.IndexOf("enabled", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch (Exception ex) 
            { 
                try { App.LoggingService?.LogWarning("[AuditCollector] NIC capacidades: " + ex.Message); } catch { } 
            }
        }

        internal static string RunCommand(string cmd)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c " + cmd,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                };
                using var p = System.Diagnostics.Process.Start(psi);
                if (p == null) return "";
                
                if (p.WaitForExit(10000))
                {
                    return p.StandardOutput.ReadToEnd();
                }
                else
                {
                    try { p.Kill(); } catch { }
                    return "";
                }
            }
            catch { return ""; }
        }

        private static bool DetectBattery()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Battery");
                searcher.Options.Timeout = TimeSpan.FromSeconds(10);
                using var batResults = searcher.Get();
                foreach (var _ in batResults) { return true; }
            }
            catch (Exception ex) { try { App.LoggingService?.LogWarning("[AuditCollector] Bateria detectar: " + ex.Message); } catch { } }
            return false;
        }

        private static int GetBatteryPercent()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT EstimatedChargeRemaining FROM Win32_Battery");
                searcher.Options.Timeout = TimeSpan.FromSeconds(10);
                using var batPct = searcher.Get();
                foreach (ManagementObject obj in batPct)
                {
                    return Convert.ToInt32(obj["EstimatedChargeRemaining"] ?? 0);
                }
            }
            catch (Exception ex) { try { App.LoggingService?.LogWarning("[AuditCollector] Bateria porcentagem: " + ex.Message); } catch { } }
            return 0;
        }

        private static string GetBatteryStatus()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT BatteryStatus FROM Win32_Battery");
                searcher.Options.Timeout = TimeSpan.FromSeconds(10);
                using var batStatus = searcher.Get();
                foreach (ManagementObject obj in batStatus)
                {
                    var v = Convert.ToInt32(obj["BatteryStatus"] ?? 0);
                    return v switch { 1 => "Desconhecido", 2 => "Carregando", 3 => "Descarregando", 4 => "Não carregando", _ => "" };
                }
            }
            catch (Exception ex) { try { App.LoggingService?.LogWarning("[AuditCollector] Bateria status: " + ex.Message); } catch { } }
            return "";
        }

        private static int GetWindowsBuild()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT BuildNumber FROM Win32_OperatingSystem");
                searcher.Options.Timeout = TimeSpan.FromSeconds(10);
                using var osBuild = searcher.Get();
                foreach (ManagementObject obj in osBuild)
                {
                    return int.TryParse(obj["BuildNumber"]?.ToString(), out var bn) ? bn : 0;
                }
            }
            catch (Exception ex) { try { App.LoggingService?.LogWarning("[AuditCollector] Windows build: " + ex.Message); } catch { } }
            return 0;
        }

        private static bool GetSecureBoot()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State", false);
                var v = key?.GetValue("UEFISecureBootEnabled");
                return Convert.ToInt32(v ?? 0) == 1;
            }
            catch (Exception ex) 
            { 
                try { App.LoggingService?.LogWarning("[AuditCollector] SecureBoot: " + ex.Message); } catch { } 
                return false; 
            }
        }

        private static bool GetDriverSigningPolicy()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Kernel", false);
                var v = key?.GetValue("DisableExceptionChainValidation");
                return Convert.ToInt32(v ?? 0) == 0; // true => enforcement
            }
            catch (Exception ex) 
            { 
                try { App.LoggingService?.LogWarning("[AuditCollector] Driver signing: " + ex.Message); } catch { } 
                return true; 
            }
        }

        private static bool IsHyperVActive()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT HypervisorPresent FROM Win32_ComputerSystem");
                searcher.Options.Timeout = TimeSpan.FromSeconds(10);
                using var hv = searcher.Get();
                foreach (ManagementObject obj in hv)
                {
                    return Convert.ToBoolean(obj["HypervisorPresent"] ?? false);
                }
            }
            catch (Exception ex) { try { App.LoggingService?.LogWarning("[AuditCollector] Hyper-V: " + ex.Message); } catch { } }
            return false;
        }

        private static bool IsWslInstalled()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Lxss", false);
                return key != null;
            }
            catch (Exception ex) 
            { 
                try { App.LoggingService?.LogWarning("[AuditCollector] WSL: " + ex.Message); } catch { } 
                return false; 
            }
        }

        private static DpcLatencyInfo SampleDpcLatency()
        {
            var info = new DpcLatencyInfo();
            try
            {
                using var dpc  = new VoltrisOptimizer.Utils.SafePerformanceCounter("Processor Information", "DPC Rate", "_Total");
                using var ints = new VoltrisOptimizer.Utils.SafePerformanceCounter("Processor", "Interrupts/sec", "_Total");
                dpc.NextValue(); ints.NextValue();
                System.Threading.Thread.Sleep(200);
                info.DpcRate          = dpc.NextValue();
                info.InterruptsPerSec = ints.NextValue();
            }
            catch (Exception ex)
            {
                try { App.LoggingService?.LogWarning($"[AuditCollector] DPC Latency: {ex.Message}"); } catch { }
                info.DpcRate = 0; info.InterruptsPerSec = 0;
            }
            return info;
        }

        private static int CountStartupApps()
        {
            int count = 0;
            try
            {
                using var k1 = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
                using var k2 = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
                count += k1?.ValueCount ?? 0;
                count += k2?.ValueCount ?? 0;
            }
            catch (Exception ex)
            {
                try { App.LoggingService?.LogWarning($"[AuditCollector] Erro ao contar apps de startup: {ex.Message}"); } catch { }
            }
            return count;
        }

        private static int GetRunningServicesCount()
        {
            try
            {
                var services = System.ServiceProcess.ServiceController.GetServices();
                return Math.Min(services.Length, 1000); 
            }
            catch (Exception ex) 
            { 
                try { App.LoggingService?.LogWarning($"[AuditCollector] Erro ao contar serviços: {ex.Message}"); } catch { }
                return 0; 
            }
        }

        private static double GetCpuTemperature()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
                searcher.Options.Timeout = TimeSpan.FromSeconds(5);
                using var tempResult = searcher.Get();
                foreach (ManagementObject obj in tempResult)
                {
                    double rawTemp = Convert.ToDouble(obj["CurrentTemperature"]);
                    return (rawTemp - 2732) / 10.0; // Kelvin to Celsius
                }
            }
            catch { }
            return 0.0;
        }

        private static double GetGpuTemperature()
        {
            return 0.0; 
        }

        private static void CollectDisplayDetails(AuditData data)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT CurrentRefreshRate, CurrentHorizontalResolution, CurrentVerticalResolution FROM Win32_VideoController");
                searcher.Options.Timeout = TimeSpan.FromSeconds(5);
                using var display = searcher.Get();
                foreach (ManagementObject obj in display)
                {
                    data.Display.RefreshRateHz = Convert.ToInt32(obj["CurrentRefreshRate"] ?? 60);
                    var h = obj["CurrentHorizontalResolution"]?.ToString() ?? "0";
                    var v = obj["CurrentVerticalResolution"]?.ToString() ?? "0";
                    data.Display.Resolution = $"{h}x{v}";
                    break;
                }
            }
            catch { data.Display.RefreshRateHz = 60; }
        }

        private static void ClassifyEnvironment(AuditData data)
        {
            bool weakCpu = data.Cpu.PhysicalCores < 4;
            bool lowRam = data.Ram.TotalMb < 8192;
            bool slowDisk = data.Storage.SystemDiskType == "HDD";

            if (weakCpu || lowRam || slowDisk)
                data.PerfTier = PerformanceTier.LowEnd;
            else if (data.Cpu.PhysicalCores >= 8 && data.Ram.TotalMb >= 32000 && data.Gpu.VramMb >= 8000)
                data.PerfTier = PerformanceTier.Workstation;
            else if (data.Cpu.PhysicalCores >= 6 && data.Ram.TotalMb >= 16000 && data.Gpu.VramMb >= 6000)
                data.PerfTier = PerformanceTier.HighEnd;
            else
                data.PerfTier = PerformanceTier.MidRange;

            if (data.CpuTemp > 90 || data.GpuTemp > 90 || data.IsThermalThrottling)
                data.ThermalStatus = ThermalTier.Critical;
            else if (data.CpuTemp > 80 || data.GpuTemp > 80)
                data.ThermalStatus = ThermalTier.Warm;
            else
                data.ThermalStatus = ThermalTier.Stable;
        }
    }
}
