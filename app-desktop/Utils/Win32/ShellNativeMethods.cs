using System;
using System.Runtime.InteropServices;

namespace VoltrisOptimizer.Utils.Win32
{
    /// <summary>
    /// Métodos nativos do Windows Shell
    /// </summary>
    public static class ShellNativeMethods
    {
        [Flags]
        public enum EmptyRecycleBinFlags : uint
        {
            SHERB_NOCONFIRMATION = 0x00000001,
            SHERB_NOPROGRESSUI = 0x00000002,
            SHERB_NOSOUND = 0x00000004
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern uint SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, EmptyRecycleBinFlags dwFlags);
        
        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        public struct SHQUERYRBINFO
        {
            public uint cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);
    }
}
