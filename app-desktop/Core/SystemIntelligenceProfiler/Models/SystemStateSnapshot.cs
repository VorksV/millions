using System;
using System.Collections.Generic;

namespace VoltrisOptimizer.Core.SystemIntelligenceProfiler.Models
{
    /// <summary>
    /// Represents a complete snapshot of system state for a specific optimization category
    /// </summary>
    public class SystemStateSnapshot
    {
        /// <summary>
        /// Timestamp when the snapshot was captured
        /// </summary>
        public DateTime Timestamp { get; set; }
        
        /// <summary>
        /// The optimization category this snapshot represents
        /// </summary>
        public OptimizationCategory Category { get; set; }
        
        /// <summary>
        /// Current registry key states
        /// </summary>
        public Dictionary<string, RegistryValueState> RegistryStates { get; set; } = new();
        
        /// <summary>
        /// Current service states
        /// </summary>
        public Dictionary<string, ServiceState> ServiceStates { get; set; } = new();
        
        /// <summary>
        /// Current policy states
        /// </summary>
        public Dictionary<string, PolicyState> PolicyStates { get; set; } = new();
        
        /// <summary>
        /// Current file states
        /// </summary>
        public Dictionary<string, FileState> FileStates { get; set; } = new();
        
        /// <summary>
        /// Hardware profile at time of capture
        /// </summary>
        public HardwareProfile HardwareProfile { get; set; }
    }

    /// <summary>
    /// Represents the state of a registry value
    /// </summary>
    public class RegistryValueState
    {
        /// <summary>
        /// Full registry path (e.g., "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer")
        /// </summary>
        public string KeyPath { get; set; }
        
        /// <summary>
        /// Value name
        /// </summary>
        public string ValueName { get; set; }
        
        /// <summary>
        /// Current value data
        /// </summary>
        public object ValueData { get; set; }
        
        /// <summary>
        /// Registry value type
        /// </summary>
        public Microsoft.Win32.RegistryValueKind ValueType { get; set; }
        
        /// <summary>
        /// Whether the value exists
        /// </summary>
        public bool Exists { get; set; }
        
        /// <summary>
        /// Hash of the value for comparison
        /// </summary>
        public string ValueHash { get; set; }
    }

    /// <summary>
    /// Represents the state of a Windows service
    /// </summary>
    public class ServiceState
    {
        /// <summary>
        /// Service name
        /// </summary>
        public string ServiceName { get; set; }
        
        /// <summary>
        /// Display name
        /// </summary>
        public string DisplayName { get; set; }
        
        /// <summary>
        /// Current status (Running, Stopped, Paused, etc.)
        /// </summary>
        public System.ServiceProcess.ServiceControllerStatus Status { get; set; }
        
        /// <summary>
        /// Startup type (Automatic, Manual, Disabled)
        /// </summary>
        public System.ServiceProcess.ServiceStartMode StartType { get; set; }
        
        /// <summary>
        /// Whether the service exists
        /// </summary>
        public bool Exists { get; set; }
        
        /// <summary>
        /// Service executable path
        /// </summary>
        public string ExecutablePath { get; set; }
    }

    /// <summary>
    /// Represents the state of a group policy
    /// </summary>
    public class PolicyState
    {
        /// <summary>
        /// Policy name/identifier
        /// </summary>
        public string PolicyName { get; set; }
        
        /// <summary>
        /// Policy category
        /// </summary>
        public string Category { get; set; }
        
        /// <summary>
        /// Current policy value
        /// </summary>
        public object Value { get; set; }
        
        /// <summary>
        /// Whether policy is enforced
        /// </summary>
        public bool IsEnforced { get; set; }
        
        /// <summary>
        /// Policy source (Local, Domain, etc.)
        /// </summary>
        public string Source { get; set; }
    }

    /// <summary>
    /// Represents the state of a configuration file
    /// </summary>
    public class FileState
    {
        /// <summary>
        /// Full file path
        /// </summary>
        public string FilePath { get; set; }
        
        /// <summary>
        /// File exists
        /// </summary>
        public bool Exists { get; set; }
        
        /// <summary>
        /// File size in bytes
        /// </summary>
        public long Size { get; set; }
        
        /// <summary>
        /// Last modified timestamp
        /// </summary>
        public DateTime LastModified { get; set; }
        
        /// <summary>
        /// File hash for content comparison
        /// </summary>
        public string Hash { get; set; }
        
        /// <summary>
        /// File attributes
        /// </summary>
        public System.IO.FileAttributes Attributes { get; set; }
    }

    /// <summary>
    /// Hardware profile information
    /// </summary>
    public class HardwareProfile
    {
        /// <summary>
        /// CPU information
        /// </summary>
        public CpuInfo Cpu { get; set; }
        
        /// <summary>
        /// Memory information
        /// </summary>
        public RamInfo Ram { get; set; }
        
        /// <summary>
        /// Storage information
        /// </summary>
        public StorageInfo Storage { get; set; }
        
        /// <summary>
        /// Overall hardware score (1.0-10.0)
        /// </summary>
        public double HardwareScore { get; set; }
        
        /// <summary>
        /// System classification
        /// </summary>
        public HardwareClass Classification { get; set; }
    }

    /// <summary>
    /// CPU information
    /// </summary>
    public class CpuInfo
    {
        /// <summary>
        /// CPU name/model
        /// </summary>
        public string Name { get; set; }
        
        /// <summary>
        /// Number of logical cores
        /// </summary>
        public int LogicalCores { get; set; }
        
        /// <summary>
        /// Number of physical cores
        /// </summary>
        public int PhysicalCores { get; set; }
        
        /// <summary>
        /// Maximum clock speed in MHz
        /// </summary>
        public int MaxClockSpeed { get; set; }
        
        /// <summary>
        /// CPU architecture
        /// </summary>
        public string Architecture { get; set; }
    }

    /// <summary>
    /// Memory information
    /// </summary>
    public class RamInfo
    {
        /// <summary>
        /// Total memory in MB
        /// </summary>
        public int TotalMb { get; set; }
        
        /// <summary>
        /// Available memory in MB
        /// </summary>
        public int AvailableMb { get; set; }
        
        /// <summary>
        /// Memory speed in MHz
        /// </summary>
        public int SpeedMhz { get; set; }
        
        /// <summary>
        /// Memory type
        /// </summary>
        public string Type { get; set; }
    }

    /// <summary>
    /// Storage information
    /// </summary>
    public class StorageInfo
    {
        /// <summary>
        /// Total storage capacity in GB
        /// </summary>
        public int TotalGb { get; set; }
        
        /// <summary>
        /// Free space in GB
        /// </summary>
        public int FreeGb { get; set; }
        
        /// <summary>
        /// Storage type (SSD, HDD, NVMe)
        /// </summary>
        public string Type { get; set; }
        
        /// <summary>
        /// Drive letter
        /// </summary>
        public string DriveLetter { get; set; }
    }

    /// <summary>
    /// Hardware classification levels
    /// </summary>
    public enum HardwareClass
    {
        Unknown,
        LowEnd,      // Basic systems, older hardware
        MidRange,    // Standard consumer systems
        HighEnd,     // Gaming/high-performance systems
        Workstation, // Professional/workstation systems
        Server       // Server-class hardware
    }

    /// <summary>
    /// Status of optimization evaluation
    /// </summary>
    public enum OptimizationStatus
    {
        Unknown,
        AlreadyApplied,
        NeedsApplication,
        Incompatible,
        ConflictsDetected,
        RequiresElevation
    }

    /// <summary>
    /// Optimization category grouping
    /// </summary>
    public class OptimizationCategory
    {
        /// <summary>
        /// Category name
        /// </summary>
        public string Name { get; set; }
        
        /// <summary>
        /// Category description
        /// </summary>
        public string Description { get; set; }
        
        /// <summary>
        /// Registry keys to monitor
        /// </summary>
        public List<string> RegistryKeys { get; set; } = new();
        
        /// <summary>
        /// Services to monitor
        /// </summary>
        public List<string> Services { get; set; } = new();
        
        /// <summary>
        /// Policies to monitor
        /// </summary>
        public List<string> Policies { get; set; } = new();
        
        /// <summary>
        /// Configuration files to monitor
        /// </summary>
        public List<string> ConfigFiles { get; set; } = new();
    }
}
