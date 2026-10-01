using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Display;

namespace VoltrisOptimizer.UI.ViewModels
{
    public class DisplayViewModel : ViewModelBase
    {
        private const string TAG = "[DisplayVM]";
        private readonly DisplayService _display;
        private readonly ILoggingService _logger;
           private CancellationTokenSource? _monitorCts;


        private ObservableCollection<MonitorInfo> _monitors = new();
        public ObservableCollection<MonitorInfo> Monitors
        {
            get => _monitors;
            set => SetProperty(ref _monitors, value);
        }

        private MonitorInfo? _selectedMonitor;
        public MonitorInfo? SelectedMonitor
        {
            get => _selectedMonitor;
            set
            {
                if (SetProperty(ref _selectedMonitor, value) && value != null)
                {
                    _logger.LogInfo($"{TAG} Monitor selecionado: {value.Name} ({value.DeviceName})");
                    PopulateMonitorOptions(value);
                }
            }
        }

        private ObservableCollection<int> _availableHz = new();
        public ObservableCollection<int> AvailableHz
        {
            get => _availableHz;
            set => SetProperty(ref _availableHz, value);
        }

        private int _selectedHz;
        public int SelectedHz
        {
            get => _selectedHz;
            set => SetProperty(ref _selectedHz, value);
        }

        private ObservableCollection<string> _availableResolutions = new();
        public ObservableCollection<string> AvailableResolutions
        {
            get => _availableResolutions;
            set => SetProperty(ref _availableResolutions, value);
        }

        private string _selectedResolution = string.Empty;
        public string SelectedResolution
        {
            get => _selectedResolution;
            set => SetProperty(ref _selectedResolution, value);
        }

        private double _currentHz;
        public double CurrentHz
        {
            get => _currentHz;
            set => SetProperty(ref _currentHz, value);
        }

        private double _estimatedInputLagMs;
        public double EstimatedInputLagMs
        {
            get => _estimatedInputLagMs;
            set => SetProperty(ref _estimatedInputLagMs, value);
        }

        private double _framePacingMs;
        public double FramePacingMs
        {
            get => _framePacingMs;
            set => SetProperty(ref _framePacingMs, value);
        }

        private bool _tearingDetected;
        public bool TearingDetected
        {
            get => _tearingDetected;
            set
            {
                if (SetProperty(ref _tearingDetected, value))
                    OnPropertyChanged(nameof(TearingColor));
            }
        }

        public string TearingColor => TearingDetected ? "#FF4466" : "#00FF88";

        private string _inputLagColor = "#00FF88";
        public string InputLagColor
        {
            get => _inputLagColor;
            set => SetProperty(ref _inputLagColor, value);
        }

        private string _stutterColor = "#00FF88";
        public string StutterColor
        {
            get => _stutterColor;
            set => SetProperty(ref _stutterColor, value);
        }

        private bool _vSyncEnabled;
        public bool VSyncEnabled
        {
            get => _vSyncEnabled;
            set
            {
                if (SetProperty(ref _vSyncEnabled, value))
                    _ = ApplyVSyncAsync(value);
            }
        }

        private double _gammaValue = 1.0;
        private double _pendingGammaValue = 1.0;
        public double GammaValue
        {
            get => _gammaValue;
            set
            {
                SetProperty(ref _gammaValue, value);
                OnPropertyChanged(nameof(ApplyGammaEnabled));
            }
        }

        public bool ApplyGammaEnabled => Math.Abs(_gammaValue - _pendingGammaValue) > 0.01;

        public double PendingGammaValue
        {
            get => _pendingGammaValue;
            set
            {
                if (SetProperty(ref _pendingGammaValue, value))
                {
                    OnPropertyChanged(nameof(ApplyGammaEnabled));
                }
            }
        }

        private bool _isTestRunning;
        public bool IsTestRunning
        {
            get => _isTestRunning;
            set => SetProperty(ref _isTestRunning, value);
        }

        private string _testResult = string.Empty;
        public string TestResult
        {
            get => _testResult;
            set => SetProperty(ref _testResult, value);
        }

        private string _testResultColor = "#00FF88";
        public string TestResultColor
        {
            get => _testResultColor;
            set => SetProperty(ref _testResultColor, value);
        }

        private string _statusMessage = LocalizationService.Instance.GetString("DisplayStatusReady");
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private string _statusColor = "#00FF88";
        public string StatusColor
        {
            get => _statusColor;
            set => SetProperty(ref _statusColor, value);
        }

        public ICommand LoadCommand { get; }
        public ICommand StartMonitoringCommand { get; }
        public ICommand StopMonitoringCommand { get; }
        public ICommand ApplyRefreshRateCommand { get; }
        public ICommand ApplyResolutionCommand { get; }
        public ICommand ApplyGammaCommand { get; }
        public ICommand RestoreDefaultsCommand { get; }
        public ICommand RunGhostingTestCommand { get; }
        public ICommand RunTearingTestCommand { get; }
        public ICommand RunInputLagTestCommand { get; }
        public ICommand FixGhostingCommand { get; }
        public ICommand FixTearingCommand { get; }
        public ICommand FixInputLagCommand { get; }

        private bool _hasGhostingProblem;
        public bool HasGhostingProblem { get => _hasGhostingProblem; set => SetProperty(ref _hasGhostingProblem, value); }
        private bool _hasTearingProblem;
        public bool HasTearingProblem { get => _hasTearingProblem; set => SetProperty(ref _hasTearingProblem, value); }
        private bool _hasInputLagProblem;
        public bool HasInputLagProblem { get => _hasInputLagProblem; set => SetProperty(ref _hasInputLagProblem, value); }

        public DisplayViewModel(DisplayService display, ILoggingService logger)
        {
            _display = display ?? throw new ArgumentNullException(nameof(display));
            _logger  = logger  ?? throw new ArgumentNullException(nameof(logger));

            LoadCommand             = new AsyncRelayCommand(_ => LoadAsync());
            StartMonitoringCommand  = new AsyncRelayCommand(_ => StartMonitoringAsync());
            StopMonitoringCommand   = new RelayCommand(_ => StopMonitoring());
            ApplyRefreshRateCommand = new AsyncRelayCommand(_ => ApplyRefreshRateAsync());
            ApplyResolutionCommand  = new AsyncRelayCommand(_ => ApplyResolutionAsync());
            ApplyGammaCommand       = new AsyncRelayCommand(_ => ApplyGammaAsync());
            RestoreDefaultsCommand  = new AsyncRelayCommand(_ => RestoreDefaultsAsync());
            RunGhostingTestCommand  = new AsyncRelayCommand(_ => RunGhostingTestAsync());
            RunTearingTestCommand   = new AsyncRelayCommand(_ => RunTearingTestAsync());
            RunInputLagTestCommand  = new AsyncRelayCommand(_ => RunInputLagTestAsync());
            FixGhostingCommand      = new AsyncRelayCommand(_ => FixGhostingAsync());
            FixTearingCommand       = new AsyncRelayCommand(_ => FixTearingAsync());
            FixInputLagCommand      = new AsyncRelayCommand(_ => FixInputLagAsync());

            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;

            _logger.LogInfo($"{TAG} ViewModel criado.");
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            var ready = LocalizationService.Instance.GetString("DisplayStatusReady");
            if (_statusMessage == "Pronto" || _statusMessage == "Ready" || _statusMessage == "Listo" || _statusMessage == ready)
            {
                StatusMessage = ready;
            }
        }

        public async Task LoadAsync()
        {
            _logger.LogInfo($"{TAG} [LoadAsync] Iniciando carregamento de monitores...");
            await RunBusyAsync(async () =>
            {
                var monitors = await _display.GetMonitorsAsync();
                int monitorCount = 0;
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Monitors.Clear();
                    foreach (var m in monitors)
                        Monitors.Add(m);

                    monitorCount = Monitors.Count;
                    if (monitorCount > 0)
                        SelectedMonitor = Monitors[0];
                });

                if (monitorCount > 0)
                    _logger.LogSuccess($"{TAG} [LoadAsync] {monitorCount} monitor(es) carregado(s).");
                else
                    _logger.LogWarning($"{TAG} [LoadAsync] Nenhum monitor detectado.");

                var settings = SettingsService.Instance.Settings;
                _gammaValue = settings.DisplayGamma > 0 ? settings.DisplayGamma : 1.0;
                _pendingGammaValue = _gammaValue;
                OnPropertyChanged(nameof(GammaValue));
                OnPropertyChanged(nameof(PendingGammaValue));

                _vSyncEnabled = settings.DisplayVSyncEnabled;
                OnPropertyChanged(nameof(VSyncEnabled));

                _logger.LogInfo($"{TAG} [LoadAsync] Aplicando configuracoes salvas: Gamma={_gammaValue}, VSync={_vSyncEnabled}");
                _ = _display.SetGammaAsync(_gammaValue);
                _ = _display.SetDwmVSyncAsync(_vSyncEnabled);

                SetStatus(LocalizationService.Instance.GetString("DisplayMonitorsLoaded"), "#00FF88");
            }, LocalizationService.Instance.GetString("DisplayLoadingMonitors"));
        }

        private void PopulateMonitorOptions(MonitorInfo monitor)
        {
            _logger.LogInfo($"{TAG} [PopulateMonitorOptions] Populando opcoes para: {monitor.Name}");

            System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            {
                if (monitor.SupportedHz != null && monitor.SupportedHz.Count > 0)
                {
                    AvailableHz.Clear();
                    foreach (var hz in monitor.SupportedHz)
                        AvailableHz.Add(hz);
                    SelectedHz = monitor.CurrentHz;
                }

                if (monitor.SupportedResolutions != null && monitor.SupportedResolutions.Count > 0)
                {
                    AvailableResolutions.Clear();
                    foreach (var (w, h) in monitor.SupportedResolutions)
                        AvailableResolutions.Add($"{w}x{h}");
                    SelectedResolution = $"{monitor.CurrentWidth}x{monitor.CurrentHeight}";
                }

                CurrentHz = monitor.CurrentHz;
                EstimatedInputLagMs = monitor.CurrentHz > 0 ? Math.Round(1000.0 / monitor.CurrentHz, 2) : 16.67;
            });
            UpdateInputLagColor();

            _logger.LogInfo($"{TAG} [PopulateMonitorOptions] Hz: {AvailableHz.Count} | Resolucoes: {AvailableResolutions.Count}");
        }

        public async Task StartMonitoringAsync()
        {
            _logger.LogInfo($"{TAG} [StartMonitoringAsync] Iniciando monitoramento continuo...");
            StopMonitoring();
            _monitorCts = new CancellationTokenSource();
            var token = _monitorCts.Token;
            SetStatus(LocalizationService.Instance.GetString("DisplayMonitoring"), "#31A8FF");
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var metrics = await _display.GetDisplayMetricsAsync();
                    CurrentHz           = metrics.RefreshRateReal;
                    EstimatedInputLagMs = metrics.EstimatedInputLagMs;
                    FramePacingMs       = metrics.FramePacingMs;
                    TearingDetected     = metrics.TearingDetected;
                    UpdateInputLagColor();
                    StutterColor = metrics.StutterDetected ? "#FFAA00" : "#00FF88";
                    _logger.LogDebug($"{TAG} [Monitor] Hz={CurrentHz} | Lag={EstimatedInputLagMs}ms | Pacing={FramePacingMs}ms | Tearing={TearingDetected}");
                    await Task.Delay(2000, token);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInfo($"{TAG} [StartMonitoringAsync] Monitoramento cancelado.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"{TAG} [StartMonitoringAsync] Erro: {ex.Message}", ex);
            }
            SetStatus(LocalizationService.Instance.GetString("DisplayMonitoringStopped"), "#FFAA00");
        }

        public void StopMonitoring()
        {
            if (_monitorCts != null)
            {
                _logger.LogInfo($"{TAG} [StopMonitoring] Parando monitoramento.");
                _monitorCts.Cancel();
                _monitorCts.Dispose();
                _monitorCts = null;
            }
        }

        private async Task ApplyRefreshRateAsync()
        {
            if (SelectedMonitor == null) return;
            _logger.LogInfo($"{TAG} [ApplyRefreshRateAsync] Aplicando {SelectedHz}Hz em {SelectedMonitor.DeviceName}");
            await RunBusyAsync(async () =>
            {
                GlobalProgressService.Instance.StartOperation(string.Format(LocalizationService.Instance.GetString("DisplayApplyingHz"), SelectedHz), true);
                try
                {
                    bool ok = await _display.SetRefreshRateAsync(SelectedMonitor.DeviceName, SelectedHz);
                    if (ok)
                    {
                        GlobalProgressService.Instance.UpdateProgress(100, string.Format(LocalizationService.Instance.GetString("DisplayHzChanged"), SelectedHz));
                        SetStatus(string.Format(LocalizationService.Instance.GetString("DisplayHzChanged"), SelectedHz), "#00FF88");
                        _logger.LogSuccess($"{TAG} [ApplyRefreshRateAsync] {SelectedHz}Hz aplicado.");
                        await LoadAsync();
                        GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("DisplayHzAppliedSuccess"), SelectedHz));
                        GlobalNotificationService.ShowSuccess(LocalizationService.Instance.GetString("DisplayNotificationTitle"), string.Format(LocalizationService.Instance.GetString("DisplayHzNotification"), SelectedHz));
                    }
                    else
                    {
                        SetStatus(string.Format(LocalizationService.Instance.GetString("DisplayHzApplyFailed"), SelectedHz), "#FF4466");
                        _logger.LogWarning($"{TAG} [ApplyRefreshRateAsync] Falha ao aplicar {SelectedHz}Hz.");
                        GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("DisplayHzApplyFailedShort"), SelectedHz));
                    }
                }
                catch (Exception ex)
                {
                    GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("DisplayHzApplyError"), ex.Message));
                    throw;
                }
            }, string.Format(LocalizationService.Instance.GetString("DisplayApplyingHz"), SelectedHz));
        }

        private async Task ApplyResolutionAsync()
        {
            if (SelectedMonitor == null || string.IsNullOrEmpty(SelectedResolution)) return;
            _logger.LogInfo($"{TAG} [ApplyResolutionAsync] Aplicando {SelectedResolution}");
            var parts = SelectedResolution.Split('x');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int w) || !int.TryParse(parts[1], out int h))
            {
                _logger.LogWarning($"{TAG} [ApplyResolutionAsync] Formato invalido: {SelectedResolution}");
                return;
            }
            await RunBusyAsync(async () =>
            {
                GlobalProgressService.Instance.StartOperation(string.Format(LocalizationService.Instance.GetString("DisplayApplyingResolution"), SelectedResolution), true);
                try
                {
                    bool ok = await _display.SetResolutionAsync(SelectedMonitor.DeviceName, w, h);
                    if (ok)
                    {
                        GlobalProgressService.Instance.UpdateProgress(100, string.Format(LocalizationService.Instance.GetString("DisplayResolutionChanged"), SelectedResolution));
                        SetStatus(string.Format(LocalizationService.Instance.GetString("DisplayResolutionChanged"), SelectedResolution), "#00FF88");
                        _logger.LogSuccess($"{TAG} [ApplyResolutionAsync] {SelectedResolution} aplicado.");
                        await LoadAsync();
                        GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("DisplayResolutionAppliedSuccess"), SelectedResolution));
                        GlobalNotificationService.ShowSuccess(LocalizationService.Instance.GetString("DisplayNotificationTitle"), string.Format(LocalizationService.Instance.GetString("DisplayResolutionNotification"), SelectedResolution));
                    }
                    else
                    {
                        SetStatus(string.Format(LocalizationService.Instance.GetString("DisplayResolutionApplyFailed"), SelectedResolution), "#FF4466");
                        _logger.LogWarning($"{TAG} [ApplyResolutionAsync] Falha.");
                        GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("DisplayResolutionApplyFailedShort"), SelectedResolution));
                    }
                }
                catch (Exception ex)
                {
                    GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("DisplayResolutionApplyError"), ex.Message));
                    throw;
                }
            }, string.Format(LocalizationService.Instance.GetString("DisplayApplyingResolution"), SelectedResolution));
        }

        private async Task ApplyVSyncAsync(bool enable)
        {
            _logger.LogInfo($"{TAG} [ApplyVSyncAsync] VSync={enable}");
            GlobalProgressService.Instance.StartOperation(string.Format(LocalizationService.Instance.GetString(enable ? "DisplayActivatingVSync" : "DisplayDeactivatingVSync")), true);
            try
            {
                SettingsService.Instance.Settings.DisplayVSyncEnabled = enable;
                SettingsService.Instance.SaveSettings();

                await _display.SetDwmVSyncAsync(enable);
                GlobalProgressService.Instance.UpdateProgress(100, string.Format(LocalizationService.Instance.GetString(enable ? "DisplayVSyncActivated" : "DisplayVSyncDeactivated")));
                SetStatus(string.Format(LocalizationService.Instance.GetString(enable ? "DisplayVSyncActivated" : "DisplayVSyncDeactivated")), "#00FF88");
                _logger.LogSuccess($"{TAG} [ApplyVSyncAsync] VSync={enable} aplicado.");
                GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString(enable ? "DisplayVSyncActivatedSuccess" : "DisplayVSyncDeactivatedSuccess")));
            }
            catch (Exception ex)
            {
                _logger.LogError($"{TAG} [ApplyVSyncAsync] Erro: {ex.Message}", ex);
                SetStatus(LocalizationService.Instance.GetString("DisplayVSyncConfigError"), "#FF4466");
                GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("DisplayVSyncConfigErrorDetail"), ex.Message));
            }
        }

        private async Task ApplyGammaAsync()
        {
            var gammaToApply = _pendingGammaValue;
            _logger.LogInfo($"{TAG} [ApplyGammaAsync] Aplicando gamma={gammaToApply:F2}");
            await RunBusyAsync(async () =>
            {
                GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("DisplayApplyingGamma"), true);
                try
                {
                    bool ok = await _display.SetGammaAsync(gammaToApply);
                    if (ok)
                    {
                        _gammaValue = gammaToApply;
                        SettingsService.Instance.Settings.DisplayGamma = gammaToApply;
                        SettingsService.Instance.SaveSettings();
                        OnPropertyChanged(nameof(GammaValue));
                        OnPropertyChanged(nameof(ApplyGammaEnabled));
                        GlobalProgressService.Instance.UpdateProgress(100, string.Format(LocalizationService.Instance.GetString("DisplayGammaAdjusted"), gammaToApply));
                        SetStatus(string.Format(LocalizationService.Instance.GetString("DisplayGammaAdjusted"), gammaToApply), "#00FF88");
                        _logger.LogSuccess($"{TAG} [ApplyGammaAsync] Gamma={gammaToApply:F2} aplicado e salvo.");
                        GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("DisplayGammaApplied"), gammaToApply));
                    }
                    else
                    {
                        SetStatus(LocalizationService.Instance.GetString("DisplayGammaAdjustFailed"), "#FF4466");
                        GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("DisplayGammaAdjustFailedShort"));
                    }
                }
                catch (Exception ex)
                {
                    GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("DisplayGammaAdjustError"), ex.Message));
                    throw;
                }
            }, LocalizationService.Instance.GetString("DisplayApplyingGamma"));
        }

        private async Task RestoreDefaultsAsync()
        {
            if (SelectedMonitor == null) return;
            _logger.LogInfo($"{TAG} [RestoreDefaultsAsync] Restaurando TODAS as configurações de display para o padrão original do Windows...");
            await RunBusyAsync(async () =>
            {
                GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("DisplayRestoringDefaults"), true);
                try
                {
                    // 🔥 1. Restaurar resolução e taxa de refresh para padrão Windows
                    bool ok = await _display.RestoreDefaultsAsync(SelectedMonitor.DeviceName);
                    GlobalProgressService.Instance.UpdateProgress(30, LocalizationService.Instance.GetString("DisplayRestoringResolutionAndRefresh"));
                    
                    // 🔥 2. Resetar gamma para valor padrão Windows (1.0)
                    GammaValue = 1.0;
                    bool gammaOk = await _display.SetGammaAsync(SelectedMonitor.DeviceName, 1.0);
                    
                    // 🔥 3. Remover configurações personalizadas salvas
                    var settings = SettingsService.Instance.Settings;
                    if (settings.CustomDisplaySettings != null && settings.CustomDisplaySettings.ContainsKey(SelectedMonitor.DeviceName))
                    {
                        settings.CustomDisplaySettings.Remove(SelectedMonitor.DeviceName);
                        SettingsService.Instance.SaveSettings();
                        _logger.LogInfo($"{TAG} [RestoreDefaults] Configurações personalizadas de display removidas.");
                    }
                    GlobalProgressService.Instance.UpdateProgress(60, LocalizationService.Instance.GetString("DisplayRestoringGammaAndSettings"));
                    
                    // 🔥 4. Resetar configurações de cor brilhante para padrão
                    try
                    {
                        await System.Threading.Tasks.Task.Run(() =>
                        {
                            var scope = new System.Management.ManagementScope("\\root\\wmi");
                            var query = new System.Management.SelectQuery("WmiMonitorBrightness");
                            using var searcher = new System.Management.ManagementObjectSearcher(scope, query);
                            foreach (System.Management.ManagementObject obj in searcher.Get())
                            {
                                obj.InvokeMethod("SetBrightness", new object[] { 100 });
                                break;
                            }
                        });
                        _logger.LogInfo($"{TAG} [RestoreDefaults] Brilho resetado para 100% (padrão Windows).");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"{TAG} [RestoreDefaults] Não foi possível resetar brilho: {ex.Message}");
                    }
                    
                    GlobalProgressService.Instance.UpdateProgress(80, LocalizationService.Instance.GetString("DisplayRestoringReloading"));
                    // 🔥 5. Recarregar informações atualizadas
                    await LoadAsync();
                    
                    // 🔥 6. Feedback completo ao usuário
                    bool success = ok && gammaOk;
                    SetStatus(
                        success ? LocalizationService.Instance.GetString("DisplayDefaultsRestoredSuccess") 
                               : LocalizationService.Instance.GetString("DisplayDefaultsRestoredPartial"), 
                        success ? "#00FF88" : "#FFAA00");
                    
                    _logger.LogSuccess($"{TAG} [RestoreDefaults] 🎉 RESTAURAÇÃO COMPLETA: Display restaurado para configurações originais do Windows. Success={success}");
                    GlobalProgressService.Instance.CompleteOperation(success ? LocalizationService.Instance.GetString("DisplayDefaultsRestoredComplete") : LocalizationService.Instance.GetString("DisplayDefaultsRestoredPartialComplete"));
                    GlobalNotificationService.ShowSuccess(LocalizationService.Instance.GetString("DisplayNotificationTitle"), success ? LocalizationService.Instance.GetString("DisplayDefaultsRestoredNotification") : LocalizationService.Instance.GetString("DisplayDefaultsRestoredPartialNotification"));
                }
                catch (Exception ex)
                {
                    GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("DisplayRestoreError"), ex.Message));
                    throw;
                }
            }, LocalizationService.Instance.GetString("DisplayRestoringDefaults"));
        }

        private async Task RunGhostingTestAsync()
        {
            _logger.LogInfo($"{TAG} [RunGhostingTestAsync] Iniciando teste de ghosting...");
            IsTestRunning = true;
            TestResult = LocalizationService.Instance.GetString("DisplayGhostingAnalyzing");
            TestResultColor = "#31A8FF";
            GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("DisplayGhostingTest"), false);
            try
            {
                GlobalProgressService.Instance.UpdateProgress(30, LocalizationService.Instance.GetString("DisplayCollectingMetrics"));
                var metrics = await _display.GetDisplayMetricsAsync();
                await Task.Delay(500);
                GlobalProgressService.Instance.UpdateProgress(80, LocalizationService.Instance.GetString("DisplayAnalyzingFramePacing"));
                bool risk = metrics.FramePacingMs > 3.0;
                HasGhostingProblem = risk;
                TestResult = risk
                    ? string.Format(LocalizationService.Instance.GetString("DisplayGhostingRisk"), metrics.FramePacingMs)
                    : string.Format(LocalizationService.Instance.GetString("DisplayGhostingNoRisk"), metrics.FramePacingMs);
                TestResultColor = risk ? "#FFAA00" : "#00FF88";
                _logger.LogInfo($"{TAG} [RunGhostingTestAsync] risk={risk}, pacing={metrics.FramePacingMs}ms");
                GlobalProgressService.Instance.CompleteOperation(risk ? LocalizationService.Instance.GetString("DisplayGhostingRiskDetected") : LocalizationService.Instance.GetString("DisplayGhostingNoRiskDetected"));
            }
            catch (Exception ex)
            {
                TestResult = string.Format(LocalizationService.Instance.GetString("DisplayTestError"), ex.Message);
                TestResultColor = "#FF4466";
                _logger.LogError($"{TAG} [RunGhostingTestAsync] Erro: {ex.Message}", ex);
                GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("DisplayGhostingTestError"), ex.Message));
            }
            finally { IsTestRunning = false; }
        }

        private async Task RunTearingTestAsync()
        {
            _logger.LogInfo($"{TAG} [RunTearingTestAsync] Iniciando teste de tearing...");
            IsTestRunning = true;
            TestResult = LocalizationService.Instance.GetString("DisplayTearingCheckingDWM");
            TestResultColor = "#31A8FF";
            GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("DisplayTearingTest"), false);
            try
            {
                GlobalProgressService.Instance.UpdateProgress(40, LocalizationService.Instance.GetString("DisplayTearingCheckingDWM"));
                var metrics = await _display.GetDisplayMetricsAsync();
                await Task.Delay(300);
                GlobalProgressService.Instance.UpdateProgress(90, LocalizationService.Instance.GetString("DisplayTearingAnalyzingSync"));
                HasTearingProblem = metrics.TearingDetected;
                TestResult = metrics.TearingDetected
                    ? LocalizationService.Instance.GetString("DisplayTearingDetected")
                    : LocalizationService.Instance.GetString("DisplayTearingNotDetected");
                TestResultColor = metrics.TearingDetected ? "#FF4466" : "#00FF88";
                _logger.LogInfo($"{TAG} [RunTearingTestAsync] TearingDetected={metrics.TearingDetected}");
                GlobalProgressService.Instance.CompleteOperation(metrics.TearingDetected ? LocalizationService.Instance.GetString("DisplayTearingDetectedResult") : LocalizationService.Instance.GetString("DisplayTearingNotDetectedResult"));
            }
            catch (Exception ex)
            {
                TestResult = string.Format(LocalizationService.Instance.GetString("DisplayTestError"), ex.Message);
                TestResultColor = "#FF4466";
                _logger.LogError($"{TAG} [RunTearingTestAsync] Erro: {ex.Message}", ex);
                GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("DisplayTearingTestError"), ex.Message));
            }
            finally { IsTestRunning = false; }
        }

        private async Task RunInputLagTestAsync()
        {
            _logger.LogInfo($"{TAG} [RunInputLagTestAsync] Iniciando teste de input lag...");
            IsTestRunning = true;
            TestResult = LocalizationService.Instance.GetString("DisplayInputLagCalculating");
            TestResultColor = "#31A8FF";
            GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("DisplayInputLagTest"), false);
            try
            {
                GlobalProgressService.Instance.UpdateProgress(30, LocalizationService.Instance.GetString("DisplayCollectingMetrics"));
                var metrics = await _display.GetDisplayMetricsAsync();
                await Task.Delay(300);
                GlobalProgressService.Instance.UpdateProgress(70, LocalizationService.Instance.GetString("DisplayInputLagCalculating"));
                double lag = metrics.EstimatedInputLagMs;
                HasInputLagProblem = lag > 16.7;
                string rating = lag <= 4.2 ? LocalizationService.Instance.GetString("DisplayInputLagExcellent")
                    : lag <= 6.9 ? LocalizationService.Instance.GetString("DisplayInputLagGreat")
                    : lag <= 8.3 ? LocalizationService.Instance.GetString("DisplayInputLagGood")
                    : lag <= 16.7 ? LocalizationService.Instance.GetString("DisplayInputLagAcceptable")
                    : LocalizationService.Instance.GetString("DisplayInputLagHigh");
                TestResult = string.Format(LocalizationService.Instance.GetString("DisplayInputLagResult"), lag, rating, metrics.RefreshRateReal);
                TestResultColor = lag <= 8.3 ? "#00FF88" : lag <= 16.7 ? "#FFAA00" : "#FF4466";
                _logger.LogInfo($"{TAG} [RunInputLagTestAsync] InputLag={lag}ms | Hz={metrics.RefreshRateReal}");
                GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("DisplayInputLagComplete"), lag, rating));
            }
            catch (Exception ex)
            {
                TestResult = string.Format(LocalizationService.Instance.GetString("DisplayTestError"), ex.Message);
                TestResultColor = "#FF4466";
                _logger.LogError($"{TAG} [RunInputLagTestAsync] Erro: {ex.Message}", ex);
                GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("DisplayInputLagTestError"), ex.Message));
            }
            finally { IsTestRunning = false; }
        }

        private async Task FixGhostingAsync()
        {
            _logger.LogInfo($"{TAG} [FixGhostingAsync] Corrigindo ghosting...");
            GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("DisplayFixingGhosting"), true);
            try
            {
                bool ok = await _display.SetGammaAsync(1.0);
                if (ok)
                {
                    HasGhostingProblem = false;
                    TestResult = LocalizationService.Instance.GetString("DisplayGhostingFixed");
                    TestResultColor = "#00FF88";
                    SetStatus(LocalizationService.Instance.GetString("DisplayGhostingFixed"), "#00FF88");
                    _logger.LogSuccess($"{TAG} [FixGhostingAsync] Ghosting corrigido");
                    GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("DisplayGhostingFixed"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"{TAG} [FixGhostingAsync] Erro: {ex.Message}", ex);
                GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("DisplayFixError"), ex.Message));
            }
        }

        private async Task FixTearingAsync()
        {
            _logger.LogInfo($"{TAG} [FixTearingAsync] Corrigindo tearing...");
            GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("DisplayFixingTearing"), true);
            try
            {
                bool ok = await _display.SetDwmVSyncAsync(true);
                if (ok)
                {
                    HasTearingProblem = false;
                    TestResult = LocalizationService.Instance.GetString("DisplayTearingFixed");
                    TestResultColor = "#00FF88";
                    SetStatus(LocalizationService.Instance.GetString("DisplayTearingFixed"), "#00FF88");
                    _logger.LogSuccess($"{TAG} [FixTearingAsync] Tearing corrigido");
                    GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("DisplayTearingFixed"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"{TAG} [FixTearingAsync] Erro: {ex.Message}", ex);
                GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("DisplayFixError"), ex.Message));
            }
        }

        private async Task FixInputLagAsync()
        {
            _logger.LogInfo($"{TAG} [FixInputLagAsync] Corrigindo input lag...");
            GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("DisplayFixingInputLag"), true);
            try
            {
                await _display.SetGammaAsync(1.0);
                HasInputLagProblem = false;
                TestResult = LocalizationService.Instance.GetString("DisplayInputLagFixed");
                TestResultColor = "#00FF88";
                SetStatus(LocalizationService.Instance.GetString("DisplayInputLagFixed"), "#00FF88");
                _logger.LogSuccess($"{TAG} [FixInputLagAsync] Input lag corrigido");
                GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("DisplayInputLagFixed"));
            }
            catch (Exception ex)
            {
                _logger.LogError($"{TAG} [FixInputLagAsync] Erro: {ex.Message}", ex);
                GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("DisplayFixError"), ex.Message));
            }
        }

        private void UpdateInputLagColor()
        {
            InputLagColor = EstimatedInputLagMs <= 8.3 ? "#00FF88"
                : EstimatedInputLagMs <= 16.7 ? "#FFAA00"
                : "#FF4466";
        }

        private void SetStatus(string msg, string color)
        {
            StatusMessage = msg;
            StatusColor   = color;
        }

        private async Task RunBusyAsync(Func<Task> action, string busyMessage = null)
        {
            await ExecuteSafeAsync(action, busyMessage ?? LocalizationService.Instance.GetString("DisplayProcessing"), ex =>
            {
                SetStatus(string.Format(LocalizationService.Instance.GetString("DisplayError"), ex.Message), "#FF4466");
                _logger.LogError($"{TAG} Erro: {ex.Message}", ex);
            });
        }

        protected override void OnDisposing()
        {
            StopMonitoring();
            _logger.LogInfo($"{TAG} ViewModel descartado.");
            base.OnDisposing();
        }
    }
}
