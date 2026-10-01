using System.Windows.Controls;
using VoltrisOptimizer.UI.ViewModels;

namespace VoltrisOptimizer.UI.Views
{
    /// <summary>
    /// Interaction logic for GamerDiagnosticsView.xaml
    /// </summary>
    public partial class GamerDiagnosticsView : UserControl
    {
        public GamerDiagnosticsView()
        {
            InitializeComponent();
            
            // Gerenciar ciclo de vida: iniciar monitoramento quando a View for carregada
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        public GamerDiagnosticsView(GamerDiagnosticsViewModel viewModel) : this()
        {
            DataContext = viewModel;
        }

        private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is GamerDiagnosticsViewModel vm)
                vm.StartMonitoring();
        }

        private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is GamerDiagnosticsViewModel vm)
                vm.StopMonitoring();
        }
    }
}


