using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.UI.Windows
{
    /// <summary>
    /// Modal de aviso do fluxo de vinculação.
    ///
    /// Visual: mantém o design moderno (janela sem cromo, superfície com raio
    /// de 20px, sombra suave e borda com gradiente).
    ///
    /// Ajuste por sistema:
    ///   Windows 11 — raio de 20px (formato arredondado do Windows 11).
    ///   Windows 10 — SEM bordas arredondadas: raio 0, sem sombra e sem a
    ///                borda de gradiente, porque o Windows 10 não tem cantos
    ///                arredondados e o arredondamento ficava destoando.
    /// </summary>
    public partial class ModernAlertWindow : Window
    {
        private const double RoundedCornerRadius = 20d;

        public ModernAlertWindow(string message, string? title = null, bool isSuccess = true)
        {
            InitializeComponent();
            title ??= VoltrisOptimizer.Services.LocalizationService.Instance.GetString("Loc_Success");
            SourceInitialized += ModernAlertWindow_SourceInitialized;
            RoundedWindowHelper.Apply(this, RoundedCornerRadius);

            ApplyCornerStyleForCurrentWindows();

            App.LoggingService?.LogTrace($"[UI] ModernAlertWindow exibido: [{title}] {message}");

            // Garantir que a janela sempre fique no topo
            Topmost = true;
            ShowInTaskbar = false;

            // Definir Owner como a MainWindow se disponível
            try
            {
                if (Application.Current?.MainWindow != null && Application.Current.MainWindow.IsLoaded)
                {
                    Owner = Application.Current.MainWindow;
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ModernAlertWindow] Set Owner: {ex.Message}"); }

            MessageText.Text = message;
            TitleText.Text = title;

            // Forçar foco na janela
            Loaded += (s, e) =>
            {
                Activate();
                Focus();
                Topmost = true; // Garantir novamente
            };
        }

        /// <summary>
        /// No Windows 10 remove as bordas arredondadas (raio, sombra e borda de
        /// gradiente). No Windows 11 mantém o design moderno como está.
        /// </summary>
        private void ApplyCornerStyleForCurrentWindows()
        {
            if (VisualEffectsManager.IsWindows11) return;

            AlertSurface.CornerRadius = new CornerRadius(0);
            AlertSurface.Effect = null;
            AlertGradientBorder.Visibility = Visibility.Collapsed;

            System.Diagnostics.Debug.WriteLine(
                "[ModernAlertWindow] Windows 10: bordas arredondadas removidas.");
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            App.LoggingService?.LogTrace("[UI] ModernAlertWindow fechado pelo usuário.");
            DialogResult = true;
            Close();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            try
            {
                DragMove();
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ModernAlertWindow] DragMove: {ex.Message}"); }
        }

        protected override void OnActivated(System.EventArgs e)
        {
            base.OnActivated(e);
            // Garantir que sempre fique no topo mesmo após perder foco
            Topmost = true;
        }

        private void ModernAlertWindow_SourceInitialized(object? sender, System.EventArgs e)
        {
            try
            {
                var settings = VoltrisOptimizer.Services.SettingsService.Instance.Settings;
                if (settings.EnableTransparency)
                {
                    bool isLight = settings.Theme?.Equals("Light", System.StringComparison.OrdinalIgnoreCase) == true;
                    VoltrisOptimizer.UI.Helpers.BackdropHelper.ApplyModernBackdrop(this, VoltrisOptimizer.UI.Helpers.BackdropHelper.SystemBackdropType.Acrylic, isLight);
                }
            }
            catch (System.Exception ex)
            {
                VoltrisOptimizer.App.LoggingService?.LogError($"[ModernAlertWindow] Erro ao aplicar backdrop de transparencia", ex);
            }
        }
    }
}
