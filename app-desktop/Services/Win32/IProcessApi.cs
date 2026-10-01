using System;
using System.Diagnostics;

namespace VoltrisOptimizer.Services.Win32
{
    public interface IProcessApi
    {
        IntPtr OpenProcess(int access, bool inheritHandle, int processId);
        bool CloseHandle(IntPtr hObject);
        bool SetPriorityClass(IntPtr hProcess, ProcessPriorityClass cls);
        bool SetProcessAffinityMask(IntPtr hProcess, IntPtr mask);
        bool GetProcessAffinityMask(IntPtr hProcess, out IntPtr processMask, out IntPtr systemMask);
        bool SetProcessInformation<T>(IntPtr hProcess, int infoClass, ref T state, int size) where T : struct;
    }
}
