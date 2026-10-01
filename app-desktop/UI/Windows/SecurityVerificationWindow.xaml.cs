using System;
using System.Diagnostics;
using System.Windows;
using VoltrisOptimizer.Properties;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.UI.Helpers;

namespace VoltrisOptimizer.UI.Windows
{
    public partial class SecurityVerificationWindow : Window
    {
        public SecurityVerificationWindow()
        {
            InitializeComponent();
            this.Title = $"Verificação de Segurança - {VersionInfo.Product}";

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

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
