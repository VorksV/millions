using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace VoltrisOptimizer.Helpers
{
    /// <summary>
    /// Simplified startup watchdog – runs without a dedicated thread.
    /// The heavy work is delegated to the central VoltrisDiagnosticSystem watchdog loop.
    /// </summary>
    public static class StartupWatchdog
    {
        private static volatile bool _running;
        private static volatile string _phase = "init";
        private static readonly object _logLock = new object();

        // Timing helpers for the consolidated watchdog logic
        private static DateTime _start = DateTime.MinValue;
        private static DateTime _lastWork = DateTime.MinValue;
        private static int _tick;

        public static void Start()
        {
            if (_running) return;
            _running = true;
            _start = DateTime.UtcNow;
            _lastWork = DateTime.MinValue;
            _tick = 0;
        }

        public static void Stop()
        {
            _running = false;
        }

        public static void SetPhase(string phase)
        {
            _phase = phase;
            Write($"PHASE: {phase}");
        }

        // Called from VoltrisDiagnosticSystem.WatchdogLoop on each iteration.
        internal static void PerformWorkIfDue()
        {
            if (!_running) return;

            var now = DateTime.UtcNow;
            var elapsedSec = (now - _start).TotalSeconds;

            // Determine interval (default 2 s, longer after startup is complete)
            int intervalMs = 2000;
            if (elapsedSec > 60 ||
                _phase.IndexOf("DONE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                _phase.IndexOf("COMPLETE", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                intervalMs = 30000; // after startup – log less frequently
            }

            if ((now - _lastWork).TotalMilliseconds < intervalMs) return;

            _lastWork = now;
            _tick++;

            // Snapshot process state
            using (var proc = Process.GetCurrentProcess())
            {
                var threadCount = proc.Threads.Count;
                var workingSetMB = proc.WorkingSet64 / (1024 * 1024);
                var responding = proc.Responding;
                var hasMain = false;
                string? mainTitle = null;
                try
                {
                    var hWnd = proc.MainWindowHandle;
                    hasMain = hWnd != IntPtr.Zero;
                    mainTitle = proc.MainWindowTitle;
                }
                catch { }

                Write($"TICK={_tick} elapsed={elapsedSec:F1}s phase={_phase} threads={threadCount} mem={workingSetMB}MB responding={responding} mainHwnd={hasMain} title='{mainTitle}'");
            }

            // Dump threads periodically (every 3 ticks for 2 s interval, every 2 ticks for 30 s interval)
            int dumpIntervalTicks = intervalMs == 2000 ? 3 : 2;
            if (_tick % dumpIntervalTicks == 0)
            {
                DumpThreads();
            }
        }

        private static void DumpThreads()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("=========== THREAD DUMP ===========");
                using (var proc = Process.GetCurrentProcess())
                {
                    foreach (ProcessThread pt in proc.Threads)
                    {
                        try
                        {
                            sb.AppendLine($"  TID={pt.Id} state={pt.ThreadState} waitReason={(pt.ThreadState == System.Diagnostics.ThreadState.Wait ? pt.WaitReason.ToString() : "-")} cpuTime={pt.TotalProcessorTime.TotalMilliseconds:F0}ms userTime={pt.UserProcessorTime.TotalMilliseconds:F0}ms priority={pt.CurrentPriority}");
                        }
                        catch { }
                    }
                }
                sb.AppendLine("===================================");
                Write(sb.ToString());
            }
            catch { }
        }

        private static void Write(string msg)
        {
            try
            {
                var logDir = LogDirectoryResolver.Resolve();
                Directory.CreateDirectory(logDir);
                var path = Path.Combine(logDir, $"Watchdog_{DateTime.Now:yyyy-MM-dd}.log");
                lock (_logLock)
                {
                    File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\r\n", System.Text.Encoding.UTF8);
                }
            }
            catch { }
        }
    }
}
