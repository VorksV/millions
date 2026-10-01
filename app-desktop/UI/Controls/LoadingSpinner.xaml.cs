using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VoltrisOptimizer.UI.Controls
{
    /// <summary>
    /// Componente de loading spinner moderno com gradiente
    /// </summary>
    public partial class LoadingSpinner : UserControl
    {
        public static readonly DependencyProperty SpinnerSizeProperty =
            DependencyProperty.Register(
                nameof(SpinnerSize), 
                typeof(double), 
                typeof(LoadingSpinner),
                new PropertyMetadata(60.0, OnSpinnerSizeChanged));

        public static readonly DependencyProperty SpinnerColorProperty =
            DependencyProperty.Register(
                nameof(SpinnerColor), 
                typeof(Brush), 
                typeof(LoadingSpinner),
                new PropertyMetadata(null));

        public static readonly DependencyProperty IsSpinningProperty =
            DependencyProperty.Register(
                nameof(IsSpinning), 
                typeof(bool), 
                typeof(LoadingSpinner),
                new PropertyMetadata(true, OnIsSpinningChanged));

        /// <summary>
        /// Tamanho do spinner
        /// </summary>
        public double SpinnerSize
        {
            get => (double)GetValue(SpinnerSizeProperty);
            set => SetValue(SpinnerSizeProperty, value);
        }

        /// <summary>
        /// Cor do spinner (override do gradiente padrão)
        /// </summary>
        public Brush SpinnerColor
        {
            get => (Brush)GetValue(SpinnerColorProperty);
            set => SetValue(SpinnerColorProperty, value);
        }

        /// <summary>
        /// Se o spinner está animando
        /// </summary>
        public bool IsSpinning
        {
            get => (bool)GetValue(IsSpinningProperty);
            set => SetValue(IsSpinningProperty, value);
        }

        public LoadingSpinner()
        {
            InitializeComponent();
        }

        private static void OnSpinnerSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LoadingSpinner spinner)
            {
                var size = (double)e.NewValue;
                spinner.Width = size;
                spinner.Height = size;
            }
        }

        private static void OnIsSpinningChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LoadingSpinner spinner)
            {
                spinner.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }
}

