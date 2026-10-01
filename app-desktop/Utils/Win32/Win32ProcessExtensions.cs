using System;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace VoltrisOptimizer.Utils.Win32
{
    public static class Win32ProcessExtensions
    {
        [DllImport("ntdll.dll")]
        private static extern int NtSetInformationProcess(IntPtr processHandle, int processInformationClass, ref int processInformation, int processInformationLength);

        private const int ProcessIoPriority = 0x15;
        private const int IoPriorityHigh = 2; // High priority for I/O

        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESS_MEMORY_PRIORITY_INFORMATION
        {
            public uint MemoryPriority;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessInformation(IntPtr hProcess, int ProcessInformationClass, ref PROCESS_MEMORY_PRIORITY_INFORMATION ProcessInformation, uint ProcessInformationSize);

        private const int ProcessMemoryPriority = 39;

        /// <summary>
        /// Define prioridade alta de I/O para o processo.
        /// Reduz micro-stutters causados por carregamento de assets (texturas/sons).
        /// </summary>
        public static bool SetHighIoPriority(this Process process)
        {
            try
            {
                int priority = IoPriorityHigh;
                int status = NtSetInformationProcess(process.Handle, ProcessIoPriority, ref priority, sizeof(int));
                return status == 0;
            }
            catch { return false; }
        }

        /// <summary>
        /// Define prioridade alta de memória (Page Management).
        /// Ajuda a manter o working set do jogo na RAM física, evitando paging excessivo.
        /// </summary>
        public static bool SetHighMemoryPriority(this Process process)
        {
            try
            {
                var info = new PROCESS_MEMORY_PRIORITY_INFORMATION { MemoryPriority = 5 }; // 5 = High
                return SetProcessInformation(process.Handle, ProcessMemoryPriority, ref info, (uint)Marshal.SizeOf(info));
            }
            catch { return false; }
        }
    }
}
