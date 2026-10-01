using System;
using System.Collections.Generic;

namespace VoltrisOptimizer.Core.SystemIntelligenceProfiler
{
    public enum RecommendationCategory
    {
        Safe,
        Conditional,
        Risky
    }

    public enum PerformanceTier
    {
        LowEnd,
        MidRange,
        HighEnd,
        Workstation
    }

    public enum ThermalTier
    {
        Stable,
        Warm,
        Critical
    }

    public class AuditData
    {
        public VoltrisOptimizer.Core.SystemIntelligenceProfiler.Models.HardwareProfile HardwareProfile { get; set; } = new();

        public CpuInfo Cpu { get; set; } = new CpuInfo();
        public GpuInfo Gpu { get; set; } = new GpuInfo();
        public RamInfo Ram { get; set; } = new RamInfo();
        public StorageInfo Storage { get; set; } = new StorageInfo();
        public NicInfo Nic { get; set; } = new NicInfo();
        public DisplayInfo Display { get; set; } = new DisplayInfo();
        public BatteryInfo Battery { get; set; } = new BatteryInfo();
        public WindowsInfo Windows { get; set; } = new WindowsInfo();
        public DpcLatencyInfo DpcLatency { get; set; } = new DpcLatencyInfo();
        
        public string CurrentPowerPlan { get; set; } = "";
        public bool SupportsHighPerformancePlan { get; set; }
        
        // Intelligence Fields
        public PerformanceTier PerfTier { get; set; } = PerformanceTier.MidRange;
        public ThermalTier ThermalStatus { get; set; } = ThermalTier.Stable;
        public double CpuTemp { get; set; }
        public double GpuTemp { get; set; }
        public bool IsThermalThrottling { get; set; }
    }

    public class DisplayInfo
    {
        public int RefreshRateHz { get; set; }
        public string Resolution { get; set; } = "";
        public bool IsLaptopScreen { get; set; }
        public bool GSyncFreeSync { get; set; } // Inferido (difícil detectar diretamente sem API proprietária)
    }

    public class CpuInfo
    {
        public string Vendor { get; set; } = "";
        public string Model { get; set; } = "";
        public int LogicalCores { get; set; }
        public int PhysicalCores { get; set; }
        public double MaxFrequencyMhz { get; set; }
        public bool HyperThreading { get; set; }
    }

    public class GpuInfo
    {
        public string Vendor { get; set; } = "";
        public string Model { get; set; } = "";
        public long VramMb { get; set; }
        public string DriverVersion { get; set; } = "";
        public bool HagsSupported { get; set; }
        public bool IsIntegrated { get; set; }
    }

    public class RamInfo
    {
        public long TotalMb { get; set; }
        public long AvailableMb { get; set; }
        public int SpeedMhz { get; set; }
        public long SwapUsageMb { get; set; }
        public string Type { get; set; } = "Unknown";
        public bool IsMemoryPressure { get; set; }
    }

    public class StorageInfo
    {
        public string SystemDiskModel { get; set; } = "";
        public string SystemDiskType { get; set; } = ""; // HDD/SSD/NVMe
        public long FreeSpaceMb { get; set; }
        public long TotalSpaceMb { get; set; }
        public bool SmartOk { get; set; }
    }

    public class NicInfo
    {
        public string Vendor { get; set; } = "";
        public bool SupportsRss { get; set; }
    }

    public class BatteryInfo
    {
        public bool Present { get; set; }
        public int EstimatedChargePercent { get; set; }
        public string Status { get; set; } = "";
    }

    public class WindowsInfo
    {
        public string Version { get; set; } = "";
        public int Build { get; set; }
        public bool IsAdmin { get; set; }
    }

    public class DpcLatencyInfo
    {
        public double InterruptsPerSec { get; set; }
        public double DpcRate { get; set; }
    }

    public enum UserProfile
    {
        GeneralBalanced,        // Uso geral balanceado
        WorkOffice,             // Trabalho / Escritório
        GamerCompetitive,       // Gamer competitivo
        GamerSinglePlayer,      // Gamer single player
        GamerSimulation,        // Gamer Simulação
        GamerMMO,               // Gamer MMO / RPG
        GamerStrategy,          // Gamer Estratégia
        CreativeVideoEditing,   // Edição de vídeo / Design
        DeveloperProgramming,   // Desenvolvimento / Programação
        EnterpriseSecure        // Enterprise Seguro (Corporativo)
    }

    public class UserAnswers
    {
        public UserProfile Profile { get; set; } = UserProfile.GeneralBalanced;
        public string UseCase { get; set; } = string.Empty;
        public string Priority { get; set; } = string.Empty;
        public bool IsLaptop { get; set; }

        // Os três flags abaixo deixaram de ser perguntados no questionário porque
        // não produziam resultado útil:
        //   OptimizeGPU         -> em GPU integrada a recomendação nunca é criada;
        //                           em dedicada, ligava TdrLevel=0 (tela morta em
        //                           travamento de driver, sem recuperação pelo OS).
        //   OptimizeDisk        -> escrevia SystemResponsiveness=5, que é PIOR que o
        //                           padrão (0) para multimídia.
        //   AutoRestartServices -> manutenção, não ganho de performance.
        // Como a tela não os expõe mais, o default passou a false: "não pedido"
        // precisa significar "não aplicado". Sem isso, remover o checkbox faria
        // o app aplicar as três coisas sem consentimento.
        public bool OptimizeGPU { get; set; }
        public bool ResetNetwork { get; set; } = true;
        public bool OptimizeDisk { get; set; }
        public bool CleanSystem { get; set; } = true;
        public bool AutoRestartServices { get; set; }
        public bool AllowRegistryChanges { get; set; } = true;
        public string[] Problems { get; set; } = Array.Empty<string>();
        public string ApplyMode { get; set; } = "Manual";
    }

    public enum ActionType
    {
        Unknown,
        General_Optimize,
        SystemCleanup,
        PowerPlan_HighPerformance,
        PowerPlan_Balanced,
        Network_FlushDns,
        Network_OptimizeTcp,
        Network_ResetStack,
        GamerMode_Activate,
        Memory_Optimize,
        Services_Optimize,
        Advanced_DisableHags,
        Advanced_OptimizeIrq,
        Process_Optimize,
        Visual_Optimize,
        // Storage Specific
        Storage_DisableSuperfetch,
        Storage_DisableDefragBootFiles,
        Storage_EnableTrim,
        Storage_DisableLastAccess,
        Storage_DisableHibernation,
        Storage_DisableSearchIndexing,
        Storage_DisableEventLogging,
        Storage_Disable83Naming,
        Storage_Defrag,
        Storage_Trim,
        Advanced_RestartCriticalServices,
        Advanced_EnableHags
    }

    public enum OptimizationState
    {
        Recommended,
        AlreadyOptimized,
        PartiallyApplied,
        Conflicting,
        Unsupported,
        RequiresReboot,
        Failed,
        Deferred
    }

    public class ActionRecommendation
    {
        public string Name { get; set; } = "";
        public ActionType Type { get; set; } = ActionType.Unknown; // Propriedade Tipada

        public ActionRecommendation()
        {
            App.LoggingService?.LogTrace($"[PROFILER_MODEL] Recomendação de ação criada (Tipo: {Type})");
        }
        public RecommendationCategory Category { get; set; }
        public int CompatibilityScore { get; set; }
        public int ExpectedGainScore { get; set; }
        public int RiskScore { get; set; }
        public bool Supported { get; set; }
        public string Explanation { get; set; } = "";
        public string Module { get; set; } = "";
        
        public bool IsApplied { get; set; }
        public bool IsAlreadyOptimized { get; set; }
        public bool IsSelected { get; set; } = true;
        
        // State Validation
        public OptimizationState State { get; set; } = OptimizationState.Recommended;
        public string StateMessage { get; set; } = "";
        public bool IsOptimized => State == OptimizationState.AlreadyOptimized;
        public bool NeedsAction => State == OptimizationState.Recommended || State == OptimizationState.PartiallyApplied;

        // Technical Details for Compatibility Check
        public List<string>? RegistryKeys { get; set; }
        public List<string>? Services { get; set; }
    }

    public class ProfilerReport
    {
        public AuditData Audit { get; set; } = new AuditData();
        public UserAnswers Answers { get; set; } = new UserAnswers();
        public List<ActionRecommendation> Recommendations { get; set; } = new List<ActionRecommendation>();
        public List<string> HealthAlerts { get; set; } = new List<string>();
        public List<string> Backups { get; set; } = new List<string>();
        public List<string> Applied { get; set; } = new List<string>();
        public string Status { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;

        public ProfilerReport()
        {
            App.LoggingService?.LogTrace("[PROFILER_MODEL] Relatório de profiler instanciado");
        }
    }

    public class ApplyResult
    {
        public bool Success { get; set; }
        public List<string> Applied { get; set; } = new List<string>();
        public List<string> Errors { get; set; } = new List<string>();
        public List<string> Backups { get; set; } = new List<string>();
    }
}
