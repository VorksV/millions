using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Optimization.Unification
{
    public class BurstAccelerationEngine : IDisposable
    {
        private readonly ILoggingService _logger;
        private readonly TimerResolutionManager _timerManager;
        private readonly OptimizationOrchestrator _orchestrator;
        private readonly IntelligentDecisionEngine _decisionEngine;
        private IntPtr _hook;
        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);
        private WinEventDelegate _winEventProc;

        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint WINEVENT_OUTOFCONTEXT = 0;

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private readonly ConcurrentDictionary<int, Task> _activeBursts = new();
        private readonly ConcurrentDictionary<int, DateTime> _lastBurstTime = new();
        private readonly TimeSpan _burstCooldown = TimeSpan.FromSeconds(15);

        public BurstAccelerationEngine(ILoggingService logger, TimerResolutionManager timerManager, OptimizationOrchestrator orchestrator, IntelligentDecisionEngine decisionEngine)
        {
            _logger = logger;
            _timerManager = timerManager;
            _orchestrator = orchestrator;
            _decisionEngine = decisionEngine;
            _winEventProc = new WinEventDelegate(WinEventCallback);
        }

        public void Start()
        {
            if (_hook == IntPtr.Zero)
            {
                _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _winEventProc, 0, 0, WINEVENT_OUTOFCONTEXT);
                _logger.LogInfo("[BurstEngine] WinEventHook (Foreground) anexado. Estabilização de carga ativa.");
            }
        }

        public void Stop()
        {
            if (_hook != IntPtr.Zero)
            {
                UnhookWinEvent(_hook);
                _hook = IntPtr.Zero;
                _logger.LogInfo("[BurstEngine] WinEventHook desanexado.");
            }
        }

        private void WinEventCallback(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (eventType == EVENT_SYSTEM_FOREGROUND && hwnd != IntPtr.Zero)
            {
                 uint pid;
                 GetWindowThreadProcessId(hwnd, out pid);
                 if (pid > 0 && !_activeBursts.ContainsKey((int)pid))
                 {
                      _ = Task.Run(() => SafeStartBurst((int)pid));
                 }
            }
        }

        private async Task SafeStartBurst(int pid)
        {
             try
             {
                 using var p = Process.GetProcessById(pid);
                 string pName = p.ProcessName;
                 
                 // Validação via DECISION ENGINE (Cérebro Central)
                 if (!_decisionEngine.ShouldEffectBurst(pid, pName, 0)) return;

                 if (!_activeBursts.ContainsKey(pid))
                 {
                      var task = Task.Run(() => ExecuteBurst(pid));
                      _activeBursts.TryAdd(pid, task);
                 }
             }
             catch { }
        }

        private async Task ExecuteBurst(int pid)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                
                // Transactional Priority Snapshot
                var origPri = p.PriorityClass;
                var targetPri = ProcessPriorityClass.AboveNormal;

                if (origPri >= targetPri) return; // Já está no nível alto ou acima

                _timerManager.RequestHighPrecision($"Burst_{pid}");
                
                try { p.PriorityClass = targetPri; } catch { }
                _orchestrator.EcoQoS.RemoveEcoQoS(pid);

                await Task.Delay(2000); // Janela crítica de estabilização

                // Atomic Rollback
                try 
                { 
                    if (!p.HasExited) 
                    {
                        if (p.PriorityClass == targetPri)
                            p.PriorityClass = origPri;
                    } 
                } 
                catch { }
                
                _timerManager.ReleaseHighPrecision($"Burst_{pid}");
            }
            catch { }
            finally
            {
                _activeBursts.TryRemove(pid, out _);
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
