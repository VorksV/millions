using System;
using System.Runtime.InteropServices;

namespace VoltrisOptimizer.Utils.Win32
{
    /// <summary>
    /// APIs nativas de alta performance para o DSL 5.0 (Dynamic Load Stabilizer).
    /// Substitui PerformanceCounters e Process.GetProcesses() por chamadas diretas ao NT Kernel.
    /// </summary>
    public static class ProcessNativeMethods
    {
        #region NTDLL — Process & System Information

        [DllImport("ntdll.dll")]
        public static extern int NtQuerySystemInformation(
            int systemInformationClass,
            IntPtr systemInformation,
            int systemInformationLength,
            out int returnLength);

        // Classes do NtQuerySystemInformation
        public const int SystemBasicInformation = 0;
        public const int SystemProcessorPerformanceInformation = 8;
        public const int SystemProcessInformation = 5;
        public const int SystemPerformanceInformation = 2;
        public const int SystemInterruptInformation = 23;
        public const int SystemProcessorIdleCycleTimeInformation = 83;

        [StructLayout(LayoutKind.Sequential)]
        public struct UNICODE_STRING
        {
            public ushort Length;
            public ushort MaximumLength;
            public IntPtr Buffer;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEM_PROCESS_INFORMATION
        {
            public uint NextEntryOffset;
            public uint NumberOfThreads;
            public long WorkingSetPrivateSize;
            public uint HardFaultCount;
            public uint NumberOfThreadsHighWatermark;
            public ulong CycleTime;
            public long CreateTime;
            public long UserTime;
            public long KernelTime;
            public UNICODE_STRING ImageName;
            public int BasePriority;
            public IntPtr UniqueProcessId;
            public IntPtr InheritedFromUniqueProcessId;
            public uint HandleCount;
            public uint SessionId;
            public IntPtr UniqueProcessKey;
            public UIntPtr PeakVirtualSize;
            public UIntPtr VirtualSize;
            public uint PageFaultCount;
            public UIntPtr PeakWorkingSetSize;
            public UIntPtr WorkingSetSize;
            public UIntPtr QuotaPeakPagedPoolUsage;
            public UIntPtr QuotaPagedPoolUsage;
            public UIntPtr QuotaPeakNonPagedPoolUsage;
            public UIntPtr QuotaNonPagedPoolUsage;
            public UIntPtr PagefileUsage;
            public UIntPtr PeakPagefileUsage;
            public UIntPtr PrivatePageCount;
            public long ReadOperationCount;
            public long WriteOperationCount;
            public long OtherOperationCount;
            public long ReadTransferCount;
            public long WriteTransferCount;
            public long OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION
        {
            public long IdleTime;
            public long KernelTime;
            public long UserTime;
            public long DpcTime;
            public long InterruptTime;
            public uint InterruptCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEM_PERFORMANCE_INFORMATION
        {
            public long IdleProcessTime;
            public long IoReadTransferCount;
            public long IoWriteTransferCount;
            public long IoOtherTransferCount;
            public uint IoReadOperationCount;
            public uint IoWriteOperationCount;
            public uint IoOtherOperationCount;
            public uint AvailablePages;
            public uint CommittedPages;
            public uint CommitLimit;
            public uint PeakCommitment;
            public uint PageFaultCount;
            public uint CopyOnWriteCount;
            public uint TransitionCount;
            public uint CacheTransitionCount;
            public uint DemandZeroCount;
            public uint PageReadCount;
            public uint PageReadIoCount;
            public uint CacheReadCount;
            public uint CacheReadIoCount;
            public uint PageWriteCount;
            public uint PageWriteIoCount;
            public uint CacheWriteCount;
            public uint CacheWriteIoCount;
            public uint DirtyPagesWriteCount;
            public uint DirtyPagesWriteIoCount;
            public uint EnqueuedFreePageCount;
            public uint EnqueuedModifiedPageCount;
            public uint FreePageCount;
            public uint ModifiedPageCount;
            public uint DirtyPageCount;
            public uint LpPageCount;
            public uint WriteCopyFreePageCount;
            public uint ReadCookieCount;
            public uint ReadCookieIoCount;
            public uint SListFaultCount;
            public uint SListFaultIoCount;
            public uint SmListFaultCount;
            public uint SmListFaultIoCount;
            public uint PageSListFaultCount;
            public uint PageSListFaultIoCount;
            public uint SystemCallCount;
            public uint SmallAddressPageCount;
            public uint CopyReadCount;
            public uint FastReadCount;
            public uint FastReadResourceMissCount;
            public uint FastReadNotPossibleCount;
            public uint CopyWriteCount;
            public uint FastWriteCount;
            public uint FastWriteResourceMissCount;
            public uint FastWriteNotPossibleCount;
            public uint MdlReadCount;
            public uint MdlReadResourceMissCount;
            public uint MdlReadNotPossibleCount;
            public uint MdlWriteCount;
            public uint MdlWriteResourceMissCount;
            public uint MdlWriteNotPossibleCount;
            public uint MapViewCount;
            public uint UnmapViewCount;
        }

        #endregion

        #region KERNEL32 — Priority & Handles

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint GetPriorityClass(IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetPriorityClass(IntPtr hProcess, uint dwPriorityClass);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetProcessInformation(IntPtr hProcess, int infoClass, ref MEMORY_PRIORITY_INFORMATION info, int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetProcessInformation(IntPtr hProcess, int infoClass, ref PROCESS_POWER_THROTTLING_STATE info, int size);

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORY_PRIORITY_INFORMATION { public uint MemoryPriority; }

        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESS_POWER_THROTTLING_STATE { public uint Version; public uint ControlMask; public uint StateMask; }

        #region I/O Priority (Windows 8+)
        // ProcessInformationClass for I/O Priority
        public const int ProcessIoPriority = 33;
        
        [StructLayout(LayoutKind.Sequential)]
        public struct IO_PRIORITY_HINT
        {
            public uint Priority; // 0=VeryLow, 1=Low, 2=Normal, 3=High, 4=Critical
        }

        // IO Priority constants
        public const uint IoPriorityVeryLow = 0;
        public const uint IoPriorityLow = 1;
        public const uint IoPriorityNormal = 2;
        public const uint IoPriorityHigh = 3;
        public const uint IoPriorityCritical = 4;
        #endregion

        // Constantes de prioridade
        public const uint IDLE_PRIORITY_CLASS = 0x00000040;
        public const uint BELOW_NORMAL_PRIORITY_CLASS = 0x00004000;
        public const uint NORMAL_PRIORITY_CLASS = 0x00000020;
        public const uint ABOVE_NORMAL_PRIORITY_CLASS = 0x00008000;
        public const uint HIGH_PRIORITY_CLASS = 0x00000080;
        public const uint REALTIME_PRIORITY_CLASS = 0x00000100;

        // Permissões
        public const uint PROCESS_SET_INFORMATION = 0x0200;
        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        #endregion
        
        #region NTDLL — CPU Topology & Load

        [DllImport("ntdll.dll")]
        public static extern int NtQueryInformationProcess(
            IntPtr processHandle,
            int processInformationClass,
            ref PROCESS_BASIC_INFORMATION processInformation,
            int processInformationLength,
            out int returnLength);

        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESS_BASIC_INFORMATION
        {
            public IntPtr Reserved1;
            public IntPtr PebBaseAddress;
            public IntPtr Reserved2_0;
            public IntPtr Reserved2_1;
            public IntPtr UniqueProcessId;
            public IntPtr InheritedFromUniqueProcessId;
        }

        #endregion
    }
}
