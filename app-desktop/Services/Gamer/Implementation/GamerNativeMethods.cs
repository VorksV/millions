using System;
using System.Runtime.InteropServices;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer.Implementation
{
    internal static class GamerNativeMethods
    {
        [DllImport("kernel32.dll", EntryPoint = "SetProcessWorkingSetSize", SetLastError = true)]
        private static extern bool SetProcessWorkingSetSizeNative(IntPtr hProcess, int dwMinimumWorkingSetSize, int dwMaximumWorkingSetSize);

        [DllImport("psapi.dll", EntryPoint = "EmptyWorkingSet", SetLastError = true)]
        private static extern bool EmptyWorkingSetNative(IntPtr hProcess);

        [DllImport("ntdll.dll", EntryPoint = "NtSetInformationProcess", SetLastError = true)]
        private static extern int NtSetInformationProcessNative(IntPtr processHandle, int processInformationClass, ref IO_PRIORITY_HINT processInformation, int processInformationLength);

        public static bool SetProcessWorkingSetSize(IntPtr hProc, int min, int max) => SetProcessWorkingSetSizeNative(hProc, min, max);
        public static bool EmptyWorkingSet(IntPtr hProc) => EmptyWorkingSetNative(hProc);

        private static long _ntSetCallCount;
        private static long _ntSetFailCount;

        private static volatile bool _nativeIoPriorityDisabled;
        private static int _consecutivePrivilegeFailures;
        private const int MaxPrivilegeFailures = 3;

        private static ILoggingService? _logger;
        public static void SetLogger(ILoggingService logger) => _logger = logger;

        [DllImport("advapi32.dll", EntryPoint = "AdjustTokenPrivileges", SetLastError = true)]
        private static extern bool AdjustTokenPrivilegesNative(IntPtr TokenHandle, bool DisableAllPrivileges, ref TOKEN_PRIVILEGES NewState, int BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

        [DllImport("kernel32.dll", EntryPoint = "GetCurrentProcess")]
        private static extern IntPtr GetCurrentProcessNative();

        [DllImport("advapi32.dll", EntryPoint = "OpenProcessToken", SetLastError = true)]
        private static extern bool OpenProcessTokenNative(IntPtr ProcessHandle, int DesiredAccess, ref IntPtr TokenHandle);

        [DllImport("advapi32.dll", EntryPoint = "LookupPrivilegeValueW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool LookupPrivilegeValueNative(string? lpSystemName, string lpName, ref LUID lpLuid);

        private static bool AdjustTokenPrivileges(IntPtr hToken, bool disable, ref TOKEN_PRIVILEGES state, int len, IntPtr prev, IntPtr retLen)
            => AdjustTokenPrivilegesNative(hToken, disable, ref state, len, prev, retLen);
        private static IntPtr GetCurrentProcess() => GetCurrentProcessNative();
        private static bool OpenProcessToken(IntPtr hProc, int access, ref IntPtr hToken) => OpenProcessTokenNative(hProc, access, ref hToken);
        private static bool LookupPrivilegeValue(string? sysName, string name, ref LUID luid) => LookupPrivilegeValueNative(sysName, name, ref luid);

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_PRIVILEGES
        {
            public int PrivilegeCount;
            public LUID Luid;
            public int Attributes;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        private static bool _privilegeChecked = false;

        public static void EnsurePrivilege()
        {
            _logger?.LogEntry(nameof(EnsurePrivilege));
            if (_privilegeChecked)
            {
                _logger?.LogExit(nameof(EnsurePrivilege));
                return;
            }
            _privilegeChecked = true;
            try
            {
                IntPtr hToken = IntPtr.Zero;
                if (OpenProcessToken(GetCurrentProcess(), 0x0020 | 0x0008, ref hToken))
                {
                    LUID luid = new LUID();
                    if (LookupPrivilegeValue(null, "SeIncreaseBasePriorityPrivilege", ref luid))
                    {
                        var tp = new TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = 0x00000002 };
                        AdjustTokenPrivileges(hToken, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
                    }
                }
            }
            catch { }
            _logger?.LogExit(nameof(EnsurePrivilege));
        }

        public static int NtSetInformationProcess(IntPtr processHandle, int processInformationClass, ref IO_PRIORITY_HINT processInformation, int processInformationLength)
        {
            _logger?.LogEntry(nameof(NtSetInformationProcess));

            if (_nativeIoPriorityDisabled && processInformationClass == ProcessIoPriority)
            {
                _logger?.LogExit(nameof(NtSetInformationProcess));
                return unchecked((int)0xC0000061);
            }

            EnsurePrivilege();

            if (processHandle == IntPtr.Zero || processHandle == new IntPtr(-1))
            {
                _logger?.LogExit(nameof(NtSetInformationProcess));
                return -1;
            }

            if (!IsProcessHandleValid(processHandle))
            {
                _logger?.LogExit(nameof(NtSetInformationProcess));
                return -1;
            }

            var callNum = System.Threading.Interlocked.Increment(ref _ntSetCallCount);

            if (callNum == 1)
            {
                Core.Diagnostics.CrashDiagnostics.Mark(
                    $"NtSetInfoProcess #{callNum} handle=0x{processHandle:X} class=0x{processInformationClass:X2} prio={processInformation.Priority}");
            }

            int maxRetries = 3;
            int retryDelay = 200;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    int status = NtSetInformationProcessNative(processHandle, processInformationClass, ref processInformation, processInformationLength);

                    if (status == 0)
                    {
                        System.Threading.Interlocked.Exchange(ref _consecutivePrivilegeFailures, 0);
                        _logger?.LogExit(nameof(NtSetInformationProcess));
                        return status;
                    }

                    if (status == unchecked((int)0xC000010A))
                    {
                        if (attempt < maxRetries)
                        {
                            System.Threading.Thread.Sleep(retryDelay * attempt);

                            if (!IsProcessHandleValid(processHandle))
                            {
                                _logger?.LogExit(nameof(NtSetInformationProcess));
                                return -1;
                            }

                            continue;
                        }
                        else
                        {
                            Core.Diagnostics.CrashDiagnostics.Mark(
                                $"NtSetInfoProcess STATUS_INVALID_PARAMETER (0xC000010A) após {maxRetries} tentativas - processo pode estar protegido ou em inicialização");
                        }
                    }

                    if (status != 0)
                    {
                        var fails = System.Threading.Interlocked.Increment(ref _ntSetFailCount);

                        if (status == unchecked((int)0xC0000061) && processInformationClass == ProcessIoPriority)
                        {
                            var consecutive = System.Threading.Interlocked.Increment(ref _consecutivePrivilegeFailures);
                            if (consecutive >= MaxPrivilegeFailures && !_nativeIoPriorityDisabled)
                            {
                                _nativeIoPriorityDisabled = true;
                                var msg = $"[NativeInterop] CIRCUIT BREAKER: NtSetInformationProcess I/O Priority desabilitado após {consecutive} falhas STATUS_PRIVILEGE_NOT_HELD. " +
                                          "Causa provável: processo alvo tem integridade alta (Protected Process Light). " +
                                          "Fallback SetPriorityClass ativo — funcionalidade preservada com menor granularidade.";
                                Core.Diagnostics.CrashDiagnostics.Mark(msg);
                                _logger?.LogWarning(msg);
                            }
                        }

                        if (fails <= 1 || fails % 500 == 0)
                        {
                            Core.Diagnostics.CrashDiagnostics.Mark(
                                $"NtSetInfoProcess NTSTATUS=0x{status:X8} (fail #{fails})");
                        }
                    }

                    _logger?.LogExit(nameof(NtSetInformationProcess));
                    return status;
                }
                catch (Exception ex)
                {
                    if (attempt == maxRetries)
                    {
                        Core.Diagnostics.CrashDiagnostics.TraceException("NtSetInformationProcess", ex);
                        _logger?.LogExit(nameof(NtSetInformationProcess));
                        return -1;
                    }

                    System.Threading.Thread.Sleep(retryDelay * attempt);
                }
            }

            _logger?.LogExit(nameof(NtSetInformationProcess));
            return -1;
        }

        private static bool IsProcessHandleValid(IntPtr processHandle)
        {
            _logger?.LogEntry(nameof(IsProcessHandleValid));
            try
            {
                uint processId = GetProcessId(processHandle);
                var result = processId != 0;
                _logger?.LogExit(nameof(IsProcessHandleValid));
                return result;
            }
            catch
            {
                _logger?.LogExit(nameof(IsProcessHandleValid));
                return false;
            }
        }

        [DllImport("kernel32.dll")]
        private static extern uint GetProcessId(IntPtr hProcess);

        [DllImport("ntdll.dll")]
        public static extern int NtSetSystemInformation(int SystemInformationClass, ref int SystemInformation, int SystemInformationLength);

        public const int ProcessIoPriority = 0x21;

        [StructLayout(LayoutKind.Sequential)]
        public struct IO_PRIORITY_HINT
        {
            public int Priority;
        }

        public enum IoPriorityLevel
        {
            IoPriorityVeryLow = 0,
            IoPriorityLow = 1,
            IoPriorityNormal = 2,
            IoPriorityHigh = 3,
            IoPriorityCritical = 4
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetPriorityClass(IntPtr hProcess, uint dwPriorityClass);

        public const uint IDLE_PRIORITY_CLASS = 0x00000040;
        public const uint BELOW_NORMAL_PRIORITY_CLASS = 0x00004000;
        public const uint NORMAL_PRIORITY_CLASS = 0x00000020;
        public const uint ABOVE_NORMAL_PRIORITY_CLASS = 0x00008000;
        public const uint HIGH_PRIORITY_CLASS = 0x00000080;
        public const uint REALTIME_PRIORITY_CLASS = 0x00000100;

        public static bool SetProcessIoPriority(IntPtr processHandle, IoPriorityLevel priority, out string method)
        {
            _logger?.LogEntry(nameof(SetProcessIoPriority));

            try
            {
                var ioPriority = new IO_PRIORITY_HINT { Priority = (int)priority };
                int ntStatus = NtSetInformationProcess(processHandle, ProcessIoPriority, ref ioPriority, Marshal.SizeOf<IO_PRIORITY_HINT>());

                if (ntStatus == 0)
                {
                    method = "NtSetInformationProcess (Native I/O Priority)";
                    _logger?.LogExit(nameof(SetProcessIoPriority));
                    return true;
                }
            }
            catch
            {
            }

            try
            {
                uint priorityClass = priority switch
                {
                    IoPriorityLevel.IoPriorityCritical => HIGH_PRIORITY_CLASS,
                    IoPriorityLevel.IoPriorityHigh => ABOVE_NORMAL_PRIORITY_CLASS,
                    IoPriorityLevel.IoPriorityNormal => NORMAL_PRIORITY_CLASS,
                    IoPriorityLevel.IoPriorityLow => BELOW_NORMAL_PRIORITY_CLASS,
                    IoPriorityLevel.IoPriorityVeryLow => IDLE_PRIORITY_CLASS,
                    _ => NORMAL_PRIORITY_CLASS
                };

                bool success = SetPriorityClass(processHandle, priorityClass);
                if (success)
                {
                    method = "SetPriorityClass (Standard Windows API)";
                    _logger?.LogExit(nameof(SetProcessIoPriority));
                    return true;
                }
            }
            catch
            {
            }

            method = "Failed";
            _logger?.LogExit(nameof(SetProcessIoPriority));
            return false;
        }
    }
}
