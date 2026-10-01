using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.UI.Views;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// Páginas disponíveis para navegação.
    ///
    /// NOTA: não existe <c>Logs</c>. A página de visualização de logs foi removida
    /// da interface, mas TODO o sistema de logging continua intacto: a pasta
    /// <c>LOGS</c> na raiz do programa é criada e gravada normalmente, e
    /// <see cref="ILoggingService"/> segue disponível para todos os serviços.
    /// </summary>
    public enum AppPage
    {
        Dashboard,
        Performance,
        Network,
        System,
        Gamer,
        Diagnostics,
        History,
        Scheduler,
        Settings,
        Onboarding,
        ProfilerQuestionnaire,
        ProfilerSummary,
          LyraResponse,
          // [FIX:PAGINA-ENERGIA-REMOVIDA] `Energy` saiu do enum.
          //
          // A página de Energia oferecia atalhos que CONTRADIZIAM o Perfil
          // Inteligente: ativar planos nativos do Windows, editar EPP à mão,
          // restaurar padrões e "consertar" energia. Qualquer um desses botões
          // desfazia o perfil escolhido, e era por isso que o app e o usuário
          // brigavam sobre o resultado.
          //
          // A energia agora tem uma única interface de controle: o Perfil.
          StreamHub,
        SmartRepair,
        Shield,
        Security,
        Privacy,
        Debloat,
        Recovery,
        DeviceInfo
    }
    
    /// <summary>
    /// Serviço de navegação tipado e centralizado
    /// </summary>
    public class NavigationService : INavigationService
    {
        private ContentControl? _contentHost;
        private readonly Stack<AppPage> _navigationHistory = new();
        private AppPage _currentPage = AppPage.Dashboard;
        private readonly ILoggingService? _logger;
        private readonly VoltrisOptimizer.Services.Telemetry.TelemetryService? _telemetryService;
        
        /// <summary>
        /// Evento disparado quando a navegação ocorre
        /// </summary>
        public event EventHandler<NavigationEventArgs>? Navigated;
        
        /// <summary>
        /// Página atual
        /// </summary>
        public AppPage CurrentPage => _currentPage;
        
        /// <summary>
        /// Indica se pode voltar no histórico
        /// </summary>
        public bool CanGoBack => _navigationHistory.Count > 0;
        
        public NavigationService()
        {
            _logger = App.LoggingService;
            // Fallback para ServiceLocator se não injetado, mas idealmente deve ser via construtor
            try { _telemetryService = VoltrisOptimizer.Core.ServiceLocator.GetService<VoltrisOptimizer.Services.Telemetry.TelemetryService>(); } catch { }
            InitializeContentHost();
        }
        
        public NavigationService(ILoggingService? logger, VoltrisOptimizer.Services.Telemetry.TelemetryService? telemetryService)
        {
            _logger = logger;
            _telemetryService = telemetryService;
            InitializeContentHost();
        }
        
        private void InitializeContentHost()
        {
            try
            {
                var app = Application.Current;
                if (app?.MainWindow == null) return;

                var dispatcher = app.Dispatcher;
                if (dispatcher == null) return;

                if (dispatcher.CheckAccess())
                {
                    _contentHost = app.MainWindow.FindName("ContentFrame") as ContentControl;
                }
                else
                {
                    dispatcher.Invoke(() =>
                    {
                        _contentHost = app.MainWindow?.FindName("ContentFrame") as ContentControl;
                    });
                }
            }
            catch (InvalidOperationException)
            {
                // MainWindow ainda não está pronto — será retentado no NavigateTo
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[NAV] Erro ao inicializar ContentHost: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Navega para uma página tipada
        /// </summary>
        public bool NavigateTo(AppPage page, object? parameter = null)
        {
            try
            {
                if (_contentHost == null)
                {
                    InitializeContentHost();
                    if (_contentHost == null)
                    {
                        _logger?.LogWarning("[NAV] ContentHost não encontrado");
                        return false;
                    }
                }
                
                var view = CreateView(page, parameter);
                if (view == null)
                {
                    _logger?.LogWarning($"[NAV] Não foi possível criar view para: {page}");
                    return false;
                }
                
                // Salvar página atual no histórico
                _navigationHistory.Push(_currentPage);
                _currentPage = page;
                
                // Navegar
                _contentHost.Content = view;
                
                _logger?.LogInfo($"[NAV] Navegou para: {page}");
                
                // Telemetry: Track page view with forced flush for real-time dashboard update
                _telemetryService?.TrackEvent("PAGE_VIEW", "Navigation", page.ToString(), forceFlush: true);
                
                Navigated?.Invoke(this, new NavigationEventArgs(page, parameter));
                
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[NAV] Erro ao navegar para {page}: {ex.Message}", ex);
                return false;
            }
        }
        
        /// <summary>
        /// Navega para uma página por string (compatibilidade com código legado)
        /// </summary>
        public void NavigateTo(string viewKey)
        {
            if (Enum.TryParse<AppPage>(viewKey, true, out var page))
            {
                NavigateTo(page);
            }
            else
            {
                // Fallback para views antigas
                var view = ResolveViewLegacy(viewKey);
                if (view != null && _contentHost != null)
                {
                    _contentHost.Content = view;
                }
            }
        }
        
        /// <summary>
        /// Volta para a página anterior
        /// </summary>
        public bool GoBack()
        {
            if (!CanGoBack) return false;
            
            var previousPage = _navigationHistory.Pop();
            _currentPage = previousPage;
            
            var view = CreateView(previousPage);
            if (view != null && _contentHost != null)
            {
                _contentHost.Content = view;
                _logger?.LogInfo($"[NAV] Voltou para: {previousPage}");
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Exibe um modal
        /// </summary>
        public void ShowModal(string viewKey)
        {
            var view = ResolveViewLegacy(viewKey);
            if (view == null) return;
            
            var window = new Window 
            { 
                Content = view, 
                WindowStartupLocation = WindowStartupLocation.CenterOwner, 
                Owner = Application.Current?.MainWindow,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent
            };
            window.ShowDialog();
        }
        
        /// <summary>
        /// Fecha o modal ativo
        /// </summary>
        public void CloseModal()
        {
            var windows = Application.Current?.Windows;
            if (windows == null || windows.Count == 0) return;
            
            var topWindow = windows[windows.Count - 1] as Window;
            if (topWindow != null && topWindow != Application.Current?.MainWindow)
            {
                topWindow.Close();
            }
        }
        
        /// <summary>
        /// Limpa o histórico de navegação
        /// </summary>
        public void ClearHistory()
        {
            _navigationHistory.Clear();
        }
        
        /// <summary>
        /// Cria uma view baseada na página
        /// </summary>
        private UserControl? CreateView(AppPage page, object? parameter = null)
        {
            try
            {
                return page switch
                {
                    AppPage.Dashboard => new DashboardView(),
                    AppPage.Performance => new PerformanceView(),
                    AppPage.Network => new NetworkView(),
                    AppPage.System => new SystemView(),
                    AppPage.Gamer => new GamerView(),
                    AppPage.Diagnostics => new GameDiagnosticsView(),
                    AppPage.History => new HistoryView(),
                    AppPage.Scheduler => new SchedulerView(),
                    AppPage.Settings => new SettingsView(),
                    AppPage.Onboarding => new OnboardingView(),
            AppPage.ProfilerQuestionnaire => new Core.SystemIntelligenceProfiler.UI.Views.ProfilerQuestionnaireView(),
            AppPage.ProfilerSummary => null,
                      AppPage.LyraResponse => null,
                      AppPage.StreamHub => new VoltrisOptimizer.UI.Views.StreamHubView(),
                    AppPage.SmartRepair => new VoltrisOptimizer.UI.Views.SmartRepairView(),
                    AppPage.Shield => new ShieldView(),
                    AppPage.Security => new SecurityView(),
                    AppPage.Privacy => new PrivacyView(),
                    AppPage.Debloat => new DebloatView(),
                    AppPage.Recovery => new RecoveryView(),
                    AppPage.DeviceInfo => new DeviceInfoView(),
                    _ => null
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[NAV] Erro ao criar view {page}: {ex.Message}", ex);
                return null;
            }
        }
        
        /// <summary>
        /// Resolve view por string (legado)
        /// </summary>
        private object? ResolveViewLegacy(string key)
        {
            return key switch
            {
                "ProfilerFlow" => null,
                "Dashboard" => new DashboardView(),
                "Performance" => new PerformanceView(),
                "Network" => new NetworkView(),
                "System" => new SystemView(),
                "Gamer" => new GamerView(),
                "Diagnostics" => new GameDiagnosticsView(),
                "History" => new HistoryView(),
                "Scheduler" => new SchedulerView(),
                "Settings" => new SettingsView(),
                _ => null
            };
        }
    }
    
    /// <summary>
    /// Argumentos do evento de navegação
    /// </summary>
    public class NavigationEventArgs : EventArgs
    {
        public AppPage Page { get; }
        public object? Parameter { get; }
        
        public NavigationEventArgs(AppPage page, object? parameter = null)
        {
            Page = page;
            Parameter = parameter;
        }
    }
}
