using System.Windows;
using System.Windows.Media;

namespace VoltrisOptimizerInstaller
{
    public partial class CustomMessageBox : Window
    {
        public CustomMessageBox(string title, string message, MessageBoxImage icon = MessageBoxImage.Information)
        {
            InitializeComponent();

            TitleText.Text = title;
            MessageText.Text = message;

            // Configurar ícone baseado no tipo (usando gradiente Voltris)
            var voltrisGradient = new LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0, 0),
                EndPoint = new System.Windows.Point(1, 1)
            };
            voltrisGradient.GradientStops.Add(new GradientStop(Color.FromRgb(255, 75, 107), 0));
            voltrisGradient.GradientStops.Add(new GradientStop(Color.FromRgb(139, 49, 255), 0.5));
            voltrisGradient.GradientStops.Add(new GradientStop(Color.FromRgb(49, 168, 255), 1));

            switch (icon)
            {
                case MessageBoxImage.Information:
                    IconBorder.Background = voltrisGradient;
                    IconText.Text = "ℹ";
                    break;
                case MessageBoxImage.Warning:
                    IconBorder.Background = voltrisGradient;
                    IconText.Text = "⚠";
                    break;
                case MessageBoxImage.Error:
                    IconBorder.Background = voltrisGradient;
                    IconText.Text = "❌";
                    break;
                case MessageBoxImage.Question:
                    IconBorder.Background = voltrisGradient;
                    IconText.Text = "❓";
                    break;
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            DragMove();
        }

        public static void Show(string title, string message, MessageBoxImage icon = MessageBoxImage.Information)
        {
            var msgBox = new CustomMessageBox(title, message, icon);
            msgBox.ShowDialog();
        }
    }
}
