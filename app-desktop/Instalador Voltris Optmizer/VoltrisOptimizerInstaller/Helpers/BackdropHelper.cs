using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace VoltrisOptimizerInstaller.Helpers
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
                {
                    hWnd = new WindowInteropHelper(window).EnsureHandle();
                    System.Diagnostics.Debug.WriteLine($"[BackdropHelper] Handle was Zero, EnsureHandle got: {hWnd}");
                }
                if (hWnd == IntPtr.Zero)
                {
                    System.Diagnostics.Debug.WriteLine("[BackdropHelper] Handle still Zero, aborting");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"[BackdropHelper] Applying backdrop, Handle={hWnd}, OSBuild={Environment.OSVersion.Version.Build}");

                if (type == SystemBackdropType.None)
                {
                    DisableAccent(hWnd);
                    if (Environment.OSVersion.Version.Build >= 22000)
                        WindowRoundedCornersHelper.ForceApply(window, 12);
                    return;
                }

                // Aplicar arredondamento ANTES do efeito acrylic
                // Isso garante que o compositor calcule o blur apenas dentro da região
                if (Environment.OSVersion.Version.Build >= 22000)
                    WindowRoundedCornersHelper.ForceApply(window, 12);

                // No Windows 11, pedir ao DWM para arredondar nativamente
                if (Environment.OSVersion.Version.Build >= 22000)
                {
                    try
                    {
                        int preference = 2;
                        DwmSetWindowAttribute(hWnd, 33, ref preference, sizeof(int));
                        System.Diagnostics.Debug.WriteLine("[BackdropHelper] DwmSetWindowAttribute succeeded");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[BackdropHelper] DwmSetWindowAttribute failed: {ex.Message}");
                    }
                }

                EnableAcrylic(hWnd, isLightTheme);
                System.Diagnostics.Debug.WriteLine("[BackdropHelper] EnableAcrylic completed");

                // Reaplicar arredondamento imediatamente após o acrylic
                // SetWindowCompositionAttribute pode resetar a região
                WindowRoundedCornersHelper.ForceApply(window, 12);

                window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { WindowRoundedCornersHelper.ForceApply(window, 12); } catch { }
                }), System.Windows.Threading.DispatcherPriority.Render);

                window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { WindowRoundedCornersHelper.ForceApply(window, 12); } catch { }
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
                window.Clip = null;
                WindowRoundedCornersHelper.ForceApply(window, 12);
            }
            catch { }
        }

        private static void EnableAcrylic(IntPtr hWnd, bool isLight)
        {
            int tintColor = isLight
                ? unchecked((int)0x40FCF8F8)
                : unchecked((int)0x550A0A0F);

            int accentState = ACCENT_ENABLE_ACRYLICBLURBEHIND;
            if (Environment.OSVersion.Version.Build < 17134)
                accentState = ACCENT_ENABLE_BLURBEHIND;

            AccentPolicy accent = new AccentPolicy
            {
                AccentState = accentState,
                AccentFlags = 0x20 | 0x40 | 0x2, // Fix para Windows 11: Força o DWM a renderizar o Blur com Acrylic
                GradientColor = tintColor,
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

                int result = SetWindowCompositionAttribute(hWnd, ref data);
                System.Diagnostics.Debug.WriteLine($"[BackdropHelper] SetWindowCompositionAttribute returned: {result}");
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
