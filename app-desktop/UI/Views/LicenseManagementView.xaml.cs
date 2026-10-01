using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.License;
using VoltrisOptimizer.UI.ViewModels;

namespace VoltrisOptimizer.UI.Views
{
    /// <summary>
    /// Upgrade/Plans Modal for License Purchase
    /// </summary>
    public partial class LicenseManagementView : UserControl
    {
        public LicenseManagementView()
        {
            InitializeComponent();
            Loaded += async (s, e) => await OnLoadedAsync();
        }

        private async Task OnLoadedAsync()
        {
            App.LoggingService?.LogInfo("[LicenseManagementView] Modal de Upgrade Carregado");
            await Task.CompletedTask;
        }

        private void CloseWindow_Click(object sender, RoutedEventArgs e)
        {
            Window.GetWindow(this)?.Close();
        }

        private void BuyLicense_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                App.LoggingService?.LogInfo("[LicenseManagementView] Botão de compra clicado");
                
                // Navegar para página de ativação de licença
                var mainWindow = Window.GetWindow(this) as VoltrisOptimizer.UI.MainWindow;
                if (mainWindow != null)
                {
                    mainWindow.NavigateToPageSafe("LicenseActivation");
                    Window.GetWindow(this)?.Close();
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[LicenseManagementView] Erro ao abrir página de compra: {ex.Message}");
            }
        }
    }
}
