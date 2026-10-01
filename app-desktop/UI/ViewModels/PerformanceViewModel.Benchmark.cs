using System.Windows.Input;

namespace VoltrisOptimizer.UI.ViewModels
{
    /// <summary>
    /// Partial class mantida para compatibilidade.
    /// O benchmark agora é uma página dedicada no Sidebar.
    /// </summary>
    public partial class PerformanceViewModel
    {
        // Mantidos para evitar erros de binding residuais
        public ICommand? OpenBenchmarkCommand { get; private set; }
        public ICommand? ToggleBenchmarkModeCommand { get; private set; }

        private bool _isBenchmarkMode;
        public bool IsBenchmarkMode
        {
            get => _isBenchmarkMode;
            set { SetProperty(ref _isBenchmarkMode, value); }
        }

        public BenchmarkViewModel? BenchmarkViewModel => null;

        private void InitializeBenchmarkIntegration()
        {
            App.LoggingService?.LogTrace("[PERF] Inicializando integração de benchmark (Modo Legado/Migrado)");
            // Benchmark migrado para página dedicada - noop
        }
    }
}
