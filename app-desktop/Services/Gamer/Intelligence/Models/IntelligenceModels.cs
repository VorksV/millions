using System;

using System.Collections.Generic;



namespace VoltrisOptimizer.Services.Gamer.Intelligence.Models

{

    #region Hardware Classification



    /// <summary>

    /// Classificação do hardware do PC

    /// </summary>

    public enum HardwareClass

    {

        Unknown = 0,

        UltraLow = 1,      // PC muito fraco (escritório básico)

        Low = 2,           // PC fraco (navegação, tarefas leves)

        Medium = 3,        // PC médio (jogos leves, produtividade)

        High = 4,          // PC forte (jogos AAA em médio/alto)

        Ultra = 5,         // PC gamer (jogos AAA em ultra)

        Enthusiast = 6     // PC entusiasta (4K, ray tracing, streaming)

    }



    /// <summary>

    /// Tipo de dispositivo

    /// </summary>

    public enum DeviceType

    {

        Unknown,

        Desktop,

        Laptop,

        Workstation,

        MiniPC,

        AllInOne

    }



    /// <summary>

    /// Perfil completo do hardware

    /// </summary>

    public class HardwareProfile

    {

        public string Id { get; set; } = Guid.NewGuid().ToString();

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime LastUpdated { get; set; } = DateTime.Now;



        // Classificação

        public HardwareClass Classification { get; set; } = HardwareClass.Unknown;

        public DeviceType DeviceType { get; set; } = DeviceType.Unknown;

        public int PerformanceScore { get; set; } // 0-100



        // CPU

        public CpuProfile Cpu { get; set; } = new();



        // GPU

        public GpuProfile Gpu { get; set; } = new();



        // RAM

        public RamProfile Ram { get; set; } = new();



        // Storage

        public StorageProfile Storage { get; set; } = new();



        // Network

        public NetworkProfile Network { get; set; } = new();



        // Thermal

        public ThermalProfile Thermal { get; set; } = new();



        // Power

        public PowerProfile Power { get; set; } = new();



        // Flags derivados

        public bool IsLaptop => DeviceType == DeviceType.Laptop;

        public bool HasDiscreteGpu => Gpu.IsDiscrete;

        public bool IsHybridGpu => Gpu.HasIntegrated && Gpu.IsDiscrete;

        public bool IsLowEnd => Classification <= HardwareClass.Low;

        public bool IsMidRange => Classification == HardwareClass.Medium;

        public bool IsHighEnd => Classification >= HardwareClass.High;

        public bool CanRunHeavyOptimizations => Classification >= HardwareClass.Medium && !Thermal.IsThermalConstrained;

    }



    public class CpuProfile

    {

        public string Name { get; set; } = "";

        public string Vendor { get; set; } = ""; // Intel, AMD

        public int Cores { get; set; }

        public int Threads { get; set; }

        public double BaseClock { get; set; } // MHz

        public double MaxClock { get; set; } // MHz

        public bool IsHybrid { get; set; } // Intel 12th gen+

        public int PerformanceCores { get; set; }

        public int EfficiencyCores { get; set; }

        public int CpuScore { get; set; } // 0-100

        public bool SupportsAVX { get; set; }

        public bool SupportsAVX512 { get; set; }

    }



    public class GpuProfile

    {

        public string Name { get; set; } = "";

        public string Vendor { get; set; } = ""; // NVIDIA, AMD, Intel

        public long VramBytes { get; set; }

        public int VramMb => (int)(VramBytes / (1024 * 1024));

        public bool IsDiscrete { get; set; }

        public bool HasIntegrated { get; set; }

        public string IntegratedName { get; set; } = "";

        public int GpuScore { get; set; } // 0-100

        public bool SupportsRayTracing { get; set; }

        public bool SupportsDLSS { get; set; }

        public bool SupportsFSR { get; set; }

        public bool SupportsHAGS { get; set; }

        public bool SupportsVRR { get; set; }

        public string DriverVersion { get; set; } = "";

    }



    public class RamProfile

    {

        public int TotalGb { get; set; }

        public int Speed { get; set; } // MHz

        public string Type { get; set; } = ""; // DDR4, DDR5

        public int Channels { get; set; } // 1, 2, 4

        public int RamScore { get; set; } // 0-100

        public bool IsSufficient => TotalGb >= 16;

        public bool IsOptimal => TotalGb >= 32 && Channels >= 2;

    }



    public class StorageProfile

    {

        public bool HasSsd { get; set; }

        public bool HasNvme { get; set; }

        public string SystemDriveType { get; set; } = ""; // HDD, SSD, NVMe

        public long SystemDriveFreeBytes { get; set; }

        public int StorageScore { get; set; } // 0-100

    }



    public class NetworkProfile

    {

        public string AdapterName { get; set; } = "";

        public bool IsWifi { get; set; }

        public bool IsEthernet { get; set; }

        public int SpeedMbps { get; set; }

        public double AvgLatencyMs { get; set; }

        public double JitterMs { get; set; }

        public int NetworkScore { get; set; } // 0-100

    }



    public class ThermalProfile

    {

        public double CpuTempCurrent { get; set; }

        public double CpuTempMax { get; set; }

        public double GpuTempCurrent { get; set; }

        public double GpuTempMax { get; set; }

        public bool IsThermalConstrained { get; set; }

        public bool HasGoodCooling => CpuTempMax < 85 && GpuTempMax < 83;

    }



    public class PowerProfile

    {

        public bool IsLaptop { get; set; }

        public bool IsOnBattery { get; set; }

        public int BatteryPercent { get; set; }

        public string CurrentPowerPlan { get; set; } = "";

        public bool SupportsUltimatePerformance { get; set; }

    }



    #endregion



    #region Frame Time Analysis



    /// <summary>

    /// Métricas de frame time

    /// </summary>

    public class FrameTimeMetrics

    {

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        public double AvgFrameTimeMs { get; set; }

        public double MinFrameTimeMs { get; set; }

        public double MaxFrameTimeMs { get; set; }

        public double P1FrameTimeMs { get; set; }  // 1% low

        public double P01FrameTimeMs { get; set; } // 0.1% low

        public double JitterMs { get; set; }

        public double StutterRatio { get; set; } // % de frames com stutter

        public int Fps => AvgFrameTimeMs > 0 ? (int)(1000.0 / AvgFrameTimeMs) : 0;

        public int FpsP1 => P1FrameTimeMs > 0 ? (int)(1000.0 / P1FrameTimeMs) : 0;

        public bool IsStable => JitterMs < 5 && StutterRatio < 0.05;

    }



    /// <summary>

    /// Evento de stutter detectado

    /// </summary>

    public class StutterEvent

    {

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        public double FrameTimeMs { get; set; }

        public double DeviationMs { get; set; }

        public StutterCause Cause { get; set; }

        public string Details { get; set; } = "";

        public Dictionary<string, double> Metrics { get; set; } = new();

    }



    public enum StutterCause

    {

        Unknown,

        CpuBound,

        GpuBound,

        VramExhausted,

        RamPaging,

        DiskIO,

        ThermalThrottling,

        DriverIssue,

        BackgroundProcess,

        NetworkSpike

    }



    #endregion



    #region Input Lag



    /// <summary>

    /// Métricas de input lag

    /// </summary>

    public class InputLagMetrics

    {

        public double TotalInputLagMs { get; set; }

        public double MousePollingRateHz { get; set; }

        public double KeyboardPollingRateHz { get; set; }

        public double RenderLatencyMs { get; set; }

        public double DisplayLatencyMs { get; set; }

        public bool IsVSyncEnabled { get; set; }

        public bool IsFullscreenOptimized { get; set; }

        public InputLagClass Classification { get; set; }

    }



    public enum InputLagClass

    {

        Excellent,  // < 10ms

        Good,       // 10-20ms

        Average,    // 20-40ms

        Poor,       // 40-60ms

        Bad         // > 60ms

    }



    #endregion



    #region Game Profiles



    /// <summary>

    /// Perfil inteligente por jogo

    /// </summary>

    public class GameIntelligenceProfile

    {

        public string GameId { get; set; } = "";

        public string GameName { get; set; } = "";

        public string ExecutableName { get; set; } = "";

        

        // Classificação do jogo

        public GameCategory Category { get; set; }

        public GameIntensity CpuIntensity { get; set; }

        public GameIntensity GpuIntensity { get; set; }

        public GameIntensity VramIntensity { get; set; }

        public GameIntensity NetworkIntensity { get; set; }



        // Prioridades

        public OptimizationPriority Priority { get; set; }



        // Configurações específicas

        public int RecommendedDscp { get; set; } = 46;

        public bool NeedsLowLatency { get; set; }

        public bool NeedsStableFrameTime { get; set; }

        public bool NeedsHighVram { get; set; }

        public bool BenefitsFromHAGS { get; set; }

        

        // Servidores conhecidos

        public List<string> KnownServerIps { get; set; } = new();

        public List<int> KnownPorts { get; set; } = new();



        // Configurações recomendadas

        public Dictionary<string, string> RecommendedSettings { get; set; } = new();

    }



    public enum GameCategory

    {

        Unknown,

        FPS,            // CS2, Valorant, Apex

        MOBA,           // LoL, Dota 2

        BattleRoyale,   // Fortnite, PUBG, Warzone

        Racing,         // Forza, F1

        Sports,         // FIFA, NBA

        RPG,            // Witcher, Elden Ring

        MMO,            // WoW, FF14

        Strategy,       // Civ, AoE

        Sandbox,        // Minecraft, Terraria

        Simulation,     // Flight Sim, Farming

        Indie           // Jogos leves

    }



    public enum GameIntensity

    {

        VeryLow,

        Low,

        Medium,

        High,

        VeryHigh,

        Extreme

    }



    public enum OptimizationPriority

    {

        InputLag,       // FPS competitivo

        FrameStability, // RPG, imersão

        MaxFps,         // Qualquer jogo

        Battery,        // Laptop gaming casual

        Balanced        // Padrão

    }



    #endregion



    #region Network Intelligence



    /// <summary>

    /// Inteligência de rede por jogo

    /// </summary>

    public class NetworkIntelligence

    {

        public string GameId { get; set; } = "";

        public string DetectedServerIp { get; set; } = "";

        public string DetectedServerRegion { get; set; } = "";

        public int DetectedPort { get; set; }

        public double BaseLatencyMs { get; set; }

        public double OptimizedLatencyMs { get; set; }

        public bool QosApplied { get; set; }

        public int AppliedDscp { get; set; }

        public List<NetworkOptimization> AppliedOptimizations { get; set; } = new();

    }



    public class NetworkOptimization

    {

        public string Name { get; set; } = "";

        public string Description { get; set; } = "";

        public double LatencyImpactMs { get; set; }

        public bool Applied { get; set; }

    }



    #endregion



    #region VRAM Management



    /// <summary>

    /// Status de VRAM

    /// </summary>

    public class VramStatus

    {

        public long TotalBytes { get; set; }

        public long UsedBytes { get; set; }

        public long AvailableBytes { get; set; }

        public double UsagePercent => TotalBytes > 0 ? (UsedBytes * 100.0 / TotalBytes) : 0;

        public bool IsCritical => UsagePercent > 90;

        public bool IsWarning => UsagePercent > 75;

        public List<VramConsumer> TopConsumers { get; set; } = new();

    }



    public class VramConsumer

    {

        public string ProcessName { get; set; } = "";

        public int ProcessId { get; set; }

        public long VramBytes { get; set; }

        public bool IsGame { get; set; }

        public bool CanBeOptimized { get; set; }

    }



    #endregion



    #region Benchmark



    /// <summary>

    /// Resultado de benchmark

    /// </summary>

    public class BenchmarkResult

    {

        public DateTime Timestamp { get; set; } = DateTime.Now;

        public int OverallScore { get; set; } // 0-100

        public int CpuScore { get; set; }

        public int GpuScore { get; set; }

        public int RamScore { get; set; }

        public int StorageScore { get; set; }

        public int NetworkScore { get; set; }

        

        public HardwareClass RecommendedClass { get; set; }

        public List<BenchmarkRecommendation> Recommendations { get; set; } = new();

        

        // Comparação

        public int PercentileBetter { get; set; } // Melhor que X% dos PCs

    }



    public class BenchmarkRecommendation

    {

        public string Category { get; set; } = "";

        public string Recommendation { get; set; } = "";

        public string Impact { get; set; } = "";

        public RecommendationPriority Priority { get; set; }

    }



    public enum RecommendationPriority

    {

        Low,

        Medium,

        High,

        Critical

    }



    #endregion



    #region Optimization Strategy



    /// <summary>

    /// Estratégia de otimização baseada no perfil

    /// </summary>

    public class OptimizationStrategy

    {

        public HardwareClass TargetClass { get; set; }

        public string GameId { get; set; } = "";

        

        // Níveis de agressividade

        public int CpuOptimizationLevel { get; set; } // 0-3

        public int GpuOptimizationLevel { get; set; }

        public int NetworkOptimizationLevel { get; set; }

        public int MemoryOptimizationLevel { get; set; }

        

        // Flags

        public bool EnableTdrTweaks { get; set; }

        public bool EnableTimerResolution { get; set; }

        public bool EnableHags { get; set; }

        public bool EnableQoS { get; set; }

        public bool EnableRamMonitor { get; set; }

        public bool EnableVramMonitor { get; set; }

        public bool EnableThermalMonitor { get; set; }

        public bool EnableAdaptiveGovernor { get; set; }

        

        // Limites de segurança

        public int MaxCpuTempC { get; set; } = 90;

        public int MaxGpuTempC { get; set; } = 85;

        public int MinBatteryPercent { get; set; } = 20;

    }



    #endregion

}



