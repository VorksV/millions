using System.Windows.Controls;
using VoltrisOptimizer.UI.ViewModels;

namespace VoltrisOptimizer.UI.Views
{
    /// <summary>
    /// Interação lógica para BenchmarkView.xaml
    /// </summary>
    public partial class BenchmarkView : UserControl
    {
        public BenchmarkView()
        {
            InitializeComponent();
            App.LoggingService?.LogTrace("[UI] Navegou para BenchmarkView");
        }
    }
}
