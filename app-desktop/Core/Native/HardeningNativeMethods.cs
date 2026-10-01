using System;
using System.Runtime.InteropServices;

namespace VoltrisOptimizer.Core.Native
{
    /// <summary>
    /// Fornece definições nativas para Hardening de Produção (WCT e NtQuery).
    /// </summary>
    internal static class HardeningNativeMethods
    {
        #region Wait Chain Traversal (WCT)

        public enum WCT_OBJECT_TYPE
        {
            WctCriticalSectionType = 1,
            WctSendMessageType,
            WctMutexType,
            WctAlpcType,
            WctComType,
            WctThreadWaitType,
            WctProcessWaitType,
            WctThreadType,
            WctComActivationType,
            WctUnknownType,
            WctMaxType
        }

        public enum WCT_OBJECT_STATUS
        {
            WctStatusNoWait = 1,
            WctStatusQueued,
            WctStatusOwned,
            WctStatusNotOwned,
            WctStatusAbandoned,
            WctStatusUnknown,
            WctStatusError
        }

        // A struct real do Windows é uma union de 172 bytes.
        // O layout correto precisa de Explicit para mapear os campos da union.
        [StructLayout(LayoutKind.Explicit, Size = 172)]
        public struct WAITC_CHAIN_NODE_INFO
        {
            [FieldOffset(0)] public WCT_OBJECT_TYPE ObjectType;
            [FieldOffset(4)] public WCT_OBJECT_STATUS ObjectStatus;

            // Union: ThreadObject (quando ObjectType == WctThreadType)
            [FieldOffset(8)]  public uint ThreadId;
            [FieldOffset(12)] public uint ProcessId;
            [FieldOffset(16)] public uint ContextSwitches;

            // Union: LockObject (quando ObjectType != WctThreadType)
            // ObjectName começa em offset 8 e ocupa até 128 WCHARs (256 bytes)
            // mas o tamanho total da struct é fixo em 172 bytes.

            // Alias para compatibilidade com código existente
            public uint LockOwner => ProcessId;
        }

        [DllImport("advapi32.dll")]
        public static extern IntPtr OpenThreadWaitChainSession(uint Flags, IntPtr callback);

        [DllImport("advapi32.dll")]
        public static extern void CloseThreadWaitChainSession(IntPtr WctHandle);

        [DllImport("advapi32.dll")]
        public static extern bool GetThreadWaitChain(
            IntPtr WctHandle,
            IntPtr Context,
            uint Flags,
            uint ThreadId,
            ref uint NodeCount,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 4)] WAITC_CHAIN_NODE_INFO[] NodeInfoArray,
            out bool IsCycle);

        public const uint WCTP_GETINFO_ALL_FLAGS = 1;

        #endregion

        #region NT Query System Information (Low Overhead Process Scanning)

        [DllImport("ntdll.dll")]
        public static extern int NtQuerySystemInformation(
            int SystemInformationClass,
            IntPtr SystemInformation,
            int SystemInformationLength,
            out int ReturnLength);

        public const int SystemProcessInformation = 5;

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
            public UIntPtr UniqueProcessKey;
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

        #endregion
        
        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();
    }
}
