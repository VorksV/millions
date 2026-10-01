using System;
using VoltrisOptimizer.Models;
using VoltrisOptimizer.Services.Gamer.Implementation;
using VoltrisOptimizer.Utils.Win32;
using System.Runtime.InteropServices;

namespace VoltrisOptimizer.Services.Optimization.Engines
{
    /// <summary>
    /// DSL 5.0 - IO Scheduler Awareness
    /// Gerência de prioridade de I/O com suporte nativo e cache.
    /// </summary>
    public class IoSchedulerEngine : IDisposable
    {
        private readonly ILoggingService _logger;

        public IoSchedulerEngine(ILoggingService logger)
        {
            _logger = logger;
        }

        public void Governance(ProcessCacheService.CachedProcessInfo p, SystemState50 state)
        {
            try
            {
                // 1. FOREGROUND — sempre I/O High
                if (p.Id == state.ForegroundPid)
                {
                    SetIoPriorityNative(p.Id, GamerNativeMethods.IoPriorityLevel.IoPriorityHigh, false);
                    return;
                }

                // 2. CONTENÇÃO DE DISCO
                if (state.DiskActiveTime > 80f || state.DiskQueueLength > 1.2f)
                {
                    if (p.WorkingSet64 > 50 * 1024 * 1024)
                    {
                        SetIoPriorityNative(p.Id, GamerNativeMethods.IoPriorityLevel.IoPriorityVeryLow, true);
                        return;
                    }
                    SetIoPriorityNative(p.Id, GamerNativeMethods.IoPriorityLevel.IoPriorityLow, false);
                    return;
                }

                // 3. PRESSÃO MODERADA
                if (state.IoPressure >= PressureLevel.Medium && p.WorkingSet64 > 100 * 1024 * 1024)
                {
                    SetIoPriorityNative(p.Id, GamerNativeMethods.IoPriorityLevel.IoPriorityLow, false);
                    return;
                }

                // 4. RESTAURAR
                SetIoPriorityNative(p.Id, GamerNativeMethods.IoPriorityLevel.IoPriorityNormal, false);
            }
            catch { }
        }

        private void SetIoPriorityNative(int pid, GamerNativeMethods.IoPriorityLevel priority, bool enableEcoQoS)
        {
            IntPtr hProc = ProcessNativeMethods.OpenProcess(
                ProcessNativeMethods.PROCESS_SET_INFORMATION | ProcessNativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, 
                false, pid);

            if (hProc != IntPtr.Zero)
            {
                try
                {
                    GamerNativeMethods.SetProcessIoPriority(hProc, priority, out _);
                    
                    var powerState = new ProcessNativeMethods.PROCESS_POWER_THROTTLING_STATE
                    {
                        Version = 1, 
                        ControlMask = 0x01, 
                        StateMask = enableEcoQoS ? 0x01u : 0x00u
                    };
                    ProcessNativeMethods.SetProcessInformation(hProc, 4, ref powerState, Marshal.SizeOf(powerState));
                }
                finally { ProcessNativeMethods.CloseHandle(hProc); }
            }
        }

        public void Dispose() { }
    }
}
