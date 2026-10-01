using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.UI.Commands;
using VoltrisOptimizer.UI.Helpers;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Models;
using VoltrisOptimizer.Core.Configuration;
using VoltrisOptimizer.Services.License;

namespace VoltrisOptimizer.UI.ViewModels
{
    /// <summary>
    /// Estados de licença da interface unificada
    /// </summary>
    public enum LicenseUIState
    {
        NoLicense,      // Sem licença ativa
        Activating,     // Processando ativação
        Active,         // Licença ativa
        Changing,       // Alterando para nova licença
        Deactivating,   // Desativando licença
        Error           // Erro na operação
    }

    /// <summary>
    /// ViewModel para a tela de ativação de licença (agora também gerenciamento)
    /// </summary>
    public partial class LicenseActivationViewModel : ViewModelBase
    {
        private readonly ILoggingService? _logger;
        private CancellationTokenSource? _cancellationTokenSource;

        private string _deviceId = "";
        public string DeviceId 
        { 
            get => _deviceId;
            private set
            {
                _logger?.LogInfo($"[LicenseActivationViewModel] DeviceId SET - Antes: '{_deviceId}' (length: {_deviceId?.Length ?? 0})");
                _logger?.LogInfo($"[LicenseActivationViewModel] DeviceId SET - Depois: '{value}' (length: {value?.Length ?? 0})");
                _deviceId = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HwidDisplay));
                _logger?.LogInfo("[LicenseActivationViewModel] DeviceId SET - OnPropertyChanged() chamado (incluindo HwidDisplay)");
            }
        }

        // ════════════════════════════════════════════════════════════════════════════════
        // ESTADO DE LICENÇA - Propriedades para Gerenciamento
        // ════════════════════════════════════════════════════════════════════════════════

        private LicenseUIState _uiState = LicenseUIState.NoLicense;
        public LicenseUIState UiState
        {
            get => _uiState;
            private set
            {
                if (SetProperty(ref _uiState, value))
                {
                    _logger?.LogInfo($"╔════════════════════════════════════════════════════════════════════════════════╗");
                    _logger?.LogInfo($"║ [UiState] MUDANÇA DETECTADA                                                      ║");
                    _logger?.LogInfo($"║ Novo UiState: {value}                                                              ║");
                    _logger?.LogInfo($"╚════════════════════════════════════════════════════════════════════════════════╝");
                    
                    _logger?.LogInfo($"[UiState] Disparando OnPropertyChanged para todas as propriedades derivadas...");
                    OnPropertyChanged(nameof(IsLicenseActive));
                    _logger?.LogInfo($"[UiState] ✓ IsLicenseActive notificado");
                    
                    OnPropertyChanged(nameof(IsInActivationMode));
                    _logger?.LogInfo($"[UiState] ✓ IsInActivationMode notificado");
                    
                    OnPropertyChanged(nameof(CanShowLicenseInfo));
                    _logger?.LogInfo($"[UiState] ✓ CanShowLicenseInfo notificado | Valor agora: {CanShowLicenseInfo}");
                    
                    OnPropertyChanged(nameof(CanShowActivationForm));
                    _logger?.LogInfo($"[UiState] ✓ CanShowActivationForm notificado | Valor agora: {CanShowActivationForm}");
                    
                    OnPropertyChanged(nameof(ShowDeactivationDialog));
                    _logger?.LogInfo($"[UiState] ✓ ShowDeactivationDialog notificado | Valor agora: {ShowDeactivationDialog}");
                    
                    _logger?.LogInfo($"[UiState] Todas as propriedades atualizadas com sucesso");
                }
                else
                {
                    _logger?.LogWarning($"[UiState] SetProperty retornou false - valor não mudou (ainda é {_uiState})");
                }
            }
        }

        // Estado da licença ativa (from LicenseTokenStore)
        private bool _isLicenseActive = false;
        public bool IsLicenseActive
        {
            get => _isLicenseActive;
            private set => SetProperty(ref _isLicenseActive, value);
        }

        // Tipo de licença ativa (Pro, Standard, Enterprise)
        private string _activeLicenseType = "";
        public string ActiveLicenseType
        {
            get => _activeLicenseType;
            private set => SetProperty(ref _activeLicenseType, value);
        }

        // Chave da licença ativa
        private string _activeLicenseKey = "";
        public string ActiveLicenseKey
        {
            get => _activeLicenseKey;
            private set => SetProperty(ref _activeLicenseKey, value);
        }

        // Data de ativação da licença
        private DateTime? _licenseActivationDate;
        public DateTime? LicenseActivationDate
        {
            get => _licenseActivationDate;
            private set => SetProperty(ref _licenseActivationDate, value);
        }

        // Data de expiração da licença
        private DateTime? _licenseExpirationDate;
        public DateTime? LicenseExpirationDate
        {
            get => _licenseExpirationDate;
            private set
            {
                if (SetProperty(ref _licenseExpirationDate, value))
                {
                    OnPropertyChanged(nameof(LicenseValidityDisplay));
                }
            }
        }

        // Período de faturamento da licença (mensal ou anual)
        private string _billingPeriod = "";
        public string BillingPeriod
        {
            get => _billingPeriod;
            private set
            {
                if (SetProperty(ref _billingPeriod, value))
                {
                    OnPropertyChanged(nameof(LicenseValidityDisplay));
                }
            }
        }

        // Máximo de dispositivos permitidos
        private int _maxDevices = 0;
        public int MaxDevices
        {
            get => _maxDevices;
            private set => SetProperty(ref _maxDevices, value);
        }

        // Dispositivos em uso
        private int _devicesInUse = 0;
        public int DevicesInUse
        {
            get => _devicesInUse;
            private set => SetProperty(ref _devicesInUse, value);
        }

        // Exibição formatada da validade - COM PERÍODO DE FATURAMENTO
        public string LicenseValidityDisplay
        {
            get
            {
                if (!IsLicenseActive) return "";
                if (LicenseExpirationDate == null) return "Vitalícia";
                
                var expDate = LicenseExpirationDate.Value;
                if (expDate.Year == 9999) return "Vitalícia";
                
                var dateStr = expDate.ToString("dd/MM/yyyy");
                
                // Adicionar período de faturamento se disponível
                if (!string.IsNullOrWhiteSpace(BillingPeriod))
                {
                    var periodDisplay = BillingPeriod.ToLower() == "mensal" ? "Mensal" : "Anual";
                    return $"{dateStr} - {periodDisplay}";
                }
                
                return dateStr;
            }
        }

        // Computed: Mostra informação de slots de dispositivos
        public string DeviceSlotsDisplay
        {
            get
            {
                if (!IsLicenseActive || MaxDevices == 0) return "";
                if (MaxDevices >= 9999) return "Ilimitado";
                return $"{DevicesInUse} de {MaxDevices}";
            }
        }
        
        private bool _isOnline = true;
        public bool IsOnline 
        { 
            get => _isOnline;
            private set
            {
                _logger?.LogInfo($"[LicenseActivationViewModel] IsOnline SET - Antes: {_isOnline}");
                _logger?.LogInfo($"[LicenseActivationViewModel] IsOnline SET - Depois: {value}");
                _isOnline = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(OfflineWarningVisible));
                OnPropertyChanged(nameof(ConnectionStatusColor));
                OnPropertyChanged(nameof(ConnectionStatusText));
                _logger?.LogInfo("[LicenseActivationViewModel] IsOnline SET - OnPropertyChanged() chamado (incluindo propriedades dependentes)");
            }
        }

        public LicenseActivationViewModel()
        {
            _logger = App.LoggingService;
            _logger?.LogInfo("=== [LicenseActivationViewModel] CONSTRUTOR INICIADO ===");
            
            // Inicializar propriedades básicas (síncrono)
            _logger?.LogInfo("[LicenseActivationViewModel] Configurando IsOnline = true...");
            IsOnline = true;
            
            // Freemium: sem trial, apenas inicializar variáveis
            _logger?.LogInfo($"[LicenseActivationViewModel] Freemium mode - sem trial");
            TrialDaysRemaining = 0;
            _logger?.LogInfo($"[LicenseActivationViewModel] TrialDaysRemaining: 0 (Freemium)");
            
            _logger?.LogInfo("[LicenseActivationViewModel] Configurando StatusMessage = \"\"...");
            StatusMessage = "";
            _logger?.LogInfo("[LicenseActivationViewModel] Configurando ErrorMessage = \"\"...");
            ErrorMessage = "";
            _logger?.LogInfo("[LicenseActivationViewModel] Propriedades básicas inicializadas com sucesso");
            
            // Inicializar comandos
            _logger?.LogInfo("[LicenseActivationViewModel] Criando ActivateCommand...");
            ActivateCommand = new AsyncRelayCommand(async _ => await ActivateLicenseAsync(), _ => CanActivate);
            CopyDeviceIdCommand = new RelayCommand(_ => CopyDeviceId());
            CopyHwidCommand = new RelayCommand(_ => CopyDeviceId());
            SyncWithServerCommand = new AsyncRelayCommand(async _ => await SyncWithServerAsync());
            
            // Novos comandos para gerenciamento de licença
            CopyLicenseKeyCommand = new RelayCommand(_ => CopyLicenseKey());
            ChangeLicenseCommand = new RelayCommand(_ => EnterChangeLicenseMode());
            DeactivateLicenseCommand = new RelayCommand(_ => PromptDeactivateLicense());
            ConfirmDeactivationCommand = new AsyncRelayCommand(async _ => await ConfirmDeactivationAsync());
            CancelDeactivationCommand = new RelayCommand(_ => CancelDeactivation());
            
            _logger?.LogInfo("[LicenseActivationViewModel] Todos os comandos criados com sucesso");
            
            // Inicializar HWID de forma assíncrona sem bloquear
            _ = InitializeHwidAsync();
        }

        private async Task InitializeHwidAsync()
        {
            try
            {
                _logger?.LogInfo("=== [LicenseActivationViewModel] INICIALIZAÇÃO HWID ASSÍNCRONA INICIADA ===");
                
                _logger?.LogInfo("[LicenseActivationViewModel] Obtendo HardwareTrialService...");
                var hwidService = VoltrisOptimizer.Services.License.HardwareTrialService.Instance;
                _logger?.LogInfo($"[LicenseActivationViewModel] hwidService null? {hwidService == null}");
                if (hwidService != null)
                {
                    _logger?.LogInfo($"[LicenseActivationViewModel] hwidService.IsInitialized: {hwidService.IsInitialized}");
                }
                
                if (hwidService != null && hwidService.IsInitialized)
                {
                    _logger?.LogInfo("[LicenseActivationViewModel] Chamando GetHwidAsync()...");
                    var hwid = await hwidService.GetHwidAsync(); 
                    _logger?.LogInfo($"[LicenseActivationViewModel] HWID bruto: '{hwid}' (length: {hwid?.Length ?? 0})");
                    
                    DeviceId = hwid ?? Environment.MachineName;
                    _logger?.LogInfo($"[LicenseActivationViewModel] DeviceId final: '{DeviceId}' (length: {DeviceId.Length})");
                    _logger?.LogInfo($"[LicenseActivationViewModel] HWID exibido: {DeviceId.Substring(0, Math.Min(16, DeviceId.Length))}...");
                }
                else
                {
                    DeviceId = Environment.MachineName;
                    _logger?.LogWarning("[LicenseActivationViewModel] HardwareTrialService não inicializado, usando MachineName");
                }
                
                _logger?.LogInfo($"[LicenseActivationViewModel] HWID inicializado - DeviceId: '{DeviceId}', IsOnline: {IsOnline}, TrialDaysRemaining: {TrialDaysRemaining}");
                _logger?.LogInfo("[LicenseActivationViewModel] ===== FIM DO CONSTRUTOR =====");
            }
            catch (Exception ex)
            {
                _logger?.LogError("[LicenseActivationViewModel] ERRO NO CONSTRUTOR", ex);
                _logger?.LogError($"[LicenseActivationViewModel] Exception Type: {ex.GetType().Name}");
                _logger?.LogError($"[LicenseActivationViewModel] Exception Message: {ex.Message}");
                _logger?.LogError($"[LicenseActivationViewModel] Exception StackTrace: {ex.StackTrace}");
                
                if (ex.InnerException != null)
                {
                    _logger?.LogError($"[LicenseActivationViewModel] InnerException Type: {ex.InnerException.GetType().Name}");
                    _logger?.LogError($"[LicenseActivationViewModel] InnerException Message: {ex.InnerException.Message}");
                    _logger?.LogError($"[LicenseActivationViewModel] InnerException StackTrace: {ex.InnerException.StackTrace}");
                }
                
                throw; // Re-lançar para não esconder o erro
            }
        }

        public event EventHandler? ActivationSucceeded;
        public event EventHandler? LicenseStateChanged;

        public ICommand ActivateCommand { get; }
        public ICommand CopyDeviceIdCommand { get; }
        public ICommand CopyHwidCommand { get; }
        public ICommand SyncWithServerCommand { get; }
        
        // Novos comandos para gerenciamento
        public ICommand CopyLicenseKeyCommand { get; }
        public ICommand ChangeLicenseCommand { get; }
        public ICommand DeactivateLicenseCommand { get; }
        public ICommand ConfirmDeactivationCommand { get; }
        public ICommand CancelDeactivationCommand { get; }

        private string _licenseKey = "";
        public string LicenseKey
        {
            get => _licenseKey;
            set
            {
                if (SetProperty(ref _licenseKey, value))
                {
                    OnPropertyChanged(nameof(CanActivate));
                    ShowError = false;
                    ErrorMessage = "";
                }
            }
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        private bool _isActivating;
        public bool IsActivating
        {
            get => _isActivating;
            set
            {
                if (SetProperty(ref _isActivating, value))
                {
                    OnPropertyChanged(nameof(CanActivate));
                }
            }
        }

        private string _statusMessage = "";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private string _hwidStatusMessage = "";
        public string HwidStatusMessage
        {
            get => _hwidStatusMessage;
            set => SetProperty(ref _hwidStatusMessage, value);
        }

        private string _licenseStatusMessage = "";
        public string LicenseStatusMessage
        {
            get => _licenseStatusMessage;
            set => SetProperty(ref _licenseStatusMessage, value);
        }

        private bool _showError;
        public bool ShowError
        {
            get => _showError;
            set => SetProperty(ref _showError, value);
        }

        private string _errorMessage = "";
        public string ErrorMessage
        {
            get => _errorMessage;
            set
            {
                if (SetProperty(ref _errorMessage, value))
                {
                    ShowError = !string.IsNullOrWhiteSpace(value);
                    if (ShowError) ShowSuccess = false;
                }
            }
        }

        private bool _showSuccess;
        private static readonly int DefaultTrialDays = 0; // Freemium: sem trial
        public bool ShowSuccess
        {
            get => _showSuccess;
            set => SetProperty(ref _showSuccess, value);
        }

        private int _trialDaysRemaining = 0; // Freemium: sem trial
        public int TrialDaysRemaining
        {
            get => _trialDaysRemaining;
            set
            {
                _logger?.LogInfo($"[LicenseActivationViewModel] TrialDaysRemaining SET - Antes: {_trialDaysRemaining}");
                _logger?.LogInfo($"[LicenseActivationViewModel] TrialDaysRemaining SET - Depois: {value}");
                
                if (SetProperty(ref _trialDaysRemaining, value))
                {
                    _logger?.LogInfo("[LicenseActivationViewModel] TrialDaysRemaining SET - SetProperty retornou true, chamando OnPropertyChanged");
                    OnPropertyChanged(nameof(DaysRemainingText));
                    OnPropertyChanged(nameof(TrialProgressPercent));
                    OnPropertyChanged(nameof(TrialStatusText));
                    OnPropertyChanged(nameof(TrialStatusMessage));
                    OnPropertyChanged(nameof(TrialStatusColor));
                    OnPropertyChanged(nameof(CanActivate));
                    _logger?.LogInfo("[LicenseActivationViewModel] TrialDaysRemaining SET - OnPropertyChanged concluído (incluindo propriedades dependentes)");
                }
                else
                {
                    _logger?.LogInfo("[LicenseActivationViewModel] TrialDaysRemaining SET - SetProperty retornou false (valor igual)");
                }
            }
        }

        public string DaysRemainingText
        {
            get
            {
                if (TrialDaysRemaining <= 0)
                {
                    return LocalizationService.Instance.GetString("LicTrialExpired");
                }
                
                if (TrialDaysRemaining == 1)
                {
                    return LocalizationService.Instance.GetString("LicOneDayRemaining");
                }
                
                return string.Format(LocalizationService.Instance.GetString("LicDaysRemaining"), TrialDaysRemaining);
            }
        }

        public string TrialStatusText
        {
            get
            {
                if (TrialDaysRemaining <= 0)
                {
                    return LocalizationService.Instance.GetString("LicTrialEnded");
                }
                
                return LocalizationService.Instance.GetString("LicTrialActive");
            }
        }

        /// <summary>
        /// HWID formatado para exibição na UI
        /// </summary>
        public string HwidDisplay
        {
            get
            {
                if (string.IsNullOrEmpty(DeviceId))
                {
                    return LocalizationService.Instance.GetString("LicLoading");
                }
                
                // Exibir primeiros 16 caracteres + "..."
                if (DeviceId.Length > 16)
                {
                    return DeviceId.Substring(0, 16) + "...";
                }
                
                return DeviceId;
            }
        }

        /// <summary>
        /// Indica se o aviso de modo offline deve ser visível
        /// </summary>
        public bool OfflineWarningVisible => !IsOnline;

        /// <summary>
        /// Mensagem de status do trial para exibição
        /// </summary>
        public string TrialStatusMessage
        {
            get
            {
                if (TrialDaysRemaining <= 0)
                {
                    return LocalizationService.Instance.GetString("LicTrialExpired");
                }
                
                if (TrialDaysRemaining == 1)
                {
                    return LocalizationService.Instance.GetString("LicOneDayRemaining");
                }
                
                return string.Format(LocalizationService.Instance.GetString("LicDaysRemaining"), TrialDaysRemaining);
            }
        }

        /// <summary>
        /// Cor do status do trial
        /// </summary>
        public string TrialStatusColor
        {
            get
            {
                if (TrialDaysRemaining <= 0)
                {
                    return "#FF4B6B";
                }
                
                if (TrialDaysRemaining <= 3)
                {
                    return "#FFB84D";
                }
                
                return "#00FF88";
            }
        }

        /// <summary>
        /// Cor do status de conexão
        /// </summary>
        public string ConnectionStatusColor => IsOnline ? "#00FF88" : "#FF4B6B";

        /// <summary>
        /// Texto do status de conexão
        /// </summary>
        public string ConnectionStatusText => IsOnline ? "Online" : "Offline";

        public double TrialProgressPercent
        {
            get
            {
                if (TrialDaysRemaining <= 0)
                {
                    return 100;
                }
                
                return Math.Max(0, Math.Min(100, (DefaultTrialDays - TrialDaysRemaining) * 100 / DefaultTrialDays));
            }
            set { }
        }
        
        public bool CanActivate => 
            !IsLoading && 
            !IsActivating && 
            !string.IsNullOrWhiteSpace(LicenseKey) &&
            LicenseKey.Length >= 10;
        
        public bool IsActivateButtonVisible => true;

        // ════════════════════════════════════════════════════════════════════════════════
        // PROPRIEDADES COMPUTED - Visibilidade e Estados
        // ════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Se true, mostra card de ativação (sem licença ou alterando)
        /// </summary>
        public bool CanShowActivationForm => 
            UiState == LicenseUIState.NoLicense || 
            UiState == LicenseUIState.Changing || 
            UiState == LicenseUIState.Error;

        /// <summary>
        /// Se true, mostra informações da licença ativa
        /// </summary>
        public bool CanShowLicenseInfo => 
            UiState == LicenseUIState.Active || 
            UiState == LicenseUIState.Changing;

        /// <summary>
        /// Se true, está em modo ativação (não gerenciamento)
        /// </summary>
        public bool IsInActivationMode => 
            UiState == LicenseUIState.NoLicense || 
            UiState == LicenseUIState.Activating || 
            UiState == LicenseUIState.Changing;

        /// <summary>
        /// Se true, mostra diálogo de confirmação de desativação
        /// </summary>
        public bool ShowDeactivationDialog 
        { 
            get 
            { 
                var result = UiState == LicenseUIState.Deactivating;
                _logger?.LogInfo($"[ShowDeactivationDialog] GET chamado:");
                _logger?.LogInfo($"[ShowDeactivationDialog]   • UiState atual: {UiState}");
                _logger?.LogInfo($"[ShowDeactivationDialog]   • Comparando com: Deactivating");
                _logger?.LogInfo($"[ShowDeactivationDialog]   • Resultado: {result}");
                return result;
            }
        }

        /// <summary>
        /// Inicializa o ViewModel e carrega estado da licença
        /// </summary>
        public async Task InitializeAsync()
        {
            try
            {
                _logger?.LogInfo("╔════════════════════════════════════════════════════════════════════════════════╗");
                _logger?.LogInfo("║ [LicenseActivationViewModel] INICIANDO InitializeAsync                        ║");
                _logger?.LogInfo("╚════════════════════════════════════════════════════════════════════════════════╝");
                _logger?.LogInfo($"[InitializeAsync] Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}");
                _logger?.LogInfo($"[InitializeAsync] Thread: {Thread.CurrentThread.ManagedThreadId}");
                
                IsLoading = true;
                StatusMessage = LocalizationService.Instance.GetString("LicVerifyingStatus");
                
                // 1️⃣ RECARREGAR ESTADO DA LICENÇA
                _logger?.LogInfo("[InitializeAsync] ► PASSO 1: Recarregando estado da licença...");
                await RefreshLicenseStateAsync();
                _logger?.LogSuccess("[InitializeAsync] ✓ PASSO 1: Estado da licença recarregado");
                
                // 2️⃣ VERIFICAÇÃO DE TRIAL E CONEXÃO
                _logger?.LogInfo("[InitializeAsync] ► PASSO 2: Iniciando verificação de trial e conexão...");
                await Task.Run(async () =>
                {
                    _logger?.LogInfo("[InitializeAsync] Verificação rodando em background thread");
                    
                    try
                    {
                        var hwidService = VoltrisOptimizer.Services.License.HardwareTrialService.Instance;
                        _logger?.LogInfo($"[InitializeAsync] HardwareTrialService obtido - null? {hwidService == null}");
                        
                        if (hwidService != null && hwidService.IsInitialized)
                        {
                            _logger?.LogInfo("[InitializeAsync] ► Chamando CheckTrialStatusAsync()...");
                            var trialStatus = await hwidService.CheckTrialStatusAsync();
                            
                            if (trialStatus != null)
                            {
                                IsOnline = trialStatus.IsOnlineMode;
                                _logger?.LogInfo($"[InitializeAsync] ✓ Trial Status obtido:");
                                _logger?.LogInfo($"[InitializeAsync]   • IsActive: {trialStatus.IsActive}");
                                _logger?.LogInfo($"[InitializeAsync]   • DaysRemaining: {trialStatus.DaysRemaining}");
                                _logger?.LogInfo($"[InitializeAsync]   • IsOnlineMode: {trialStatus.IsOnlineMode}");
                                
                                Application.Current.Dispatcher.BeginInvoke(() =>
                                {
                                    var newDays = trialStatus.IsActive ? trialStatus.DaysRemaining : 0;
                                    TrialDaysRemaining = Math.Max(0, newDays);
                                    StatusMessage = "";
                                    IsLoading = false;
                                    _logger?.LogSuccess($"[InitializeAsync] ✓ UI atualizada - Trial dias: {TrialDaysRemaining}, Online: {IsOnline}");
                                });
                                return;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError($"[InitializeAsync] Erro na verificação online: {ex.Message}", ex);
                        IsOnline = false;
                    }
                    
                    // FALLBACK: Freemium - sem trial
                    _logger?.LogWarning("[InitializeAsync] ⚠️  Modo Freemium sem trial - inicializando...");
                    
                    Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        TrialDaysRemaining = 0;
                        StatusMessage = IsOnline ? "" : LocalizationService.Instance.GetString("LicOfflineWarning");
                        IsLoading = false;
                        _logger?.LogSuccess($"[InitializeAsync] ✓ Inicialização em fallback concluída");
                    });
                });
                
                _logger?.LogInfo("╔════════════════════════════════════════════════════════════════════════════════╗");
                _logger?.LogSuccess("║ [LicenseActivationViewModel] InitializeAsync CONCLUÍDO                      ║");
                _logger?.LogInfo("╚════════════════════════════════════════════════════════════════════════════════╝");
                _logger?.LogInfo($"[InitializeAsync] Estado final:");
                _logger?.LogInfo($"[InitializeAsync]   • UiState: {UiState}");
                _logger?.LogInfo($"[InitializeAsync]   • IsLicenseActive: {IsLicenseActive}");
                _logger?.LogInfo($"[InitializeAsync]   • ActiveLicenseType: {ActiveLicenseType}");
                _logger?.LogInfo($"[InitializeAsync]   • BillingPeriod: {BillingPeriod}");
                _logger?.LogInfo($"[InitializeAsync]   • TrialDaysRemaining: {TrialDaysRemaining}");
                _logger?.LogInfo($"[InitializeAsync]   • IsOnline: {IsOnline}");
            }
            catch (Exception ex)
            {
                _logger?.LogError("╔════════════════════════════════════════════════════════════════════════════════╗");
                _logger?.LogError("[InitializeAsync] ❌ ERRO ao inicializar", ex);
                _logger?.LogError($"[InitializeAsync]   Exception: {ex.GetType().Name}");
                _logger?.LogError($"[InitializeAsync]   Message: {ex.Message}");
                _logger?.LogError($"[InitializeAsync]   StackTrace: {ex.StackTrace}");
                _logger?.LogError("╚════════════════════════════════════════════════════════════════════════════════╝");
                
                StatusMessage = LocalizationService.Instance.GetString("LicVerifyingError");
                IsLoading = false;
                IsOnline = false;
                UiState = LicenseUIState.Error;
            }
        }

        /// <summary>
        /// Recarrega o estado real da licença do TokenStore e LicenseService
        /// </summary>
        private async Task RefreshLicenseStateAsync()
        {
            try
            {
                _logger?.LogInfo("╔════════════════════════════════════════════════════════════════════════════════╗");
                _logger?.LogInfo("║ [LicenseActivationViewModel] INICIANDO RefreshLicenseStateAsync                ║");
                _logger?.LogInfo("╚════════════════════════════════════════════════════════════════════════════════╝");
                _logger?.LogInfo($"[RefreshLicense] Thread ID: {Thread.CurrentThread.ManagedThreadId}");
                _logger?.LogInfo($"[RefreshLicense] Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}");
                
                // 1️⃣ Forçar atualização do estado no orquestrador
                _logger?.LogInfo("[RefreshLicense] ► Chamando LicenseOrchestrationService.RefreshStateAsync()...");
                await LicenseOrchestrationService.Instance.RefreshStateAsync();
                _logger?.LogSuccess("[RefreshLicense] ✓ RefreshStateAsync() concluído");
                
                // 2️⃣ Obter estado atual
                _logger?.LogInfo("[RefreshLicense] ► Obtendo estado atual do LicenseOrchestrator...");
                var licenseState = await LicenseOrchestrationService.Instance.GetCurrentStateAsync();
                
                _logger?.LogInfo($"[RefreshLicense] Estado obtido:");
                _logger?.LogInfo($"  • IsActive: {licenseState.IsActive}");
                _logger?.LogInfo($"  • LicenseType: {licenseState.LicenseType}");
                _logger?.LogInfo($"  • LicenseKey: {(string.IsNullOrEmpty(licenseState.LicenseKey) ? "(vazio)" : licenseState.LicenseKey.Substring(0, Math.Min(20, licenseState.LicenseKey.Length)) + "...")}");
                _logger?.LogInfo($"  • ExpiresAt: {licenseState.ExpiresAt:yyyy-MM-dd HH:mm:ss}");
                _logger?.LogInfo($"  • LastValidated: {licenseState.LastValidated:yyyy-MM-dd HH:mm:ss}");
                _logger?.LogInfo($"  • MaxDevices: {licenseState.MaxDevices}");
                _logger?.LogInfo($"  • DevicesInUse: {licenseState.DevicesInUse}");
                _logger?.LogInfo($"  • BillingPeriod: {licenseState.BillingPeriod}");
                
                if (licenseState.IsActive)
                {
                    _logger?.LogInfo("[RefreshLicense] ✅ ESTADO: LICENÇA ATIVA DETECTADA");
                    
                    // Licença ativa
                    IsLicenseActive = true;
                    ActiveLicenseType = licenseState.LicenseType ?? "";
                    ActiveLicenseKey = licenseState.LicenseKey ?? "";
                    LicenseActivationDate = licenseState.LastValidated;
                    LicenseExpirationDate = licenseState.ExpiresAt;
                    MaxDevices = licenseState.MaxDevices;
                    DevicesInUse = licenseState.DevicesInUse;
                    BillingPeriod = licenseState.BillingPeriod ?? "";
                    
                    _logger?.LogInfo($"[RefreshLicense] ► Atualizando propriedades do ViewModel...");
                    _logger?.LogInfo($"  • IsLicenseActive: {IsLicenseActive}");
                    _logger?.LogInfo($"  • ActiveLicenseType: {ActiveLicenseType}");
                    _logger?.LogInfo($"  • BillingPeriod: {BillingPeriod}");
                    
                    UiState = LicenseUIState.Active;
                    _logger?.LogSuccess($"[RefreshLicense] ✓ Estado alterado para ACTIVE");
                    
                    // Resetar campo de entrada
                    LicenseKey = "";
                    ErrorMessage = "";
                    ShowError = false;
                    _logger?.LogInfo($"[RefreshLicense] ✓ Campo de entrada resetado");
                }
                else
                {
                    _logger?.LogWarning("[RefreshLicense] ⚠️  ESTADO: SEM LICENÇA");
                    
                    // Sem licença
                    IsLicenseActive = false;
                    ActiveLicenseType = "";
                    ActiveLicenseKey = "";
                    LicenseActivationDate = null;
                    LicenseExpirationDate = null;
                    MaxDevices = 0;
                    DevicesInUse = 0;
                    BillingPeriod = "";
                    
                    _logger?.LogInfo($"[RefreshLicense] ► Limpando propriedades da licença...");
                    _logger?.LogInfo($"  • IsLicenseActive: {IsLicenseActive}");
                    _logger?.LogInfo($"  • UiState ATUAL: {UiState}");
                    
                    if (UiState != LicenseUIState.Changing && UiState != LicenseUIState.Activating)
                    {
                        UiState = LicenseUIState.NoLicense;
                        _logger?.LogSuccess($"[RefreshLicense] ✓ Estado alterado para NO_LICENSE");
                    }
                    else
                    {
                        _logger?.LogWarning($"[RefreshLicense] ⚠️  Mantendo estado {UiState} (em transição)");
                    }
                }
                
                LicenseStateChanged?.Invoke(this, EventArgs.Empty);
                _logger?.LogSuccess("[RefreshLicense] ✓ Evento LicenseStateChanged disparado");
                
                // ✅ Verificação final
                _logger?.LogInfo($"[RefreshLicense] VERIFICAÇÃO FINAL:");
                _logger?.LogInfo($"  • UiState: {UiState}");
                _logger?.LogInfo($"  • IsLicenseActive: {IsLicenseActive}");
                _logger?.LogInfo($"  • ActiveLicenseType: {ActiveLicenseType}");
                _logger?.LogInfo($"  • CanShowLicenseInfo: {CanShowLicenseInfo}");
                _logger?.LogInfo($"  • CanShowActivationForm: {CanShowActivationForm}");
                
                _logger?.LogInfo("╔════════════════════════════════════════════════════════════════════════════════╗");
                _logger?.LogSuccess("║ [LicenseActivationViewModel] RefreshLicenseStateAsync CONCLUÍDO COM SUCESSO    ║");
                _logger?.LogInfo("╚════════════════════════════════════════════════════════════════════════════════╝");
            }
            catch (Exception ex)
            {
                _logger?.LogError("╔════════════════════════════════════════════════════════════════════════════════╗");
                _logger?.LogError("[LicenseActivationViewModel] ERRO em RefreshLicenseStateAsync", ex);
                _logger?.LogError($"  Exception Type: {ex.GetType().Name}");
                _logger?.LogError($"  Exception Message: {ex.Message}");
                _logger?.LogError($"  StackTrace: {ex.StackTrace}");
                
                if (ex.InnerException != null)
                {
                    _logger?.LogError($"  InnerException: {ex.InnerException.Message}");
                }
                
                _logger?.LogError("╚════════════════════════════════════════════════════════════════════════════════╝");
                
                UiState = LicenseUIState.Error;
                ErrorMessage = "Erro ao carregar estado da licença";
                ShowError = true;
            }
        }
        
        /// <summary>
        /// Copia Device ID para área de transferência
        /// </summary>
        private void CopyDeviceId()
        {
            try
            {
                Clipboard.SetText(DeviceId);
                HwidStatusMessage = LocalizationService.Instance["LicCopied"];
                OnPropertyChanged(nameof(HwidStatusMessage));
                _ = Task.Delay(2000).ContinueWith(_ => 
                {
                    HwidStatusMessage = "";
                    OnPropertyChanged(nameof(HwidStatusMessage));
                });
                _logger?.LogInfo($"[CopyDeviceId] Device ID copiado para clipboard");
            }
            catch (Exception ex)
            {
                _logger?.LogError("[CopyDeviceId] Erro ao copiar Device ID", ex);
            }
        }

        /// <summary>
        /// Copia Chave da Licença para área de transferência
        /// </summary>
        private void CopyLicenseKey()
        {
            try
            {
                if (string.IsNullOrEmpty(ActiveLicenseKey))
                {
                    _logger?.LogWarning("[CopyLicenseKey] Chave da licença vazia");
                    return;
                }

                Clipboard.SetText(ActiveLicenseKey);
                LicenseStatusMessage = LocalizationService.Instance["LicCopied"];
                OnPropertyChanged(nameof(LicenseStatusMessage));
                _ = Task.Delay(2000).ContinueWith(_ => 
                {
                    LicenseStatusMessage = "";
                    OnPropertyChanged(nameof(LicenseStatusMessage));
                });
                _logger?.LogInfo($"[CopyLicenseKey] Chave de licença copiada para clipboard");
            }
            catch (Exception ex)
            {
                _logger?.LogError("[CopyLicenseKey] Erro ao copiar chave da licença", ex);
            }
        }
        
        /// <summary>
        /// Ativa uma licença
        /// </summary>
        public async Task ActivateLicenseAsync()
        {
            _logger?.LogInfo("[LicenseActivationViewModel] ===== INÍCIO DO ActivateLicenseAsync ====");
            _logger?.LogInfo($"[LicenseActivationViewModel] Thread ID: {Thread.CurrentThread.ManagedThreadId}, IsThreadPool: {Thread.CurrentThread.IsThreadPoolThread}, IsBackground: {Thread.CurrentThread.IsBackground}");
            _logger?.LogInfo($"[LicenseActivationViewModel] CanActivate: {CanActivate}, IsActivating: {IsActivating}");
            
            if (!CanActivate) 
            {
                _logger?.LogWarning("[LicenseActivationViewModel] Cannot activate - returning");
                return;
            }
            
            IsActivating = true;
            ShowError = false;
            ShowSuccess = false;
            StatusMessage = LocalizationService.Instance.GetString("LicValidatingKey");
            _logger?.LogInfo("[LicenseActivationViewModel] UI atualizada para modo ativação");
            
            var progressToken = VoltrisOptimizer.Services.GlobalProgressService.Instance.BeginOperation(LocalizationService.Instance.GetString("ActivatingLicense"), isPriority: true);

            try
            {
                // IMPORTANTE: NÃO usar ToUpperInvariant() na chave inteira.
                // A assinatura (parte SIG) é base64url e case-sensitive — maiúsculas
                // a corrompem. Só PLANO/ID/DATA são case-insensitive (tratados na verificação).
                var licenseKey = LicenseKey.Trim();
                
                _logger?.LogInfo($"[LicenseActivation] Estado UI: Loading={IsLoading}, Error={ShowError}, Success={ShowSuccess}");
                
                progressToken.UpdateProgress(30, StatusMessage);

                // 🔧 CORREÇÃO: Usar LicenseManager para ativação profissional
                var result = await LicenseManager.Instance.ActivateLicenseAsync(licenseKey);
                
                _logger?.LogInfo($"[LicenseActivation] Resultado da ativação: Success={result.Success}, Plan={result.Plan}, Message={result.Message}");
                
                if (result.Success)
                {
                    _logger?.LogInfo($"[LicenseActivation] ✅ Ativação bem-sucedida! Plano: {result.Plan}");
                    
                    ShowSuccess = true;
                    ShowError = false;
                    IsActivating = false;  // CRÍTICO: Desbloquear UI imediatamente
                    
                    // 🔥 CORREÇÃO: Usar nome real do backend
                    var displayName = LicenseTokenStore.LicenseDisplayName ?? result.Plan;
                    StatusMessage = string.Format(LocalizationService.Instance.GetString("LicActivationSuccess"), displayName.ToUpper());
                    
                    _logger?.LogInfo($"[LicenseActivation] Mensagem definida: {StatusMessage}");
                    _logger?.LogInfo($"[LicenseActivation] DisplayName usado: '{displayName}'");
                    
                    progressToken.UpdateProgress(50, "Salvando dados da licença...");
                    
                    // ⏸ Aguardar persistência completa
                    _logger?.LogInfo($"[LicenseActivation] Aguardando persistência completa em Registry/JSON...");
                    await Task.Delay(1500);
                    
                    // 🔄 Forçar refresh do LicenseOrchestrator
                    _logger?.LogInfo($"[LicenseActivation] Atualizando cache do LicenseOrchestrator...");
                    await LicenseOrchestrationService.Instance.RefreshStateAsync();
                    
                    // 📊 Recarregar estado REAL da licença no ViewModel
                    _logger?.LogInfo($"[LicenseActivation] Sincronizando estado da licença com UI...");
                    await RefreshLicenseStateAsync();
                    
                    // ✅ Verificar que o estado foi realmente atualizado
                    if (IsLicenseActive)
                    {
                        _logger?.LogInfo($"[LicenseActivation] ✓ Estado confirmado - Licença: {ActiveLicenseType}, Chave: {ActiveLicenseKey.Substring(0, Math.Min(20, ActiveLicenseKey.Length))}...");
                    }
                    else
                    {
                        _logger?.LogWarning($"[LicenseActivation] ⚠ AVISO: RefreshLicenseStateAsync não atualizou o estado!");
                    }
                    
                    progressToken.UpdateProgress(80, "Finalizando...");
                    
                    // 📢 Atualizar Dashboard
                    _logger?.LogInfo($"[LicenseActivation] Atualizando Dashboard...");
                    try
                    {
                        var dashboardVM = ViewModelLocator.Instance?.DashboardVM;
                        if (dashboardVM != null)
                        {
                            _logger?.LogInfo($"[LicenseActivation] Dashboard encontrado - enviando atualização...");
                            await dashboardVM.UpdateLicenseStatusAsync("LicenseActivated");
                            _logger?.LogInfo($"[LicenseActivation] Dashboard atualizado com sucesso");
                        }
                        else
                        {
                            _logger?.LogWarning($"[LicenseActivation] Dashboard não encontrado");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError($"[LicenseActivation] Erro ao atualizar Dashboard", ex);
                    }
                    
                    progressToken.Complete("✅ Licença ativada com sucesso");
                    GlobalNotificationService.ShowSuccess(LocalizationService.Instance.GetString("LicenseTitle"), StatusMessage);

                    _logger?.LogInfo($"[LicenseActivation] 🎉 Ativação completa - Disparando evento ActivationSucceeded...");
                    ActivationSucceeded?.Invoke(this, EventArgs.Empty);

                    // CORREÇÃO CRÍTICA: Disparar ativação automática do Modo Gamer diretamente (bypass event race)
                    _logger?.LogInfo("[LicenseActivation] Disparando verificação de ativação automática do Modo Gamer...");
                    try
                    {
                        var gamerVM = ViewModelLocator.Instance?.GamerVM;
                        if (gamerVM != null)
                        {
                            var method = gamerVM.GetType().GetMethod("CheckAndActivateGamerModeOnLicenseChange", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            if (method != null)
                            {
                                _logger?.LogInfo("[LicenseActivation] Chamando CheckAndActivateGamerModeOnLicenseChange via reflection");
                                var task = method.Invoke(gamerVM, null) as Task;
                                if (task != null)
                                {
                                    _ = task.ContinueWith(t => 
                                    {
                                        if (t.IsFaulted)
                                            _logger?.LogError($"[LicenseActivation] Erro no CheckAndActivateGamerModeOnLicenseChange: {t.Exception?.GetBaseException().Message}");
                                        else
                                            _logger?.LogInfo("[LicenseActivation] CheckAndActivateGamerModeOnLicenseChange concluído");
                                    });
                            }
                            else
                            {
                                _logger?.LogWarning("[LicenseActivation] CheckAndActivateGamerModeOnLicenseChange retornou null");
                            }
                        }
                        else
                        {
                            _logger?.LogWarning("[LicenseActivation] Método CheckAndActivateGamerModeOnLicenseChange não encontrado no GamerViewModel");
                        }
                    }
                    else
                    {
                        _logger?.LogWarning("[LicenseActivation] GamerViewModel não encontrado no ViewModelLocator");
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError($"[LicenseActivation] Erro ao disparar ativação automática do Modo Gamer: {ex.Message}");
                }
            }
            else
                {
                    _logger?.LogWarning($"[LicenseActivation] Falha na ativação: {result.Message}");
                    ErrorMessage = result.Message;
                    IsActivating = false;
                    progressToken.Fail(result.Message);
                    GlobalNotificationService.ShowError(LocalizationService.Instance.GetString("LicenseTitle"), result.Message);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[LicenseActivation] Erro ao ativar licença", ex);
                ErrorMessage = string.Format(LocalizationService.Instance.GetString("LicTechError"), ex.Message);
                IsActivating = false; // GARANTIR QUE DESTRAVA O BOTÃO
                progressToken.Fail(ex.Message);
                GlobalNotificationService.ShowError(LocalizationService.Instance.GetString("LicenseTitle"), ErrorMessage);
            }
            finally
            {
                IsLoading = false;
                // Se não teve sucesso nem erro explícito (ex: timeout silencioso), garantir reset
                if (!ShowSuccess && !ShowError) IsActivating = false;
                progressToken.Dispose();
                
                _logger?.LogInfo($"[LicenseActivation] Estado final: Loading={IsLoading}, Error={ShowError}, Success={ShowSuccess}, Activating={IsActivating}");
            }
        }
        
        /// <summary>
        /// Sincroniza licenças com o servidor
        /// </summary>
        private async Task SyncWithServerAsync()
        {
            if (IsLoading) return;
            
            IsLoading = true;
            StatusMessage = LocalizationService.Instance.GetString("LicSyncingServer");
            ErrorMessage = "";
            
            var progressToken = VoltrisOptimizer.Services.GlobalProgressService.Instance.BeginOperation(LocalizationService.Instance.GetString("SyncingLicense"), isPriority: true);

            try
            {
                progressToken.UpdateProgress(30, StatusMessage);
                var result = await LicenseManager.Instance.SyncWithServerAsync();
                if (result.success)
                {
                    StatusMessage = LocalizationService.Instance.GetString("LicSyncSuccess");
                    progressToken.Complete("✅ " + LocalizationService.Instance.GetString("SyncComplete"));
                    GlobalNotificationService.ShowSuccess(LocalizationService.Instance.GetString("LicenseTitle"), StatusMessage);
                    await RefreshLicenseStateAsync();
                }
                else
                {
                    ErrorMessage = result.message;
                    progressToken.Fail(result.message);
                    GlobalNotificationService.ShowError(LocalizationService.Instance.GetString("LicenseTitle"), result.message);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError("[License] Erro ao sincronizar", ex);
                ErrorMessage = LocalizationService.Instance.GetString("LicSyncError");
                progressToken.Fail(ex.Message);
                GlobalNotificationService.ShowError(LocalizationService.Instance.GetString("LicenseTitle"), ErrorMessage);
            }
            finally
            {
                IsLoading = false;
                progressToken.Dispose();
                if (string.IsNullOrEmpty(ErrorMessage))
                    _ = Task.Delay(3000).ContinueWith(_ => StatusMessage = "");
            }
        }

        // ════════════════════════════════════════════════════════════════════════════════
        // NOVOS MÉTODOS - Gerenciamento de Licença
        // ════════════════════════════════════════════════════════════════════════════════



        /// <summary>
        /// Entra no modo de alteração de licença
        /// </summary>
        private void EnterChangeLicenseMode()
        {
            _logger?.LogInfo("[LicenseActivationViewModel] Entrando em modo alteração de licença...");
            UiState = LicenseUIState.Changing;
            LicenseKey = "";  // Limpar campo para nova entrada
            ErrorMessage = "";
            ShowError = false;
            StatusMessage = "Insira uma nova chave de licença para ativar";
            _logger?.LogInfo("[LicenseActivationViewModel] Modo Changing ativado - campo limpo e pronto para input");
        }

        /// <summary>
        /// Solicita confirmação para desativar licença
        /// </summary>
        private void PromptDeactivateLicense()
        {
            _logger?.LogInfo("╔════════════════════════════════════════════════════════════════════════════════╗");
            _logger?.LogInfo("║ [PROMPT_DEACTIVATE] INICIADO - Solicitando confirmação de desativação          ║");
            _logger?.LogInfo("╚════════════════════════════════════════════════════════════════════════════════╝");
            
            _logger?.LogInfo($"[PROMPT_DEACTIVATE] Estado ANTES:");
            _logger?.LogInfo($"  • UiState atual: {_uiState}");
            _logger?.LogInfo($"  • IsLicenseActive: {IsLicenseActive}");
            _logger?.LogInfo($"  • ActiveLicenseType: {ActiveLicenseType}");
            
            _logger?.LogInfo($"[PROMPT_DEACTIVATE] ► Tentando mudar UiState para Deactivating...");
            UiState = LicenseUIState.Deactivating;
            
            _logger?.LogInfo($"[PROMPT_DEACTIVATE] Estado DEPOIS:");
            _logger?.LogInfo($"  • UiState agora: {_uiState}");
            _logger?.LogInfo($"  • ShowDeactivationDialog: {ShowDeactivationDialog}");
            _logger?.LogInfo($"  • CanShowLicenseInfo: {CanShowLicenseInfo}");
            _logger?.LogInfo($"  • CanShowActivationForm: {CanShowActivationForm}");
            
            _logger?.LogInfo("╔════════════════════════════════════════════════════════════════════════════════╗");
            _logger?.LogSuccess("║ [PROMPT_DEACTIVATE] CONCLUÍDO - Aguardando confirmação do usuário          ║");
            _logger?.LogInfo("╚════════════════════════════════════════════════════════════════════════════════╝");
        }

        /// <summary>
        /// Cancela o processo de desativação
        /// </summary>
        private void CancelDeactivation()
        {
            _logger?.LogInfo("[LicenseActivationViewModel] Cancelando desativação...");
            UiState = LicenseUIState.Active;
            ErrorMessage = "";
            ShowError = false;
        }

        /// <summary>
        /// Confirma e executa desativação da licença
        /// </summary>
        private async Task ConfirmDeactivationAsync()
        {
            _logger?.LogInfo("╔════════════════════════════════════════════════════════════════════════════════╗");
            _logger?.LogInfo("║ [LicenseActivationViewModel] ✓ CONFIRMANDO DESATIVAÇÃO DE LICENÇA             ║");
            _logger?.LogInfo("╚════════════════════════════════════════════════════════════════════════════════╝");
            _logger?.LogInfo($"[Deactivation] Thread ID: {Thread.CurrentThread.ManagedThreadId}");
            _logger?.LogInfo($"[Deactivation] Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}");
            
            IsLoading = true;
            StatusMessage = "Desativando licença...";
            
            var progressToken = GlobalProgressService.Instance.BeginOperation("Desativando licença", isPriority: true);

            try
            {
                _logger?.LogInfo("[Deactivation] ESTADO ANTES:");
                _logger?.LogInfo($"  • IsLicenseActive: {IsLicenseActive}");
                _logger?.LogInfo($"  • ActiveLicenseType: {ActiveLicenseType}");
                _logger?.LogInfo($"  • UiState: {UiState}");
                
                progressToken.UpdateProgress(20, "Iniciando desativação...");
                
                // 🔴 PASSO 1: Executar desativação real via LicenseManager
                _logger?.LogInfo("[Deactivation] ► PASSO 1: Chamando LicenseManager.ResetTrial()...");
                LicenseManager.Instance.RevokeLicense();
                _logger?.LogSuccess("[Deactivation] ✓ PASSO 1: LicenseManager.RevokeLicense() concluído (Freemium)");
                
                progressToken.UpdateProgress(35, "Aguardando persistência...");
                
                // ⏸ PASSO 2: Aguardar persistência completa
                _logger?.LogInfo("[Deactivation] ► PASSO 2: Aguardando 1500ms para persistência em Registry/JSON...");
                await Task.Delay(1500);
                _logger?.LogSuccess("[Deactivation] ✓ PASSO 2: Persistência completa");
                
                progressToken.UpdateProgress(50, "Sincronizando estado com servidor...");
                
                // 🔄 PASSO 3: Forçar refresh no orquestrador
                _logger?.LogInfo("[Deactivation] ► PASSO 3: Chamando LicenseOrchestrationService.RefreshStateAsync()...");
                await LicenseOrchestrationService.Instance.RefreshStateAsync();
                _logger?.LogSuccess("[Deactivation] ✓ PASSO 3: Cache do orquestrador invalidado");
                
                progressToken.UpdateProgress(70, "Recarregando estado da UI...");
                
                // 📊 PASSO 4: Recarregar estado COMPLETO da licença
                _logger?.LogInfo("[Deactivation] ► PASSO 4: Chamando RefreshLicenseStateAsync()...");
                await RefreshLicenseStateAsync();
                _logger?.LogSuccess("[Deactivation] ✓ PASSO 4: Estado da UI recarregado");
                
                // ✅ VERIFICAÇÃO CRÍTICA: Garantir que desativação foi bem-sucedida
                _logger?.LogInfo("[Deactivation] ✅ VERIFICAÇÃO PÓS-DESATIVAÇÃO:");
                _logger?.LogInfo($"  • IsLicenseActive: {IsLicenseActive}");
                _logger?.LogInfo($"  • ActiveLicenseType: {ActiveLicenseType}");
                _logger?.LogInfo($"  • UiState: {UiState}");
                _logger?.LogInfo($"  • CanShowActivationForm: {CanShowActivationForm}");
                _logger?.LogInfo($"  • CanShowLicenseInfo: {CanShowLicenseInfo}");
                
                if (!IsLicenseActive && UiState == LicenseUIState.NoLicense)
                {
                    _logger?.LogSuccess("[Deactivation] ✓✓✓ DESATIVAÇÃO CONFIRMADA COM SUCESSO!");
                    _logger?.LogSuccess("[Deactivation] UI está em estado correto (NoLicense) e pronta para nova ativação");
                }
                else if (IsLicenseActive)
                {
                    _logger?.LogError("[Deactivation] ❌❌ CRÍTICO: Licença ainda ativa após desativação!");
                    _logger?.LogError($"[Deactivation] IsLicenseActive={IsLicenseActive}, UiState={UiState}");
                    _logger?.LogError("[Deactivation] Tentando desativação adicional...");
                    
                    // Tentar uma segunda vez se ainda estiver ativa (Freemium)
                    LicenseManager.Instance.RevokeLicense();
                    await Task.Delay(2000);
                    await LicenseOrchestrationService.Instance.RefreshStateAsync();
                    await RefreshLicenseStateAsync();
                    
                    _logger?.LogWarning($"[Deactivation] Segundo ciclo - IsLicenseActive: {IsLicenseActive}, UiState: {UiState}");
                }
                else if (UiState != LicenseUIState.NoLicense)
                {
                    _logger?.LogWarning($"[Deactivation] ⚠️  Licença desativada mas UiState incorreto: {UiState}");
                    _logger?.LogWarning("[Deactivation] Forçando UiState para NoLicense");
                    UiState = LicenseUIState.NoLicense;
                }
                
                progressToken.UpdateProgress(85, "Notificando Dashboard...");
                
                // 📢 PASSO 5: Notificar Dashboard
                _logger?.LogInfo("[Deactivation] ► PASSO 5: Notificando Dashboard sobre desativação...");
                try
                {
                    var dashboardVM = ViewModelLocator.Instance?.DashboardVM;
                    if (dashboardVM != null)
                    {
                        _logger?.LogInfo("[Deactivation] Dashboard encontrado - enviando atualização...");
                        await dashboardVM.UpdateLicenseStatusAsync("LicenseDeactivated");
                        _logger?.LogSuccess("[Deactivation] ✓ Dashboard atualizado com sucesso");
                    }
                    else
                    {
                        _logger?.LogWarning("[Deactivation] Dashboard não encontrado (pode estar em minimized/tray)");
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError("[Deactivation] Erro ao atualizar Dashboard (não crítico)", ex);
                }
                
                progressToken.UpdateProgress(95, "Finalizando...");
                
                // 📢 PASSO 6: Mensagens de feedback
                _logger?.LogInfo("[Deactivation] ► PASSO 6: Exibindo mensagens de feedback...");
                StatusMessage = "✓ Licença desativada com sucesso!";
                ShowSuccess = true;
                ShowError = false;
                ErrorMessage = "";
                
                progressToken.Complete("✅ Licença desativada com sucesso");
                _logger?.LogSuccess("[Deactivation] 🎉 Notificação exibida ao usuário");
                
                GlobalNotificationService.ShowSuccess(
                    LocalizationService.Instance.GetString("LicenseTitle"), 
                    "Licença desativada com sucesso. A página será recarregada."
                );
                
                // Limpar mensagem após 3 segundos
                _ = Task.Delay(3000).ContinueWith(_ => 
                {
                    StatusMessage = "";
                    ShowSuccess = false;
                });

                // 🔔 GARANTIA FINAL: Notificar todos os controles PRO que a licença foi revogada
                LicenseManager.Instance.NotifyLicenseStatusChanged();
                
                _logger?.LogInfo("╔════════════════════════════════════════════════════════════════════════════════╗");
                _logger?.LogSuccess("║ [LicenseActivationViewModel] DESATIVAÇÃO CONCLUÍDA COM SUCESSO             ║");
                _logger?.LogInfo("╚════════════════════════════════════════════════════════════════════════════════╝");
            }
            catch (Exception ex)
            {
                _logger?.LogError("╔════════════════════════════════════════════════════════════════════════════════╗");
                _logger?.LogError("[Deactivation] ❌ ERRO AO DESATIVAR LICENÇA", ex);
                _logger?.LogError($"  Exception Type: {ex.GetType().Name}");
                _logger?.LogError($"  Message: {ex.Message}");
                _logger?.LogError($"  StackTrace: {ex.StackTrace}");
                _logger?.LogError("╚════════════════════════════════════════════════════════════════════════════════╝");
                
                ErrorMessage = $"Erro ao desativar: {ex.Message}";
                ShowError = true;
                UiState = LicenseUIState.Error;
                progressToken.Fail(ex.Message);
                
                GlobalNotificationService.ShowError(
                    LocalizationService.Instance.GetString("LicenseTitle"), 
                    ErrorMessage
                );
            }
            finally
            {
                IsLoading = false;
                progressToken.Dispose();
                
                _logger?.LogInfo("[Deactivation] Estado final:");
                _logger?.LogInfo($"  • IsLoading: {IsLoading}");
                _logger?.LogInfo($"  • IsLicenseActive: {IsLicenseActive}");
                _logger?.LogInfo($"  • UiState: {UiState}");
            }
        }
    }
}


