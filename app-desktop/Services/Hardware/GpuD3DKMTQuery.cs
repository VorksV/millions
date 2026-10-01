using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VoltrisOptimizer.Services.Hardware
{
    public sealed class GpuD3DKMTQuery : IDisposable
    {
        private delegate int D3DKMTQueryStatisticsDelegate(ref D3DKMT_QUERYSTATISTICS query);
        private readonly D3DKMTQueryStatisticsDelegate _queryStats;
        private int _nodeCount;
        private int _adapterLuidLow;
        private int _adapterLuidHigh;
        private long _prevRunningTime;
        private long _prevTimestamp;
        public bool IsAvailable { get; private set; }

        public GpuD3DKMTQuery()
        {
            try
            {
                IntPtr gdi32 = NativeLibrary.Load("gdi32.dll");
                IntPtr proc = NativeLibrary.GetExport(gdi32, "D3DKMTQueryStatistics");
                _queryStats = Marshal.GetDelegateForFunctionPointer<D3DKMTQueryStatisticsDelegate>(proc);
                EnumerateAdapters();
                IsAvailable = _nodeCount > 0;
            }
            catch
            {
                IsAvailable = false;
            }
        }

        private void EnumerateAdapters()
        {
            var q = new D3DKMT_QUERYSTATISTICS
            {
                Type = D3DKMT_QUERYSTATISTICS_TYPE.D3DKMT_QUERYSTATISTICS_ADAPTER,
                hProcess = IntPtr.Zero
            };
            if (_queryStats(ref q) == 0)
            {
                _nodeCount = (int)q.QueryResult.AdapterInformation.NodeCount;
                _adapterLuidLow = q.AdapterLuidLow;
                _adapterLuidHigh = q.AdapterLuidHigh;
            }
            else
            {
                _nodeCount = 1;
            }
        }

        public double GetGpuUsagePercent()
        {
            if (!IsAvailable) return -1;
            long now = Stopwatch.GetTimestamp();
            long totalRunningTime = 0;
            int validNodes = 0;
            for (int i = 0; i < _nodeCount; i++)
            {
                var q = new D3DKMT_QUERYSTATISTICS
                {
                    Type = D3DKMT_QUERYSTATISTICS_TYPE.D3DKMT_QUERYSTATISTICS_NODE,
                    AdapterLuidLow = _adapterLuidLow,
                    AdapterLuidHigh = _adapterLuidHigh,
                    NodeId = i,
                    hProcess = IntPtr.Zero
                };
                if (_queryStats(ref q) == 0)
                {
                    totalRunningTime += q.QueryResult.NodeInformation.GlobalInformation.RunningTime;
                    validNodes++;
                }
            }
            if (validNodes == 0) return -1;
            if (_prevTimestamp == 0)
            {
                _prevRunningTime = totalRunningTime;
                _prevTimestamp = now;
                return 0;
            }
            long dt = now - _prevTimestamp;
            long drt = totalRunningTime - _prevRunningTime;
            if (dt <= 0 || drt <= 0) return 0;
            double elapsed100ns = dt * 10_000_000.0 / Stopwatch.Frequency;
            double usage = drt / elapsed100ns * 100.0;
            _prevRunningTime = totalRunningTime;
            _prevTimestamp = now;
            return Math.Max(0.0, Math.Min(100.0, usage));
        }

        public void Dispose() { }

        // Layout nativo exato de D3DKMT_QUERYSTATISTICS (x64)
        //  0:4 Type
        //  4:4 AdapterLuid.LowPart
        //  8:4 AdapterLuid.HighPart
        // 12:4 QuerySegment union { Node.NodeId }
        // 16:8 hProcess (HANDLE)
        // 24:? QueryResult (union)
        [StructLayout(LayoutKind.Explicit, Size = 64)]
        private struct D3DKMT_QUERYSTATISTICS
        {
            [FieldOffset(0)]  public D3DKMT_QUERYSTATISTICS_TYPE Type;
            [FieldOffset(4)]  public int AdapterLuidLow;
            [FieldOffset(8)]  public int AdapterLuidHigh;
            [FieldOffset(12)] public int NodeId; // QuerySegment.Node.NodeId
            [FieldOffset(16)] public IntPtr hProcess;
            [FieldOffset(24)] public D3DKMT_QUERYSTATISTICS_RESULT QueryResult;
        }

        private enum D3DKMT_QUERYSTATISTICS_TYPE
        {
            D3DKMT_QUERYSTATISTICS_ADAPTER = 0,
            D3DKMT_QUERYSTATISTICS_PROCESS = 1,
            D3DKMT_QUERYSTATISTICS_PHYSICAL_ADAPTER = 2,
            D3DKMT_QUERYSTATISTICS_NODE = 4}

        [StructLayout(LayoutKind.Explicit)]
        private struct D3DKMT_QUERYSTATISTICS_RESULT
        {
            [FieldOffset(0)] public D3DKMT_QUERYSTATISTICS_RESULT_ADAPTER AdapterInformation;
            [FieldOffset(0)] public D3DKMT_QUERYSTATISTICS_RESULT_NODE NodeInformation;
            [FieldOffset(0)] public D3DKMT_QUERYSTATISTICS_RESULT_PHYSICAL_ADAPTER PhysicalAdapterInformation;
        }

        // Adapter result: { ULONG NodeCount; ULONG Reserved; ... }
        [StructLayout(LayoutKind.Sequential)]
        private struct D3DKMT_QUERYSTATISTICS_RESULT_ADAPTER
        {
            public int NodeCount;
        }

        // Node → GlobalInformation → RunningTime
        [StructLayout(LayoutKind.Sequential)]
        private struct D3DKMT_QUERYSTATISTICS_RESULT_NODE
        {
            public D3DKMT_QUERYSTATISTICS_GLOBAL_INFORMATION GlobalInformation;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3DKMT_QUERYSTATISTICS_GLOBAL_INFORMATION
        {
            public long RunningTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3DKMT_QUERYSTATISTICS_RESULT_PHYSICAL_ADAPTER
        {
            public int Count;
        }
    }
}
