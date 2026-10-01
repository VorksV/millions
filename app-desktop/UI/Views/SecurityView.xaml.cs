using System;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using VoltrisOptimizer.Core;
using VoltrisOptimizer.UI.ViewModels;

namespace VoltrisOptimizer.UI.Views
{
    public partial class SecurityView : UserControl
    {
        private bool _loaded;

        public SecurityView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            App.LoggingService?.LogTrace("[UI] SecurityView construída");
        }

        private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (_loaded) return;
            _loaded = true;

            App.LoggingService?.LogInfo("[UI] SecurityView.OnLoaded — inicializando ViewModel...");
            try
            {
                var dc = DataContext as SecurityViewModel;
                var vm = ViewModelLocator.Instance.SecurityVM;
                var dcHash = dc?.GetHashCode().ToString() ?? "null";
                var vmHash = vm?.GetHashCode().ToString() ?? "null";
                App.LoggingService?.LogInfo($"[UI] SecurityView: DataContext hash={dcHash}, Resolved VM hash={vmHash}, Match={dc == vm}");

                if (vm != null)
                {
                    await vm.EnsureInitializedAsync();
                    if (dc != vm)
                        App.LoggingService?.LogWarning($"[UI] DataContext ({dcHash}) != resolved VM ({vmHash}) — corrigindo DataContext");
                }
                else
                    App.LoggingService?.LogWarning("[UI] SecurityVM não encontrado no ViewModelLocator");
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[UI] Erro ao inicializar SecurityView: {ex.Message}", ex);
            }
        }

        private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
        {
            var vm = ViewModelLocator.Instance.SecurityVM;
            vm?.StopPolling();
            App.LoggingService?.LogInfo("[UI] SecurityView.OnUnloaded — polling cancelado");
        }
    }
}
