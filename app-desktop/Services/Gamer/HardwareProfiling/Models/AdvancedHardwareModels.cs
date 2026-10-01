using System;

namespace VoltrisOptimizer.Services.Gamer.HardwareProfiling.Models
{
    public enum MachineClass
    {
        Unknown = 0,
        BasicNotebook = 1,
        MidNotebook = 2,
        GamerNotebook = 3,
        BasicDesktop = 4,
        MidDesktop = 5,
        AdvancedDesktop = 6,
        GamerDesktop = 7
    }

    public enum GpuVendor
    {
        Unknown,
        NVIDIA,
        AMD,
        IntelArc,
        IntelIntegrated
    }

    public enum GpuTier
    {
        Entry,
        MidRange,
        HighEnd,
        Enthusiast
    }

    public enum StorageTier
    {
        HDD,
        SataSSD,
        NVMeGen3,
        NVMeGen4,
        NVMeGen5
    }

    public class CpuTopologyInfo
    {
        public string Name { get; set; } = string.Empty;
        public int LogicalCores { get; set; }
        public int PhysicalCores { get; set; }
        public int PCores { get; set; }
        public int ECores { get; set; }
        public bool HasHyperThreading { get; set; }
        public bool IsThreadDirectorSupported { get; set; } // Intel 12th+ Gen
        public int CCDCount { get; set; } // AMD
        public bool HasVCache { get; set; } // AMD X3D
        public bool IsNuma { get; set; }
        public uint MaxFrequencyMHz { get; set; }
        public bool IsAmd => Name.Contains("AMD", StringComparison.OrdinalIgnoreCase);
        public bool IsIntel => Name.Contains("Intel", StringComparison.OrdinalIgnoreCase);
    }

    public class GpuArchitectureInfo
    {
        public string Name { get; set; } = string.Empty;
        public bool IsIntegrated { get; set; }
        public GpuVendor Vendor { get; set; }
        public GpuTier Tier { get; set; }
        public ulong VramTotalMB { get; set; }
        public bool SupportsHags { get; set; }
        public bool SupportsResizableBar { get; set; }
    }

    public class StorageProfile
    {
        public string DriveLetter { get; set; } = "C:";
        public StorageTier Tier { get; set; }
        public bool IsSystemDrive { get; set; }
    }

    public class MemoryProfile
    {
        public ulong TotalCapacityMB { get; set; }
        public uint SpeedMHz { get; set; }
        public bool IsDualChannel { get; set; }
    }

    public class AdvancedHardwareProfile
    {
        public MachineClass MachineClass { get; set; }
        public CpuTopologyInfo Cpu { get; set; } = new();
        public GpuArchitectureInfo Gpu { get; set; } = new();
        public StorageProfile PrimaryStorage { get; set; } = new();
        public MemoryProfile Memory { get; set; } = new();
        public bool IsWindows11 { get; set; }
        
        public bool IsHighEndGamer => MachineClass == MachineClass.GamerDesktop || MachineClass == MachineClass.AdvancedDesktop || MachineClass == MachineClass.GamerNotebook;
        public bool IsWeak => MachineClass == MachineClass.BasicDesktop || MachineClass == MachineClass.BasicNotebook;
    }
}
