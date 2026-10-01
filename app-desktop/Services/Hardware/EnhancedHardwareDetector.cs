using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Hardware
{
    /// <summary>
    /// Enhanced hardware detection with workload-specific considerations
    /// Provides detailed hardware analysis for optimization decisions
    /// </summary>
    public class EnhancedHardwareDetector
    {
        private readonly ISystemInfoService _systemInfoService;
        private readonly ILoggingService _logger;

        public EnhancedHardwareDetector(ISystemInfoService systemInfoService, ILoggingService logger)
        {
            _systemInfoService = systemInfoService ?? throw new ArgumentNullException(nameof(systemInfoService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Performs comprehensive hardware analysis
        /// </summary>
        public async Task<DetailedHardwareProfile> AnalyzeHardwareAsync()
        {
            _logger.Log(LogLevel.Info, LogCategory.System,
                "Performing comprehensive hardware analysis",
                source: "EnhancedHardwareDetector");

            var profile = new DetailedHardwareProfile();

            try
            {
                // CPU Analysis
                profile.CpuAnalysis = await AnalyzeCpuAsync();
                
                // GPU Analysis
                profile.GpuAnalysis = await AnalyzeGpuAsync();
                
                // Memory Analysis
                profile.MemoryAnalysis = await AnalyzeMemoryAsync();
                
                // Storage Analysis
                profile.StorageAnalysis = await AnalyzeStorageAsync();
                
                // System Analysis
                profile.SystemAnalysis = await AnalyzeSystemAsync();

                // Calculate overall scores
                profile.PerformanceScore = CalculatePerformanceScore(profile);
                profile.GamingScore = CalculateGamingScore(profile);
                profile.WorkloadClassification = ClassifyWorkload(profile);

                _logger.Log(LogLevel.Success, LogCategory.System,
                    $"Hardware analysis complete - Performance: {profile.PerformanceScore:F1}, Gaming: {profile.GamingScore:F1}",
                    source: "EnhancedHardwareDetector");

                return profile;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Hardware analysis failed: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// Analyzes CPU capabilities and characteristics
        /// </summary>
        private async Task<CpuAnalysis> AnalyzeCpuAsync()
        {
            var cpuInfo = await _systemInfoService.GetCpuInfoAsync();
            var analysis = new CpuAnalysis
            {
                Name = cpuInfo.Name,
                CoreCount = cpuInfo.CoreCount,
                ThreadCount = cpuInfo.ThreadCount,
                MaxClockSpeedMhz = cpuInfo.MaxClockSpeedMHz,
                Architecture = cpuInfo.Architecture,
                IsHybrid = cpuInfo.IsHybrid
            };

            // Determine CPU tier
            analysis.Tier = ClassifyCpuTier(analysis);
            
            // Calculate performance metrics
            analysis.SingleThreadScore = CalculateSingleThreadScore(analysis);
            analysis.MultiThreadScore = CalculateMultiThreadScore(analysis);
            analysis.GamingSuitability = AssessGamingSuitability(analysis);

            return analysis;
        }

        /// <summary>
        /// Analyzes GPU capabilities
        /// </summary>
        private async Task<GpuAnalysis> AnalyzeGpuAsync()
        {
            var gpuInfo = await _systemInfoService.GetGpuInfoAsync();
            var analysis = new GpuAnalysis
            {
                Name = gpuInfo.Name,
                VideoMemoryBytes = gpuInfo.VideoMemoryBytes,
                IsDiscrete = gpuInfo.IsDiscrete,
                DriverVersion = gpuInfo.DriverVersion,
                Vendor = gpuInfo.Vendor
            };

            // Determine GPU tier
            analysis.Tier = ClassifyGpuTier(analysis);
            analysis.GamingCapability = AssessGamingCapability(analysis);

            return analysis;
        }

        /// <summary>
        /// Analyzes memory configuration
        /// </summary>
        private async Task<MemoryAnalysis> AnalyzeMemoryAsync()
        {
            var ramInfo = await _systemInfoService.GetRamInfoAsync();
            var analysis = new MemoryAnalysis
            {
                TotalBytes = ramInfo.TotalBytes,
                AvailableBytes = ramInfo.AvailableBytes,
                UsagePercentage = ramInfo.UsagePercent
            };

            // Determine memory tier and suitability
            analysis.Tier = ClassifyMemoryTier(analysis);
            analysis.Suitability = AssessMemorySuitability(analysis);

            return analysis;
        }

        /// <summary>
        /// Analyzes storage devices
        /// </summary>
        private async Task<StorageAnalysis> AnalyzeStorageAsync()
        {
            var drives = await _systemInfoService.GetDrivesInfoAsync();
            var analysis = new StorageAnalysis();

            foreach (var drive in drives)
            {
                var driveAnalysis = new DriveAnalysis
                {
                    Letter = drive.Letter,
                    TotalBytes = drive.TotalBytes,
                    FreeBytes = drive.FreeBytes,
                    FileSystem = drive.FileSystem,
                    IsSsd = drive.IsSsd
                };

                driveAnalysis.Type = drive.IsSsd ? StorageType.SSD : StorageType.HDD;
                analysis.Drives.Add(driveAnalysis);
            }

            // Determine primary storage characteristics
            var primaryDrive = analysis.Drives.FirstOrDefault();
            if (primaryDrive != null)
            {
                analysis.PrimaryStorageType = primaryDrive.Type;
                analysis.HasFastStorage = primaryDrive.IsSsd;
            }

            return analysis;
        }

        /// <summary>
        /// Analyzes overall system characteristics
        /// </summary>
        private async Task<SystemAnalysis> AnalyzeSystemAsync()
        {
            var analysis = new SystemAnalysis();

            // Windows version and build
            analysis.WindowsVersion = _systemInfoService.GetWindowsVersion();
            analysis.WindowsBuild = _systemInfoService.GetWindowsBuild();
            analysis.IsWindows11 = _systemInfoService.IsWindows11();

            // Administrator status
            analysis.IsAdministrator = await _systemInfoService.IsRunningAsAdministratorAsync();

            // System uptime (simplified)
            analysis.Uptime = TimeSpan.FromHours(1); // Placeholder

            return analysis;
        }

        // Scoring and classification methods
        private double CalculatePerformanceScore(DetailedHardwareProfile profile)
        {
            // Weighted scoring system
            double cpuWeight = 0.35;
            double gpuWeight = 0.35;
            double memoryWeight = 0.20;
            double storageWeight = 0.10;

            double cpuScore = profile.CpuAnalysis.MultiThreadScore / 100.0;
            double gpuScore = profile.GpuAnalysis.GamingCapability switch
            {
                GamingCapability.HighEnd => 1.0,
                GamingCapability.MidRange => 0.7,
                GamingCapability.Budget => 0.4,
                _ => 0.2
            };
            double memoryScore = profile.MemoryAnalysis.Suitability switch
            {
                MemorySuitability.Excellent => 1.0,
                MemorySuitability.Good => 0.8,
                MemorySuitability.Adequate => 0.6,
                _ => 0.3
            };
            double storageScore = profile.StorageAnalysis.HasFastStorage ? 1.0 : 0.6;

            return (cpuScore * cpuWeight) + (gpuScore * gpuWeight) + 
                   (memoryScore * memoryWeight) + (storageScore * storageWeight);
        }

        private double CalculateGamingScore(DetailedHardwareProfile profile)
        {
            // Gaming-focused scoring
            double cpuGamingWeight = 0.30;
            double gpuGamingWeight = 0.50;
            double memoryGamingWeight = 0.15;
            double storageGamingWeight = 0.05;

            double cpuScore = profile.CpuAnalysis.GamingSuitability switch
            {
                GamingSuitability.Excellent => 1.0,
                GamingSuitability.Good => 0.8,
                GamingSuitability.Fair => 0.6,
                _ => 0.3
            };
            double gpuScore = profile.GpuAnalysis.GamingCapability switch
            {
                GamingCapability.HighEnd => 1.0,
                GamingCapability.MidRange => 0.7,
                GamingCapability.Budget => 0.4,
                _ => 0.1
            };
            double memoryScore = profile.MemoryAnalysis.TotalBytes >= 16L * 1024 * 1024 * 1024 ? 1.0 :
                               profile.MemoryAnalysis.TotalBytes >= 8L * 1024 * 1024 * 1024 ? 0.8 : 0.5;
            double storageScore = profile.StorageAnalysis.HasFastStorage ? 1.0 : 0.7;

            return (cpuScore * cpuGamingWeight) + (gpuScore * gpuGamingWeight) + 
                   (memoryScore * memoryGamingWeight) + (storageScore * storageGamingWeight);
        }

        private WorkloadClassification ClassifyWorkload(DetailedHardwareProfile profile)
        {
            var gamingScore = profile.GamingScore;
            var performanceScore = profile.PerformanceScore;

            if (gamingScore >= 0.8 && performanceScore >= 0.7)
                return WorkloadClassification.GamingHighPerformance;
            
            if (gamingScore >= 0.6)
                return WorkloadClassification.GamingFocused;
            
            if (performanceScore >= 0.8)
                return WorkloadClassification.WorkstationHighPerformance;
            
            if (performanceScore >= 0.6)
                return WorkloadClassification.Workstation;
            
            if (performanceScore >= 0.4)
                return WorkloadClassification.Mainstream;
            
            return WorkloadClassification.Budget;
        }

        // CPU Classification
        private CpuTier ClassifyCpuTier(CpuAnalysis cpu)
        {
            // Simplified tier classification based on core count and clock speed
            if (cpu.CoreCount >= 8 && cpu.MaxClockSpeedMhz >= 3500)
                return CpuTier.HighEnd;
            
            if (cpu.CoreCount >= 6 && cpu.MaxClockSpeedMhz >= 3000)
                return CpuTier.MidHighEnd;
            
            if (cpu.CoreCount >= 4 && cpu.MaxClockSpeedMhz >= 2500)
                return CpuTier.MidRange;
            
            if (cpu.CoreCount >= 2 && cpu.MaxClockSpeedMhz >= 2000)
                return CpuTier.EntryLevel;
            
            return CpuTier.Budget;
        }

        private double CalculateSingleThreadScore(CpuAnalysis cpu)
        {
            // Normalize single-thread performance (0-100 scale)
            return Math.Min((cpu.MaxClockSpeedMhz / 5000.0) * 100, 100);
        }

        private double CalculateMultiThreadScore(CpuAnalysis cpu)
        {
            // Multi-thread score considers both cores and clock speed
            double coreScore = Math.Min(cpu.ThreadCount / 16.0 * 100, 100);
            double clockScore = Math.Min((cpu.MaxClockSpeedMhz / 5000.0) * 100, 100);
            return (coreScore * 0.6) + (clockScore * 0.4);
        }

        private GamingSuitability AssessGamingSuitability(CpuAnalysis cpu)
        {
            if (cpu.SingleThreadScore >= 80 && cpu.MultiThreadScore >= 70)
                return GamingSuitability.Excellent;
            
            if (cpu.SingleThreadScore >= 60 && cpu.MultiThreadScore >= 50)
                return GamingSuitability.Good;
            
            if (cpu.SingleThreadScore >= 40 && cpu.MultiThreadScore >= 30)
                return GamingSuitability.Fair;
            
            return GamingSuitability.Poor;
        }

        // GPU Classification
        private GpuTier ClassifyGpuTier(GpuAnalysis gpu)
        {
            var vramGb = gpu.VideoMemoryBytes / (1024L * 1024 * 1024);
            
            if (vramGb >= 8 && gpu.IsDiscrete)
                return GpuTier.HighEnd;
            
            if (vramGb >= 6 && gpu.IsDiscrete)
                return GpuTier.MidHighEnd;
            
            if (vramGb >= 4 && gpu.IsDiscrete)
                return GpuTier.MidRange;
            
            if (vramGb >= 2)
                return GpuTier.EntryLevel;
            
            return GpuTier.Integrated;
        }

        private GamingCapability AssessGamingCapability(GpuAnalysis gpu)
        {
            var vramGb = gpu.VideoMemoryBytes / (1024L * 1024 * 1024);
            
            if (vramGb >= 8 && gpu.IsDiscrete)
                return GamingCapability.HighEnd;
            
            if (vramGb >= 4 && gpu.IsDiscrete)
                return GamingCapability.MidRange;
            
            if (vramGb >= 2)
                return GamingCapability.Budget;
            
            return GamingCapability.IntegratedOnly;
        }

        // Memory Classification
        private MemoryTier ClassifyMemoryTier(MemoryAnalysis memory)
        {
            var totalGb = memory.TotalBytes / (1024L * 1024 * 1024);
            
            if (totalGb >= 32)
                return MemoryTier.HighEnd;
            
            if (totalGb >= 16)
                return MemoryTier.MidHighEnd;
            
            if (totalGb >= 8)
                return MemoryTier.MidRange;
            
            if (totalGb >= 4)
                return MemoryTier.EntryLevel;
            
            return MemoryTier.Budget;
        }

        private MemorySuitability AssessMemorySuitability(MemoryAnalysis memory)
        {
            var totalGb = memory.TotalBytes / (1024L * 1024 * 1024);
            
            if (totalGb >= 16)
                return MemorySuitability.Excellent;
            
            if (totalGb >= 8)
                return MemorySuitability.Good;
            
            if (totalGb >= 4)
                return MemorySuitability.Adequate;
            
            return MemorySuitability.Limited;
        }
    }

    // Data Models
    public class DetailedHardwareProfile
    {
        public CpuAnalysis CpuAnalysis { get; set; }
        public GpuAnalysis GpuAnalysis { get; set; }
        public MemoryAnalysis MemoryAnalysis { get; set; }
        public StorageAnalysis StorageAnalysis { get; set; }
        public SystemAnalysis SystemAnalysis { get; set; }

        public double PerformanceScore { get; set; }
        public double GamingScore { get; set; }
        public WorkloadClassification WorkloadClassification { get; set; }
    }

    public class CpuAnalysis
    {
        public string Name { get; set; }
        public int CoreCount { get; set; }
        public int ThreadCount { get; set; }
        public double MaxClockSpeedMhz { get; set; }
        public string Architecture { get; set; }
        public bool IsHybrid { get; set; }
        public CpuTier Tier { get; set; }
        public double SingleThreadScore { get; set; }
        public double MultiThreadScore { get; set; }
        public GamingSuitability GamingSuitability { get; set; }
    }

    public class GpuAnalysis
    {
        public string Name { get; set; }
        public long VideoMemoryBytes { get; set; }
        public bool IsDiscrete { get; set; }
        public string DriverVersion { get; set; }
        public string Vendor { get; set; }
        public GpuTier Tier { get; set; }
        public GamingCapability GamingCapability { get; set; }
    }

    public class MemoryAnalysis
    {
        public long TotalBytes { get; set; }
        public long AvailableBytes { get; set; }
        public double UsagePercentage { get; set; }
        public MemoryTier Tier { get; set; }
        public MemorySuitability Suitability { get; set; }
    }

    public class StorageAnalysis
    {
        public List<DriveAnalysis> Drives { get; set; } = new();
        public StorageType PrimaryStorageType { get; set; }
        public bool HasFastStorage { get; set; }
    }

    public class DriveAnalysis
    {
        public string Letter { get; set; }
        public long TotalBytes { get; set; }
        public long FreeBytes { get; set; }
        public string FileSystem { get; set; }
        public bool IsSsd { get; set; }
        public StorageType Type { get; set; }
    }

    public class SystemAnalysis
    {
        public string WindowsVersion { get; set; }
        public int WindowsBuild { get; set; }
        public bool IsWindows11 { get; set; }
        public bool IsAdministrator { get; set; }
        public TimeSpan Uptime { get; set; }
    }

    // Enums
    public enum CpuTier
    {
        Budget,
        EntryLevel,
        MidRange,
        MidHighEnd,
        HighEnd
    }

    public enum GpuTier
    {
        Integrated,
        EntryLevel,
        MidRange,
        MidHighEnd,
        HighEnd
    }

    public enum MemoryTier
    {
        Budget,
        EntryLevel,
        MidRange,
        MidHighEnd,
        HighEnd
    }

    public enum StorageType
    {
        HDD,
        SSD,
        NVMe
    }

    public enum GamingSuitability
    {
        Poor,
        Fair,
        Good,
        Excellent
    }

    public enum GamingCapability
    {
        IntegratedOnly,
        Budget,
        MidRange,
        HighEnd
    }

    public enum MemorySuitability
    {
        Limited,
        Adequate,
        Good,
        Excellent
    }

    public enum WorkloadClassification
    {
        Budget,
        Mainstream,
        Workstation,
        WorkstationHighPerformance,
        GamingFocused,
        GamingHighPerformance
    }
}
