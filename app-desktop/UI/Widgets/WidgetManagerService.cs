using System;
using System.Windows;
using System.Windows.Threading;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.UI.Widgets
{
    /// <summary>
    /// Serviço gerenciador do widget flutuante
    /// Responsável por criar, mostrar, esconder e destruir a janela do widget
    /// de forma determinística e thread-safe, sem causar deadlocks ou concorrência.
    /// </summary>
    public class WidgetManagerService : IDisposable
    {
        private readonly ILoggingService _logger;
        private readonly global::VoltrisOptimizer.VoltrisGlobalInsightService _insightService;
        private WidgetWindow? _widgetWindow;
        private readonly object _lock = new object();
        private bool _isDisposed;
        private volatile bool _isToggling;
        private volatile bool _isWidgetVisibleState;
        
        public event Action<bool>? VisibilityChanged;

        public bool IsWidgetVisible
        {
            get
            {
                lock (_lock)
                {
                    if (_widgetWindow == null)
                        return false;

                    if (_widgetWindow.Dispatcher.CheckAccess())
                    {
                        return _widgetWindow.IsVisible;
                    }

                    return _isWidgetVisibleState;
                }
            }
        }

        public WidgetManagerService(ILoggingService logger, global::VoltrisOptimizer.VoltrisGlobalInsightService insightService)
        {
            _logger = logger;
            _insightService = insightService;
            try
            {
                VoltrisDiagnosticSystem.Instance.Timeline("WIDGET", "WidgetManagerService: Instanciado");
            }
            catch { }

            VoltrisOptimizer.Services.LicenseManager.Instance.LicenseStatusChanged += OnLicenseStatusChanged;
        }

        /// <summary>
        /// Cria (se necessário) e mostra o widget
        /// </summary>
        public void ShowWidget()
        {
            if (_isDisposed)
                return;

            try
            {
                VoltrisDiagnosticSystem.Instance.Timeline("WIDGET", "ShowWidget: Chamado");
            }
            catch { }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            void Action()
            {
                if (_isDisposed) return;

                lock (_lock)
                {
                    if (_isToggling) return;
                    _isToggling = true;
                }

                try
                {
                    WidgetWindow window;
                    lock (_lock)
                    {
                        if (_widgetWindow == null)
                        {
                            _logger?.LogInfo("[WidgetManager] Criando nova instância do WidgetWindow...");
                            try
                            {
                                VoltrisDiagnosticSystem.Instance.Timeline("WIDGET", "ShowWidget: Criando nova instância do WidgetWindow...");
                            }
                            catch { }

                            _widgetWindow = new WidgetWindow(_logger, _insightService);
                            _widgetWindow.OpenDashboardRequested += OnOpenDashboardRequested;
                            _widgetWindow.Closed += (s, e) =>
                            {
                                lock (_lock)
                                {
                                    _widgetWindow = null;
                                    _isWidgetVisibleState = false;
                                }
                                _logger?.LogInfo("[WidgetManager] Widget fechado (evento Closed)");
                                try
                                {
                                    VoltrisDiagnosticSystem.Instance.Timeline("WIDGET", "WidgetWindow: Fechado");
                                }
                                catch { }
                                VisibilityChanged?.Invoke(false);
                            };
                        }
                        window = _widgetWindow;
                    }

                    // A geometria do widget já está resolvida no construtor
                    // (WidgetWindow.ApplyInitialGeometry), que mede a largura com
                    // restrição FINITA e posiciona a janela no monitor correto
                    // ANTES do primeiro Show(). Este serviço não deve mais calcular
                    // posição.
                    //
                    // BUG ORIGINAL: este bloco tentava recentralizar quando a janela
                    // ainda estava no placeholder off-screen, mas calculava
                    //     novoLeft = area.Left + (area.Width - window.Width) / 2
                    // com window.Width ainda sendo NaN (Width só é resolvido depois
                    // do primeiro layout). O resultado era Left = NaN, capturado
                    // pelo catch e engolido — a janela ficava com a geometria
                    // quebrada até o próximo passe de layout. Pior, se o catch não
                    // disparasse, o NaN contaminava o posicionamento final.
                    //
                    // A única verificação que sobra é uma garantia de sanidade: se a
                    // janela estiver com Left/Top inválidos, ela é recentralizada
                    // pelo próprio widget (que tem a medição correta), não aqui.
                    if (double.IsNaN(window.Left) || double.IsNaN(window.Top)
                        || window.Left <= -9000 || window.Top <= -9000)
                    {
                        _logger?.LogWarning(
                            $"[WidgetManager] Geometria invalida no momento de exibir " +
                            $"({window.Left:F1}, {window.Top:F1}); sera corrigida pelo proprio widget.");
                    }

                    window.Visibility = Visibility.Visible;
                    window.WindowState = WindowState.Normal;
                    if (!window.Topmost) window.Topmost = true;

                    window.Show();
                    _isWidgetVisibleState = true;

                    // CORREÇÃO FREEZE: ApplyTransparency NUNCA deve ser chamado sincronamente
                    // após Show() na UI Thread. O DWM precisa processar o Show() antes
                    // de aceitar novos SetWindowCompositionAttribute. Agendar com Background
                    // priority permite que o frame atual do Show() seja commitado primeiro.
                    bool transparencyEnabled = VoltrisOptimizer.Services.SettingsService.Instance?.Settings?.EnableTransparency ?? true;
                    window.Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            if (!_isDisposed && window.IsVisible)
                                window.ApplyTransparency(transparencyEnabled);
                        }
                        catch { }
                    }, DispatcherPriority.Background);

                    _logger?.LogInfo($"[WidgetManager] Widget exibido com sucesso em ({window.Left:F1}, {window.Top:F1})");
                    try
                    {
                        VoltrisDiagnosticSystem.Instance.Timeline("WIDGET", $"ShowWidget: Widget exibido com sucesso em ({window.Left:F1}, {window.Top:F1})");
                    }
                    catch { }
                    
                    VisibilityChanged?.Invoke(true);
                }
                catch (Exception ex)
                {
                    _logger?.LogError($"[WidgetManager] Erro ao exibir widget: {ex.Message}", ex);
                    try
                    {
                        VoltrisDiagnosticSystem.Instance.Timeline("WIDGET", $"ShowWidget: ERRO ao exibir widget: {ex.Message}");
                    }
                    catch { }
                }
                finally
                {
                    lock (_lock)
                    {
                        _isToggling = false;
                    }
                }
            }

            if (dispatcher.CheckAccess())
            {
                Action();
            }
            else
            {
                dispatcher.BeginInvoke(Action, DispatcherPriority.Normal);
            }
        }

        /// <summary>
        /// Esconde o widget (não destrói)
        /// </summary>
        public void HideWidget()
        {
            if (_isDisposed)
                return;

            try
            {
                VoltrisDiagnosticSystem.Instance.Timeline("WIDGET", "HideWidget: Chamado");
            }
            catch { }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            void Action()
            {
                if (_isDisposed) return;

                lock (_lock)
                {
                    if (_isToggling) return;
                    _isToggling = true;
                }

                try
                {
                    WidgetWindow? window;
                    lock (_lock)
                    {
                        window = _widgetWindow;
                    }

                    if (window != null && window.IsVisible)
                    {
                        window.Hide();
                        _isWidgetVisibleState = false;
                        _logger?.LogInfo("[WidgetManager] Widget escondido");
                        try
                        {
                            VoltrisDiagnosticSystem.Instance.Timeline("WIDGET", "HideWidget: Widget ocultado com sucesso");
                        }
                        catch { }
                        
                        VisibilityChanged?.Invoke(false);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError($"[WidgetManager] Erro ao esconder widget: {ex.Message}", ex);
                    try
                    {
                        VoltrisDiagnosticSystem.Instance.Timeline("WIDGET", $"HideWidget: ERRO ao esconder widget: {ex.Message}");
                    }
                    catch { }
                }
                finally
                {
                    lock (_lock)
                    {
                        _isToggling = false;
                    }
                }
            }

            if (dispatcher.CheckAccess())
            {
                Action();
            }
            else
            {
                dispatcher.BeginInvoke(Action, DispatcherPriority.Normal);
            }
        }

        /// <summary>
        /// Alterna visibilidade do widget de forma atômica
        /// </summary>
        public void ToggleWidget()
        {
            if (_isDisposed)
                return;

            if (IsWidgetVisible)
                HideWidget();
            else
                ShowWidget();
        }

        public void ToggleCollapseWidget()
        {
            if (_isDisposed)
                return;

            WidgetWindow? window;
            lock (_lock)
            {
                window = _widgetWindow;
            }

            if (window == null)
                return;

            if (window.Dispatcher.CheckAccess())
            {
                window.ToggleCollapse();
            }
            else
            {
                window.Dispatcher.BeginInvoke(window.ToggleCollapse, DispatcherPriority.Normal);
            }
        }

        /// <summary>
        /// Fecha e destrói o widget
        /// </summary>
        public void CloseWidget()
        {
            WidgetWindow? window;
            lock (_lock)
            {
                window = _widgetWindow;
                _widgetWindow = null;
                _isWidgetVisibleState = false;
            }

            if (window == null)
                return;

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            void Action()
            {
                try
                {
                    window.Close();
                    _logger?.LogInfo("[WidgetManager] Widget fechado e destruído");
                    VisibilityChanged?.Invoke(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning($"[WidgetManager] Erro ao fechar widget: {ex.Message}");
                }
            }

            if (dispatcher.CheckAccess())
            {
                Action();
            }
            else
            {
                dispatcher.BeginInvoke(Action, DispatcherPriority.Normal);
            }
        }

        /// <summary>
        /// Inicializa o widget se AutoStart estiver habilitado
        /// </summary>
        public void InitializeIfEnabled()
        {
            try
            {
                var config = WidgetConfig.Load();
                _logger?.LogInfo($"[WidgetManager] InitializeIfEnabled: AutoStart={config.AutoStart}, IsVisible={config.IsVisible}");
                try
                {
                    VoltrisDiagnosticSystem.Instance.Timeline("WIDGET", $"InitializeIfEnabled: AutoStart={config.AutoStart}, IsVisible={config.IsVisible}");
                }
                catch { }

                if (!config.AutoStart)
                {
                    _logger?.LogInfo("[WidgetManager] AutoStart desabilitado pelo usuário — widget não será iniciado");
                    try
                    {
                        VoltrisDiagnosticSystem.Instance.Timeline("WIDGET", "InitializeIfEnabled: AutoStart desabilitado — abortando inicialização");
                    }
                    catch { }
                    return;
                }

                ShowWidget();
                _logger?.LogInfo("[WidgetManager] Widget iniciado automaticamente");
                try
                {
                    VoltrisDiagnosticSystem.Instance.Timeline("WIDGET", "InitializeIfEnabled: Chamada ShowWidget realizada");
                }
                catch { }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[WidgetManager] Erro na inicialização: {ex.Message}", ex);
                try
                {
                    VoltrisDiagnosticSystem.Instance.Timeline("WIDGET", $"InitializeIfEnabled: ERRO na inicialização: {ex.Message}");
                }
                catch { }
            }
        }

        private void OnOpenDashboardRequested(object? sender, EventArgs e)
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null) return;

                dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        var mainWindow = Application.Current?.MainWindow;
                        if (mainWindow != null)
                        {
                            mainWindow.Show();
                            mainWindow.WindowState = WindowState.Normal;
                            mainWindow.Activate();
                            _logger?.LogInfo("[WidgetManager] Dashboard aberto via widget");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError($"[WidgetManager] Erro ao abrir dashboard (UI): {ex.Message}");
                    }
                }, DispatcherPriority.Normal);
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[WidgetManager] Erro ao abrir dashboard: {ex.Message}");
            }
        }

        /// <summary>
        /// Atualiza o estado da transparência do widget em tempo real
        /// </summary>
        public void UpdateTransparency(bool enabled)
        {
            try
            {
                WidgetWindow? window;
                lock (_lock)
                {
                    window = _widgetWindow;
                }

                if (window != null)
                {
                    if (window.Dispatcher.CheckAccess())
                    {
                        window.ApplyTransparency(enabled);
                    }
                    else
                    {
                        window.Dispatcher.BeginInvoke(() =>
                        {
                            try
                            {
                                window.ApplyTransparency(enabled);
                                _logger?.LogInfo($"[WIDGET-MANAGER] Transparência atualizada via Manager: {enabled}");
                            }
                            catch (Exception ex)
                            {
                                _logger?.LogError($"[WIDGET-MANAGER] Erro ao aplicar transparência na janela: {ex.Message}");
                            }
                        }, DispatcherPriority.Normal);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[WIDGET-MANAGER] Erro ao disparar atualização de transparência: {ex.Message}");
            }
        }

        private void OnLicenseStatusChanged(object? sender, EventArgs e)
        {
            // FREEMIUM: widgets são gratuitos — não são fechados por licença.
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            VoltrisOptimizer.Services.LicenseManager.Instance.LicenseStatusChanged -= OnLicenseStatusChanged;
            CloseWidget();
            _logger?.LogInfo("[WidgetManager] Serviço destruído");
        }
    }
}

