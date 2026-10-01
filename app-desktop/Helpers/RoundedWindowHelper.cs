using System;
using System.Windows;
using System.Windows.Controls;

namespace VoltrisOptimizer.Helpers
{
    /// <summary>
    /// Wrapper compatível para preservar chamadas antigas sem redistribuir lógica de composição.
    /// </summary>
    public static class RoundedWindowHelper
    {
        public static bool IsWindows11 => Environment.OSVersion.Version.Build >= 22000;

        public static bool ShouldApplyRoundedCorners()
        {
            return VisualEffectsManager.ShouldApplyRoundedCorners(IsWindows11, true);
        }

        public static void Apply(Window window, double radius = 16)
        {
            ForceApply(window, radius);
        }

        public static void ForceApply(Window window, double radius = 16)
        {
            if (window == null) return;
            if (!ShouldApplyRoundedCorners())
            {
                window.Clip = null;
                return;
            }

            if (!window.Dispatcher.CheckAccess())
            {
                window.Dispatcher.BeginInvoke(new Action(() => ForceApply(window, radius)));
                return;
            }

            VisualEffectsManager.ApplyVisualEffects(
                window,
                VisualEffectsManager.WindowType.MainWindow,
                VisualEffectsManager.BackdropType.Acrylic,
                true);
        }

        public static void ApplyToContextMenu(ContextMenu menu, double radius = 12)
        {
        }

        public static void EnableGlobalContextMenuRounding(double radius = 12)
        {
        }
    }
}
