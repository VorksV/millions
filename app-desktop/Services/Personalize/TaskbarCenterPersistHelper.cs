using System;
using System.Runtime.InteropServices;

namespace VoltrisOptimizer.Services.Personalize
{
    /// <summary>
    /// Helper estático para o processo headless --taskbar-center.
    /// Fornece acesso a APIs Win32 necessárias sem dependência da UI ou do DI container.
    /// </summary>
    internal static class TaskbarCenterPersistHelper
    {
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

        /// <summary>
        /// Tenta encontrar a Shell_TrayWnd (janela principal da taskbar).
        /// Retorna IntPtr.Zero se o Explorer ainda não inicializou.
        /// </summary>
        public static IntPtr FindShellTrayWnd()
        {
            try
            {
                return FindWindow("Shell_TrayWnd", null);
            }
            catch
            {
                return IntPtr.Zero;
            }
        }
    }
}
