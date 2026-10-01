using System;

 using System.Threading.Tasks;

 using System.Windows;

 using System.Windows.Controls;

 using System.Windows.Input;

 using VoltrisUninstaller.Core;
 using VoltrisUninstaller.Services;
 using VoltrisUninstaller.Helpers;
 using Language = VoltrisUninstaller.Services.Language;

 namespace VoltrisUninstaller 
 {
     public partial class MainWindow : Window
     {
         private readonly ILogger _logger;
         private readonly UninstallOptions _options;
         private bool _isUninstalling = false;
         private readonly LocalizationService _localization;
         private bool _transparencyEnabled = true;

         public MainWindow(ILogger logger, UninstallOptions options)
         {
             _logger = logger ?? throw new ArgumentNullException(nameof(logger));
             _options = options ?? throw new ArgumentNullException(nameof(options));
             
             InitializeComponent();
             
             _localization = LocalizationService.Instance;
             _localization.LanguageChanged += (s, e) => UpdateLocalizedTexts();
             
             var detectedLanguage = LanguageDetector.DetectWindowsLanguage();
             _localization.SetLanguage(detectedLanguage);
             
             UpdateLocalizedTexts();
             
WindowRoundedCornersHelper.ApplyRoundedCorners(this, cornerRadius: 12);

              this.Loaded += (s, e) =>
              {
                  ApplyThemeAndTransparency(_transparencyEnabled);
              };
              
              KeepUserDataCheckBox.IsChecked = _options.KeepUserData;
             KeepUserDataCheckBox.Checked += (s, e) => _options.KeepUserData = true;
             KeepUserDataCheckBox.Unchecked += (s, e) => _options.KeepUserData = false;
         }

         private void UpdateLocalizedTexts()
         {
         }
         
         private void ShowSuccessModal()
         {
             try
             {
                 var dialog = new Window
                 {
                     Owner = this,
                     WindowStyle = WindowStyle.None,
                     AllowsTransparency = true,
                     Background = System.Windows.Media.Brushes.Transparent,
                     ResizeMode = ResizeMode.NoResize,
                     ShowInTaskbar = false,
                     SizeToContent = SizeToContent.WidthAndHeight,
                     WindowStartupLocation = WindowStartupLocation.CenterOwner
                 };

                 var root = new Border
                 {
                     Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1E1E2E")),
                     BorderBrush = (System.Windows.Media.SolidColorBrush)System.Windows.Application.Current.Resources["DarkBorderBrush"],
                     BorderThickness = new Thickness(1),
                     CornerRadius = new CornerRadius(20),
                     Padding = new Thickness(40, 50, 40, 40),
                     MaxWidth = 450
                 };

                 root.Effect = new System.Windows.Media.Effects.DropShadowEffect
                 {
                     BlurRadius = 40,
                     ShadowDepth = 0,
                     Opacity = 0.7,
                     Color = System.Windows.Media.Colors.Black
                 };

                 var stack = new StackPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Center };

                 var successIcon = new System.Windows.Controls.TextBlock
                 {
                     Text = "✓",
                     FontSize = 60,
                     FontWeight = System.Windows.FontWeights.Bold,
                     HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                     Margin = new Thickness(0, 0, 0, 20)
                 };
                 successIcon.Foreground = new System.Windows.Media.LinearGradientBrush
                 {
                     StartPoint = new System.Windows.Point(0, 0),
                     EndPoint = new System.Windows.Point(1, 0),
                     GradientStops = new System.Windows.Media.GradientStopCollection
                     {
                         new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(255, 75, 107), 0),
                         new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(139, 49, 255), 1)
                     }
                 };

                 var titleBlock = new TextBlock
                 {
                     Text = _localization.GetString("UninstallComplete"),
                     FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                     FontWeight = FontWeights.Bold,
                     FontSize = 22,
                     TextAlignment = TextAlignment.Center,
                     TextWrapping = TextWrapping.Wrap,
                     Margin = new Thickness(0, 0, 0, 16),
                     HorizontalAlignment = System.Windows.HorizontalAlignment.Center
                 };
                 titleBlock.Foreground = System.Windows.Media.Brushes.White;

                 var closeBtn = new System.Windows.Controls.Button
                 {
                     Content = _localization.GetString("Close"),
                     Style = (Style)System.Windows.Application.Current.Resources["ModernButtonStyle"],
                     MinWidth = 150,
                     Height = 45,
                     Margin = new Thickness(0, 20, 0, 0),
                     HorizontalAlignment = System.Windows.HorizontalAlignment.Center
                 };
                 closeBtn.Click += (s, e) => dialog.Close();

                 stack.Children.Add(successIcon);
                 stack.Children.Add(titleBlock);
                 stack.Children.Add(closeBtn);
                 root.Child = stack;
                 dialog.Content = root;

                 dialog.ShowDialog();
             }
             catch { }
         }

         private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
         {
             if (e.ChangedButton == MouseButton.Left) this.DragMove();
         }

         private void MinimizeButton_Click(object sender, RoutedEventArgs e)
         {
             this.WindowState = WindowState.Minimized;
         }

         private void CloseButton_Click(object sender, RoutedEventArgs e)
         {
             if (_isUninstalling)
             {
                 var result = MessageBox.Show(
                     _localization.GetString("Cancel") + "?", 
                     _localization.GetString("Warning"), 
                     MessageBoxButton.YesNo, MessageBoxImage.Warning);
                 if (result == MessageBoxResult.No) return;
             }
             
             System.Windows.Input.Mouse.Capture(null);
             this.Dispatcher.BeginInvoke(new Action(() => this.Close()), System.Windows.Threading.DispatcherPriority.Background);
         }

         private void CancelButton_Click(object sender, RoutedEventArgs e)
         {
             // Se estiver no questionário, permitir voltar para a intro
             if (SurveyView.Visibility == Visibility.Visible)
             {
                 SurveyView.Visibility = Visibility.Collapsed;
                 IntroPanel.Visibility = Visibility.Visible;
                 UninstallButton.Content = _localization.GetString("Next");
                 CancelButton.Content = _localization.GetString("Cancel");
                 return;
             }
             
             System.Windows.Input.Mouse.Capture(null);
             this.Dispatcher.BeginInvoke(new Action(() => this.Close()), System.Windows.Threading.DispatcherPriority.Background);
         }
         
         private void LanguageButton_Click(object sender, RoutedEventArgs e)
         {
             ShowLanguageModal();
         }

         private void ShowLanguageModal()
         {
             try
             {
                 var dialog = new Window
                 {
                     Owner = this,
                     WindowStyle = WindowStyle.None,
                     AllowsTransparency = true,
                     Background = System.Windows.Media.Brushes.Transparent,
                     ResizeMode = ResizeMode.NoResize,
                     ShowInTaskbar = false,
                     SizeToContent = SizeToContent.WidthAndHeight,
                     WindowStartupLocation = WindowStartupLocation.CenterOwner
                 };

                 var root = new Border
                {
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(28, 28, 30)),
                    BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(58, 58, 62)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(24),
                    MaxWidth = 360
                };

                 root.Effect = new System.Windows.Media.Effects.DropShadowEffect
                 {
                     BlurRadius = 24,
                     ShadowDepth = 0,
                     Opacity = 0.6,
                     Color = System.Windows.Media.Colors.Black
                 };

                 var stack = new StackPanel { MinWidth = 300 };

                 var titleBlock = new TextBlock
                 {
                     Text = _localization.GetString("SelectLanguage"),
                     FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                     FontWeight = FontWeights.SemiBold,
                     FontSize = 18,
                     Margin = new Thickness(0, 0, 0, 16),
                     HorizontalAlignment = System.Windows.HorizontalAlignment.Center
                 };
                 titleBlock.Foreground = new System.Windows.Media.LinearGradientBrush
                 {
                     StartPoint = new System.Windows.Point(0, 0),
                     EndPoint = new System.Windows.Point(1, 0),
                     GradientStops = new System.Windows.Media.GradientStopCollection
                     {
                         new System.Windows.Media.GradientStop(((System.Windows.Media.SolidColorBrush)System.Windows.Application.Current.Resources["AccentBrush"]).Color, 0),
                         new System.Windows.Media.GradientStop(((System.Windows.Media.SolidColorBrush)System.Windows.Application.Current.Resources["SecondaryBrush"]).Color, 0.5),
                         new System.Windows.Media.GradientStop(((System.Windows.Media.SolidColorBrush)System.Windows.Application.Current.Resources["PrimaryBrush"]).Color, 1)
                     }
                 };

                 var buttonsPanel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

                 Action<Services.Language, string> addLangButton = (lang, label) =>
                 {
                     var btn = new System.Windows.Controls.Button
                     {
                         Content = label,
                         Style = (Style)System.Windows.Application.Current.Resources["SecondaryButtonStyle"],
                         Margin = new Thickness(0, 4, 0, 4),
                         Height = 44,
                         HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch
                     };
                     btn.Click += (s, ev) =>
                     {
                         _localization.SetLanguage(lang);
                         dialog.Close();
                     };
                     buttonsPanel.Children.Add(btn);
                 };

                 addLangButton(Services.Language.Portuguese, _localization.GetLanguageName(Services.Language.Portuguese));
                 addLangButton(Services.Language.Spanish, _localization.GetLanguageName(Services.Language.Spanish));
                 addLangButton(Services.Language.English, _localization.GetLanguageName(Services.Language.English));

                 var cancelBtn = new System.Windows.Controls.Button
                 {
                     Content = _localization.GetString("Cancel"),
                     Style = (Style)System.Windows.Application.Current.Resources["ModernButtonStyle"],
                     Margin = new Thickness(0, 12, 0, 0),
                     Height = 40,
                     HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch
                 };
                 cancelBtn.Click += (s, ev) => dialog.Close();

                 stack.Children.Add(titleBlock);
                 stack.Children.Add(buttonsPanel);
                 stack.Children.Add(cancelBtn);
                 root.Child = stack;
                 dialog.Content = root;

                 dialog.KeyDown += (s, ev) => { if (ev.Key == System.Windows.Input.Key.Escape) dialog.Close(); };
                 dialog.ShowDialog();
             }
             catch
             {
             }
         }

        private async void UninstallButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isUninstalling) return;

            // PASSO 1: Transição para Questionário
            if (IntroPanel.Visibility == Visibility.Visible)
            {
                _logger.LogInfo("[UI] Indo para o questionário...");
                IntroPanel.Visibility = Visibility.Collapsed;
                SurveyView.Visibility = Visibility.Visible;
                UninstallButton.Content = "FINALIZAR";
                return;
            }

            // PASSO 2: Transição para Execução
            if (SurveyView.Visibility == Visibility.Visible)
            {
                // VALIDAR SE ALGO FOI SELECIONADO
                bool anySelected = Reason1.IsChecked == true || Reason2.IsChecked == true || Reason3.IsChecked == true || Reason4.IsChecked == true || Reason5.IsChecked == true;
                if (!anySelected)
                {
                    MessageBox.Show(_localization.GetString("SelectReasonWarning"), _localization.GetString("Warning"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _logger.LogInfo("[UI] Iniciando processo de desinstalação...");
                VoltrisUninstaller.Core.TelegramLogger.SendMessageFireAndForget($"🗑️ <b>Iniciando Desinstalação do Voltris Optimizer</b>\n<b>Máquina:</b> {Environment.MachineName}\n<b>Usuário:</b> {Environment.UserName}");
                _isUninstalling = true;
                _options.KeepUserData = KeepUserDataCheckBox.IsChecked ?? false;

                SurveyView.Visibility = Visibility.Collapsed;
                ProgressPanel.Visibility = Visibility.Visible;
                UninstallButton.IsEnabled = false;
                CancelButton.IsEnabled = false;

                await Task.Delay(500);

                try
                {
                    var uninstaller = new Uninstaller(_logger, _options);
                    var progress = new Progress<UninstallProgress>(p =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            ProgressBar.Value = p.Percent;
                            StatusText.Text = p.Step;
                        }, System.Windows.Threading.DispatcherPriority.Normal);
                    });

                    var result = await uninstaller.ExecuteAsync(progress);

                    if (result.Success)
                    {
                        string reason = "Não especificado";
                        if (Reason1.IsChecked == true) reason = "Não entendi como usar o programa";
                        else if (Reason2.IsChecked == true) reason = "O programa causou erros no meu sistema";
                        else if (Reason3.IsChecked == true) reason = "Achei o valor da licença muito alto";
                        else if (Reason4.IsChecked == true) reason = "Vou formatar meu computador";
                        else if (Reason5.IsChecked == true) reason = "Outro (testes, curiosidade, etc.)";

                        StatusText.Text = _localization.GetString("UninstallComplete");
                        ProgressBar.Value = 100;
                        VoltrisUninstaller.Core.TelegramLogger.SendMessageFireAndForget($"✅ <b>Voltris Optimizer Desinstalado com Sucesso!</b>\n<b>Motivo:</b> {reason}\n<b>Máquina:</b> {Environment.MachineName}");
                        ShowSuccessModal();
                        
                        await Task.Delay(2000);
                        System.Windows.Input.Mouse.Capture(null);
                        this.Dispatcher.BeginInvoke(new Action(() => this.Close()), System.Windows.Threading.DispatcherPriority.Background);
                    }
                    else
                    {
                        StatusText.Text = $"Erro: {result.ErrorMessage}";
                        UninstallButton.IsEnabled = true;
                        UninstallButton.Content = "Tentar Novamente";
                        _isUninstalling = false;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Erro crítico: {ex.Message}");
                    StatusText.Text = "Erro durante a remoção.";
                    UninstallButton.IsEnabled = true;
                    _isUninstalling = false;
                }
            }
        }

        private void TransparencyToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _transparencyEnabled = !_transparencyEnabled;
            ApplyThemeAndTransparency(_transparencyEnabled);
            UpdateTransparencyIcon(_transparencyEnabled);
        }

        private void ApplyThemeAndTransparency(bool transparency)
        {
            var res = System.Windows.Application.Current.Resources;
            var accentColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF4B6B");
            var secondaryColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8B31FF");
            if (transparency)
            {
                var aC = accentColor; aC.A = 38;
                var sC = secondaryColor; sC.A = 38;
                res["AccentBlurBrush"] = new System.Windows.Media.SolidColorBrush(aC);
                res["SecondaryBlurBrush"] = new System.Windows.Media.SolidColorBrush(sC);
                res["DarkPanelBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(100, 15, 15, 17));
            }
            else
            {
                res["AccentBlurBrush"] = System.Windows.Media.Brushes.Transparent;
                res["SecondaryBlurBrush"] = System.Windows.Media.Brushes.Transparent;
                res["DarkPanelBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 15, 15, 17));
            }

            ApplyTransparency(transparency);
        }

        private void ApplyTransparency(bool enabled)
        {
            try
            {
                // A aplicação de Acrylic em janelas AllowsTransparency="True" causa NullReferenceException 
                // no InputManager do WPF. Apenas garantimos que os cantos continuem arredondados.
                WindowRoundedCornersHelper.ForceApply(this, 12);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[Transparency] Error: {ex.Message}");
            }
        }

        private void UpdateTransparencyIcon(bool enabled)
        {
            if (TransparencyToggleButton == null) return;

            var path = enabled
                ? "M12,3C7.05,3 3,7.05 3,12C3,16.95 7.05,21 12,21C16.95,21 21,16.95 21,12C21,7.05 16.95,3 12,3M12,5C15.87,5 19,8.13 19,12C19,15.87 15.87,19 12,19V5Z"
                : "M12,2A10,10 0 0,1 22,12A10,10 0 0,1 12,22A10,10 0 0,1 2,12A10,10 0 0,1 12,2M12,4A8,8 0 0,0 4,12A8,8 0 0,0 12,20A8,8 0 0,0 20,12A8,8 0 0,0 12,4Z";

            var border = TransparencyToggleButton.Template?.FindName("transBorder", TransparencyToggleButton) as System.Windows.Controls.Border;
            var icon = TransparencyToggleButton.Template?.FindName("transIcon", TransparencyToggleButton) as System.Windows.Shapes.Path;

            if (icon != null)
            {
                icon.Data = System.Windows.Media.Geometry.Parse(path);
            }
        }
    }
}
