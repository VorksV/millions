using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VoltrisOptimizer.Services.Win32
{
    public class WindowsProcessApi : IProcessApi
    {
        private readonly ILoggingService _logger;

        public WindowsProcessApi(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr Kernel32_OpenProcess(int access, bool inheritHandle, int processId);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool Kernel32_CloseHandle(IntPtr hObject);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool Kernel32_SetPriorityClass(IntPtr hProcess, ProcessPriorityClass dwPriorityClass);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool Kernel32_SetProcessAffinityMask(IntPtr hProcess, IntPtr dwProcessAffinityMask);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool Kernel32_GetProcessAffinityMask(IntPtr hProcess, out IntPtr lpProcessAffinityMask, out IntPtr lpSystemAffinityMask);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool Kernel32_SetProcessInformation(IntPtr hProcess, int infoClass, ref byte state, int size);

        public IntPtr OpenProcess(int access, bool inheritHandle, int processId)
        {
            var hProcess = Kernel32_OpenProcess(access, inheritHandle, processId);
            if (hProcess == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                _logger.LogWarning($"[Win32Api] OpenProcess falhou para PID {processId}. Win32 Error: {error}");
            }
            return hProcess;
        }

        public bool CloseHandle(IntPtr hObject)
        {
            if (hObject == IntPtr.Zero) return false;
            var success = Kernel32_CloseHandle(hObject);
            if (!success)
            {
                var error = Marshal.GetLastWin32Error();
                _logger.LogTrace($"[Win32Api] CloseHandle falhou. Win32 Error: {error}");
            }
            return success;
        }

        public bool SetPriorityClass(IntPtr hProcess, ProcessPriorityClass cls)
        {
            var success = Kernel32_SetPriorityClass(hProcess, cls);
            if (!success)
            {
                var error = Marshal.GetLastWin32Error();
                _logger.LogWarning($"[Win32Api] SetPriorityClass ({cls}) falhou. Win32 Error: {error}");
            }
            else
            {
                _logger.LogDebug($"[Win32Api] Prioridade alterada com sucesso para {cls}");
            }
            return success;
        }

        public bool SetProcessAffinityMask(IntPtr hProcess, IntPtr mask)
        {
            var success = Kernel32_SetProcessAffinityMask(hProcess, mask);
            if (!success)
            {
                var error = Marshal.GetLastWin32Error();
                _logger.LogWarning($"[Win32Api] SetProcessAffinityMask ({mask}) falhou. Win32 Error: {error}");
            }
            else
            {
                _logger.LogDebug($"[Win32Api] Afinidade CPU alterada com sucesso.");
            }
            return success;
        }

        public bool GetProcessAffinityMask(IntPtr hProcess, out IntPtr processMask, out IntPtr systemMask)
        {
            var success = Kernel32_GetProcessAffinityMask(hProcess, out processMask, out systemMask);
            if (!success)
            {
                var error = Marshal.GetLastWin32Error();
                _logger.LogTrace($"[Win32Api] GetProcessAffinityMask falhou. Win32 Error: {error}");
            }
            return success;
        }

        public bool SetProcessInformation<T>(IntPtr hProcess, int infoClass, ref T state, int size) where T : struct
        {
            var bytes = StructureToByteArray(state);
            var success = Kernel32_SetProcessInformation(hProcess, infoClass, ref bytes[0], size);
            if (!success)
            {
                var error = Marshal.GetLastWin32Error();
                _logger.LogWarning($"[Win32Api] SetProcessInformation (Class={infoClass}) falhou. Win32 Error: {error}");
            }
            return success;
        }

        private static byte[] StructureToByteArray<T>(T obj) where T : struct
        {
            int length = Marshal.SizeOf<T>();
            byte[] arr = new byte[length];
            IntPtr ptr = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.StructureToPtr(obj, ptr, false);
                Marshal.Copy(ptr, arr, 0, length);
                return arr;
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
    }
}
