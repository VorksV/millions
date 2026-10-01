using VoltrisOptimizer.Utils;
using System;
using System.Runtime.InteropServices;

namespace VoltrisOptimizer.Services.Optimization.Providers
{
    /// <summary>
    /// PERFORMANCE FIX: Substituição completa de PerformanceCounters por NtQuerySystemInformation.
    /// 
    /// ANTES: Criava 1 SafePerformanceCounter por núcleo físico de CPU (PDH) → N objetos pesados.
    /// AGORA: 1 única syscall por chamada → dados de todos os núcleos em microsegundos.
    /// </summary>
    public class WindowsCpuCoreLoadProvider : ICpuCoreLoadProvider, IDisposable
    {
        private long[] _prevIdleTimes  = Array.Empty<long>();
        private long[] _prevKernelTimes = Array.Empty<long>();
        private long[] _prevUserTimes  = Array.Empty<long>();
        private DateTime _prevSampleTime = DateTime.MinValue;
        private double[] _lastLoads = Array.Empty<double>();
        private bool _disposed;
        private readonly object _lock = new();

        public WindowsCpuCoreLoadProvider() { }

        public double[] GetCoreLoads()
        {
            if (_disposed) return Array.Empty<double>();
            lock (_lock) return ReadCoreLoadsNative();
        }

        private double[] ReadCoreLoadsNative()
        {
            try
            {
                int cpuCount = Environment.ProcessorCount;
                int structSize = Marshal.SizeOf<SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION>();
                IntPtr buf = Marshal.AllocHGlobal(structSize * cpuCount);
                try
                {
                    if (NtQuerySystemInformation(8, buf, structSize * cpuCount, out _) != 0)
                        return _lastLoads.Length > 0 ? _lastLoads : new double[cpuCount];

                    var now = DateTime.UtcNow;
                    if (_prevIdleTimes.Length != cpuCount)
                    {
                        // Primeira leitura — inicializa baseline
                        _prevIdleTimes   = new long[cpuCount];
                        _prevKernelTimes = new long[cpuCount];
                        _prevUserTimes   = new long[cpuCount];
                        _lastLoads       = new double[cpuCount];
                        _prevSampleTime  = now;
                        for (int i = 0; i < cpuCount; i++)
                        {
                            var info = Marshal.PtrToStructure<SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION>(buf + i * structSize);
                            _prevIdleTimes[i]   = info.IdleTime;
                            _prevKernelTimes[i] = info.KernelTime;
                            _prevUserTimes[i]   = info.UserTime;
                        }
                        return _lastLoads;
                    }

                    var loads = new double[cpuCount];
                    for (int i = 0; i < cpuCount; i++)
                    {
                        var info = Marshal.PtrToStructure<SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION>(buf + i * structSize);

                        long idleDelta   = info.IdleTime   - _prevIdleTimes[i];
                        long kernelDelta = info.KernelTime - _prevKernelTimes[i];
                        long userDelta   = info.UserTime   - _prevUserTimes[i];

                        _prevIdleTimes[i]   = info.IdleTime;
                        _prevKernelTimes[i] = info.KernelTime;
                        _prevUserTimes[i]   = info.UserTime;

                        long totalDelta = kernelDelta + userDelta;
                        if (totalDelta <= 0) { loads[i] = _lastLoads.Length > i ? _lastLoads[i] : 0; continue; }

                        double busyFraction = Math.Max(0, totalDelta - idleDelta) / (double)totalDelta;
                        loads[i] = Math.Clamp(busyFraction * 100.0, 0, 100);
                    }

                    _lastLoads = loads;
                    _prevSampleTime = now;
                    return loads;
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            catch { return _lastLoads.Length > 0 ? _lastLoads : new double[Environment.ProcessorCount]; }
        }

        public void Dispose() { _disposed = true; }

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION
        {
            public long IdleTime;
            public long KernelTime;
            public long UserTime;
            public long DpcTime;
            public long InterruptTime;
            public uint InterruptCount;
        }

        [DllImport("ntdll.dll")]
        private static extern int NtQuerySystemInformation(int cls, IntPtr buf, int len, out int ret);
    }
}

