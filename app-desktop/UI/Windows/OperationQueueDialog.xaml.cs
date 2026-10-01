using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.UI.Windows
{
    /// <summary>
    /// Modal moderno para escolha do usuário quando há conflito de operações
    /// </summary>
    public partial class OperationQueueDialog : Window
    {
        public enum UserChoice
        {
            AddToQueue,
            ExecuteNow,
            ScheduleLater,
            Cancel
        }

        public UserChoice Result { get; private set; } = UserChoice.Cancel;

        private readonly string _currentOperationName;
        private readonly double _currentProgress;
        private readonly string _requestedOperationName;

        public OperationQueueDialog(
            string currentOperationName,
            double currentProgress,
            string requestedOperationName)
        {
            InitializeComponent();
            SourceInitialized += OperationQueueDialog_SourceInitialized;
            App.LoggingService?.LogInfo($"[QUEUE] Aberto diálogo de conflito: '{currentOperationName}' ({currentProgress:F1}%) vs '{requestedOperationName}'");
            RoundedWindowHelper.Apply(this, 20);

            _currentOperationName = currentOperationName;
            _currentProgress = currentProgress;
            _requestedOperationName = requestedOperationName;

            InitializeUI();
            AnimateEntrance();
        }

        private void InitializeUI()
        {
            // Atualizar texto da operação atual
            CurrentOperationText.Text = _currentOperationName;

            // Atualizar progresso
            ProgressText.Text = $"{_currentProgress:F0}%";
            ProgressBar.Width = (ProgressText.ActualWidth > 0 ? ProgressText.ActualWidth : 500) * (_currentProgress / 100.0);

            // Calcular tempo restante estimado
            var remainingPercent = 100 - _currentProgress;
            var estimatedMinutes = (int)Math.Ceiling(remainingPercent / 20.0); // ~20% por minuto (estimativa)
            
            if (estimatedMinutes <= 1)
                TimeRemainingText.Text = LocalizationService.Instance.GetString("QueueAboutOneMinute");
            else if (estimatedMinutes <= 5)
                TimeRemainingText.Text = $"~{estimatedMinutes} " + LocalizationService.Instance.GetString("QueueMinutesRemainingUnit");
            else
                TimeRemainingText.Text = LocalizationService.Instance.GetString("QueueAboutFewMinutes");

            // Animar barra de progresso
            var progressAnimation = new DoubleAnimation
            {
                From = 0,
                To = 500 * (_currentProgress / 100.0),
                Duration = TimeSpan.FromMilliseconds(800),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            ProgressBar.BeginAnimation(WidthProperty, progressAnimation);
        }

        private void AnimateEntrance()
        {
            // Fade in
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            this.BeginAnimation(OpacityProperty, fadeIn);

            // Scale in
            this.RenderTransformOrigin = new Point(0.5, 0.5);
            var scaleTransform = new System.Windows.Media.ScaleTransform(0.9, 0.9);
            this.RenderTransform = scaleTransform;

            var scaleAnimation = new DoubleAnimation(0.9, 1.0, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
            };
            scaleTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleAnimation);
            scaleTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleAnimation);
        }

        private void Option1_Click(object sender, MouseButtonEventArgs e)
        {
            App.LoggingService?.LogInfo("[QUEUE] Usuário escolheu: ADICIONAR À FILA");
            Result = UserChoice.AddToQueue;
            AnimateExit();
        }

        private void Option2_Click(object sender, MouseButtonEventArgs e)
        {
            App.LoggingService?.LogWarning("[QUEUE] Usuário tentando: EXECUTAR AGORA (Saltar dependências)");
            // Mostrar confirmação adicional para opção avançada
            var confirmResult = MessageBox.Show(
                LocalizationService.Instance.GetString("QueueCancelDepsConfirm"),
                LocalizationService.Instance.GetString("QueueConfirmImmediateTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirmResult == MessageBoxResult.Yes)
            {
                Result = UserChoice.ExecuteNow;
                AnimateExit();
            }
        }

        private void Option3_Click(object sender, MouseButtonEventArgs e)
        {
            App.LoggingService?.LogInfo("[QUEUE] Usuário escolheu: AGENDAR PARA DEPOIS");
            Result = UserChoice.ScheduleLater;
            AnimateExit();
        }

        private void AnimateExit()
        {
            // Fade out
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            fadeOut.Completed += (s, e) => this.DialogResult = true;
            this.BeginAnimation(OpacityProperty, fadeOut);
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }
    
        private void OperationQueueDialog_SourceInitialized(object? sender, System.EventArgs e)
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
                VoltrisOptimizer.App.LoggingService?.LogError($"[OperationQueueDialog] Erro ao aplicar backdrop de transparencia", ex);
            }
        }}
}
