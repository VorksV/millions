using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Linq;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Personalize;
using VoltrisOptimizer.Services.Telemetry;

namespace VoltrisOptimizer.UI.ViewModels
{
    public class PersonalizeViewModel : ViewModelBase
    {
        private const string TAG = "[PersonalizeVM]";

        private readonly SystemTweaksService _tweaks;
        private readonly GpuControlService _gpu;
        private readonly ILoggingService _logger;
        private readonly TaskbarControlService _taskbarCtrl;
        private readonly VoltrisBlurService _explorerBlur;
        private readonly Windows11IconsService _win11Icons;
        private readonly CursorThemeService _cursorTheme;
        private readonly VoltrisOptimizer.Services.Gamer.Interfaces.IGamerModeOrchestrator _gamerMode;

        private bool _isGamerModeInternalChange = false;

        private PersonalizeProfile _selectedProfile = PersonalizeProfile.Normal;
        public PersonalizeProfile SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                if (SetProperty(ref _selectedProfile, value))
                {
                    _logger.LogInfo($"{TAG} Perfil selecionado: {value}");
                    OnPropertyChanged(nameof(IsCustomProfile));
                    SavePreference(s => s.PersonalizeProfilePlan = value);
                }
            }
        }
        public bool IsCustomProfile => SelectedProfile == PersonalizeProfile.Custom;

        public ObservableCollection<string> ProfileNames { get; } = new()
        {
            "Normal", "Performance", "Ultra", "Custom"
        };

        private string _selectedProfileName = "Normal";
        public string SelectedProfileName
        {
            get => _selectedProfileName;
            set
            {
                if (SetProperty(ref _selectedProfileName, value))
                {
                    SelectedProfile = value switch
                    {
                        "Performance" => PersonalizeProfile.Performance,
                        "Ultra"       => PersonalizeProfile.Ultra,
                        "Custom"      => PersonalizeProfile.Custom,
                        _             => PersonalizeProfile.Normal
                    };
                }
            }
        }

        /// <summary>
        /// Aplica uma alteração de personalização e mantém a interface coerente com o
        /// resultado REAL.
        ///
        /// POR QUE ISTO EXISTE: os setters dos toggles chamavam os serviços com
        /// `Task` sem observar (`_ = ...`) e SEM verificar o retorno. Como o retorno
        /// era booleano nas funções já corrigidas, o resultado era simplesmente
        /// descartado — o toggle mostrava "ativado" mesmo quando nada era gravado
        /// (por exemplo, sem permissão). Pior: nenhuma das ~22 ações de
        /// personalização entrava no Histórico.
        ///
        /// Agora: verifica, reverte o toggle em caso de falha, avisa o usuário e
        /// registra a operação com sucesso/falha reais.
        /// </summary>
        private async Task ApplyTweakAsync(
            Func<Task<bool>> apply,
            bool requestedValue,
            string toggleName,
            bool localizeIt = false,
            Action? revert = null)
        {
            try
            {
                bool ok = await apply().ConfigureAwait(true);

                string description = localizeIt
                    ? string.Format(
                        LocalizationService.Instance.GetString("PersonalizeToggleChanged"),
                        LocalizationService.Instance.GetString(toggleName),
                        requestedValue
                            ? LocalizationService.Instance.GetString("Loc_Enabled")
                            : LocalizationService.Instance.GetString("Loc_Disabled"))
                    : string.Format(
                        LocalizationService.Instance.GetString("PersonalizeToggleChanged"),
                        toggleName,
                        requestedValue ? "ON" : "OFF");

                HistoryService.Instance.RecordActivityInstance(
                    actionType: HistoryActionTypes.Personalization,
                    description: description,
                    success: ok,
                    spaceFreed: 0,
                    origin: HistoryOrigin.Manual,
                    duration: TimeSpan.Zero,
                    itemCount: 1,
                    errorMessage: ok ? null
                        : LocalizationService.Instance.GetString("PersonalizeToggleNotApplied"));

                if (ok)
                {
                    _logger.LogSuccess($"{TAG} [{toggleName}] Alteração aplicada e CONFIRMADA no sistema ({requestedValue}).");
                    return;
                }

                _logger.LogError($"{TAG} [{toggleName}] A alteração NÃO foi aplicada ({requestedValue}). Revertendo o toggle.");

                // Reverte o toggle para não mentir ao usuário.
                if (revert != null)
                {
                    var app = System.Windows.Application.Current;
                    if (app != null) await app.Dispatcher.InvokeAsync(revert);
                    else revert();
                }

                GlobalNotificationService.ShowError(
                    LocalizationService.Instance.GetString("Loc_Error"),
                    LocalizationService.Instance.GetString("PersonalizeToggleNotApplied"));
            }
            catch (Exception ex)
            {
                _logger.LogError($"{TAG} [{toggleName}] Erro ao aplicar: {ex.Message}", ex);

                if (revert != null)
                {
                    try
                    {
                        var app = System.Windows.Application.Current;
                        if (app != null) await app.Dispatcher.InvokeAsync(revert);
                        else revert();
                    }
                    catch { }
                }

                HistoryService.Instance.RecordActivityInstance(
                    actionType: HistoryActionTypes.Personalization,
                    description: $"{toggleName} — erro ao aplicar",
                    success: false, spaceFreed: 0,
                    origin: HistoryOrigin.Manual, duration: TimeSpan.Zero, itemCount: 1,
                    errorMessage: ex.Message);
            }
        }

        private bool _windowAnimations = true;
        public bool WindowAnimations
        {
            get => _windowAnimations;
            set 
            { 
                if (SetProperty(ref _windowAnimations, value)) 
                {
                    ApplyTweakAsync(async () => { await _tweaks.SetWindowAnimationsAsync(value); return true; }, value, nameof(WindowAnimations), localizeIt: true, revert: () => _windowAnimations = !value);
                    SavePreference(s => s.PersonalizeWindowAnimations = value);
                }
            }
        }

        private bool _menuAnimations = true;
        public bool MenuAnimations
        {
            get => _menuAnimations;
            set 
            { 
                if (SetProperty(ref _menuAnimations, value)) 
                {
                    ApplyTweakAsync(async () => { await _tweaks.SetMenuAnimationsAsync(value); return true; }, value, nameof(MenuAnimations), localizeIt: true, revert: () => _menuAnimations = !value);
                    SavePreference(s => s.PersonalizeMenuAnimations = value);
                }
            }
        }

        private bool _taskbarAnimations = true;
        public bool TaskbarAnimations
        {
            get => _taskbarAnimations;
            set 
            { 
                if (SetProperty(ref _taskbarAnimations, value)) 
                {
                    ApplyTweakAsync(async () => { await _tweaks.SetTaskbarAnimationsAsync(value); return true; }, value, nameof(TaskbarAnimations), localizeIt: true, revert: () => _taskbarAnimations = !value);
                    SavePreference(s => s.PersonalizeTaskbarAnimations = value);
                }
            }
        }

        private bool _dropShadows = true;
        public bool DropShadows
        {
            get => _dropShadows;
            set 
            { 
                if (SetProperty(ref _dropShadows, value)) 
                {
                    ApplyTweakAsync(() => _tweaks.SetDropShadowsAsync(value), value, nameof(DropShadows), revert: () => _dropShadows = !value);
                    SavePreference(s => s.PersonalizeDropShadows = value);
                }
            }
        }

        private bool _fontSmoothing = true;
        public bool FontSmoothing
        {
            get => _fontSmoothing;
            set 
            { 
                if (SetProperty(ref _fontSmoothing, value)) 
                {
                    ApplyTweakAsync(async () => { await _tweaks.SetFontSmoothingAsync(value); return true; }, value, nameof(FontSmoothing), localizeIt: true, revert: () => _fontSmoothing = !value);
                    SavePreference(s => s.PersonalizeFontSmoothing = value);
                }
            }
        }

        private bool _transparencyEffects = true;
        public bool TransparencyEffects
        {
            get => _transparencyEffects;
            set 
            { 
                if (SetProperty(ref _transparencyEffects, value)) 
                {
                    ApplyTweakAsync(async () => { await _tweaks.SetTransparencyEffectsAsync(value); return true; }, value, nameof(TransparencyEffects), revert: () => _transparencyEffects = !value);
                    SavePreference(s => s.PersonalizeTransparencyEffects = value);
                }
            }
        }

        private bool _hardwareAcceleration = false;
        public bool HardwareAcceleration
        {
            get => _hardwareAcceleration;
            set 
            { 
                if (SetProperty(ref _hardwareAcceleration, value)) 
                {
                    ApplyTweakAsync(() => _tweaks.SetHardwareAccelerationAsync(value), value, nameof(HardwareAcceleration), revert: () => _hardwareAcceleration = !value);
                    SavePreference(s => s.PersonalizeHardwareAcceleration = value);
                }
            }
        }

        private bool _explorerHighPerf = false;
        public bool ExplorerHighPerf
        {
            get => _explorerHighPerf;
            set 
            { 
                if (SetProperty(ref _explorerHighPerf, value)) 
                {
                    ApplyTweakAsync(() => _tweaks.SetExplorerResponsivenessAsync(value), value, nameof(ExplorerHighPerf), revert: () => _explorerHighPerf = !value);
                    SavePreference(s => s.PersonalizeExplorerHighPerf = value);
                }
            }
        }

        private bool _hardwareScheduling = false;
        public bool HardwareScheduling
        {
            get => _hardwareScheduling;
            set
            {
                if (SetProperty(ref _hardwareScheduling, value))
                {
                    ApplyTweakAsync(() => _gpu.SetHardwareSchedulingAsync(value), value, nameof(HardwareScheduling), revert: () => _hardwareScheduling = !value);
                    SavePreference(s => s.PersonalizeHardwareScheduling = value);
                }
            }
        }

        private bool _gamingGpuPriority = false;
        public bool GamingGpuPriority
        {
            get => _gamingGpuPriority;
            set
            {
                if (SetProperty(ref _gamingGpuPriority, value))
                {
                    ApplyTweakAsync(async () => { await _gpu.SetGamingGpuPriorityAsync(value); return true; }, value, nameof(GamingGpuPriority), revert: () => _gamingGpuPriority = !value);
                    SavePreference(s => s.PersonalizeGamingGpuPriority = value);
                }
            }
        }

        private bool _mpoEnabled = true;
        public bool MpoEnabled
        {
            get => _mpoEnabled;
            set
            {
                if (SetProperty(ref _mpoEnabled, value))
                {
                    ApplyTweakAsync(() => _gpu.SetMpoAsync(value), value, nameof(MpoEnabled), revert: () => _mpoEnabled = !value);
                    SavePreference(s => s.PersonalizeMpoEnabled = value);
                }
            }
        }

        /// <summary>
        /// Estado da centralização dos ícones da barra de tarefas.
        ///
        /// O setter registra a preferẽncia do usuário e pede a aplicação, mas o
        /// estado exibido passa a ser lido do SISTEMA (registro TaskbarAl no Win11 /
        /// posição real da janela no Win10), de modo que o toggle reflita a realidade
        /// — inclusive quando o usuário centraliza manualmente pelo Windows.
        /// </summary>
        private bool _taskbarCenteringEnabled = false;
        public bool TaskbarCenteringEnabled
        {
            get => _taskbarCenteringEnabled;
            set
            {
                if (!SetProperty(ref _taskbarCenteringEnabled, value)) return;

                _logger.LogInfo($"{TAG} [Taskbar] Solicitada centralização: {value}");
                _taskbarCtrl.SetCentering(value);

                SettingsService.Instance.Settings.TaskbarCenteringEnabled = value;
                SettingsService.Instance.SaveSettings();

                // Registra no histórico como "alteração de personalização", com o
                // desfecho real devolvido pelo serviço.
                _ = RecordTaskbarCenteringAsync(value);
            }
        }

        /// <summary>
        /// Registra a alteração de centralização no Histórico com o resultado real.
        /// </summary>
        private async Task RecordTaskbarCenteringAsync(bool requested)
        {
            try
            {
                // Pequeno atraso para o serviço concluir a aplicação e o read-back.
                await Task.Delay(600).ConfigureAwait(true);

                bool actual = _taskbarCtrl.IsCenteringCurrentlyApplied();
                bool success = requested ? actual : !actual;

                // Aplica no dispatcher para atualizar a UI a partir do estado real.
                var app = System.Windows.Application.Current;
                if (app != null)
                {
                    await app.Dispatcher.InvokeAsync(() =>
                    {
                        if (_taskbarCenteringEnabled != actual)
                        {
                            _taskbarCenteringEnabled = actual;
                            OnPropertyChanged(nameof(TaskbarCenteringEnabled));
                            _logger.LogInfo(
                                $"{TAG} [Taskbar] Toggle ajustado para o estado REAL do sistema: {actual}");
                        }
                    });
                }

                HistoryService.Instance.RecordActivityInstance(
                    actionType: HistoryActionTypes.Personalization,
                    description: requested
                        ? (success
                            ? "Ícones da barra de tarefas centralizados."
                            : "Centralização dos ícones solicitada, mas o Windows não confirmou a alteração.")
                        : (success
                            ? "Ícones da barra de tarefas alinhados à esquerda."
                            : "Alinhamento à esquerda solicitado, mas o Windows não confirmou a alteração."),
                    success: success,
                    spaceFreed: 0,
                    origin: HistoryOrigin.Manual,
                    duration: TimeSpan.Zero,
                    itemCount: 1,
                    errorMessage: success ? null : "O estado real do Windows difere do solicitado.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"{TAG} [Taskbar] Falha ao registrar a centralização no histórico: {ex.Message}");
            }
        }

        private bool _taskbarStyleEnabled = false;
        public bool TaskbarStyleEnabled
        {
            get => _taskbarStyleEnabled;
            set
            {
                if (SetProperty(ref _taskbarStyleEnabled, value))
                {
                    _logger.LogInfo($"{TAG} [Taskbar] Estilo visual: {value}");
                    ApplyTaskbarStyle();
                    SettingsService.Instance.Settings.TaskbarStyleEnabled = value;
                    SettingsService.Instance.SaveSettings();
                }
            }
        }

        private int _taskbarStyleIndex = 0;
        public int TaskbarStyleIndex
        {
            get => _taskbarStyleIndex;
            set
            {
                if (SetProperty(ref _taskbarStyleIndex, value))
                {
                    _logger.LogInfo($"{TAG} [Taskbar] StyleIndex alterado para {value}");
                    if (_taskbarStyleEnabled) ApplyTaskbarStyle();
                    SettingsService.Instance.Settings.TaskbarStyleIndex = value;
                    SettingsService.Instance.SaveSettings();
                }
            }
        }

        private int _taskbarOpacity = 255;
        public int TaskbarOpacity
        {
            get => _taskbarOpacity;
            set
            {
                if (SetProperty(ref _taskbarOpacity, value))
                {
                    _logger.LogInfo($"{TAG} [Taskbar] Opacidade: {value}");
                    if (_taskbarStyleEnabled) ScheduleApplyTaskbarStyle();
                    SettingsService.Instance.Settings.TaskbarOpacity = value;
                }
            }
        }

        private CancellationTokenSource? _styleDebounce;
        private void ScheduleApplyTaskbarStyle()
        {
            _styleDebounce?.Cancel();
            _styleDebounce = new CancellationTokenSource();
            var token = _styleDebounce.Token;
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(80, token);
                    if (!token.IsCancellationRequested)
                    {
                        _logger.LogDebug($"{TAG} [Taskbar] Debounce disparado — aplicando estilo.", source: "PersonalizeVM");
                        ApplyTaskbarStyle();
                        SettingsService.Instance.Settings.TaskbarOpacity = _taskbarOpacity;
                        SettingsService.Instance.SaveSettings();
                    }
                }
                catch (OperationCanceledException) { }
            }, token);
        }

        private void ApplyTaskbarStyle()
        {
            var mode = (TaskbarStyleMode)_taskbarStyleIndex;
            byte opacity = (byte)Math.Clamp(_taskbarOpacity, 0, 255);
            _logger.LogDebug($"{TAG} [Taskbar] ApplyTaskbarStyle: enabled={_taskbarStyleEnabled} mode={mode} opacity={opacity}", source: "PersonalizeVM");
            _taskbarCtrl.SetVoltrisBlurActive(false);
            _taskbarCtrl.SetStyle(_taskbarStyleEnabled, mode, opacity);
        }

        private string _gpuName = LocalizationService.Instance.GetString("DetectingGpu");
        public string GpuName
        {
            get => _gpuName;
            set => SetProperty(ref _gpuName, value);
        }

        private string _bottleneckReport = string.Empty;
        public string BottleneckReport
        {
            get => _bottleneckReport;
            set => SetProperty(ref _bottleneckReport, value);
        }

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private string _statusColor = "#00FF88";
        public string StatusColor
        {
            get { return _statusColor; }
            set { SetProperty(ref _statusColor, value); }
        }

        public bool IsWindows11 => SystemInfoService.IsWindows11;
        public bool IsWindows10 => !IsWindows11;

        private bool _win11IconsEnabled = false;
        public bool Win11IconsEnabled
        {
            get => _win11IconsEnabled;
            set
            {
                if (SetProperty(ref _win11IconsEnabled, value))
                {
                    _logger.LogInfo($"{TAG} [Win11Icons] Alterando estado para: {value}");
                    _ = ApplyWin11IconsAsync(value);
                }
            }
        }

        public System.Collections.ObjectModel.ObservableCollection<CursorThemeInfo> AvailableCursorThemes { get; } = new();

        private CursorThemeInfo? _selectedCursorTheme;
        public CursorThemeInfo? SelectedCursorTheme
        {
            get => _selectedCursorTheme;
            set
            {
                if (SetProperty(ref _selectedCursorTheme, value))
                {
                    _logger.LogInfo($"{TAG} [Cursor] Tema selecionado com aplicacao em tempo real: {value?.Name ?? "(nenhum)"}");
                    if (value != null)
                    {
                        _ = ApplyCursorThemeAsync();
                    }
                }
            }
        }

        private string _cursorStatusMessage = string.Empty;
        public string CursorStatusMessage
        {
            get => _cursorStatusMessage;
            set => SetProperty(ref _cursorStatusMessage, value);
        }

        private string _cursorStatusColor = "#00FF88";
        public string CursorStatusColor
        {
            get => _cursorStatusColor;
            set => SetProperty(ref _cursorStatusColor, value);
        }

        public ICommand ApplyCursorThemeCommand  { get; private set; } = null!;
        public ICommand RestoreCursorCommand     { get; private set; } = null!;

        private async Task ApplyWin11IconsAsync(bool enable)
        {
            await RunBusyAsync(async () =>
            {
                GlobalProgressService.Instance.StartOperation(enable ? LocalizationService.Instance.GetString("ApplyingWin11Icons") : LocalizationService.Instance.GetString("RevertingIcons"), true);
                try
                {
                    SetStatus(enable ? LocalizationService.Instance.GetString("ApplyingWin11Icons") : LocalizationService.Instance.GetString("RevertingIcons"), "#31A8FF");
                    bool ok = enable ? await _win11Icons.InstallIconsAsync() : await _win11Icons.UninstallIconsAsync();
                    
                    if (ok)
                    {
                        GlobalProgressService.Instance.UpdateProgress(100, enable ? LocalizationService.Instance.GetString("Win11IconsApplied") : LocalizationService.Instance.GetString("IconsReverted"));
                        SetStatus(enable ? LocalizationService.Instance.GetString("IconsAppliedSuccess") : LocalizationService.Instance.GetString("IconsRevertedSuccess"), "#00FF88");
                        GlobalNotificationService.ShowSuccess(LocalizationService.Instance.GetString("Icons"), enable ? LocalizationService.Instance.GetString("Win11IconsApplied") : LocalizationService.Instance.GetString("IconsRevertedToDefault"));
                        _logger.LogSuccess($"{TAG} [Win11Icons] Acao '{(enable ? "Apply" : "Revert")}' completada sem tocar em DLLs.");
                        GlobalProgressService.Instance.CompleteOperation(enable ? LocalizationService.Instance.GetString("Win11IconsAppliedOp") : LocalizationService.Instance.GetString("IconsRevertedOp"));
                    }
                    else
                    {
                        SetStatus(LocalizationService.Instance.GetString("IconsConfigFailed"), "#FF4466");
                        GlobalNotificationService.ShowError(LocalizationService.Instance.GetString("Icons"), LocalizationService.Instance.GetString("IconsConfigFailedMsg"));
                        _win11IconsEnabled = _win11Icons.IsInstalled();
                        OnPropertyChanged(nameof(Win11IconsEnabled));
                        GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("IconsConfigFailedOp"));
                    }
                }
                catch (Exception ex)
                {
                    GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("IconsConfigErrorOp"), ex.Message));
                    throw;
                }
            }, busyMessage: enable ? LocalizationService.Instance.GetString("ModifyingRegistryForWin11Icons") : LocalizationService.Instance.GetString("ClearingIconMapping"));
        }

        private bool _explorerBlurDllPresent = false;
        public bool ExplorerBlurDllPresent
        {
            get => _explorerBlurDllPresent;
            set => SetProperty(ref _explorerBlurDllPresent, value);
        }

        private bool _explorerBlurInstalled = false;
        public bool ExplorerBlurInstalled
        {
            get => _explorerBlurInstalled;
            set
            {
                if (SetProperty(ref _explorerBlurInstalled, value))
                {
                    _logger.LogInfo($"{TAG} [Explorer] ExplorerBlurInstalled={value} — notificando TaskbarControlService.");
                    _taskbarCtrl.SetVoltrisBlurActive(value);
                    if (!value && _taskbarStyleEnabled)
                    {
                        _logger.LogInfo($"{TAG} [Explorer] VoltrisBlur desinstalado — reaplicando estilo nativo da taskbar.");
                        ApplyTaskbarStyle();
                    }
                }
            }
        }

        private int _explorerEffectIndex = 1;
        public int ExplorerEffectIndex
        {
            get => _explorerEffectIndex;
            set { if (SetProperty(ref _explorerEffectIndex, value)) _logger.LogDebug($"{TAG} [Explorer] EffectIndex={value}", source: "PersonalizeVM"); }
        }

        private bool _explorerClearAddress = true;
        public bool ExplorerClearAddress
        {
            get => _explorerClearAddress;
            set { if (SetProperty(ref _explorerClearAddress, value)) _logger.LogDebug($"{TAG} [Explorer] ClearAddress={value}", source: "PersonalizeVM"); }
        }

        private bool _explorerClearBarBg = true;
        public bool ExplorerClearBarBg
        {
            get => _explorerClearBarBg;
            set { if (SetProperty(ref _explorerClearBarBg, value)) _logger.LogDebug($"{TAG} [Explorer] ClearBarBg={value}", source: "PersonalizeVM"); }
        }

        private bool _explorerClearWinUIBg = true;
        public bool ExplorerClearWinUIBg
        {
            get => _explorerClearWinUIBg;
            set
            {
                if (SetProperty(ref _explorerClearWinUIBg, value))
                    _logger.LogDebug($"{TAG} [Explorer] ClearWinUIBg={value}", source: "PersonalizeVM");
            }
        }

        public bool IsWin11 => Environment.OSVersion.Version.Build >= 22000;
        public bool IsWin10 => Environment.OSVersion.Version.Build < 22000;

        private bool _explorerShowLine = false;
        public bool ExplorerShowLine
        {
            get => _explorerShowLine;
            set { if (SetProperty(ref _explorerShowLine, value)) _logger.LogDebug($"{TAG} [Explorer] ShowLine={value}", source: "PersonalizeVM"); }
        }

        private int _explorerAlpha = 120;
        public int ExplorerAlpha
        {
            get => _explorerAlpha;
            set { if (SetProperty(ref _explorerAlpha, value)) _logger.LogDebug($"{TAG} [Explorer] Alpha={value}", source: "PersonalizeVM"); }
        }

        private int _explorerColorR = 0;
        public int ExplorerColorR
        {
            get => _explorerColorR;
            set { if (SetProperty(ref _explorerColorR, value)) _logger.LogDebug($"{TAG} [Explorer] R={value}", source: "PersonalizeVM"); }
        }

        private int _explorerColorG = 0;
        public int ExplorerColorG
        {
            get => _explorerColorG;
            set { if (SetProperty(ref _explorerColorG, value)) _logger.LogDebug($"{TAG} [Explorer] G={value}", source: "PersonalizeVM"); }
        }

        private int _explorerColorB = 0;
        public int ExplorerColorB
        {
            get => _explorerColorB;
            set { if (SetProperty(ref _explorerColorB, value)) _logger.LogDebug($"{TAG} [Explorer] B={value}", source: "PersonalizeVM"); }
        }

        public ICommand InstallExplorerBlurCommand { get; private set; } = null!;
        public ICommand UninstallExplorerBlurCommand { get; private set; } = null!;

        public ICommand ApplyProfileCommand { get; }
        public ICommand DetectBottlenecksCommand { get; }
        public ICommand RestoreDefaultsCommand { get; }
        public ICommand LoadCommand { get; }

        public PersonalizeViewModel(
            SystemTweaksService tweaks,
            GpuControlService gpu,
            ILoggingService logger,
            TaskbarControlService taskbarCtrl,
            VoltrisBlurService explorerBlur,
            Windows11IconsService win11Icons,
            CursorThemeService cursorTheme,
            VoltrisOptimizer.Services.Gamer.Interfaces.IGamerModeOrchestrator gamerMode)
        {
            _tweaks       = tweaks       ?? throw new ArgumentNullException(nameof(tweaks));
            _gpu          = gpu          ?? throw new ArgumentNullException(nameof(gpu));
            _logger       = logger       ?? throw new ArgumentNullException(nameof(logger));
            _taskbarCtrl  = taskbarCtrl  ?? throw new ArgumentNullException(nameof(taskbarCtrl));
            _explorerBlur = explorerBlur ?? throw new ArgumentNullException(nameof(explorerBlur));
            _win11Icons   = win11Icons   ?? throw new ArgumentNullException(nameof(win11Icons));
            _cursorTheme  = cursorTheme  ?? throw new ArgumentNullException(nameof(cursorTheme));
            _gamerMode    = gamerMode    ?? throw new ArgumentNullException(nameof(gamerMode));

            _gamerMode.StatusChanged += OnGamerModeStatusChanged;
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;

            ApplyProfileCommand       = new AsyncRelayCommand(p => ApplyProfileAsync(p as string));
            DetectBottlenecksCommand  = new AsyncRelayCommand(_ => DetectBottlenecksAsync());
            RestoreDefaultsCommand    = new AsyncRelayCommand(_ => RestoreDefaultsAsync());
            LoadCommand               = new AsyncRelayCommand(_ => LoadAsync());
            InstallExplorerBlurCommand   = new AsyncRelayCommand(_ => InstallExplorerBlurAsync());
            UninstallExplorerBlurCommand = new AsyncRelayCommand(_ => UninstallExplorerBlurAsync());
            ApplyCursorThemeCommand  = new AsyncRelayCommand(p =>
            {
                if (p is CursorThemeInfo theme)
                    SelectedCursorTheme = theme;
                return ApplyCursorThemeAsync();
            });
            RestoreCursorCommand = new AsyncRelayCommand(_ => RestoreCursorAsync());

            ExplorerBlurDllPresent = _explorerBlur.IsDllPresent();
            ExplorerBlurInstalled  = _explorerBlur.IsInstalled;

            _explorerBlur.InstalledStateResolved += OnExplorerBlurInstalledStateResolved;

            _logger.LogInfo($"{TAG} ViewModel criado.");
            _logger.LogInfo($"{TAG} [Explorer] DLL presente: {ExplorerBlurDllPresent}");
            _logger.LogInfo($"{TAG} [Explorer] Ja instalado: {ExplorerBlurInstalled}");
        }

        public async Task LoadAsync()
        {
            _logger.LogInfo($"{TAG} [LoadAsync] Iniciando carregamento...");
            await RunBusyAsync(() => LoadInternalAsync(), busyMessage: LocalizationService.Instance.GetString("LoadingConfigurations"));
        }

        private async Task LoadInternalAsync()
        {
            _logger.LogInfo($"{TAG} [LoadInternalAsync] Sincronizando UI com preferencias salvas e hardware...");
            
            var s = SettingsService.Instance.Settings;
            
            _isGamerModeInternalChange = true;
            try
            {
                WindowAnimations  = s.PersonalizeWindowAnimations;
                MenuAnimations    = s.PersonalizeMenuAnimations;
                TaskbarAnimations = s.PersonalizeTaskbarAnimations;
                DropShadows       = s.PersonalizeDropShadows;
                FontSmoothing     = s.PersonalizeFontSmoothing;
                TransparencyEffects = s.PersonalizeTransparencyEffects;
                HardwareAcceleration = s.PersonalizeHardwareAcceleration;
                ExplorerHighPerf     = s.PersonalizeExplorerHighPerf;
                HardwareScheduling   = s.PersonalizeHardwareScheduling;
                GamingGpuPriority    = s.PersonalizeGamingGpuPriority;
                MpoEnabled           = s.PersonalizeMpoEnabled;
                SelectedProfile = s.PersonalizeProfilePlan;

                _logger.LogDebug($"{TAG} [LoadAsync] Passo 2: Detectando GPU...");
                var gpus = await _gpu.GetGpuInfoAsync();
                GpuName = gpus.Count > 0 ? gpus[0].Name : LocalizationService.Instance.GetString("GpuNotDetected");

                _logger.LogDebug($"{TAG} [LoadAsync] Passo 3: Verificando Icones Win11...");
                _win11IconsEnabled = _win11Icons.IsInstalled();
                OnPropertyChanged(nameof(Win11IconsEnabled));
                _logger.LogInfo($"{TAG} [LoadAsync] Icones Win11 habilitados: {_win11IconsEnabled}");

                _logger.LogDebug($"{TAG} [LoadAsync] Passo 4: Carregando temas de Cursor...");
                await LoadCursorThemesAsync();

                _logger.LogDebug($"{TAG} [LoadAsync] Passo 5: Verificando VoltrisBlur...");
                ExplorerBlurInstalled = await _explorerBlur.GetInstalledStateAsync();
                _taskbarCtrl.SetVoltrisBlurActive(ExplorerBlurInstalled);
            }
            finally
            {
                _isGamerModeInternalChange = false;
            }

            var savedThemeName = SettingsService.Instance.Settings.SelectedCursorTheme;
            if (!string.IsNullOrEmpty(savedThemeName))
            {
                var found = AvailableCursorThemes.FirstOrDefault(t =>
                    string.Equals(t.Name, savedThemeName, StringComparison.OrdinalIgnoreCase));
                if (found != null)
                {
                    _selectedCursorTheme = found;
                    OnPropertyChanged(nameof(SelectedCursorTheme));
                    _logger.LogInfo($"{TAG} [Cursor] Tema salvo restaurado na UI: '{savedThemeName}'");
                }
            }

            _explorerEffectIndex   = s.VoltrisBlurEffectIndex;
            _explorerClearAddress  = s.VoltrisBlurClearAddress;
            _explorerClearBarBg    = s.VoltrisBlurClearBarBg;
            _explorerClearWinUIBg  = s.VoltrisBlurClearWinUIBg;
            _explorerShowLine      = s.VoltrisBlurShowLine;
            _explorerAlpha         = s.VoltrisBlurAlpha;
            _explorerColorR        = s.VoltrisBlurColorR;
            _explorerColorG        = s.VoltrisBlurColorG;
            _explorerColorB        = s.VoltrisBlurColorB;
            OnPropertyChanged(nameof(ExplorerEffectIndex));
            OnPropertyChanged(nameof(ExplorerClearAddress));
            OnPropertyChanged(nameof(ExplorerClearBarBg));
            OnPropertyChanged(nameof(ExplorerClearWinUIBg));
            OnPropertyChanged(nameof(ExplorerShowLine));
            OnPropertyChanged(nameof(ExplorerAlpha));
            OnPropertyChanged(nameof(ExplorerColorR));
            OnPropertyChanged(nameof(ExplorerColorG));
            OnPropertyChanged(nameof(ExplorerColorB));

            _taskbarStyleIndex   = s.TaskbarStyleIndex;
            _taskbarOpacity      = s.TaskbarOpacity;
            _taskbarStyleEnabled = s.TaskbarStyleEnabled;
            OnPropertyChanged(nameof(TaskbarStyleIndex));
            OnPropertyChanged(nameof(TaskbarOpacity));
            OnPropertyChanged(nameof(TaskbarStyleEnabled));

            if (_taskbarStyleEnabled) 
                ApplyTaskbarStyle();

            // CARREGAR CENTRALIZAÇÃO DA TASKBAR
            _logger.LogInfo($"{TAG} [LoadAsync] TaskbarCenteringEnabled salvo: {s.TaskbarCenteringEnabled}, IsWin11: {IsWin11}");
            if (s.TaskbarCenteringEnabled)
            {
                _logger.LogInfo($"{TAG} [LoadAsync] ✅ Centralização estava habilitada nas configurações!");
                _taskbarCenteringEnabled = true;
                OnPropertyChanged(nameof(TaskbarCenteringEnabled));
                
                if (!IsWin11)
                {
                    _logger.LogInfo($"{TAG} [LoadAsync] Windows 10 detectado - chamando SetCentering(true)...");
                    _taskbarCtrl.SetCentering(true);
                }
                else
                {
                    _logger.LogInfo($"{TAG} [LoadAsync] Windows 11 detectado - registro nativo já deve estar aplicando");
                }
            }
            else
            {
                _logger.LogInfo($"{TAG} [LoadAsync] Centralização estava DESABILITADA nas configurações");
            }

            SetStatus(LocalizationService.Instance.GetString("ConfigurationsLoaded"), "#00FF88");
            _logger.LogSuccess($"{TAG} [LoadAsync] Carregamento concluido. GPU={GpuName}");
        }

        private async Task ApplyProfileAsync(string? profileParam = null)
        {
            if (profileParam != null)
            {
                SelectedProfileName = profileParam;
            }

            _logger.LogInfo($"{TAG} [ApplyProfileAsync] Aplicando perfil: {SelectedProfile}");
            await RunBusyAsync(async () =>
            {
                GlobalProgressService.Instance.StartOperation(string.Format(LocalizationService.Instance.GetString("ApplyingProfile"), SelectedProfileName), true);
                try
                {
                    await _tweaks.ApplyProfileAsync(SelectedProfile);
                    GlobalProgressService.Instance.UpdateProgress(50, LocalizationService.Instance.GetString("ProfileAppliedSyncing"));

                    var sv = SettingsService.Instance.Settings;
                    sv.PersonalizeProfilePlan = SelectedProfile;

                    switch (SelectedProfile)
                    {
                        case PersonalizeProfile.Performance:
                            sv.PersonalizeWindowAnimations = false;
                            sv.PersonalizeMenuAnimations = false;
                            sv.PersonalizeTaskbarAnimations = false;
                            sv.PersonalizeDropShadows = false;
                            sv.PersonalizeHardwareAcceleration = true;
                            sv.PersonalizeExplorerHighPerf = true;
                            break;
                        case PersonalizeProfile.Ultra:
                            sv.PersonalizeWindowAnimations = false;
                            sv.PersonalizeMenuAnimations = false;
                            sv.PersonalizeTaskbarAnimations = false;
                            sv.PersonalizeDropShadows = false;
                            sv.PersonalizeFontSmoothing = false;
                            sv.PersonalizeTransparencyEffects = false;
                            sv.PersonalizeHardwareAcceleration = true;
                            sv.PersonalizeExplorerHighPerf = true;
                            break;
                        case PersonalizeProfile.Normal:
                            sv.PersonalizeWindowAnimations = true;
                            sv.PersonalizeMenuAnimations = true;
                            sv.PersonalizeTaskbarAnimations = true;
                            sv.PersonalizeDropShadows = true;
                            sv.PersonalizeFontSmoothing = true;
                            sv.PersonalizeTransparencyEffects = true;
                            sv.PersonalizeHardwareAcceleration = false;
                            sv.PersonalizeExplorerHighPerf = false;
                            break;
                    }

                    SettingsService.Instance.SaveSettings();
                    GlobalProgressService.Instance.UpdateProgress(80, LocalizationService.Instance.GetString("SettingsSavedUpdatingUI"));

                    await LoadInternalAsync();
                    
                    HistoryService.RecordActivity("System Optimization", string.Format(LocalizationService.Instance.GetString("ProfileAppliedHistory"), SelectedProfileName));
                    SetStatus(string.Format(LocalizationService.Instance.GetString("ProfileAppliedStatus"), SelectedProfileName), "#00FF88");
                    GlobalNotificationService.ShowSuccess(LocalizationService.Instance.GetString("Personalize"), string.Format(LocalizationService.Instance.GetString("ProfileAppliedNotification"), SelectedProfileName));
                    _logger.LogSuccess($"{TAG} [ApplyProfileAsync] Perfil {SelectedProfile} aplicado e persistido.");
                    GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("ProfileAppliedOp"), SelectedProfileName));
                }
                catch (Exception ex)
                {
                    GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("ProfileApplyErrorOp"), ex.Message));
                    throw;
                }
            }, busyMessage: string.Format(LocalizationService.Instance.GetString("ApplyingProfile"), SelectedProfileName));
        }

        private async Task DetectBottlenecksAsync()
        {
            _logger.LogInfo($"{TAG} [DetectBottlenecksAsync] Iniciando analise...");
            await RunBusyAsync(async () =>
            {
                GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("AnalyzingVisualBottlenecks"), false);
                try
                {
                    GlobalProgressService.Instance.UpdateProgress(30, LocalizationService.Instance.GetString("CheckingVisualSettings"));
                    BottleneckReport = await _tweaks.DetectVisualBottlenecksAsync();
                    GlobalProgressService.Instance.UpdateProgress(100, LocalizationService.Instance.GetString("AnalysisComplete"));
                    SetStatus(LocalizationService.Instance.GetString("AnalysisCompleteStatus"), "#00FF88");
                    _logger.LogInfo($"{TAG} [DetectBottlenecksAsync] Relatorio: {BottleneckReport}");
                    GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("BottleneckAnalysisCompleteOp"));
                }
                catch (Exception ex)
                {
                    GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("BottleneckAnalysisErrorOp"), ex.Message));
                    throw;
                }
            }, busyMessage: LocalizationService.Instance.GetString("AnalyzingVisualBottlenecks"));
        }

        private async Task RestoreDefaultsAsync()
        {
            _logger.LogInfo($"{TAG} [RestoreDefaultsAsync] Restaurando TODAS as configuracoes para o original do Windows...");
            await RunBusyAsync(async () =>
            {
                GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("RestoringDefaultConfigurations"), true);
                try
                {
                    await _tweaks.ApplyProfileAsync(PersonalizeProfile.Normal);
                    GlobalProgressService.Instance.UpdateProgress(15, LocalizationService.Instance.GetString("ProfileRestored"));

                    await _gpu.SetHardwareSchedulingAsync(false);
                    await _gpu.SetGamingGpuPriorityAsync(false);
                    await _gpu.SetMpoAsync(true);
                    GlobalProgressService.Instance.UpdateProgress(30, LocalizationService.Instance.GetString("GpuRestored"));
                    
                    _taskbarCtrl.SetCentering(false);
                    _logger.LogInfo($"{TAG} [RestoreDefaults] Centralizacao de icones restaurada para padrao (esquerda).");
                    
                    _taskbarCtrl.SetStyle(false);
                    _logger.LogInfo($"{TAG} [RestoreDefaults] Efeitos visuais do taskbar restaurados para padrao Windows.");
                    
                    if (_explorerBlur.IsInstalled)
                    {
                        _logger.LogInfo($"{TAG} [RestoreDefaults] Removendo VoltrisBlur para restaurar Explorer original...");
                        await _explorerBlur.UninstallAsync();
                    }
                    GlobalProgressService.Instance.UpdateProgress(50, LocalizationService.Instance.GetString("ExplorerRestored"));
                    
                    if (_win11Icons.IsApplied)
                    {
                        _logger.LogInfo($"{TAG} [RestoreDefaults] Restaurando icones para padrao Windows 10...");
                        await _win11Icons.RestoreDefaultIconsAsync();
                    }
                    
                    if (!string.IsNullOrEmpty(SettingsService.Instance.Settings.SelectedCursorTheme))
                    {
                        _logger.LogInfo($"{TAG} [RestoreDefaults] Restaurando cursor para padrao Windows...");
                        await _cursorTheme.RestoreDefaultAsync();
                        SettingsService.Instance.Settings.SelectedCursorTheme = string.Empty;
                    }
                    GlobalProgressService.Instance.UpdateProgress(70, LocalizationService.Instance.GetString("IconsAndCursorRestored"));
                    
                    SettingsService.Instance.Settings.TaskbarCenteringEnabled = false;
                    SettingsService.Instance.Settings.TaskbarStyleEnabled = false;
                    SettingsService.Instance.Settings.VoltrisBlurInstalled = false;
                    SettingsService.Instance.Settings.Win11IconsEnabled = false;
                    SettingsService.Instance.SaveSettings();
                    
                    await LoadInternalAsync();
                    
                    SetStatus(LocalizationService.Instance.GetString("AllConfigurationsRestoredStatus"), "#00FF88");
                    GlobalNotificationService.ShowSuccess(LocalizationService.Instance.GetString("Personalize"), LocalizationService.Instance.GetString("AllConfigurationsRestoredNotification"));
                    _logger.LogSuccess($"{TAG} [RestoreDefaults] 🎉 RESTAURACAO COMPLETA: Sistema restaurado para configuracoes originais do Windows.");
                    GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("AllConfigurationsRestoredOp"));
                }
                catch (Exception ex)
                {
                    GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("RestoreDefaultsErrorOp"), ex.Message));
                    throw;
                }
            }, busyMessage: LocalizationService.Instance.GetString("RestoringDefaultConfigurations"));
        }

        private void SetStatus(string msg, string color)
        {
            StatusMessage = msg;
            StatusColor   = color;
        }

        private async Task RunBusyAsync(Func<Task> action, string busyMessage = "Processando...")
        {
            int waited = 0;
            while (IsBusy && waited < 5000)
            {
                await Task.Delay(100);
                waited += 100;
            }

            if (IsBusy)
            {
                _logger.LogWarning($"{TAG} [RunBusyAsync] IsBusy ainda true apos 5s de espera — forcando IsBusy=false para prosseguir.");
                IsBusy = false;
            }

            _logger.LogInfo($"{TAG} [RunBusyAsync] Iniciando acao: {busyMessage}");
            try
            {
                IsBusy = true;
                BusyMessage = busyMessage;
                await action();
            }
            catch (Exception ex)
            {
                SetStatus(string.Format(LocalizationService.Instance.GetString("Error"), ex.Message), "#FF4466");
                _logger.LogError($"{TAG} [RunBusyAsync] Excecao capturada: {ex.GetType().Name}: {ex.Message}", ex);
                if (ex.InnerException != null)
                    _logger.LogError($"{TAG} [RunBusyAsync] InnerException: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}", ex.InnerException);
            }
            finally
            {
                IsBusy = false;
                BusyMessage = string.Empty;
                _logger.LogInfo($"{TAG} [RunBusyAsync] Acao concluida: {busyMessage}");
            }
        }

        private void OnExplorerBlurInstalledStateResolved(object? sender, bool isInstalled)
        {
            _logger.LogInfo($"{TAG} [InstalledStateResolved] IsInstalled={isInstalled} — atualizando botao.");
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                ExplorerBlurInstalled = isInstalled;
                _taskbarCtrl.SetVoltrisBlurActive(isInstalled);
            });
        }

        private async Task InstallExplorerBlurAsync()
        {
            _logger.LogInfo($"{TAG} [InstallExplorerBlur] Comando acionado pelo usuario.");
            _logger.LogInfo($"{TAG} [InstallExplorerBlur] Efeito={ExplorerEffectIndex} ClearAddr={ExplorerClearAddress} ClearBar={ExplorerClearBarBg} ClearWinUI={ExplorerClearWinUIBg} ShowLine={ExplorerShowLine}");
            _logger.LogInfo($"{TAG} [InstallExplorerBlur] Alpha={ExplorerAlpha} R={ExplorerColorR} G={ExplorerColorG} B={ExplorerColorB}");
            _logger.LogInfo($"{TAG} [InstallExplorerBlur] JaInstalado={ExplorerBlurInstalled} — usando {(ExplorerBlurInstalled ? "ApplyConfigOnly" : "Install")}");
            _logger.LogInfo($"{TAG} [InstallExplorerBlur] [light] e [dark] = R={ExplorerColorR} G={ExplorerColorG} B={ExplorerColorB} A={ExplorerAlpha}");

            await RunBusyAsync(async () =>
            {
                GlobalProgressService.Instance.StartOperation(
                    ExplorerBlurInstalled ? LocalizationService.Instance.GetString("ApplyingVoltrisBlurConfig") : LocalizationService.Instance.GetString("InstallingVoltrisBlur"), true);
                try
                {
                    SetStatus(LocalizationService.Instance.GetString("ApplyingVoltrisBlur"), "#31A8FF");

                    bool ok;
                    if (ExplorerBlurInstalled)
                    {
                        ok = await _explorerBlur.ApplyConfigOnlyAsync(
                            effect:       (ExplorerEffect)ExplorerEffectIndex,
                            clearAddress: ExplorerClearAddress,
                            clearBarBg:   ExplorerClearBarBg,
                            clearWinUI:   false,
                            showLine:     ExplorerShowLine,
                            alpha:        (byte)ExplorerAlpha,
                            r:            (byte)ExplorerColorR,
                            g:            (byte)ExplorerColorG,
                            b:            (byte)ExplorerColorB);
                    }
                    else
                    {
                        ok = await _explorerBlur.InstallAsync(
                            effect:       (ExplorerEffect)ExplorerEffectIndex,
                            clearAddress: ExplorerClearAddress,
                            clearBarBg:   ExplorerClearBarBg,
                            clearWinUIBg: ExplorerClearWinUIBg,
                            showLine:     ExplorerShowLine,
                            lightR: ExplorerColorR, lightG: ExplorerColorG, lightB: ExplorerColorB, lightA: ExplorerAlpha,
                            darkR:  ExplorerColorR, darkG:  ExplorerColorG, darkB:  ExplorerColorB, darkA:  ExplorerAlpha);
                    }

                    GlobalProgressService.Instance.UpdateProgress(70, LocalizationService.Instance.GetString("FinalizingConfiguration"));
                    ExplorerBlurInstalled = ok || ExplorerBlurInstalled;
                    if (ok)
                    {
                        var s = SettingsService.Instance.Settings;
                        s.VoltrisBlurInstalled    = true;
                        s.VoltrisBlurEffectIndex  = ExplorerEffectIndex;
                        s.VoltrisBlurClearAddress = ExplorerClearAddress;
                        s.VoltrisBlurClearBarBg   = ExplorerClearBarBg;
                        s.VoltrisBlurShowLine     = ExplorerShowLine;
                        s.VoltrisBlurAlpha        = ExplorerAlpha;
                        s.VoltrisBlurColorR       = ExplorerColorR;
                        s.VoltrisBlurColorG       = ExplorerColorG;
                        s.VoltrisBlurColorB       = ExplorerColorB;
                        SettingsService.Instance.SaveSettings();
                        _logger.LogInfo($"{TAG} [InstallExplorerBlur] Configuracoes persistidas.");

                        SetStatus(ExplorerBlurInstalled ? LocalizationService.Instance.GetString("VoltrisBlurApplied") : LocalizationService.Instance.GetString("VoltrisBlurInstalled"), "#00FF88");
                        GlobalNotificationService.ShowSuccess(LocalizationService.Instance.GetString("VoltrisBlur"), LocalizationService.Instance.GetString("VoltrisBlurAppliedSuccess"));
                        _logger.LogSuccess($"{TAG} [InstallExplorerBlur] Aplicado com sucesso.");
                        GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("VoltrisBlurAppliedOp"));
                    }
                    else
                    {
                        SetStatus(LocalizationService.Instance.GetString("VoltrisBlurApplyFailedStatus"), "#FF4466");
                        GlobalNotificationService.ShowError(LocalizationService.Instance.GetString("VoltrisBlur"), LocalizationService.Instance.GetString("VoltrisBlurApplyFailedMsg"));
                        _logger.LogError($"{TAG} [InstallExplorerBlur] Falha na aplicacao.", null);
                        GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("VoltrisBlurApplyFailedOp"));
                    }
                }
                catch (Exception ex)
                {
                    GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("VoltrisBlurApplyErrorOp"), ex.Message));
                    throw;
                }
            }, busyMessage: ExplorerBlurInstalled ? LocalizationService.Instance.GetString("ApplyingConfigurations") : LocalizationService.Instance.GetString("InstallingVoltrisBlur"));
        }

        private async Task UninstallExplorerBlurAsync()
        {
            _logger.LogInfo($"{TAG} [UninstallExplorerBlur] Comando acionado pelo usuario.");
            await RunBusyAsync(async () =>
            {
                GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("RemovingVoltrisBlur"), true);
                try
                {
                    SetStatus(LocalizationService.Instance.GetString("RemovingVoltrisBlur"), "#FFAA00");
                    bool ok = await _explorerBlur.UninstallAsync();
                    ExplorerBlurInstalled = !ok ? ExplorerBlurInstalled : false;
                    if (ok)
                    {
                        SettingsService.Instance.Settings.VoltrisBlurInstalled = false;
                        SettingsService.Instance.SaveSettings();
                        _logger.LogInfo($"{TAG} [UninstallExplorerBlur] VoltrisBlurInstalled=false persistido.");
                        SetStatus(LocalizationService.Instance.GetString("VoltrisBlurRemovedStatus"), "#00FF88");
                        GlobalNotificationService.ShowSuccess(LocalizationService.Instance.GetString("VoltrisBlur"), LocalizationService.Instance.GetString("VoltrisBlurRemovedSuccess"));
                        _logger.LogSuccess($"{TAG} [UninstallExplorerBlur] Removido com sucesso.");
                        GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("VoltrisBlurRemovedOp"));
                    }
                    else
                    {
                        SetStatus(LocalizationService.Instance.GetString("VoltrisBlurRemoveFailedStatus"), "#FF4466");
                        GlobalNotificationService.ShowError(LocalizationService.Instance.GetString("VoltrisBlur"), LocalizationService.Instance.GetString("VoltrisBlurRemoveFailedMsg"));
                        _logger.LogError($"{TAG} [UninstallExplorerBlur] Falha na remocao.", null);
                        GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("VoltrisBlurRemoveFailedOp"));
                    }
                }
                catch (Exception ex)
                {
                    GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("VoltrisBlurRemoveErrorOp"), ex.Message));
                    throw;
                }
            }, busyMessage: LocalizationService.Instance.GetString("RemovingVoltrisBlur"));
        }

        private async Task LoadCursorThemesAsync()
        {
            try
            {
                _logger.LogInfo($"{TAG} [Cursor] Carregando temas disponiveis...");
                
                var themes = await Task.Run(() => _cursorTheme.GetAvailableThemes());

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    AvailableCursorThemes.Clear();
                    foreach (var t in themes)
                        AvailableCursorThemes.Add(t);
                    _logger.LogInfo($"{TAG} [Cursor] {AvailableCursorThemes.Count} temas carregados.");
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"{TAG} [Cursor] Erro ao carregar temas: {ex.Message}", ex);
            }
        }

        private async Task ApplyCursorThemeAsync()
        {
            if (_selectedCursorTheme == null)
            {
                CursorStatusMessage = LocalizationService.Instance.GetString("SelectThemeBeforeApplying");
                CursorStatusColor   = "#FFAA00";
                _logger.LogWarning($"{TAG} [Cursor] ApplyCursorThemeAsync chamado sem tema selecionado.");
                return;
            }

            _logger.LogInfo($"{TAG} [Cursor] Aplicando tema: '{_selectedCursorTheme.Name}'");
            await RunBusyAsync(async () =>
            {
                GlobalProgressService.Instance.StartOperation(string.Format(LocalizationService.Instance.GetString("ApplyingCursorTheme"), _selectedCursorTheme.DisplayName), true);
                try
                {
                    CursorStatusMessage = string.Format(LocalizationService.Instance.GetString("ApplyingCursorThemeStatus"), _selectedCursorTheme.DisplayName);
                    CursorStatusColor   = "#31A8FF";
                    bool ok = await _cursorTheme.ApplyThemeAsync(_selectedCursorTheme);
                    if (ok)
                    {
                        SettingsService.Instance.Settings.SelectedCursorTheme = _selectedCursorTheme.Name;
                        SettingsService.Instance.SaveSettings();
                        CursorStatusMessage = string.Format(LocalizationService.Instance.GetString("CursorThemeAppliedStatus"), _selectedCursorTheme.DisplayName);
                        CursorStatusColor   = "#00FF88";
                        GlobalNotificationService.ShowSuccess(LocalizationService.Instance.GetString("Cursor"), string.Format(LocalizationService.Instance.GetString("CursorThemeAppliedNotification"), _selectedCursorTheme.DisplayName));
                        _logger.LogSuccess($"{TAG} [Cursor] Tema '{_selectedCursorTheme.Name}' aplicado e salvo.");
                        GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("CursorThemeAppliedOp"), _selectedCursorTheme.DisplayName));
                    }
                    else
                    {
                        CursorStatusMessage = LocalizationService.Instance.GetString("CursorThemeApplyFailedStatus");
                        CursorStatusColor   = "#FF4466";
                        GlobalNotificationService.ShowError(LocalizationService.Instance.GetString("Cursor"), LocalizationService.Instance.GetString("CursorThemeApplyFailedMsg"));
                        _logger.LogError($"{TAG} [Cursor] Falha ao aplicar tema '{_selectedCursorTheme.Name}'.", null);
                        GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("CursorThemeApplyFailedOp"));
                    }
                }
                catch (Exception ex)
                {
                    GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("CursorThemeApplyErrorOp"), ex.Message));
                    throw;
                }
            }, busyMessage: string.Format(LocalizationService.Instance.GetString("ApplyingCursorTheme"), _selectedCursorTheme.DisplayName));
        }

        private async Task RestoreCursorAsync()
        {
            _logger.LogInfo($"{TAG} [Cursor] Restaurando cursor padrao do Windows...");
            await RunBusyAsync(async () =>
            {
                GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("RestoringDefaultCursor"), true);
                try
                {
                    CursorStatusMessage = LocalizationService.Instance.GetString("RestoringDefaultCursor");
                    CursorStatusColor   = "#FFAA00";
                    bool ok = await _cursorTheme.RestoreDefaultAsync();
                    if (ok)
                    {
                        SettingsService.Instance.Settings.SelectedCursorTheme = string.Empty;
                        SettingsService.Instance.SaveSettings();
                        SelectedCursorTheme = null;
                        CursorStatusMessage = LocalizationService.Instance.GetString("DefaultCursorRestoredStatus");
                        CursorStatusColor   = "#00FF88";
                        GlobalNotificationService.ShowSuccess(LocalizationService.Instance.GetString("Cursor"), LocalizationService.Instance.GetString("DefaultCursorRestoredNotification"));
                        _logger.LogSuccess($"{TAG} [Cursor] Cursor padrao restaurado.");
                        GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("DefaultCursorRestoredOp"));
                    }
                    else
                    {
                        CursorStatusMessage = LocalizationService.Instance.GetString("DefaultCursorRestoreFailedStatus");
                        CursorStatusColor   = "#FF4466";
                        GlobalNotificationService.ShowError(LocalizationService.Instance.GetString("Cursor"), LocalizationService.Instance.GetString("DefaultCursorRestoreFailedMsg"));
                        GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("DefaultCursorRestoreFailedOp"));
                    }
                }
                catch (Exception ex)
                {
                    GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("DefaultCursorRestoreErrorOp"), ex.Message));
                    throw;
                }
            }, busyMessage: LocalizationService.Instance.GetString("RestoringDefaultCursor"));
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            var currentEnum = SelectedProfile;
            SelectedProfileName = currentEnum switch
            {
                PersonalizeProfile.Performance => LocalizationService.Instance.GetString("ProfileGamer"),
                PersonalizeProfile.Normal => LocalizationService.Instance.GetString("ProfileWork"),
                PersonalizeProfile.Ultra => LocalizationService.Instance.GetString("ProfileMaxPerformance"),
                PersonalizeProfile.Custom => LocalizationService.Instance.GetString("Custom"),
                _ => LocalizationService.Instance.GetString("ProfileWork")
            };
        }

        private void OnGamerModeStatusChanged(object? sender, VoltrisOptimizer.Services.Gamer.Models.GamerModeStatus status)
        {
            _logger.LogInfo($"{TAG} Gamer Mode status alterado: IsActive={status.IsActive}. Sincronizando UI...");
            
            System.Windows.Application.Current?.Dispatcher?.Invoke(() => 
            {
                _isGamerModeInternalChange = true;
                try
                {
                    if (status.IsActive)
                    {
                        _logger.LogInfo($"{TAG} [GamerMode] Ativo: Forcando otimizacoes visuais maximas na UI.");
                        
                        WindowAnimations      = false;
                        MenuAnimations        = false;
                        TaskbarAnimations     = true;
                        TaskbarAnimations     = false;
                        DropShadows           = false;
                        TransparencyEffects   = false;
                        HardwareAcceleration  = true;
                        ExplorerHighPerf      = true;
                        HardwareScheduling    = true;
                        GamingGpuPriority     = true;
                        MpoEnabled            = false;
                    }
                    else
                    {
                        _logger.LogInfo($"{TAG} [GamerMode] Inativo: Restaurando preferencias salvas do usuario.");
                        var s = SettingsService.Instance.Settings;
                        WindowAnimations      = s.PersonalizeWindowAnimations;
                        MenuAnimations        = s.PersonalizeMenuAnimations;
                        TaskbarAnimations     = s.PersonalizeTaskbarAnimations;
                        DropShadows           = s.PersonalizeDropShadows;
                        FontSmoothing         = s.PersonalizeFontSmoothing;
                        TransparencyEffects   = s.PersonalizeTransparencyEffects;
                        HardwareAcceleration  = s.PersonalizeHardwareAcceleration;
                        ExplorerHighPerf      = s.PersonalizeExplorerHighPerf;
                        HardwareScheduling    = s.PersonalizeHardwareScheduling;
                        GamingGpuPriority     = s.PersonalizeGamingGpuPriority;
                        MpoEnabled            = s.PersonalizeMpoEnabled;
                        SelectedProfile       = s.PersonalizeProfilePlan;
                    }
                }
                finally
                {
                    _isGamerModeInternalChange = false;
                }
            });
        }

        private void SavePreference(Action<AppSettings> updateAction)
        {
            if (!_isGamerModeInternalChange)
            {
                var s = SettingsService.Instance.Settings;
                updateAction(s);
                SettingsService.Instance.SaveSettings();
            }
        }
    }
}
