using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace VoltrisOptimizer.Core.VMRG.Models
{
    public enum VmHypervisor
    {
        Unknown,
        VirtualBox,
        VMwareWorkstation,
        VMwarePlayer,
        VMwareFusion,
        HyperV,
        WindowsHypervisorPlatform,
        Qemu,
        Wsl2,
        BlueStacks,
        LDPlayer,
        NoxPlayer,
        MEmu,
        MuMuPlayer,
        Genymotion,
        AndroidEmulator,
        ParallelsDesktop
    }

    public enum VmWindowState
    {
        Unknown,
        Foreground,
        Background,
        Minimized,
        Closed
    }

    public enum VmPriorityClass
    {
        Idle,
        BelowNormal,
        Normal,
        AboveNormal,
        High,
        RealTime
    }

    public class VmInfo
    {
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public string WindowTitle { get; set; } = string.Empty;
        public VmHypervisor Hypervisor { get; set; } = VmHypervisor.Unknown;
        public string HypervisorDisplayName => Hypervisor switch
        {
            VmHypervisor.VirtualBox => "Oracle VirtualBox",
            VmHypervisor.VMwareWorkstation => "VMware Workstation",
            VmHypervisor.VMwarePlayer => "VMware Player",
            VmHypervisor.VMwareFusion => "VMware Fusion",
            VmHypervisor.HyperV => "Microsoft Hyper-V",
            VmHypervisor.WindowsHypervisorPlatform => "Windows Hypervisor Platform",
            VmHypervisor.Qemu => "QEMU",
            VmHypervisor.Wsl2 => "WSL2",
            VmHypervisor.BlueStacks => "BlueStacks",
            VmHypervisor.LDPlayer => "LDPlayer",
            VmHypervisor.NoxPlayer => "NoxPlayer",
            VmHypervisor.MEmu => "MEmu",
            VmHypervisor.MuMuPlayer => "MuMu Player",
            VmHypervisor.Genymotion => "Genymotion",
            VmHypervisor.AndroidEmulator => "Android Studio Emulator",
            VmHypervisor.ParallelsDesktop => "Parallels Desktop",
            _ => "Desconhecido"
        };
        public bool IsHostProcess { get; set; }
        public VmWindowState WindowState { get; set; } = VmWindowState.Unknown;
        public double CpuUsagePercent { get; set; }
        public long WorkingSetMb { get; set; }
        public VmPriorityClass CurrentPriority { get; set; } = VmPriorityClass.Normal;
        public VmPriorityClass OriginalPriority { get; set; } = VmPriorityClass.Normal;
        public long AffinityMask { get; set; }
        public long OriginalAffinityMask { get; set; }
        public DateTime FirstDetectedAt { get; set; }
        public DateTime LastActiveAt { get; set; }
        public bool IsActive => WindowState == VmWindowState.Foreground;
        public bool ChangesApplied => CurrentPriority != OriginalPriority || AffinityMask != OriginalAffinityMask;

        public ProcessPriorityClass ToProcessPriorityClass() => CurrentPriority switch
        {
            VmPriorityClass.Idle => ProcessPriorityClass.Idle,
            VmPriorityClass.BelowNormal => ProcessPriorityClass.BelowNormal,
            VmPriorityClass.Normal => ProcessPriorityClass.Normal,
            VmPriorityClass.AboveNormal => ProcessPriorityClass.AboveNormal,
            VmPriorityClass.High => ProcessPriorityClass.High,
            VmPriorityClass.RealTime => ProcessPriorityClass.RealTime,
            _ => ProcessPriorityClass.Normal
        };

        public static VmPriorityClass FromProcessPriorityClass(ProcessPriorityClass p) => p switch
        {
            ProcessPriorityClass.Idle => VmPriorityClass.Idle,
            ProcessPriorityClass.BelowNormal => VmPriorityClass.BelowNormal,
            ProcessPriorityClass.Normal => VmPriorityClass.Normal,
            ProcessPriorityClass.AboveNormal => VmPriorityClass.AboveNormal,
            ProcessPriorityClass.High => VmPriorityClass.High,
            ProcessPriorityClass.RealTime => VmPriorityClass.RealTime,
            _ => VmPriorityClass.Normal
        };
    }

    public class VmDetectionResult
    {
        public List<VmInfo> DetectedVms { get; set; } = new();
        public List<VmInfo> HostProcesses { get; set; } = new();
        public DateTime DetectedAt { get; set; }
        public bool AnyVmDetected => DetectedVms.Count > 0;
    }

    public class VmDecision
    {
        public enum DecisionType
        {
            NoAction,
            ReducePriority,
            ReduceIoPriority,
            ReduceMemoryPriority,
            RestrictAffinity,
            RestoreDefaults
        }

        public DecisionType Action { get; set; } = DecisionType.NoAction;
        public VmInfo TargetVm { get; set; } = null!;
        public string Reason { get; set; } = string.Empty;
        public double Confidence { get; set; }
        public bool RequiresValidation { get; set; } = true;
        public DateTime DecidedAt { get; set; }
    }

    public class VmValidationResult
    {
        public bool Success { get; set; }
        public double CpuDelta { get; set; }
        public double RamDelta { get; set; }
        public string Details { get; set; } = string.Empty;
        public bool ShouldRollback { get; set; }
    }

    public class VmLearningEntry
    {
        public VmHypervisor Hypervisor { get; set; }
        public string MachineId { get; set; } = string.Empty;
        public DayOfWeek DayOfWeek { get; set; }
        public int Hour { get; set; }
        public bool UserActive { get; set; }
        public double SystemCpuLoad { get; set; }
        public double SystemRamLoad { get; set; }
        public VmDecision.DecisionType AppliedAction { get; set; }
        public bool WasEffective { get; set; }
        public double ImprovementScore { get; set; }
        public DateTime RecordedAt { get; set; }
    }
}
