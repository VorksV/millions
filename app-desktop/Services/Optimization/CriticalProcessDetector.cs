using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace VoltrisOptimizer.Services.Optimization
{
    public class CriticalProcessDetector
    {
        private readonly VoltrisOptimizer.Services.Optimization.Providers.IProcessProvider? _processProvider;
        private readonly HashSet<string> _browserSet;
        public CriticalProcessDetector(VoltrisOptimizer.Services.Optimization.Providers.IProcessProvider? processProvider = null, IEnumerable<string>? browserNames = null)
        {
            _processProvider = processProvider;
            _browserSet = browserNames != null ? new HashSet<string>(browserNames, StringComparer.OrdinalIgnoreCase) : new HashSet<string>(new[] { "chrome", "msedge", "firefox" }, StringComparer.OrdinalIgnoreCase);
        }
        
        private readonly HashSet<string> _recorders = new(StringComparer.OrdinalIgnoreCase) { "obs64", "medal", "medalrecord", "shadowplay" };

        public bool IsCritical(Process p)
        {
            try
            {
                var name = p.ProcessName;
                if (string.Equals(name, "dwm", StringComparison.OrdinalIgnoreCase)) return true;
                if (_browserSet.Contains(name)) return IsForeground(p) || IsFullscreen(p);
                if (_recorders.Contains(name)) return true;
                return HasHighPriority(p) || IsGameLike(p) || IsFullscreen(p);
            }
            catch { return false; }
        }

        public Process? DetectGame(Process[] processes, string? manualName)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(manualName))
                {
                    var m = processes.FirstOrDefault(x => x.ProcessName.Equals(manualName, StringComparison.OrdinalIgnoreCase));
                    if (m != null) return m;
                }
                var fg = GetForegroundProcess();
                if (fg != null && IsGameLike(fg)) return fg;
                return processes.FirstOrDefault(IsGameLike);
            }
            catch { return null; }
        }

        public bool IsFullscreen(Process p)
        {
            try
            {
                var hwnd = p.MainWindowHandle;
                if (hwnd == IntPtr.Zero) return false;
                RECT r;
                if (!GetWindowRect(hwnd, out r)) return false;
                var width = r.Right - r.Left;
                var height = r.Bottom - r.Top;
                var sw = GetSystemMetrics(0);
                var sh = GetSystemMetrics(1);
                return Math.Abs(width - sw) < 10 && Math.Abs(height - sh) < 10;
            }
            catch { return false; }
        }

        private bool HasHighPriority(Process p)
        {
            try { return p.PriorityClass == ProcessPriorityClass.High || p.PriorityClass == ProcessPriorityClass.AboveNormal; } catch { return false; }
        }

        private bool IsGameLike(Process p)
        {
            try
            {
                var name = p.ProcessName.ToLowerInvariant();
                if (name.Contains("game") || name.Contains("steam") || name.Contains("epic") || name.Contains("valorant") || name.Contains("csgo") || name.Contains("fortnite")) return true;
                if (IsFullscreen(p)) return true;
                return false;
            }
            catch { return false; }
        }

        private bool IsForeground(Process p)
        {
            try
            {
                var fg = GetForegroundProcess();
                return fg != null && fg.Id == p.Id;
            }
            catch { return false; }
        }

        private Process? GetForegroundProcess()
        {
            try
            {
                if (_processProvider != null) return _processProvider.GetForegroundProcess();
                int pid = VoltrisOptimizer.Core.ForegroundWindowTracker.Instance.CurrentPid;
                if (pid <= 0) return null;
                return Process.GetProcessById(pid);
            }
            catch { return null; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    }
}
