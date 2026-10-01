using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.Gamer.Interfaces;
using VoltrisOptimizer.Services.Performance.Decision.Models;
using VoltrisOptimizer.Services.Performance.Models;
using VoltrisOptimizer.Services.Telemetry;

namespace VoltrisOptimizer.Services.Performance.Orchestration
{
    /// <summary>
    /// Builder for PerformanceContext.
    /// Collects data from various sensors and profiles to create an immutable snapshot.
    /// Follows the "Principal Software Architect" vision.
    /// </summary>
    public sealed class PerformanceContextBuilder
    {
        private readonly IHardwareDetector _hardwareDetector;
        private readonly HardwareProfiler _profiler;
        private readonly IGameDetector _gameDetector;
        private readonly ILoggingService _logger;

        public PerformanceContextBuilder(
            IHardwareDetector hardwareDetector,
            HardwareProfiler profiler,
            IGameDetector gameDetector,
            ILoggingService logger)
        {
            _hardwareDetector = hardwareDetector ?? throw new ArgumentNullException(nameof(hardwareDetector));
            _profiler = profiler ?? throw new ArgumentNullException(nameof(profiler));
            _gameDetector = gameDetector ?? throw new ArgumentNullException(nameof(gameDetector));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Captures current system state and builds a PerformanceContext.
        /// </summary>
        public async Task<PerformanceContext> BuildAsync(CancellationToken ct = default)
        {
            _logger.LogInfo("[ContextBuilder] 🧊 Capturing intelligent system snapshot...");

            // 1. Get Hardware Capabilities (Cached in service)
            var caps = await _hardwareDetector.GetCapabilitiesAsync(ct);

            // 2. Get Real-time Vitals
            var vitals = _profiler.GetVitals();

            // 3. Map to Performance Profile
            var profile = MapToProfile(caps);

            // 4. Determine Tier & Classification
            var tier = DetermineTier(caps);
            var classification = DetermineClassification(caps, tier);

            // 5. Detect Active Application & Game Status
            var (activeAppName, activeAppPid, isGameRunning) = GetActiveApplicationInfo();

            // 6. Determine Usage Scenario
            var scenario = DetermineScenario(isGameRunning, vitals.CpuUsagePercent);

            // 7. Build the Context
            var context = new PerformanceContext(
                profile,
                tier,
                classification,
                vitals.HealthScore / 100.0, // Normalize to 0.0 - 1.0
                scenario,
                vitals.CpuUsagePercent,
                vitals.RamUsagePercent,
                vitals.DiskUsagePercent,
                0, // GPU usage (future)
                caps.IsOnBattery ? PowerState.Battery : PowerState.AC,
                caps.IsLaptop,
                caps.IsOnBattery,
                vitals.ProcessCount,
                activeAppName,
                activeAppPid,
                isGameRunning
            );

            _logger.LogInfo($"[ContextBuilder] ✅ Snapshot built: Tier={tier}, Scenario={scenario}, Game={isGameRunning}");

            return context;
        }

        private PerformanceSystemProfile MapToProfile(Gamer.Models.HardwareCapabilities caps)
        {
            return new PerformanceSystemProfile
            {
                CPUName = caps.CpuName,
                CPUCores = caps.CoreCount,
                TotalRAMGB = caps.TotalRamGb,
                GPUName = caps.PrimaryGpu.Name,
                HasDedicatedGPU = caps.HasDiscreteGpu,
                IsLaptop = caps.IsLaptop,
                IsHybrid = caps.IsHybridCpu,
                IsGamingPC = caps.HasDiscreteGpu && caps.TotalRamGb >= 16,
                WindowsVersion = Environment.OSVersion.Version,
                IsWindows11 = Environment.OSVersion.Version.Build >= 22000
            };
        }

        private HardwareTier DetermineTier(Gamer.Models.HardwareCapabilities caps)
        {
            // Enthusiast
            if (caps.TotalRamGb >= 32 && caps.CoreCount >= 12 && caps.PrimaryGpu.Name.Contains("RTX"))
                return HardwareTier.Enthusiast;

            // HighEnd
            if (caps.TotalRamGb >= 16 && caps.HasDiscreteGpu)
                return HardwareTier.HighEnd;

            // LowEnd
            if (caps.TotalRamGb <= 8 || !caps.HasDiscreteGpu)
                return HardwareTier.LowEnd;

            return HardwareTier.MidRange;
        }

        private Performance.Models.MachineClassification DetermineClassification(Gamer.Models.HardwareCapabilities caps, HardwareTier tier)
        {
            if (tier == HardwareTier.Enthusiast) return Performance.Models.MachineClassification.EnthusiastRig;
            
            if (caps.IsLaptop)
            {
                if (caps.HasDiscreteGpu) return Performance.Models.MachineClassification.GamingHighEnd;
                return Performance.Models.MachineClassification.LowEndOffice;
            }

            if (caps.HasDiscreteGpu)
            {
                if (tier >= HardwareTier.HighEnd) return Performance.Models.MachineClassification.GamingHighEnd;
                return Performance.Models.MachineClassification.GamingEntry;
            }

            return Performance.Models.MachineClassification.MidRangeWorkstation;
        }

        private (string? appName, int? appPid, bool isGame) GetActiveApplicationInfo()
        {
            try
            {
                int processId = VoltrisOptimizer.Core.ForegroundWindowTracker.Instance.CurrentPid;
                if (processId <= 0)
                    return (null, null, false);

                Process process;
                try
                {
                    process = Process.GetProcessById(processId);
                }
                catch (ArgumentException)
                {
                    VoltrisOptimizer.Core.ForegroundWindowTracker.Instance.InvalidateStalePid();
                    return (null, null, false);
                }

                if (process.HasExited)
                {
                    process.Dispose();
                    VoltrisOptimizer.Core.ForegroundWindowTracker.Instance.InvalidateStalePid();
                    return (null, null, false);
                }

                var processName = process.ProcessName;
                var executablePath = GetProcessExecutablePath(process);

                // Check if it's a known game
                bool isGame = _gameDetector.IsKnownGame(processName, executablePath);

                return (processName, processId, isGame);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ContextBuilder] Failed to detect active application: {ex.Message}");
                return (null, null, false);
            }
        }

        private string? GetProcessExecutablePath(Process process)
        {
            try
            {
                return process.MainModule?.FileName;
            }
            catch
            {
                return null;
            }
        }

        private UsageScenario DetermineScenario(bool isGameRunning, double cpuUsage)
        {
            if (isGameRunning)
                return UsageScenario.Gaming;

            if (cpuUsage < 10)
                return UsageScenario.Idle;

            if (cpuUsage > 70)
                return UsageScenario.Rendering;

            return UsageScenario.Productivity;
        }

        #region Win32 API Imports

        #endregion
    }
}
