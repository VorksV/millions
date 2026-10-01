using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using VoltrisOptimizer.Core.Updater;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.UI.Controls;
using VoltrisOptimizer.UI.Windows;
using VoltrisOptimizer.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using VoltrisOptimizer.Services.Optimization;
using VoltrisOptimizer.Services.UpdateFlow;
using VoltrisOptimizer.Services.Gamer.Interfaces;
using Language = VoltrisOptimizer.Services.Language;
using System.Security.Principal;
using System.ComponentModel;
using VoltrisOptimizer.Services.Cloud;

namespace VoltrisOptimizer.UI.Views
{
    public partial class SettingsView : UserControl
    {
        private readonly LocalizationService _localization;
        private readonly SettingsService _settings;
        private readonly IDialogService? _dialogs;
        private readonly ILoggingService? _logger;
        private readonly StartupManager _startupManager;

        public string TitleText => _localization.GetString("Settings");
        public string LanguageSectionTitle => _localization.GetString("Language");
        public string StartupSectionTitle => _localization.GetString("StartWithWindows");
        public string BehaviorSectionTitle => _localization.GetString("AdvancedSettings");
        
        public string PortugueseText => _localization.GetString("Portuguese");
        public string SpanishText => _localization.GetString("Spanish");
        public string EnglishText => _localization.GetString("English");
        
        public string StartWithWindowsText => _localization.GetString("StartWithWindows");
        public string StartMinimizedText => _localization.GetString("StartMinimized");
        public string MinimizeToTrayText => _localization.GetString("MinimizeToTray");
        public string CloseToTrayText => _localization.GetString("CloseToTray");
        
        public string IntelligentProfileTitle => _localization.GetString("IntelligentProfile");
        public string IntelligentProfileDesc => _localization.GetString("IntelligentProfileDesc");
        public string ProfileGamerCompetitiveText => _localization.GetString("ProfileGamerCompetitive");
        public string ProfileGamerSinglePlayerText => _localization.GetString("ProfileGamerSinglePlayer");
        public string ProfileGamerSimulationText => _localization.GetString("ProfileGamerSimulation");
        public string ProfileGamerMMOText => _localization.GetString("ProfileGamerMMO");
        public string ProfileGamerStrategyText => _localization.GetString("ProfileGamerStrategy");
        public string ProfileWorkOfficeText => _localization.GetString("ProfileWorkOffice");
        public string ProfileCreativeVideoEditingText => _localization.GetString("ProfileCreativeVideoEditing");
        public string ProfileDeveloperProgrammingText => _localization.GetString("ProfileDeveloperProgramming");
        public string ProfileGeneralBalancedText => _localization.GetString("ProfileGeneralBalanced");
        public string ProfileEnterpriseSecureText => _localization.GetString("ProfileEnterpriseSecure");
        

        
        public bool StartWithWindowsChecked
        {
            get => _settings.Settings.StartWithWindows;
            set
            {
                _settings.Settings.StartWithWindows = value;
                _settings.SaveSettings();
            }
        }
        
        public bool StartMinimizedChecked
        {
            get => _settings.Settings.StartMinimized;
            set
            {
                _settings.Settings.StartMinimized = value;
                _settings.SaveSettings();
            }
        }
        
        public bool MinimizeToTrayChecked
        {
            get => _settings.Settings.MinimizeToTray;
            set
            {
                _settings.Settings.MinimizeToTray = value;
                _settings.SaveSettings();
            }
        }
        
        public bool CloseToTrayChecked
        {
            get => _settings.Settings.CloseToTray;
            set
            {
                _settings.Settings.CloseToTray = value;
                _settings.SaveSettings();
            }
        }

        private bool _isLoading = true; // Flag para evitar disparar eventos durante carregamento
        
        public SettingsView()
        {
            // CRITICAL FIX: Ensure Application resources are loaded before InitializeComponent
            if (Application.Current == null)
            {
                throw new InvalidOperationException("Application.Current is null. SettingsView must be created after Application initialization.");
            }

            // Verify that required resource dictionaries are loaded
            try
            {
                // Test if key resources are available
                var testBrush = Application.Current.TryFindResource("DarkBrush");
                if (testBrush == null)
                {
                    System.Diagnostics.Debug.WriteLine("WARNING: DarkBrush resource not found. Resources may not be fully loaded.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WARNING: Error checking resources: {ex.Message}");
            }

            // CORREÇÃO CRÍTICA: Inicializar serviços ANTES de InitializeComponent
            _localization = LocalizationService.Instance;
            _settings = SettingsService.Instance;
            try { _dialogs = App.Services?.GetService<IDialogService>(); } catch { }
            try { _logger = App.Services?.GetService<ILoggingService>(); } catch { }
            _startupManager = new StartupManager(_logger);
            
            // CORREÇÃO CRÍTICA: Definir DataContext ANTES de InitializeComponent para evitar erros de binding
            DataContext = this;

            try
            {
                InitializeComponent();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CRITICAL ERROR in InitializeComponent: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
                
                // Log to file for debugging
                try
                {
                    var logDir = LogDirectoryResolver.Resolve();
                    if (!System.IO.Directory.Exists(logDir))
                        System.IO.Directory.CreateDirectory(logDir);
                    System.IO.File.AppendAllText(
                        System.IO.Path.Combine(logDir, "settingsview_error.log"),
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] InitializeComponent Error:\n{ex.Message}\n{ex.StackTrace}\n\n",
                        System.Text.Encoding.UTF8
                    );
                }
                catch { }
                
                throw; // Re-throw para que o erro seja capturado pelo MainWindow
            }
            
            // Carregar estado atual SEM disparar eventos
            _isLoading = true;
            
            // CORREÇÃO CRÍTICA: Mover TODA inicialização de controles para o evento Loaded
            Loaded += async (s, e) =>
            {
                try
                {
                    // Apenas refletir o estado real do sistema — evita schtasks pesado no carregamento
                    var isEnabled = await Task.Run(() => _startupManager.IsStartupEnabled()).ConfigureAwait(true);
                    if (CheckStartWithWindows != null)
                        CheckStartWithWindows.IsChecked = isEnabled;
                    _settings.Settings.StartWithWindows = isEnabled;
                    _settings.SaveSettings();
                }
                catch (Exception ex)
                {
                    // Em caso de erro, refletir o estado real do sistema
                    try
                    {
                        var isEnabled = await Task.Run(() => _startupManager.IsStartupEnabled());
                        if (CheckStartWithWindows != null)
                            CheckStartWithWindows.IsChecked = isEnabled;
                        _settings.Settings.StartWithWindows = isEnabled;
                        _settings.SaveSettings();
                    }
                    catch
                    {
                        // Se tudo falhar, usar o valor das configurações
                        if (CheckStartWithWindows != null)
                            CheckStartWithWindows.IsChecked = _settings.Settings.StartWithWindows;
                    }
                }
                
                // INICIALIZAR TODOS OS CONTROLES AQUI (após Loaded)
                try
                {
                    if (CheckStartMinimized != null)
                        CheckStartMinimized.IsChecked = _settings.Settings.StartMinimized;
                    if (CheckMinimizeToTray != null)
                        CheckMinimizeToTray.IsChecked = _settings.Settings.MinimizeToTray;
                    if (CheckCloseToTray != null)
                        CheckCloseToTray.IsChecked = _settings.Settings.CloseToTray;
                    
                    var extreme = App.ExtremeOptimizations;
                    if (extreme != null)
                    {
                        if (CheckDryRun != null)
                            CheckDryRun.IsChecked = extreme.DryRun;
                        if (CheckAllowWatchdog != null)
                            CheckAllowWatchdog.IsChecked = extreme.AllowBackgroundWatchdog;
                    }
                    
                    // Carregar estado do menu de contexto do desktop
                    if (CheckDesktopContextMenu != null)
                        CheckDesktopContextMenu.IsChecked = _settings.Settings.EnableDesktopContextMenu;
                    
                    // Carregar estado da transparência
                    if (TransparencyToggle != null)
                        TransparencyToggle.IsChecked = _settings.Settings.EnableTransparency;
                    
                    // Carregar estado das notificações
                    if (CheckNotificationsEnabled != null)
                        CheckNotificationsEnabled.IsChecked = _settings.Settings.NotificationsEnabled;
                    if (CheckNotifyThreats != null)
                        CheckNotifyThreats.IsChecked = _settings.Settings.NotifyOnThreatDetected;
                    if (CheckNotifyScanComplete != null)
                        CheckNotifyScanComplete.IsChecked = _settings.Settings.NotifyOnScanComplete;
                    if (CheckNotifyNewDevice != null)
                        CheckNotifyNewDevice.IsChecked = _settings.Settings.NotifyOnNewDevice;
                    if (CheckToastMuted != null)
                        CheckToastMuted.IsChecked = _settings.Settings.ToastNotificationsMuted;

                    // Carregar estado dos toggles Beta
                    if (ToggleShowDrivers != null)
                        ToggleShowDrivers.IsChecked = _settings.Settings.ShowDriversPage;
                    if (ToggleShowRecovery != null)
                        ToggleShowRecovery.IsChecked = _settings.Settings.ShowRecoveryPage;
                    
                    // Selecionar tema atual
                    var dicts = Application.Current.Resources.MergedDictionaries;
                    var hasLight = false;
                    foreach (var d in dicts)
                    {
                        var src = d.Source?.ToString() ?? string.Empty;
                        if (src.EndsWith("LightTheme.xaml", StringComparison.OrdinalIgnoreCase))
                        {
                            hasLight = true;
                            break;
                        }
                    }
                    if (!hasLight && _settings.Settings.Theme == "Light")
                    {
                        hasLight = true;
                    }
                    if (ThemeCombo != null)
                        ThemeCombo.SelectedIndex = hasLight ? 1 : 0;

                    // Configurar idioma
                    UpdateLanguageComboSelection();
                    UpdateAllTexts();

                    // Atualizar versão
                    UpdateVersionInfo();
                }
                catch (Exception initEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Erro ao inicializar controles: {initEx.Message}");
                }
                
                // Atualizar Status Admin
                try
                {
                    bool isAdmin = IsRunningAsAdministrator();
                    if (isAdmin)
                    {
                        if (AdminStatusText != null)
                        {
                            AdminStatusText.Text = _localization.GetString("AdminElevated");
                            try { AdminStatusText.Foreground = (System.Windows.Media.Brush)Application.Current.Resources["SuccessBrush"]; } catch { }
                        }
                        if (RestartAdminButton != null)
                            RestartAdminButton.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        if (AdminStatusText != null)
                        {
                            AdminStatusText.Text = _localization.GetString("StandardUser");
                            try { AdminStatusText.Foreground = (System.Windows.Media.Brush)Application.Current.Resources["TextSecondaryBrush"]; } catch { }
                        }
                        if (RestartAdminButton != null)
                            RestartAdminButton.Visibility = Visibility.Visible;
                    }
                }
                catch { }

                _isLoading = false;
                
                // Subscrever ao evento de mudança de estado da conta para atualizações em tempo real
                CloudAccountService.Instance.AccountStateChanged += OnCloudAccountStateChanged;
                
                // Atualizar status da conta
                _ = UpdateAccountStatusAsync();
            };
            
            Unloaded += (s, e) =>
            {
                CloudAccountService.Instance.AccountStateChanged -= OnCloudAccountStateChanged;
            };
            
            // Configurar evento de mudança de idioma
            _localization.LanguageChanged += (s, e) => UpdateAllTexts();
            
            // Reagir a mudanças de estado da conta cloud em tempo real
            CloudAccountService.Instance.AccountStateChanged += OnCloudAccountStateChanged;

            Unloaded += (s, e) =>
            {
                CloudAccountService.Instance.AccountStateChanged -= OnCloudAccountStateChanged;
            };
        }

        

        private void UpdateLanguageComboSelection()
        {
            try
            {
                var currentLang = _settings.Settings.Language;
                if (LanguageCombo == null) return;
                LanguageCombo.SelectedIndex = currentLang switch
                {
                    VoltrisOptimizer.Services.Language.Portuguese => 0,
                    VoltrisOptimizer.Services.Language.Spanish => 1,
                    VoltrisOptimizer.Services.Language.English => 2,
                    _ => 0
                };
            }
            catch { }
        }

        private void UpdateAllTexts()
        {
            try
            {
                // Atualizar textos do dropdown de idioma via binding
                DataContext = null;
                DataContext = this;
            }
            catch { }
        }

        private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // CORREÇÃO: Verificar se está carregando ou se os controles estão nulos
            if (_isLoading || LanguageCombo == null || _settings == null || _localization == null) return;

            var selectedLang = LanguageCombo.SelectedIndex switch
            {
                0 => VoltrisOptimizer.Services.Language.Portuguese,
                1 => VoltrisOptimizer.Services.Language.Spanish,
                2 => VoltrisOptimizer.Services.Language.English,
                _ => (VoltrisOptimizer.Services.Language?)null
            };

            if (selectedLang == null) return;

            _settings.Settings.Language = selectedLang.Value;
            _localization.SetLanguage(selectedLang.Value);
            _settings.SaveSettings();
            UpdateAllTexts();
        }

        private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            try
            {
                var selected = (ThemeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Dark";
                var isLight = selected.Contains("Light", StringComparison.OrdinalIgnoreCase);
                _settings.Settings.Theme = isLight ? "Light" : "Dark";
                _settings.SaveSettings();
                ApplyThemeAndTransparency(_settings.Settings.Theme, _settings.Settings.EnableTransparency);
                
                // Atualizar ícones do header em tempo real
                SyncHeaderIcons();
                
                App.TelemetryService?.TrackEvent("SETTINGS_THEME_CHANGE", "Settings", "Change", metadata: new { Theme = _settings.Settings.Theme });
            }
            catch { }
        }

        private async void CheckStartWithWindows_Checked(object sender, RoutedEventArgs e)
        {
            if (_isLoading)
                return;

            CheckStartWithWindows.IsEnabled = false;
            try
            {
                var startMinimized = CheckStartMinimized.IsChecked == true;

                await Task.Run(() => _startupManager.SetStartup(true, startMinimized)).ConfigureAwait(true);

                _settings.Settings.StartWithWindows = true;
                _settings.Settings.StartMinimized = startMinimized;
                await Task.Run(() => _settings.SaveSettings()).ConfigureAwait(true);

                App.TelemetryService?.TrackEvent("SETTINGS_STARTUP_TOGGLE", "Settings", "Change", metadata: new { Enabled = true });
            }
            catch (Exception ex)
            {
                CheckStartWithWindows.IsChecked = false;
                _settings.Settings.StartWithWindows = false;
                await Task.Run(() => _settings.SaveSettings()).ConfigureAwait(true);
                
                // Logar erro para debug
                try
                {
                    var logDir = LogDirectoryResolver.Resolve();
                    if (!Directory.Exists(logDir))
                        Directory.CreateDirectory(logDir);
                    File.AppendAllText(
                        Path.Combine(logDir, "startup_error.log"),
                        $"[{DateTime.Now}] Erro ao habilitar startup: {ex.Message}\n{ex.StackTrace}\n\n"
                    );
                }
                catch { }
                
                // Mostrar erro ao usuário
                _dialogs?.ShowError(_localization.GetString("EnableStartupError"), 
                    string.Format(_localization.GetString("EnableStartupErrorDetail"), ex.Message));
            }
            finally
            {
                CheckStartWithWindows.IsEnabled = true;
            }
        }
        
        private string? GetExecutablePath()
        {
            // CRÍTICO: Tentar múltiplos métodos para encontrar o executável
            // Priorizar ProcessPath pois é o mais confiável
            var paths = new[]
            {
                // 1. ProcessPath - mais confiável, sempre retorna o .exe real
                System.Environment.ProcessPath,
                
                // 2. Assembly Location (pode retornar .dll em alguns casos)
                System.Reflection.Assembly.GetExecutingAssembly().Location,
                
                // 3. BaseDirectory + nome do executável
                System.IO.Path.Combine(System.AppContext.BaseDirectory, "VoltrisOptimizer.exe"),
                
                // 4. Locais de instalação padrão
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Voltris Optimizer", "VoltrisOptimizer.exe"),
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Voltris Optimizer", "VoltrisOptimizer.exe"),
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Voltris Optimizer", "VoltrisOptimizer.exe")
            };
            
            // Logar tentativas
            try
            {
                var logDir = LogDirectoryResolver.Resolve();
                if (!System.IO.Directory.Exists(logDir))
                    System.IO.Directory.CreateDirectory(logDir);
                var logFile = System.IO.Path.Combine(logDir, "path_detection.log");
                System.IO.File.AppendAllText(logFile, 
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ===== DETECÇÃO DE EXECUTÁVEL =====\n");
                
                foreach (var path in paths)
                {
                    if (string.IsNullOrEmpty(path))
                    {
                        System.IO.File.AppendAllText(logFile, $"  Tentando: (null)\n");
                        continue;
                    }
                    
                    // Normalizar o caminho
                    var normalizedPath = System.IO.Path.GetFullPath(path);
                    System.IO.File.AppendAllText(logFile, $"  Tentando: {normalizedPath}\n");
                    
                    // Verificar se existe e é .exe
                    if (System.IO.File.Exists(normalizedPath))
                    {
                        // Se for .dll, tentar converter para .exe (para casos onde Assembly.Location retorna .dll)
                        if (normalizedPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        {
                            var exePath = normalizedPath.Replace(".dll", ".exe", StringComparison.OrdinalIgnoreCase);
                            if (System.IO.File.Exists(exePath))
                            {
                                System.IO.File.AppendAllText(logFile, $"  ✓ ENCONTRADO (convertido de .dll): {exePath}\n");
                                System.IO.File.AppendAllText(logFile, $"===== FIM DETECÇÃO =====\n\n");
                                return exePath;
                            }
                        }
                        else if (normalizedPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            System.IO.File.AppendAllText(logFile, $"  ✓ ENCONTRADO: {normalizedPath}\n");
                            System.IO.File.AppendAllText(logFile, $"  Tamanho: {new System.IO.FileInfo(normalizedPath).Length} bytes\n");
                            System.IO.File.AppendAllText(logFile, $"===== FIM DETECÇÃO =====\n\n");
                            return normalizedPath;
                        }
                    }
                }
                
                System.IO.File.AppendAllText(logFile, "  ✗ NENHUM CAMINHO ENCONTRADO!\n");
                System.IO.File.AppendAllText(logFile, $"===== FIM DETECÇÃO (SEM SUCESSO) =====\n\n");
            }
            catch (Exception ex)
            {
                try
                {
                    var logDir = LogDirectoryResolver.Resolve();
                    var logFile = System.IO.Path.Combine(logDir, "path_detection.log");
                    System.IO.File.AppendAllText(logFile, $"ERRO ao detectar caminho: {ex.Message}\n");
                }
                catch { }
            }
            
            return null;
        }

        private async void CheckStartWithWindows_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isLoading)
                return;

            CheckStartWithWindows.IsEnabled = false;
            try
            {
                await Task.Run(() => _startupManager.SetStartup(false, false)).ConfigureAwait(true);

                _settings.Settings.StartWithWindows = false;
                await Task.Run(() => _settings.SaveSettings()).ConfigureAwait(true);

                App.TelemetryService?.TrackEvent("SETTINGS_STARTUP_TOGGLE", "Settings", "Change", metadata: new { Enabled = false });
            }
            catch (Exception ex)
            {
                CheckStartWithWindows.IsChecked = true;
                _settings.Settings.StartWithWindows = true;
                await Task.Run(() => _settings.SaveSettings()).ConfigureAwait(true);
                
                _dialogs?.ShowError(_localization.GetString("DisableStartupError"), 
                    string.Format(_localization.GetString("DisableStartupErrorDetail"), ex.Message));
            }
            finally
            {
                CheckStartWithWindows.IsEnabled = true;
            }
        }
        
        private async void CheckStartMinimized_Checked(object sender, RoutedEventArgs e)
        {
            // Ignorar se estiver carregando
            if (_isLoading)
                return;
                
            // Quando o usuário marca "Iniciar minimizado", atualizar as configurações
            _settings.Settings.StartMinimized = true;
            _settings.SaveSettings();
            
            // Se já está configurado para iniciar com Windows, atualizar o registro automaticamente
            if (CheckStartWithWindows.IsChecked == true)
            {
                try
                {
                    await Task.Run(() => _startupManager.SetStartup(true, true));
                }
                catch
                {
                    // Ignorar erros silenciosamente
                }
            }
        }
        
        private async void CheckStartMinimized_Unchecked(object sender, RoutedEventArgs e)
        {
            // Ignorar se estiver carregando
            if (_isLoading)
                return;
                
            // Quando o usuário desmarca "Iniciar minimizado", atualizar as configurações
            _settings.Settings.StartMinimized = false;
            _settings.SaveSettings();
            
            // Se já está configurado para iniciar com Windows, atualizar o registro automaticamente
            if (CheckStartWithWindows.IsChecked == true)
            {
                try
                {
                    await Task.Run(() => _startupManager.SetStartup(true, false));
                }
                catch
                {
                    // Ignorar erros silenciosamente
                }
            }
        }

        private void CheckDesktopContextMenu_Checked(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            _settings.Settings.EnableDesktopContextMenu = true;
            _settings.SaveSettings();
            try
            {
                var ctxMenu = App.Services?.GetService(typeof(VoltrisOptimizer.Services.Shell.DesktopContextMenuService)) as VoltrisOptimizer.Services.Shell.DesktopContextMenuService;
                if (ctxMenu != null)
                {
                    ctxMenu.RegisterWithSubMenus();
                    App.LoggingService?.LogInfo("[Settings] Menu de contexto registrado via checkbox");
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[Settings] Erro ao registrar menu de contexto: {ex.Message}");
            }
        }

        private void CheckDesktopContextMenu_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            _settings.Settings.EnableDesktopContextMenu = false;
            _settings.SaveSettings();
            try
            {
                var ctxMenu = App.Services?.GetService(typeof(VoltrisOptimizer.Services.Shell.DesktopContextMenuService)) as VoltrisOptimizer.Services.Shell.DesktopContextMenuService;
                if (ctxMenu != null)
                {
                    ctxMenu.Unregister();
                    App.LoggingService?.LogInfo("[Settings] Menu de contexto removido via checkbox");
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[Settings] Erro ao remover menu de contexto: {ex.Message}");
            }
        }

        

        
        



        private void CheckDryRun_Checked(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            try
            {
                var extreme = App.ExtremeOptimizations;
                if (extreme == null) return;
                extreme.DryRun = true;
                _dialogs?.ShowInfo(_localization.GetString("DryRunEnabledTitle"), _localization.GetString("DryRunEnabledMsg"));
            }
            catch (Exception ex)
            {
                _dialogs?.ShowError(_localization.GetString("Error"), string.Format(_localization.GetString("DryRunEnableError"), ex.Message));
            }
        }

        private void CheckDryRun_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            try
            {
                var confirm = ModernMessageBox.Show(
                    _localization.GetString("ConfirmRealChangesMsg"),
                    _localization.GetString("ConfirmRealChangesTitle"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (confirm != MessageBoxResult.Yes)
                {
                    CheckDryRun.IsChecked = true;
                    return;
                }
                var extreme = App.ExtremeOptimizations;
                if (extreme == null) return;
                extreme.DryRun = false;
                _dialogs?.ShowInfo(_localization.GetString("DryRunDisabledTitle"), _localization.GetString("DryRunDisabledMsg"));
            }
            catch (Exception ex)
            {
                _dialogs?.ShowError(_localization.GetString("Error"), string.Format(_localization.GetString("DryRunDisableError"), ex.Message));
                CheckDryRun.IsChecked = true;
            }
        }

        private void CheckAllowWatchdog_Checked(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            try
            {
                _settings.Settings.AllowBackgroundDpcWatchdog = true;
                _settings.SaveSettings();
                
                var extreme = App.ExtremeOptimizations;
                if (extreme != null)
                {
                    extreme.AllowBackgroundWatchdog = true;
                    extreme.StartDpcWatchdog();
                }
            }
            catch { }
        }

        private void CheckAllowWatchdog_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            try
            {
                _settings.Settings.AllowBackgroundDpcWatchdog = false;
                _settings.SaveSettings();
                
                var extreme = App.ExtremeOptimizations;
                if (extreme != null)
                {
                    extreme.AllowBackgroundWatchdog = false;
                    extreme.StopDpcWatchdog();
                }
            }
            catch { }
        }


        
        // ============================================
        // ATUALIZAÇÕES
        // ============================================
        
        private void UpdateVersionInfo()
        {
            try
            {
                var currentVersion = UpdateService.GetCurrentVersion();
                CurrentVersionText.Text = $"{_localization.GetString("CurrentVersionLabel")}{currentVersion}";
                UpdateStatusText.Text = _localization.GetString("ClickToCheckUpdates");
            }
            catch (Exception ex)
            {
                CurrentVersionText.Text = $"{_localization.GetString("CurrentVersionLabel")}{_localization.GetString("ErrorObtaining")}";
                UpdateStatusText.Text = $"{_localization.GetString("Error")}: {ex.Message}";
            }
        }
        
        private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Desabilitar botão durante verificação
                CheckUpdatesButton.IsEnabled = false;
                CheckUpdatesButton.Content = $"🔄 {_localization.GetString("CheckingUpdatesMessage")}";
                UpdateStatusText.Text = _localization.GetString("CheckingUpdatesMessage");
                
                // [UPDATE-FLUXO] A VERIFICAÇÃO NÃO MUDOU. SÓ MUDOU O DESTINO.
                //
                // Antes: achou atualização -> `new UpdateWindow(...).ShowDialog()`.
                // Agora: achou atualização -> o botão circular do Dashboard assume.
                //
                // A chamada é a MESMA de sempre, e é o que garante que quem usa
                // uma versão antiga continua enxergando a nova:
                // `UpdateService.CheckForUpdatesAsync()` NÃO FOI TOCADO. Ele
                // continua sendo quem decide SE existe atualização e QUAL é a
                // versão — este arquivo só decide o que fazer DEPOIS.
                //
                // `UpdateFlowController` é o dono do fluxo a partir daqui.
                //
// [FIX:BOTAO-MORTO-POR-ESPERA] O GUARDA QUE IMPEDIA O CLIQUE FOI REMOVIDO.
                // =======================================================
                // Havia aqui um `if (IsRunning) { texto; return; }`. Era uma
                // ideia razoável na minha cabeça e um desastre na sua tela: se
                // a trava interna ficasse presa por qualquer motivo, o botão
                // parava de responder PARA SEMPRE, e nada no programa permitiria
                // recuperar.
                //
                // Um botão que às vezes não responde é pior do que um botão
                // que responde dizendo a verdade. O clique agora SEMPRE é
                // tentado, e é o resultado que decide a mensagem — inclusive
                // quando a resposta é "já estou ocupado".
                UpdateRunOutcome outcome = await UpdateFlowController.RunAsync(simulate: false, logger: _logger);

                if (outcome == UpdateRunOutcome.Started)
                {
                    UpdateStatusText.Text =
                        string.Format(_localization.GetString("UpdateDetectedVersion"), UpdateFlowState.LatestVersion);

                    // [UPDATE-FLUXO] LEVA O USUÁRIO AO BOTÃO CIRCULAR.
                    //
                    // O botão circular fica no Dashboard, e esconder a
                    // atualização numa tela em que ele não está seria
                    // contraditório: o usuário leria "atualizando" no texto e
                    // não veria nenhum progresso acontecendo.
                    if (Application.Current.MainWindow is MainWindow mw)
                    {
                        mw.NavigateToDashboard();
                    }
                }
                else
                {
//[FIX:MENTIRA-NA-RECUSA] "ATUALIZADO" SÓ QUANDO É VERDADE.
                //
                // Chegou até aqui sem `IsRunning` no início, então o `false`
                // significa "não achou nada" ou "falhou". Se outra execução
                // começou entre a consulta e o retorno, o texto de ocupado
                // continua sendo o certo.
                UpdateStatusText.Text = outcome switch
                {
                    UpdateRunOutcome.AlreadyRunning =>
                        _localization.GetString("UpdateFlowAlreadyRunning"),

                    UpdateRunOutcome.Failed =>
                        $"{_localization.GetString("ErrorCheckingUpdates")}. {_localization.GetString("Error")}",

                    _ => _localization.GetString("UpToDateMessage")
                };
                // Não mostrar diálogo automático, apenas atualizar o texto
                // _dialogs?.ShowInfo("Atualizado", $"Você já está usando a versão mais recente ({UpdateService.GetCurrentVersion()}).");
                }
            }
            catch (Exception ex)
            {
                _dialogs?.ShowError(_localization.GetString("Error"), $"{_localization.GetString("ErrorCheckingUpdates")}: {ex.Message}");
                UpdateStatusText.Text = $"{_localization.GetString("Error")}: {ex.Message}";
            }
            finally
            {
                // Restaurar botão
                CheckUpdatesButton.IsEnabled = true;
                CheckUpdatesButton.Content = $"🔍 {_localization.GetString("CheckUpdatesButton")}";
            }
        }

        /// <summary>
        /// [UPDATE-FLUXO] COMO TESTAR SEM ENCOSTAR NO GITHUB.
        ///
        /// Roda o fluxo inteiro — botão circular, arco, barra global, trava de
        /// interface e os textos nos três idiomas — com progresso sintético.
        ///
        /// O que NÃO acontece: nenhuma chamada de rede, nenhum download,
        /// nenhum instalador. É o único jeito de ver o estado "100%, aguardando
        /// autorização", porque uma atualização de verdade chega nesse estado e
        /// FECHA O PROGRAMA na linha seguinte.
        ///
        /// Não altera nenhum arquivo do projeto: é um comando de teste.
        /// </summary>
        private async void SimulateUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // [FIX:BOTAO-MORTO-POR-ESPERA] A SIMULAÇÃO TAMBÉM NÃO PODE VOLTAR CEDO.
                // ==========================================================
                // Este botão recebia `false` e não fazia NADA — um clique sem
                // resposta é indistinguível de um botão morto. Pior: eu tinha
                // colocado aqui um guarda que devolvia cedo, que é a regra
                // exata que transformou a flag travada em botão morto.
                //
                // A simulação é o botão de TESTE. Se ele tem uma regra
                // diferente dos outros, ele mente justamente na hora em que o
                // usuário mais precisa dele funcionando.
                UpdateStatusText.Text = _localization.GetString("UpdateFlowSimulation");

                UpdateRunOutcome outcome = await UpdateFlowController.RunAsync(simulate: true, logger: _logger);

                // [FIX:NAVEGACAO-SO-QUANDO-COMEÇOU] Só navega se o download
                // realmente começou. Se o programa já estava ocupado, a tela
                // continuaria em Configurações e o usuário veria apenas um
                // texto, sem botão circular e sem barra — que é exatamente a
                // reclamação que motivou esta correção.
                if (outcome == UpdateRunOutcome.Started)
                {
                    if (Application.Current.MainWindow is MainWindow mw)
                    {
                        mw.NavigateToDashboard();
                    }
                }
                else if (outcome == UpdateRunOutcome.AlreadyRunning)
                {
                    UpdateStatusText.Text = _localization.GetString("UpdateFlowAlreadyRunning");
                }
            }
            catch (Exception ex)
            {
                UpdateStatusText.Text = $"{_localization.GetString("Error")}: {ex.Message}";
            }
        }

        private void SupportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var url = "https://wa.me/5511996716235";
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                _dialogs?.ShowError(_localization.GetString("Error"), string.Format(_localization.GetString("OpenLinkError"), ex.Message));
            }
        }

        private void OnCloudAccountStateChanged(object? sender, AccountStateChangedEventArgs args)
        {
            Dispatcher.InvokeAsync(() =>
            {
                UpdateSections(args.IsLinked, args.Email);
            });
        }

        private void UpdateSections(bool isLinked, string? email)
        {
            if (AccountSection == null || LinkAccountSection == null) return;

            if (isLinked)
            {
                AccountSection.Visibility = Visibility.Visible;
                LinkAccountSection.Visibility = Visibility.Collapsed;
                if (AccountEmailText != null)
                    AccountEmailText.Text = $"{_localization.GetString("LinkedTo")} {email ?? _localization.GetString("UserWord")}";
            }
            else
            {
                AccountSection.Visibility = Visibility.Collapsed;
                LinkAccountSection.Visibility = Visibility.Visible;
            }
        }

        private async void UnlinkDevice_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = ModernMessageBox.Show(
                    _localization.GetString("ConfirmUnlinkMsg"),
                    _localization.GetString("ConfirmUnlinkTitle"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning
                );

                if (result != MessageBoxResult.Yes)
                    return;

                if (UnlinkButton != null)
                    UnlinkButton.IsEnabled = false;

                // REGRA 19: so declara sucesso depois de confirmacao real do
                // backend. Antes o retorno era descartado e a UI deslogava +
                // mostrava "sucesso" mesmo com o servidor recusando (401), e o
                // poll devolvia a conta 30 s depois.
                var unlinked = await CloudAccountService.Instance.UnlinkDeviceAsync();

                if (!unlinked)
                {
                    _logger?.Log(LogLevel.Warning, LogCategory.System,
                        "Desvinculacao recusada pelo servidor; conta local preservada.");
                    ModernMessageBox.Show(
                        string.Format(
                            _localization.GetString("UnlinkErrorMsg"),
                            LocalizationService.Instance.GetString("UnlinkNotConfirmedReason")),
                        _localization.GetString("UnlinkErrorTitle"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                    return;
                }

                UpdateSections(false, null);

                if (Application.Current.MainWindow is MainWindow mainWindow)
                    mainWindow.ForceUpdateLinkingStatus();

                ModernMessageBox.Show(
                    _localization.GetString("UnlinkSuccessMsg"),
                    _localization.GetString("UnlinkSuccessTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );

                _logger?.Log(LogLevel.Info, LogCategory.System, "Dispositivo desvinculado com sucesso");
            }
            catch (Exception ex)
            {
                _logger?.Log(LogLevel.Error, LogCategory.System, $"Erro ao desvincular dispositivo: {ex.Message}");
                ModernMessageBox.Show(
                    string.Format(_localization.GetString("UnlinkErrorMsg"), ex.Message),
                    _localization.GetString("UnlinkErrorTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
            finally
            {
                if (UnlinkButton != null)
                    UnlinkButton.IsEnabled = true;
            }
        }

        private void LinkAccount_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var welcomeWindow = new WelcomeLinkWindow();
                welcomeWindow.Owner = Window.GetWindow(this);
                welcomeWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                welcomeWindow.ShowDialog();

                _ = Task.Run(async () =>
                {
                    await Task.Delay(500);
                    await Dispatcher.InvokeAsync(() =>
                    {
                        UpdateSections(CloudAccountService.Instance.IsLinked, CloudAccountService.Instance.LinkedEmail);
                    });
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(_localization.GetString("LinkErrorMsg"), ex.Message), _localization.GetString("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task UpdateAccountStatusAsync()
        {
            try
            {
                bool isLinked = CloudAccountService.Instance.IsLinked;
                string? email = CloudAccountService.Instance.LinkedEmail;
                UpdateSections(isLinked, email);
            }
            catch { }
        }
        private void RestartAsAdmin_Click(object sender, RoutedEventArgs e)
        {
            if (IsRunningAsAdministrator())
            {
                _dialogs?.ShowInfo(_localization.GetString("AlreadyAdminTitle"), _localization.GetString("AlreadyAdminMsg"));
                return;
            }

            var result = ModernMessageBox.Show(
                _localization.GetString("AdminModeMsg"),
                _localization.GetString("AdminModeTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question); // Usando Question pois Shield pode não existir no enum do ModernMessageBox

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    var exeName = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "VoltrisOptimizer.exe";
                    var startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = exeName,
                        UseShellExecute = true,
                        Verb = "runas" 
                    };

                    System.Diagnostics.Process.Start(startInfo);
                    Application.Current.Shutdown();
                }
                catch (Win32Exception)
                {
                    // Usuário cancelou o UAC ou erro de permissão do Windows
                    // Não fazer nada, apenas logar se possível
                    try { 
                        var logDir = LogDirectoryResolver.Resolve();
                        if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
                        File.AppendAllText(Path.Combine(logDir, "admin_elevation.log"), $"[{DateTime.Now}] UAC Cancelado pelo usuário.\n");
                    } catch { }
                }
                catch (Exception ex)
                {
                    _dialogs?.ShowError(_localization.GetString("Error"), string.Format(_localization.GetString("AdminElevationError"), ex.Message));
                }
            }
        }

        public static bool IsRunningAsAdministrator()
        {
            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    var principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        private void TransparencyToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            
            try
            {
                ApplyTransparency(true);
                _settings.Settings.EnableTransparency = true;
                _settings.SaveSettings();
                
                // Atualizar ícones do header em tempo real
                SyncHeaderIcons();
                
                App.TelemetryService?.TrackEvent("SETTINGS_TRANSPARENCY_TOGGLE", "Settings", "Change", metadata: new { Enabled = true });
            }
            catch (Exception ex)
            {
                _logger?.Log(LogLevel.Error, LogCategory.System, $"Erro ao ativar transparência: {ex.Message}");
            }
        }

        private void TransparencyToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            
            try
            {
                ApplyTransparency(false);
                _settings.Settings.EnableTransparency = false;
                _settings.SaveSettings();
                
                // Atualizar ícones do header em tempo real
                SyncHeaderIcons();
                
                App.TelemetryService?.TrackEvent("SETTINGS_TRANSPARENCY_TOGGLE", "Settings", "Change", metadata: new { Enabled = false });
            }
            catch (Exception ex)
            {
                _logger?.Log(LogLevel.Error, LogCategory.System, $"Erro ao desativar transparência: {ex.Message}");
            }
        }

        // ============================================
        // NOTIFICAÇÕES
        // ============================================

        private void CheckNotificationsEnabled_Checked(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            _settings.Settings.NotificationsEnabled = true;
            _settings.SaveSettings();
            
            // Habilitar sub-opções
            if (CheckNotifyThreats != null) CheckNotifyThreats.IsEnabled = true;
            if (CheckNotifyScanComplete != null) CheckNotifyScanComplete.IsEnabled = true;
            if (CheckNotifyNewDevice != null) CheckNotifyNewDevice.IsEnabled = true;
        }

        private void CheckNotificationsEnabled_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            _settings.Settings.NotificationsEnabled = false;
            _settings.SaveSettings();
            
            // Desabilitar sub-opções visualmente
            if (CheckNotifyThreats != null) CheckNotifyThreats.IsEnabled = false;
            if (CheckNotifyScanComplete != null) CheckNotifyScanComplete.IsEnabled = false;
            if (CheckNotifyNewDevice != null) CheckNotifyNewDevice.IsEnabled = false;
        }

        private void CheckNotifyThreats_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            _settings.Settings.NotifyOnThreatDetected = CheckNotifyThreats?.IsChecked ?? true;
            _settings.SaveSettings();
        }

        private void CheckNotifyScanComplete_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            _settings.Settings.NotifyOnScanComplete = CheckNotifyScanComplete?.IsChecked ?? true;
            _settings.SaveSettings();
        }

        private void CheckNotifyNewDevice_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            _settings.Settings.NotifyOnNewDevice = CheckNotifyNewDevice?.IsChecked ?? true;
            _settings.SaveSettings();
        }

        private void CheckToastMuted_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            bool muted = CheckToastMuted?.IsChecked ?? false;
            // Atualiza via GlobalNotificationService que sincroniza com Settings
            GlobalNotificationService.IsToastMuted = muted;
        }

        /// <summary>
        /// Sincroniza os ícones do header da MainWindow com o estado atual das configurações.
        /// Chamado quando tema ou transparência são alterados pela SettingsView.
        /// </summary>
        private void SyncHeaderIcons()
        {
            try
            {
                if (Application.Current.MainWindow is MainWindow mainWindow)
                {
                    var isLight = _settings.Settings.Theme?.Equals("Light", StringComparison.OrdinalIgnoreCase) == true;
                    var transparency = _settings.Settings.EnableTransparency;
                    mainWindow.SyncHeaderToggleIcons(isLight, transparency);
                }
            }
            catch { }
        }

        /// <summary>
        /// Atualiza os controles da SettingsView para refletir o estado atual das configurações.
        /// Chamado pela MainWindow quando tema/transparência são alterados pelo header.
        /// </summary>
        public void SyncFromSettings()
        {
            try
            {
                _isLoading = true;
                
                // Sincronizar ThemeCombo
                if (ThemeCombo != null)
                {
                    var isLight = _settings.Settings.Theme?.Equals("Light", StringComparison.OrdinalIgnoreCase) == true;
                    ThemeCombo.SelectedIndex = isLight ? 1 : 0;
                }
                
                // Sincronizar TransparencyToggle
                if (TransparencyToggle != null)
                {
                    TransparencyToggle.IsChecked = _settings.Settings.EnableTransparency;
                }
                
                // 🔥 Carregar status do Modo Seguro
                
                _isLoading = false;
            }
            catch
            {
                _isLoading = false;
            }
        }

        private void ApplyTransparency(bool enabled)
        {
            try
            {
                ApplyThemeAndTransparency(_settings.Settings.Theme, enabled);
                _logger?.Log(LogLevel.Info, LogCategory.System, $"Transparência {(enabled ? "ativada" : "desativada")} com sucesso");
            }
            catch (Exception ex)
            {
                _logger?.Log(LogLevel.Error, LogCategory.System, $"Erro ao aplicar transparência: {ex.Message}");
                throw;
            }
        }

        private void ApplyThemeAndTransparency(string? theme, bool transparency)
        {
            var dicts = Application.Current.Resources.MergedDictionaries;

            // Remover tema e transparência existentes
            for (int i = dicts.Count - 1; i >= 0; i--)
            {
                var src = dicts[i].Source?.ToString() ?? string.Empty;
                if (src.EndsWith("DarkTheme.xaml",           StringComparison.OrdinalIgnoreCase) ||
                    src.EndsWith("LightTheme.xaml",          StringComparison.OrdinalIgnoreCase) ||
                    src.EndsWith("TransparencyOn.xaml",      StringComparison.OrdinalIgnoreCase) ||
                    src.EndsWith("TransparencyOff.xaml",     StringComparison.OrdinalIgnoreCase) ||
                    src.EndsWith("LightTransparencyOn.xaml", StringComparison.OrdinalIgnoreCase))
                    dicts.RemoveAt(i);
            }

            bool isLight = theme?.Equals("Light", StringComparison.OrdinalIgnoreCase) == true;

            // 1. Tema base — define todas as cores sólidas do tema
            dicts.Add(new ResourceDictionary
            {
                Source = new Uri(isLight ? "/UI/Themes/LightTheme.xaml" : "/UI/Themes/DarkTheme.xaml", UriKind.Relative)
            });

            // 2. Transparência por cima — sobrescreve fundos com versões semi-transparentes
            if (transparency)
            {
                if (isLight)
                {
                    dicts.Add(new ResourceDictionary
                    {
                        Source = new Uri("/UI/Themes/LightTransparencyOn.xaml", UriKind.Relative)
                    });
                }
                else
                {
                    dicts.Add(new ResourceDictionary
                    {
                        Source = new Uri("/UI/Themes/TransparencyOn.xaml", UriKind.Relative)
                    });
                }
            }
            else
            {
                dicts.Add(new ResourceDictionary
                {
                    Source = new Uri("/UI/Themes/TransparencyOff.xaml", UriKind.Relative)
                });
            }

            // 3. Backdrop DWM
            if (Application.Current.MainWindow is VoltrisOptimizer.UI.MainWindow mainWindow)
                mainWindow.ApplyWindowTransparency(transparency);
        }

        // ============================================
        // RECURSOS EXPERIMENTAIS (BETA)
        // ============================================

        private void ToggleShowDrivers_Checked(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            _settings.Settings.ShowDriversPage = true;
            _settings.SaveSettings();
        }

        private void ToggleShowDrivers_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            _settings.Settings.ShowDriversPage = false;
            _settings.SaveSettings();
        }

        private void ToggleShowRecovery_Checked(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            _settings.Settings.ShowRecoveryPage = true;
            _settings.SaveSettings();
        }

        private void ToggleShowRecovery_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            _settings.Settings.ShowRecoveryPage = false;
            _settings.SaveSettings();
        }
    }
}
