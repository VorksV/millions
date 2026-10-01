using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;

namespace VoltrisOptimizer.Utils.Win32
{
    public static class WindowManager
    {
        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, string windowTitle);

        public const uint WM_CLOSE = 0x0010;
        public const uint WM_COMMAND = 0x0111;
        public const uint BM_CLICK = 0x00F5;

        public static List<IntPtr> GetWindowsByProcessId(uint processId)
        {
            App.LoggingService?.LogTrace($"[WindowManager] Buscando janelas para o PID: {processId}");
            var windows = new List<IntPtr>();
            EnumWindows((hWnd, lParam) =>
            {
                GetWindowThreadProcessId(hWnd, out uint windowProcessId);
                if (windowProcessId == processId)
                {
                    windows.Add(hWnd);
                }
                return true;
            }, IntPtr.Zero);
            
            if (windows.Count > 0)
                App.LoggingService?.LogTrace($"[WindowManager] Encontrada(s) {windows.Count} janela(s) para o PID {processId}");
                
            return windows;
        }

        public static string GetWindowTitle(IntPtr hWnd)
        {
            var sb = new StringBuilder(256);
            GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }
    }
}
