using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace VoltrisOptimizer.UI.Helpers
{
    /// <summary>
    /// Helper para criar janelas com cantos arredondados perfeitos.
    /// Delega para RoundedWindowHelper centralizado.
    /// Compatível com Windows 10 e Windows 11.
    /// </summary>
    public static class WindowRoundedCornersHelper
    {
        /// <summary>
        /// Aplica cantos arredondados à janela WPF.
        /// </summary>
        /// <param name="window">Janela WPF</param>
        /// <param name="cornerRadius">Raio dos cantos em pixels (padrão: 12)</param>
        public static void ApplyRoundedCorners(Window window, int cornerRadius = 12)
        {
            App.LoggingService?.LogTrace($"[UI_HELPER] Aplicando cantos arredondados (raio: {cornerRadius}) para a janela: {window?.Title ?? "Sem Título"}");
            
            // Windows 10: não aplicar bordas arredondadas
            if (!VoltrisOptimizer.Helpers.RoundedWindowHelper.ShouldApplyRoundedCorners())
            {
                App.LoggingService?.LogTrace("[UI_HELPER] Windows 10 detectado - mantendo bordas quadradas nativas");
                return;
            }
            
            // Windows 11: aplicar bordas arredondadas
            VoltrisOptimizer.Helpers.RoundedWindowHelper.Apply(window, cornerRadius);
        }
    }
}
