using System;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace VoltrisOptimizer.UI.ViewModels
{
    public class ViewModelLocator
    {
        private static ViewModelLocator? _instance;
        public static ViewModelLocator Instance => _instance ??= new ViewModelLocator();
        
        private IServiceProvider? Services => App.Services;
        private DashboardViewModel? _dashboardVM;

        public ViewModelLocator()
        {
            App.LoggingService?.LogInfo("[VM_LOCATOR] ===== CONSTRUTOR DO ViewModelLocator CHAMADO =====");
            App.LoggingService?.LogInfo($"[VM_LOCATOR] App.Services disponível: {App.Services != null}");
            _instance = this;
        }

        /// <summary>
        /// Pré-resolve ViewModels pesados para garantir que as instâncias singleton
        /// são criadas antes do primeiro acesso via NavBar/XAML.
        ///
        /// IMPORTANTE: ViewModels como DashboardViewModel contêm DispatcherTimer
        /// (DependencyObject com thread-affinity). Portanto a resolução DEVE
        /// ocorrer na UI thread. Usamos Dispatcher.InvokeAsync com prioridade
        /// Background para não bloquear a UI mas respeitar a afinidade de thread.
        /// </summary>
        public async Task WarmupAsync()
        {
            var tid = System.Threading.Thread.CurrentThread.ManagedThreadId;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            App.LoggingService?.LogInfo($"[VM_LOCATOR][TID:{tid}] WarmupAsync INÍCIO");

            try
            {
                // CORREÇÃO: NÃO usar Task.Run aqui.
                // DashboardViewModel (e outros) criam DispatcherTimer no construtor,
                // que exige a UI thread. Task.Run causa InvalidOperationException (cross-thread).
                // Dispatcher.InvokeAsync + Background priority: não bloqueia renderização
                // mas garante que os ViewModels são instanciados na thread correta.
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher == null)
                {
                    App.LoggingService?.LogWarning("[VM_LOCATOR] Dispatcher não disponível — Warmup abortado.");
                    return;
                }

                await dispatcher.InvokeAsync(() =>
                {
                    var wtid = System.Threading.Thread.CurrentThread.ManagedThreadId;
                    App.LoggingService?.LogInfo($"[VM_LOCATOR][TID:{wtid}] Warmup na UI thread (Background priority)...");
                    App.LoggingService?.LogInfo("[VM_LOCATOR] Resolvendo DashboardVM...");
                    _ = DashboardVM;
                }, System.Windows.Threading.DispatcherPriority.Background);

                await Task.Delay(50); // Yield explícito para a UI thread processar eventos pendentes (Render, etc)

                await dispatcher.InvokeAsync(() =>
                {
                    App.LoggingService?.LogInfo("[VM_LOCATOR] Resolvendo GamerVM...");
                    _ = GamerVM;
                }, System.Windows.Threading.DispatcherPriority.Background);

                await Task.Delay(50); // Segundo yield para evitar acúmulo de processamento

                await dispatcher.InvokeAsync(() =>
                {
                    App.LoggingService?.LogInfo("[VM_LOCATOR] Resolvendo NotificationDrawer...");
                    _ = NotificationDrawer;
                }, System.Windows.Threading.DispatcherPriority.Background);

                sw.Stop();
                App.LoggingService?.LogSuccess($"[VM_LOCATOR][TID:{tid}] Warmup concluído em {sw.ElapsedMilliseconds}ms.");
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[VM_LOCATOR][TID:{tid}] Erro durante Warmup: {ex.Message}");
            }
        }

        public DashboardViewModel? DashboardVM => _dashboardVM ??= Services?.GetService<DashboardViewModel>();
        private PerformanceViewModel? _performanceVM;

        public PerformanceViewModel? PerformanceVM => _performanceVM ??= Services?.GetService<PerformanceViewModel>();

        public VoltrisOptimizer.Services.DPC.IDpcAnalyzerService? DpcSvc => Services?.GetService<VoltrisOptimizer.Services.DPC.IDpcAnalyzerService>();

        private GamerViewModel? _gamerVM;
        public GamerViewModel? GamerVM
        {
            get
            {
                if (_gamerVM != null) return _gamerVM;
                try
                {
                    _gamerVM = Services?.GetService<GamerViewModel>();
                    App.LoggingService?.LogInfo($"[VM_LOCATOR] GamerVM resolved: {_gamerVM != null}");
                }
                catch (Exception ex)
                {
                    App.LoggingService?.LogError($"[VM_LOCATOR] ERRO ao resolver GamerVM: {ex.GetType().Name}: {ex.Message}");
                    App.LoggingService?.LogError($"[VM_LOCATOR] Stack: {ex.StackTrace}");
                }
                return _gamerVM;
            }
        }


        public RepairViewModel? RepairVM => Services?.GetService<RepairViewModel>();

        public StreamHubViewModel? StreamHubVM =>
            App.Services?.GetService<StreamHubViewModel>();
        public SmartRepairViewModel? SmartRepairVM =>
            Services?.GetService<SmartRepairViewModel>();

        public RecoveryViewModel? RecoveryVM =>
            App.Services?.GetService<RecoveryViewModel>();

        private NotificationDrawerViewModel? _notificationDrawerVM;
        public NotificationDrawerViewModel? NotificationDrawer => _notificationDrawerVM ??= Services?.GetService<NotificationDrawerViewModel>();

        public SecurityViewModel? SecurityVM => Services?.GetService<SecurityViewModel>();
        public PrivacyViewModel? PrivacyVM => Services?.GetService<PrivacyViewModel>();

        private ShortcutsViewModel? _shortcutsVM;
        public ShortcutsViewModel? ShortcutsVM => _shortcutsVM ??= Services?.GetService<ShortcutsViewModel>();

        // SettingsViewModel, NetworkViewModel and SystemViewModel have been removed or renamed.
    }
}

