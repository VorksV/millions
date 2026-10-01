using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using VoltrisOptimizer.Core;
using VoltrisOptimizer.UI.ViewModels;
using VoltrisOptimizer.UI.Windows;

namespace VoltrisOptimizer.UI.Views
{
    public partial class DebloatView : UserControl
    {
        private readonly DebloatViewModel _viewModel;

        public DebloatView()
        {
            InitializeComponent();
            App.LoggingService?.LogTrace("[UI] Navegou para DebloatView");

            _viewModel = ServiceLocator.GetRequiredService<DebloatViewModel>();
            DataContext = _viewModel;

            // Inscrever no evento de conclusão para abrir o modal de sucesso
            _viewModel.UninstallCompleted += OnUninstallCompleted;
        }

        private void OnUninstallCompleted(int successCount, int failureCount)
        {
            // Garantir execução na UI thread
            Dispatcher.InvokeAsync(() =>
            {
                var modal = new UninstallSuccessModal
                {
                    Owner = System.Windows.Application.Current.MainWindow
                };
                modal.SetResults(successCount, failureCount);
                modal.ShowDialog();
            });
        }
    }
}
