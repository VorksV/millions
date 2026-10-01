using VoltrisOptimizer.Utils;
using System;
using System.Runtime.InteropServices;

namespace VoltrisOptimizer.Services.Gamer.Intelligence
{
    public static class NativeMetrics
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
            public MEMORYSTATUSEX()
            {
                this.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        public static double GetAvailableRamGb()
        {
            var statEX = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(statEX))
            {
                return statEX.ullAvailPhys / (1024.0 * 1024.0 * 1024.0);
            }
            return 0;
        }

        private static VoltrisOptimizer.Utils.SafePerformanceCounter? _cpuCounter;
        private static DateTime _lastCpuCheck = DateTime.MinValue;
        private static float _lastCpuValue = 0f;
        private static readonly object _cpuLock = new object();

        public static float GetCpuUsage()
        {
            lock (_cpuLock)
            {
                if (_cpuCounter == null)
                {
                    try {
                        _cpuCounter = new VoltrisOptimizer.Utils.SafePerformanceCounter("Processor", "% Processor Time", "_Total");
                        _cpuCounter.NextValue();
                    } catch { System.Diagnostics.Debug.WriteLine("[NativeMetrics] Erro ao criar CPU counter"); return 0f; }
                }
                if ((DateTime.UtcNow - _lastCpuCheck).TotalMilliseconds > 1000)
                {
                    try {
                        _lastCpuValue = _cpuCounter.NextValue();
                        _lastCpuCheck = DateTime.UtcNow;
                    } catch { System.Diagnostics.Debug.WriteLine("[NativeMetrics] Erro ao ler CPU counter"); }
                }
                return _lastCpuValue;
            }
        }
    }
}


