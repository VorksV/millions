using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace VoltrisOptimizer.Helpers
{
    public sealed class NativeSystemMetrics
    {
        private readonly object _cpuLock = new();
        private ulong _prevIdleTime;
        private ulong _prevKernelTime;
        private ulong _prevUserTime;
        private bool _initialized;
        private double _lastCpuValue;

        public double GetCpuUsage()
        {
            lock (_cpuLock)
            {
                if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
                    return _lastCpuValue;

                var idle = FileTimeToUInt64(idleTime);
                var kernel = FileTimeToUInt64(kernelTime);
                var user = FileTimeToUInt64(userTime);

                if (!_initialized)
                {
                    _prevIdleTime = idle;
                    _prevKernelTime = kernel;
                    _prevUserTime = user;
                    _initialized = true;
                    return 0;
                }

                var idleDiff = idle - _prevIdleTime;
                var kernelDiff = kernel - _prevKernelTime;
                var userDiff = user - _prevUserTime;

                _prevIdleTime = idle;
                _prevKernelTime = kernel;
                _prevUserTime = user;

                var total = kernelDiff + userDiff;
                if (total == 0) return _lastCpuValue;

                var usage = (total - idleDiff) * 100.0 / total;
                _lastCpuValue = Math.Clamp(Math.Round(usage, 1), 0, 100);
                return _lastCpuValue;
            }
        }

        public MemoryInfo GetMemoryUsage()
        {
            var memStatus = new MEMORYSTATUSEX();
            if (!GlobalMemoryStatusEx(ref memStatus))
                return new MemoryInfo();

            double totalMb = memStatus.ullTotalPhys / 1024.0 / 1024.0;
            double totalGb = totalMb / 1024.0;

            double inUseGb = totalGb;
            double standbyGb = 0;

            if (GetPerformanceInfo(out var perfInfo, Marshal.SizeOf<PERFORMANCE_INFORMATION>()))
            {
                long pageSize = perfInfo.PageSize.ToInt64();
                long physicalTotal = perfInfo.PhysicalTotal.ToInt64() * pageSize;
                long physicalAvail = perfInfo.PhysicalAvailable.ToInt64() * pageSize;

                double perfTotalGb = physicalTotal / 1024.0 / 1024.0 / 1024.0;
                double perfAvailGb = physicalAvail / 1024.0 / 1024.0 / 1024.0;
                inUseGb = perfTotalGb - perfAvailGb;

                double freeGb = memStatus.ullAvailPhys / 1024.0 / 1024.0 / 1024.0;
                standbyGb = perfAvailGb - freeGb;
                if (standbyGb < 0) standbyGb = 0;
            }

            return new MemoryInfo
            {
                UsagePercent = memStatus.dwMemoryLoad,
                UsedGb = inUseGb,
                InUseGb = inUseGb,
                StandbyGb = standbyGb,
                TotalGb = totalGb
            };
        }

        private static ulong FileTimeToUInt64(FILETIME ft)
            => ((ulong)ft.dwHighDateTime << 32) | ft.dwLowDateTime;

        #region P/Invoke

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(
            out FILETIME lpIdleTime,
            out FILETIME lpKernelTime,
            out FILETIME lpUserTime);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool GetPerformanceInfo(
            out PERFORMANCE_INFORMATION pPerformanceInformation,
            int cb);

        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX
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
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PERFORMANCE_INFORMATION
        {
            public int cb;
            public IntPtr CommitTotal;
            public IntPtr CommitLimit;
            public IntPtr CommitPeak;
            public IntPtr PhysicalTotal;
            public IntPtr PhysicalAvailable;
            public IntPtr SystemCache;
            public IntPtr KernelTotal;
            public IntPtr KernelPaged;
            public IntPtr KernelNonpaged;
            public IntPtr PageSize;
            public int HandleCount;
            public int ProcessCount;
            public int ThreadCount;
        }

        #endregion
    }

    public struct MemoryInfo
    {
        public uint UsagePercent;
        public double UsedGb;
        public double InUseGb;
        public double StandbyGb;
        public double TotalGb;
    }
}
