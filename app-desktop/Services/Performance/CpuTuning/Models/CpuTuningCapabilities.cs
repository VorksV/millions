using System.Collections.Generic;

namespace VoltrisOptimizer.Services.Performance.CpuTuning.Models
{
    /// <summary>
    /// Detected CPU tuning hardware capabilities and limits
    /// </summary>
    public class CpuTuningCapabilities
    {
        public CpuVendor Vendor { get; set; } = CpuVendor.Unknown;
        public int CpuGeneration { get; set; }
        public string CpuModel { get; set; } = string.Empty;
        public int CoreCount { get; set; }
        public int ThreadCount { get; set; }
        public PlatformType Platform { get; set; } = PlatformType.Unknown;
        public MachineClass Classification { get; set; } = MachineClass.MidRange;
        
        // Thermal limits
        public double TjMax { get; set; } = 100.0;
        public double TjSafeMargin { get; set; } = 10.0;
        public double TjSafe => TjMax - TjSafeMargin;
        
        // Feature support
        public bool SupportsSpeedShift { get; set; }
        public bool SupportsTurboBoost { get; set; }
        public bool SupportsCStates { get; set; }
        public bool SupportsClockModulation { get; set; }
        public bool SupportsProchotOffset { get; set; }
        public bool SupportsTdpControl { get; set; }
        public bool SupportsPStateControl { get; set; }

        // Aliases de Compatibilidade para o Gamer Mode (Código Legado)
        public string ProcessorName => CpuModel;
        public int PhysicalCores => CoreCount;
        public int LogicalProcessors => ThreadCount;
        public bool SupportsCoreParking => SupportsCStates; // Geralmente vinculado
        public bool SupportsHeteroPolicy => Vendor == CpuVendor.Intel && CpuGeneration >= 12; // 12ª Gen+ tem arquitetura híbrida
        
        
        // Locked limits (cannot be modified)
        public HashSet<string> LockedSettings { get; set; } = new();
        
        public bool IsPL1Locked => LockedSettings.Contains("PL1");
        public bool IsPL2Locked => LockedSettings.Contains("PL2");
        public bool IsTauLocked => LockedSettings.Contains("Tau");
        public bool IsEppLocked => LockedSettings.Contains("EPP");
        public bool IsProchotLocked => LockedSettings.Contains("PROCHOT");
        
        // Base power limits
        public int BaseTdp { get; set; } = 65;
        public int MaxTdp { get; set; } = 125;

        // Optional per-CPU gamer overrides (espelham presets testados como no ThrottleStop)
        public int? GamerPl1Override { get; set; }
        public int? GamerPl2Override { get; set; }
        public double? GamerTauOverride { get; set; }
    }
}
