using System;

namespace VoltrisOptimizer.Services.Engineering
{
    // STUBS PARA COMPILAÇÃO - Tipos de Storage removidos durante refatoração

    public class StorageInfo
    {
        public string DeviceId { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public long Size { get; set; }
        public string MediaType { get; set; } = string.Empty;
        public StorageType Type { get; set; } = StorageType.SSD;
        public string DetectionMethod { get; set; } = string.Empty;
        public string DriveLetter { get; set; } = string.Empty;
        public long TotalSize { get; set; }
        public long AvailableSpace { get; set; }
        public string VolumeLabel { get; set; } = string.Empty;
        public string InterfaceType { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
        public int SectorsPerTrack { get; set; }
        public long TotalSectors { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }

    public enum StorageType
    {
        Unknown,
        HDD,
        SSD,
        NVMe
    }

    public class TrimStatus
    {
        public bool IsEnabled { get; set; }
        public string StatusMessage { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }

    public enum WriteCachePolicy
    {
        Unknown,
        WriteBack,
        WriteThrough,
        Disabled
    }

    public class StoragePerformanceStats
    {
        public double SequentialReadSpeed { get; set; }
        public double SequentialWriteSpeed { get; set; }
        public double RandomReadSpeed { get; set; }
        public double RandomWriteSpeed { get; set; }
        public double AverageLatency { get; set; }
        public DateTime Timestamp { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }
}
