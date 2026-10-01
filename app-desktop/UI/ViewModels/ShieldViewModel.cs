using System;
using System.Diagnostics;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.License.Interfaces;
using VoltrisOptimizer.Services.License.Models;
using VoltrisOptimizer.Services.Shield;
using VoltrisOptimizer.Services.Shield.Network;
using VoltrisOptimizer.Utils.Win32;

namespace VoltrisOptimizer.UI.ViewModels
{
    public class ShieldViewModel : ViewModelBase, IDisposable
    {
        private bool _disposed = false;
        private readonly VoltrisShieldService _shieldService;
        private readonly NetworkMonitorService _networkMonitor;
        private readonly DeviceTrackerService _deviceTracker;
        private readonly StartupMonitorService _startupMonitor;
        private readonly AdwareScannerService _adwareScanner;
        private readonly RansomwareMonitorService _ransomwareMonitor;
        private readonly ThreatProtectionService _threatProtection;
        private readonly ILoggingService _logger;
        private readonly ShieldLicenseGate _licenseGate;
        private readonly ILicenseDialogService _licenseDialogService;

        private bool _isShieldLicensed;
        private string _licenseTypeDisplay = "None";

        /// <summary>Indica que existe licença Standard/Pro/Enterprise ativa.</summary>
        public bool IsShieldLicensed
        {
            get => _isShieldLicensed;
            private set => SetProperty(ref _isShieldLicensed, value);
        }

        /// <summary>Indica que o Voltris Shield está bloqueado por falta de licença.</summary>
        public bool IsShieldBlocked
        {
            get => !IsShieldLicensed;
            private set => OnPropertyChanged();
        }

        /// <summary>Tipo de licença detectado, exibido na tela de bloqueio.</summary>
        public string LicenseTypeDisplay
        {
            get => _licenseTypeDisplay;
            private set => SetProperty(ref _licenseTypeDisplay, value);
        }

        /// <summary>
        /// Habilita as ações do Shield. O bloqueio é definido pela LICENÇA
        /// (Standard/Pro/Enterprise). Protection desligada não deixa o botão
        /// morto: o comando é executado e devolve um aviso claro ao usuário.
        /// </summary>
        public bool CanRunShieldAction => IsShieldLicensed;

        #region Navegação por abas

        private int _shieldTabIndex;

        /// <summary>Índice da aba ativa do Voltris Shield.</summary>
        public int ShieldTabIndex
        {
            get => _shieldTabIndex;
            set
            {
                if (SetProperty(ref _shieldTabIndex, value))
                {
                    OnPropertyChanged(nameof(ShieldTabOverview));
                    OnPropertyChanged(nameof(ShieldTabScans));
                    OnPropertyChanged(nameof(ShieldTabThreats));
                    OnPropertyChanged(nameof(ShieldTabNetwork));
                    OnPropertyChanged(nameof(ShieldTabSystem));
                    OnPropertyChanged(nameof(ShieldTabAdvanced));

                    // A aba de Rede só era populada por scan manual ou por evento,
                    // então abria vazia. Carrega ao entrar.
                    if (value == 3) LoadNetworkTabData();
                }
            }
        }

        /// <summary>
        /// Carrega dispositivos e conexões da aba de Rede. Idempotente e segura para
        /// chamar a cada entrada na aba: só refaz a leitura quando ainda está vazia,
        /// e ignora quando não há licença.
        /// </summary>
        private void LoadNetworkTabData()
        {
            if (!_licenseGate.IsLicensed) return;

            try
            {
                if (NetworkDevices.Count == 0)
                {
                    UpdateNetworkDevicesList();
                }

                if (AllActiveConnections.Count == 0)
                {
                    // Silencioso: entrar na aba não deve disparar notificação.
                    RefreshConnections(showNotification: false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ShieldVM] [LoadNetworkTabData] Erro ao carregar dados da aba de rede: {ex.Message}", ex);
            }
        }

        public bool ShieldTabOverview => _shieldTabIndex == 0;
        public bool ShieldTabScans => _shieldTabIndex == 1;
        public bool ShieldTabThreats => _shieldTabIndex == 2;
        public bool ShieldTabNetwork => _shieldTabIndex == 3;
        public bool ShieldTabSystem => _shieldTabIndex == 4;
        public bool ShieldTabAdvanced => _shieldTabIndex == 5;

        public ICommand SelectTabCommand { get; }

        private void SelectTab(object? parameter)
        {
            if (parameter == null) return;
            if (!int.TryParse(parameter.ToString(), out var index)) return;
            if (index < 0 || index > 5) return;
            ShieldTabIndex = index;
        }

        #endregion

        /// <summary>
        /// Ponto único de verificação usado por TODOS os comandos do Shield.
        /// Se estiver sem licença: registra o bloqueio e abre o mesmo modal de
        /// compra usado pelos demais botões PRO (Reparo Inteligente, Perfil, etc).
        /// </summary>
        private bool EnsureShieldLicense(string operation)
        {
            if (_licenseGate.IsAllowed(operation))
            {
                SetLicenseState(true, _licenseGate.CurrentLicenseType);
                return true;
            }

            SetLicenseState(false, _licenseGate.CurrentLicenseType);
            try
            {
                _licenseDialogService.ShowLicenseBlocked(
                    new LicenseState
                    {
                        LicenseType = _licenseGate.CurrentLicenseType ?? "None",
                        IsActive = false
                    },
                    ShieldLicenseGate.FeatureId);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ShieldVM] Erro ao abrir modal de licença: {ex.Message}", ex);
            }

            return false;
        }

        /// <summary>
        /// Executa uma ação da página do Shield sempre tieda à barra de progresso
        /// global do footer. TODOS os botões da página passam por aqui, garantindo que
        /// nenhum fique de fora da barra global.
        /// </summary>
        private async Task RunWithGlobalProgressAsync(string titleKey, Func<Task> action)
        {
            var token = GlobalProgressService.Instance.BeginOperation(
                LocalizationService.Instance.GetString(titleKey), isPriority: false);
            try
            {
                token.UpdateProgress(15, LocalizationService.Instance.GetString("ShieldOpWorking"));
                await action();
                token.UpdateProgress(100, LocalizationService.Instance.GetString("ShieldOpDone"));
                token.Complete(LocalizationService.Instance.GetString("ShieldOpDone"));
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ShieldVM] Falha em '{titleKey}'", ex);
                token.Fail(string.Format(
                    LocalizationService.Instance.GetString("ShieldOpFailed"), ex.Message));
            }
        }

        /// <summary>
        /// Versão síncrona de <see cref="RunWithGlobalProgressAsync"/> para ações de
        /// atualização e alternância (não bloqueantes).
        /// </summary>
        private void RunWithGlobalProgress(string titleKey, Action action)
        {
            var token = GlobalProgressService.Instance.BeginOperation(
                LocalizationService.Instance.GetString(titleKey), isPriority: false);
            try
            {
                token.UpdateProgress(40, LocalizationService.Instance.GetString("ShieldOpWorking"));
                action();
                token.UpdateProgress(100, LocalizationService.Instance.GetString("ShieldOpDone"));
                token.Complete(LocalizationService.Instance.GetString("ShieldOpDone"));
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ShieldVM] Falha em '{titleKey}'", ex);
                token.Fail(string.Format(
                    LocalizationService.Instance.GetString("ShieldOpFailed"), ex.Message));
            }
        }

        /// <summary>
        /// Reage à troca de idioma nas Configurações: recalcula todas as strings
        /// derivadas e notifica a UI.
        /// </summary>
        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            try
            {
                void Refresh()
                {
                    if (_disposed) return;

                    _licenseTypeDisplay = _licenseGate.CurrentLicenseType;
                    OnPropertyChanged(nameof(LicenseTypeDisplay));

                    if (!IsShieldLicensed)
                    {
                        StatusMessage = LocalizationService.Instance.GetString("ShieldRequiresLicense");
                    }
                    else if (IsProtectionActive)
                    {
                        StatusMessage = LocalizationService.Instance.GetString("ShieldProtectionEnabled");
                    }
                    else
                    {
                        StatusMessage = LocalizationService.Instance.GetString("ProtectionDisabled");
                    }
                    OnPropertyChanged(nameof(StatusMessage));

                    // Coleções: cada item recalcula o próprio texto no getter.
                    RefreshDerivedTexts(NetworkDevices);
                    RefreshDerivedTexts(StartupItems);
                    RefreshDerivedTexts(AdwareItems);
                    RefreshDerivedTexts(QuarantineItems);
                    RefreshDerivedTexts(ScanThreatItems);
                    RefreshDerivedTexts(SuspiciousConnections);
                    RefreshDerivedTexts(AllActiveConnections);
                    RefreshDerivedTexts(ActiveProcessThreats);

                    OnPropertyChanged(nameof(LastScanTime));
                    OnPropertyChanged(nameof(OnlineDevicesCount));
                    OnPropertyChanged(nameof(TotalDevicesCount));
                    OnPropertyChanged(nameof(ActiveConnectionsCount));
                    OnPropertyChanged(nameof(SuspiciousConnectionsCount));
                    OnPropertyChanged(nameof(IsDefenderEnabled));
                    OnPropertyChanged(nameof(IsDefenderUpToDate));
                    OnPropertyChanged(nameof(ScanProgressText));
                    OnPropertyChanged(nameof(RansomwareStatus));
                }

                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.CheckAccess()) Refresh();
                else dispatcher.BeginInvoke(Refresh);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ShieldVM] Erro ao aplicar novo idioma: {ex.Message}", ex);
            }
        }

        private static void RefreshDerivedTexts<T>(System.Collections.ObjectModel.ObservableCollection<T> items)
        {
            if (items == null) return;
            foreach (var item in items)
            {
                if (item is ViewModelBase vm)
                {
                    vm.RefreshAllLocalizedProperties();
                }
            }
        }

        private void SetLicenseState(bool licensed, string licenseType)        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            void Apply()
            {
                if (IsShieldLicensed != licensed) IsShieldLicensed = licensed;
                OnPropertyChanged(nameof(IsShieldBlocked));
                OnPropertyChanged(nameof(CanRunShieldAction));
                if (LicenseTypeDisplay != licenseType) LicenseTypeDisplay = licenseType;

                // Modo Gamer: ATIVADO por padrão, porém somente com licença válida.
                if (IsGamerModeEnabled != licensed)
                {
                    IsGamerModeEnabled = licensed;
                }
            }

            if (dispatcher == null || dispatcher.CheckAccess()) Apply();            else dispatcher.BeginInvoke(Apply);
        }

        private void OnShieldLicenseChanged(object? sender, ShieldLicenseChangedEventArgs e)
        {
            _logger.LogInfo($"[ShieldVM] Licença alterada: licensed={e.IsLicensed}, tipo={e.LicenseType}");
            SetLicenseState(e.IsLicensed, e.LicenseType);
        }

        /// <summary>
        /// Aviso amigável quando o comando é acionado com a proteção desligada.
        /// Evita botão "morto" e evita resultado enganoso de "0 ameaças".
        /// </summary>
        private bool EnsureProtectionEnabled(string operation)
        {
            if (IsProtectionActive) return true;

            var msg = LocalizationService.Instance.GetString("ShieldRequiresProtectionActive");
            StatusMessage = msg;
            GlobalNotificationService.ShowWarning("Shield", msg);
            _logger.LogWarning($"[ShieldVM] '{operation}' ignorado: proteção está desativada");
            return false;
        }

        
        private bool _isProtectionActive;
        private bool _isScanning;
        private bool _isGamerModeEnabled;
        private string _lastScanTime = LocalizationService.Instance.GetString("Never");
        private string _statusMessage = LocalizationService.Instance.GetString("ProtectionDisabled");
        private int _threatsDetected;
        private DefenderStatus? _defenderStatus;
        
        // Scan Results
        private bool _isScanResultVisible;
        private string _scanResultSummary = string.Empty;
        private string _scanResultType = string.Empty;
        private int _scanProgress;
        private string _scanProgressText = string.Empty;
        private ObservableCollection<ScanThreatItemViewModel> _scanThreatItems;
        
        // Network Guardian
        private bool _isNetworkMonitoringActive;
        private bool _isNetworkScanning;
        private int _onlineDevicesCount;
        private int _totalDevicesCount;
        private ObservableCollection<NetworkDeviceViewModel> _networkDevices;
        
        // Startup Monitor
        private bool _isStartupScanning;
        private ObservableCollection<StartupItemViewModel> _startupItems;
        private int _startupItemsCount;
        
        // Adware Scanner
        private bool _isAdwareScanning;
        private ObservableCollection<AdwareItemViewModel> _adwareItems;
        private int _adwareItemsCount;
        
        // Ransomware Monitor
        private bool _isRansomwareMonitoringActive;
        private string _ransomwareStatus = LocalizationService.Instance.GetString("Inactive");
        private int _ransomwareAlertsCount;

        /// <summary>
        /// [FIX:SHIELD-CONTAGEM-ZERADA] HISTÓRICO DE ALERTAS DE RANSOMWARE DA SESSÃO.
        ///
        /// O contador `RansomwareAlertsCount` era exibido na Visão Geral e nunca
        /// tinha NENHUMA escrita: não existia coleção que o alimentasse, nem o
        /// serviço guardava histórico. O número ficava em zero para sempre — e,
        /// pior, um zero que a tela apresentava com a mesma segurança de um
        /// "realmente não houve ataques".
        ///
        /// O `RansomwareMonitorService` é exatamente o componente que deveria
        /// ser a fonte da verdade — é ele que detecta e que emite o evento. A
        /// correção é fazer o serviço guardar o que já sabe, pela mesma razão do
        /// histórico de ameaças: o dado é do serviço, e a UI é uma das
        /// consumidoras. Um contador mantido só pela UI morre junto com a UI.
        /// </summary>
        private readonly System.Collections.Generic.List<VoltrisOptimizer.Services.Shield.RansomwareAlertEventArgs> _ransomwareAlertHistory = new();
        private readonly object _ransomwareHistoryLock = new();

        private int RansomwareAlertCount
        {
            get { lock (_ransomwareHistoryLock) { return _ransomwareAlertHistory.Count; } }
        }
        
        // Quarantine
        private ObservableCollection<QuarantineItemViewModel> _quarantineItems;
        private int _quarantineCount;
        
        // Port Monitor
        private bool _isPortMonitoringActive;
        private int _activeConnectionsCount;
        private int _suspiciousConnectionsCount;
        private ObservableCollection<ConnectionViewModel> _suspiciousConnections;
        private ObservableCollection<ConnectionViewModel> _allActiveConnections;
        
        // Threat Protection
        private bool _isThreatProtectionActive;
        private int _activeThreatsCount;
        private ObservableCollection<ProcessThreatViewModel> _activeProcessThreats;
        
        #region Properties
        
        public bool IsProtectionActive
        {
            get => _isProtectionActive;
            set
            {
                if (SetProperty(ref _isProtectionActive, value))
                {
                    OnPropertyChanged(nameof(StatusColor));
                    OnPropertyChanged(nameof(StatusIcon));
                    OnPropertyChanged(nameof(CanRunShieldAction));
                }
            }
        }
        
        public bool IsScanning
        {
            get => _isScanning;
            set => SetProperty(ref _isScanning, value);
        }
        
        public bool IsGamerModeEnabled
        {
            get => _isGamerModeEnabled;
            set
            {
                // Trava real: sem licença o Modo Gamer não pode LIGAR.
                // O serviço já rejeita a chamada, mas sem esta guarda a UI
                // exibia o toggle ligado enquanto nada acontecia de fato.
                if (value && !_isShieldLicensed)
                {
                    if (_isGamerModeEnabled)
                    {
                        SetProperty(ref _isGamerModeEnabled, false);
                    }
                    _logger.LogWarning("[ShieldVM] Modo Gamer bloqueado: sem licença válida");
                    return;
                }

                if (SetProperty(ref _isGamerModeEnabled, value))
                {
                    if (_isShieldLicensed)
                    {
                        _shieldService.SetGamerMode(value);
                    }
                }
            }
        }
        
        public string LastScanTime
        {
            get => _lastScanTime;
            set
            {
                if (SetProperty(ref _lastScanTime, value))
                    OnPropertyChanged(nameof(LastScanDisplay));
            }
        }
        
        public string LastScanDisplay => string.Format(LocalizationService.Instance.GetString("Loc_LastScanned"), _lastScanTime);
        
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }
        
        public int ThreatsDetected
        {
            get => _threatsDetected;
            set
            {
                if (SetProperty(ref _threatsDetected, value))
                    OnPropertyChanged(nameof(ThreatsDetectedDisplay));
            }
        }

        public string ThreatsDetectedDisplay => string.Format(LocalizationService.Instance.GetString("Loc_ThreatsDetectedCount"), _threatsDetected);
        
        // Scan Results Properties
        public bool IsScanResultVisible
        {
            get => _isScanResultVisible;
            set => SetProperty(ref _isScanResultVisible, value);
        }
        
        public string ScanResultSummary
        {
            get => _scanResultSummary;
            set => SetProperty(ref _scanResultSummary, value);
        }
        
        public string ScanResultType
        {
            get => _scanResultType;
            set => SetProperty(ref _scanResultType, value);
        }
        
        public int ScanProgress
        {
            get => _scanProgress;
            set => SetProperty(ref _scanProgress, value);
        }
        
        public string ScanProgressText
        {
            get => _scanProgressText;
            set => SetProperty(ref _scanProgressText, value);
        }
        
        public ObservableCollection<ScanThreatItemViewModel> ScanThreatItems
        {
            get => _scanThreatItems;
            set => SetProperty(ref _scanThreatItems, value);
        }
        
        public Brush StatusColor => IsProtectionActive 
            ? new SolidColorBrush(Color.FromRgb(0, 255, 136)) 
            : new SolidColorBrush(Color.FromRgb(255, 68, 102));
        
        public string StatusIcon => IsProtectionActive ? "✓" : "⚠";
        
        public bool IsDefenderEnabled => _defenderStatus?.IsEnabled ?? false;
        public bool IsDefenderUpToDate => _defenderStatus?.IsUpToDate ?? false;
        
        // Network Guardian Properties
        public bool IsNetworkMonitoringActive
        {
            get => _isNetworkMonitoringActive;
            set => SetProperty(ref _isNetworkMonitoringActive, value);
        }
        
        public bool IsNetworkScanning
        {
            get => _isNetworkScanning;
            set => SetProperty(ref _isNetworkScanning, value);
        }
        
        public int OnlineDevicesCount
        {
            get => _onlineDevicesCount;
            set => SetProperty(ref _onlineDevicesCount, value);
        }
        
        public int TotalDevicesCount
        {
            get => _totalDevicesCount;
            set => SetProperty(ref _totalDevicesCount, value);
        }
        
        public ObservableCollection<NetworkDeviceViewModel> NetworkDevices
        {
            get => _networkDevices;
            set => SetProperty(ref _networkDevices, value);
        }
        
        // Startup Monitor Properties
        public bool IsStartupScanning
        {
            get => _isStartupScanning;
            set => SetProperty(ref _isStartupScanning, value);
        }
        
        public ObservableCollection<StartupItemViewModel> StartupItems
        {
            get => _startupItems;
            set => SetProperty(ref _startupItems, value);
        }
        
        public int StartupItemsCount
        {
            get => _startupItemsCount;
            set => SetProperty(ref _startupItemsCount, value);
        }
        
        // Adware Scanner Properties
        public bool IsAdwareScanning
        {
            get => _isAdwareScanning;
            set => SetProperty(ref _isAdwareScanning, value);
        }
        
        public ObservableCollection<AdwareItemViewModel> AdwareItems
        {
            get => _adwareItems;
            set => SetProperty(ref _adwareItems, value);
        }
        
        public int AdwareItemsCount
        {
            get => _adwareItemsCount;
            set => SetProperty(ref _adwareItemsCount, value);
        }
        
        // Ransomware Monitor Properties
        public bool IsRansomwareMonitoringActive
        {
            get => _isRansomwareMonitoringActive;
            set => SetProperty(ref _isRansomwareMonitoringActive, value);
        }
        
        public string RansomwareStatus
        {
            get => _ransomwareStatus;
            set => SetProperty(ref _ransomwareStatus, value);
        }
        
        public int RansomwareAlertsCount
        {
            get => _ransomwareAlertsCount;
            set => SetProperty(ref _ransomwareAlertsCount, value);
        }
        
        // Quarantine Properties
        public ObservableCollection<QuarantineItemViewModel> QuarantineItems
        {
            get => _quarantineItems;
            set => SetProperty(ref _quarantineItems, value);
        }
        
        public int QuarantineCount
        {
            get => _quarantineCount;
            set => SetProperty(ref _quarantineCount, value);
        }
        
        // Port Monitor Properties
        public bool IsPortMonitoringActive
        {
            get => _isPortMonitoringActive;
            set => SetProperty(ref _isPortMonitoringActive, value);
        }
        
        public int ActiveConnectionsCount
        {
            get => _activeConnectionsCount;
            set => SetProperty(ref _activeConnectionsCount, value);
        }
        
        public int SuspiciousConnectionsCount
        {
            get => _suspiciousConnectionsCount;
            set => SetProperty(ref _suspiciousConnectionsCount, value);
        }
        
        public ObservableCollection<ConnectionViewModel> SuspiciousConnections
        {
            get => _suspiciousConnections;
            set => SetProperty(ref _suspiciousConnections, value);
        }
        
        public ObservableCollection<ConnectionViewModel> AllActiveConnections
        {
            get => _allActiveConnections;
            set => SetProperty(ref _allActiveConnections, value);
        }
        
        // Threat Protection Properties
        public bool IsThreatProtectionActive
        {
            get => _isThreatProtectionActive;
            set => SetProperty(ref _isThreatProtectionActive, value);
        }
        
        public int ActiveThreatsCount
        {
            get => _activeThreatsCount;
            set => SetProperty(ref _activeThreatsCount, value);
        }
        
        public ObservableCollection<ProcessThreatViewModel> ActiveProcessThreats
        {
            get => _activeProcessThreats;
            set => SetProperty(ref _activeProcessThreats, value);
        }
        
        #endregion
        
        #region Commands
        
        public ICommand ToggleProtectionCommand { get; }
        public ICommand RunQuickScanCommand { get; }
        public ICommand RunFullScanCommand { get; }
        public ICommand RunAdwareScanCommand { get; }
        public ICommand StartDefenderScanCommand { get; }
        public ICommand RefreshDefenderStatusCommand { get; }
        
        // Network Guardian Commands
        public ICommand ToggleNetworkMonitoringCommand { get; }
        public ICommand RunNetworkScanCommand { get; }
        
        // Startup Monitor Commands
        public ICommand ScanStartupItemsCommand { get; }
        public ICommand DisableStartupItemCommand { get; }
        public ICommand RemoveStartupItemCommand { get; }
        
        // Adware Scanner Commands
        public ICommand ScanAdwareCommand { get; }
        public ICommand RemoveAdwareItemCommand { get; }
        
        // Ransomware Monitor Commands
        public ICommand ToggleRansomwareMonitoringCommand { get; }
        
        // Quarantine Commands
        public ICommand RefreshQuarantineCommand { get; }
        public ICommand RestoreQuarantineItemCommand { get; }
        public ICommand DeleteQuarantineItemCommand { get; }
        
        // Port Monitor Commands
        public ICommand TogglePortMonitoringCommand { get; }
        public ICommand RefreshConnectionsCommand { get; }
        
        // Threat Protection Commands
        public ICommand ToggleThreatProtectionCommand { get; }
        public ICommand RefreshActiveThreatsCommand { get; }
        
        // Scan Results Commands
        public ICommand RemoveScanThreatCommand { get; }
        public ICommand IgnoreScanThreatCommand { get; }
        public ICommand QuarantineScanThreatCommand { get; }
        public ICommand ClearScanResultsCommand { get; }
        
        #endregion
        
        #region Constructor
        
        public ShieldViewModel(
            VoltrisShieldService shieldService, 
            NetworkMonitorService networkMonitor,
            DeviceTrackerService deviceTracker,
            StartupMonitorService startupMonitor,
            AdwareScannerService adwareScanner,
            RansomwareMonitorService ransomwareMonitor,
            ThreatProtectionService threatProtection,
            ILoggingService logger,
            ShieldLicenseGate licenseGate,
            ILicenseDialogService licenseDialogService)
        {
            _shieldService = shieldService ?? throw new ArgumentNullException(nameof(shieldService));
            _networkMonitor = networkMonitor ?? throw new ArgumentNullException(nameof(networkMonitor));
            _deviceTracker = deviceTracker ?? throw new ArgumentNullException(nameof(deviceTracker));
            _startupMonitor = startupMonitor ?? throw new ArgumentNullException(nameof(startupMonitor));
            _adwareScanner = adwareScanner ?? throw new ArgumentNullException(nameof(adwareScanner));
            _ransomwareMonitor = ransomwareMonitor ?? throw new ArgumentNullException(nameof(ransomwareMonitor));
            _threatProtection = threatProtection ?? throw new ArgumentNullException(nameof(threatProtection));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _licenseGate = licenseGate ?? throw new ArgumentNullException(nameof(licenseGate));
            _licenseDialogService = licenseDialogService ?? throw new ArgumentNullException(nameof(licenseDialogService));
            _isShieldLicensed = _licenseGate.IsLicensed;
            _licenseTypeDisplay = _licenseGate.CurrentLicenseType;

            try
            {
                _licenseGate.LicenseChanged += OnShieldLicenseChanged;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ShieldVM] Erro ao assinar gate de licença: {ex.Message}");
            }

            try
            {
                // Sem esta assinatura, trocar o idioma nas Configurações atualizava apenas
                // os textos vinculados via XAML. As strings calculadas aqui (StatusMessage,
                // LicenseTypeDisplay, DeviceInfo, ConfidenceText, etc.) ficariam no idioma
                // antigo até recarregar a página.
                LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ShieldVM] Erro ao assinar LanguageChanged: {ex.Message}");
            }
            
            // Inicializar coleções
            _networkDevices = new ObservableCollection<NetworkDeviceViewModel>();
            _startupItems = new ObservableCollection<StartupItemViewModel>();
            _adwareItems = new ObservableCollection<AdwareItemViewModel>();
            _scanThreatItems = new ObservableCollection<ScanThreatItemViewModel>();
            _quarantineItems = new ObservableCollection<QuarantineItemViewModel>();
            _suspiciousConnections = new ObservableCollection<ConnectionViewModel>();
            _allActiveConnections = new ObservableCollection<ConnectionViewModel>();
            _activeProcessThreats = new ObservableCollection<ProcessThreatViewModel>();
            
            // Conectar eventos
            _shieldService.StatusChanged += OnShieldStatusChanged;
            _shieldService.ThreatDetected += OnThreatDetected;
            _networkMonitor.StatusChanged += OnNetworkMonitoringStatusChanged;
            _deviceTracker.NewDeviceDetected += OnNewDeviceDetected;
            _deviceTracker.DeviceDisconnected += OnDeviceDisconnected;
            _ransomwareMonitor.StatusChanged += OnRansomwareMonitoringStatusChanged;
            _ransomwareMonitor.SuspiciousActivityDetected += OnRansomwareAlertDetected;

            // [FIX:SHIELD-CONTAGEM-ZERADA] RECUPERAR O QUE JÁ TINHA ACONTECIDO.
            //
            // Este ViewModel só existe quando o usuário abre a aba Shield, e é
            // aqui que ele se inscreve em `ThreatDetected`. A proteção, porém,
            // roda desde o startup. Tudo o que fosse detectado antes de a aba
            // abrir foi perdido para a UI — e o resultado visível era
            // "Ameaças detectadas: 0" com notificações aparecendo normalmente.
            //
            // O log do usuário mostrava 5 ameaças de alto risco às 02:18:29 e
            // ZERO linhas deste ViewModel, porque ele ainda não existia.
            //
            // A recuperação é simples e explícita: o serviço, que existe desde o
            // início, guarda o histórico da sessão. Ao abrir a aba, o ViewModel
            // busca essa lista e reconstrói a contagem. A partir daí o fluxo
            // normal (evento ao vivo) continua como estava.
            // [FIX:SHIELD-CONTAGEM-ZERADA] SINCRONIZAÇÃO MOVIDA PARA O `Loaded`.
            //
            // A recuperação do histórico ficava aqui, no construtor, e nunca
            // rodava a tempo: este ViewModel é um singleton do contêiner,
            // construído bem antes de o usuário abrir a aba, e as ameaças ainda
            // não tinham acontecido.
            //
            // Ela agora é feita por `SyncFromServiceOnDisplay()`, chamada
            // quando a aba é EXIBIDA. Ver `ShieldView.xaml.cs`.
            SyncFromServiceOnDisplay();

            // O PortMonitor já é iniciado pelo VoltrisShieldService na ativação da proteção.
            // Sem este assinatura a lista de conexões nunca era preenchida automaticamente,
            // porque antes só era assinado ao ativar o monitoramento manualmente.
            try
            {
                _shieldService.PortMonitor.SnapshotUpdated += OnPortSnapshotUpdated;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ShieldVM] Erro ao assinar SnapshotUpdated do PortMonitor: {ex.Message}");
            }
            
            // Comandos principais
            // IMPORTANTE 1: os predicados de CanExecute NÃO podem depender de IsProtectionActive.
            // No WPF, um Button com Command tem seu estado habilitado definido por CanExecute,
            // e o IsEnabled definido no XAML é ignorado. Colocar IsProtectionActive aqui travava
            // todos os botões. A licença é validada dentro dos métodos (EnsureShieldLicense) e
            // proteção desligada gera aviso amigável (EnsureProtectionEnabled).
            //
            // IMPORTANTE 2: TODA ação da página é envolvida por RunWithGlobalProgress(Async),
            // para que nenhum botão fique de fora da barra de progresso global do footer.
            ToggleProtectionCommand = new AsyncRelayCommand(async () =>
                await RunWithGlobalProgressAsync("ShieldOpActivateProtection", async () => await ToggleProtectionAsync()));
            RunQuickScanCommand = new AsyncRelayCommand(async () =>
                await RunWithGlobalProgressAsync("ShieldOpQuickScan", async () => await RunQuickScanAsync()), () => !IsScanning);
            RunFullScanCommand = new AsyncRelayCommand(async () =>
                await RunWithGlobalProgressAsync("ShieldOpFullScan", async () => await RunFullScanAsync()), () => !IsScanning);
            RunAdwareScanCommand = new AsyncRelayCommand(async () =>
                await RunWithGlobalProgressAsync("ShieldOpAdwareScan", async () => await RunAdwareScanAsync()), () => !IsScanning);
            StartDefenderScanCommand = new AsyncRelayCommand(async () =>
                await RunWithGlobalProgressAsync("ShieldOpDefenderScan", async () => await StartDefenderScanAsync()));
            RefreshDefenderStatusCommand = new AsyncRelayCommand(async () =>
                await RunWithGlobalProgressAsync("ShieldOpRefreshingStatus", async () => await RefreshDefenderStatusAsync()));
            
            // Network Guardian Commands
            ToggleNetworkMonitoringCommand = new AsyncRelayCommand(async () =>
                await RunWithGlobalProgressAsync("ShieldOpNetworkMonitor", async () => await ToggleNetworkMonitoringAsync()));
            RunNetworkScanCommand = new AsyncRelayCommand(async () =>
                await RunWithGlobalProgressAsync("ShieldOpNetworkScan", async () => await RunNetworkScanAsync()), () => !IsNetworkScanning);
            
            // Startup Monitor Commands
            ScanStartupItemsCommand = new AsyncRelayCommand(async () =>
                await RunWithGlobalProgressAsync("ShieldOpStartupScan", async () => await ScanStartupItemsAsync()), () => !IsStartupScanning);
            DisableStartupItemCommand = new AsyncRelayCommand(async (item) =>
                await RunWithGlobalProgressAsync("ShieldOpDisablingItem", async () => await DisableStartupItemAsync(item as StartupItemViewModel)));
            RemoveStartupItemCommand = new AsyncRelayCommand(async (item) =>
                await RunWithGlobalProgressAsync("ShieldOpRemovingItem", async () => await RemoveStartupItemAsync(item as StartupItemViewModel)));
            
            // Adware Scanner Commands
            ScanAdwareCommand = new AsyncRelayCommand(async () =>
                await RunWithGlobalProgressAsync("ShieldOpAdwareItemScan", async () => await ScanAdwareAsync()), () => !IsAdwareScanning);
            RemoveAdwareItemCommand = new AsyncRelayCommand(async (item) =>
                await RunWithGlobalProgressAsync("ShieldOpRemovingItem", async () => await RemoveAdwareItemAsync(item as AdwareItemViewModel)));
            
            // Ransomware Monitor Commands
            ToggleRansomwareMonitoringCommand = new AsyncRelayCommand(async () =>
                await RunWithGlobalProgressAsync("ShieldOpRansomwareMonitor", async () => await ToggleRansomwareMonitoringAsync()));
            
            // Quarantine Commands
            RefreshQuarantineCommand = new RelayCommand(() =>
                RunWithGlobalProgress("ShieldOpRefreshingQuarantine", () => RefreshQuarantineList(promptIfBlocked: true)));
            RestoreQuarantineItemCommand = new AsyncRelayCommand(async (item) =>
                await RunWithGlobalProgressAsync("ShieldOpRestoringItem", async () => await RestoreQuarantineItemAsync(item as QuarantineItemViewModel)));
            DeleteQuarantineItemCommand = new AsyncRelayCommand(async (item) =>
                await RunWithGlobalProgressAsync("ShieldOpDeletingItem", async () => await DeleteQuarantineItemAsync(item as QuarantineItemViewModel)));
            
            // Port Monitor Commands
            TogglePortMonitoringCommand = new RelayCommand(() =>
                RunWithGlobalProgress("ShieldOpPortMonitor", () => TogglePortMonitoring()));
            RefreshConnectionsCommand = new RelayCommand(() =>
                RunWithGlobalProgress("ShieldOpRefreshConnections", () => RefreshConnections()));
            
            // Threat Protection Commands
            ToggleThreatProtectionCommand = new RelayCommand(() =>
                RunWithGlobalProgress("ShieldOpThreatProtection", () => ToggleThreatProtection()));
            RefreshActiveThreatsCommand = new RelayCommand(() =>
                RunWithGlobalProgress("ShieldOpRefreshingThreats", () => RefreshActiveThreats()));
            
            // Scan Results Commands
            RemoveScanThreatCommand = new AsyncRelayCommand(async (item) =>
                await RunWithGlobalProgressAsync("ShieldOpRemovingItem", async () => await RemoveScanThreatAsync(item as ScanThreatItemViewModel)));
            IgnoreScanThreatCommand = new RelayCommand((item) =>
                RunWithGlobalProgress("ShieldOpIgnoringThreat", () => IgnoreScanThreat(item as ScanThreatItemViewModel)));
            QuarantineScanThreatCommand = new AsyncRelayCommand(async (item) =>
                await RunWithGlobalProgressAsync("ShieldOpQuarantiningThreat", async () => await QuarantineScanThreatAsync(item as ScanThreatItemViewModel)));
            ClearScanResultsCommand = new RelayCommand(() =>
                RunWithGlobalProgress("ShieldOpClearingResults", () =>
                {
                    System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        IsScanResultVisible = false;
                        ScanThreatItems.Clear();
                    });
                }));

            // Troca de aba é instantânea e não deve acionar a barra global.
            SelectTabCommand = new RelayCommand(SelectTab);
            
            // Inicializar
            _ = InitializeAsync();
        }
        
        #endregion
        
        #region Initialization
        
        private async Task InitializeAsync()
        {
            _logger.LogInfo("[ShieldVM] [InitializeAsync] INICIADO - Inicializando Voltris Shield");
            try
            {
                SetLicenseState(_licenseGate.IsLicensed, _licenseGate.CurrentLicenseType);
                _logger.LogInfo($"[ShieldVM] Estado de licença: licensed={IsShieldLicensed}, tipo={LicenseTypeDisplay}");

                 IsProtectionActive = _shieldService.IsProtectionActive;
                 StatusMessage = !IsShieldLicensed
                     ? LocalizationService.Instance.GetString("ShieldRequiresLicense")
                     : IsProtectionActive
                        ? LocalizationService.Instance.GetString("ShieldProtectionEnabled")
                        : LocalizationService.Instance.GetString("ProtectionDisabled");
                 SyncProtectionModuleState();
                
                // Carregar apenas dados estáticos que não dependem de monitoramento ativo
                _logger.LogInfo("[ShieldVM] Carregando quarentena...");
                RefreshQuarantineList();
                
                await RefreshDefenderStatusAsync();
                
                if (_shieldService.LastScanTime.HasValue)
                {
                    LastScanTime = _shieldService.LastScanTime.Value.ToString("dd/MM/yyyy HH:mm");
                }
                
                _logger.LogSuccess("[ShieldVM] Voltris Shield inicializado em modo de espera");
                _logger.LogInfo("[ShieldVM] [InitializeAsync] FINALIZADO com sucesso");
            }
            catch (Exception ex)
            {
                _logger.LogError("[ShieldVM] [InitializeAsync] Erro na inicialização", ex);
            }
        }
        
        #endregion
        
        #region Protection Toggle
        
        private async Task ToggleProtectionAsync()
        {
            if (!EnsureShieldLicense("Ativar/Desativar proteção")) return;

            _logger.LogInfo("[ShieldVM] [ToggleProtectionAsync] INICIADO - Alternando proteção global");
            try
            {
                if (IsProtectionActive)
                {
                    bool deactivated = await _shieldService.DeactivateProtectionAsync();
                    if (!deactivated)
                    {
                        StatusMessage = LocalizationService.Instance.GetString("ErrorTogglingProtection");
                        return;
                    }

                    IsProtectionActive = false;
                    SyncProtectionModuleState();
                    HistoryService.RecordActivity("Shield Protection", LocalizationService.Instance.GetString("ShieldProtectionDeactivated"));
                    GlobalNotificationService.ShowWarning("Shield", LocalizationService.Instance.GetString("ShieldProtectionDeactivated"));
                }
                else
                {
                    bool activated = await _shieldService.ActivateProtectionAsync();
                    if (!activated)
                    {
                        IsProtectionActive = false;
                        SyncProtectionModuleState();
                        StatusMessage = LocalizationService.Instance.GetString("ErrorTogglingProtection");
                        return;
                    }

                    IsProtectionActive = true;
                    SyncProtectionModuleState();
                    HistoryService.RecordActivity("Shield Protection", LocalizationService.Instance.GetString("ShieldProtectionActivated"));
                    GlobalNotificationService.ShowSuccess("Shield", LocalizationService.Instance.GetString("ShieldProtectionActivated"));
                }

                CommandManager.InvalidateRequerySuggested();
                _logger.LogInfo("[ShieldVM] [ToggleProtectionAsync] FINALIZADO com sucesso");
            }
            catch (Exception ex)
            {
                _logger.LogError("[ShieldVM] [ToggleProtectionAsync] Erro ao alternar proteção", ex);
                StatusMessage = LocalizationService.Instance.GetString("ErrorTogglingProtection");
            }
        }

        private void SyncProtectionModuleState()
        {
            void ApplyState()
            {
                IsNetworkMonitoringActive = _shieldService.IsNetworkMonitoringActive;
                IsRansomwareMonitoringActive = _shieldService.IsRansomwareMonitoringActive;
                IsPortMonitoringActive = _shieldService.IsPortMonitoringActive;
                IsThreatProtectionActive = _shieldService.IsThreatProtectionActive;
                OnPropertyChanged(nameof(StatusColor));
                OnPropertyChanged(nameof(StatusIcon));
                CommandManager.InvalidateRequerySuggested();
            }

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                ApplyState();
            }
            else
            {
                dispatcher.InvokeAsync(ApplyState);
            }
        }
        
        #endregion
        
        #region Scan Methods
        
        public async Task RunQuickScanAsync()
        {
            if (!EnsureShieldLicense("Scan rápido")) return;
            if (!EnsureProtectionEnabled("Scan rápido")) return;

            _logger.LogInfo("[ShieldVM] [RunQuickScanAsync] INICIADO - Executando Quick Scan");
            bool started = false;
            try
            {
                IsScanning = true;
                IsScanResultVisible = false;
                ScanProgress = 0;
                ScanProgressText = LocalizationService.Instance.GetString("StartingQuickScan");
                StatusMessage = LocalizationService.Instance.GetString("RunningQuickScan");
                _logger.LogInfo("[ShieldVM] Iniciando Quick Scan...");

                // REPARO: Verificar retorno — se bloqueado, não chamar CompleteOperation
                started = VoltrisOptimizer.Services.GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("QuickScan"), isPriority: true);

                var result = await _shieldService.RunQuickScanAsync((progress, message) =>
                {
                    ScanProgress = progress;
                    ScanProgressText = message;
                    if (started) VoltrisOptimizer.Services.GlobalProgressService.Instance.UpdateProgress(progress, message);
                });

                ThreatsDetected = result.ThreatsFound;
                LastScanTime = result.CompletedAt.ToString("dd/MM/yyyy HH:mm");
                StatusMessage = result.ThreatsFound > 0
                    ? string.Format(LocalizationService.Instance.GetString("QuickScanThreatsFound"), result.ThreatsFound, result.ItemsScanned)
                    : string.Format(LocalizationService.Instance.GetString("QuickScanClean"), result.ItemsScanned);

                PopulateScanResults(result);

                try
                {
                    var shieldNotification = App.Services?.GetService(typeof(ShieldNotificationService)) as ShieldNotificationService;
                    shieldNotification?.NotifyScanComplete(result);
                }
                catch (Exception innerEx)
                {
                    _logger.LogWarning($"[ShieldVM] Falha ao notificar scan completo no QuickScan: {innerEx.Message}");
                }

                _logger.LogDebug($"[ShieldVM] [RunQuickScanAsync] Scan concluído: {result.ThreatsFound} ameaças, {result.ItemsScanned} itens");

                if (started)
                {
                    HistoryService.RecordActivity("Security Scan", result.ThreatsFound > 0 
                        ? string.Format(LocalizationService.Instance.GetString("QuickScanDetectedThreats"), result.ThreatsFound) 
                        : LocalizationService.Instance.GetString("QuickScanSystemSafe"));

                    VoltrisOptimizer.Services.GlobalProgressService.Instance.CompleteOperation(
                        result.ThreatsFound > 0
                            ? string.Format(LocalizationService.Instance.GetString("QuickScanThreatsFoundShort"), result.ThreatsFound)
                            : LocalizationService.Instance.GetString("QuickScanCleanShort"));
                    GlobalNotificationService.ShowSuccess("Shield",
                        result.ThreatsFound > 0
                            ? string.Format(LocalizationService.Instance.GetString("QuickScanThreatsFoundShort"), result.ThreatsFound)
                            : LocalizationService.Instance.GetString("QuickScanCleanShort"));
                }
                _logger.LogInfo("[ShieldVM] [RunQuickScanAsync] FINALIZADO com sucesso");
            }
            catch (Exception ex)
            {
                _logger.LogError("[ShieldVM] [RunQuickScanAsync] Erro no scan rápido", ex);
                StatusMessage = LocalizationService.Instance.GetString("QuickScanError");
                ScanProgressText = LocalizationService.Instance.GetString("ScanErrorGeneric");
                if (started) VoltrisOptimizer.Services.GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("QuickScanError"));
                GlobalNotificationService.ShowError("Shield", LocalizationService.Instance.GetString("QuickScanError"));
            }
            finally
            {
                IsScanning = false;
                ScanProgress = 0;
            }
        }
        
        public async Task RunFullScanAsync()
        {
            if (!EnsureShieldLicense("Scan completo")) return;
            if (!EnsureProtectionEnabled("Scan completo")) return;

            _logger.LogInfo("[ShieldVM] [RunFullScanAsync] INICIADO - Executando Full Scan");
            bool started = false;
            try
            {
                IsScanning = true;
                IsScanResultVisible = false;
                ScanProgress = 0;
                ScanProgressText = LocalizationService.Instance.GetString("StartingFullScan");
                StatusMessage = LocalizationService.Instance.GetString("RunningFullScan");
                _logger.LogInfo("[ShieldVM] Iniciando Full Scan...");

                started = VoltrisOptimizer.Services.GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("FullScan"), isPriority: true);

                var result = await _shieldService.RunFullScanAsync((progress, message) =>
                {
                    ScanProgress = progress;
                    ScanProgressText = message;
                    if (started) VoltrisOptimizer.Services.GlobalProgressService.Instance.UpdateProgress(progress, message);
                });

                ThreatsDetected = result.ThreatsFound;
                LastScanTime = result.CompletedAt.ToString("dd/MM/yyyy HH:mm");
                StatusMessage = result.ThreatsFound > 0
                    ? string.Format(LocalizationService.Instance.GetString("FullScanThreatsFound"), result.ThreatsFound, result.ItemsScanned)
                    : string.Format(LocalizationService.Instance.GetString("FullScanClean"), result.ItemsScanned);

                PopulateScanResults(result);

                try
                {
                    var shieldNotification = App.Services?.GetService(typeof(ShieldNotificationService)) as ShieldNotificationService;
                    shieldNotification?.NotifyScanComplete(result);
                }
                catch (Exception innerEx)
                {
                    _logger.LogWarning($"[ShieldVM] Falha ao notificar scan completo no DeepScan: {innerEx.Message}");
                }

                _logger.LogDebug($"[ShieldVM] [RunFullScanAsync] Scan concluído: {result.ThreatsFound} ameaças, {result.ItemsScanned} itens");

                if (started)
                {
                    HistoryService.RecordActivity("Security Scan", result.ThreatsFound > 0 
                        ? string.Format(LocalizationService.Instance.GetString("FullScanIdentifiedThreats"), result.ThreatsFound) 
                        : LocalizationService.Instance.GetString("FullScanPassed"));

                    VoltrisOptimizer.Services.GlobalProgressService.Instance.CompleteOperation(
                        result.ThreatsFound > 0
                            ? string.Format(LocalizationService.Instance.GetString("FullScanThreatsFoundShort"), result.ThreatsFound)
                            : LocalizationService.Instance.GetString("FullScanCleanShort"));
                    GlobalNotificationService.ShowSuccess("Shield",
                        result.ThreatsFound > 0
                            ? string.Format(LocalizationService.Instance.GetString("FullScanThreatsFoundShort"), result.ThreatsFound)
                            : LocalizationService.Instance.GetString("FullScanCleanShort"));
                }
                _logger.LogInfo("[ShieldVM] [RunFullScanAsync] FINALIZADO com sucesso");
            }
            catch (Exception ex)
            {
                _logger.LogError("[ShieldVM] [RunFullScanAsync] Erro no scan completo", ex);
                StatusMessage = LocalizationService.Instance.GetString("FullScanError");
                ScanProgressText = LocalizationService.Instance.GetString("ScanErrorGeneric");
                if (started) VoltrisOptimizer.Services.GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("FullScanError"));
                GlobalNotificationService.ShowError("Shield", LocalizationService.Instance.GetString("FullScanError"));
            }
            finally
            {
                IsScanning = false;
                ScanProgress = 0;
            }
        }
        
        private async Task RunAdwareScanAsync()
        {
            if (!EnsureShieldLicense("Scan de adware")) return;
            if (!EnsureProtectionEnabled("Scan de adware")) return;

            _logger.LogInfo("[ShieldVM] [RunAdwareScanAsync] INICIADO - Executando Adware Scan");
            bool started = false;
            try
            {
                IsScanning = true;
                IsScanResultVisible = false;
                ScanProgress = 0;
                ScanProgressText = LocalizationService.Instance.GetString("StartingAdwareScan");
                StatusMessage = LocalizationService.Instance.GetString("RunningAdwareScan");
                _logger.LogInfo("[ShieldVM] Iniciando Adware Scan...");

                started = VoltrisOptimizer.Services.GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("AdwareScan"), isPriority: true);

                var result = await _shieldService.RunAdwareScanAsync((progress, message) =>
                {
                    ScanProgress = progress;
                    ScanProgressText = message;
                    if (started) VoltrisOptimizer.Services.GlobalProgressService.Instance.UpdateProgress(progress, message);
                });

                ThreatsDetected = result.ThreatsFound;
                LastScanTime = result.CompletedAt.ToString("dd/MM/yyyy HH:mm");
                StatusMessage = result.ThreatsFound > 0
                    ? string.Format(LocalizationService.Instance.GetString("AdwareScanItemsDetected"), result.ThreatsFound)
                    : LocalizationService.Instance.GetString("AdwareScanClean");

                PopulateScanResults(result);

                try
                {
                    var shieldNotification = App.Services?.GetService(typeof(ShieldNotificationService)) as ShieldNotificationService;
                    shieldNotification?.NotifyScanComplete(result);
                }
                catch (Exception innerEx)
                {
                    _logger.LogWarning($"[ShieldVM] Falha ao notificar scan completo no AdwareScan: {innerEx.Message}");
                }

                _logger.LogDebug($"[ShieldVM] [RunAdwareScanAsync] Scan concluído: {result.ThreatsFound} ameaças, {result.ItemsScanned} itens");

                if (started)
                {
                    HistoryService.RecordActivity("Security Scan", result.ThreatsFound > 0 
                        ? string.Format(LocalizationService.Instance.GetString("AdwareFoundItems"), result.ThreatsFound) 
                        : LocalizationService.Instance.GetString("AdwareNoneDetected"));

                    VoltrisOptimizer.Services.GlobalProgressService.Instance.CompleteOperation(
                        result.ThreatsFound > 0
                            ? string.Format(LocalizationService.Instance.GetString("AdwareItemsDetectedShort"), result.ThreatsFound)
                            : LocalizationService.Instance.GetString("AdwareCleanShort"));
                    GlobalNotificationService.ShowSuccess("Shield",
                        result.ThreatsFound > 0
                            ? string.Format(LocalizationService.Instance.GetString("AdwareItemsDetectedShort"), result.ThreatsFound)
                            : LocalizationService.Instance.GetString("AdwareCleanShort"));
                }
                _logger.LogInfo("[ShieldVM] [RunAdwareScanAsync] FINALIZADO com sucesso");
            }
            catch (Exception ex)
            {
                _logger.LogError("[ShieldVM] [RunAdwareScanAsync] Erro no scan de adware", ex);
                StatusMessage = LocalizationService.Instance.GetString("AdwareScanError");
                ScanProgressText = LocalizationService.Instance.GetString("ScanErrorGeneric");
                if (started) VoltrisOptimizer.Services.GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("AdwareScanError"));
                GlobalNotificationService.ShowError("Shield", LocalizationService.Instance.GetString("AdwareScanError"));
            }
            finally
            {
                IsScanning = false;
                ScanProgress = 0;
            }
        }
        
        #endregion
        
        #region Scan Results & Threat Actions
        
        private void PopulateScanResults(ScanResult result)
        {
            _logger.LogInfo("[ShieldVM] [PopulateScanResults] INICIADO - Populando resultados de scan");
            _logger.LogDebug($"[ShieldVM] [PopulateScanResults] ScanType={result?.ScanType}, Threats={result?.Threats?.Count}, ItemsScanned={result?.ItemsScanned}");
            System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ScanThreatItems.Clear();
                
                ScanResultType = result.ScanType switch
                {
                    ScanType.Quick => LocalizationService.Instance.GetString("QuickScan"),
                    ScanType.Full => LocalizationService.Instance.GetString("FullScan"),
                    ScanType.Adware => LocalizationService.Instance.GetString("AdwareScan"),
                    _ => LocalizationService.Instance.GetString("Scan")
                };
                
                // CORREÇÃO: Usar Threats.Count como fonte de verdade (não ThreatsFound)
                if (result.Threats.Count > 0)
                {
                    ScanResultSummary = string.Format(LocalizationService.Instance.GetString("ThreatsFoundSummary"), result.Threats.Count, result.ItemsScanned);
                    
                    foreach (var threat in result.Threats)
                    {
                        ScanThreatItems.Add(new ScanThreatItemViewModel(threat));
                    }
                }
                else
                {
                    ScanResultSummary = string.Format(LocalizationService.Instance.GetString("NoThreatsFoundSummary"), result.ItemsScanned);
                }
                
                IsScanResultVisible = true;
                _logger.LogInfo($"[ShieldVM] Resultados populados: {result.Threats.Count} ameaças detalhadas");
            });
        }
        
        private async Task RemoveScanThreatAsync(ScanThreatItemViewModel? item)
        {
            if (!EnsureShieldLicense("Remover ameaça")) return;
            _logger.LogInfo("[ShieldVM] [RemoveScanThreatAsync] INICIADO - Removendo ameaça");
            if (item == null)
            {
                _logger.LogWarning("[ShieldVM] [RemoveScanThreatAsync] item é nulo");
                return;
            }

            _logger.LogDebug($"[ShieldVM] [RemoveScanThreatAsync] Item={item.Name}, Path={item.Path}, ThreatType={item.ThreatType}, Severity={item.Severity}");
            try
            {
                _logger.LogInfo($"[ShieldVM] Removendo ameaça: {item.Name} ({item.Path})");
                
                bool success = false;

                // 1. Se for um item de Adware/Registry, usa o AdwareScanner
                if (item.ThreatType == "RegistryKey" || item.ThreatType == "BrowserExtension" || item.ThreatType == "Adware")
                {
                    var adwareItem = new AdwareItem
                    {
                        Name = item.Name,
                        Path = item.Path,
                        Type = item.ThreatType == "RegistryKey" ? AdwareType.RegistryKey : AdwareType.Directory,
                        Severity = item.Severity == ThreatSeverity.High ? AdwareSeverity.High
                                 : item.Severity == ThreatSeverity.Medium ? AdwareSeverity.Medium
                                 : AdwareSeverity.Low
                    };
                    success = await _adwareScanner.RemoveAdwareItemAsync(adwareItem);
                }
                // 2. Se for um arquivo físico (ex: VOLKS.EXE / Metasploit)
                else if (File.Exists(item.Path))
                {
                    try
                    {
                        _logger.LogInfo($"[ShieldVM] Iniciando remoção forçada: {item.Path}");
                        
                        string error;
                        if (Win32FileHelper.ForceDeleteFile(item.Path, out error))
                        {
                            success = true;
                            if (string.IsNullOrEmpty(error))
                                _logger.LogSuccess($"[ShieldVM] Arquivo malicioso removido do disco: {item.Path}");
                            else
                                StatusMessage = error;
                        }
                        else
                        {
                            _logger.LogError($"[ShieldVM] Falha permanente na remoção: {item.Path}");
                            StatusMessage = string.Format(LocalizationService.Instance.GetString("ErrorWithDetails"), error);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"[ShieldVM] Erro inesperado ao deletar: {item.Path}", ex);
                        StatusMessage = LocalizationService.Instance.GetString("RemovalErrorRetry");
                    }
                }
                else
                {
                     _logger.LogWarning($"[ShieldVM] Alvo não encontrado para remoção: {item.Path}");
                     StatusMessage = LocalizationService.Instance.GetString("ThreatNotFoundOrRemoved");
                     success = false;
                }
                
                if (success)
                {
                    HistoryService.RecordActivity("Threat Removed", string.Format(LocalizationService.Instance.GetString("ThreatRemoved"), item.Name));
                    GlobalNotificationService.ShowSuccess("Shield", string.Format(LocalizationService.Instance.GetString("ThreatRemovedName"), item.Name));
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        ScanThreatItems.Remove(item);
                        if (ThreatsDetected > 0) ThreatsDetected--;
                        StatusMessage = string.Format(LocalizationService.Instance.GetString("ThreatRemovedName"), item.Name);
                        
                        if (ScanThreatItems.Count == 0)
                            ScanResultSummary = LocalizationService.Instance.GetString("AllThreatsHandled");
                    });
                }
                else
                {
                    StatusMessage = string.Format(LocalizationService.Instance.GetString("FailedToRemove"), item.Name);
                    GlobalNotificationService.ShowWarning("Shield", string.Format(LocalizationService.Instance.GetString("FailedToRemove"), item.Name));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ShieldVM] Erro ao tratar ação de remoção: {item.Name}", ex);
                StatusMessage = string.Format(LocalizationService.Instance.GetString("ErrorRemoving"), item.Name);
            }
        }
        
        private void IgnoreScanThreat(ScanThreatItemViewModel? item)
        {
            _logger.LogInfo("[ShieldVM] [IgnoreScanThreat] INICIADO - Ignorando ameaça");
            if (item == null)
            {
                _logger.LogWarning("[ShieldVM] [IgnoreScanThreat] item é nulo");
                return;
            }

            _logger.LogDebug($"[ShieldVM] [IgnoreScanThreat] Ameaça ignorada: {item.Name}, Path={item.Path}");
            System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ScanThreatItems.Remove(item);
                if (ThreatsDetected > 0) ThreatsDetected--;
                StatusMessage = string.Format(LocalizationService.Instance.GetString("ThreatIgnored"), item.Name);
                
                if (ScanThreatItems.Count == 0)
                    ScanResultSummary = LocalizationService.Instance.GetString("AllThreatsHandled");
            });
        }
        
        private async Task QuarantineScanThreatAsync(ScanThreatItemViewModel? item)
        {
            if (!EnsureShieldLicense("Quarentenar ameaça")) return;
            _logger.LogInfo("[ShieldVM] [QuarantineScanThreatAsync] INICIADO - Colocando ameaça em quarentena");
            if (item == null)
            {
                _logger.LogWarning("[ShieldVM] [QuarantineScanThreatAsync] item é nulo");
                return;
            }

            _logger.LogDebug($"[ShieldVM] [QuarantineScanThreatAsync] Item={item.Name}, Path={item.Path}, ThreatType={item.ThreatType}, Severity={item.Severity}");
            try
            {
                _logger.LogInfo($"[ShieldVM] Colocando em quarentena: {item.Name} ({item.Path})");
                
                // Se for um arquivo físico
                if (File.Exists(item.Path))
                {
                    // Tentar encerrar processos antes da quarentena
                    KillProcessesUsingFile(item.Path);

                    var success = await _shieldService.QuarantineFileAsync(item.Path, item.ThreatType, item.Severity);
                    if (success)
                    {
                        GlobalNotificationService.ShowSuccess("Shield", string.Format(LocalizationService.Instance.GetString("ThreatQuarantined"), item.Name));
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            ScanThreatItems.Remove(item);
                            if (ThreatsDetected > 0) ThreatsDetected--;
                            StatusMessage = string.Format(LocalizationService.Instance.GetString("ThreatQuarantined"), item.Name);
                            RefreshQuarantineList();
                            
                            if (ScanThreatItems.Count == 0)
                            ScanResultSummary = LocalizationService.Instance.GetString("AllThreatsHandled");
                        });
                        _logger.LogSuccess($"[ShieldVM] Arquivo enviado para quarentena: {item.Path}");
                    }
                    else
                    {
                        StatusMessage = string.Format(LocalizationService.Instance.GetString("QuarantineFailed"), item.Name);
                        GlobalNotificationService.ShowError("Shield", string.Format(LocalizationService.Instance.GetString("QuarantineFailed"), item.Name));
                    }
                }
                else
                {
                    StatusMessage = LocalizationService.Instance.GetString("FileNotFoundForQuarantine");
                    _logger.LogWarning($"[ShieldVM] Arquivo não encontrado para quarentena: {item.Path}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ShieldVM] Erro ao processar quarentena: {item.Name}", ex);
                StatusMessage = string.Format(LocalizationService.Instance.GetString("QuarantineError"), item.Name);
            }
        }
        
        #endregion
        
        #region Defender Integration
        
        private async Task StartDefenderScanAsync()
        {
            if (!EnsureShieldLicense("Scan do Windows Defender")) return;

            _logger.LogInfo("[ShieldVM] [StartDefenderScanAsync] INICIADO - Iniciando scan do Windows Defender");
            bool started = false;
            try
            {
                StatusMessage = LocalizationService.Instance.GetString("StartingDefenderScan");

                started = VoltrisOptimizer.Services.GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("DefenderScanOpName"), isPriority: true);
                if (started) VoltrisOptimizer.Services.GlobalProgressService.Instance.UpdateProgress(30, LocalizationService.Instance.GetString("StartingWindowsDefender"));

                var success = await _shieldService.StartDefenderScanAsync();

                if (success)
                {
                    _logger.LogInfo("[ShieldVM] [StartDefenderScanAsync] Scan do Defender iniciado com sucesso");
                    StatusMessage = LocalizationService.Instance.GetString("DefenderScanStarted");
                    if (started) VoltrisOptimizer.Services.GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("DefenderScanStartedShort"));
                    GlobalNotificationService.ShowSuccess("Shield", LocalizationService.Instance.GetString("DefenderScanStartedShort"));
                }
                else
                {
                    _logger.LogWarning("[ShieldVM] [StartDefenderScanAsync] Falha ao iniciar scan do Defender");
                    StatusMessage = LocalizationService.Instance.GetString("DefenderScanStartFailed");
                    if (started) VoltrisOptimizer.Services.GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("DefenderScanFailedShort"));
                    GlobalNotificationService.ShowWarning("Shield", LocalizationService.Instance.GetString("DefenderScanFailedShort"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("[ShieldVM] [StartDefenderScanAsync] Erro ao iniciar scan do Defender", ex);
                StatusMessage = LocalizationService.Instance.GetString("DefenderScanError");
                if (started) VoltrisOptimizer.Services.GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("DefenderScanErrorShort"));
                GlobalNotificationService.ShowError("Shield", LocalizationService.Instance.GetString("DefenderScanError"));
            }
        }
        
        private async Task RefreshDefenderStatusAsync()
        {
            _logger.LogInfo("[ShieldVM] [RefreshDefenderStatusAsync] INICIADO - Atualizando status do Windows Defender");
            try
            {
                if (!EnsureShieldLicense("Atualizar status do Defender")) return;

                _defenderStatus = await _shieldService.GetDefenderStatusAsync();
                _logger.LogDebug($"[ShieldVM] IsEnabled={_defenderStatus?.IsEnabled}, IsUpToDate={_defenderStatus?.IsUpToDate}");
                OnPropertyChanged(nameof(IsDefenderEnabled));
                OnPropertyChanged(nameof(IsDefenderUpToDate));

                // Feedback explícito: sem isto o clique parece não fazer nada.
                if (_defenderStatus?.IsEnabled == true)
                {
                    var statusText = _defenderStatus.IsUpToDate
                        ? LocalizationService.Instance.GetString("ShieldDefenderUpToDate")
                        : LocalizationService.Instance.GetString("ShieldDefenderOutdated");
                    GlobalNotificationService.ShowInfo("Shield",
                        $"{LocalizationService.Instance.GetString("Loc_WindowsDefenderStatus")}: {statusText}");
                }
                else
                {
                    GlobalNotificationService.ShowWarning("Shield",
                        LocalizationService.Instance.GetString("ShieldDefenderUnavailable"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("[ShieldVM] Erro ao atualizar status do Defender", ex);
                GlobalNotificationService.ShowError("Shield",
                    $"{LocalizationService.Instance.GetString("Loc_WindowsDefenderStatus")}: {ex.Message}");
            }
        }
        
        #endregion
        
        #region Event Handlers
        
        private void OnShieldStatusChanged(object? sender, ShieldStatusChangedEventArgs e)
        {
            if (e == null) return;

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            void ApplyState()
            {
                IsProtectionActive = e.IsActive;
                StatusMessage = e.Message;
                SyncProtectionModuleState();

                // Reaplica o Modo Gamer (ativado por padrão) após a proteção subir.
                if (e.IsActive && IsShieldLicensed && IsGamerModeEnabled)
                {
                    _shieldService.SetGamerMode(true);
                }
            }

            if (dispatcher == null || dispatcher.CheckAccess())
            {
                ApplyState();
            }
            else
            {
                dispatcher.InvokeAsync(ApplyState);
            }
        }
        
        /// <summary>
        /// Re-deriva lista E contador de ameaças a partir da fonte autoritativa.
        /// <para>
        /// Antes, <c>ActiveThreatsCount</c> só era atualizado em
        /// <c>RefreshActiveThreats()</c> e <c>QuarantineCount</c> só em
        /// <c>RefreshQuarantineList()</c> — ambos acionados APENAS pelo botão
        /// "Atualizar". Quando uma ameaça era resolvida sozinha (processo
        /// encerrado, detecção expirada) ou um arquivo ia para quarentena
        /// automaticamente, o número da Visão Geral ficava congelado até o
        /// usuário apertar o botão. É o sintoma de "contador travado".
        /// </para>
        /// <para>
        /// Reaproveitamos os dois métodos que já constroem lista e contador a
        /// partir da mesma fonte, em vez de recontar por conta própria — assim
        /// número e lista não podem divergir. O argumento <c>false</c> dispensa
        /// o prompt de licença, que só se aplica ao clique do botão.
        /// </para>
        /// </summary>
        /// <summary>
        /// [FIX:SHIELD-CONTAGEM-ZERADA] SINCRONIZA COM O SERVIÇO QUANDO A ABA ABRE.
        ///
        /// O `ShieldViewModel` é um singleton: ele é construído uma vez, quando o
        /// contêiner o resolve, muito antes de o usuário abrir a aba. Por isso a
        /// recuperação do histórico não pode acontecer no construtor — as
        /// ameaças detectadas nesse intervalo não existiam ainda.
        ///
        /// Este método é chamado pelo `Loaded` da `ShieldView`, ou seja, no
        /// instante em que o usuário realmente olha para a tela. Ele é
        /// idempotente: rodar várias vezes não duplica nada, porque a regra de
        /// duplicata é a mesma do evento ao vivo (mesmo caminho + mesmo tipo de
        /// ameaça = mesma linha).
        ///
        /// Além de trazer as ameaças, ele recontorna os três contadores da visão
        /// geral — ameaças, quarentena e alertas — a partir do estado real dos
        /// serviços. Os três ficavam em zero pelos mesmos motivos: são
        /// atualizados por eventos que o ViewModel só veria se já existisse, e
        /// nenhum deles é reconstruído na exibição.
        /// </summary>
        public void SyncFromServiceOnDisplay()
        {
            try
            {
                ReconcileFromService();
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ShieldVM] Erro ao sincronizar com o serviço: {ex.Message}");
            }
        }

        /// <summary>
        /// Reconstrói a contagem de ameaças a partir do histórico do serviço.
        /// </summary>
        private void ReconcileFromService()
        {
            // [FIX:SHIELD-CONTAGEM-ZERADA] ALERTAS DE RANSOMWARE VÊM DO SERVIÇO.
            //
            // A lista local do ViewModel nunca era alimentada — o serviço
            // detected, emitia o evento, e o evento era consumido só para
            // notificação. O contador, sem fonte, ficava em zero. Aqui ele lê o
            // histórico que o próprio monitor acumula, que existe pela mesma
            // razão do histórico de ameaças.
            try
            {
                var alertas = _ransomwareMonitor.GetAlertHistory();
                if (alertas != null && alertas.Count > 0)
                {
                    int total = alertas.Count;
                    if (RansomwareAlertsCount != total)
                    {
                        RansomwareAlertsCount = total;
                        _logger.LogInfo(
                            $"[ShieldVM] Sincronização: {total} alerta(s) de ransomware registrado(s) na sessão.");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ShieldVM] Erro ao ler histórico de ransomware: {ex.Message}");
            }

            var historico = _shieldService.GetThreatHistory();
            if (historico.Count == 0)
            {
                _logger.LogDebug("[ShieldVM] Sincronização: serviço ainda não registrou ameaças.");
                return;
            }

            int adicionadas = 0;

            foreach (var item in historico)
            {
                string caminho = item.FilePath;
                string nome = System.IO.Path.GetFileName(caminho);

                if (ScanThreatItems.Any(x =>
                        string.Equals(x.Path, caminho, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(x.ThreatType, item.ThreatType, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                // A coleção é observável e precisa ser alterada na thread da UI.
                var vm = System.Windows.Application.Current.Dispatcher;
                if (vm.CheckAccess())
                {
                    AddThreatItem(nome, caminho, item.ThreatType, item.Severity);
                }
                else
                {
                    vm.Invoke(() => AddThreatItem(nome, caminho, item.ThreatType, item.Severity));
                }

                adicionadas++;
            }

            if (adicionadas > 0)
            {
                _logger.LogInfo(
                    $"[ShieldVM] Sincronização: {adicionadas} ameaça(s) restaurada(s) do histórico " +
                    $"(total na lista = {ScanThreatItems.Count}).");
            }

            RefreshThreatCountersFromList();
        }

        /// <summary>Adiciona uma linha à lista de ameaças da visão geral.</summary>
        private void AddThreatItem(string nome, string caminho, string tipo, VoltrisOptimizer.Services.Shield.ThreatSeverity severidade)
        {
            ScanThreatItems.Add(new ScanThreatItemViewModel(new ScanThreatItem
            {
                Name = nome,
                Path = caminho,
                ThreatType = tipo,
                Severity = severidade
            }));
        }

        /// <summary>
        /// [FIX:SHIELD-CONTAGEM-ZERADA] O CONTADOR VEM DA LISTA.
        ///
        /// A contagem por incremento (`ThreatsDetected++`) pode divergir da lista
        /// sempre que um item for adicionado por um caminho que não passa pelo
        /// evento, ou removido por uma das rotas de decremento. O sintoma era
        /// exatamente esse: uma tela mostrando 0 com itens visíveis logo abaixo.
        ///
        /// Derivar o número da lista elimina a classe inteira do defeito — não
        /// existe mais estado para divergir.
        /// </summary>
        private void RefreshThreatCountersFromList()
        {
            if (ScanThreatItems.Count == 0)
            {
                return;
            }

            int total = ScanThreatItems.Count;

            if (ThreatsDetected != total)
            {
                ThreatsDetected = total;
            }

            IsScanResultVisible = true;
            ScanResultType = LocalizationService.Instance.GetString("RealTimeProtection");
            ScanResultSummary = string.Format(
                LocalizationService.Instance.GetString("ThreatsDetectedCount"),
                total);

            OnPropertyChanged(nameof(ThreatsDetectedDisplay));
        }

        private void ReconcileThreatCounters()
        {
            try
            {
                // [FIX:SHIELD-CONTAGEM-ZERADA] A VISÃO GERAL PRECISA DOS TRÊS NÚMEROS.
                //
                // A reconciliação já existia e já rederivava dois deles — as
                // ameaças ativas e a quarentena — mas era chamada por caminhos
                // que dependiam de a aba já existir: o botão "Atualizar" e o
                // evento de ameaça. O usuário não aperta nada ao abrir a tela,
                // então os números ficavam no valor inicial, que é zero.
                //
                // `RansomwareAlertsCount` nunca entrava em nenhum desses
                // caminhos, e por isso ficava em zero mesmo com alertas
                // registrados no serviço.
                //
                // A correção é fazer a reconciliação ser parte da SINCRONIZAÇÃO
                // DE EXIBIÇÃO, junto com a recuperação do histórico de ameaças.
                // Os três números passam a vir do estado real dos serviços no
                // instante em que o usuário olha para a tela.
                RefreshActiveThreats();
                RefreshQuarantineList(promptIfBlocked: false);
                RefreshRansomwareAlerts();
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ShieldVM] Erro ao reconciliar contadores de ameaça: {ex.Message}");
            }
        }
        /// <summary>
        /// [FIX:SHIELD-CONTAGEM-ZERADA] ATUALIZA O CONTADOR DE ALERTAS DE RANSOMWARE.
        ///
        /// Este número não tinha NENHUMA rota de atualização. A propriedade
        /// existe, é exibida na Visão Geral, e nada nunca a escrevia — o que é
        /// pior do que um bug de contagem: é um indicador permanentemente morto,
        /// que reporta "zero alertas" com a mesma confiança com que reportaria
        /// um ataque real.
        ///
        /// A origem é o histórico que o próprio monitor acumula: o mesmo motivo
        /// que levou a criar o histórico de ameaças. O número é derivado da
        /// lista, e não pode divergir dela.
        /// </summary>
        private void RefreshRansomwareAlerts()
        {
            RansomwareAlertsCount = RansomwareAlertCount;
        }

        private void OnThreatDetected(object? sender, ThreatDetectedEventArgs e)
        {
            _logger.LogInfo("[ShieldVM] [OnThreatDetected] INICIADO - Ameaça detectada em tempo real");
            if (e == null)
            {
                _logger.LogWarning("[ShieldVM] [OnThreatDetected] Argumento e é nulo");
                return;
            }

            _logger.LogDebug($"[ShieldVM] [OnThreatDetected] FilePath={e.FilePath}, ThreatType={e.ThreatType}, Severity={e.Severity}");

            // Dedup na propria lista. A cadeia a montante ja limita o volume de
            // toasts, mas a lista da aba Shield e acumulativa por sessao: sem
            // esta checagem, o mesmo arquivo reativado varias vezes (o que
            // acontece com instaladores que reexecutam) enche a tela de linhas
            // identicas. E a exibicao que o usuario le como "o programa viu
            // atividade suspeita sem parar".
            var fileName = System.IO.Path.GetFileName(e.FilePath);
            var alreadyListed = ScanThreatItems.Any(x =>
                string.Equals(x.Path, e.FilePath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.ThreatType, e.ThreatType, StringComparison.OrdinalIgnoreCase));

            if (alreadyListed)
            {
                _logger.LogDebug($"[ShieldVM] Item ja listado, ignorando duplicata: {e.FilePath}");
                return;
            }

            // CORREÇÃO THREAD-SAFE: Toda atualização de UI no dispatcher
            System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                // Re-checagem dentro do dispatcher: dois eventos podem passar
                // pelo teste acima antes de a UI enfileirar o primeiro.
                if (ScanThreatItems.Any(x =>
                        string.Equals(x.Path, e.FilePath, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(x.ThreatType, e.ThreatType, StringComparison.OrdinalIgnoreCase)))
                {
                    return;
                }

                ThreatsDetected++;
                StatusMessage = string.Format(LocalizationService.Instance.GetString("ThreatDetectedTitle"), e.ThreatType);
                ScanThreatItems.Add(new ScanThreatItemViewModel(new ScanThreatItem
                {
                    Name = fileName,
                    Path = e.FilePath,
                    ThreatType = e.ThreatType,
                    Severity = e.Severity
                }));
                IsScanResultVisible = true;
                ScanResultType = LocalizationService.Instance.GetString("RealTimeProtection");
                ScanResultSummary = string.Format(LocalizationService.Instance.GetString("ThreatsDetectedCount"), ScanThreatItems.Count);

                // Mantém a contagem de ameaças ativas e a de quarentena em dia sem
                // depender do botão "Atualizar".
                ReconcileThreatCounters();
            });
        }
        
        #endregion
        
        #region Network Guardian Methods
        
        private async Task ToggleNetworkMonitoringAsync()
        {
            if (!EnsureShieldLicense("Monitoramento de rede")) return;

            _logger.LogInfo("[ShieldVM] [ToggleNetworkMonitoringAsync] INICIADO - Alternando monitoramento de rede");
            try
            {
                if (IsNetworkMonitoringActive)
                {
                    _logger.LogInfo("[ShieldVM] [ToggleNetworkMonitoringAsync] Desativando monitoramento de rede");
                    await _networkMonitor.StopMonitoringAsync();
                    _logger.LogInfo("[ShieldVM] [ToggleNetworkMonitoringAsync] Monitoramento de rede desativado");
                    GlobalNotificationService.ShowWarning("Shield", LocalizationService.Instance.GetString("NetworkMonitoringDeactivated"));
                }
                else
                {
                    _logger.LogInfo("[ShieldVM] [ToggleNetworkMonitoringAsync] Ativando monitoramento de rede");
                    var success = await _networkMonitor.StartMonitoringAsync();
                    _logger.LogDebug($"[ShieldVM] [ToggleNetworkMonitoringAsync] StartMonitoringAsync retornou {success}");
                    if (success)
                    {
                        UpdateNetworkDevicesList();
                        _logger.LogInfo("[ShieldVM] [ToggleNetworkMonitoringAsync] Monitoramento de rede ativado com sucesso");
                        GlobalNotificationService.ShowSuccess("Shield", LocalizationService.Instance.GetString("NetworkMonitoringActivated"));
                    }
                    else
                    {
                        _logger.LogWarning("[ShieldVM] [ToggleNetworkMonitoringAsync] Falha ao ativar monitoramento de rede");
                        GlobalNotificationService.ShowError("Shield", LocalizationService.Instance.GetString("NetworkMonitoringActivationFailed"));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("[ShieldVM] [ToggleNetworkMonitoringAsync] Erro ao alternar monitoramento de rede", ex);
            }
        }
        
        public async Task RunNetworkScanAsync()
        {
            if (!EnsureShieldLicense("Scan de rede")) return;

            _logger.LogInfo("[ShieldVM] [RunNetworkScanAsync] INICIADO - Executando scan de rede");
            bool started = false;
            try
            {
                IsNetworkScanning = true;

                started = VoltrisOptimizer.Services.GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("NetworkScan"), isPriority: true);
                if (started) VoltrisOptimizer.Services.GlobalProgressService.Instance.UpdateProgress(20, LocalizationService.Instance.GetString("ScanningNetworkDevices"));

                var success = await _networkMonitor.RunManualScanAsync();
                _logger.LogDebug($"[ShieldVM] [RunNetworkScanAsync] RunManualScanAsync retornou {success}");

                if (success)
                {
                    _logger.LogInfo("[ShieldVM] [RunNetworkScanAsync] Scan de rede concluído com sucesso");
                    UpdateNetworkDevicesList();
                    if (started)
                    {
                        VoltrisOptimizer.Services.GlobalProgressService.Instance.UpdateProgress(100, string.Format(LocalizationService.Instance.GetString("NetworkScanCompleteDevices"), OnlineDevicesCount));
                        VoltrisOptimizer.Services.GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("NetworkOnlineDevices"), OnlineDevicesCount));
                    }
                    GlobalNotificationService.ShowSuccess("Shield", string.Format(LocalizationService.Instance.GetString("NetworkScanCompleteDevices"), OnlineDevicesCount));
                }
                else
                {
                    _logger.LogWarning("[ShieldVM] [RunNetworkScanAsync] Scan de rede falhou");
                    if (started) VoltrisOptimizer.Services.GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("NetworkScanError"));
                    GlobalNotificationService.ShowError("Shield", LocalizationService.Instance.GetString("NetworkScanError"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("[ShieldVM] [RunNetworkScanAsync] Erro no scan de rede", ex);
                if (started) VoltrisOptimizer.Services.GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("NetworkScanError"));
                GlobalNotificationService.ShowError("Shield", LocalizationService.Instance.GetString("NetworkScanError"));
            }
            finally
            {
                IsNetworkScanning = false;
            }
        }
        
        private void UpdateNetworkDevicesList()
        {
            _logger.LogInfo("[ShieldVM] [UpdateNetworkDevicesList] INICIADO - Atualizando lista de dispositivos de rede");
            try
            {
                _logger.LogInfo("[ShieldVM] Atualizando lista de dispositivos de rede...");
                
                _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        // VERIFICAÇÃO DE THREAD-SAFE: Verificar se Dispatcher ainda está ativo
                        if (System.Windows.Application.Current == null || System.Windows.Application.Current.Dispatcher == null)
                        {
                            _logger.LogWarning("[ShieldVM] Dispatcher não está disponível - ignorando atualização");
                            return;
                        }

                        NetworkDevices.Clear();
                        
                        // VERIFICAÇÃO DE NULL: Garantir que KnownDevices não seja nulo
                        var knownDevices = _deviceTracker.KnownDevices;
                        if (knownDevices == null)
                        {
                            _logger.LogWarning("[ShieldVM] KnownDevices é nulo - ignorando atualização");
                            return;
                        }

                        var devicesList = knownDevices.OrderByDescending(d => d.IsOnline).ThenBy(d => d.FriendlyName).ToList();
                        
                        foreach (var device in devicesList)
                        {
                            // VERIFICAÇÃO DE NULL: Garantir que device não seja nulo
                            if (device != null)
                            {
                                NetworkDevices.Add(new NetworkDeviceViewModel(device));
                            }
                        }
                        
                        OnlineDevicesCount = _deviceTracker.GetOnlineDeviceCount();
                        TotalDevicesCount = _deviceTracker.GetTotalDeviceCount();
                        
                        _logger.LogSuccess($"[ShieldVM] Lista atualizada: {OnlineDevicesCount}/{TotalDevicesCount} dispositivos online");
                    }
                    catch (InvalidOperationException ex)
                    {
                        _logger.LogError("[ShieldVM] Erro de thread-safe ao atualizar UI", ex);
                    }
                    catch (ArgumentException ex)
                    {
                        _logger.LogError("[ShieldVM] Erro de argumento ao atualizar dispositivos", ex);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("[ShieldVM] Erro genérico ao atualizar lista de dispositivos", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError("[ShieldVM] Erro ao atualizar lista de dispositivos", ex);
            }
        }
        
        private void OnNetworkMonitoringStatusChanged(object? sender, MonitoringStatusChangedEventArgs e)
        {
            if (e == null) return;

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                IsNetworkMonitoringActive = e.IsActive;
            }
            else
            {
                dispatcher.InvokeAsync(() => IsNetworkMonitoringActive = e.IsActive);
            }
        }
        
        private void OnNewDeviceDetected(object? sender, DeviceEventArgs e)
        {
            _logger.LogInfo("[ShieldVM] [OnNewDeviceDetected] INICIADO - Novo dispositivo detectado");
            if (e?.Device == null)
            {
                _logger.LogWarning("[ShieldVM] [OnNewDeviceDetected] e ou Device é nulo");
                return;
            }

               _logger.LogDebug($"[ShieldVM] [OnNewDeviceDetected] Hostname={e.Device.Hostname}, IP={e.Device.IPAddress}, MAC={e.Device.MacAddress}");
               _logger.LogInfo($"[ShieldVM] Novo dispositivo detectado: {e.Device.Hostname}");
               UpdateNetworkDevicesList();
               // A notificação visual (com o ícone do dispositivo) é enviada por
               // ShieldNotificationService, que já assina este evento desde a inicialização.
               // Notificar aqui duplicaria o toast ao abrir a aba Rede.
               _logger.LogInfo("[ShieldVM] [OnNewDeviceDetected] FINALIZADO com sucesso");
           }
        
        private void OnDeviceConnected(object? sender, DeviceEventArgs e)
        {
            _logger.LogInfo("[ShieldVM] [OnDeviceConnected] INICIADO - Dispositivo conectado à rede");
            if (e?.Device == null)
            {
                _logger.LogWarning("[ShieldVM] [OnDeviceConnected] e ou Device é nulo");
                return;
            }

            _logger.LogInfo("[ShieldVM] NOVO DISPOSITIVO CONECTADO A REDE!");
            _logger.LogInfo($"[ShieldVM] IP: {e.Device.IPAddress}");
            _logger.LogInfo($"[ShieldVM] MAC: {e.Device.MacAddress}");
            _logger.LogInfo($"[ShieldVM] Hostname: {e.Device.Hostname}");
            _logger.LogInfo($"[ShieldVM] Vendor: {e.Device.Vendor}");
            _logger.LogInfo($"[ShieldVM] DeviceType: {e.Device.DeviceType}");
            _logger.LogInfo($"[ShieldVM] OS: {e.Device.OperatingSystem}");
            _logger.LogInfo($"[ShieldVM] FriendlyName: {e.Device.FriendlyName}");
            _logger.LogInfo($"[ShieldVM] Confidence: {e.Device.ConfidenceLevel}%");
            
            _logger.LogInfo("[ShieldVM] Enviando notificacao de novo dispositivo...");
            
            UpdateNetworkDevicesList();
        }
        
        private void OnDeviceDisconnected(object? sender, DeviceEventArgs e)
        {
            _logger.LogInfo("[ShieldVM] [OnDeviceDisconnected] INICIADO - Dispositivo desconectado");
            if (e?.Device == null)
            {
                _logger.LogWarning("[ShieldVM] [OnDeviceDisconnected] e ou Device é nulo");
                return;
            }

            _logger.LogInfo("[ShieldVM] DISPOSITIVO DESCONECTADO DA REDE!");
            _logger.LogInfo($"[ShieldVM] IP: {e.Device.IPAddress}");
            _logger.LogInfo($"[ShieldVM] MAC: {e.Device.MacAddress}");
            _logger.LogInfo($"[ShieldVM] Hostname: {e.Device.Hostname}");
            _logger.LogInfo($"[ShieldVM] Vendor: {e.Device.Vendor}");
            _logger.LogInfo($"[ShieldVM] DeviceType: {e.Device.DeviceType}");
            _logger.LogInfo($"[ShieldVM] OS: {e.Device.OperatingSystem}");
            _logger.LogInfo($"[ShieldVM] FriendlyName: {e.Device.FriendlyName}");
            _logger.LogInfo($"[ShieldVM] Confidence: {e.Device.ConfidenceLevel}%");
            
            _logger.LogInfo("[ShieldVM] Enviando notificacao de dispositivo desconectado...");
            
            UpdateNetworkDevicesList();
        }
        
        #endregion
        
        #region Startup Monitor Methods
        
        private async Task ScanStartupItemsAsync()
        {
            if (!EnsureShieldLicense("Scan de inicialização")) return;
            _logger.LogInfo("[ShieldVM] [ScanStartupItemsAsync] INICIADO - Escanando itens de inicialização");
            bool started = false;
            try
            {
                IsStartupScanning = true;
                StatusMessage = LocalizationService.Instance.GetString("ScanningStartupItems");

                started = VoltrisOptimizer.Services.GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("StartupScan"), isPriority: true);
                if (started) VoltrisOptimizer.Services.GlobalProgressService.Instance.UpdateProgress(30, LocalizationService.Instance.GetString("AnalyzingStartupPrograms"));

                var items = await _startupMonitor.GetAllStartupItemsAsync();
                var suspiciousItems = items.Where(i => i.IsSuspicious).ToList();
                _logger.LogInfo($"[ShieldVM] [ScanStartupItemsAsync] {items.Count} itens de inicialização encontrados, {suspiciousItems.Count} suspeito(s)");
                foreach (var suspicious in suspiciousItems)
                {
                    _logger.LogWarning($"[ShieldVM] [ScanStartupItemsAsync] Suspeito: {suspicious.Name} -> {suspicious.Path} ({suspicious.SuspiciousReason})");
                }

                if (started) VoltrisOptimizer.Services.GlobalProgressService.Instance.UpdateProgress(80, LocalizationService.Instance.GetString("ProcessingResults"));

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    StartupItems.Clear();
                    foreach (var item in items)
                    {
                        StartupItems.Add(new StartupItemViewModel(item));
                    }
                    StartupItemsCount = items.Count;
                });

                StatusMessage = string.Format(LocalizationService.Instance.GetString("StartupScanComplete"), items.Count);
                if (started) VoltrisOptimizer.Services.GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("StartupItemsFound"), items.Count));
                GlobalNotificationService.ShowInfo("Shield", string.Format(LocalizationService.Instance.GetString("StartupItemsFound"), items.Count));
                _logger.LogInfo("[ShieldVM] [ScanStartupItemsAsync] FINALIZADO com sucesso");
            }
            catch (Exception ex)
            {
                _logger.LogError("[ShieldVM] [ScanStartupItemsAsync] Erro no scan de inicialização", ex);
                StatusMessage = LocalizationService.Instance.GetString("StartupScanError");
                if (started) VoltrisOptimizer.Services.GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("StartupScanError"));
            }
            finally
            {
                IsStartupScanning = false;
            }
        }
        
        private async Task DisableStartupItemAsync(StartupItemViewModel? item)
        {
            if (!EnsureShieldLicense("Desativar item de inicialização")) return;
            _logger.LogInfo("[ShieldVM] [DisableStartupItemAsync] INICIADO - Desativando item de inicialização");
            if (item == null)
            {
                _logger.LogWarning("[ShieldVM] [DisableStartupItemAsync] item é nulo");
                return;
            }

            _logger.LogDebug($"[ShieldVM] [DisableStartupItemAsync] Item={item.Name}");
            try
            {
                var success = await _startupMonitor.DisableStartupItemAsync(item.GetStartupItem());
                _logger.LogDebug($"[ShieldVM] [DisableStartupItemAsync] DisableStartupItemAsync retornou {success}");
                if (success)
                {
                    item.IsEnabled = false;
                    StatusMessage = string.Format(LocalizationService.Instance.GetString("StartupItemDisabled"), item.Name);
                    GlobalNotificationService.ShowSuccess("Shield", string.Format(LocalizationService.Instance.GetString("StartupItemDisabled"), item.Name));
                    _logger.LogInfo("[ShieldVM] [DisableStartupItemAsync] Item desativado com sucesso");
                }
                else
                {
                    _logger.LogWarning($"[ShieldVM] [DisableStartupItemAsync] Falha ao desativar item: {item.Name}");
                    GlobalNotificationService.ShowWarning("Shield", string.Format(LocalizationService.Instance.GetString("StartupItemDisableFailed"), item.Name));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("[ShieldVM] [DisableStartupItemAsync] Erro ao desativar item", ex);
            }
        }
        
        private async Task RemoveStartupItemAsync(StartupItemViewModel? item)
        {
            if (!EnsureShieldLicense("Remover item de inicialização")) return;
            _logger.LogInfo("[ShieldVM] [RemoveStartupItemAsync] INICIADO - Removendo item de inicialização");
            if (item == null)
            {
                _logger.LogWarning("[ShieldVM] [RemoveStartupItemAsync] item é nulo");
                return;
            }

            _logger.LogDebug($"[ShieldVM] [RemoveStartupItemAsync] Item={item.Name}");
            try
            {
                var success = await _startupMonitor.RemoveStartupItemAsync(item.GetStartupItem());
                _logger.LogDebug($"[ShieldVM] [RemoveStartupItemAsync] RemoveStartupItemAsync retornou {success}");
                if (success)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        StartupItems.Remove(item);
                        StartupItemsCount = StartupItems.Count;
                    });
                    StatusMessage = string.Format(LocalizationService.Instance.GetString("StartupItemRemoved"), item.Name);
                    GlobalNotificationService.ShowSuccess("Shield", string.Format(LocalizationService.Instance.GetString("StartupItemRemoved"), item.Name));
                    _logger.LogInfo("[ShieldVM] [RemoveStartupItemAsync] Item removido com sucesso");
                }
                else
                {
                    _logger.LogWarning($"[ShieldVM] [RemoveStartupItemAsync] Falha ao remover item: {item.Name}");
                    GlobalNotificationService.ShowWarning("Shield", string.Format(LocalizationService.Instance.GetString("StartupItemRemoveFailed"), item.Name));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("[ShieldVM] [RemoveStartupItemAsync] Erro ao remover item", ex);
            }
        }
        
        #endregion
        
        #region Adware Scanner Methods
        
        private async Task ScanAdwareAsync()
        {
            if (!EnsureShieldLicense("Scanner de adware")) return;
            _logger.LogInfo("[ShieldVM] [ScanAdwareAsync] INICIADO - Escanando adware");
            bool started = false;
            try
            {
                IsAdwareScanning = true;
                StatusMessage = LocalizationService.Instance.GetString("ScanningAdware");

                started = VoltrisOptimizer.Services.GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("AdwareScan"), isPriority: true);
                if (started) VoltrisOptimizer.Services.GlobalProgressService.Instance.UpdateProgress(10, LocalizationService.Instance.GetString("StartingAdwareScan"));

                var items = await _adwareScanner.ScanForAdwareAsync();
                _logger.LogDebug($"[ShieldVM] [ScanAdwareAsync] {items.Count} itens de adware encontrados");

                if (started) VoltrisOptimizer.Services.GlobalProgressService.Instance.UpdateProgress(100, string.Format(LocalizationService.Instance.GetString("AdwareScanCompleteItems"), items.Count));

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    AdwareItems.Clear();
                    foreach (var item in items)
                    {
                        AdwareItems.Add(new AdwareItemViewModel(item));
                    }
                    AdwareItemsCount = items.Count;
                });

                StatusMessage = string.Format(LocalizationService.Instance.GetString("AdwareScanComplete"), items.Count);
                if (started) VoltrisOptimizer.Services.GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("AdwareItemsDetectedShort"), items.Count));
                _logger.LogInfo("[ShieldVM] [ScanAdwareAsync] FINALIZADO com sucesso");
            }
            catch (Exception ex)
            {
                _logger.LogError("[ShieldVM] [ScanAdwareAsync] Erro no scan de adware", ex);
                StatusMessage = LocalizationService.Instance.GetString("AdwareScanError");
                if (started) VoltrisOptimizer.Services.GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("AdwareScanError"));
            }
            finally
            {
                IsAdwareScanning = false;
            }
        }
        
        private async Task RemoveAdwareItemAsync(AdwareItemViewModel? item)
        {
            if (!EnsureShieldLicense("Remover item de adware")) return;
            _logger.LogInfo("[ShieldVM] [RemoveAdwareItemAsync] INICIADO - Removendo item de adware");
            if (item == null)
            {
                _logger.LogWarning("[ShieldVM] [RemoveAdwareItemAsync] item é nulo");
                return;
            }

            _logger.LogDebug($"[ShieldVM] [RemoveAdwareItemAsync] Item={item.Name}");
            try
            {
                var success = await _adwareScanner.RemoveAdwareItemAsync(item.GetAdwareItem());
                _logger.LogDebug($"[ShieldVM] [RemoveAdwareItemAsync] RemoveAdwareItemAsync retornou {success}");
                if (success)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        AdwareItems.Remove(item);
                        AdwareItemsCount = AdwareItems.Count;
                    });
                    StatusMessage = string.Format(LocalizationService.Instance.GetString("AdwareRemoved"), item.Name);
                    _logger.LogInfo("[ShieldVM] [RemoveAdwareItemAsync] Item removido com sucesso");
                }
                else
                {
                    _logger.LogWarning($"[ShieldVM] [RemoveAdwareItemAsync] Falha ao remover item: {item.Name}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("[ShieldVM] [RemoveAdwareItemAsync] Erro ao remover adware", ex);
            }
        }
        
        #endregion
        
        #region Ransomware Monitor Methods
        
        private async Task ToggleRansomwareMonitoringAsync()
        {
            if (!EnsureShieldLicense("Monitoramento de ransomware")) return;
            _logger.LogInfo("[ShieldVM] [ToggleRansomwareMonitoringAsync] INICIADO - Alternando monitoramento de ransomware");
            // REPARO: Verificar retorno de StartOperation — se bloqueado (false), NÃO chamar
            // CompleteOperation. Um CompleteOperation órfão faria Pop da operação errada na pilha
            // (ex: 'Reparo Completo Ultra Avançado'). Causa raiz do bug confirmado no log 16:41:41.
            bool started = false;
            try
            {
                if (IsRansomwareMonitoringActive)
                {
                    _logger.LogInfo("[ShieldVM] [ToggleRansomwareMonitoringAsync] Desativando monitoramento de ransomware");
                    started = VoltrisOptimizer.Services.GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("RansomwareMonitorOpName"), isPriority: true);
                    if (started) VoltrisOptimizer.Services.GlobalProgressService.Instance.UpdateProgress(50, LocalizationService.Instance.GetString("DisablingMonitoring"));

                    if (!await _shieldService.SetRansomwareMonitoringAsync(false))
                        throw new InvalidOperationException("Não foi possível desativar o monitoramento de ransomware.");
                    IsRansomwareMonitoringActive = false;
                    RansomwareStatus = LocalizationService.Instance.GetString("Inactive");

                    if (started) VoltrisOptimizer.Services.GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("RansomwareMonitorDeactivated"));
                    GlobalNotificationService.ShowWarning("Shield", LocalizationService.Instance.GetString("RansomwareMonitorDeactivated"));
                }
                else
                {
                    _logger.LogInfo("[ShieldVM] [ToggleRansomwareMonitoringAsync] Ativando monitoramento de ransomware");
                    started = VoltrisOptimizer.Services.GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("RansomwareMonitorOpName"), isPriority: true);
                    if (started) VoltrisOptimizer.Services.GlobalProgressService.Instance.UpdateProgress(50, LocalizationService.Instance.GetString("ActivatingMonitoring"));

                    if (!await _shieldService.SetRansomwareMonitoringAsync(true))
                        throw new InvalidOperationException("Não foi possível ativar o monitoramento de ransomware.");
                    IsRansomwareMonitoringActive = true;
                    RansomwareStatus = LocalizationService.Instance.GetString("MonitoringCriticalFolders");

                    if (started) VoltrisOptimizer.Services.GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("RansomwareMonitorActivated"));
                    GlobalNotificationService.ShowSuccess("Shield", LocalizationService.Instance.GetString("RansomwareMonitorActivated"));
                    _logger.LogInfo("[ShieldVM] [ToggleRansomwareMonitoringAsync] Monitoramento de ransomware ativado");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("[ShieldVM] [ToggleRansomwareMonitoringAsync] Erro ao alternar monitoramento de ransomware", ex);
                if (started) VoltrisOptimizer.Services.GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("RansomwareMonitorToggleError"));
            }
        }
        
        private void OnRansomwareAlertDetected(object? sender, RansomwareAlertEventArgs e)
        {
            _logger.LogInfo("[ShieldVM] [OnRansomwareAlertDetected] INICIADO - Alerta de ransomware detectado");
            if (e == null)
            {
                _logger.LogWarning("[ShieldVM] [OnRansomwareAlertDetected] Argumento e é nulo");
                return;
            }

            _logger.LogDebug($"[ShieldVM] [OnRansomwareAlertDetected] AlertType={e.AlertType}, Details={e.Details}");
            _logger.LogWarning($"[ShieldVM] [OnRansomwareAlertDetected] Ransomware alert: {e.AlertType} - {e.Details}");
            
            // CORREÇÃO THREAD-SAFE: UI bindings no dispatcher
            System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                RansomwareAlertsCount++;
                StatusMessage = string.Format(LocalizationService.Instance.GetString("RansomwareAlert"), e.AlertType);
            });
        }
        
        private void OnRansomwareMonitoringStatusChanged(object? sender, MonitoringStatusChangedEventArgs e)
        {
            _logger.LogInfo("[ShieldVM] [OnRansomwareMonitoringStatusChanged] INICIADO - Status de monitoramento de ransomware alterado");
            if (e == null)
            {
                _logger.LogWarning("[ShieldVM] [OnRansomwareMonitoringStatusChanged] Argumento e é nulo");
                return;
            }

            _logger.LogDebug($"[ShieldVM] [OnRansomwareMonitoringStatusChanged] IsActive={e.IsActive}");
            _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                IsRansomwareMonitoringActive = e.IsActive;
                RansomwareStatus = e.IsActive ? LocalizationService.Instance.GetString("MonitoringCriticalFolders") : LocalizationService.Instance.GetString("Inactive");
                _logger.LogInfo("[ShieldVM] [OnRansomwareMonitoringStatusChanged] FINALIZADO com sucesso");
            });
        }
        
        #endregion
        
        #region Quarantine Methods
        
        private void RefreshQuarantineList(bool promptIfBlocked = false)
        {
            _logger.LogInfo("[ShieldVM] [RefreshQuarantineList] INICIADO - Atualizando lista de quarentena");
            try
            {
                // Só exige licença quando a chamada vem do botão "Atualizar".
                if (promptIfBlocked && !EnsureShieldLicense("Atualizar quarentena")) return;

                _logger.LogInfo("[ShieldVM] Atualizando lista de quarentena...");
                var entries = _shieldService.Quarantine.GetAllEntries();
                
                System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    QuarantineItems.Clear();
                    foreach (var entry in entries)
                    {
                        QuarantineItems.Add(new QuarantineItemViewModel(entry));
                    }
                    QuarantineCount = entries.Count;
                    _logger.LogInfo($"[ShieldVM] Quarentena: {entries.Count} itens");
                });
            }
            catch (Exception ex)
            {
                _logger.LogError("[ShieldVM] Erro ao atualizar quarentena", ex);
            }
        }
        
        private async Task RestoreQuarantineItemAsync(QuarantineItemViewModel? item)
        {
            if (!EnsureShieldLicense("Restaurar item da quarentena")) return;
            _logger.LogInfo("[ShieldVM] [RestoreQuarantineItemAsync] INICIADO - Restaurando item da quarentena");
            if (item == null)
            {
                _logger.LogWarning("[ShieldVM] [RestoreQuarantineItemAsync] item é nulo");
                return;
            }

            _logger.LogDebug($"[ShieldVM] [RestoreQuarantineItemAsync] Item={item.OriginalName}, Id={item.Id}");
            try
            {
                _logger.LogInfo($"[ShieldVM] Restaurando da quarentena: {item.OriginalName}");
                var success = await _shieldService.Quarantine.RestoreFileAsync(item.Id);
                if (success)
                {
                    StatusMessage = string.Format(LocalizationService.Instance.GetString("QuarantineRestored"), item.OriginalName);
                    RefreshQuarantineList();
                    _logger.LogSuccess($"[ShieldVM] Restaurado da quarentena: {item.OriginalName}");
                }
                else
                {
                    StatusMessage = string.Format(LocalizationService.Instance.GetString("QuarantineRestoreFailed"), item.OriginalName);
                    _logger.LogWarning($"[ShieldVM] Falha ao restaurar: {item.OriginalName}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ShieldVM] Erro ao restaurar: {item.OriginalName}", ex);
            }
        }
        
        private async Task DeleteQuarantineItemAsync(QuarantineItemViewModel? item)
        {
            if (!EnsureShieldLicense("Excluir item da quarentena")) return;
            _logger.LogInfo("[ShieldVM] [DeleteQuarantineItemAsync] INICIADO - Excluindo item permanentemente");
            if (item == null)
            {
                _logger.LogWarning("[ShieldVM] [DeleteQuarantineItemAsync] item é nulo");
                return;
            }

            _logger.LogDebug($"[ShieldVM] [DeleteQuarantineItemAsync] Item={item.OriginalName}, Id={item.Id}");
            try
            {
                _logger.LogInfo($"[ShieldVM] Excluindo permanentemente: {item.OriginalName}");
                var success = _shieldService.Quarantine.DeletePermanently(item.Id);
                if (success)
                {
                    StatusMessage = string.Format(LocalizationService.Instance.GetString("QuarantineDeleted"), item.OriginalName);
                    RefreshQuarantineList();
                    _logger.LogSuccess($"[ShieldVM] Excluído permanentemente: {item.OriginalName}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ShieldVM] Erro ao excluir: {item.OriginalName}", ex);
            }
        }
        
        #endregion
        
        #region Port Monitor Methods
        
        private void TogglePortMonitoring()
        {
            if (!EnsureShieldLicense("Monitoramento de portas")) return;
            _logger.LogInfo("[ShieldVM] [TogglePortMonitoring] INICIADO - Alternando monitoramento de portas");
            try
            {
                if (IsPortMonitoringActive)
                {
                    _logger.LogInfo("[ShieldVM] [TogglePortMonitoring] Desativando monitoramento de portas");
                    _shieldService.PortMonitor.StopMonitoring();
                    _shieldService.PortMonitor.SnapshotUpdated -= OnPortSnapshotUpdated;
                    IsPortMonitoringActive = false;
                    StatusMessage = LocalizationService.Instance.GetString("PortMonitoringDeactivated");
                    GlobalNotificationService.ShowWarning("Shield", LocalizationService.Instance.GetString("PortMonitoringDeactivated"));
                    _logger.LogInfo("[ShieldVM] Port Monitor desativado");
                }
                else
                {
                    _logger.LogInfo("[ShieldVM] [TogglePortMonitoring] Ativando monitoramento de portas");
                    _shieldService.PortMonitor.SnapshotUpdated += OnPortSnapshotUpdated;
                    _shieldService.PortMonitor.StartMonitoring();
                    IsPortMonitoringActive = true;
                    StatusMessage = LocalizationService.Instance.GetString("PortMonitoringActivated");
                    GlobalNotificationService.ShowSuccess("Shield", LocalizationService.Instance.GetString("PortMonitoringActivated"));
                    _logger.LogInfo("[ShieldVM] Port Monitor ativado");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("[ShieldVM] [TogglePortMonitoring] Erro ao alternar Port Monitor", ex);
            }
        }
        
        private void RefreshConnections()
        {
            RefreshConnections(showNotification: true);
        }

        private void RefreshConnections(bool showNotification)
        {
            _logger.LogInfo("[ShieldVM] [RefreshConnections] INICIADO - Atualizando conexões de rede");
            try
            {
                if (!EnsureShieldLicense("Atualizar conexões")) return;

                var snapshot = _shieldService.PortMonitor.GetCurrentSnapshot();
                _logger.LogDebug($"[ShieldVM] [RefreshConnections] Snapshot obtido: TotalConnections={snapshot?.TotalConnections}");
                UpdateConnectionsUI(snapshot);
                if (showNotification)
                {
                    GlobalNotificationService.ShowInfo("Shield", string.Format(LocalizationService.Instance.GetString("ActiveConnectionsCount"), ActiveConnectionsCount));
                }
                _logger.LogInfo("[ShieldVM] [RefreshConnections] FINALIZADO com sucesso");
            }
            catch (Exception ex)
            {
                _logger.LogError("[ShieldVM] [RefreshConnections] Erro ao atualizar conexões", ex);
            }
        }
        
        private void OnPortSnapshotUpdated(object? sender, ConnectionSnapshot snapshot)
        {
            _logger.LogInfo("[ShieldVM] [OnPortSnapshotUpdated] INICIADO - Snapshot de portas atualizado");
            try
            {
                _logger.LogDebug($"[ShieldVM] [OnPortSnapshotUpdated] TotalConnections={snapshot?.TotalConnections}, Disparando UpdateConnectionsUI");
                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() => UpdateConnectionsUI(snapshot));
                _logger.LogInfo("[ShieldVM] [OnPortSnapshotUpdated] FINALIZADO com sucesso");
            }
            catch
            {
                _logger.LogWarning("[ShieldVM] [OnPortSnapshotUpdated] Erro ao invocar UpdateConnectionsUI no Dispatcher");
            }
        }
        
        private void UpdateConnectionsUI(ConnectionSnapshot snapshot)
        {
            _logger.LogInfo("[ShieldVM] [UpdateConnectionsUI] INICIADO - Atualizando interface de conexões");
            _logger.LogDebug($"[ShieldVM] [UpdateConnectionsUI] TotalConnections={snapshot?.TotalConnections}, Suspicious={snapshot?.SuspiciousConnections?.Count}, AllConnections={snapshot?.AllConnections?.Count}");
            ActiveConnectionsCount = snapshot.TotalConnections;
            SuspiciousConnectionsCount = snapshot.SuspiciousConnections.Count + snapshot.SuspiciousListeners.Count;
            
            CollectionDiffUpdater.ReplaceInPlace(_suspiciousConnections,
                snapshot.SuspiciousConnections.Take(20).Select(c => new ConnectionViewModel(c)),
                c => c.ConnectionKey);

            CollectionDiffUpdater.ReplaceInPlace(_allActiveConnections,
                snapshot.AllConnections.Take(50).Select(c => new ConnectionViewModel(c)),
                c => c.ConnectionKey);
            _logger.LogInfo("[ShieldVM] [UpdateConnectionsUI] FINALIZADO com sucesso");
        }
        
        #endregion
        
        #region Threat Protection Methods
        
        private void ToggleThreatProtection()
        {
            if (!EnsureShieldLicense("Proteção avançada de ameaças")) return;
            _logger.LogInfo("[ShieldVM] [ToggleThreatProtection] INICIADO - Alternando proteção contra ameaças");
            try
            {
                if (IsThreatProtectionActive)
                {
                    _logger.LogInfo("[ShieldVM] [ToggleThreatProtection] Desativando proteção contra ameaças");
                    _shieldService.ThreatProtection.StopProtection();
                    _shieldService.ThreatProtection.ThreatAlertRaised -= OnProcessThreatDetected;
                    IsThreatProtectionActive = false;
                    StatusMessage = LocalizationService.Instance.GetString("AdvancedProtectionDeactivated");
                    GlobalNotificationService.ShowWarning("Shield", LocalizationService.Instance.GetString("AdvancedProtectionDeactivated"));
                    _logger.LogInfo("[ShieldVM] Threat Protection desativado");
                }
                else
                {
                    _logger.LogInfo("[ShieldVM] [ToggleThreatProtection] Ativando proteção contra ameaças");
                    _shieldService.ThreatProtection.ThreatAlertRaised += OnProcessThreatDetected;
                    _shieldService.ThreatProtection.StartProtection();
                    IsThreatProtectionActive = true;
                    StatusMessage = LocalizationService.Instance.GetString("AdvancedProtectionActivated");
                    GlobalNotificationService.ShowSuccess("Shield", LocalizationService.Instance.GetString("AdvancedProtectionActivated"));
                    _logger.LogInfo("[ShieldVM] Threat Protection ativado");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("[ShieldVM] [ToggleThreatProtection] Erro ao alternar Threat Protection", ex);
            }
        }
        
        private void RefreshActiveThreats()
        {
            _logger.LogInfo("[ShieldVM] [RefreshActiveThreats] INICIADO - Atualizando ameaças ativas");
            try
            {
                if (!EnsureShieldLicense("Atualizar ameaças ativas")) return;

                var threats = _shieldService.ThreatProtection.GetActiveThreats();
                _logger.LogDebug($"[ShieldVM] [RefreshActiveThreats] {threats.Count} ameaças ativas encontradas");
                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    ActiveProcessThreats.Clear();
                    foreach (var t in threats)
                    {
                        ActiveProcessThreats.Add(new ProcessThreatViewModel(t));
                    }
                    ActiveThreatsCount = threats.Count;
                    _logger.LogInfo($"[ShieldVM] Ameaças ativas: {threats.Count}");
                });
            }
            catch (Exception ex)
            {
                _logger.LogError("[ShieldVM] Erro ao atualizar ameaças ativas", ex);
            }
        }
        
        private void OnProcessThreatDetected(object? sender, ThreatAlertEventArgs e)
        {
            _logger.LogInfo("[ShieldVM] [OnProcessThreatDetected] INICIADO - Ameaça de processo detectada");
            if (e == null)
            {
                _logger.LogWarning("[ShieldVM] [OnProcessThreatDetected] Argumento e é nulo");
                return;
            }

            _logger.LogDebug($"[ShieldVM] [OnProcessThreatDetected] ProcessName={e.ProcessName}, ProcessId={e.ProcessId}, ThreatType={e.ThreatType}, Severity={e.Severity}");
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                ActiveProcessThreats.Add(new ProcessThreatViewModel(new ProcessScanResult
                {
                    ProcessId = e.ProcessId,
                    ProcessName = e.ProcessName,
                    IsMalicious = true,
                    ThreatType = e.ThreatType,
                    Severity = e.Severity,
                    Details = e.Details
                }));
                ActiveThreatsCount = ActiveProcessThreats.Count;
                StatusMessage = string.Format(LocalizationService.Instance.GetString("ThreatAlert"), e.ThreatType, e.ProcessName);
                _logger.LogWarning($"[ShieldVM] Ameaça em tempo real: {e.ProcessName} ({e.ThreatType})");

                // Se a ameaça expirar sozinha, o contador da Visão Geral precisa
                // refletir isso sem depender do botão "Atualizar".
                ReconcileThreatCounters();
            });
        }
        
        #endregion
        
        #region IDisposable
        
        public void Dispose()
        {
            _logger.LogInfo("[ShieldVM] [Dispose] INICIADO - Descartando ShieldViewModel");
            if (_disposed)
            {
                _logger.LogWarning("[ShieldVM] [Dispose] Já foi descartado anteriormente");
                return;
            }

            try
            {
                _logger.LogInfo("[ShieldVM] Disposing ShieldViewModel...");
                
                _licenseGate.LicenseChanged -= OnShieldLicenseChanged;
                _shieldService.StatusChanged -= OnShieldStatusChanged;
                _shieldService.ThreatDetected -= OnThreatDetected;
                _networkMonitor.StatusChanged -= OnNetworkMonitoringStatusChanged;
                _deviceTracker.NewDeviceDetected -= OnNewDeviceDetected;
                _deviceTracker.DeviceDisconnected -= OnDeviceDisconnected;
                _ransomwareMonitor.StatusChanged -= OnRansomwareMonitoringStatusChanged;
                _ransomwareMonitor.SuspiciousActivityDetected -= OnRansomwareAlertDetected;
                try { LocalizationService.Instance.LanguageChanged -= OnLanguageChanged; } catch { /* singleton */ }
                
                // Cleanup novos módulos
                try { _shieldService.PortMonitor.SnapshotUpdated -= OnPortSnapshotUpdated; } catch (Exception ex) { _logger.LogWarning($"[ShieldVM] Erro ao limpar evento SnapshotUpdated: {ex.Message}"); }
                try { _shieldService.ThreatProtection.ThreatAlertRaised -= OnProcessThreatDetected; } catch (Exception ex) { _logger.LogWarning($"[ShieldVM] Erro ao limpar evento ThreatAlertRaised: {ex.Message}"); }
                
                _logger.LogSuccess("[ShieldVM] ShieldViewModel disposed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError("[ShieldVM] Erro ao fazer dispose do ViewModel", ex);
            }
            finally
            {
                _disposed = true;
            }
        }
        
        #endregion
        
        private void KillProcessesUsingFile(string filePath)
        {
            _logger.LogInfo("[ShieldVM] [KillProcessesUsingFile] INICIADO - Encerrando processos associados ao arquivo");
            _logger.LogDebug($"[ShieldVM] [KillProcessesUsingFile] FilePath={filePath}");
            try
            {
                // NOVO: Usar Restart Manager para identificar bloqueadores reais de handles
                var lockers = Win32FileHelper.GetProcessesLockingFile(filePath);
                
                if (lockers.Count == 0)
                {
                    // Fallback para busca por nome se nenhum handle for detectado
                    var fileName = Path.GetFileNameWithoutExtension(filePath);
                    lockers = System.Diagnostics.Process.GetProcessesByName(fileName).ToList();
                }

                foreach (var proc in lockers)
                {
                    try
                    {
                        _logger.LogWarning($"[ShieldVM] Encerrando processo ativo da ameaça: {proc.ProcessName} (PID: {proc.Id})");
                        proc.Kill(true);
                        proc.WaitForExit(2000);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ShieldVM] Falha ao tentar encerrar processos para {filePath}: {ex.Message}");
            }
        }
    }
    
    #region ViewModels Auxiliares
    
    public class ScanThreatItemViewModel : ViewModelBase
    {
        private readonly ScanThreatItem _item;
        
        public string Name => _item.Name;
        public string Path => _item.Path;
        public string ThreatType => _item.ThreatType;
        public ThreatSeverity Severity => _item.Severity;
        
        public string SeverityText => _item.Severity switch
        {
            ThreatSeverity.Critical => LocalizationService.Instance.GetString("SeverityCritical"),
            ThreatSeverity.High => LocalizationService.Instance.GetString("SeverityHigh"),
            ThreatSeverity.Medium => LocalizationService.Instance.GetString("SeverityMedium"),
            _ => LocalizationService.Instance.GetString("SeverityLow")
        };
        
        public Brush SeverityColor => _item.Severity switch
        {
            ThreatSeverity.Critical => new SolidColorBrush(Color.FromRgb(220, 38, 38)),
            ThreatSeverity.High => new SolidColorBrush(Color.FromRgb(255, 68, 102)),
            ThreatSeverity.Medium => new SolidColorBrush(Color.FromRgb(255, 170, 0)),
            _ => new SolidColorBrush(Color.FromRgb(250, 204, 21))
        };
        
        public string ThreatTypeDisplay => _item.ThreatType switch
        {
            "Directory" => LocalizationService.Instance.GetString("SuspiciousDirectory"),
            "RegistryKey" => LocalizationService.Instance.GetString("RegistryKey"),
            "File" => LocalizationService.Instance.GetString("SuspiciousFile"),
            _ => _item.ThreatType
        };
        
        public ScanThreatItemViewModel(ScanThreatItem item)
        {
            _item = item ?? throw new ArgumentNullException(nameof(item));
        }
    }
    
    public class StartupItemViewModel : ViewModelBase
    {
        private readonly StartupItem _item;
        private bool _isEnabled;
        
        public string Name => _item.Name;
        public string Path => _item.Path;
        public string Location => _item.Location.ToString();
        public bool IsSuspicious => _item.IsSuspicious;
        
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }
        
        public Brush StatusColor => IsSuspicious 
            ? new SolidColorBrush(Color.FromRgb(255, 68, 102)) 
            : new SolidColorBrush(Color.FromRgb(0, 255, 136));
        
        public StartupItemViewModel(StartupItem item)
        {
            _item = item ?? throw new ArgumentNullException(nameof(item));
            _isEnabled = item.IsEnabled;
        }
        
        public StartupItem GetStartupItem() => _item;
    }
    
    public class AdwareItemViewModel : ViewModelBase
    {
        private readonly AdwareItem _item;
        
        public string Name => _item.Name;
        public string Path => _item.Path;
        public string Type => _item.Type.ToString();
        public string Severity => _item.Severity.ToString();
        
        public Brush SeverityColor => _item.Severity switch
        {
            AdwareSeverity.High => new SolidColorBrush(Color.FromRgb(255, 68, 102)),
            AdwareSeverity.Medium => new SolidColorBrush(Color.FromRgb(255, 170, 0)),
            _ => new SolidColorBrush(Color.FromRgb(255, 255, 136))
        };
        
        public AdwareItemViewModel(AdwareItem item)
        {
            _item = item ?? throw new ArgumentNullException(nameof(item));
        }
        
        public AdwareItem GetAdwareItem() => _item;
    }
    
    #endregion
    
    public class QuarantineItemViewModel : ViewModelBase
    {
        private readonly QuarantineEntry _entry;
        public string Id => _entry.Id;
        public string OriginalName => _entry.OriginalName;
        public string OriginalPath => _entry.OriginalPath;
        public string SHA256 => _entry.SHA256;
        public string Reason => _entry.Reason;
        public ThreatSeverity Severity => _entry.Severity;
        public string SizeDisplay => _entry.SizeBytes >= 1048576 ? $"{_entry.SizeBytes / 1048576.0:F1} MB" : $"{_entry.SizeBytes / 1024.0:F0} KB";
        public string QuarantinedDate => _entry.QuarantinedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
        public Brush SeverityColor => _entry.Severity switch
        {
            ThreatSeverity.Critical => new SolidColorBrush(Color.FromRgb(220, 38, 38)),
            ThreatSeverity.High => new SolidColorBrush(Color.FromRgb(255, 68, 102)),
            ThreatSeverity.Medium => new SolidColorBrush(Color.FromRgb(255, 170, 0)),
            _ => new SolidColorBrush(Color.FromRgb(250, 204, 21))
        };
        public QuarantineItemViewModel(QuarantineEntry entry) { _entry = entry ?? throw new ArgumentNullException(nameof(entry)); }
    }
    
    public class ConnectionViewModel : ViewModelBase
    {
        private readonly ConnectionRecord _record;
        public string ProcessName => _record.ProcessName ?? LocalizationService.Instance.GetString("Unknown");
        public string RemoteEndpoint => $"{_record.RemoteIp}:{_record.RemotePort}";
        public string LocalPort => $":{_record.LocalPort}";
        public string State => _record.State;
        public string FirstSeen => _record.FirstSeen.ToString("HH:mm:ss");
        public string FirstSeenLabel => string.Format(LocalizationService.Instance.GetString("Loc_FirstSeen"), FirstSeen);
        public string FirstSeenDetectedLabel => string.Format(LocalizationService.Instance.GetString("Loc_SuspiciousDetected"), FirstSeen);
        public SuspicionLevel Level => _record.SuspicionLevel;
        public string LevelText => _record.SuspicionLevel switch { SuspicionLevel.Critical => LocalizationService.Instance.GetString("SeverityCritical"), SuspicionLevel.High => LocalizationService.Instance.GetString("SeverityHigh"), SuspicionLevel.Medium => LocalizationService.Instance.GetString("SeverityMedium"), _ => LocalizationService.Instance.GetString("SeverityLow") };
        public Brush LevelColor => _record.SuspicionLevel switch
        {
            SuspicionLevel.Critical => new SolidColorBrush(Color.FromRgb(220, 38, 38)),
            SuspicionLevel.High => new SolidColorBrush(Color.FromRgb(255, 68, 102)),
            SuspicionLevel.Medium => new SolidColorBrush(Color.FromRgb(255, 170, 0)),
            _ => new SolidColorBrush(Color.FromRgb(250, 204, 21))
        };
        public string ConnectionKey => $"{_record.RemoteIp}:{_record.RemotePort}:{_record.LocalPort}";
        public ConnectionViewModel(ConnectionRecord record) { _record = record ?? throw new ArgumentNullException(nameof(record)); }
    }
    
    public class ProcessThreatViewModel : ViewModelBase
    {
        private readonly ProcessScanResult _result;
        public string ProcessName => _result.ProcessName;
        public int ProcessId => _result.ProcessId;
        public string ThreatType => _result.ThreatType;
        public string Details => _result.Details;
        public ThreatSeverity Severity => _result.Severity;
        public string SeverityText => _result.Severity switch { ThreatSeverity.Critical => LocalizationService.Instance.GetString("SeverityCritical"), ThreatSeverity.High => LocalizationService.Instance.GetString("SeverityHigh"), _ => LocalizationService.Instance.GetString("SeverityMedium") };
        public Brush SeverityColor => _result.Severity switch
        {
            ThreatSeverity.Critical => new SolidColorBrush(Color.FromRgb(220, 38, 38)),
            ThreatSeverity.High => new SolidColorBrush(Color.FromRgb(255, 68, 102)),
            _ => new SolidColorBrush(Color.FromRgb(255, 170, 0))
        };
        public ProcessThreatViewModel(ProcessScanResult result) { _result = result ?? throw new ArgumentNullException(nameof(result)); }
    }
    
    public class NetworkDeviceViewModel : ViewModelBase
    {
        private readonly NetworkDevice _device;
        
        public string Hostname => _device.Hostname;
        public string IPAddress => _device.IPAddress;
        public string MacAddress => _device.MacAddress;
        public string Vendor => _device.Vendor;

        // O modelo (NetworkDevice) armazena o sentinela neutro "Unknown". Os valores em
        // português abaixo são fallbacks para dispositivos já persistidos antes da correção,
        // para que nada salvo em cache continue aparecendo em português em ES/EN.
        public string DeviceType => LocalizeDeviceText(_device.DeviceType, "UnknownDevice");
        public string OperatingSystem => LocalizeDeviceText(_device.OperatingSystem, "Unknown");

        private static string LocalizeDeviceText(string? value, string unknownKey)
        {
            if (string.IsNullOrWhiteSpace(value)) return LocalizationService.Instance.GetString(unknownKey);
            if (value == "Unknown" || value == "Desconhecido" || value == "Dispositivo Desconhecido")
            {
                return LocalizationService.Instance.GetString(unknownKey);
            }
            return value;
        }

        /// <summary>
        /// Indica se o sistema operacional do dispositivo foi realmente identificado.
        /// Usado pela UI para ocultar a linha "· SO" quando o valor é desconhecido, sem
        /// depender de comparação com texto traduzido.
        /// </summary>
        public bool HasKnownOperatingSystem
        {
            get
            {
                string? os = _device.OperatingSystem;
                return !string.IsNullOrWhiteSpace(os)
                    && os != "Unknown"
                    && os != "Desconhecido";
            }
        }

        public string FriendlyName => _device.FriendlyName;
        public string DeviceIcon => _device.Icon;
        public bool IsGateway => _device.IsGateway;
        public int ConfidenceLevel => _device.ConfidenceLevel;
        public bool IsOnline => _device.IsOnline;
        public bool IsNew => _device.IsNew;
        public string FirstSeenText => _device.FirstSeen.ToString("dd/MM/yyyy HH:mm");
        public string LastSeenText => _device.LastSeen.ToString("HH:mm:ss");
        
        public Brush StatusColor => IsOnline 
            ? new SolidColorBrush(Color.FromRgb(0, 255, 136)) 
            : new SolidColorBrush(Color.FromRgb(255, 68, 102));
        
        public string StatusText => IsOnline ? LocalizationService.Instance.GetString("Online") : LocalizationService.Instance.GetString("Offline");
        
        public string DeviceInfo
        {
            get
            {
                if (IsGateway)
                    return string.Format(LocalizationService.Instance.GetString("GatewayDeviceInfo"), DeviceType);
                
                if (!string.IsNullOrEmpty(OperatingSystem) && OperatingSystem != "Unknown")
                    return string.Format(LocalizationService.Instance.GetString("DeviceInfoFormat"), DeviceType, OperatingSystem);
                
                return DeviceType;
            }
        }
        
        public string ConfidenceText => ConfidenceLevel > 0 ? string.Format(LocalizationService.Instance.GetString("ConfidencePercent"), ConfidenceLevel) : "";
        
        public NetworkDeviceViewModel(NetworkDevice device)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
        }
    }
}
