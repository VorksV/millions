using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Linq;

using VoltrisOptimizer.Models;
using VoltrisOptimizer.Utils.Win32;

namespace VoltrisOptimizer.Services.Optimization.Engines
{
    public class StabilityGuardEngine
    {
        private readonly ILoggingService _logger;
        private DateTime _lastEmergencyAction = DateTime.MinValue;
        private DateTime _lastStarvationCheck = DateTime.MinValue;
        private DateTime _lastSaturationAction = DateTime.MinValue;
        private int _cycleCount;
        private int _saturationCount;
        private const int WarmupCycles = 20; // Aumentado para evitar falsos positivos durante o spike de startup
        private const int SaturationCooldownSeconds = 45; // Aumentado cooldown para 45s

        private static readonly HashSet<string> ProtectedProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "winlogon", "csrss", "smss", "lsass", "services", "svchost",
            "wininit", "dwm", "explorer", "System", "WUDFHost", "WmiPrvSE", "audiodg"
        };

        public StabilityGuardEngine(ILoggingService logger)
        {
            _logger = logger;
        }

        public void Governance(ProcessCacheService.CachedProcessInfo p, SystemState50 state)
        {
            if (_cycleCount < WarmupCycles) return;

            // CRITICAL: Se a fila já está saturada (>30), chamar syscalls em loop
            // só vai PIORAR a latência do scheduler (context switches inúteis).
            if (state.CpuQueueLength > 30.0f)
            {
                if ((DateTime.Now - _lastEmergencyAction).TotalSeconds < 2) return;
                
                EmergencyRestore(p);
            }
        }

        private void EmergencyRestore(ProcessCacheService.CachedProcessInfo p)
        {
            try
            {
                if (ProtectedProcesses.Contains(p.ProcessName)) return;

                // PERFORMANCE: Só restaura se não for Normal (evita syscall inútil)
                if (p.BasePriority < 8) // 8 é Normal no NT
                {
                    IntPtr hProc = ProcessNativeMethods.OpenProcess(ProcessNativeMethods.PROCESS_SET_INFORMATION, false, p.Id);
                    if (hProc != IntPtr.Zero)
                    {
                        try
                        {
                            ProcessNativeMethods.SetPriorityClass(hProc, ProcessNativeMethods.NORMAL_PRIORITY_CLASS);
                            _logger.LogInfo($"[StabilityGuard] Emergency Restore: {p.ProcessName} (PID {p.Id})");
                            _lastEmergencyAction = DateTime.Now;
                        }
                        finally { ProcessNativeMethods.CloseHandle(hProc); }
                    }
                }
            }
            catch { }
        }

        public void CheckGlobalHealth(SystemState50 state)
        {
            _cycleCount++;
            if (_cycleCount <= WarmupCycles) return;

            // Detecção de Saturação Crítica
            if (state.CpuUsagePercent > 98 && state.CpuQueueLength > 40)
            {
                _saturationCount++;
                var now = DateTime.Now;
                if ((now - _lastSaturationAction).TotalSeconds >= SaturationCooldownSeconds)
                {
                    _lastSaturationAction = now;
                    _logger.LogCritical($"[StabilityGuard] CRITICAL SATURATION — CPU: {state.CpuUsagePercent:F0}%, Queue: {state.CpuQueueLength:F1}. Self-throttling active.");

                    // Reduzir prioridade do próprio programa para não roubar CPU de drivers críticos
                    try
                    {
                        var self = Process.GetCurrentProcess();
                        if (self.PriorityClass != ProcessPriorityClass.BelowNormal)
                        {
                            self.PriorityClass = ProcessPriorityClass.BelowNormal;
                        }
                    }
                    catch { }
                }
            }
            else if (_saturationCount > 0 && state.CpuUsagePercent < 70)
            {
                _saturationCount = 0;
                try
                {
                    IntPtr hSelf = ProcessNativeMethods.OpenProcess(ProcessNativeMethods.PROCESS_SET_INFORMATION, false, Process.GetCurrentProcess().Id);
                    if (hSelf != IntPtr.Zero)
                    {
                        try { ProcessNativeMethods.SetPriorityClass(hSelf, ProcessNativeMethods.NORMAL_PRIORITY_CLASS); }
                        finally { ProcessNativeMethods.CloseHandle(hSelf); }
                        _logger.LogInfo("[StabilityGuard] Sistema estabilizado. Prioridade restaurada.");
                    }
                }
                catch { }
            }
        }
    }
}
