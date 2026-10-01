using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace VoltrisOptimizer.UI.Controls
{
    public partial class UnlinkConfirmationDialog : UserControl
    {
        public event EventHandler<bool> ConfirmationResult;
        
        public UnlinkConfirmationDialog()
        {
            InitializeComponent();
        }
        
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            ConfirmationResult?.Invoke(this, false);
        }
        
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            ConfirmationResult?.Invoke(this, false);
        }
        
        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            // Adicionar animação de confirmação
            var scaleAnim = new DoubleAnimation
            {
                From = 1.0,
                To = 0.95,
                Duration = TimeSpan.FromMilliseconds(100),
                AutoReverse = true
            };
            
            ConfirmButton.BeginAnimation(RenderTransformProperty, scaleAnim);
            
            ConfirmationResult?.Invoke(this, true);
        }
        
        // Permitir fechar com ESC
        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (e.Key == Key.Escape)
            {
                ConfirmationResult?.Invoke(this, false);
                e.Handled = true;
            }
        }
    }
}
