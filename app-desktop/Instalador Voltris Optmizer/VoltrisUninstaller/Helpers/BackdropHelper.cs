using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace VoltrisUninstaller.Helpers
{
    public static class BackdropHelper
    {
        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        private const int WCA_ACCENT_POLICY = 19;
        private const int ACCENT_DISABLED = 0;
        private const int ACCENT_ENABLE_BLURBEHIND = 3;
        private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

        public enum SystemBackdropType { None, Acrylic }

        public static void ApplyModernBackdrop(Window window, SystemBackdropType type = SystemBackdropType.Acrylic, bool isLightTheme = false)
        {
            try
            {
                IntPtr hWnd = new WindowInteropHelper(window).Handle;
                if (hWnd == IntPtr.Zero)
                    hWnd = new WindowInteropHelper(window).EnsureHandle();
                if (hWnd == IntPtr.Zero) return;

                if (type == SystemBackdropType.None)
                {
                    DisableAccent(hWnd);
                    WindowRoundedCornersHelper.ForceApply(window, 16);
                    return;
                }

                if (Environment.OSVersion.Version.Build >= 22000)
                {
                    try
                    {
                        int attribute = 2;
                        DwmSetWindowAttribute(hWnd, attribute, ref attribute, sizeof(int));
                    }
                    catch { }
                }

                EnableAcrylic(hWnd, isLightTheme);

                WindowRoundedCornersHelper.ForceApply(window, 16);

                window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { WindowRoundedCornersHelper.ForceApply(window, 16); } catch { }
                }), System.Windows.Threading.DispatcherPriority.Render);

                window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { WindowRoundedCornersHelper.ForceApply(window, 16); } catch { }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BackdropHelper] ApplyModernBackdrop failed: {ex.Message}");
            }
        }

        public static void RemoveBackdrop(Window window)
        {
            try
            {
                IntPtr hWnd = new WindowInteropHelper(window).Handle;
                if (hWnd == IntPtr.Zero) return;
                DisableAccent(hWnd);
                WindowRoundedCornersHelper.ForceApply(window, 16);
            }
            catch { }
        }

        private static void EnableAcrylic(IntPtr hWnd, bool isLight)
        {
            int gradientColor = isLight ? 0 : 0;

            AccentPolicy accent = new AccentPolicy
            {
                AccentState = ACCENT_ENABLE_ACRYLICBLURBEHIND,
                AccentFlags = 0,
                GradientColor = gradientColor,
                AnimationId = 0
            };

            int accentSize = Marshal.SizeOf(accent);
            IntPtr accentPtr = Marshal.AllocHGlobal(accentSize);

            try
            {
                Marshal.StructureToPtr(accent, accentPtr, false);

                WindowCompositionAttributeData data = new WindowCompositionAttributeData
                {
                    Attribute = WCA_ACCENT_POLICY,
                    SizeOfData = accentSize,
                    Data = accentPtr
                };

                SetWindowCompositionAttribute(hWnd, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(accentPtr);
            }
        }

        private static void DisableAccent(IntPtr hWnd)
        {
            AccentPolicy accent = new AccentPolicy
            {
                AccentState = ACCENT_DISABLED
            };

            int accentSize = Marshal.SizeOf(accent);
            IntPtr accentPtr = Marshal.AllocHGlobal(accentSize);

            try
            {
                Marshal.StructureToPtr(accent, accentPtr, false);

                WindowCompositionAttributeData data = new WindowCompositionAttributeData
                {
                    Attribute = WCA_ACCENT_POLICY,
                    SizeOfData = accentSize,
                    Data = accentPtr
                };

                SetWindowCompositionAttribute(hWnd, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(accentPtr);
            }
        }
    }
}
