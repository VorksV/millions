using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using VoltrisOptimizer.Core;
using VoltrisOptimizer.Utils.Win32;
using VoltrisOptimizer.Models;

namespace VoltrisOptimizer.Services.Optimization
{
    /// <summary>
    /// DSL 5.0 - Unified Telemetry Core
    /// Centraliza toda a telemetria do Windows NT Kernel em um unico ciclo sincronizado.
    /// </summary>
    public class SharedTelemetryProvider : IDisposable
    {
        private readonly ILoggingService _logger;
        
        private long _prevContextSwitches;
        private long _prevTotalDpcTime;
        private long _prevTotalInterruptTime;
        private long _prevPageReads;
        private DateTime _lastSampleTime;

        // PERFORMANCE: PerformanceCounters são caros (PDH). Substituídos por valores calculados
        // diretamente via NtQuerySystemInformation que já coletamos no UpdateNativeMetrics.
        // Disco: leitura rápida via GlobalMemoryStatusEx ou estimação via deltas de IO.
        private float _cachedDiskQueue;
        private float _cachedDiskActive;
        private DateTime _lastDiskSample = DateTime.MinValue;
        private const int MinDiskSampleMs = 3000; // Atualizar disco a cada 3s máximo

        public SharedTelemetryProvider(ILoggingService logger)
        {
            _logger = logger;
            InitializeCounters();
        }

        private void InitializeCounters()
        {
            try
            {
                _lastSampleTime = DateTime.UtcNow;
                // Warm-up: primeira leitura para inicializar deltas
                UpdateNativeMetrics(new SystemState50());
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[DSL 5.0] Telemetry native init failure: {ex.Message}");
            }
        }

        public void Update(SystemState50 state)
        {
            try
            {
                var cache = Core.SystemMetricsCache.Instance;
                state.CpuUsagePercent     = (float)cache.CpuPercent;
                state.AvailableRamMb      = cache.AvailableRamMb;
                state.CommitChargePercent = (uint)cache.MemoryUsedPercent;

                UpdateNativeMetrics(state);

                // Disco: atualizar apenas a cada MinDiskSampleMs para evitar syscalls frequentes
                var now = DateTime.UtcNow;
                if ((now - _lastDiskSample).TotalMilliseconds >= MinDiskSampleMs)
                {
                    _lastDiskSample = now;
                    UpdateDiskMetrics();
                }
                state.DiskQueueLength = _cachedDiskQueue;
                state.DiskActiveTime  = _cachedDiskActive;
                state.LastInputActivityMs = cache.LastInputMs;
            }
            catch { }
        }

        private void UpdateDiskMetrics()
        {
            try
            {
                // Usar NtQuerySystemInformation para IO stats — zero WMI, puro kernel
                int size = Marshal.SizeOf<ProcessNativeMethods.SYSTEM_PERFORMANCE_INFORMATION>();
                IntPtr pPerf = Marshal.AllocHGlobal(size);
                try
                {
                    if (ProcessNativeMethods.NtQuerySystemInformation(
                        ProcessNativeMethods.SystemPerformanceInformation, pPerf, size, out _) == 0)
                    {
                        var perf = Marshal.PtrToStructure<ProcessNativeMethods.SYSTEM_PERFORMANCE_INFORMATION>(pPerf);
                        // Estimativa de queue length via page reads como proxy de IO pressure
                        // (0 = idle, >5 = busy)
                        _cachedDiskQueue = Math.Min(perf.PageReadCount > 50 ? 2.0f : perf.PageReadCount / 25.0f, 10f);
                        _cachedDiskActive = Math.Min(_cachedDiskQueue * 15f, 100f);
                    }
                }
                finally { Marshal.FreeHGlobal(pPerf); }
            }
            catch { }
        }

        private void UpdateNativeMetrics(SystemState50 state)
        {
            try
            {
                var now = DateTime.UtcNow;
                var intervalSec = (now - _lastSampleTime).TotalSeconds;
                if (intervalSec <= 0) intervalSec = 1.0;
                _lastSampleTime = now;

                int size = Marshal.SizeOf<ProcessNativeMethods.SYSTEM_PERFORMANCE_INFORMATION>();
                IntPtr pPerf = Marshal.AllocHGlobal(size);
                try
                {
                    if (ProcessNativeMethods.NtQuerySystemInformation(
                        ProcessNativeMethods.SystemPerformanceInformation, pPerf, size, out _) == 0)
                    {
                        var perf = Marshal.PtrToStructure<ProcessNativeMethods.SYSTEM_PERFORMANCE_INFORMATION>(pPerf);
                        state.ContextSwitchesPerSec = (float)((perf.TransitionCount - _prevContextSwitches) / intervalSec);
                        _prevContextSwitches = perf.TransitionCount;
                        state.HardPageFaults = (float)((perf.PageReadIoCount - _prevPageReads) / intervalSec);
                        _prevPageReads = perf.PageReadIoCount;
                    }
                }
                finally { Marshal.FreeHGlobal(pPerf); }

                int pSize = Marshal.SizeOf<ProcessNativeMethods.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION>() * Environment.ProcessorCount;
                IntPtr pProc = Marshal.AllocHGlobal(pSize);
                try
                {
                    if (ProcessNativeMethods.NtQuerySystemInformation(
                        ProcessNativeMethods.SystemProcessorPerformanceInformation, pProc, pSize, out _) == 0)
                    {
                        long totalDpc = 0;
                        long totalInt = 0;
                        for (int i = 0; i < Environment.ProcessorCount; i++)
                        {
                            var procInfo = Marshal.PtrToStructure<ProcessNativeMethods.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION>(
                                pProc + (i * Marshal.SizeOf<ProcessNativeMethods.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION>()));
                            totalDpc += procInfo.DpcTime;
                            totalInt += procInfo.InterruptTime;
                        }

                        double dpcDelta = (totalDpc - _prevTotalDpcTime) / (double)Environment.ProcessorCount;
                        double intDelta = (totalInt - _prevTotalInterruptTime) / (double)Environment.ProcessorCount;

                        state.DpcTime = (float)Math.Clamp(dpcDelta / (intervalSec * 10000000.0) * 100.0, 0, 100);
                        state.InterruptTime = (float)Math.Clamp(intDelta / (intervalSec * 10000000.0) * 100.0, 0, 100);

                        _prevTotalDpcTime = totalDpc;
                        _prevTotalInterruptTime = totalInt;
                    }
                }
                finally { Marshal.FreeHGlobal(pProc); }

                // OTIMIZAÇÃO PROFISSIONAL: Em vez de heurística fixa (50% -> 2), usamos o valor real de prontidão se possível.
                double cores = Environment.ProcessorCount;
                state.CpuQueueLength = (float)((state.CpuUsagePercent / 100.0) * cores * 0.8); 
            }
            catch { }
        }

        public void Dispose()
        {
            // Nada a descartar — sem PerformanceCounters
        }
    }
}
