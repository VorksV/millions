using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VoltrisOptimizer.Services.Gamer.OptimizationModules
{
    public static class NativeProcessOptimizer
    {
        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtSetInformationProcess(IntPtr processHandle, int processInformationClass, ref uint processInformation, int processInformationLength);

        private const int ProcessIoPriority = 33;
        private const int ProcessPagePriority = 39;
        private const int ProcessPowerThrottling = 77; // EcoQoS

        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESS_POWER_THROTTLING_STATE
        {
            public uint Version;
            public uint ControlMask;
            public uint StateMask;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessInformation(IntPtr hProcess, int processInformationClass, ref PROCESS_POWER_THROTTLING_STATE processInformation, uint processInformationSize);

        // IO Priorities: 0 (Very Low), 1 (Low), 2 (Normal), 3 (High - requires privs), 4 (Critical)
        public static void SetIoPriority(IntPtr handle, uint ioPriority)
        {
            try {
                NtSetInformationProcess(handle, ProcessIoPriority, ref ioPriority, sizeof(uint));
            } catch { }
        }

        // Page Priorities: 1 (Very Low) to 5 (Normal/High)
        public static void SetPagePriority(IntPtr handle, uint pagePriority)
        {
            try {
                NtSetInformationProcess(handle, ProcessPagePriority, ref pagePriority, sizeof(uint));
            } catch { }
        }

        public static void SetEcoMode(IntPtr handle, bool enable)
        {
            try {
                var state = new PROCESS_POWER_THROTTLING_STATE
                {
                    Version = 1,
                    ControlMask = 0x1 | 0x2, // EXECUTION_SPEED | IGNORE_TIMER_RESOLUTION
                    StateMask = enable ? (uint)(0x1 | 0x2) : 0
                };
                SetProcessInformation(handle, ProcessPowerThrottling, ref state, (uint)Marshal.SizeOf(state));
            } catch { }
        }
    }
}
