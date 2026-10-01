using System;
using VoltrisOptimizer.Models;
using VoltrisOptimizer.Utils.Win32;
using System.Runtime.InteropServices;

namespace VoltrisOptimizer.Services.Optimization.Engines
{
    public class CpuGovernorEngine : IDisposable
    {
        private readonly ILoggingService _logger;
        private DateTime _lastGlobalAction = DateTime.MinValue;
        
        public CpuGovernorEngine(ILoggingService logger)
        {
            _logger = logger;
        }

        public void Governance(ProcessCacheService.CachedProcessInfo p, SystemState50 state)
        {
            try
            {
                // 1. FOREGROUND BOOST
                if (p.Id == state.ForegroundPid)
                {
                    uint target = state.LastInputActivityMs < 300 ? ProcessNativeMethods.HIGH_PRIORITY_CLASS : ProcessNativeMethods.ABOVE_NORMAL_PRIORITY_CLASS;
                    ApplyBoost(p.Id, target);
                    return;
                }

                // 2. THROTTLING ADAPTATIVO
                if (state.CpuUsagePercent > 70 || state.CpuQueueLength > 2.0f)
                {
                    if ((DateTime.Now - _lastGlobalAction).TotalMilliseconds < 50) return;
                    ApplyThrottling(p.Id, p.BasePriority, state.CpuQueueLength > 5.0f);
                }
            }
            catch { }
        }

        private void ApplyBoost(int pid, uint target)
        {
            try
            {
                IntPtr hProc = ProcessNativeMethods.OpenProcess(
                    ProcessNativeMethods.PROCESS_SET_INFORMATION | ProcessNativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, 
                    false, pid);
                
                if (hProc != IntPtr.Zero)
                {
                    try
                    {
                        uint current = ProcessNativeMethods.GetPriorityClass(hProc);
                        if (current != target)
                        {
                            ProcessNativeMethods.SetPriorityClass(hProc, target);
                            SetPowerThrottlingNative(hProc, false);
                            _lastGlobalAction = DateTime.Now;
                        }
                    }
                    finally { ProcessNativeMethods.CloseHandle(hProc); }
                }
            }
            catch { }
        }

        private void ApplyThrottling(int pid, int basePriority, bool strong)
        {
            try
            {
                // Prioridade Normal (8) ou AboveNormal (10) -> Throttling para BelowNormal
                if (basePriority >= 8)
                {
                    IntPtr hProc = ProcessNativeMethods.OpenProcess(ProcessNativeMethods.PROCESS_SET_INFORMATION, false, pid);
                    if (hProc != IntPtr.Zero)
                    {
                        try
                        {
                            ProcessNativeMethods.SetPriorityClass(hProc, ProcessNativeMethods.BELOW_NORMAL_PRIORITY_CLASS);
                            SetPowerThrottlingNative(hProc, true);
                            _lastGlobalAction = DateTime.Now;
                        }
                        finally { ProcessNativeMethods.CloseHandle(hProc); }
                    }
                }
            }
            catch { }
        }

        private void SetPowerThrottlingNative(IntPtr hProcess, bool enable)
        {
            var pState = new ProcessNativeMethods.PROCESS_POWER_THROTTLING_STATE
            {
                Version = 1, 
                ControlMask = 0x01, 
                StateMask = enable ? 0x01u : 0x00u
            };
            ProcessNativeMethods.SetProcessInformation(hProcess, 4, ref pState, Marshal.SizeOf(pState));
        }

        public void Dispose() { }
    }
}
