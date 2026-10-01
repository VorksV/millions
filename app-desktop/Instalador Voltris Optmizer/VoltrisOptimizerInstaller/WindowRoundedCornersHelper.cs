using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace VoltrisOptimizerInstaller
{
    /// <summary>
    /// Helper para criar janelas com cantos arredondados perfeitos
    /// Compatível com Windows 10 e Windows 11
    /// </summary>
    public static class WindowRoundedCornersHelper
    {
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;

        [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("user32.dll")]
        private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int cx, int cy);

        /// <summary>
        /// Aplica cantos arredondados à janela WPF
        /// </summary>
        public static void ApplyRoundedCorners(Window window, int cornerRadius = 12)
        {
            if (window == null) return;

            window.Loaded += (s, e) =>
            {
                try
                {
                    var handle = new WindowInteropHelper(window).Handle;
                    if (handle == IntPtr.Zero) return;

                    if (IsWindows11OrGreater())
                    {
                        ApplyWindows11RoundedCorners(handle);
                    }
                    else
                    {
                        ApplyWindows10RoundedCorners(window, handle, cornerRadius);
                    }
                }
                catch { }
            };
        }

        private static bool IsWindows11OrGreater()
        {
            try
            {
                var version = Environment.OSVersion.Version;
                return version.Major >= 10 && version.Build >= 22000;
            }
            catch
            {
                return false;
            }
        }

        private static void ApplyWindows11RoundedCorners(IntPtr handle)
        {
            try
            {
                int cornerPreference = DWMWCP_ROUND;
                DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));
            }
            catch { }
        }

        private static void ApplyWindows10RoundedCorners(Window window, IntPtr handle, int cornerRadius)
        {
            try
            {
                window.SizeChanged += (s, e) =>
                {
                    UpdateWindowRegion(window, handle, cornerRadius);
                };
                UpdateWindowRegion(window, handle, cornerRadius);
            }
            catch { }
        }

        private static void UpdateWindowRegion(Window window, IntPtr handle, int cornerRadius)
        {
            try
            {
                var source = PresentationSource.FromVisual(window);
                var dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
                var dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

                var width = (int)(window.ActualWidth * dpiX);
                var height = (int)(window.ActualHeight * dpiY);

                if (width <= 0 || height <= 0) return;

                var scaledCornerRadius = (int)(cornerRadius * dpiX);
                var hRgn = CreateRoundRectRgn(0, 0, width, height, scaledCornerRadius * 2, scaledCornerRadius * 2);

                if (hRgn != IntPtr.Zero)
                {
                    SetWindowRgn(handle, hRgn, true);
                }
            }
            catch { }
        }

        public static void ForceApply(Window window, double radius = 16)
        {
            if (window == null) return;
            try
            {
                var handle = new WindowInteropHelper(window).Handle;
                if (handle == IntPtr.Zero) return;

                if (IsWindows11OrGreater())
                {
                    int cornerPreference = DWMWCP_ROUND;
                    DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));
                    
                    window.Clip = null;
                    window.Clip = new System.Windows.Media.RectangleGeometry
                    {
                        Rect = new Rect(0, 0, window.ActualWidth, window.ActualHeight),
                        RadiusX = radius,
                        RadiusY = radius
                    };
                }
                else
                {
                    window.Clip = null;
                    UpdateWindowRegion(window, handle, 0);
                }
            }
            catch { }
        }
    }
}
