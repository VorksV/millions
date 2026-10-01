using System.Windows;
using VoltrisOptimizer.UI.Controls;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.UI.Windows
{
    public partial class PurchasePlansWindow : Window
    {
        public PurchasePlansWindow()
        {
            InitializeComponent();
            SourceInitialized += PurchasePlansWindow_SourceInitialized;
            App.LoggingService?.LogTrace("[UI] Aberta janela de planos de aquisição");
            RoundedWindowHelper.Apply(this, 16);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BuyLicenseActivation_Click(object sender, RoutedEventArgs e)
        {
            var deviceId = LicenseManager.Instance.GetDeviceId();
            App.LoggingService?.LogInfo("[PLANS] Licença de Ativação comercial selecionada");

            var baseUrl = VoltrisOptimizer.Services.SiteConfig.PurchaseLicenseUrl;

            OpenUrl($"{baseUrl}?plan=activation&installation_id={System.Uri.EscapeDataString(deviceId)}");
        }

        private void BuyPremium_Click(object sender, RoutedEventArgs e)
        {
            var deviceId = LicenseManager.Instance.GetDeviceId();
            App.LoggingService?.LogInfo("[PLANS] Voltris Premium selecionado");

            var baseUrl = VoltrisOptimizer.Services.SiteConfig.PurchaseLicenseUrl;

            OpenUrl($"{baseUrl}?plan=premium&installation_id={System.Uri.EscapeDataString(deviceId)}");
        }

        private void OpenUrl(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
                Close();
            }
            catch
            {
                ModernMessageBox.Show(string.Format(VoltrisOptimizer.Services.LocalizationService.Instance.GetString("PurchasePlansOpenError"), url), VoltrisOptimizer.Services.LocalizationService.Instance.GetString("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error, this);
            }
        }
    
        private void PurchasePlansWindow_SourceInitialized(object? sender, System.EventArgs e)
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
                VoltrisOptimizer.App.LoggingService?.LogError($"[PurchasePlansWindow] Erro ao aplicar backdrop de transparencia", ex);
            }
        }
    }
}
