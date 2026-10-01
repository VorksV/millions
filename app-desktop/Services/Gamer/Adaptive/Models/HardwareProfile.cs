using System;

namespace VoltrisOptimizer.Services.Gamer.Adaptive.Models
{
    /// <summary>
    /// Perfil completo de hardware detectado
    /// </summary>
    public class HardwareProfile
    {
        public string SessionId { get; set; } = string.Empty;
        public DateTime DetectedAt { get; set; } = DateTime.UtcNow;

        // CPU
        public CpuProfile Cpu { get; set; } = new();
        
        // GPU
        public GpuProfile Gpu { get; set; } = new();
        
        // RAM
        public RamProfile Ram { get; set; } = new();
        
        // Storage
        public StorageProfile Storage { get; set; } = new();
        
        // System
        public SystemProfile System { get; set; } = new();
        
        // Overall tier classification
        public HardwareTier OverallTier { get; set; } = HardwareTier.MID;
    }

    public class CpuProfile
    {
        public string Name { get; set; } = "Unknown";
        public string Vendor { get; set; } = "Unknown"; // Intel, AMD
        public int PhysicalCores { get; set; }
        public int LogicalProcessors { get; set; }
        public double MaxClockMhz { get; set; }
        public bool IsHybrid { get; set; } // Intel 12th gen+
        public CpuTier Tier { get; set; } = CpuTier.MID;
        public int Generation { get; set; } // Para AMD Ryzen: 3000, 5000, 7000, etc.
    }

    public class GpuProfile
    {
        public string Name { get; set; } = "Unknown";
        public string Vendor { get; set; } = "Unknown"; // NVIDIA, AMD, Intel
        public long VideoMemoryBytes { get; set; }
        public bool IsDiscrete { get; set; }
        public GpuTier Tier { get; set; } = GpuTier.INTEGRATED;
        public int Generation { get; set; } // Para NVIDIA: 10 (Pascal), 20 (Turing), 30 (Ampere), 40 (Ada)
    }

    public class RamProfile
    {
        public double TotalGb { get; set; }
        public double UsedGb { get; set; }
        public double AvailableGb => TotalGb - UsedGb;
        public double UsagePercent => TotalGb > 0 ? (UsedGb / TotalGb) * 100 : 0;
    }

    public class StorageProfile
    {
        public StorageType Type { get; set; } = StorageType.Unknown;
        public string GameDrive { get; set; } = string.Empty; // C:, D:, etc.
        public long AvailableSpaceBytes { get; set; }
    }

    public class SystemProfile
    {
        public bool IsLaptop { get; set; }
        public bool IsOnBattery { get; set; }
        public string WindowsVersion { get; set; } = "Unknown"; // Windows 10, Windows 11
        public int WindowsBuild { get; set; }
        public SystemProfileType Type { get; set; } = SystemProfileType.DESKTOP_MIDRANGE;
    }

    // Enums
    public enum HardwareTier
    {
        ENTRY,
        MID,
        HIGH,
        HIGHEND
    }

    public enum CpuTier
    {
        ENTRY,      // <=4 cores
        MID,        // 6 cores
        HIGH,       // 8 cores
        HIGHEND     // 10+ cores or hybrid
    }

    public enum GpuTier
    {
        INTEGRATED, // Intel UHD, AMD Vega integrated
        ENTRY,      // <2GB VRAM
        MID,        // 2-4GB VRAM
        HIGH,       // 6-8GB VRAM
        HIGHEND     // 10GB+ VRAM
    }

    public enum StorageType
    {
        Unknown,
        HDD,
        SSD,
        NVMe
    }

    public enum SystemProfileType
    {
        LAPTOP_BATTERY,
        LAPTOP_PLUGGED,
        DESKTOP_LOWEND,
        DESKTOP_MIDRANGE,
        DESKTOP_HIGHEND,
        WORKSTATION
    }
}
