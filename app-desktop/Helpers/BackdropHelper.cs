using System;
using System.Windows;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.UI.Helpers
{
    /// <summary>
    /// Wrapper para aplicar efeitos visuais de forma retrocompatível.
    /// Redireciona tudo para o VisualEffectsManager (Single Source of Truth).
    /// </summary>
    public static class BackdropHelper
    {
        public enum SystemBackdropType { None, Acrylic }

        /// <summary>
        /// Aplica acrylic blur na janela WPF (Agora suporta WindowChrome nativo no Windows 11).
        /// </summary>
        public static void ApplyModernBackdrop(Window window, SystemBackdropType type = SystemBackdropType.Acrylic, bool isLightTheme = false)
        {
            if (window == null) return;

            var backdropType = type == SystemBackdropType.Acrylic ? 
                VisualEffectsManager.BackdropType.Acrylic : 
                VisualEffectsManager.BackdropType.None;

            if (window.Dispatcher.CheckAccess())
            {
                VisualEffectsManager.ApplyVisualEffects(window, VisualEffectsManager.WindowType.MainWindow, backdropType, true);
            }
            else
            {
                window.Dispatcher.BeginInvoke(() => 
                    VisualEffectsManager.ApplyVisualEffects(window, VisualEffectsManager.WindowType.MainWindow, backdropType, true), 
                    System.Windows.Threading.DispatcherPriority.Send);
            }
        }

        /// <summary>
        /// Remove o efeito acrylic e restaura fundo normal.
        /// </summary>
        public static void RemoveBackdrop(Window window)
        {
            if (window == null) return;
            VisualEffectsManager.RemoveVisualEffects(window);
        }
    }
}