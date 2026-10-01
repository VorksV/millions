using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace VoltrisOptimizer.UI.Controls
{
    /// <summary>
    /// Overlay de loading com animação para uso em views
    /// </summary>
    public partial class LoadingOverlay : UserControl
    {
        #region Dependency Properties

        public static readonly DependencyProperty LoadingMessageProperty =
            DependencyProperty.Register(
                nameof(LoadingMessage),
                typeof(string),
                typeof(LoadingOverlay),
                new PropertyMetadata(VoltrisOptimizer.Services.LocalizationService.Instance["Loading"]));

        public static readonly DependencyProperty ShowProgressProperty =
            DependencyProperty.Register(
                nameof(ShowProgress),
                typeof(bool),
                typeof(LoadingOverlay),
                new PropertyMetadata(false));

        public static readonly DependencyProperty ProgressProperty =
            DependencyProperty.Register(
                nameof(Progress),
                typeof(double),
                typeof(LoadingOverlay),
                new PropertyMetadata(0.0, OnProgressChanged));

        public static readonly DependencyProperty ProgressTextProperty =
            DependencyProperty.Register(
                nameof(ProgressText),
                typeof(string),
                typeof(LoadingOverlay),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty IsLoadingProperty =
            DependencyProperty.Register(
                nameof(IsLoading),
                typeof(bool),
                typeof(LoadingOverlay),
                new PropertyMetadata(false, OnIsLoadingChanged));

        #endregion

        #region Properties

        /// <summary>
        /// Mensagem de loading exibida
        /// </summary>
        public string LoadingMessage
        {
            get => (string)GetValue(LoadingMessageProperty);
            set => SetValue(LoadingMessageProperty, value);
        }

        /// <summary>
        /// Se deve mostrar a barra de progresso
        /// </summary>
        public bool ShowProgress
        {
            get => (bool)GetValue(ShowProgressProperty);
            set => SetValue(ShowProgressProperty, value);
        }

        /// <summary>
        /// Valor do progresso (0-100)
        /// </summary>
        public double Progress
        {
            get => (double)GetValue(ProgressProperty);
            set => SetValue(ProgressProperty, Math.Max(0, Math.Min(100, value)));
        }

        /// <summary>
        /// Texto adicional de progresso
        /// </summary>
        public string ProgressText
        {
            get => (string)GetValue(ProgressTextProperty);
            set => SetValue(ProgressTextProperty, value);
        }

        /// <summary>
        /// Se o overlay está visível/ativo
        /// </summary>
        public bool IsLoading
        {
            get => (bool)GetValue(IsLoadingProperty);
            set => SetValue(IsLoadingProperty, value);
        }

        #endregion

        public LoadingOverlay()
        {
            InitializeComponent();
        }

        #region Event Handlers

        private static void OnProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LoadingOverlay overlay)
            {
                overlay.UpdateProgressBar((double)e.NewValue);
            }
        }

        private static void OnIsLoadingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LoadingOverlay overlay)
            {
                var isLoading = (bool)e.NewValue;
                if (isLoading)
                {
                    overlay.Show();
                }
                else
                {
                    overlay.Hide();
                }
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Mostra o overlay com animação
        /// </summary>
        public void Show(string? message = null)
        {
            if (!string.IsNullOrEmpty(message))
            {
                LoadingMessage = message;
            }

            Visibility = Visibility.Visible;

            var storyboard = (Storyboard)FindResource("ShowAnimation");
            storyboard?.Begin(this);
        }

        /// <summary>
        /// Esconde o overlay com animação
        /// </summary>
        public void Hide()
        {
            var storyboard = (Storyboard)FindResource("HideAnimation");
            if (storyboard != null)
            {
                var completedHandler = new EventHandler((s, e) =>
                {
                    Visibility = Visibility.Collapsed;
                    Progress = 0;
                });

                storyboard.Completed += completedHandler;
                storyboard.Begin(this);
            }
            else
            {
                Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Atualiza progresso e mensagem
        /// </summary>
        public void UpdateProgress(double progress, string? message = null)
        {
            Progress = progress;
            
            if (!string.IsNullOrEmpty(message))
            {
                LoadingMessage = message;
            }

            ProgressText = $"{progress:F0}%";
        }

        #endregion

        #region Private Methods

        private void UpdateProgressBar(double progress)
        {
            if (ProgressFill != null && ProgressContainer != null)
            {
                var maxWidth = 300.0; // Width do container
                var newWidth = (progress / 100.0) * maxWidth;

                // Animar a largura
                var animation = new DoubleAnimation
                {
                    To = newWidth,
                    Duration = TimeSpan.FromMilliseconds(200),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                ProgressFill.BeginAnimation(WidthProperty, animation);
            }
        }

        #endregion
    }
}

