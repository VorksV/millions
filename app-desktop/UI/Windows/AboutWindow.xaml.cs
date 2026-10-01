using System;
using System.Diagnostics;
using System.Windows;
using VoltrisOptimizer.Properties;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.UI.Helpers;

namespace VoltrisOptimizer.UI.Windows
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            this.Title = $"Sobre o {VersionInfo.Product} v{VersionInfo.Version}";
            
            // Aplicar transparência/backdrop se ativado nas configurações
            if (SettingsService.Instance.Settings.EnableTransparency)
            {
                bool isLight = SettingsService.Instance.Settings.Theme?.Equals("Light", StringComparison.OrdinalIgnoreCase) == true;
                BackdropHelper.ApplyModernBackdrop(this, BackdropHelper.SystemBackdropType.Acrylic, isLight);
            }
        }

        private void Window_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                this.DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
        
        private void WebsiteButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = VersionInfo.Website,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(LocalizationService.Instance["Loc_NaoFoiPossivelAbrirOWebsite"] + ex.Message, 
                              "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void SupportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = $"mailto:{VersionInfo.Support}",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(LocalizationService.Instance["Loc_NaoFoiPossivelAbrirOSuporte"] + ex.Message, 
                              "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void PrivacyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = VersionInfo.Privacy,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(LocalizationService.Instance["Loc_NaoFoiPossivelAbrirAPoliticaDePrivacidade"] + ex.Message, 
                              "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void TermsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = VersionInfo.Terms,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(LocalizationService.Instance["Loc_NaoFoiPossivelAbrirOsTermosDeUso"] + ex.Message, 
                              "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
