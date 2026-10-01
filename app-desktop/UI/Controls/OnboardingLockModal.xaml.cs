using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace VoltrisOptimizer.UI.Controls
{
    public partial class OnboardingLockModal : UserControl
    {
        public OnboardingLockModal()
        {
            InitializeComponent();
        }

        public void Show()
        {
            ModalRoot.Visibility = Visibility.Visible;
            
            // Animação de entrada
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
            var scaleIn = new DoubleAnimation(0.8, 1.0, TimeSpan.FromMilliseconds(400))
            {
                EasingFunction = new BackEase { Amplitude = 0.3 }
            };

            ModalRoot.BeginAnimation(OpacityProperty, fadeIn);
            
            var transform = new System.Windows.Media.ScaleTransform(0.8, 0.8);
            ModalContent.RenderTransform = transform;
            ModalContent.RenderTransformOrigin = new Point(0.5, 0.5);
            
            transform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleIn);
            transform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleIn);
        }

        public void Hide()
        {
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            fadeOut.Completed += (s, e) => ModalRoot.Visibility = Visibility.Collapsed;
            ModalRoot.BeginAnimation(OpacityProperty, fadeOut);
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }
    }
}
