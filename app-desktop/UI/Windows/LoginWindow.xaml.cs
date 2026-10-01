using System;
using System.Windows;
using System.Windows.Input;
using VoltrisOptimizer.Services.Licensing;

namespace VoltrisOptimizer.UI.Windows
{
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();
            SourceInitialized += LoginWindow_SourceInitialized;
        }

        private void LoginWindow_SourceInitialized(object? sender, EventArgs e)
        {
            try
            {
                var settings = VoltrisOptimizer.Services.SettingsService.Instance.Settings;
                if (settings.EnableTransparency)
                {
                    bool isLight = settings.Theme?.Equals("Light", StringComparison.OrdinalIgnoreCase) == true;
                    VoltrisOptimizer.UI.Helpers.BackdropHelper.ApplyModernBackdrop(this, VoltrisOptimizer.UI.Helpers.BackdropHelper.SystemBackdropType.Acrylic, isLight);
                    App.LoggingService?.LogInfo("[LoginWindow] Backdrop de transparência Acrylic aplicado com sucesso");
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[LoginWindow] Erro ao aplicar backdrop de transparência", ex);
            }
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            var email = EmailBox.Text.Trim();
            var password = PasswordBox.Password.Trim();

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                ShowError(VoltrisOptimizer.Services.LocalizationService.Instance.GetString("LoginFillAllFields"));
                return;
            }

            LoginButton.IsEnabled = false;
            LoginButton.Content = VoltrisOptimizer.Services.LocalizationService.Instance.GetString("LoginAccessing");
            ErrorText.Visibility = Visibility.Collapsed;

            var result = await VoltrisApiService.Instance.LoginAsync(email, password);

            if (result.success)
            {
                DialogResult = true;
                Close();
            }
            else
            {
                ShowError(result.error ?? VoltrisOptimizer.Services.LocalizationService.Instance.GetString("LoginAuthFailed"));
                LoginButton.IsEnabled = true;
                LoginButton.Content = VoltrisOptimizer.Services.LocalizationService.Instance.GetString("LoginEnter");
            }
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
