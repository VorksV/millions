using System.Windows.Controls;
using VoltrisOptimizer.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace VoltrisOptimizer.UI.Views
{
    public partial class DisplayView : UserControl
    {
        public DisplayView()
        {
            InitializeComponent();
            App.LoggingService?.LogTrace("[UI] Navegou para DisplayView");

            var vm = App.Services?.GetService<DisplayViewModel>();
            if (vm != null)
            {
                DataContext = vm;
                Loaded += async (_, _) => 
                {
                    App.LoggingService?.LogTrace("[UI] DisplayView carregado. Iniciando monitoramento...");
                    await vm.LoadAsync();
                };
                Unloaded += (_, _) => 
                {
                    App.LoggingService?.LogTrace("[UI] Saindo de DisplayView. Parando monitoramento...");
                    vm.StopMonitoring();
                };
            }
        }
    }
}
