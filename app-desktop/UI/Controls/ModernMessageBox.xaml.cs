using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.UI.Controls
{
    public partial class ModernMessageBox : Window
    {
        public string Message { get; set; } = "";
        public MessageBoxButton ButtonType { get; set; } = MessageBoxButton.OK;
        public MessageBoxImage IconType { get; set; } = MessageBoxImage.None;
        
        public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

        public ModernMessageBox()
        {
            InitializeComponent();
            SourceInitialized += ModernMessageBox_SourceInitialized;
            RoundedWindowHelper.Apply(this, 16);
            DataContext = this;
        }

        public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, Window? owner = null)
        {
            var dialog = new ModernMessageBox
            {
                Message = messageBoxText,
                ButtonType = button,
                IconType = icon,
                Owner = owner ?? Application.Current.MainWindow
            };
            
            // Definir título usando a propriedade herdada da Window
            dialog.Title = caption;
            dialog.TitleText.Text = caption;
            
            dialog.SetupIcon();
            dialog.SetupButtons();
            dialog.ShowDialog();
            
            return dialog.Result;
        }

        private void SetupIcon()
        {
            IconText.Text = IconType switch
            {
                MessageBoxImage.Question => "?",
                MessageBoxImage.Warning => "?",
                MessageBoxImage.Error => "?",
                MessageBoxImage.Information => "?",
                _ => ""
            };

            IconText.Foreground = IconType switch
            {
                MessageBoxImage.Question => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#31A8FF")),
                MessageBoxImage.Warning => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFB84D")),
                MessageBoxImage.Error => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF3B3B")),
                MessageBoxImage.Information => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#31A8FF")),
                _ => new SolidColorBrush(Colors.White)
            };
        }

        private void SetupButtons()
        {
            ButtonsPanel.Children.Clear();
            
            

            switch (ButtonType)
            {
                case MessageBoxButton.OK:
                    AddButton(VoltrisOptimizer.Services.LocalizationService.Instance.GetString("CommonOk"), MessageBoxResult.OK, isPrimary: true);
                    break;
                case MessageBoxButton.OKCancel:
                    AddButton(VoltrisOptimizer.Services.LocalizationService.Instance.GetString("CommonCancel"), MessageBoxResult.Cancel);
                    AddButton(VoltrisOptimizer.Services.LocalizationService.Instance.GetString("CommonOk"), MessageBoxResult.OK, isPrimary: true);
                    break;
                case MessageBoxButton.YesNo:
                    AddButton(VoltrisOptimizer.Services.LocalizationService.Instance.GetString("CommonNo"), MessageBoxResult.No);
                    AddButton(VoltrisOptimizer.Services.LocalizationService.Instance.GetString("CommonYes"), MessageBoxResult.Yes, isPrimary: true);
                    break;
                case MessageBoxButton.YesNoCancel:
                    AddButton(VoltrisOptimizer.Services.LocalizationService.Instance.GetString("CommonCancel"), MessageBoxResult.Cancel);
                    AddButton(VoltrisOptimizer.Services.LocalizationService.Instance.GetString("CommonNo"), MessageBoxResult.No);
                    AddButton(VoltrisOptimizer.Services.LocalizationService.Instance.GetString("CommonYes"), MessageBoxResult.Yes, isPrimary: true);
                    break;
            }
        }

        private void AddButton(string content, MessageBoxResult result, bool isPrimary = false)
        {
            var button = new Button
            {
                Content = content,
                MinWidth = 120,
                Height = 44,
                Margin = new Thickness(12, 0, 0, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold
            };

            // Usar estilos globais do programa
            if (isPrimary)
            {
                var style = Application.Current.TryFindResource("PrimaryButtonStyle") as Style;
                if (style != null) 
                {
                    button.Style = style;
                }
                else
                {
                    // Fallback com gradiente premium
                    var gradientBrush = new LinearGradientBrush();
                    gradientBrush.StartPoint = new Point(0, 0);
                    gradientBrush.EndPoint = new Point(1, 0);
                    gradientBrush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#FF4B6B"), 0));
                    gradientBrush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#8B31FF"), 0.5));
                    gradientBrush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#31A8FF"), 1));
                    button.Background = gradientBrush;
                    button.Foreground = new SolidColorBrush(Colors.White);
                }
            }
            else
            {
                var style = Application.Current.TryFindResource("SecondaryButtonStyle") as Style;
                if (style != null) 
                {
                    button.Style = style;
                }
                else
                {
                    button.Background = (SolidColorBrush)Application.Current.Resources["DarkPanelBrush"];
                    button.Foreground = (SolidColorBrush)Application.Current.Resources["TextPrimaryBrush"];
                }
            }
            
            button.Click += (s, e) =>
            {
                Result = result;
                try { DialogResult = result == MessageBoxResult.OK || result == MessageBoxResult.Yes; } catch { }
                Close();
            };

            ButtonsPanel.Children.Add(button);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.Cancel;
            DialogResult = false;
            Close();
        }
    
        private void ModernMessageBox_SourceInitialized(object? sender, System.EventArgs e)
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
                VoltrisOptimizer.App.LoggingService?.LogError($"[ModernMessageBox] Erro ao aplicar backdrop de transparencia", ex);
            }
        }}
}
