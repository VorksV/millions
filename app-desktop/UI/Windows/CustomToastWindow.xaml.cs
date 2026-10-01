using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.UI.Windows
{
    public partial class CustomToastWindow : Window
    {
        private readonly double _targetLeft;
        private readonly double _targetTop;
        private readonly TimeSpan _autoDismissDelay = TimeSpan.FromSeconds(5);
        private CancellationTokenSource? _autoDismissCts;
        private Storyboard? _progressStoryboard;
        private bool _isClosing;
        private bool _isPaused;

        public CustomToastWindow(string title, string message, NotificationType type,
            double targetLeft, double targetTop, double width, double height,
            Geometry? contextualGlyph = null, Color? contextualAccent = null, bool contextualFilled = false)
        {
            InitializeComponent();

            Width = width;
            Height = height;
            _targetLeft = targetLeft;
            _targetTop = targetTop;

            TitleText.Text = GlobalNotificationService.StripEmojis(title);
            MessageText.Text = GlobalNotificationService.StripEmojis(message);

            ConfigureVisuals(type);
            if (contextualGlyph != null)
            {
                SetContextualIcon(contextualGlyph, contextualAccent ?? Colors.SlateGray, contextualFilled);
            }
            UpdateMuteState();

            // Ajustar cantos e margens para Windows 10
            if (!SystemInfoService.IsWindows11)
            {
                ToastBorder.CornerRadius = new CornerRadius(0);
                ProgressBarContainerBorder.CornerRadius = new CornerRadius(0);
                ToastBorder.Margin = new Thickness(0);
                ToastBorder.Effect = null;
            }

            SourceInitialized += CustomToastWindow_SourceInitialized;
        }

        /// <summary>
        /// Exibe um ícone contextual no lugar dos ícones fixos (sucesso/erro/aviso/info).
        /// Usado para dispositivo conectado, reaproveitando exatamente o mesmo glifo e a mesma
        /// cor de destaque usados na aba Rede do Voltris Shield.
        /// </summary>
        /// <param name="filled">
        /// Quando true, o glifo é preenchido com a cor de destaque em vez de
        /// apenas contornado. Os glifos de rede são traçados (false); o
        /// termômetro dos cartões de CPU/GPU do Dashboard é preenchido (true),
        /// e por isso precisa deste modo para ficar visualmente idêntico.
        /// </param>
        public void SetContextualIcon(Geometry glyph, Color accent, bool filled = false)
        {
            try
            {
                SuccessIcon.Visibility = Visibility.Collapsed;
                WarningIcon.Visibility = Visibility.Collapsed;
                ErrorIcon.Visibility = Visibility.Collapsed;
                InfoIcon.Visibility = Visibility.Collapsed;

                ContextualIcon.Data = glyph;

                if (filled)
                {
                    ContextualIcon.Fill = new SolidColorBrush(accent);
                    ContextualIcon.Stroke = Brushes.Transparent;
                    ContextualIcon.StrokeThickness = 0;
                }
                else
                {
                    ContextualIcon.Fill = Brushes.Transparent;
                    ContextualIcon.Stroke = new SolidColorBrush(accent);
                    ContextualIcon.StrokeThickness = 2.4;
                }

                ContextualIconHost.Visibility = Visibility.Visible;
                ContextualIcon.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 8,
                    ShadowDepth = 0,
                    Color = accent,
                    Opacity = 0.7
                };
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[TOAST] Erro ao aplicar ícone contextual", ex);
            }
        }

        public new double Left
        {
            get => base.Left;
            set => base.Left = value;
        }

        /// <summary>
        /// Remove emojis/glifos de conclusão (🎯, ⚡, ✅, ❌, etc.) do texto do toast.
        /// O ícone moderno do cabeçalho é o ÚNICO ícone exibido — o texto deve ser plano.
        /// Implementação centralizada em <see cref="GlobalNotificationService.StripEmojis"/>.
        /// </summary>
        private static string StripEmojis(string text)
        {
            return GlobalNotificationService.StripEmojis(text);
        }

        public new double Top
        {
            get => base.Top;
            set => base.Top = value;
        }

        public void Reposition(double left, double top)
        {
            if (_isClosing) return;
            var anim = new DoubleAnimation
            {
                To = top,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            BeginAnimation(TopProperty, anim);
            base.Left = left;
        }

        private void ConfigureVisuals(NotificationType type)
        {
            // Hide all icons first
            SuccessIcon.Visibility = Visibility.Collapsed;
            WarningIcon.Visibility = Visibility.Collapsed;
            ErrorIcon.Visibility = Visibility.Collapsed;
            InfoIcon.Visibility = Visibility.Collapsed;

            // Select icon and style based on type
            switch (type)
            {
                case NotificationType.Success:
                    IconBorder.Style = (Style)FindResource("ToastIconSuccessStyle");
                    SuccessIcon.Visibility = Visibility.Visible;
                    break;
                case NotificationType.Error:
                    IconBorder.Style = (Style)FindResource("ToastIconErrorStyle");
                    ErrorIcon.Visibility = Visibility.Visible;
                    break;
                case NotificationType.Warning:
                    IconBorder.Style = (Style)FindResource("ToastIconWarningStyle");
                    WarningIcon.Visibility = Visibility.Visible;
                    break;
                case NotificationType.Processing:
                    IconBorder.Style = (Style)FindResource("ToastIconInfoStyle");
                    InfoIcon.Visibility = Visibility.Visible;
                    break;
                default:
                    IconBorder.Style = (Style)FindResource("ToastIconInfoStyle");
                    InfoIcon.Visibility = Visibility.Visible;
                    break;
            }

            // Animação Premium: Rotacionar o ícone continuamente se for Processamento
            if (type == NotificationType.Processing)
            {
                var rotateTransform = new RotateTransform(0);
                InfoIcon.RenderTransformOrigin = new Point(0.5, 0.5);
                InfoIcon.RenderTransform = rotateTransform;

                var rotateAnim = new DoubleAnimation
                {
                    From = 0,
                    To = 360,
                    Duration = TimeSpan.FromSeconds(1.8),
                    RepeatBehavior = RepeatBehavior.Forever
                };
                rotateTransform.BeginAnimation(RotateTransform.AngleProperty, rotateAnim);
            }
            else
            {
                SuccessIcon.RenderTransform = null;
                WarningIcon.RenderTransform = null;
                ErrorIcon.RenderTransform = null;
                InfoIcon.RenderTransform = null;
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Position at the target (bottom-right)
                base.Left = _targetLeft;
                base.Top = _targetTop + 30; // Start 30px below for slide-up effect

                App.LoggingService?.LogInfo($"[TOAST] Window_Loaded: targetLeft={_targetLeft:F0} targetTop={_targetTop:F0} startTop={Top:F0}");

                // Animate slide up + fade in
                var fadeIn = new DoubleAnimation
                {
                    From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                BeginAnimation(OpacityProperty, fadeIn);

                var slideUp = new DoubleAnimation
                {
                    From = Top, To = _targetTop, Duration = TimeSpan.FromMilliseconds(350),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                BeginAnimation(TopProperty, slideUp);

                StartAutoDismiss();
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[CustomToast] Erro no Loaded: {ex.Message}", ex);
                base.Left = _targetLeft;
                base.Top = _targetTop;
                Opacity = 1;
            }
        }

        public void RefreshToast()
        {
            if (_isClosing) return;

            try
            {
                // Cancel existing auto-dismiss task and stop storyboard
                _autoDismissCts?.Cancel();
                _autoDismissCts?.Dispose();
                _progressStoryboard?.Stop();

                // Premium pulse flash effect
                var pulseAnim = new DoubleAnimation
                {
                    From = 1.0,
                    To = 0.4,
                    Duration = TimeSpan.FromMilliseconds(100),
                    AutoReverse = true,
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
                };
                BeginAnimation(OpacityProperty, pulseAnim);

                // Restart auto-dismiss timer and progress bar
                StartAutoDismiss();
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[CustomToast] Erro ao atualizar Toast: {ex.Message}", ex);
            }
        }

        private void StartAutoDismiss()
        {
            _autoDismissCts = new CancellationTokenSource();
            var token = _autoDismissCts.Token;

            _progressStoryboard = new Storyboard();
            // Largura real da barra em vez de constante mágica: com o número fixo a barra
            // não zerava exatamente, parecendo piscar a cada atualização.
            var progressBar = this.FindName("ProgressBar") as FrameworkElement;
            double barWidth = progressBar?.ActualWidth > 0 ? progressBar.ActualWidth : 372d;
            var progressAnim = new DoubleAnimation
            {
                From = 0,
                To = -barWidth,
                Duration = _autoDismissDelay
            };
            Storyboard.SetTargetName(progressAnim, "ProgressBar");
            Storyboard.SetTargetProperty(progressAnim, new PropertyPath("(Border.RenderTransform).(TranslateTransform.X)"));
            _progressStoryboard.Children.Add(progressAnim);
            _progressStoryboard.Begin(this);

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(_autoDismissDelay, token);
                    if (!token.IsCancellationRequested)
                        Dispatcher.InvokeAsync(() => CloseToast());
                }
                catch (TaskCanceledException) { }
            }, token);
        }

        private void PauseAutoDismiss()
        {
            _isPaused = true;
            _progressStoryboard?.Pause();
        }

        private void ResumeAutoDismiss()
        {
            _isPaused = false;
            _progressStoryboard?.Resume();
        }

        private void CloseToast()
        {
            if (_isClosing) return;
            _isClosing = true;

            try
            {
                var fadeOut = new DoubleAnimation
                {
                    From = 1, To = 0, Duration = TimeSpan.FromMilliseconds(250),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                };
                fadeOut.Completed += (s, e) => Close();
                BeginAnimation(OpacityProperty, fadeOut);

                var slideDown = new DoubleAnimation
                {
                    To = Top + 20, Duration = TimeSpan.FromMilliseconds(250),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                };
                BeginAnimation(TopProperty, slideDown);
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[CustomToast] Erro no CloseToast: {ex.Message}", ex);
                try { Close(); } catch (Exception ex2) { System.Diagnostics.Debug.WriteLine($"[CustomToastWindow] Close: {ex2.Message}"); }
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => CloseToast();

        private void MuteButton_Click(object sender, RoutedEventArgs e)
        {
            bool newMuted = !GlobalNotificationService.IsToastMuted;
            GlobalNotificationService.IsToastMuted = newMuted;
            UpdateMuteState();

            App.LoggingService?.LogInfo($"[TOAST] Mute button toggled: {newMuted}");

            if (newMuted)
            {
                CloseToast();
                CloseAllActiveToasts();
            }
        }

        private void UpdateMuteState()
        {
            bool muted = GlobalNotificationService.IsToastMuted;
            if (muted)
            {
                MuteIcon.Data = Geometry.Parse("M19,11c0,3.53-2.61,6.43-6,6.92V21h-3v1h8v-1h-3v-3.08c3.39-0.49,6-3.39,6-6.92h-2zM4.41,2.86L3,4.27l6,6V11a3,3 0 0,0 3,3c0.41,0,0.8-.08,1.15-.24l4,4C15.8,18.4 13.97,19 12,19c-3.87,0-7-3.13-7-7H3c0,4.53,3.31,8.31,7.66,8.9v1.1h3v-1.1c1.36-.18,2.64-.67,3.75-1.4l2.32,2.33 1.41-1.41L4.41,2.86zM15,11V5.5c0-1.66-1.34-3-3-3-1.31,0-2.42.84-2.83,2L14.73,10c.17-.3.27-.64.27-1z");
                MuteIcon.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                MuteButton.ToolTip = LocalizationService.Instance.GetString("ToastTooltipUnmute");
            }
            else
            {
                MuteIcon.Data = Geometry.Parse("M3 9v6h4l5 5V4L7 9H3z");
                MuteIcon.Fill = new SolidColorBrush(Color.FromRgb(156, 163, 175));
                MuteButton.ToolTip = LocalizationService.Instance.GetString("ToastTooltipMute");
            }
        }

        private static void CloseAllActiveToasts()
        {
            try
            {
                // Acessamos via reflection a lista estática — ou fechamos via dispatcher.
                // Como não temos acesso direto à lista de _activeToasts (estática privada no GlobalNotificationService),
                // esta função é chamada dentro do loop de fechamento do toast atual.
                // Os demais toasts serão fechados na próxima vez que o GlobalNotificationService tentar exibir.
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[CustomToastWindow] CloseAllActiveToasts: {ex.Message}"); }
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => CloseToast();
        private void Window_MouseEnter(object sender, MouseEventArgs e) => PauseAutoDismiss();
        private void Window_MouseLeave(object sender, MouseEventArgs e) => ResumeAutoDismiss();

        protected override void OnClosed(EventArgs e)
        {
            _autoDismissCts?.Cancel();
            _autoDismissCts?.Dispose();
            _progressStoryboard?.Stop();
            base.OnClosed(e);
        }

        private void CustomToastWindow_SourceInitialized(object? sender, EventArgs e)
        {
            try
            {
                var settings = VoltrisOptimizer.Services.SettingsService.Instance.Settings;
                bool acrylicEnabled = settings.EnableTransparency;
                
                App.LoggingService?.LogInfo($"[Toast] Acrylic {(acrylicEnabled ? "applied" : "disabled")} (EnableTransparency={acrylicEnabled})");
                
                if (acrylicEnabled)
                {
                    bool isLight = settings.Theme?.Equals("Light", System.StringComparison.OrdinalIgnoreCase) == true;
                    VoltrisOptimizer.UI.Helpers.BackdropHelper.ApplyModernBackdrop(
                        this,
                        VoltrisOptimizer.UI.Helpers.BackdropHelper.SystemBackdropType.Acrylic,
                        isLight);
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[CustomToastWindow] Erro ao aplicar backdrop de transparencia", ex);
            }
        }
    }
}