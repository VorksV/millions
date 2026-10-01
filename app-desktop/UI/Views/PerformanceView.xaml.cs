using System;
using System.Windows;
using System.Windows.Controls;
using VoltrisOptimizer.UI.ViewModels.Performance.Models;

namespace VoltrisOptimizer.UI.Views
{
    /// <summary>
    /// VOLTRIS ULTRA PERFORMANCE - Interface Inteligente
    /// Code-behind limpo - toda lógica está no ViewModel
    /// </summary>
    public partial class PerformanceView : UserControl
    {
        public PerformanceView()
        {
            InitializeComponent();
            Loaded += PerformanceView_Loaded;
        }
        
        private async void PerformanceView_Loaded(object sender, RoutedEventArgs e)
        {
            // Track page view
            App.TelemetryService?.TrackEvent(
                "PAGE_VIEW",
                "Performance",
                "Load",
                success: true
            );
            
            // CORREÇÃO PERFORMANCE: Usar async void para await correto.
            // O LoadDataAsync anterior era fire-and-forget sem await, o que podia
            // causar race conditions e exceções não observadas.
            App.LoggingService?.LogTrace("[UI] Carregando PerformanceView...");
            if (DataContext is ViewModels.PerformanceViewModel vm)
            {
                await vm.LoadDataAsync();
                App.LoggingService?.LogTrace("[UI] PerformanceView pronto para interação.");
            }
        }
    }
}
