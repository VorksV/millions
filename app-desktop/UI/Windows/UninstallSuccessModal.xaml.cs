using System.Windows;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.UI.Windows
{
    /// <summary>
    /// Modal de sucesso exibido após a desinstalação de aplicativos no Debloat.
    /// Mostra o total de programas desinstalados com sucesso e os que falharam.
    /// </summary>
    public partial class UninstallSuccessModal : Window
    {
        public UninstallSuccessModal()
        {
            InitializeComponent();
            SourceInitialized += UninstallSuccessModal_SourceInitialized;
            App.LoggingService?.LogTrace("[DEBLOAT] Exibindo modal de sucesso de desinstalação");
            RoundedWindowHelper.Apply(this, 20);
        }

        /// <summary>
        /// Configura os dados exibidos no modal.
        /// </summary>
        /// <param name="successCount">Quantidade de apps desinstalados com sucesso.</param>
        /// <param name="failureCount">Quantidade de apps que falharam na desinstalação.</param>
        public void SetResults(int successCount, int failureCount)
        {
            App.LoggingService?.LogInfo($"[DEBLOAT] Resultados: {successCount} sucesso(s), {failureCount} falha(s)");
            SuccessCountText.Text = successCount.ToString();
            FailureCountText.Text = failureCount.ToString();

            // Mostrar badge de falhas apenas se houver
            FailureBadge.Visibility = failureCount > 0
                ? Visibility.Visible
                : Visibility.Collapsed;

            // Ajustar título e subtítulo conforme resultado
            if (successCount == 0 && failureCount > 0)
            {
                TitleText.Text = VoltrisOptimizer.Services.LocalizationService.Instance.GetString("UninstallNoneRemoved");
                SubtitleText.Text = string.Format(VoltrisOptimizer.Services.LocalizationService.Instance.GetString("UninstallNoneRemovedSubtitle"), failureCount);
            }
            else if (failureCount > 0)
            {
                TitleText.Text = string.Format(VoltrisOptimizer.Services.LocalizationService.Instance.GetString("UninstallPartialSuccess"), successCount);
                SubtitleText.Text = string.Format(VoltrisOptimizer.Services.LocalizationService.Instance.GetString("UninstallPartialSubtitle"), failureCount);
            }
            else
            {
                int total = successCount;
                TitleText.Text = total == 1
                    ? VoltrisOptimizer.Services.LocalizationService.Instance.GetString("UninstallSingleSuccess")
                    : string.Format(VoltrisOptimizer.Services.LocalizationService.Instance.GetString("UninstallMultipleSuccess"), total);
                SubtitleText.Text = VoltrisOptimizer.Services.LocalizationService.Instance.GetString("UninstallSuccessSubtitle");
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Window_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                DragMove();
        }
    
        private void UninstallSuccessModal_SourceInitialized(object? sender, System.EventArgs e)
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
                VoltrisOptimizer.App.LoggingService?.LogError($"[UninstallSuccessModal] Erro ao aplicar backdrop de transparencia", ex);
            }
        }}
}
