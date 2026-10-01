  using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Gamer.GameCategorization;
using VoltrisOptimizer.Services.Gamer.GameCategorization.Models;
using VoltrisOptimizer.Services.Gamer.HardwareProfiling;
using VoltrisOptimizer.Services.Gamer.Pipeline.Models;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer.OBrain
{
    public class OBrainService : IOBrain
    {
        private readonly ILoggingService _logger;
        private readonly IAdvancedHardwareProfiler _hwProfiler;
        private readonly IGameCategorizerEngine _gameCategorizer;

        public OBrainService(
            ILoggingService logger,
            IAdvancedHardwareProfiler hwProfiler,
            IGameCategorizerEngine gameCategorizer)
        {
            _logger = logger;
            _hwProfiler = hwProfiler;
            _gameCategorizer = gameCategorizer;
        }

        public async Task<HardwareProfile> ProfileHardwareAsync(CancellationToken ct = default)
        {
            _logger.LogInfo("[OBrain] 🔍 Perfilando hardware...");
            var sw = Stopwatch.StartNew();

            var profile = new HardwareProfile();

            try
            {
                var advancedProfile = await _hwProfiler.GetCurrentProfileAsync(ct);

                profile.CpuName = advancedProfile.Cpu.Name ?? "";
                profile.PhysicalCores = advancedProfile.Cpu.PhysicalCores;
                profile.LogicalCores = advancedProfile.Cpu.LogicalCores;
                profile.IsHybrid = advancedProfile.Cpu.PCores > 0 && advancedProfile.Cpu.ECores > 0;
                profile.PerformanceCores = advancedProfile.Cpu.PCores;
                profile.EfficientCores = advancedProfile.Cpu.ECores;
                profile.MaxFrequencyMhz = advancedProfile.Cpu.MaxFrequencyMHz;
                profile.HasVCache = advancedProfile.Cpu.HasVCache;
                profile.IsThreadDirectorSupported = advancedProfile.Cpu.IsThreadDirectorSupported;

                profile.GpuName = advancedProfile.Gpu.Name ?? "";
                profile.GpuVendor = advancedProfile.Gpu.Vendor.ToString();
                profile.GpuVideoMemoryMB = (long)(advancedProfile.Gpu.VramTotalMB);
                profile.IsDiscreteGpu = !advancedProfile.Gpu.IsIntegrated;
                profile.SupportsHags = advancedProfile.Gpu.SupportsHags;
                profile.SupportsVrr = false;

                profile.TotalRamGB = advancedProfile.Memory.TotalCapacityMB / 1024.0;
                profile.RamSpeedMhz = advancedProfile.Memory.SpeedMHz;
                profile.IsDualChannel = advancedProfile.Memory.IsDualChannel;
                profile.StorageType = advancedProfile.PrimaryStorage.Tier.ToString();

                profile.IsLaptop = advancedProfile.MachineClass.ToString().Contains("Notebook");
                profile.MachineClass = advancedProfile.MachineClass.ToString();

                profile.IsOnBattery = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus ==
                    System.Windows.Forms.PowerLineStatus.Offline;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[OBrain] Erro no perfil de hardware: {ex.Message}");
                profile = await ProfileHardwareFallbackAsync(ct);
            }

            sw.Stop();
            _logger.LogInfo($"[OBrain] Hardware perfilado em {sw.ElapsedMilliseconds}ms: {profile.CpuName} | {profile.GpuName} | {profile.TotalRamGB:F0}GB RAM");
            return profile;
        }

        private Task<HardwareProfile> ProfileHardwareFallbackAsync(CancellationToken ct)
        {
            _logger.LogInfo("[OBrain.ProfileHardwareFallbackAsync] Entry");
            var profile = new HardwareProfile();
            try
            {
                var cpu = new ManagementObjectSearcher("SELECT * FROM Win32_Processor")
                    .Get().Cast<ManagementObject>().FirstOrDefault();
                if (cpu != null)
                {
                    profile.CpuName = cpu["Name"]?.ToString() ?? "";
                    profile.PhysicalCores = Convert.ToInt32(cpu["NumberOfCores"]);
                    profile.LogicalCores = Convert.ToInt32(cpu["NumberOfLogicalProcessors"]);
                    profile.MaxFrequencyMhz = Convert.ToDouble(cpu["MaxClockSpeed"]);
                }

                var gpu = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController")
                    .Get().Cast<ManagementObject>().FirstOrDefault();
                if (gpu != null)
                {
                    profile.GpuName = gpu["Name"]?.ToString() ?? "";
                    profile.GpuVideoMemoryMB = Convert.ToInt64(gpu["AdapterRAM"] ?? 0) / 1024 / 1024;
                    profile.IsDiscreteGpu = profile.GpuVideoMemoryMB > 2048;
                }

                var mem = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem")
                    .Get().Cast<ManagementObject>().FirstOrDefault();
                if (mem != null)
                {
                    profile.TotalRamGB = Convert.ToDouble(mem["TotalPhysicalMemory"] ?? 0) / 1024 / 1024 / 1024;
                }
            }
            catch { }
            _logger.LogInfo("[OBrain.ProfileHardwareFallbackAsync] Exit");
            return Task.FromResult(profile);
        }

        public async Task<SystemProfile> ProfileSystemAsync(CancellationToken ct = default)
        {
            _logger.LogInfo("[OBrain] 🔍 Perfilando sistema operacional...");
            var profile = new SystemProfile();

            try
            {
                var os = Environment.OSVersion;
                profile.WindowsVersion = os.VersionString;
                profile.BuildNumber = os.Version.Build;

                using var powerKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes");
                profile.ActivePowerPlanGuid = powerKey?.GetValue("ActivePowerScheme")?.ToString() ?? "";

                profile.CoreParkingEnabled = true;

                profile.RunningProcessCount = Process.GetProcesses().Length;

                profile.CpuUsage = await GetCpuUsageAsync();
                profile.RamUsagePercent = GetRamUsagePercent();
                profile.AvailableRamGB = GetAvailableRamGB();

                profile.CpuTemperature = await GetCpuTemperatureAsync();

                profile.CurrentTimerResolutionMs = 15.625;

                profile.GameBarEnabled = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\GameBar")?.GetValue("AllowAutoGameMode")?.ToString() == "1";
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[OBrain] Perfil de sistema parcial: {ex.Message}");
            }

            _logger.LogInfo($"[OBrain] SO: {profile.WindowsVersion} | Processos: {profile.RunningProcessCount} | CPU: {profile.CpuUsage:F1}% | RAM: {profile.RamUsagePercent:F0}%");
            return profile;
        }

        public async Task<GameAnalysis> ProfileGameAsync(string executablePath, CancellationToken ct = default)
        {
            _logger.LogInfo($"[OBrain] 🔍 Perfilando jogo: {executablePath}");
            var analysis = new GameAnalysis
            {
                ExecutablePath = executablePath,
                Name = System.IO.Path.GetFileNameWithoutExtension(executablePath)
            };

            try
            {
                var gameProfile = await _gameCategorizer.AnalyzeGameAsync(executablePath, ct);
                analysis.Category = gameProfile.Category;
                analysis.Engine = gameProfile.Engine;
                analysis.ConfidenceLevel = gameProfile.ConfidenceLevel;

                var processes = Process.GetProcessesByName(analysis.Name);
                if (processes.Length > 0)
                {
                    var proc = processes[0];
                    analysis.MemoryUsageMB = proc.WorkingSet64 / 1024 / 1024;

                    try
                    {
                        var hasWindow = proc.MainWindowHandle != IntPtr.Zero;
                        _logger.LogInfo($"[OBrain] Jogo {analysis.Name} PID={proc.Id} Janela={hasWindow} Mem={analysis.MemoryUsageMB:F0}MB");
                    }
                    catch { }

                    proc.Dispose();
                }

                _logger.LogInfo($"[OBrain] Jogo perfilado: {analysis.Name} | Categoria: {analysis.Category} | Engine: {analysis.Engine} | Confiança: {analysis.ConfidenceLevel}%");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[OBrain] Perfil de jogo parcial: {ex.Message}");
                analysis.Category = GameCategory.AAA;
            }

            return analysis;
        }

        public async Task<GamerSessionContext> BuildFullProfileAsync(string? gameExecutable, CancellationToken ct = default)
        {
            var context = new GamerSessionContext
            {
                GameExecutable = gameExecutable,
                GameProcessName = gameExecutable != null
                    ? System.IO.Path.GetFileNameWithoutExtension(gameExecutable)
                    : null
            };

            context.Hardware = await ProfileHardwareAsync(ct);
            context.System = await ProfileSystemAsync(ct);

            if (gameExecutable != null)
            {
                context.Game = await ProfileGameAsync(gameExecutable, ct);
                context.GameProcessName = context.Game.Name;
            }

            _logger.LogSuccess($"[OBrain] ✅ Perfil completo construído para sessão {context.SessionId[..8]}");
            return context;
        }

        private async Task<double> GetCpuUsageAsync()
        {
            _logger.LogInfo("[OBrain.GetCpuUsageAsync] Entry");
            try
            {
                var perfCpu = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                perfCpu.NextValue();
                await Task.Delay(200);
                return Math.Round(perfCpu.NextValue(), 1);
            }
            catch { _logger.LogInfo("[OBrain.GetCpuUsageAsync] Exit (error)"); return 0; }
            _logger.LogInfo("[OBrain.GetCpuUsageAsync] Exit");
        }

        private double GetRamUsagePercent()
        {
            _logger.LogInfo("[OBrain.GetRamUsagePercent] Entry");
            try
            {
                var perfRam = new PerformanceCounter("Memory", "% Committed Bytes In Use");
                return Math.Round(perfRam.NextValue(), 1);
            }
            catch { _logger.LogInfo("[OBrain.GetRamUsagePercent] Exit (error)"); return 0; }
            _logger.LogInfo("[OBrain.GetRamUsagePercent] Exit");
        }

        private double GetAvailableRamGB()
        {
            _logger.LogInfo("[OBrain.GetAvailableRamGB] Entry");
            try
            {
                var perfAvail = new PerformanceCounter("Memory", "Available MBytes");
                return Math.Round(perfAvail.NextValue() / 1024.0, 1);
            }
            catch { _logger.LogInfo("[OBrain.GetAvailableRamGB] Exit (error)"); return 0; }
            _logger.LogInfo("[OBrain.GetAvailableRamGB] Exit");
        }

        private async Task<double> GetCpuTemperatureAsync()
        {
            _logger.LogInfo("[OBrain.GetCpuTemperatureAsync] Entry");
            try
            {
                var searcher = new ManagementObjectSearcher(
                    "SELECT Temperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation");
                foreach (var obj in searcher.Get())
                {
                using var __dispose_obj = obj;
                    var temp = Convert.ToDouble(obj["Temperature"]);
                    if (temp > 0) return Math.Round((temp - 273.15), 1);
                }
            }
            catch { }
            _logger.LogInfo("[OBrain.GetCpuTemperatureAsync] Exit");
            return 0;
        }
    }
}