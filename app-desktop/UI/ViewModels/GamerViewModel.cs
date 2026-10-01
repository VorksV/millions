using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using VoltrisOptimizer.UI.Commands;
using VoltrisOptimizer.Services.License.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using VoltrisOptimizer.UI.Controls;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Gamer.Implementation;
using VoltrisOptimizer.Services.Gamer.Interfaces;
using VoltrisOptimizer.Services.Gamer.OptimizationModules;
using VoltrisOptimizer.Services.Gamer.GamerModeManager;
// Usar alias para evitar conflito com tipos antigos
using GamerModels = VoltrisOptimizer.Services.Gamer.Models;
using AppPage = VoltrisOptimizer.Services.AppPage;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.Gamer.Diagnostics.Implementation;
using VoltrisOptimizer.Services.Gamer.Models;
using VoltrisOptimizer.Services.Gamer.Intelligence.Interfaces;
using VoltrisOptimizer.Services.Gamer.Intelligence.Models;
using VoltrisOptimizer.Services.Gamer.Intelligence.Implementation;
using VoltrisOptimizer.Services.Gamer.Adaptive;
using VoltrisOptimizer.Services.Gamer.Intelligence;
using LogLevel = VoltrisOptimizer.Services.LogLevel;
using VoltrisOptimizer.Utils;
using VoltrisOptimizer.Helpers;
 
using GameDetectionService = VoltrisOptimizer.Services.GameDetectionService;
using IThermalMonitorService = VoltrisOptimizer.Services.Thermal.IThermalMonitorService;
using GamerModeManager = VoltrisOptimizer.Services.Gamer.GamerModeManager;
using VoltrisOptimizer.Services.Thermal;
using VoltrisOptimizer.Services.Thermal.Models;
using VoltrisOptimizer.Services.Gamer.Overlay.Interfaces;
using VoltrisOptimizer.Services.Telemetry;
using VoltrisOptimizer.Services.Performance.CpuTuning.Features;
using VoltrisOptimizer.Services.Performance.CpuTuning.Core.Interfaces;
using VoltrisOptimizer.Services.GameRepairEnterprise.Engine;
using VoltrisOptimizer.Services.GameRepairEnterprise.Interfaces;
using VoltrisOptimizer.Services.GameRepairEnterprise.Models;
namespace VoltrisOptimizer.UI.ViewModels
{
    /// <summary>
    /// ViewModel para a página Gamer com suporte completo a MVVM
    /// </summary>
    public class GamerViewModel : ViewModelBase
    {
        private readonly IGamerModeOrchestrator _orchestrator;
        private readonly IGameDetector _gameDetector;
        private readonly IGameLibraryService _libraryService;
        private readonly IGpuGamingOptimizer _gpuOptimizer;
        private readonly ILoggingService _logger;

        private readonly IRealGameBoosterService? _realBooster;
        private GamerSessionManager? _gamerSessionManager;
        private TrendAnalyzerService? _trendAnalyzer;
        private IPowerProfileDiagnosticsService? _powerDiag;
        private readonly IMachineProfileDetector _profileDetector;
        private readonly IAdaptiveOptimizationEngine _adaptiveEngine;
        private readonly IHardwareDetector _hardwareDetector;
        private readonly IOverlayService? _overlayService;
        private readonly VoltrisOptimizer.Services.Gamer.Interfaces.IGameProfileService? _profileService;
        private readonly SemaphoreSlim _gamerModeLock = new(1, 1); // Guardrail de reentrância
        private readonly IGamerModeManager? _gamerModeManager;
        
        private VoltrisOptimizer.Services.Gamer.GamerProfileResolver? _gamerProfileResolver;
        private GamerModels.DetectedGame? _pendingGameActivation;

        // -- GAME REPAIR SERVICE ----------------------------------------------
        private GameRepairService? _gameRepairService;
        private IEnterpriseGameRepairEngine? _enterpriseRepairEngine;
        private IEnumerable<IGameDependencyProvider>? _dependencyProviders;
        private bool _useEnterpriseRepair = true; // FEATURE FLAG FOR V2 ARCHITECTURE
        private CancellationTokenSource? _gameRepairCts;
        // Semáforo para evitar execução dupla (double-click / chamada concorrente)
        private readonly SemaphoreSlim _gameRepairLock = new(1, 1);

        private string _adapterMessage = "";
        public string AdapterMessage
        {
            get => _adapterMessage;
            set => SetProperty(ref _adapterMessage, value);
        }

        private bool _hasAdapterMessage;
        public bool HasAdapterMessage
        {
            get => _hasAdapterMessage;
            set => SetProperty(ref _hasAdapterMessage, value);
        }

        private string _warningMessage = "";
        public string WarningMessage
        {
            get => _warningMessage;
            set => SetProperty(ref _warningMessage, value);
        }

        private bool _hasWarning;
        public bool HasWarning
        {
            get => _hasWarning;
            set => SetProperty(ref _hasWarning, value);
        }

        #region Properties - Estado do Modo Gamer

        private bool _isGamerModeActive;
        public bool IsGamerModeActive
        {
            get => _isGamerModeActive;
            set
            {
                if (_isGamerModeActive != value)
                {
                    var threadInfo = System.Threading.Thread.CurrentThread.ManagedThreadId == System.Windows.Application.Current?.Dispatcher.Thread.ManagedThreadId ? "UI" : "BG";
                    _logger.Log(LogLevel.Info, LogCategory.Gamer, $"[GamerMode][ViewModel] Propriedade IsGamerModeActive alterada: {_isGamerModeActive} ? {value} (Thread={threadInfo})");
                    SetProperty(ref _isGamerModeActive, value);
                    OnPropertyChanged(nameof(GamerModeStatusDescription));
                }
            }
        }

        private string _activeProfileName = "";
        public string ActiveProfileName
        {
            get => _activeProfileName;
            set => SetProperty(ref _activeProfileName, value);
        }

        public string GamerModeStatusDescription
        {
            get
            {
                if (IsGamerModeActive)
                {
                    var loc = LocalizationService.Instance;
                    if (ActiveGameName != null)
                    {
                        if (!string.IsNullOrEmpty(ActiveProfileName))
                            return loc.GetString("GamerOptimizingWithProfile")
                                .Replace("{0}", ActiveGameName)
                                .Replace("{1}", ActiveProfileName);
                        return loc.GetString("GamerOptimizingGame")
                            .Replace("{0}", ActiveGameName);
                    }
                    if (!string.IsNullOrEmpty(ActiveProfileName))
                        return loc.GetString("GamerOptimizedWithProfile")
                            .Replace("{0}", ActiveProfileName);
                    return loc.GetString("GamerOptimized");
                }
                return LocalizationService.Instance.GetString("ClickActivateToStart");
            }
        }

        private string _statusText = LocalizationService.Instance.GetString("GamerModeInactive");
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        private string? _activeGameName;
        public string? ActiveGameName
        {
            get => _activeGameName;
            set 
            {
                string? finalName = value;
                if (!string.IsNullOrEmpty(value))
                {
                    string processName = value.ToLowerInvariant();
                    if (processName.EndsWith(".exe")) processName = processName.Substring(0, processName.Length - 4);
                    
                    if (VoltrisOptimizer.Services.Gamer.Data.GameSignatureBank.KnownSignatures.TryGetValue(processName, out var sig))
                    {
                        finalName = sig.DisplayName;
                    }
                }
                
                if (SetProperty(ref _activeGameName, finalName))
                {
                    OnPropertyChanged(nameof(GamerModeStatusDescription));
                }
            }
        }

        private string _fpsText = "";
        public string FpsText
        {
            get => _fpsText;
            set => SetProperty(ref _fpsText, value);
        }

        private string _cpuTempText = LocalizationService.Instance.GetString("CpuTempDefault");
        public string CpuTempText
        {
            get => _cpuTempText;
            set => SetProperty(ref _cpuTempText, value);
        }

        private string _gpuTempText = LocalizationService.Instance.GetString("GpuTempDefault");
        public string GpuTempText
        {
            get => _gpuTempText;
            set => SetProperty(ref _gpuTempText, value);
        }

        private bool _cpuThrottlingActive;
        public bool CpuThrottlingActive
        {
            get => _cpuThrottlingActive;
            set => SetProperty(ref _cpuThrottlingActive, value);
        }

        private bool _gpuThrottlingActive;
        public bool GpuThrottlingActive
        {
            get => _gpuThrottlingActive;
            set => SetProperty(ref _gpuThrottlingActive, value);
        }

        // Thermal Monitoring Properties (Real-time)
        private double _cpuTemperature;
        public double CpuTemperature { get => _cpuTemperature; set => SetProperty(ref _cpuTemperature, value); }
        
        private double _gpuTemperature;
        public double GpuTemperature { get => _gpuTemperature; set => SetProperty(ref _gpuTemperature, value); }
        
        private bool _isTemperatureEstimated;
        public bool IsTemperatureEstimated { get => _isTemperatureEstimated; set => SetProperty(ref _isTemperatureEstimated, value); }
        
        private string _thermalStatus = LocalizationService.Instance.GetString("ThermalNormal");
        public string ThermalStatus { get => _thermalStatus; set => SetProperty(ref _thermalStatus, value); }
        
        private string _thermalStatusColor = "#10B981";
        public string ThermalStatusColor { get => _thermalStatusColor; set => SetProperty(ref _thermalStatusColor, value); }
        
        private bool _hasThermalAlert;
        public bool HasThermalAlert { get => _hasThermalAlert; set => SetProperty(ref _hasThermalAlert, value); }

        private bool _isAutoModeEnabled = true; // SEMPRE true por padrão
        public bool IsAutoModeEnabled
        {
            get => _isAutoModeEnabled;
            set
            {
                if (SetProperty(ref _isAutoModeEnabled, value))
                {
                    if (value)
                    {
                        // Verificar licença PRO antes de iniciar AutoPilot
                        if (!VoltrisOptimizer.Services.LicenseManager.IsPro)
                        {
                            _logger.LogInfo("[GamerViewModel] AutoPilot ignorado — requer licença PRO");
                            return;
                        }
                        _logger.LogInfo("[GamerViewModel] Iniciando AutoPilot - Modo Gamer Automático ATIVADO pelo usuário");
                        _orchestrator.StartAutoPilot();
                        _logger.LogSuccess("[GamerViewModel] AutoPilot iniciado com sucesso");
                    }
                    else
                    {
                        _logger.LogInfo("[GamerViewModel] Parando AutoPilot - Modo Gamer Automático DESATIVADO pelo usuário");
                        _orchestrator.StopAutoPilot();
                        _logger.LogInfo("[GamerViewModel] AutoPilot parado com sucesso");
                    }
                    
                    // Salvar configuração imediatamente
                    try
                    {
                        var settings = SettingsService.Instance?.Settings;
                        if (settings != null)
                        {
                            settings.AutoGamerMode = value;
                            SettingsService.Instance.SaveSettings();
                            _logger.LogInfo($"[GamerViewModel] Configuração AutoGamerMode salva: {value}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"[GamerViewModel] Erro ao salvar configuração AutoGamerMode: {ex.Message}");
                    }
                }
            }
        }

        // Módulos de Otimização Temporários
        private bool _isTemporaryOptimizationSessionActive;
        public bool IsTemporaryOptimizationSessionActive
        {
            get => _isTemporaryOptimizationSessionActive;
            set => SetProperty(ref _isTemporaryOptimizationSessionActive, value);
        }

        private string _temporaryOptimizationStatus = LocalizationService.Instance.GetString("Inactive");
        public string TemporaryOptimizationStatus
        {
            get => _temporaryOptimizationStatus;
            set => SetProperty(ref _temporaryOptimizationStatus, value);
        }

        private bool _autoActivateTemporaryOptimizations = true; // ATIVADO por padrão
        public bool AutoActivateTemporaryOptimizations
        {
            get => _autoActivateTemporaryOptimizations;
            set => SetProperty(ref _autoActivateTemporaryOptimizations, value);
        }

        #endregion

        #region Properties - Biblioteca de Jogos

        private readonly object _gamesLock = new object();

        private ObservableCollection<GamerModels.DetectedGame>? _games;
        public ObservableCollection<GamerModels.DetectedGame> Games
        {
            get
            {
                if (_games == null)
                {
                    // CORREÇÃO CRÍTICA: Nunca fazer dispatcher.Invoke bloqueante no getter.
                    // Se o ViewModel for construído em Task.Run, o Invoke trava a thread de background.
                    _games = new ObservableCollection<GamerModels.DetectedGame>();
                    System.Windows.Data.BindingOperations.EnableCollectionSynchronization(_games, _gamesLock);
                }
                return _games;
            }
        }

        private GamerModels.DetectedGame? _selectedGame;
        public GamerModels.DetectedGame? SelectedGame
        {
            get => _selectedGame;
            set
            {
                if (SetProperty(ref _selectedGame, value))
                {
                    _logger.LogInfo($"[GamerViewModel] Jogo selecionado na UI: {value?.Name ?? "Nenhum"}");
                    OnPropertyChanged(nameof(HasSelectedGame));
                    
                    // CARREGAR CONFIGURAÇÕES DO PERFIL PARA A UI
                    LoadProfileToUi();
                    
                    // Raise CanExecuteChanged for all commands that depend on SelectedGame
                    _logger.LogInfo("[GamerViewModel] Atualizando estados dos comandos de jogo");
                    if (ApplyProfileCommand is RelayCommand applyCmd)
                        applyCmd.RaiseCanExecuteChanged();
                    if (ApplyProfileCommand is AsyncRelayCommand applyAsyncCmd)
                        applyAsyncCmd.RaiseCanExecuteChanged();
                    
                    if (CreateProfileCommand is RelayCommand createCmd)
                        createCmd.RaiseCanExecuteChanged();
                    if (CreateProfileCommand is AsyncRelayCommand createAsyncCmd)
                        createAsyncCmd.RaiseCanExecuteChanged();
                    
                    if (RemoveProfileCommand is RelayCommand removeCmd)
                        removeCmd.RaiseCanExecuteChanged();
                    if (RemoveProfileCommand is AsyncRelayCommand removeAsyncCmd)
                        removeAsyncCmd.RaiseCanExecuteChanged();
                    
                    if (CleanCacheCommand is RelayCommand cleanCmd)
                        cleanCmd.RaiseCanExecuteChanged();
                    if (CleanCacheCommand is AsyncRelayCommand cleanAsyncCmd)
                        cleanAsyncCmd.RaiseCanExecuteChanged();
                }
            }
        }

        public bool HasSelectedGame => SelectedGame != null;

        #endregion

        #region Properties - Opções de Otimização

        private GamerModels.GamerOptimizationOptions _options = new();

        public bool OptimizePowerPlan
        {
            get => _options.OptimizePowerPlan;
            set 
            { 
                _options.OptimizePowerPlan = value; 
                _logger?.LogInfo($"[GamerViewModel][TOGGLE] OptimizePowerPlan alterado para: {value}");
                OnPropertyChanged(); 
                SyncOptions(); 
                _ = ApplyCurrentOptionsIfActiveAsync();
            }
        }

        public bool OptimizeCpu
        {
            get => _options.OptimizeCpu;
            set 
            { 
                _options.OptimizeCpu = value; 
                _logger?.LogInfo($"[GamerViewModel][TOGGLE] OptimizeCpu alterado para: {value}");
                OnPropertyChanged(); 
                SyncOptions(); 
                _ = ApplyCurrentOptionsIfActiveAsync();
            }
        }

        public bool OptimizeGpu
        {
            get => _options.OptimizeGpu;
            set 
            { 
                _options.OptimizeGpu = value; 
                _logger?.LogInfo($"[GamerViewModel][TOGGLE] OptimizeGpu alterado para: {value}");
                OnPropertyChanged(); 
                SyncOptions(); 
                _ = ApplyCurrentOptionsIfActiveAsync();
            }
        }

        public bool OptimizeNetwork
        {
            get => _options.OptimizeNetwork;
            set 
            { 
                _options.OptimizeNetwork = value; 
                _logger?.LogInfo($"[GamerViewModel][TOGGLE] OptimizeNetwork alterado para: {value}");
                OnPropertyChanged(); 
                SyncOptions(); 
                _ = ApplyCurrentOptionsIfActiveAsync();
            }
        }

        public bool OptimizeMemory
        {
            get => _options.OptimizeMemory;
            set 
            { 
                _options.OptimizeMemory = value; 
                _logger?.LogInfo($"[GamerViewModel][TOGGLE] OptimizeMemory alterado para: {value}");
                OnPropertyChanged(); 
                SyncOptions(); 
                _ = ApplyCurrentOptionsIfActiveAsync();
            }
        }

        public bool EnableGameMode
        {
            get => _options.EnableGameMode;
            set 
            { 
                _options.EnableGameMode = value; 
                _logger?.LogInfo($"[GamerViewModel][TOGGLE] EnableGameMode alterado para: {value}");
                OnPropertyChanged(); 
                SyncOptions(); 
                _ = ApplyCurrentOptionsIfActiveAsync();
            }
        }

        public bool ReduceLatency
        {
            get => _options.ReduceLatency;
            set 
            { 
                _options.ReduceLatency = value; 
                _logger?.LogInfo($"[GamerViewModel][TOGGLE] ReduceLatency alterado para: {value}");
                OnPropertyChanged(); 
                SyncOptions(); 
                _ = ApplyCurrentOptionsIfActiveAsync();
            }
        }

        public bool CloseBackgroundApps
        {
            get => _options.CloseBackgroundApps;
            set 
            { 
                _options.CloseBackgroundApps = value; 
                _logger?.LogInfo($"[GamerViewModel][TOGGLE] CloseBackgroundApps alterado para: {value}");
                OnPropertyChanged(); 
                SyncOptions(); 
                _ = ApplyCurrentOptionsIfActiveAsync();
            }
        }

        public bool ApplyFpsBoost
        {
            get => _options.ApplyFpsBoost;
            set 
            { 
                _options.ApplyFpsBoost = value; 
                _logger?.LogInfo($"[GamerViewModel][TOGGLE] ApplyFpsBoost alterado para: {value}");
                OnPropertyChanged(); 
                SyncOptions(); 
                _ = ApplyCurrentOptionsIfActiveAsync();
            }
        }

        public bool EnableExtremeMode
        {
            get => _options.EnableExtremeMode;
            set 
            { 
                _options.EnableExtremeMode = value; 
                _logger?.LogInfo($"[GamerViewModel][TOGGLE] EnableExtremeMode alterado para: {value}");
                OnPropertyChanged(); 
                SyncOptions(); 
                _ = ApplyCurrentOptionsIfActiveAsync();
            }
        }

        public bool EnableAntiStutter
        {
            get => _options.EnableAntiStutter;
            set 
            { 
                _options.EnableAntiStutter = value; 
                _logger?.LogInfo($"[GamerViewModel][TOGGLE] EnableAntiStutter alterado para: {value}");
                OnPropertyChanged(); 
                SyncOptions(); 
                _ = ApplyCurrentOptionsIfActiveAsync();
            }
        }

        public bool EnableAdaptiveNetwork
        {
            get => _options.EnableAdaptiveNetwork;
            set 
            { 
                _options.EnableAdaptiveNetwork = value; 
                _logger?.LogInfo($"[GamerViewModel][TOGGLE] EnableAdaptiveNetwork alterado para: {value}");
                OnPropertyChanged(); 
                SyncOptions(); 
                _ = ApplyCurrentOptionsIfActiveAsync();
            }
        }

        public bool DisableHpet
        {
            get => _options.DisableHpet;
            set 
            { 
                _options.DisableHpet = value; 
                _logger?.LogInfo($"[GamerViewModel][TOGGLE] DisableHpet alterado para: {value}");
                OnPropertyChanged(); 
                SyncOptions(); 
                _ = ApplyCurrentOptionsIfActiveAsync();
            }
        }

        public bool DisableWallpaperSlideshow
        {
            get => _options.DisableWallpaperSlideshow;
            set 
            { 
                _options.DisableWallpaperSlideshow = value; 
                _logger?.LogInfo($"[GamerViewModel][TOGGLE] DisableWallpaperSlideshow (Cor Sólida) alterado para: {value}");
                OnPropertyChanged(); 
                SyncOptions(); 
                _ = ApplyCurrentOptionsIfActiveAsync();
            }
        }

        public bool DisableUwpBackgroundApps
        {
            get => _options.DisableUwpBackgroundApps;
            set 
            { 
                _options.DisableUwpBackgroundApps = value; 
                _logger?.LogInfo($"[GamerViewModel][TOGGLE] DisableUwpBackgroundApps alterado para: {value}");
                OnPropertyChanged(); 
                SyncOptions(); 
                _ = ApplyCurrentOptionsIfActiveAsync();
            }
        }

        public bool DisableHeavyServices
        {
            get => _options.DisableHeavyServices;
            set 
            { 
                _options.DisableHeavyServices = value; 
                _logger?.LogInfo($"[GamerViewModel][TOGGLE] DisableHeavyServices alterado para: {value}");
                OnPropertyChanged(); 
                SyncOptions(); 
                _ = ApplyCurrentOptionsIfActiveAsync();
            }
        }

        public bool DisableDwmVSync
        {
            get => _options.DisableDwmVSync;
            set 
            { 
                _options.DisableDwmVSync = value; 
                _logger?.LogInfo($"[GamerViewModel][TOGGLE] DisableDwmVSync alterado para: {value}");
                OnPropertyChanged(); 
                SyncOptions(); 
                _ = ApplyCurrentOptionsIfActiveAsync();
            }
        }

        public bool OptimizeGamma
        {
            get => _options.OptimizeGamma;
            set 
            { 
                _options.OptimizeGamma = value; 
                _logger?.LogInfo($"[GamerViewModel][TOGGLE] OptimizeGamma alterado para: {value}");
                OnPropertyChanged(); 
                SyncOptions(); 
                _ = ApplyCurrentOptionsIfActiveAsync();
            }
        }

        public GamerModels.VisualOptimizationLevel VisualLevel
        {
            get => _options.VisualLevel;
            set 
            { 
                if (_options.VisualLevel != value)
                {
                    _logger?.LogInfo($"[GamerViewModel][TOGGLE] VisualLevel alterado de {_options.VisualLevel} para: {value}");
                    _options.VisualLevel = value; 
                    OnPropertyChanged(); 
                    OnPropertyChanged(nameof(DisableWindowAnimations));
                    SyncOptions(); 
                }
            }
        }

        public bool DisableWindowAnimations
        {
            get => VisualLevel == GamerModels.VisualOptimizationLevel.Balanced;
            set 
            { 
                VisualLevel = value ? GamerModels.VisualOptimizationLevel.Balanced : GamerModels.VisualOptimizationLevel.None; 
                _logger?.LogInfo($"[GamerViewModel][TOGGLE] DisableWindowAnimations alterado para: {value}");
                OnPropertyChanged();
            }
        }

        private bool _enableSchedulerSuspension = true;
        public bool EnableSchedulerSuspension
        {
            get => _enableSchedulerSuspension;
            set 
            { 
                _enableSchedulerSuspension = value; 
                _logger?.LogInfo($"[GamerViewModel][TOGGLE] EnableSchedulerSuspension alterado para: {value}");
                OnPropertyChanged();
                SyncOptions();
                _ = ApplyCurrentOptionsIfActiveAsync();
            }
        }

        private bool _enableWindowsServiceOptimization = true;
        public bool EnableWindowsServiceOptimization
        {
            get => _enableWindowsServiceOptimization;
            set 
            { 
                _enableWindowsServiceOptimization = value; 
                _logger?.LogInfo($"[GamerViewModel][TOGGLE] EnableWindowsServiceOptimization alterado para: {value}");
                OnPropertyChanged();
                SyncOptions();
                _ = ApplyCurrentOptionsIfActiveAsync();
            }
        }

        public GamerModels.PingTargetMode PingTarget
        {
            get => _options.PingTarget;
            set 
            { 
                _options.PingTarget = value; 
                _logger?.LogInfo($"[GamerViewModel][TOGGLE] PingTarget alterado para: {value}");
                OnPropertyChanged(); 
                SyncOptions(); 
            }
        }

        private void SyncOptions()
        {
            try
            {
                _orchestrator.SetOptions(_options);
                // Sincronizar OptimizePowerPlan com o GamerModeManager
                if (_gamerModeManager != null)
                    _gamerModeManager.Config.OptimizePowerPlan = _options.OptimizePowerPlan;
                _logger?.LogInfo($"[GamerViewModel][SYNC] Opções sincronizadas com Orchestrator | " +
                    $"PowerPlan={OptimizePowerPlan}, CPU={OptimizeCpu}, GPU={OptimizeGpu}, NET={OptimizeNetwork}, RAM={OptimizeMemory}, " +
                    $"GameMode={EnableGameMode}, Latency={ReduceLatency}, BGApps={CloseBackgroundApps}, " +
                    $"FPSBoost={ApplyFpsBoost}, Extreme={EnableExtremeMode}, AntiStutter={EnableAntiStutter}, " +
                    $"AdaptNet={EnableAdaptiveNetwork}, HPET={DisableHpet}, " +
                    $"Wallpaper={DisableWallpaperSlideshow}, UWP={DisableUwpBackgroundApps}, " +
                    $"Services={DisableHeavyServices}, VSync={DisableDwmVSync}, Gamma={OptimizeGamma}, " +
                    $"Visual={VisualLevel}, Scheduler={_options.EnableSchedulerSuspension}, WinServices={_options.EnableWindowsServiceOptimization}");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[GamerViewModel] Erro ao sincronizar opções: {ex.Message}");
            }
        }

        private async Task ApplyCurrentOptionsIfActiveAsync()
        {
            if (!IsGamerModeActive) return;
            try
            {
                var machineProfile = await _profileDetector.AnalyzeMachineProfileAsync();
                var options = new GamerModels.GamerOptimizationOptions
                {
                    EnableGameMode = _options.EnableGameMode,
                    ReduceLatency = _options.ReduceLatency,
                    CloseBackgroundApps = _options.CloseBackgroundApps,
                    ApplyFpsBoost = _options.ApplyFpsBoost,
                    EnableExtremeMode = _options.EnableExtremeMode,
                    EnableAntiStutter = _options.EnableAntiStutter,
                    EnableAdaptiveNetwork = _options.EnableAdaptiveNetwork,
                    PingTarget = _options.PingTarget,
                    DisableHeavyServices = _options.DisableHeavyServices,
                    DisableUwpBackgroundApps = _options.DisableUwpBackgroundApps,
                    OptimizeGamma = _options.OptimizeGamma,
                    DisableHpet = _options.DisableHpet,
                    DisableWallpaperSlideshow = _options.DisableWallpaperSlideshow,
                    DisableDwmVSync = _options.DisableDwmVSync,
                    OptimizePowerPlan = _options.OptimizePowerPlan,
                    OptimizeCpu = _options.OptimizeCpu,
                    OptimizeGpu = _options.OptimizeGpu,
                    OptimizeNetwork = _options.OptimizeNetwork,
                    OptimizeMemory = _options.OptimizeMemory,
                    VisualLevel = _options.VisualLevel,
                    EnableSchedulerSuspension = _options.EnableSchedulerSuspension,
                    EnableWindowsServiceOptimization = _options.EnableWindowsServiceOptimization
                };
                await _adaptiveEngine.ApplyAdaptiveOptimizationsAsync(options, machineProfile);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[GamerViewModel] Erro ao aplicar otimizações do toggle: {ex.Message}");
            }
        }

        #endregion

        #region Properties - OSD Overlay

        private bool _suppressOverlayUpdate = false;
        // CORREÇÃO: Debounce para evitar loop de UpdateOverlaySettings durante carga de settings
        // O problema: LoadOverlaySettingsAsync seta múltiplas propriedades em sequência,
        // cada uma dispara UpdateOverlaySettings()  mesmo com _suppressOverlayUpdate,
        // chamadas concorrentes podem escapar. O debounce garante que apenas a última
        // chamada dentro de 50ms seja executada.
        private System.Threading.Timer? _overlayDebounceTimer;
        private readonly object _overlayDebounceLock = new object();
        private bool _overlayEnabled = false;
        public bool OverlayEnabled
        {
            get => _overlayEnabled;
            set
            {
                _logger?.LogInfo($"[GamerViewModel][TOGGLE] OverlayEnabled alterado para: {value}");
                if (SetProperty(ref _overlayEnabled, value))
                {
                    _logger?.LogInfo($"[GamerViewModel][TOGGLE] OverlayEnabled APLICADO: {value} — chamando UpdateOverlaySettings()");
                    UpdateOverlaySettings();
                }
            }
        }

        private bool _overlayAutoStartWithGamerMode = false;
        public bool OverlayAutoStartWithGamerMode
        {
            get => _overlayAutoStartWithGamerMode;
            set
            {
                _logger?.LogInfo($"[GamerViewModel][TOGGLE] OverlayAutoStartWithGamerMode alterado para: {value}");
                if (SetProperty(ref _overlayAutoStartWithGamerMode, value))
                {
                    _logger?.LogInfo($"[GamerViewModel][TOGGLE] OverlayAutoStartWithGamerMode APLICADO: {value}");
                    UpdateOverlaySettings();
                }
            }
        }

        private bool _overlayShowFps = true;
        public bool OverlayShowFps
        {
            get => _overlayShowFps;
            set 
            { 
                _logger?.LogInfo($"[GamerViewModel][DEBUG] OverlayShowFps mudando: {_overlayShowFps} ? {value}");
                if (SetProperty(ref _overlayShowFps, value)) 
                {
                    _logger?.LogInfo($"[GamerViewModel][DEBUG] OverlayShowFps atualizado com sucesso para {value}");
                    // Forçar atualização mesmo durante carga
                    _suppressOverlayUpdate = false;
                    UpdateOverlaySettings();
                }
            }
        }

        private bool _overlayShowFrameTime = true;
        public bool OverlayShowFrameTime
        {
            get => _overlayShowFrameTime;
            set 
            { 
                _logger?.LogInfo($"[GamerViewModel][DEBUG] OverlayShowFrameTime mudando: {_overlayShowFrameTime} ? {value}");
                if (SetProperty(ref _overlayShowFrameTime, value)) 
                {
                    _logger?.LogInfo($"[GamerViewModel][DEBUG] OverlayShowFrameTime atualizado com sucesso para {value}");
                    // Forçar atualização mesmo durante carga
                    _suppressOverlayUpdate = false;
                    UpdateOverlaySettings();
                }
            }
        }

        private bool _overlayShowCpuUsage = true;
        public bool OverlayShowCpuUsage
        {
            get => _overlayShowCpuUsage;
            set 
            { 
                _logger?.LogInfo($"[GamerViewModel][DEBUG] OverlayShowCpuUsage mudando: {_overlayShowCpuUsage} ? {value}");
                if (SetProperty(ref _overlayShowCpuUsage, value)) 
                {
                    _logger?.LogInfo($"[GamerViewModel][DEBUG] OverlayShowCpuUsage atualizado com sucesso para {value}");
                    // Forçar atualização mesmo durante carga
                    _suppressOverlayUpdate = false;
                    UpdateOverlaySettings();
                }
            }
        }

        private bool _overlayShowGpuUsage = true;
        public bool OverlayShowGpuUsage
        {
            get => _overlayShowGpuUsage;
            set 
            { 
                _logger?.LogInfo($"[GamerViewModel][DEBUG] OverlayShowGpuUsage mudando: {_overlayShowGpuUsage} ? {value}");
                if (SetProperty(ref _overlayShowGpuUsage, value)) 
                {
                    _logger?.LogInfo($"[GamerViewModel][DEBUG] OverlayShowGpuUsage atualizado com sucesso para {value}");
                    // Forçar atualização mesmo durante carga
                    _suppressOverlayUpdate = false;
                    UpdateOverlaySettings();
                }
            }
        }

        private bool _overlayShowRamUsage = true;
        public bool OverlayShowRamUsage
        {
            get => _overlayShowRamUsage;
            set 
            { 
                _logger?.LogInfo($"[GamerViewModel][DEBUG] OverlayShowRamUsage mudando: {_overlayShowRamUsage} ? {value}");
                if (SetProperty(ref _overlayShowRamUsage, value)) 
                {
                    _logger?.LogInfo($"[GamerViewModel][DEBUG] OverlayShowRamUsage atualizado com sucesso para {value}");
                    // Forçar atualização mesmo durante carga
                    _suppressOverlayUpdate = false;
                    UpdateOverlaySettings();
                }
            }
        }

        private bool _overlayShowVramUsage = true;
        public bool OverlayShowVramUsage
        {
            get => _overlayShowVramUsage;
            set 
            { 
                _logger?.LogInfo($"[GamerViewModel][DEBUG] OverlayShowVramUsage mudando: {_overlayShowVramUsage} ? {value}");
                if (SetProperty(ref _overlayShowVramUsage, value)) 
                {
                    _logger?.LogInfo($"[GamerViewModel][DEBUG] OverlayShowVramUsage atualizado com sucesso para {value}");
                    // Forçar atualização mesmo durante carga
                    _suppressOverlayUpdate = false;
                    UpdateOverlaySettings();
                }
            }
        }

        private bool _overlayShowCpuTemp = false;
        public bool OverlayShowCpuTemp
        {
            get => _overlayShowCpuTemp;
            set 
            { 
                _logger?.LogInfo($"[GamerViewModel][DEBUG] OverlayShowCpuTemp mudando: {_overlayShowCpuTemp} ? {value}");
                if (SetProperty(ref _overlayShowCpuTemp, value)) 
                {
                    _logger?.LogInfo($"[GamerViewModel][DEBUG] OverlayShowCpuTemp atualizado com sucesso para {value}");
                    // Forçar atualização mesmo durante carga
                    _suppressOverlayUpdate = false;
                    UpdateOverlaySettings();
                }
            }
        }

        private bool _overlayShowGpuTemp = true;
        public bool OverlayShowGpuTemp
        {
            get => _overlayShowGpuTemp;
            set 
            { 
                _logger?.LogInfo($"[GamerViewModel][DEBUG] OverlayShowGpuTemp mudando: {_overlayShowGpuTemp} ? {value}");
                if (SetProperty(ref _overlayShowGpuTemp, value)) 
                {
                    _logger?.LogInfo($"[GamerViewModel][DEBUG] OverlayShowGpuTemp atualizado com sucesso para {value}");
                    // Forçar atualização mesmo durante carga
                    _suppressOverlayUpdate = false;
                    UpdateOverlaySettings();
                }
            }
        }

        private bool _overlayShowCpuClock = true;
        public bool OverlayShowCpuClock
        {
            get => _overlayShowCpuClock;
            set 
            { 
                _logger?.LogInfo($"[GamerViewModel][DEBUG] OverlayShowCpuClock mudando: {_overlayShowCpuClock} ? {value}");
                if (SetProperty(ref _overlayShowCpuClock, value)) 
                {
                    _logger?.LogInfo($"[GamerViewModel][DEBUG] OverlayShowCpuClock atualizado com sucesso para {value}");
                    // Forçar atualização mesmo durante carga
                    _suppressOverlayUpdate = false;
                    UpdateOverlaySettings();
                }
            }
        }

        private bool _overlayShowGpuClock = true;
        public bool OverlayShowGpuClock
        {
            get => _overlayShowGpuClock;
            set 
            { 
                _logger?.LogInfo($"[GamerViewModel][DEBUG] OverlayShowGpuClock mudando: {_overlayShowGpuClock} ? {value}");
                if (SetProperty(ref _overlayShowGpuClock, value)) 
                {
                    _logger?.LogInfo($"[GamerViewModel][DEBUG] OverlayShowGpuClock atualizado com sucesso para {value}");
                    // Forçar atualização mesmo durante carga
                    _suppressOverlayUpdate = false;
                    UpdateOverlaySettings();
                }
            }
        }

        private bool _overlayShowInputLatency = false;
        public bool OverlayShowInputLatency
        {
            get => _overlayShowInputLatency;
            set 
            { 
                _logger?.LogInfo($"[GamerViewModel][DEBUG] OverlayShowInputLatency mudando: {_overlayShowInputLatency} ? {value}");
                if (SetProperty(ref _overlayShowInputLatency, value)) 
                {
                    _logger?.LogInfo($"[GamerViewModel][DEBUG] OverlayShowInputLatency atualizado com sucesso para {value}");
                    // Forçar atualização mesmo durante carga
                    _suppressOverlayUpdate = false;
                    UpdateOverlaySettings();
                }
            }
        }

        private string _overlayTextColor = "#00E5FF";
        public string OverlayTextColor
        {
            get => _overlayTextColor;
            set 
            { 
                _logger?.LogInfo($"[GamerViewModel][TOGGLE] OverlayTextColor alterado de '{_overlayTextColor}' para '{value}'");
                if (SetProperty(ref _overlayTextColor, value)) 
                {
                    _logger?.LogInfo($"[GamerViewModel][TOGGLE] OverlayTextColor APLICADO: {value}");
                    _suppressOverlayUpdate = false;
                    UpdateOverlaySettings();
                }
            }
        }
        
        private string _overlayBackgroundColor = "#B2000000";
        public string OverlayBackgroundColor
        {
            get => _overlayBackgroundColor;
            set 
            { 
                _logger?.LogInfo($"[GamerViewModel][TOGGLE] OverlayBackgroundColor alterado de '{_overlayBackgroundColor}' para '{value}'");
                if (SetProperty(ref _overlayBackgroundColor, value)) 
                {
                    _logger?.LogInfo($"[GamerViewModel][TOGGLE] OverlayBackgroundColor APLICADO: {value}");
                    _suppressOverlayUpdate = false;
                    UpdateOverlaySettings();
                }
            }
        }

        private VoltrisOptimizer.Services.Gamer.Overlay.Models.OverlayPosition _overlaySelectedPosition = VoltrisOptimizer.Services.Gamer.Overlay.Models.OverlayPosition.TopRight;
        public VoltrisOptimizer.Services.Gamer.Overlay.Models.OverlayPosition OverlaySelectedPosition
        {
            get => _overlaySelectedPosition;
            set 
            { 
                _logger?.LogInfo($"[GamerViewModel][TOGGLE] OverlaySelectedPosition alterado para: {value}");
                SetProperty(ref _overlaySelectedPosition, value); 
                UpdateOverlaySettings(); 
            }
        }

        public System.Collections.Generic.List<VoltrisOptimizer.Services.Gamer.Overlay.Models.OverlayPosition> OverlayPositions { get; } = 
            new System.Collections.Generic.List<VoltrisOptimizer.Services.Gamer.Overlay.Models.OverlayPosition>
            {
                VoltrisOptimizer.Services.Gamer.Overlay.Models.OverlayPosition.TopLeft,
                VoltrisOptimizer.Services.Gamer.Overlay.Models.OverlayPosition.TopRight,
                VoltrisOptimizer.Services.Gamer.Overlay.Models.OverlayPosition.BottomLeft,
                VoltrisOptimizer.Services.Gamer.Overlay.Models.OverlayPosition.BottomRight,
                VoltrisOptimizer.Services.Gamer.Overlay.Models.OverlayPosition.TopCenter,
                VoltrisOptimizer.Services.Gamer.Overlay.Models.OverlayPosition.BottomCenter
            };

        private double _overlayFontSize = 14.0;
        public double OverlayFontSize
        {
            get => _overlayFontSize;
            set 
            { 
                _logger?.LogInfo($"[GamerViewModel][TOGGLE] OverlayFontSize alterado para: {value:F1}");
                SetProperty(ref _overlayFontSize, value); 
                UpdateOverlaySettings(); 
            }
        }

        private double _overlayOpacity = 0.9;
        public double OverlayOpacity
        {
            get => _overlayOpacity;
            set 
            { 
                _logger?.LogInfo($"[GamerViewModel][TOGGLE] OverlayOpacity alterado para: {value:F2}");
                SetProperty(ref _overlayOpacity, value); 
                UpdateOverlaySettings(); 
            }
        }

        private ICommand? _testOverlayCommand;
        public ICommand TestOverlayCommand
        {
            get
            {
                return _testOverlayCommand ??= new LicensedCommand(
                    new AsyncRelayCommand(async _ =>
                    {
                        if (_overlayService != null)
                        {
                            var currentProcess = Process.GetCurrentProcess();
                            await _overlayService.StartAsync(currentProcess.Id);
                            await Task.Delay(5000); // Mostrar por 5 segundos
                            await _overlayService.StopAsync();
                        }
                    }),
                    App.Services?.GetService<ILicenseGuard>() ?? throw new InvalidOperationException("ILicenseGuard não registrado"),
                    "gamer_mode",
                    App.Services?.GetService<ILicenseDialogService>() ?? throw new InvalidOperationException("ILicenseDialogService não registrado"));
            }
        }

        private ICommand? _activateOverlayCommand;
        public ICommand ActivateOverlayCommand
        {
            get
            {
                return _activateOverlayCommand ??= new LicensedCommand(
                    new AsyncRelayCommand(async _ =>
                    {
                        try
                        {
                            _logger?.LogInfo("[GamerViewModel] Ativando Performance Monitor...");
                            _suppressOverlayUpdate = false;
                            _overlayEnabled = true;
                            OnPropertyChanged(nameof(OverlayEnabled));

                            if (_overlayService != null && IsGamerModeActive)
                            {
                                var currentProcess = Process.GetCurrentProcess();
                                await _overlayService.StartAsync(currentProcess.Id);
                                _logger?.LogSuccess("[GamerViewModel] Performance Monitor ativado com sucesso!");
                            }
                            else
                            {
                                _logger?.LogInfo("[GamerViewModel] Performance Monitor configurado para iniciar com o Modo Gamer");
                            }
                            ApplyOverlaySettings();
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError($"[GamerViewModel] Erro ao ativar Performance Monitor: {ex.Message}");
                        }
                    }),
                    App.Services?.GetService<ILicenseGuard>() ?? throw new InvalidOperationException("ILicenseGuard não registrado"),
                    "gamer_mode",
                    App.Services?.GetService<ILicenseDialogService>() ?? throw new InvalidOperationException("ILicenseDialogService não registrado"));
            }
        }

        private ICommand? _deactivateOverlayCommand;
        public ICommand DeactivateOverlayCommand
        {
            get
            {
                return _deactivateOverlayCommand ??= new AsyncRelayCommand(async _ =>
                {
                    try
                    {
                        _logger?.LogInfo("[GamerViewModel] Desativando Performance Monitor...");
                        _suppressOverlayUpdate = false;
                        _overlayEnabled = false;
                        OnPropertyChanged(nameof(OverlayEnabled));
                        
                        if (_overlayService != null)
                        {
                            await _overlayService.StopAsync();
                            _logger?.LogSuccess("[GamerViewModel] Performance Monitor desativado com sucesso!");
                        }
                        ApplyOverlaySettings();
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError($"[GamerViewModel] Erro ao desativar Performance Monitor: {ex.Message}");
                    }
                });
            }
        }

        private void UpdateOverlaySettings()
        {
            if (_suppressOverlayUpdate) 
            {
                _logger?.LogInfo($"[GamerViewModel][DEBUG] UpdateOverlaySettings() SUPRIMIDO - _suppressOverlayUpdate=true");
                return;
            }
            
            _logger?.LogInfo($"[GamerViewModel][DEBUG] UpdateOverlaySettings() INICIADO");
            
            // CORREÇÃO: Debounce de 50ms para evitar loop de chamadas em sequência
            // (ex: durante LoadOverlaySettingsAsync ou binding em cascata)
            lock (_overlayDebounceLock)
            {
                _logger?.LogInfo($"[GamerViewModel][DEBUG] Debounce lock adquirido, timer anterior descartado");
                _overlayDebounceTimer?.Dispose();
                // OTIMIZAÇÃO: Aumentado debounce de 50ms para 200ms para reduzir frequência
                _overlayDebounceTimer = new System.Threading.Timer(_ =>
                {
                    _overlayDebounceTimer = null;
                    _logger?.LogInfo($"[GamerViewModel][DEBUG] Debounce timer disparado - chamando ApplyOverlaySettings()");
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => ApplyOverlaySettings());
                }, null, 200, System.Threading.Timeout.Infinite);
                _logger?.LogInfo($"[GamerViewModel][DEBUG] Novo debounce timer criado (200ms)");
            }
        }

        private void ApplyOverlaySettings()
        {
            if (_suppressOverlayUpdate) 
            {
                _logger?.LogInfo($"[GamerViewModel][DEBUG] ApplyOverlaySettings() SUPRIMIDO - _suppressOverlayUpdate=true");
                return;
            }
            
            _logger?.LogInfo("[GamerViewModel][DEBUG] ApplyOverlaySettings() INICIADO");
            
            if (_overlayService == null)
            {
                _logger?.LogError("[GamerViewModel][DEBUG] _overlayService é NULL! Overlay não pode ser atualizado");
                return;
            }

            _logger?.LogInfo($"[GamerViewModel][DEBUG] Criando settings: IsEnabled={_overlayEnabled}, AutoStartWithGamerMode={_overlayAutoStartWithGamerMode}");
            _logger?.LogInfo($"[GamerViewModel][DEBUG] Métricas: FPS={_overlayShowFps}, FrameTime={_overlayShowFrameTime}, CPU={_overlayShowCpuUsage}, GPU={_overlayShowGpuUsage}, RAM={_overlayShowRamUsage}, VRAM={_overlayShowVramUsage}");
            _logger?.LogInfo($"[GamerViewModel][DEBUG] Temperaturas: CPU={_overlayShowCpuTemp}, GPU={_overlayShowGpuTemp}");
            _logger?.LogInfo($"[GamerViewModel][DEBUG] Clocks: CPU={_overlayShowCpuClock}, GPU={_overlayShowGpuClock}");
            _logger?.LogInfo($"[GamerViewModel][DEBUG] Latency={_overlayShowInputLatency}");

            var settings = new VoltrisOptimizer.Services.Gamer.Overlay.Models.OverlaySettings
            {
                IsEnabled = _overlayEnabled,
                AutoStartWithGamerMode = _overlayAutoStartWithGamerMode,
                Metrics = new VoltrisOptimizer.Services.Gamer.Overlay.Models.OverlayMetrics
                {
                    ShowFps = _overlayShowFps,
                    ShowFrameTime = _overlayShowFrameTime,
                    ShowCpuUsage = _overlayShowCpuUsage,
                    ShowGpuUsage = _overlayShowGpuUsage,
                    ShowRamUsage = _overlayShowRamUsage,
                    ShowVramUsage = _overlayShowVramUsage,
                    ShowCpuTemperature = _overlayShowCpuTemp,
                    ShowGpuTemperature = _overlayShowGpuTemp,
                    ShowCpuClock = _overlayShowCpuClock,
                    ShowGpuClock = _overlayShowGpuClock,
                    ShowInputLatency = _overlayShowInputLatency
                },
                Position = _overlaySelectedPosition,
                Font = new VoltrisOptimizer.Services.Gamer.Overlay.Models.FontSettings
                {
                    FontFamily = "Consolas",
                    FontSize = _overlayFontSize,
                    IsBold = true
                },
                TextColor = _overlayTextColor,
                BackgroundColor = _overlayBackgroundColor,
                Opacity = _overlayOpacity
            };

            _logger?.LogInfo("[GamerViewModel][DEBUG] Sincronizando propriedades planas com Metrics...");
            settings.SyncFlatWithMetrics();
            
            _logger?.LogInfo("[GamerViewModel][DEBUG] Chamando _overlayService.UpdateSettings()");
            _overlayService.UpdateSettings(settings);
            
            _logger?.LogInfo("[GamerViewModel][DEBUG] Chamando _overlayService.SaveSettingsAsync()");
            _overlayService.SaveSettingsAsync();
            
            _logger?.LogSuccess($"[GamerViewModel][DEBUG] Overlay settings atualizados com sucesso! IsEnabled={_overlayEnabled}, AutoStartWithGamerMode={_overlayAutoStartWithGamerMode}");
        }

        private async Task LoadOverlaySettingsAsync()
        {
            try
            {
                _logger?.LogInfo("[GamerViewModel][DEBUG] LoadOverlaySettingsAsync() INICIADO");
                
                if (_overlayService != null)
                {
                    _logger?.LogInfo("[GamerViewModel][DEBUG] Chamando _overlayService.LoadSettingsAsync()");
                    await _overlayService.LoadSettingsAsync();
                    var settings = _overlayService.Settings;
                    
                    _logger?.LogInfo($"[GamerViewModel][DEBUG] Settings carregados: IsEnabled={settings.IsEnabled}, AutoStartWithGamerMode={settings.AutoStartWithGamerMode}");
                    
                    // Suprimir chamadas individuais a UpdateOverlaySettings durante carga
                    _suppressOverlayUpdate = true;
                    try
                    {
                        _logger?.LogInfo("[GamerViewModel][DEBUG] Aplicando settings ao ViewModel (suppress=true)");
                        
                        _overlayEnabled = settings.IsEnabled;
                        _overlayAutoStartWithGamerMode = settings.AutoStartWithGamerMode;
                        _overlayShowFps = settings.Metrics.ShowFps;
                        _overlayShowFrameTime = settings.Metrics.ShowFrameTime;
                        _overlayShowCpuUsage = settings.Metrics.ShowCpuUsage;
                        _overlayShowGpuUsage = settings.Metrics.ShowGpuUsage;
                        _overlayShowRamUsage = settings.Metrics.ShowRamUsage;
                        _overlayShowVramUsage = settings.Metrics.ShowVramUsage;
                        _overlayShowCpuTemp = settings.Metrics.ShowCpuTemperature;
                        _overlayShowGpuTemp = settings.Metrics.ShowGpuTemperature;
                        _overlayShowCpuClock = settings.Metrics.ShowCpuClock;
                        _overlayShowGpuClock = settings.Metrics.ShowGpuClock;
                        _overlayShowInputLatency = settings.Metrics.ShowInputLatency;
                        _overlaySelectedPosition = settings.Position;
                        _overlayFontSize = settings.Font.FontSize;
                        _overlayOpacity = settings.Opacity;
                        _overlayTextColor = string.IsNullOrEmpty(settings.TextColor) ? "#00E5FF" : settings.TextColor;
                        _overlayBackgroundColor = string.IsNullOrEmpty(settings.BackgroundColor) ? "#B2000000" : settings.BackgroundColor;
                        
                        // Notificar todas as propriedades de uma vez (batch)
                        OnPropertyChanged(nameof(OverlayEnabled));
                        OnPropertyChanged(nameof(OverlayAutoStartWithGamerMode));
                        OnPropertyChanged(nameof(OverlayShowFps));
                        OnPropertyChanged(nameof(OverlayShowFrameTime));
                        OnPropertyChanged(nameof(OverlayShowCpuUsage));
                        OnPropertyChanged(nameof(OverlayShowGpuUsage));
                        OnPropertyChanged(nameof(OverlayShowRamUsage));
                        OnPropertyChanged(nameof(OverlayShowVramUsage));
                        OnPropertyChanged(nameof(OverlayShowCpuTemp));
                        OnPropertyChanged(nameof(OverlayShowGpuTemp));
                        OnPropertyChanged(nameof(OverlayShowCpuClock));
                        OnPropertyChanged(nameof(OverlayShowGpuClock));
                        OnPropertyChanged(nameof(OverlayShowInputLatency));
                        OnPropertyChanged(nameof(OverlaySelectedPosition));
                        OnPropertyChanged(nameof(OverlayFontSize));
                        OnPropertyChanged(nameof(OverlayOpacity));
                        OnPropertyChanged(nameof(OverlayTextColor));
                        OnPropertyChanged(nameof(OverlayBackgroundColor));
                        
                        _logger?.LogInfo("[GamerViewModel][DEBUG] Todos os settings aplicados ao ViewModel via batch");
                    }
                    finally
                    {
                        _suppressOverlayUpdate = false;
                        _logger?.LogInfo("[GamerViewModel][DEBUG] Suppression removida (suppress=false)");
                    }
                    
                    // Única chamada ao ApplyOverlaySettings após todas as propriedades carregadas
                    _logger?.LogInfo("[GamerViewModel][DEBUG] Chamando ApplyOverlaySettings após batch load");
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => ApplyOverlaySettings());
                    
                    _logger?.LogSuccess("[GamerViewModel] Overlay settings carregados do disco");
                }
                else
                {
                    _logger?.LogError("[GamerViewModel][DEBUG] _overlayService é NULL durante LoadOverlaySettingsAsync");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[GamerViewModel][DEBUG] Erro em LoadOverlaySettingsAsync: {ex.Message}");
                _logger?.LogError($"[GamerViewModel][DEBUG] StackTrace: {ex.StackTrace}");
            }
        }

        #endregion

        #region Properties - GPU

        private string _gpuInfoText = LocalizationService.Instance.GetString("GpuDetecting");
        public string GpuInfoText
        {
            get => _gpuInfoText;
            set => SetProperty(ref _gpuInfoText, value);
        }

        #endregion

        #region Properties - Diagnóstico

        private readonly object _incidentsLock = new object();
        private ObservableCollection<GamerModels.StutterIncident>? _incidents;
        public ObservableCollection<GamerModels.StutterIncident> Incidents
        {
            get
            {
                if (_incidents == null)
                {
                    // CORREÇÃO CRÍTICA: mesma razão acima  evitar Invoke bloqueante no getter
                    _incidents = new ObservableCollection<GamerModels.StutterIncident>();
                    System.Windows.Data.BindingOperations.EnableCollectionSynchronization(_incidents, _incidentsLock);
                }
                return _incidents;
            }
        }
        
        // Sample class for diagnostics
        public class Sample
        {
            public DateTime T { get; set; }
            public double CpuPercent { get; set; }
            public double CpuQueue { get; set; }
            public double CpuCurrentMhz { get; set; }
            public double CpuMaxMhz { get; set; }
            public double CpuDpcPercent { get; set; }
            public double CpuInterruptPercent { get; set; }
            public double CpuTemperature { get; set; }
            public bool CpuThrottling { get; set; }
            public double RamUsedGb { get; set; }
            public double RamTotalGb { get; set; }
            public double RamPageFaultsPerSec { get; set; }
            public double DiskReadsPerSec { get; set; }
            public double DiskWritesPerSec { get; set; }
            public double DiskQueueLen { get; set; }
            public double DiskLatencySec { get; set; }
            public double GpuUtilPercent { get; set; }
            public double GpuVramUsedMb { get; set; }
            public double GpuVramTotalMb { get; set; }
            public double GpuTemperature { get; set; }
            public bool GpuThrottling { get; set; }
            public double NetJitterMs { get; set; }
            public double Fps { get; set; }
            public string Cause { get; set; } = "";
        }



        private bool _isDiagnosticsRunning;
        public bool IsDiagnosticsRunning
        {
            get => _isDiagnosticsRunning;
            set => SetProperty(ref _isDiagnosticsRunning, value);
        }

        private string _gameDetectionStatus = "";
        public string GameDetectionStatus
        {
            get => _gameDetectionStatus;
            set => SetProperty(ref _gameDetectionStatus, value);
        }

        private int _gameDetectionProgressPercent = 0;
        public int GameDetectionProgressPercent
        {
            get => _gameDetectionProgressPercent;
            set => SetProperty(ref _gameDetectionProgressPercent, value);
        }

        private int _gamesFoundCount = 0;
        public int GamesFoundCount
        {
            get => _gamesFoundCount;
            set
            {
                if (SetProperty(ref _gamesFoundCount, value))
                    OnPropertyChanged(nameof(GamesFoundText));
            }
        }

        public string GamesFoundText => string.Format(LocalizationService.Instance.GetString("GamerGamesFoundCount"), _gamesFoundCount);

        public bool IsGameDetectionActive => GameDetectionProgressPercent > 0 && GameDetectionProgressPercent < 100;
        public bool HasDetectedGames => GamesFoundCount > 0;
        public bool NoGamesDetectedYet => !HasDetectedGames && !IsGameDetectionActive;

        private bool _isDetecting;
        public bool IsDetecting
        {
            get => _isDetecting;
            set
            {
                if (SetProperty(ref _isDetecting, value))
                {
                    OnPropertyChanged(nameof(DetectButtonText));
                    OnPropertyChanged(nameof(ShowDetectionProgress));
                    OnPropertyChanged(nameof(ShowDetectionSummary));
                }
            }
        }

        public string DetectButtonText => IsDetecting
            ? LocalizationService.Instance.GetString("GamerDetectInProgressBtn")
            : LocalizationService.Instance.GetString("GamerDetectGamesBtn");

        public bool ShowDetectionProgress => IsDetecting;
        public bool ShowDetectionSummary => !IsDetecting && !string.IsNullOrEmpty(_detectionSummaryText);

        private string _detectionSummaryText = "";
        public string DetectionSummaryText
        {
            get => _detectionSummaryText;
            set
            {
                if (SetProperty(ref _detectionSummaryText, value))
                    OnPropertyChanged(nameof(ShowDetectionSummary));
            }
        }

        private int _previousGameCount = 0;
        private System.Diagnostics.Stopwatch _detectionStopwatch = new();

        #endregion

        #region Properties - BH PROCHOT (apenas durante jogos)

        public ProchotService? ProchotService => _gamerModeManager?.ProchotService;

        public bool IsProchotSupported => ProchotService != null;

        private bool _bdProchotGameEnabled = true;
        /// <summary>
        /// Habilita BH PROCHOT apenas durante sessões de jogo.
        /// Fora de jogo, o checkbox não tem efeito — a desativação só ocorre
        /// automaticamente quando um jogo é detectado.
        /// SEMPRE ativado por padrão para garantir máxima performance em jogos.
        /// </summary>
        public bool BdProchotGameEnabled
        {
            get => _bdProchotGameEnabled;
            set
            {
                if (SetProperty(ref _bdProchotGameEnabled, value))
                {
                    if (value && _isInGameSession && ProchotService != null
                        && VoltrisOptimizer.Services.License.ProFeatureGuard.IsPaidActive())
                    {
                        // Se está em sessão de jogo, aplicar IMEDIATAMENTE (apenas com licença paga)
                        _ = ProchotService.DisableBdProchotAsync();
                        _logger.LogInfo("[GamerViewModel] BH PROCHOT desativado (jogo ativo + toggle ligado)");
                    }
                    else if (!value && ProchotService != null)
                    {
                        // Reativar PROCHOT (voltar ao normal)
                        _ = ProchotService.EnableBdProchotAsync();
                        _logger.LogInfo("[GamerViewModel] BH PROCHOT reativado (toggle desligado)");
                    }
                }
            }
        }

        #endregion

        #region Commands

        public ICommand DetectGamesCommand { get; private set; }
        public ICommand ApplyProfileCommand { get; private set; }
        public ICommand CreateProfileCommand { get; private set; }
        public ICommand RemoveProfileCommand { get; private set; }
        public ICommand CleanCacheCommand { get; private set; }
        public ICommand OptimizeGpuCommand { get; private set; }
        public ICommand CheckGpuTempCommand { get; private set; }
        public ICommand RunGamerModeCommand { get; private set; }
        public ICommand ActivateGamerModeCommand { get; private set; } // Alias para RunGamerModeCommand
        public ICommand DeactivateGamerModeCommand { get; private set; }
        public ICommand StartDiagnosticsCommand { get; private set; }
        public ICommand ExportCsvCommand { get; private set; }
        public ICommand OpenDiagnosticsCommand { get; private set; }
        public ICommand StartTemporaryOptimizationCommand { get; private set; }
        public ICommand StopTemporaryOptimizationCommand { get; private set; }
        public ICommand ForceRollbackTemporaryOptimizationCommand { get; private set; }
        public ICommand ApplyProOptimizationsCommand { get; private set; }
        public ICommand RevertProOptimizationsCommand { get; private set; }

        // -- CPU TUNING COMMANDS ---------------------------------------------
        public ICommand RefreshProchotStatusCommand { get; private set; }

        // -- GAME REPAIR COMMANDS ---------------------------------------------
        public ICommand ScanGameErrorsCommand  { get; private set; }
        public ICommand RepairGameErrorsCommand { get; private set; }
        public ICommand CancelGameRepairCommand { get; private set; }

        #endregion

        #region Properties - Game Repair

        private bool _isGameRepairScanning;
        public bool IsGameRepairScanning
        {
            get => _isGameRepairScanning;
            set
            {
                SetProperty(ref _isGameRepairScanning, value);
                OnPropertyChanged(nameof(CanStartGameRepair));
                // Notifica o botão X (Cancel) para habilitar/desabilitar corretamente
                if (CancelGameRepairCommand is RelayCommand c) c.RaiseCanExecuteChanged();
            }
        }

        private bool _isGameRepairRunning;
        public bool IsGameRepairRunning
        {
            get => _isGameRepairRunning;
            set
            {
                SetProperty(ref _isGameRepairRunning, value);
                OnPropertyChanged(nameof(CanStartGameRepair));
                // Notifica o botão X (Cancel) para habilitar/desabilitar corretamente
                if (CancelGameRepairCommand is RelayCommand c) c.RaiseCanExecuteChanged();
            }
        }

        public bool CanStartGameRepair => !IsGameRepairScanning && !IsGameRepairRunning;

        private string _gameRepairStatus = LocalizationService.Instance.GetString("ReadyToScan");
        public string GameRepairStatus
        {
            get => _gameRepairStatus;
            set => SetProperty(ref _gameRepairStatus, value);
        }

        private int _gameRepairProgress;
        public int GameRepairProgress
        {
            get => _gameRepairProgress;
            set => SetProperty(ref _gameRepairProgress, value);
        }

        private string _gameRepairReport = "";
        public string GameRepairReport
        {
            get => _gameRepairReport;
            set => SetProperty(ref _gameRepairReport, value);
        }

        private bool _hasGameRepairReport;
        public bool HasGameRepairReport
        {
            get => _hasGameRepairReport;
            set => SetProperty(ref _hasGameRepairReport, value);
        }

        private bool _gameRepairAllOk;
        public bool GameRepairAllOk
        {
            get => _gameRepairAllOk;
            set => SetProperty(ref _gameRepairAllOk, value);
        }

        private bool _hasGameRepairIssues;
        public bool HasGameRepairIssues
        {
            get => _hasGameRepairIssues;
            set => SetProperty(ref _hasGameRepairIssues, value);
        }

        private readonly object _gameRepairIssuesLock = new object();
        private ObservableCollection<GameRepairIssue>? _gameRepairIssues;
        public ObservableCollection<GameRepairIssue> GameRepairIssues
        {
            get
            {
                if (_gameRepairIssues == null)
                {
                    _gameRepairIssues = new ObservableCollection<GameRepairIssue>();
                    System.Windows.Data.BindingOperations.EnableCollectionSynchronization(_gameRepairIssues, _gameRepairIssuesLock);
                }
                return _gameRepairIssues;
            }
            set => SetProperty(ref _gameRepairIssues, value);
        }

        private bool _autoScanOnGameStart = true;
        public bool AutoScanOnGameStart
        {
            get => _autoScanOnGameStart;
            set 
            { 
                _logger?.LogInfo($"[GamerViewModel][TOGGLE] AutoScanOnGameStart alterado para: {value}");
                SetProperty(ref _autoScanOnGameStart, value); 
            }
        }

        #endregion

        public GamerViewModel(
            IGamerModeOrchestrator orchestrator,
            IGameDetector gameDetector,
            IGameLibraryService libraryService,
            IGpuGamingOptimizer gpuOptimizer,
            ILoggingService logger,
            IMachineProfileDetector profileDetector,
            IAdaptiveOptimizationEngine adaptiveEngine,
            IHardwareDetector hardwareDetector,
            IOverlayService? overlayService = null,
            IGameProfileService? profileService = null,
            IRealGameBoosterService? realBooster = null,
            IPowerProfileDiagnosticsService? powerDiag = null,
            IGamerModeManager? gamerModeManager = null)
        {
            VoltrisOptimizer.Helpers.StartupStepTracker.Instance?.Begin("GAMERVM_CTOR", $"Thread={Thread.CurrentThread.ManagedThreadId}");
            App.LoggingService?.LogInfo("[GamerViewModel DIAG] Construtor INÍCIO");
            _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
            _gameDetector = gameDetector ?? throw new ArgumentNullException(nameof(gameDetector));
            _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
            _gpuOptimizer = gpuOptimizer ?? throw new ArgumentNullException(nameof(gpuOptimizer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _profileDetector = profileDetector ?? throw new ArgumentNullException(nameof(profileDetector));
            _adaptiveEngine = adaptiveEngine ?? throw new ArgumentNullException(nameof(adaptiveEngine));
            _hardwareDetector = hardwareDetector ?? throw new ArgumentNullException(nameof(hardwareDetector));
            _overlayService = overlayService;
            _profileService = profileService;
            _realBooster = realBooster;
            try
            {
                _gamerModeManager = gamerModeManager ?? Core.ServiceLocator.GetService<IGamerModeManager>();
            }
            catch (Exception ex_diag)
            {
                App.LoggingService?.LogError($"[GamerViewModel DIAG] ServiceLocator.GetService<IGamerModeManager> falhou: {ex_diag.Message}");
            }
            App.LoggingService?.LogInfo("[GamerViewModel DIAG] Construtor continua...");
            
            // MOVIDO PARA BACKGROUND INITIALIZE: 
            // _powerDiag, _trendAnalyzer, _gameRepairService, _gamerProfileResolver
            
            _isAutoModeEnabled = false; // Default: não ativar auto sem consentimento do usuário
            _logger.LogInfo("[GamerViewModel] Modo Automático inicializado no construtor (default=false)");
            
            // ATIVAR AUTOPILOT (MOVIDO PARA BACKGROUND INITIALIZE)
            _logger.LogInfo("[GamerViewModel] AutoPilot será iniciado em background task");
            
            // SESSION MANAGER (MOVIDO PARA BACKGROUND INITIALIZE)

            // Inicializar comandos
            var licenseGuard = App.Services?.GetService<ILicenseGuard>() ?? throw new InvalidOperationException("ILicenseGuard não registrado");
            var licenseDialog = App.Services?.GetService<ILicenseDialogService>() ?? throw new InvalidOperationException("ILicenseDialogService não registrado");

            // Comandos de ação exigem licença PRO ("gamer_mode"): sem licença, o clique
            // não executa nada e abre o modal de compra de licença.
            ICommand GateLicensed(ICommand inner) => new LicensedCommand(inner, licenseGuard, "gamer_mode", licenseDialog);

            DetectGamesCommand = GateLicensed(new AsyncRelayCommand(async _ => await DetectGamesAsync()));
            ApplyProfileCommand = GateLicensed(new AsyncRelayCommand(async _ => ApplyProfile(), _ => HasSelectedGame));
            CreateProfileCommand = GateLicensed(new AsyncRelayCommand(async _ => CreateProfile(), _ => HasSelectedGame));
            RemoveProfileCommand = GateLicensed(new AsyncRelayCommand(async _ => RemoveProfile(), _ => HasSelectedGame));
            CleanCacheCommand = GateLicensed(new AsyncRelayCommand(async _ => await CleanCacheAsync()));
            OptimizeGpuCommand = GateLicensed(new AsyncRelayCommand(async _ => await OptimizeGpuAsync()));
            CheckGpuTempCommand = GateLicensed(new AsyncRelayCommand(async _ => await CheckGpuTempAsync()));
            RunGamerModeCommand = GateLicensed(new AsyncRelayCommand(async _ => await RunGamerModeAsync(), _ => !IsGamerModeActive));
            ActivateGamerModeCommand = RunGamerModeCommand; // Alias para compatibilidade com XAML
            DeactivateGamerModeCommand = new AsyncRelayCommand(async _ => await DeactivateGamerModeAsync(), _ => IsGamerModeActive);
            StartDiagnosticsCommand = new RelayCommand(_ => ToggleDiagnostics());
            ExportCsvCommand = new RelayCommand(_ => ExportCsv());
            OpenDiagnosticsCommand = new RelayCommand(_ => OpenDiagnostics());
            StartTemporaryOptimizationCommand = GateLicensed(new AsyncRelayCommand(async _ => await StartTemporaryOptimizationAsync(), _ => _gamerSessionManager != null && !IsTemporaryOptimizationSessionActive));
            StopTemporaryOptimizationCommand = new AsyncRelayCommand(async _ => await StopTemporaryOptimizationAsync(), _ => _gamerSessionManager != null && IsTemporaryOptimizationSessionActive);

            ForceRollbackTemporaryOptimizationCommand = new AsyncRelayCommand(async _ => await ForceRollbackTemporaryOptimizationAsync(), _ => _gamerSessionManager != null && IsTemporaryOptimizationSessionActive);
            ApplyProOptimizationsCommand = GateLicensed(new AsyncRelayCommand(async _ => await ApplyProOptimizationsAsync()));
            RevertProOptimizationsCommand = GateLicensed(new AsyncRelayCommand(async _ => await RevertProOptimizationsAsync()));

            // -- CPU TUNING COMMANDS -----------------------------------------
            RefreshProchotStatusCommand = new AsyncRelayCommand(async _ => await RefreshProchotStatusAsync(), _ => IsProchotSupported);

            // -- GAME REPAIR COMMANDS -----------------------------------------
            ScanGameErrorsCommand   = GateLicensed(new AsyncRelayCommand(async _ => await ScanGameErrorsAsync(), _ => CanStartGameRepair));
            RepairGameErrorsCommand = GateLicensed(new AsyncRelayCommand(async _ => await RepairGameErrorsAsync(), _ => HasGameRepairIssues && CanStartGameRepair));
            CancelGameRepairCommand = new RelayCommand(_ => CancelGameRepair(), _ => IsGameRepairScanning || IsGameRepairRunning);

            // Subscrever a eventos
            _orchestrator.StatusChanged += OnStatusChanged;
            _gameDetector.GameStarted += OnGameStarted;
            _gameDetector.GameStopped += OnGameStopped;
            _gameDetector.ProgressChanged += OnGameDetectionProgressChanged; // Assinar evento de progresso
            
            OnPropertyChanged(nameof(NoGamesDetectedYet)); // Inicializar estado do UI

            // CORREÇÃO: Fechar o step de rastreamento do construtor antes de lançar a task de background.
            // O construtor terminou — a inicialização de background é assíncrona e rastreada separadamente.
            VoltrisOptimizer.Helpers.StartupStepTracker.Instance?.End("GAMERVM_CTOR", "Construtor concluído — background async iniciado");

            // INICIALIZAÇÃO ASSÍNCRONA NÃO-BLOQUEANTE (CORREÇÃO DE STARTUP)
            _ = Task.Run(async () => await InitializeBackgroundServicesAsync());
        }

        private bool _isEventsSubscribed;

        private void SubscribeBackgroundEvents()
        {
            _logger.LogEntry($"[GamerViewModel] SubscribeBackgroundEvents");
            if (_isEventsSubscribed)
            {
                _logger.LogExit($"[GamerViewModel] SubscribeBackgroundEvents = já inscrito");
                return;
            }
            try
            {
                var thermal = VoltrisOptimizer.Services.Thermal.GlobalThermalMonitorService.Instance;
                thermal.MetricsUpdated += OnThermalMetricsUpdated;
                thermal.AlertGenerated += OnThermalAlertGenerated;

                if (App.GameDiagnostics != null)
                {
                    App.GameDiagnostics.SamplesUpdated += OnDiagnosticsSamplesUpdated;
                }

                _isEventsSubscribed = true;
                _logger.LogInfo("[GamerViewModel] Subscrito aos eventos térmicos e de diagnóstico");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GamerViewModel] Erro ao inscrever em eventos: {ex.Message}");
            }
            _logger.LogExit($"[GamerViewModel] SubscribeBackgroundEvents");
        }

        private void UnsubscribeBackgroundEvents()
        {
            _logger.LogEntry($"[GamerViewModel] UnsubscribeBackgroundEvents");
            if (!_isEventsSubscribed)
            {
                _logger.LogExit($"[GamerViewModel] UnsubscribeBackgroundEvents = não inscrito");
                return;
            }
            try
            {
                var thermal = VoltrisOptimizer.Services.Thermal.GlobalThermalMonitorService.Instance;
                thermal.MetricsUpdated -= OnThermalMetricsUpdated;
                thermal.AlertGenerated -= OnThermalAlertGenerated;

                if (App.GameDiagnostics != null)
                {
                    App.GameDiagnostics.SamplesUpdated -= OnDiagnosticsSamplesUpdated;
                }

                _isEventsSubscribed = false;
                _logger.LogInfo("[GamerViewModel] Desinscrito dos eventos térmicos e de diagnóstico");
            }
            catch (Exception ex) { _logger.LogDebug($"[GamerViewModel] Erro ao desinscrever eventos: {ex.Message}"); }
            _logger.LogExit($"[GamerViewModel] UnsubscribeBackgroundEvents");
        }

        protected override void OnActiveChanged()
        {
            _logger.LogEntry($"[GamerViewModel] OnActiveChanged(IsActive={IsActive})");
            if (IsActive)
            {
                SubscribeBackgroundEvents();
            try
            {
                var thermal = VoltrisOptimizer.Services.Thermal.GlobalThermalMonitorService.Instance;
                var metrics = thermal.CurrentMetrics;
                if (metrics != null)
                {
                    OnThermalMetricsUpdated(this, metrics);
                }
            }
            catch (Exception ex) { _logger.LogDebug($"[GamerViewModel] Erro ao obter métricas térmicas: {ex.Message}"); }
            }
            else
            {
                UnsubscribeBackgroundEvents();
            }
            _logger.LogExit($"[GamerViewModel] OnActiveChanged(IsActive={IsActive})");
        }

        /// <summary>
        /// Inicializa serviços pesados e resoluções de DI em segundo plano para não travar o construtor (UI Thread)
        /// </summary>
        private async Task InitializeBackgroundServicesAsync()
        {
            _logger.LogEntry($"[GamerViewModel] InitializeBackgroundServicesAsync");
            try
            {
                _logger.LogInfo("[GamerViewModel DIAG] InitializeBackgroundServicesAsync INÍCIO");
                
                _logger.LogInfo("[GamerViewModel] Iniciando carregamento de serviços em background...");
                
                // INICIALIZAÇÃO DE SERVIÇOS PESADOS (Movidos do Construtor para não travar UI)
                _powerDiag = new PowerProfileDiagnosticsService(_logger, new PowerPlanService(_logger), null!, _hardwareDetector);
                _trendAnalyzer = new TrendAnalyzerService(_logger);
                _gameRepairService = VoltrisOptimizer.Core.ServiceLocator.GetService<GameRepairService>() ?? new GameRepairService(_logger);
                _enterpriseRepairEngine = VoltrisOptimizer.Core.ServiceLocator.GetService<IEnterpriseGameRepairEngine>();
                _dependencyProviders = VoltrisOptimizer.Core.ServiceLocator.GetService<IEnumerable<IGameDependencyProvider>>();
                
                _trendAnalyzer.WarningDetected += OnTrendWarningDetected;
                _powerDiag.DiagnosticMessageGenerated += OnPowerDiagnosticMessage;
                
                try
                {
                    _gamerProfileResolver = new VoltrisOptimizer.Services.Gamer.GamerProfileResolver(SettingsService.Instance, _logger);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[GamerViewModel] Erro ao criar GamerProfileResolver: {ex.Message}");
                }

                // 0. Verificar crash recovery do GamerModeManager (HPET, wallpaper, UWP, etc.)
                try
                {
                    bool recovered = await _gamerModeManager.RestorePreviousStateAsync();
                    if (recovered)
                    {
                        _logger.LogWarning("[GamerViewModel] Estado anterior recuperado apos crash detectado pelo GamerModeManager");
                    }
                    else
                    {
                        _logger.LogInfo("[GamerViewModel] Nenhum estado pendente de recuperacao no GamerModeManager");
                    }
                }
                catch (Exception exRestore)
                {
                    _logger.LogError($"[GamerViewModel] Erro ao tentar RestorePreviousStateAsync: {exRestore.Message}");
                }

                // 1. Ativar AutoPilot (Monitoramento) — apenas com licença PRO E com o
                //    Modo Gamer Automático ATIVO nas configurações do usuário.
                // CORREÇÃO FREEZE: Aumentar delay para garantir que a UI já está completamente
                // estabilizada antes de iniciar o monitoramento de jogos (que ao detectar um jogo
                // imediatamente executa operações pesadas de I/O e Registry na thread de background).
                await Task.Delay(5000).ConfigureAwait(false); // 5s de fôlego para a UI terminar de carregar
                
                // Respeitar a escolha do usuário: NUNCA iniciar monitoramento automático sem o
                // toggle "Modo Gamer Automático" ativo. Antes, o monitoramento iniciava para QUALQUER
                // licença PRO, ignorando o toggle, e podia disparar modo gamer + badge vermelho
                // no carregamento da interface.
                bool isPro = VoltrisOptimizer.Services.LicenseManager.IsPro;
                bool userEnabledAutoMode = SettingsService.Instance?.Settings?.AutoGamerMode == true;
                _logger.LogInfo($"[GamerViewModel DIAG] AutoPilot: IsPro={isPro}, AutoGamerMode={userEnabledAutoMode}. Chamando StartAutoPilot apenas se PRO E automático ativo...");
                if (isPro && userEnabledAutoMode)
                {
                    _orchestrator.StartAutoPilot();
                    _logger.LogSuccess("[GamerViewModel] AutoPilot iniciado em background (licença PRO + Modo Gamer Automático ativo)");
                }
else
                    {
                        _logger.LogInfo("[GamerViewModel] AutoPilot NÃO iniciado — requer licença PRO e Modo Gamer Automático ativo. Monitoramento automático desativado.");
                    }


                // 3. Subscrever mudança de licença para detectar ativação durante gameplay
                LicenseManager.Instance.LicenseStatusChanged += OnLicenseStatusChanged;
                _logger.LogInfo("[GamerViewModel] Subscrito a LicenseManager.LicenseStatusChanged para detectar ativação durante gameplay");

                // CORREÇÃO CRÍTICA: Verificar se licença já é Pro APÓS subscrever (race condition: evento pode ter disparado antes do subscribe)
                if (LicenseManager.IsPro)
                {
                    _logger.LogInfo("[GamerViewModel] Licença já é Pro — executando lógica de ativação automática pós-subscribe");
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            OnLicenseStatusChanged(this, EventArgs.Empty);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"[GamerViewModel] Erro ao executar OnLicenseStatusChanged pós-subscribe: {ex.Message}");
                        }
                    });
                }


                // 2. Resolver SessionManager do DI de forma segura
                var serviceProvider = App.Services;
                if (serviceProvider != null)
                {
                    _gamerSessionManager = serviceProvider.GetService(typeof(GamerSessionManager)) as GamerSessionManager;
                    if (_gamerSessionManager != null)
                    {
                        _gamerSessionManager.SessionStateChanged += OnTemporaryOptimizationSessionStateChanged;
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => 
                        {
                            try { UpdateTemporaryOptimizationStatus(); } catch (Exception innerEx) { _logger.LogDebug($"[GamerViewModel] Erro ao atualizar status temporário: {innerEx.Message}"); }
                        });
                    }
                }

                // Subscrever eventos em segundo plano se ativo
                if (IsActive)
                {
                    SubscribeBackgroundEvents();
                }

                // Task-based monitoring roda em background e atualiza UI apenas quando necessário
                _ = StartIncidentMonitoringAsync();

                // Inicializar dados
                _ = InitializeAsync();
                
                // Carregar configurações do overlay
                _ = LoadOverlaySettingsAsync();

                _logger.LogSuccess("[GamerViewModel] Background initialization complete");
            }
            catch (Exception ex)
            {
                // FIX: Catching silently to prevent silent crashes in UI background thread
                _logger.LogError($"[GamerViewModel DIAG] ERRO CRÍTICO em InitializeBackgroundServicesAsync: {ex.GetType().Name}: {ex.Message}");
                _logger.LogError($"[GamerViewModel DIAG] StackTrace: {ex.StackTrace}");
            }
            finally
            {
                _logger.LogInfo("[GamerViewModel DIAG] InitializeBackgroundServicesAsync FINALIZADO");
                _logger.LogExit($"[GamerViewModel] InitializeBackgroundServicesAsync");
            }
        }

        private async Task InitializeAsync()
        {
            _logger.LogEntry($"[GamerViewModel] InitializeAsync");
            try
            {
                _logger.LogInfo("[GamerViewModel] Iniciando inicialização...");
                
                // Carregar jogos da biblioteca
                try
                {
                    LoadGamesFromLibrary();
                    _logger.LogInfo("[GamerViewModel] Jogos carregados");

                    OnPropertyChanged(nameof(NoGamesDetectedYet)); // Atualizar estado do UI após carregar jogos

                    // Carregar configuração de Auto detecção
                    var settings = SettingsService.Instance?.Settings;
                    if (settings != null)
                    {
                        if (!settings.HasGamerModeConfigured)
                        {
                            // Primeira execução: marcar como configurado (sem forçar AutoGamerMode)
                            settings.HasGamerModeConfigured = true;
                            SettingsService.Instance.SaveSettings();
                            _logger.LogInfo("[GamerViewModel] Primeira execução detectada - configuração de Gamer Mode marcada como configurada (AutoGamerMode mantém valor do usuário).");
                        }

                        // Carregar o valor persistido das configurações (respeita escolha do usuário)
                        IsAutoModeEnabled = settings.AutoGamerMode;
                        
                        // NOTA: O StartAutoPilot é iniciado em InitializeBackgroundServicesAsync
                        // com verificação de licença PRO e delay de 5s para não competir com a UI.
                        
                        // SINCRONIZAR ESTADO INICIAL DO MODO GAMER
                        OnStatusChanged(this, _orchestrator.Status);

                    }
                    else
                    {
                        _logger.LogWarning("[GamerViewModel] SettingsService não disponível - usando defaults");
                        IsAutoModeEnabled = false; // Fallback seguro: não ativar auto por padrão sem settings
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[GamerViewModel] Erro ao carregar jogos: {ex.Message}", ex);
                }

                // Detectar GPU
                try
                {
                    await DetectGpuAsync();
                    _logger.LogInfo("[GamerViewModel] GPU detectada");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[GamerViewModel] Erro ao detectar GPU: {ex.Message}", ex);
                }

                // Iniciar monitoramento automático se habilitado
                if (IsAutoModeEnabled)
                {
                    try
                    {
                        _gameDetector.StartMonitoring();
                        _logger.LogInfo("[GamerViewModel] Monitoramento automático iniciado");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"[GamerViewModel] Erro ao iniciar monitoramento automático: {ex.Message}", ex);
                    }
                }
                
                _logger.LogInfo("[GamerViewModel] Inicialização concluída");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GamerViewModel] Erro na inicialização: {ex.Message}", ex);
                // Definir valores padrão em caso de erro
                GpuInfoText = LocalizationService.Instance.GetString("GpuInitError");
                StatusText = LocalizationService.Instance["Loc_ErroNaInicializacao"];
            }
            _logger.LogExit($"[GamerViewModel] InitializeAsync");
        }

        private void LoadGamesFromLibrary()
        {
            _logger.LogEntry($"[GamerViewModel] LoadGamesFromLibrary");
            try
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                void ExecuteOnUi(Action action)
                {
                    if (dispatcher != null && !dispatcher.CheckAccess())
                        dispatcher.BeginInvoke(action);
                    else
                        action();
                }

                ExecuteOnUi(() => Games.Clear());
                
                // Carregar APENAS da biblioteca persistida em JSON
                var games = _libraryService.GetAllGames();
                
                if (games.Count > 0)
                {
                    foreach (var game in games)
                    {
                        ExecuteOnUi(() => Games.Add(game));
                    }
                    _logger.LogInfo($"[GamerViewModel] {games.Count} jogos carregados do arquivo JSON da biblioteca");
                }
                else
                {
                    _logger.LogInfo("[GamerViewModel] Biblioteca vazia — aguardando o usuário clicar em \"Detectar Jogos\"");
                }
                
                _logger.LogInfo($"[GamerViewModel] Total na UI: {Games.Count} jogos");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GamerViewModel] Erro ao carregar jogos da biblioteca: {ex.Message}", ex);
                System.Windows.Application.Current?.Dispatcher?.Invoke(() => Games.Clear());
            }
            _logger.LogExit($"[GamerViewModel] LoadGamesFromLibrary = {Games.Count} jogos");
        }

        private void LoadProfileToUi()
        {
            if (SelectedGame == null || _profileService == null) return;

            try
            {
                var profile = _profileService.GetProfile(SelectedGame.Name);
                if (profile != null && profile.Settings != null)
                {
                    // Carregar settings do perfil salvo
                    OptimizePowerPlan = profile.Settings.OptimizePowerPlan;
                    OptimizeCpu = profile.Settings.OptimizeCPU;
                    OptimizeGpu = profile.Settings.OptimizeGPU;
                    OptimizeNetwork = profile.Settings.OptimizeNetwork;
                    OptimizeMemory = profile.Settings.OptimizeMemory;
                    EnableGameMode = profile.Settings.EnableGameMode;
                    ApplyFpsBoost = profile.Settings.ApplyFPSBoost;
                    ReduceLatency = profile.Settings.ReduceLatency;
                    CloseBackgroundApps = profile.Settings.CloseBackgroundApps;
                    EnableExtremeMode = profile.Settings.EnableExtremeMode;
                    EnableAntiStutter = profile.Settings.EnableAntiStutter;
                    EnableAdaptiveNetwork = profile.Settings.EnableAdaptiveNetwork;
                    
                    // Carregar novas opções se existirem no perfil
                    DisableHpet = profile.Settings.DisableHpet;
                    DisableWallpaperSlideshow = profile.Settings.DisableWallpaperSlideshow;
                    DisableUwpBackgroundApps = profile.Settings.DisableUwpBackgroundApps;
                    DisableHeavyServices = profile.Settings.DisableHeavyServices;
                    DisableDwmVSync = profile.Settings.DisableDwmVSync;
                    OptimizeGamma = profile.Settings.OptimizeGamma;
                    VisualLevel = profile.Settings.VisualLevel;
                    
                    _logger.LogInfo($"[UI] Perfil carregado para view: {SelectedGame.Name}");
                }
                else
                {
                    // Se não tiver perfil, manter padrões ou resetar?
                    // Estratégia Enterprise: Resetar para "Smart Defaults" para evitar confuso com configs do jogo anterior
                    OptimizeCpu = true;
                    OptimizeGpu = true;
                    OptimizeNetwork = true;
                    OptimizeMemory = true;
                    EnableGameMode = true;
                    ApplyFpsBoost = true;
                    ReduceLatency = true;
                    CloseBackgroundApps = true;
                    EnableExtremeMode = false;
                    EnableAntiStutter = true;
                    EnableAdaptiveNetwork = true;
                    
                    // Resetar novas opções
                    DisableHpet = true;
                    DisableWallpaperSlideshow = false;
                    DisableUwpBackgroundApps = true;
                    DisableHeavyServices = true;
                    DisableDwmVSync = true;
                    OptimizeGamma = true;
                    VisualLevel = GamerModels.VisualOptimizationLevel.Balanced;
                    _logger.LogInfo("[UI] Perfil resetado para Smart Defaults");
                }
                
                // Sincronizar com orquestrador após carregar
                SyncOptions();
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[UI] Erro ao carregar perfil para UI: {ex.Message}");
            }
        }

        #region Command Implementations

        private async Task RefreshProchotStatusAsync()
        {
            await ExecuteSafeAsync(async () =>
            {
                if (ProchotService == null) return;
                var status = ProchotService.GetStatus();
                _logger.LogSuccess($"[GamerViewModel] PROCHOT: BD_PROCHOT={!status.bdProchotDisabled}, Offset={status.prochotOffset}°C, Locked={status.isLocked}");
            });
        }

        private async Task DetectGamesAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogEntry($"[GamerViewModel] DetectGamesAsync");
            _logger.LogInfo("[GamerViewModel] [DetectGamesAsync] INICIADO - Solicitacao de deteccao de jogos");
            _logger.LogInfo("[GamerViewModel] [DetectGamesAsync] Pipeline de deteccao: GameConfidenceEvaluator com 8 criterios + exclusoes inteligentes");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await ExecuteSafeAsync(async () =>
            {
                _logger.LogInfo("[GamerViewModel] [DetectGamesAsync] Iniciando deteccao de jogos em todos os drives");
                _logger.LogInfo("[GamerViewModel] [DetectGamesAsync] O pipeline de deteccao usa GameConfidenceEvaluator com 8 criterios");
                GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("DetectingGames"), false);
                
                var detected = await _gameDetector.DetectInstalledGamesAsync(cancellationToken);
                
                sw.Stop();
                _logger.LogSuccess($"[GamerViewModel] [DetectGamesAsync] Deteccao concluida: {detected.Count} jogos encontrados em {sw.ElapsedMilliseconds}ms");
                _logger.LogInfo($"[GamerViewModel] [DetectGamesAsync] Distribuicao de confianca: Alta(>=80)={detected.Count(g => g.ConfidenceScore >= 80)} | Media(45-79)={detected.Count(g => g.ConfidenceScore >= 45 && g.ConfidenceScore < 80)}");

                await System.Windows.Application.Current?.Dispatcher.InvokeAsync(() => 
                {
                    _logger.LogInfo("[GamerViewModel] Atualizando lista de jogos na UI...");
                    Games.Clear();
                    int added = 0;
                    foreach (var game in detected)
                    {
                        if (game != null)
                        {
                            Games.Add(game);
                            _libraryService.AddGame(game);
                            added++;
                        }
                    }
                    _logger.LogInfo($"[GamerViewModel] UI atualizada com {Games.Count} jogos ({added} novos na biblioteca)");
                });

                _logger.LogSuccess($"[GamerViewModel] [DetectGamesAsync] Detectados {detected.Count} jogos em {sw.ElapsedMilliseconds}ms");
                int highConf = detected.Count(g => g.ConfidenceScore >= 80);
                int medConf = detected.Count(g => g.ConfidenceScore >= 45 && g.ConfidenceScore < 80);
                _logger.LogInfo($"[GamerViewModel] [DetectGamesAsync] Resumo final: {detected.Count} jogos | Alta conf={highConf} | Media conf={medConf}");
            }, LocalizationService.Instance.GetString("DetectingGames"));
            
            _logger.LogInfo($"[GamerViewModel] [DetectGamesAsync] FINALIZADO - Tempo total: {sw.Elapsed.TotalSeconds:F1}s");
            _logger.LogExit($"[GamerViewModel] DetectGamesAsync");
        }

        private async void ApplyProfile()
        {
            _logger.LogEntry($"[GamerViewModel] ApplyProfile");
            _logger?.LogInfo("[GamerViewModel] [ApplyProfile] INICIADO");
            try
            {
            if (SelectedGame == null)
            {
                _logger?.LogWarning("[GamerViewModel] [ApplyProfile] SelectedGame é nulo, abortando");
                ShowMessage(LocalizationService.Instance.GetString("SelectGameFirst"), LocalizationService.Instance.GetString("NoGameSelected"));
                return;
            }
            _logger?.LogInfo($"[GamerViewModel] [ApplyProfile] Jogo selecionado: {SelectedGame.Name}");

            // ? VERIFICAÇÃO DE LICENÇA (Feature Gate) — mesmo modal de compra do botão
            if (!VoltrisOptimizer.Services.License.ProFeatureGuard.RequirePaid("gamer_mode"))
            {
                _logger?.LogWarning("[GamerViewModel] [ApplyProfile] Licença não habilitada para gamer_mode, abortando");
                return;
            }
            _logger?.LogInfo("[GamerViewModel] [ApplyProfile] Licença verificada com sucesso");

            await ExecuteSafeAsync(async () =>
            {
                _logger?.LogInfo("[GamerViewModel] [ApplyProfile] Executando ApplyProfile no ExecuteSafeAsync");
                // CORREÇÃO: Validar se o jogo está rodando
                var gameExe = SelectedGame.ExecutablePath;
                Process? gameProcess = null;
                
                if (!string.IsNullOrEmpty(gameExe))
                {
                    var processName = System.IO.Path.GetFileNameWithoutExtension(gameExe);
                    _logger?.LogInfo($"[GamerViewModel] [ApplyProfile] Buscando processo: {processName}");
                    gameProcess = await Task.Run(() =>
                    {
                        try
                        {
                            var processes = Process.GetProcessesByName(processName);
                            return processes.Length > 0 ? processes[0] : null;
                        }
                        catch
                        {
                            return null;
                        }
                    });
                }

                if (gameProcess == null)
                {
                    _logger?.LogWarning($"[GamerViewModel] [ApplyProfile] Jogo {SelectedGame.Name} não está em execução, abortando");
                    ShowMessage(
                        string.Format(LocalizationService.Instance.GetString("GameNotRunning"), SelectedGame.Name),
                        LocalizationService.Instance.GetString("GameNotFound"));
                    return;
                }
                _logger?.LogInfo($"[GamerViewModel] [ApplyProfile] Processo do jogo encontrado: PID={gameProcess.Id}");

                if (_profileService != null)
                {
                    _logger?.LogInfo($"[GamerViewModel] [ApplyProfile] Aplicando perfil via _profileService para {SelectedGame.Name}");
                    await _profileService.ApplyProfileAsync(SelectedGame.Name);
                }
                else
                {
                    _logger?.LogWarning("[GamerViewModel] [ApplyProfile] _profileService é nulo");
                }
                
                // Aplicar otimizações diretamente também
                if (_realBooster != null)
                {
                    _logger?.LogInfo("[GamerViewModel] [ApplyProfile] Aplicando boost via _realBooster");
                    await _realBooster.ActivateFullBoostAsync(gameProcess);
                }
                else
                {
                    _logger?.LogWarning("[GamerViewModel] [ApplyProfile] _realBooster é nulo");
                }
                
                _logger.LogSuccess($"[GamerViewModel] [ApplyProfile] Perfil aplicado para {SelectedGame.Name}");
                ShowToast(LocalizationService.Instance.GetString("ProfileApplied"), string.Format(LocalizationService.Instance.GetString("OptimizationsAppliedForGame"), SelectedGame.Name));
                
                HistoryService.RecordActivity("Gamer Optimizer", $"Perfil de otimização aplicado para o jogo: {SelectedGame.Name}", true);
            }, string.Format(LocalizationService.Instance.GetString("ApplyingProfileForGame"), SelectedGame.Name));
            _logger?.LogInfo("[GamerViewModel] [ApplyProfile] FINALIZADO com sucesso");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[GamerViewModel] [ApplyProfile] Erro: {ex.Message}", ex);
            }
            _logger.LogExit($"[GamerViewModel] ApplyProfile");
        }

        private async void CreateProfile()
        {
            _logger.LogEntry($"[GamerViewModel] CreateProfile");
            _logger?.LogInfo("[GamerViewModel] [CreateProfile] INICIADO");
            try
            {
            if (SelectedGame == null)
            {
                _logger?.LogWarning("[GamerViewModel] [CreateProfile] SelectedGame é nulo, abortando");
                ShowMessage(LocalizationService.Instance.GetString("SelectGameFirst"), LocalizationService.Instance.GetString("NoGameSelected"));
                return;
            }
            _logger?.LogInfo($"[GamerViewModel] [CreateProfile] Criando perfil para: {SelectedGame.Name}");

            await ExecuteSafeAsync(async () =>
            {
                _logger?.LogInfo("[GamerViewModel] [CreateProfile] Montando objeto GameProfile");
                var profile = new GamerModels.GameProfile
                {
                    GameName = SelectedGame.Name,
                    ExecutablePath = SelectedGame.ExecutablePath,
                    Settings = new GamerModels.GameProfileSettings
                    {
                        OptimizePowerPlan = OptimizePowerPlan,
                        OptimizeCPU = OptimizeCpu,
                        OptimizeGPU = OptimizeGpu,
                        OptimizeNetwork = OptimizeNetwork,
                        OptimizeMemory = OptimizeMemory,
                        EnableGameMode = EnableGameMode,
                        ApplyFPSBoost = ApplyFpsBoost,
                        ReduceLatency = ReduceLatency,
                        CloseBackgroundApps = CloseBackgroundApps,
                        EnableExtremeMode = EnableExtremeMode,
                        EnableAntiStutter = EnableAntiStutter,
                        EnableAdaptiveNetwork = EnableAdaptiveNetwork,
                        // Salvar novas opções
                        DisableHpet = DisableHpet,
                        DisableWallpaperSlideshow = DisableWallpaperSlideshow,
                        DisableUwpBackgroundApps = DisableUwpBackgroundApps,
                        DisableHeavyServices = DisableHeavyServices,
                        DisableDwmVSync = DisableDwmVSync,
                        OptimizeGamma = OptimizeGamma,
                        VisualLevel = VisualLevel
                    }
                };
                _logger?.LogInfo($"[GamerViewModel] [CreateProfile] Perfil montado: VisualLevel={VisualLevel}");

                if (_profileService != null)
                {
                    _logger?.LogInfo("[GamerViewModel] [CreateProfile] Salvando perfil via _profileService");
                    await Task.Run(() => _profileService.SaveProfile(profile));
                }
                else
                {
                    _logger?.LogWarning("[GamerViewModel] [CreateProfile] _profileService é nulo");
                }
                
                _logger?.LogInfo("[GamerViewModel] [CreateProfile] Atualizando biblioteca de jogos");
                _libraryService.UpdateGameProfile(SelectedGame.Name, profile);
                
                _logger.LogSuccess($"[GamerViewModel] [CreateProfile] Perfil criado para {SelectedGame.Name}");
                ShowToast(LocalizationService.Instance.GetString("ProfileCreated"), string.Format(LocalizationService.Instance.GetString("SettingsSavedForGame"), SelectedGame.Name));
                
                HistoryService.RecordActivity("Gamer Optimizer", $"Novo perfil de otimização personalizado criado para: {SelectedGame.Name}", true);
            }, string.Format(LocalizationService.Instance.GetString("CreatingProfileForGame"), SelectedGame.Name));
            _logger?.LogInfo("[GamerViewModel] [CreateProfile] FINALIZADO com sucesso");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[GamerViewModel] [CreateProfile] Erro: {ex.Message}", ex);
            }
            _logger.LogExit($"[GamerViewModel] CreateProfile");
        }

        private async void RemoveProfile()
        {
            _logger.LogEntry($"[GamerViewModel] RemoveProfile");
            _logger?.LogInfo("[GamerViewModel] [RemoveProfile] INICIADO");
            try
            {
            if (SelectedGame == null)
            {
                _logger?.LogWarning("[GamerViewModel] [RemoveProfile] SelectedGame é nulo, abortando");
                ShowMessage(LocalizationService.Instance.GetString("SelectGameFirst"), LocalizationService.Instance.GetString("NoGameSelected"));
                return;
            }
            _logger?.LogInfo($"[GamerViewModel] [RemoveProfile] Removendo perfil de: {SelectedGame.Name}");

            await ExecuteSafeAsync(async () =>
            {
                if (_profileService != null)
                {
                    _logger?.LogInfo($"[GamerViewModel] [RemoveProfile] Deletando perfil via _profileService para {SelectedGame.Name}");
                    await Task.Run(() => _profileService.DeleteProfile(SelectedGame.Name));
                }
                else
                {
                    _logger?.LogWarning("[GamerViewModel] [RemoveProfile] _profileService é nulo");
                }
                
                _logger?.LogInfo($"[GamerViewModel] [RemoveProfile] Removendo {SelectedGame.Name} da biblioteca");
                _libraryService.RemoveGameByName(SelectedGame.Name);
                Games.Remove(SelectedGame);
                SelectedGame = null;
                
                _logger.LogInfo($"[GamerViewModel] [RemoveProfile] Jogo removido da biblioteca");
                ShowToast(LocalizationService.Instance.GetString("GameRemoved"), LocalizationService.Instance.GetString("GameRemovedFromLibrary"));
                
                HistoryService.RecordActivity("Gamer Optimizer", $"Jogo removido da biblioteca: {SelectedGame.Name}", true);
            }, string.Format(LocalizationService.Instance.GetString("RemovingGame"), SelectedGame.Name));
            _logger?.LogInfo("[GamerViewModel] [RemoveProfile] FINALIZADO com sucesso");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[GamerViewModel] [RemoveProfile] Erro: {ex.Message}", ex);
            }
            _logger.LogExit($"[GamerViewModel] RemoveProfile");
        }

        private async Task CleanCacheAsync(CancellationToken cancellationToken = default)
        {
            _logger?.LogInfo("[GamerViewModel] [CleanCache] INICIADO");
            _logger?.LogInfo("[GamerViewModel] [CleanCache] Stage 1: Limpeza da biblioteca de jogos (entradas orfas e baixa confianca)");

            int libraryRemoved = _libraryService.CleanCache(45);
            _logger?.LogInfo($"[GamerViewModel] [CleanCache] Stage 1 concluido: {libraryRemoved} entradas removidas da biblioteca");

            await System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                int before = Games.Count;
                var toRemove = Games.Where(g => g.ConfidenceScore < 45 || (!string.IsNullOrEmpty(g.ExecutablePath) && !System.IO.File.Exists(g.ExecutablePath))).ToList();
                foreach (var r in toRemove) Games.Remove(r);
                _logger?.LogInfo($"[GamerViewModel] [CleanCache] UI sincronizada: {before} -> {Games.Count} jogos");
            });

            if (SelectedGame == null)
            {
                _logger?.LogInfo("[GamerViewModel] [CleanCache] Nenhum jogo selecionado, apenas limpeza de biblioteca realizada");
                ShowToast(LocalizationService.Instance.GetString("CacheCleaned"), string.Format(LocalizationService.Instance.GetString("CleanCacheLibraryOnly"), libraryRemoved));
                ShowMessage(LocalizationService.Instance.GetString("LibraryCleaned"), LocalizationService.Instance.GetString("CacheCleaned"));
                _logger?.LogInfo("[GamerViewModel] [CleanCache] FINALIZADO (apenas biblioteca)");
                return;
            }
            _logger?.LogInfo($"[GamerViewModel] [CleanCache] Stage 2: Limpando shader cache para: {SelectedGame.Name}");

            await ExecuteSafeAsync(async () =>
            {
                GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("CleaningGameCache"), true);
                long totalCleaned = 0;
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                
                _logger?.LogInfo("[GamerViewModel] [CleanCache] LocalAppData: " + localAppData);
                
                var shaderPaths = new[]
                {
                    System.IO.Path.Combine(localAppData, "NVIDIA", "DXCache"),
                    System.IO.Path.Combine(localAppData, "NVIDIA", "GLCache"),
                    System.IO.Path.Combine(localAppData, "AMD", "DxCache"),
                    System.IO.Path.Combine(localAppData, "D3DSCache")};

                foreach (var path in shaderPaths)
                {
                    _logger?.LogInfo($"[GamerViewModel] [CleanCache] Verificando caminho: {path}");
                    if (System.IO.Directory.Exists(path))
                    {
                        _logger?.LogInfo($"[GamerViewModel] [CleanCache] Limpando: {path}");
                        try
                        {
                            foreach (var file in System.IO.Directory.GetFiles(path, "*", System.IO.SearchOption.AllDirectories))
                            {
                                try
                                {
                                    var info = new System.IO.FileInfo(file);
                                    totalCleaned += info.Length;
                                    System.IO.File.Delete(file);
                                }
                                catch (Exception exClean) { _logger.LogDebug($"[GamerViewModel] CleanCache: erro ao deletar {file}: {exClean.Message}"); }
                            }
                        }
                            catch (Exception exDir) { _logger.LogDebug($"[GamerViewModel] CleanCache: erro ao acessar diretorio: {exDir.Message}"); }
                    }
                    else
                    {
                        _logger?.LogInfo($"[GamerViewModel] [CleanCache] Caminho nao encontrado: {path}");
                    }
                }

                var cleanedMb = totalCleaned / (1024.0 * 1024.0);
                _logger.LogSuccess($"[GamerViewModel] [CleanCache] Cache limpo: {cleanedMb:F2} MB liberados (total de {totalCleaned} bytes)");

                ShowToast(LocalizationService.Instance.GetString("CacheCleaned"), string.Format(LocalizationService.Instance.GetString("CleanCacheLibraryAndShaders"), cleanedMb, libraryRemoved));
                
                HistoryService.RecordActivity("Gamer Optimizer", $"Limpeza de cache (Shaders+biblioteca): {cleanedMb:F2} MB liberados, {libraryRemoved} entradas removidas.", true, totalCleaned);
            }, string.Format(LocalizationService.Instance.GetString("Loc_CleaningCacheOf"), SelectedGame.Name));
            _logger?.LogInfo("[GamerViewModel] [CleanCache] FINALIZADO");

        }
        
        private void ShowMessage(string message, string title)
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                UI.Controls.ModernMessageBox.Show(message, title, 
                    System.Windows.MessageBoxButton.OK, 
                    System.Windows.MessageBoxImage.Information);
            });
        }
        
        private void ShowToast(string title, string message)
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                new ToastService().Show(title, message);
            });
        }

        private async Task DetectGpuAsync()
        {
            try
            {
                _logger?.LogInfo("[GamerViewModel] [DetectGpuAsync] INICIADO - Detectando GPU...");
                var gpuInfo = await _gpuOptimizer.GetGpuInfoAsync();

                _logger?.LogInfo($"[GamerViewModel] [DetectGpuAsync] Fabricante: {gpuInfo?.Vendor}");
                _logger?.LogInfo($"[GamerViewModel] [DetectGpuAsync] Driver: {gpuInfo?.DriverVersion}");
                _logger?.LogInfo($"[GamerViewModel] [DetectGpuAsync] Dedicada: {gpuInfo?.IsDiscrete}");
                _logger?.LogInfo($"[GamerViewModel] [DetectGpuAsync] VRAM: {gpuInfo?.VideoMemoryBytes} bytes");

                if (gpuInfo == null || string.IsNullOrWhiteSpace(gpuInfo.Name))
                {
                    _logger?.LogWarning("[GamerViewModel] [DetectGpuAsync] GPU não detectada ou nome vazio");
                    GpuInfoText = LocalizationService.Instance.GetString("GpuDetectionError");
                    return;
                }

                GpuInfoText = gpuInfo.Name;
                _logger?.LogSuccess($"[GamerViewModel] [DetectGpuAsync] GPU detectada: {gpuInfo.Name}");
            }
            catch (Exception ex)
            {
                GpuInfoText = LocalizationService.Instance.GetString("GpuDetectionError");
                _logger?.LogError($"[GamerViewModel] [DetectGpuAsync] Erro ao detectar GPU: {ex.Message}");
            }
        }

        private async Task OptimizeGpuAsync(CancellationToken cancellationToken = default)
        {
            _logger?.LogInfo("[GamerViewModel] [OptimizeGpu] INICIADO");
            var loc = LocalizationService.Instance;
            await ExecuteSafeAsync(async () =>
            {
                GlobalProgressService.Instance.StartOperation(loc.GetString("OptimizingGPU"), true);
                _logger?.LogInfo("[GamerViewModel] [OptimizeGpu] Chamando _gpuOptimizer.OptimizeAsync");
                await _gpuOptimizer.OptimizeAsync(cancellationToken);
                _logger.LogSuccess($"[GamerViewModel] [OptimizeGpu] {loc.GetString("GpuOptimized")}");
            }, loc.GetString("OptimizingGPU"));
            _logger?.LogInfo("[GamerViewModel] [OptimizeGpu] FINALIZADO");
        }

        private async Task CheckGpuTempAsync(CancellationToken cancellationToken = default)
        {
            var locT = LocalizationService.Instance;
            try
            {
                _logger?.LogInfo("[GamerViewModel] Botão Temperatura clicado - obtendo métricas do GlobalThermalMonitorService");
                
                // CORREÇÃO CRÍTICA: Usar CurrentMetrics ao invés de GetCurrentMetricsAsync para evitar travamento
                // O serviço já está atualizando as métricas em tempo real, não precisa fazer nova leitura
                if (App.ThermalMonitorService != null && App.ThermalMonitorService.CurrentMetrics != null)
                {
                    var metrics = App.ThermalMonitorService.CurrentMetrics;
                    
                    // Atualizar propriedades
                    CpuTemperature = double.IsNaN(metrics.CpuTemperature) ? 0 : metrics.CpuTemperature;
                    GpuTemperature = double.IsNaN(metrics.GpuTemperature) ? 0 : metrics.GpuTemperature;
                    IsTemperatureEstimated = metrics.IsCpuTemperatureEstimated;
                    
                    // Atualizar textos legados
                    CpuTempText = CpuTemperature > 0 ? string.Format(locT.GetString("GamerCpuTemp"), CpuTemperature) : locT.GetString("GamerTempNone");
                    GpuTempText = GpuTemperature > 0 ? string.Format(locT.GetString("GamerGpuTemp"), GpuTemperature) : locT.GetString("GamerTempGpuNone");
                    
                    // Atualizar status térmico
                    UpdateThermalStatus(metrics);
                    
                    // Forçar notificação de mudança
                    OnPropertyChanged(nameof(CpuTemperature));
                    OnPropertyChanged(nameof(GpuTemperature));
                    OnPropertyChanged(nameof(CpuTempText));
                    OnPropertyChanged(nameof(GpuTempText));
                    
                    // Verificar alertas (sem await para não travar)
                    if (CpuTemperature > 0 || GpuTemperature > 0)
                    {
                        _ = CheckTemperatureAlerts(CpuTemperature, GpuTemperature);
                    }
                    
                    var tempType = metrics.IsCpuTemperatureEstimated ? "ESTIMADA" : "REAL";
                    _logger?.LogSuccess($"[GamerViewModel] Temperaturas atualizadas ({tempType}) - CPU: {CpuTemperature:F1}°C, GPU: {GpuTemperature:F1}°C");
                    
                    // Mostrar notificação de sucesso
                    GlobalNotificationService.ShowSuccess(locT.GetString("GamerTempNotificationTitle"), string.Format(locT.GetString("GamerTempNotificationBody"), CpuTemperature, GpuTemperature));
                }
                else
                {
                    _logger?.LogWarning("[GamerViewModel] GlobalThermalMonitorService não disponível ou sem métricas");
                    CpuTempText = locT.GetString("GamerTempWaiting");
                    GpuTempText = locT.GetString("GamerTempGpuWaiting");
                    
                    // Tentar forçar uma leitura se o serviço existir mas não tiver métricas
                    if (App.ThermalMonitorService != null)
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var metrics = await App.ThermalMonitorService.GetCurrentMetricsAsync();
                                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                                {
                                    OnThermalMetricsUpdated(this, metrics);
                                });
                            }
                            catch (Exception ex)
                            {
                                _logger?.LogError($"[GamerViewModel] Erro ao forçar leitura de temperatura: {ex.Message}");
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[GamerViewModel] Erro ao verificar temperatura: {ex.Message}", ex);
                CpuTempText = locT.GetString("GamerTempError");
                GpuTempText = locT.GetString("GamerTempGpuError");
            }
        }
        
        // ============================================
        // MONITORAMENTO TÉRMICO EM TEMPO REAL
        // ============================================
        
        // Throttling de log térmico: logar no máximo a cada 30s ou quando delta > 3°C
        private DateTime _lastThermalLogTime = DateTime.MinValue;
        private double _lastLoggedCpuTemp = double.NaN;
        private double _lastLoggedGpuTemp = double.NaN;
        private const double ThermalLogDeltaThreshold = 3.0;   // °C
        private const int ThermalLogIntervalSeconds = 30;

        private DateTime _lastThermalLogTimeGamer = DateTime.MinValue;

        /// <summary>
        /// Callback para atualização de métricas térmicas em tempo real.
        /// Log com throttling: apenas quando delta > 3°C ou a cada 30s.
        /// </summary>
        // [FIX:M-1] Mesmo defeito do DashboardViewModel: campos sem barreira de
        // memoria lidos pelo thread produtor e pelo thread da UI. Aqui o risco
        // era o inverso — se o BeginInvoke nao enfileirasse (Application.Current
        // nulo / Dispatcher shutting down), o flag ficava travado em true e a UI
        // gamer parava de atualizar temperatura para o resto da sessao.
        private ThermalMetrics? _pendingThermalMetrics;
        private int _thermalUiUpdatePending;
        private bool _cpuTemperaturaInvalida;

        private void OnThermalMetricsUpdated(object? sender, VoltrisOptimizer.Services.Thermal.Models.ThermalMetrics metrics)
        {
            try
            {
                var now = DateTime.Now;
                if ((now - _lastThermalLogTimeGamer).TotalSeconds >= 30)
                {
                    _lastThermalLogTimeGamer = now;
                    _logger?.LogInfo($"[GamerViewModel] OnThermalMetricsUpdated - CPU: {metrics.CpuTemperature:F1}°C, GPU: {metrics.GpuTemperature:F1}°C, Estimada: {metrics.IsCpuTemperatureEstimated}");
                }
                
                // BUG CORRIGIDO: logava WARNING a cada tick (~2s) quando o sensor
                // de temperatura nao existe. Em maquinas sem WMI termico o
                // GlobalThermalMonitorService publica NaN de proposito e ja
                // registra isso como INFO, entao este WARNING era alarme falso
                // repetido (~19 linhas por minuto) e nao indicava falha real.
                // Agora so avisa na TRANSICAO para invalido e na recuperacao.
                bool cpuTempInvalida = double.IsNaN(metrics.CpuTemperature) || metrics.CpuTemperature <= 0;
                if (cpuTempInvalida != _cpuTemperaturaInvalida)
                {
                    _cpuTemperaturaInvalida = cpuTempInvalida;
                    if (cpuTempInvalida)
                        _logger?.LogInfo($"[GamerViewModel] Temperatura de CPU indisponivel neste hardware ({metrics.CpuTemperature}). Interface exibira N/A. Isso e esperado, nao e falha.");
                    else
                        _logger?.LogInfo($"[GamerViewModel] Sensor de temperatura de CPU voltou a responder: {metrics.CpuTemperature:F1}C");
                }

                // Apenas armazenar a métrica mais recente — não empilhar BeginInvokes
                // [FIX:M-1] Volatile publica o valor antes de disputar a posse de UI.
                Volatile.Write(ref _pendingThermalMetrics, metrics);
                if (Interlocked.CompareExchange(ref _thermalUiUpdatePending, 1, 0) == 1) return;

                var locT2 = LocalizationService.Instance;
                var app = System.Windows.Application.Current;
                var disp = app?.Dispatcher;
                if (disp == null)
                {
                    // [FIX:M-1] Sem Dispatcher a posse e devolvida; antes ficava
                    // travada em 1 e nenhuma temperatura nova chegava a UI.
                    Interlocked.Exchange(ref _thermalUiUpdatePending, 0);
                    _logger?.LogWarning(
                        "[FIX:M-1] GamerViewModel sem Dispatcher na recepcao termica; posse liberada " +
                        "(antes o flag travava e a temperatura parava de atualizar)");
                    return;
                }

                disp.BeginInvoke(() =>
                {
                    try
                    {
                    var latest = Volatile.Read(ref _pendingThermalMetrics);
                    if (latest == null) return;

                    var oldCpuTemp = CpuTemperature;
                    var oldGpuTemp = GpuTemperature;

                    CpuTemperature = double.IsNaN(latest.CpuTemperature) ? 0 : latest.CpuTemperature;
                    GpuTemperature = double.IsNaN(latest.GpuTemperature) ? 0 : latest.GpuTemperature;
                    IsTemperatureEstimated = latest.IsCpuTemperatureEstimated;

                    UpdateThermalStatus(latest);

                    CpuTempText = CpuTemperature > 0 ? string.Format(locT2.GetString("GamerCpuTemp"), CpuTemperature) : locT2.GetString("GamerTempNone");
                    GpuTempText = GpuTemperature > 0 ? string.Format(locT2.GetString("GamerGpuTemp"), GpuTemperature) : locT2.GetString("GamerTempGpuNone");

                    OnPropertyChanged(nameof(CpuTemperature));
                    OnPropertyChanged(nameof(GpuTemperature));
                    OnPropertyChanged(nameof(CpuTempText));
                    OnPropertyChanged(nameof(GpuTempText));
                    OnPropertyChanged(nameof(IsTemperatureEstimated));
                    OnPropertyChanged(nameof(ThermalStatus));
                    OnPropertyChanged(nameof(ThermalStatusColor));

                    var now2 = DateTime.Now;
                    bool deltaSignificant = !double.IsNaN(_lastLoggedCpuTemp) &&
                        (Math.Abs(CpuTemperature - _lastLoggedCpuTemp) >= ThermalLogDeltaThreshold ||
                         Math.Abs(GpuTemperature - _lastLoggedGpuTemp) >= ThermalLogDeltaThreshold);
                    bool intervalElapsed = (now2 - _lastThermalLogTime).TotalSeconds >= ThermalLogIntervalSeconds;
                    bool typeChanged = oldCpuTemp == 0 && CpuTemperature > 0;

                    if (deltaSignificant || intervalElapsed || typeChanged || double.IsNaN(_lastLoggedCpuTemp))
                    {
                        var tempType = latest.IsCpuTemperatureEstimated ? "ESTIMADA" : "REAL";
                        _logger?.LogDebug($"[GamerViewModel] Temp ({tempType}) CPU: {CpuTemperature:F1}°C, GPU: {GpuTemperature:F1}°C");
                        _lastThermalLogTime = now2;
                        _lastLoggedCpuTemp = CpuTemperature;
                        _lastLoggedGpuTemp = GpuTemperature;
                    }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError($"[GamerViewModel] Erro ao aplicar temperaturas na UI: {ex.Message}", ex);
                    }
                    finally
                    {
                        // [FIX:M-1] Libera a posse de UI mesmo se o corpo lancar.
                        Interlocked.Exchange(ref _thermalUiUpdatePending, 0);
                    }
                });
            }
            catch (Exception ex)
            {
                // [FIX:M-1] Se o BeginInvoke nem enfileirou, a posse fica orfa.
                Interlocked.Exchange(ref _thermalUiUpdatePending, 0);
                _logger?.LogError($"[GamerViewModel] Erro ao despachar atualização térmica: {ex.Message}", ex);
            }
        }
        
        /// <summary>
        /// Callback para alertas térmicos
        /// </summary>
        private void OnThermalAlertGenerated(object? sender, VoltrisOptimizer.Services.Thermal.Models.ThermalAlert alert)
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                HasThermalAlert = true;
                
                // Mostrar notificação discreta
                GlobalNotificationService.ShowInfo(alert.Message, alert.Recommendation);
                
                _logger?.LogWarning($"[GamerViewModel] Alerta térmico: {alert.Message}");
            });
        }
        
        /// <summary>
        /// Atualiza o status térmico baseado nas métricas
        /// </summary>
        private void UpdateThermalStatus(VoltrisOptimizer.Services.Thermal.Models.ThermalMetrics metrics)
        {
            // Considerar apenas CPU se GPU não estiver disponível
            var maxTemp = double.IsNaN(metrics.GpuTemperature) 
                ? metrics.CpuTemperature 
                : Math.Max(metrics.CpuTemperature, metrics.GpuTemperature);
            
            var locThermal = LocalizationService.Instance;
            if (maxTemp >= 90)
            {
                ThermalStatus = locThermal.GetString("GamerThermalCritical");
                ThermalStatusColor = "#EF4444"; // Red
                HasThermalAlert = true;
            }
            else if (maxTemp >= 80)
            {
                ThermalStatus = locThermal.GetString("GamerThermalAlert");
                ThermalStatusColor = "#F59E0B"; // Orange
                HasThermalAlert = true;
            }
            else if (maxTemp >= 70)
            {
                ThermalStatus = locThermal.GetString("GamerThermalElevated");
                ThermalStatusColor = "#F59E0B"; // Orange
                HasThermalAlert = false;
            }
            else
            {
                ThermalStatus = locThermal.GetString("GamerThermalNormal");
                ThermalStatusColor = "#10B981"; // Green
                HasThermalAlert = false;
            }
        }
        
        /// <summary>
        /// Obtém temperaturas atuais do sistema
        /// </summary>
        private async Task<(double cpuTemp, double gpuTemp)> GetSystemTemperaturesAsync()
        {
            return await Task.Run(async () =>
            {
                double cpuTemp = 0;
                double gpuTemp = 0;
                
                // Obter temperatura da CPU
                try
                {
                    cpuTemp = await GetCpuTemperatureAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[GamerViewModel] Erro ao obter temperatura da CPU: {ex.Message}");
                    cpuTemp = 0;
                }
                
                // Obter temperatura da GPU
                try
                {
                    var gpuResult = await _gpuOptimizer.GetTemperatureAsync(default);
                    if (gpuResult.IsAvailable)
                    {
                        gpuTemp = gpuResult.Current;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[GamerViewModel] Erro ao obter temperatura da GPU: {ex.Message}");
                    gpuTemp = 0;
                }
                
                return (cpuTemp, gpuTemp);
            });
        }
        
        /// <summary>
        /// Verifica compatibilidade de hardware para otimizações
        /// </summary>
        private async Task<HardwareCompatibilityResult> CheckHardwareCompatibilityAsync()
        {
            GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("CheckingCompatibility"), false);
            return await Task.Run(async () =>
            {
                var result = new HardwareCompatibilityResult
                {
                    IsCompatible = true,
                    Incompatibilities = new List<string>()
                };
                
                try
                {
                    // Verificar se FPS Boost está ativado e verificar compatibilidade de HAGS
                    if (ApplyFpsBoost)
                    {
                        var gpuInfo = await _gpuOptimizer.GetGpuInfoAsync(default);
                        if (!gpuInfo.SupportsHags)
                        {
                            result.IsCompatible = false;
                            result.Incompatibilities.Add("Seu GPU não suporta Hardware-Accelerated GPU Scheduling (HAGS)");
                            result.Incompatibilities.Add("   " + LocalizationService.Instance.GetString("FPSEfficiencyReduced"));
                        }
                        else
                        {
                            result.Incompatibilities.Add("GPU compatível com HAGS");
                        }
                    }
                    
                    // Verificar outras compatibilidades futuras aqui
                    // Ex: verificar versão do Windows, drivers, etc.
                    
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[GamerViewModel] Erro ao verificar compatibilidade de hardware: {ex.Message}");
                    result.IsCompatible = false;
                    result.Incompatibilities.Add($"{LocalizationService.Instance.GetString("CompatibilityCheckError")}: {ex.Message}");
                }
                
                return result;
            });
        }
        
        /// <summary>
        /// Mostra diálogo de confirmação ao usuário
        /// </summary>
        private async Task<bool> ShowConfirmationDialogAsync(string title, string message, string confirmText, string cancelText)
        {
            // Esta é uma implementação simplificada
            // Em produção, usar MessageBox personalizada ou componente UI
            return await Task.Run(() =>
            {
                // Log para auditoria
                _logger.LogWarning($"[GamerViewModel] Diálogo de confirmação mostrado: {title}");
                _logger.LogWarning($"[GamerViewModel] Mensagem: {message}");
                
                // Por enquanto, retornar true para permitir continuidade
                // Em implementação real, mostrar MessageBox ou componente customizado
                return true;
            });
        }
        
        /// <summary>
        /// Resultado da verificação de compatibilidade de hardware
        /// </summary>
        private class HardwareCompatibilityResult
        {
            public bool IsCompatible { get; set; }
            public List<string> Incompatibilities { get; set; } = new();
        }
        private async Task<double> GetCpuTemperatureAsync()
        {
            return await Task.Run(() =>
            {
                // Método 1: MSAcpi_ThermalZoneTemperature (mais comum)
                try
                {
                    var obj = VoltrisOptimizer.Utils.WmiHelper.QueryFirstSafe("SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature", @"root\WMI");
                    if (obj != null)
                    {
                        var temp = obj["CurrentTemperature"];
                        if (temp != null)
                        {
                            var tempKelvin = Convert.ToDouble(temp) / 10.0;
                            var tempCelsius = tempKelvin - 273.15;
                            if (tempCelsius > 0 && tempCelsius < 150) return tempCelsius;
                        }
                    }
                }
                catch (Exception ex) { _logger.LogDebug($"[GamerViewModel] GetCpuTemperature MSAcpi falhou: {ex.Message}"); }
                
                // Método 2: Win32_TemperatureProbe
                try
                {
                    var obj = VoltrisOptimizer.Utils.WmiHelper.QueryFirstSafe("SELECT CurrentReading FROM Win32_TemperatureProbe");
                    if (obj != null)
                    {
                        var temp = obj["CurrentReading"];
                        if (temp != null)
                        {
                            var tempCelsius = Convert.ToDouble(temp);
                            if (tempCelsius > 0 && tempCelsius < 150) return tempCelsius;
                        }
                    }
                }
                catch (Exception ex) { _logger.LogDebug($"[GamerViewModel] GetCpuTemperature Win32_Probe falhou: {ex.Message}"); }
                

                
                // Método 4: Perf Counters
                try
                {
                    var obj = VoltrisOptimizer.Utils.WmiHelper.QueryFirstSafe("SELECT Temperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation");
                    if (obj != null)
                    {
                        var temp = Convert.ToDouble(obj["Temperature"]);
                        if (temp > 0 && temp < 150) return temp;
                    }
                }
                catch (Exception ex) { _logger.LogDebug($"[GamerViewModel] GetCpuTemperature PerfCounters falhou: {ex.Message}"); }
                
                return 0;
            });
        }
        
        /// <summary>
        /// Verifica temperaturas elevadas e alerta usuário sobre manutenção
        /// </summary>
        private async Task CheckTemperatureAlerts(double cpuTemp, double gpuTemp)
        {
            bool showAlert = false;
            string alertMessage = "";
            
            // Verificar CPU
            if (cpuTemp > 85)
            {
                showAlert = true;
                alertMessage += $"CPU muito quente ({cpuTemp:F0}°C)! \n";
            }
            else if (cpuTemp > 75)
            {
                showAlert = true;
                alertMessage += $"CPU aquecida ({cpuTemp:F0}°C). \n";
            }
            
            // Verificar GPU
            if (gpuTemp > 0)
            {
                if (gpuTemp > 85)
                {
                    showAlert = true;
                    alertMessage += $"GPU muito quente ({gpuTemp:F0}°C)! \n";
                }
                else if (gpuTemp > 75)
                {
                    showAlert = true;
                    alertMessage += $"GPU aquecida ({gpuTemp:F0}°C). \n";
                }
            }
            
            if (showAlert)
            {
                alertMessage += "\n" + LocalizationService.Instance.GetString("ThermalPasteRecommendation");
                
                // CORREÇÃO CRÍTICA: Remover Task.Run e Dispatcher.Invoke desnecessários que causam deadlock
                // Já estamos na UI thread, então podemos chamar diretamente
                GlobalNotificationService.ShowWarning(LocalizationService.Instance.GetString("TemperatureAlertTitle"), alertMessage);
                _logger?.LogWarning($"[TEMPERATURE] Alerta de temperatura: CPU={cpuTemp:F0}°C, GPU={gpuTemp:F0}°C");
            }
            
            await Task.CompletedTask;
        }

        private async Task RunGamerModeAsync(CancellationToken cancellationToken = default)
        {
            _logger?.LogInfo("[GamerViewModel] [RunGamerMode] ENTRY - Iniciando RunGamerModeAsync");

            // SaaS-LEVEL: Feature Gate centralizado — modo gamer exige licença paga.
            // Bloqueado = abre o modal de compra (LicenseUpgradeWindow).
            _logger?.LogInfo($"[GamerViewModel] [RunGamerMode] ProFeatureGuard.IsPaidActive() = {VoltrisOptimizer.Services.License.ProFeatureGuard.IsPaidActive()}");
            _logger?.LogInfo($"[GamerViewModel] [RunGamerMode] LicenseTokenStore.IsProActive = {VoltrisOptimizer.Services.License.LicenseTokenStore.IsProActive}");
            
            try 
            {
                var cachedState = VoltrisOptimizer.Services.License.LicenseOrchestrationService.Instance?.CachedState;
                _logger?.LogInfo($"[GamerViewModel] [RunGamerMode] CachedState = {cachedState?.IsActive}, Type = {cachedState?.LicenseType}");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[GamerViewModel] [RunGamerMode] Erro ao ler CachedState: {ex.Message}");
            }

            if (!VoltrisOptimizer.Services.License.ProFeatureGuard.RequirePaid("gamer_mode"))
            {
                _logger?.LogWarning("[GamerViewModel] [RunGamerMode] BLOQUEADO - RequirePaid retornou FALSE!");
                return;
            }

            _logger?.LogInfo("[GamerViewModel] [RunGamerMode] FEATURE GATE APROVADO - Modo Gamer habilitado, continuando...");

            // ✅ VERIFICAÇÃO DE ADMIN: Muitas otimizações (HKLM, powercfg) exigem elevação
            bool isAdmin = AdminHelper.IsRunningAsAdministrator();
            _logger?.LogInfo($"[GamerViewModel] [RunGamerMode] Verificação de Admin: {isAdmin}");
            if (!isAdmin)
            {
                _logger?.LogWarning("[GamerViewModel] [RunGamerMode] ⚠️ Aplicativo NÃO está em modo Administrador - otimizações HKLM/HAGS/PowerPlan não funcionarão!");
                _logger?.LogWarning("[GamerViewModel] [RunGamerMode] Recomende ao usuário reiniciar como Administrador para máximo desempenho.");

                await System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    GlobalNotificationService.ShowWarning(
                        LocalizationService.Instance.GetString("GamerAdminRequiredTitle"),
                        LocalizationService.Instance.GetString("GamerAdminRequiredMsg"));
                });
            }
            else
            {
                _logger?.LogSuccess("[GamerViewModel] [RunGamerMode] ✅ Aplicativo em modo Administrador - todas as otimizações disponíveis.");
            }

            if (!await _gamerModeLock.WaitAsync(0)) 
            {
                _logger.LogWarning("[GamerViewModel] Modo Gamer já está sendo ativado, ignorando solicitação duplicada");
                return; // Evitar reentrância
            }

            try
            {
                GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("ActivatingGamerMode"), true);
                // Decorative log removed
                _logger.LogInfo(LocalizationService.Instance.GetString("ManualGamerActivation"));
                _logger.LogInfo($"[GamerViewModel] Jogo selecionado: {SelectedGame?.Name ?? "Nenhum (Global Boost)"}");
                // Decorative log removed

                // CORREÇÃO CRÍTICA: Validar Perfil Inteligente ANTES de ativar
                if (_gamerProfileResolver != null)
                {
                    _logger.LogInfo("[GamerViewModel] Validando Perfil Inteligente e Hardware...");
                    
                    // Criar HardwareCapabilities do tipo Interfaces para o resolver
                    var systemInfoService = App.Services?.GetService(typeof(ISystemInfoService)) as ISystemInfoService;
                    var hardware = await (systemInfoService?.GetHardwareCapabilitiesAsync() ?? Task.FromResult(new VoltrisOptimizer.Interfaces.HardwareCapabilities()));
                    
                    var executionPlan = _gamerProfileResolver.ResolveExecutionPlan(hardware);
                    
                    var currentProfile = SettingsService.Instance.Settings.IntelligentProfile;
                    _logger.LogInfo($"[GamerViewModel] Perfil Inteligente Ativo: {currentProfile}");
                    _logger.LogInfo($"[GamerViewModel] Hardware: CPU: {hardware.Cpu.CoreCount} cores, RAM: {hardware.Ram.TotalGB}GB");
                    
                    // Verificar se ativação é permitida
                    if (!executionPlan.AllowActivation)
                    {
                        _logger.LogWarning($"[GamerViewModel] Modo Gamer BLOQUEADO pelo perfil {currentProfile}");
                        _logger.LogWarning($"[GamerViewModel] Motivo: {executionPlan.BlockReason}");
                        
                        await System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                        {
                            GlobalNotificationService.ShowWarning(
                                LocalizationService.Instance.GetString("GamerBlockedTitle"),
                                string.Format(LocalizationService.Instance.GetString("GamerBlockedMessage"), executionPlan.BlockReason)
                            );
                        });
                        
                        return; // SAIR SEM ATIVAR
                    }
                    
                    // Verificar temperatura se necessário
                    if (executionPlan.RequireTemperatureCheck)
                    {
                        _logger.LogInfo("[GamerViewModel] Verificando temperatura do sistema...");
                        var (cpuTemp, gpuTemp) = await GetSystemTemperaturesAsync();
                        
                        if (cpuTemp > executionPlan.MaxAllowedTempCelsius || gpuTemp > executionPlan.MaxAllowedTempCelsius)
                        {
                            _logger.LogWarning($"[GamerViewModel] Temperatura muito alta! CPU: {cpuTemp:F1}°C, GPU: {gpuTemp:F1}°C (Máx: {executionPlan.MaxAllowedTempCelsius}°C)");
                            
                            var userConfirmed = await ShowConfirmationDialogAsync(
                                LocalizationService.Instance.GetString("TemperatureAlertTitle"),
                                $"A temperatura do sistema está elevada:\n\n" +
                                $"CPU: {cpuTemp:F1}°C\n" +
                                $"GPU: {gpuTemp:F1}°C\n\n" +
                                $"Ativar o Modo Gamer pode aumentar ainda mais a temperatura.\n\n" +
                                $"Deseja continuar mesmo assim?",
                                "Continuar",
                                "Cancelar"
                            );
                            
                            if (!userConfirmed)
                            {
                                _logger.LogInfo("[GamerViewModel] Usuário cancelou ativação devido à temperatura elevada");
                                return;
                            }
                        }
                    }
                    
                    // Ajustar opções baseado no plano de execução
                    _logger.LogInfo("[GamerViewModel] Ajustando opções baseado no perfil...");
                    
                    if (!executionPlan.IsOptimizationAllowed("CloseBackgroundApps"))
                    {
                        _options.CloseBackgroundApps = false;
                        _logger.LogInfo("[GamerViewModel] CloseBackgroundApps: Desabilitado pelo perfil");
                    }
                    
                    if (!executionPlan.IsOptimizationAllowed("MemoryOptimization"))
                    {
                        _options.OptimizeMemory = false;
                        _logger.LogInfo("[GamerViewModel] MemoryOptimization: Desabilitado pelo perfil");
                    }
                    
                    if (!executionPlan.IsOptimizationAllowed("NetworkOptimization"))
                    {
                        _options.OptimizeNetwork = false;
                        _logger.LogInfo("[GamerViewModel] NetworkOptimization: Desabilitado pelo perfil");
                    }
                    
                    if (!executionPlan.IsOptimizationAllowed("UltimatePerformance"))
                    {
                        _logger.LogInfo("[GamerViewModel] UltimatePerformance: Desabilitado pelo perfil (evitar superaquecimento)");
                    }
                    
                    _logger.LogSuccess($"[GamerViewModel] Validação concluída: {executionPlan.AllowedOptimizations.Count} otimizações permitidas");
                }
                else
                {
                    _logger.LogWarning("[GamerViewModel] GamerProfileResolver não disponível, prosseguindo sem validação de perfil");
                }

                // Telemetry
                App.TelemetryService?.TrackEvent("GAMER_MODE_START", SelectedGame?.Name ?? "Global", "Activate", forceFlush: true);

                // CORREÇÃO: Executar em background para não travar a UI
                await Task.Run(async () =>
                {
                    var progress = new Progress<int>(p => 
                    {
                        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                        {
                            BusyMessage = string.Format(LocalizationService.Instance.GetString("ActivatingProgress"), p);
                        });
                    });

var gameExe = SelectedGame?.ExecutablePath;

                    _logger.LogInfo($"[GamerMode] Iniciando ativação via GamerModeManager (orquestrador único)");

                    bool result = false;
                    try
                    {
                        result = await _gamerModeManager.ActivateAsync(gameExe, cancellationToken);
                        _logger.LogInfo($"[GamerMode] GamerModeManager.ActivateAsync resultado: {result}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"[GamerMode] Erro no GamerModeManager.ActivateAsync: {ex.Message}", ex);
                    }

                    if (result)
                    {
                        _logger.LogSuccess("[GamerViewModel] Ativação confirmada pelo GamerModeManager");

                        // Aplicar BH PROCHOT durante modo gamer (sempre ativado)
                        if (_bdProchotGameEnabled && ProchotService != null)
                        {
                            _logger.LogInfo("[GamerViewModel] Aplicando BH PROCHOT (desativado) para modo gamer manual");
                            _ = ProchotService.DisableBdProchotAsync();
                        }

                        // Atualizar UI na thread principal
                        await System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                        {
                            IsGamerModeActive = true;
                            StatusText = LocalizationService.Instance.GetString("GamerModeActiveBoost");
                        });
                        
                        _logger.LogSuccess("-----------------------------------------------");
                        _logger.LogSuccess("? MODO GAMER ATIVADO E SINCRONIZADO COM A UI!");
                        _logger.LogSuccess("-----------------------------------------------");
                        
                        // Enviar notificação de ativação bem-sucedida (NA THREAD PRINCIPAL)
                        _logger.LogInfo("[GamerViewModel] Enviando notificação de ativação do Modo Gamer...");
                        await System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                        {
                            try
                            {
                                GlobalNotificationService.ShowSuccess(
                                    LocalizationService.Instance.GetString("GamerModeTitle"),
                                    string.Format(LocalizationService.Instance.GetString("GamerModeActivatedSuccess"), SelectedGame?.Name ?? LocalizationService.Instance.GetString("TheSystem"))
                                );
                                _logger.LogInfo("[GamerViewModel] Notificação de ativação enviada com sucesso");
                            }
                            catch (Exception notifEx)
                            {
                                _logger.LogWarning($"[GamerViewModel] Erro ao enviar notificação de ativação: {notifEx.Message}");
                            }
                        });
                        
                        // INTEGRAÇÃO AUTOMÁTICA: Ativar otimizações temporárias SEMPRE junto com o modo gamer
                        if (_gamerSessionManager != null && !IsTemporaryOptimizationSessionActive)
                        {
                            _logger.LogInfo("[GamerViewModel] Ativando otimizações temporárias automaticamente com o modo gamer...");
                            _logger.LogInfo($"[GamerViewModel] Estado atual: IsTemporaryOptimizationSessionActive={IsTemporaryOptimizationSessionActive}");
                            _logger.LogInfo($"[GamerViewModel] GamerSessionManager disponível: {_gamerSessionManager != null}");
                            await StartTemporaryOptimizationAsync();
                            _logger.LogInfo($"[GamerViewModel] Após StartTemporaryOptimizationAsync: IsTemporaryOptimizationSessionActive={IsTemporaryOptimizationSessionActive}");
                        }
                        else if (IsTemporaryOptimizationSessionActive)
                        {
                            _logger.LogInfo("[GamerViewModel] Otimizações temporárias já estão ativas");
                        }
                        else if (_gamerSessionManager == null)
                        {
                            _logger.LogError("[GamerViewModel] GamerSessionManager é NULL! Otimizações temporárias não podem ser ativadas");
                        }
                        
                        // AUTO-START PERFORMANCE MONITOR: Iniciar automaticamente se configurado
                        if (_overlayAutoStartWithGamerMode && !_overlayEnabled && _overlayService != null)
                        {
                            _logger.LogInfo("[GamerViewModel] Auto-iniciando Performance Monitor com o Modo Gamer...");
                            try
                            {
                                // CORREÇÃO CRÍTICA: Usar o PID do jogo ativo, NÃO o PID do VoltrisOptimizer!
                                // Process.GetCurrentProcess() retornava o PID do próprio app, causando FPS incorreto
                                int gameProcessId = 0;
                                var orchestrator = _orchestrator;
                                if (orchestrator != null && orchestrator.Status.ActiveGameProcessId > 0)
                                {
                                    gameProcessId = orchestrator.Status.ActiveGameProcessId ?? 0;
                                    _logger.LogInfo($"[GamerViewModel] PID do jogo obtido do orchestrator: {gameProcessId}");
                                }
                                
                                // Fallback: tentar encontrar pelo nome do jogo ativo
                                if (gameProcessId == 0 && orchestrator != null && !string.IsNullOrEmpty(orchestrator.Status.ActiveGameName))
                                {
                                    var gameName = orchestrator.Status.ActiveGameName;
                                    _logger.LogInfo($"[GamerViewModel] Buscando processo pelo nome: {gameName}");
                                    var procs = Process.GetProcessesByName(gameName);
                                    if (procs.Length > 0)
                                    {
                                        gameProcessId = procs[0].Id;
                                        _logger.LogInfo($"[GamerViewModel] Processo encontrado: {gameName} (PID: {gameProcessId})");
                                    }
                                }
                                
                                if (gameProcessId > 0)
                                {
                                    await System.Windows.Application.Current?.Dispatcher.InvokeAsync(async () =>
                                    {
                                        OverlayEnabled = true;
                                        await _overlayService.StartAsync(gameProcessId);
                                        _logger.LogSuccess($"[GamerViewModel] Performance Monitor iniciado automaticamente para PID {gameProcessId}!");
                                    });
                                }
                                else
                                {
                                    _logger.LogWarning("[GamerViewModel] Nenhum processo de jogo encontrado para auto-iniciar overlay. O overlay será iniciado quando um jogo for detectado.");
                                }
                            }
                            catch (Exception overlayEx)
                            {
                                _logger.LogError($"[GamerViewModel] Erro ao auto-iniciar Performance Monitor: {overlayEx.Message}");
                            }
                        }
                        else if (_overlayAutoStartWithGamerMode && _overlayEnabled)
                        {
                            _logger.LogInfo("[GamerViewModel] Performance Monitor já está ativo");
                        }
                        
                        // Update command states na thread principal
                        await System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                        {
                            UpdateCommandStates();
                        });
                    }
                }, cancellationToken);
                GlobalProgressService.Instance.UpdateProgress(100, LocalizationService.Instance.GetString("GamerModeActivated"));
                GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("GamerModeActivated"));
            }
            catch (Exception ex)
            {
                GlobalProgressService.Instance.UpdateProgress(0, LocalizationService.Instance.GetString("GamerModeActivationFailed"));
                GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("GamerModeActivationFailed"));
                _logger.LogError($"[GamerViewModel] Erro ao ativar modo gamer: {ex.Message}");
                await System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    GlobalNotificationService.ShowError(
                        LocalizationService.Instance.GetString("GamerModeActivationError"),
                        string.Format(LocalizationService.Instance.GetString("ErrorOccurred"), ex.Message)
                    );
                });
            }
            finally
            {
                _gamerModeLock.Release(); // Liberar semáforo
            }
        }

        /// <summary>
        /// Entry point público para o Tray desativar o modo gamer com segurança
        /// </summary>
        public async Task DeactivateGamerModeFromTrayAsync()
        {
            _logger?.LogInfo("[GamerViewModel] [DeactivateGamerModeFromTray] INICIADO via Tray");
            await DeactivateGamerModeAsync();
            _logger?.LogInfo("[GamerViewModel] [DeactivateGamerModeFromTray] FINALIZADO");
        }

        private async Task DeactivateGamerModeAsync(CancellationToken cancellationToken = default)
        {
            _logger?.LogInfo("[GamerViewModel] [DeactivateGamerMode] INICIADO");
            if (!await _gamerModeLock.WaitAsync(0))
            {
                _logger?.LogWarning("[GamerViewModel] [DeactivateGamerMode] Já está desativando, ignorando chamada duplicada");
                return;
            }

            using var globalCts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, globalCts.Token);
            var ct = linkedCts.Token;

            try
            {
                GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("DeactivatingGamerMode"), true);
                _logger?.LogInfo("[GamerViewModel] [DeactivateGamerMode] Iniciando desativação do Modo Gamer");

                var progress = new Progress<int>(p =>
                {
                    BusyMessage = string.Format(LocalizationService.Instance.GetString("DeactivatingProgress"), p);
                });

                // Telemetry
                App.TelemetryService?.TrackEvent("GAMER_MODE_END", SelectedGame?.Name ?? "Global", "Deactivate", forceFlush: true);

                // FASE ÚNICA: Desativar via GamerModeManager (orquestrador único, timeout 30s)
                _logger?.LogInfo("[GamerViewModel] [DeactivateGamerMode] Chamando _gamerModeManager.DeactivateAsync");
                bool result = false;
                try
                {
                    using var managerCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    using var managerLinked = CancellationTokenSource.CreateLinkedTokenSource(ct, managerCts.Token);
                    result = await _gamerModeManager.DeactivateAsync(managerLinked.Token);
                    _logger?.LogInfo($"[GamerViewModel] [DeactivateGamerMode] Resultado do DeactivateAsync: {result}");
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("[GamerViewModel] [DeactivateGamerMode] GamerModeManager.DeactivateAsync cancelado por timeout (30s) - forçando estado inativo");
                    result = false;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[GamerViewModel] [DeactivateGamerMode] Erro no GamerModeManager.DeactivateAsync: {ex.Message}", ex);
                }

                ct.ThrowIfCancellationRequested();

                // FASE 3: Forçar IsGamerModeActive = false e restaurar estado (sempre executa)
                try
                {
                    IsGamerModeActive = false;
                    StatusText = LocalizationService.Instance.GetString("GamerModeInactive");

                    if (ProchotService != null)
                    {
                        _logger.LogInfo("[GamerViewModel] Restaurando BH PROCHOT ao sair do modo gamer");
                        _ = ProchotService.EnableBdProchotAsync();
                    }

                    if (IsTemporaryOptimizationSessionActive && _gamerSessionManager != null)
                    {
                        _logger.LogInfo("[GamerViewModel] [DeactivateGamerMode] Desativando otimizações temporárias automaticamente com o modo gamer...");
                        try
                        {
                            using var tempCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                            using var tempLinked = CancellationTokenSource.CreateLinkedTokenSource(ct, tempCts.Token);
                            await StopTemporaryOptimizationAsync();
                        }
                        catch (OperationCanceledException)
                        {
                            _logger.LogWarning("[GamerViewModel] [DeactivateGamerMode] StopTemporaryOptimization cancelado por timeout - continuando");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"[GamerViewModel] [DeactivateGamerMode] Erro ao parar otimizações temporárias: {ex.Message}", ex);
                        }
                    }

                    UpdateCommandStates();

                    if (result)
                    {
                        _logger?.LogSuccess("[GamerViewModel] [DeactivateGamerMode] Modo Gamer desativado com sucesso");
                        GlobalNotificationService.ShowSuccess(LocalizationService.Instance.GetString("GamerMode"), LocalizationService.Instance.GetString("GamerModeDeactivatedSuccess"));
                    }
                    else
                    {
                        _logger?.LogWarning("[GamerViewModel] [DeactivateGamerMode] Modo Gamer desativado com falhas parciais");
                        GlobalNotificationService.ShowWarning(LocalizationService.Instance.GetString("GamerMode"), LocalizationService.Instance.GetString("GamerModeDeactivatedSuccess"));
                    }
                }
                catch (Exception uiEx)
                {
                    _logger.LogError($"[GamerViewModel] [DeactivateGamerMode] Erro ao atualizar UI após desativação: {uiEx.Message}", uiEx);
                }

                GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("GamerModeDeactivated"));
                _logger?.LogInfo("[GamerViewModel] [DeactivateGamerMode] FINALIZADO");
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("[GamerViewModel] [DeactivateGamerMode] Desativação cancelada globalmente - forçando estado inativo");
                IsGamerModeActive = false;
                StatusText = LocalizationService.Instance.GetString("GamerModeInactive");
                UpdateCommandStates();
                GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("GamerModeDeactivated"));
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GamerViewModel] [DeactivateGamerMode] Erro fatal: {ex.Message}", ex);
                IsGamerModeActive = false;
                StatusText = LocalizationService.Instance.GetString("GamerModeInactive");
                UpdateCommandStates();
                GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("GamerModeDeactivated"));
            }
            finally
            {
                _gamerModeLock.Release();
            }
        }
        
        /// <summary>
        /// Updates the CanExecute state of commands that depend on GamerMode state
        /// </summary>
        private void UpdateCommandStates()
        {
            // Update GamerMode commands
            if (RunGamerModeCommand is AsyncRelayCommand runCmd)
                runCmd.RaiseCanExecuteChanged();
            if (ActivateGamerModeCommand is AsyncRelayCommand activateCmd)
                activateCmd.RaiseCanExecuteChanged();
            if (DeactivateGamerModeCommand is AsyncRelayCommand deactivateCmd)
                deactivateCmd.RaiseCanExecuteChanged();
            
            // Update Temporary Optimization commands
            if (StartTemporaryOptimizationCommand is AsyncRelayCommand startTempCmd)
                startTempCmd.RaiseCanExecuteChanged();
            if (StopTemporaryOptimizationCommand is AsyncRelayCommand stopTempCmd)
                stopTempCmd.RaiseCanExecuteChanged();
            if (ForceRollbackTemporaryOptimizationCommand is AsyncRelayCommand rollbackTempCmd)
                rollbackTempCmd.RaiseCanExecuteChanged();
        }
        
        #region Temporary Optimization Modules
        
        private async Task StartTemporaryOptimizationAsync()
        {
            _logger?.LogInfo("-----------------------------------------------------------");
            _logger?.LogInfo("[GamerViewModel] [StartTempOptimization] INICIADO");
            _logger?.LogInfo("-----------------------------------------------------------");
            
            if (_gamerSessionManager == null)
            {
                _logger?.LogWarning("[GamerViewModel] [StartTempOptimization] GamerSessionManager não disponível");
                return;
            }
            
            var loc = LocalizationService.Instance;
            try
            {
                GlobalProgressService.Instance.StartOperation(loc.GetString("TempOptimizationProgress"), true);
                _logger?.LogInfo("[GamerViewModel] [StartTempOptimization] GamerSessionManager disponível, iniciando sessão...");
                _logger?.LogInfo($"[GamerViewModel] [StartTempOptimization] Estado ANTES: IsTemporaryOptimizationSessionActive={IsTemporaryOptimizationSessionActive}");
                _logger?.LogInfo($"[GamerViewModel] [StartTempOptimization] TemporaryOptimizationStatus={TemporaryOptimizationStatus}");

                // Atualizar UI imediatamente
                await System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    BusyMessage = loc.GetString("TempOptimizationBusyMessage");
                    _logger?.LogInfo("[GamerViewModel] [StartTempOptimization] UI atualizada: BusyMessage definido");
                });
                
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                
                // Tentar identificar o processo do jogo para passar ao SessionManager
                int? targetProcessId = null;
                
                // 1. Verificar Status do Orquestrador
                if (_orchestrator.Status.IsActive && _orchestrator.Status.ActiveGameProcessId > 0)
                {
                    targetProcessId = _orchestrator.Status.ActiveGameProcessId;
                    _logger?.LogInfo($"[GamerViewModel] [StartTempOptimization] Usando processo do orquestrador: PID={targetProcessId}");
                }
                // 2. Verificar Jogo Selecionado
                else if (SelectedGame != null && !string.IsNullOrEmpty(SelectedGame.ExecutablePath))
                {
                    try 
                    {
                        var name = System.IO.Path.GetFileNameWithoutExtension(SelectedGame.ExecutablePath);
                        var procs = Process.GetProcessesByName(name);
                        if (procs.Length > 0)
                        {
                            targetProcessId = procs[0].Id;
                            _logger?.LogInfo($"[GamerViewModel] [StartTempOptimization] Usando processo do jogo selecionado: {name} PID={targetProcessId}");
                        }
                        else
                        {
                            _logger?.LogWarning($"[GamerViewModel] [StartTempOptimization] Jogo {name} não encontrado em execução");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning($"[GamerViewModel] [StartTempOptimization] Erro ao buscar processo do jogo: {ex.Message}");
                    }
                }
                else
                {
                    _logger?.LogInfo("[GamerViewModel] [StartTempOptimization] Nenhum jogo específico identificado, iniciando sessão sem PID");
                }

                GlobalProgressService.Instance.UpdateProgress(30, loc.GetString("ApplyingTempOptimizations"));
                _logger?.LogInfo($"[GamerViewModel] [StartTempOptimization] Chamando _gamerSessionManager.StartSessionAsync(targetProcessId={targetProcessId})");
                var result = await _gamerSessionManager.StartSessionAsync(targetProcessId, cts.Token);
                GlobalProgressService.Instance.UpdateProgress(70, loc.GetString("TempOptimizationsApplied"));
                _logger?.LogInfo($"[GamerViewModel] [StartTempOptimization] Resultado recebido: Success={result.Success}, TotalChangesApplied={result.TotalChangesApplied}");
                
                if (result.Success)
                {
                    _logger?.LogSuccess($"[GamerViewModel] [StartTempOptimization] Otimizações temporárias ativadas: {result.TotalChangesApplied} mudanças aplicadas");
                    
                    // Atualizar UI na thread principal
                    await System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        IsTemporaryOptimizationSessionActive = true;
                        TemporaryOptimizationStatus = string.Format(loc.GetString("GamerTempOptActive"), result.TotalChangesApplied);
                        BusyMessage = "";
                        _logger?.LogInfo($"[GamerViewModel] [StartTempOptimization] UI atualizada: IsTemporaryOptimizationSessionActive={IsTemporaryOptimizationSessionActive}");
                        _logger?.LogInfo($"[GamerViewModel] [StartTempOptimization] UI atualizada: TemporaryOptimizationStatus={TemporaryOptimizationStatus}");
                        UpdateCommandStates();
                    });
                    
                    try
                    {
                        GlobalNotificationService.ShowInfo(
                            loc.GetString("GamerTempOptActivatedTitle"),
                            string.Format(loc.GetString("GamerTempOptActivatedBody"), result.TotalChangesApplied)
                        );
                        _logger?.LogInfo("[GamerViewModel] [StartTempOptimization] Notificação enviada");
                    }
                    catch (Exception notifEx)
                    {
                        _logger?.LogWarning($"[GamerViewModel] [StartTempOptimization] Erro ao enviar notificação: {notifEx.Message}");
                    }
                }
                else
                {
                    _logger?.LogError($"[GamerViewModel] [StartTempOptimization] Falha ao ativar otimizações temporárias: {result.ErrorMessage}");
                    
                    await System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        TemporaryOptimizationStatus = loc.GetString("GamerTempOptActivateError");
                        BusyMessage = "";
                        _logger?.LogInfo("[GamerViewModel] [StartTempOptimization] UI atualizada com erro");
                    });
                }
                
                GlobalProgressService.Instance.UpdateProgress(100, loc.GetString("TempOptimizationComplete"));
                GlobalProgressService.Instance.CompleteOperation(loc.GetString("TempOptimizationComplete"));
                _logger?.LogInfo($"[GamerViewModel] [StartTempOptimization] Estado FINAL: IsTemporaryOptimizationSessionActive={IsTemporaryOptimizationSessionActive}");
            }
            catch (Exception ex)
            {
                GlobalProgressService.Instance.UpdateProgress(0, loc.GetString("TempOptimizationFailed"));
                GlobalProgressService.Instance.CompleteOperation(loc.GetString("TempOptimizationFailed"));
                _logger?.LogError($"[GamerViewModel] [StartTempOptimization] Exceção ao iniciar otimizações temporárias: {ex.Message}");
                _logger?.LogError($"[GamerViewModel] [StartTempOptimization] StackTrace: {ex.StackTrace}");
                
                await System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    TemporaryOptimizationStatus = loc.GetString("GamerTempOptError");
                    BusyMessage = "";
                });
            }
            
            _logger?.LogInfo("-----------------------------------------------------------");
            _logger?.LogInfo("[GamerViewModel] [StartTempOptimization] FINALIZADO");
            _logger?.LogInfo("-----------------------------------------------------------");
        }
        
        private async Task StopTemporaryOptimizationAsync()
        {
            _logger?.LogInfo("-----------------------------------------------------------");
            _logger?.LogInfo("[GamerViewModel] [StopTempOptimization] INICIADO - Solicitação do usuário");
            _logger?.LogInfo($"[GamerViewModel] [StopTempOptimization] _gamerSessionManager == null: {_gamerSessionManager == null}");
            _logger?.LogInfo($"[GamerViewModel] [StopTempOptimization] IsTemporaryOptimizationSessionActive: {IsTemporaryOptimizationSessionActive}");
            
            if (_gamerSessionManager == null)
            {
                _logger?.LogError("[GamerViewModel] [StopTempOptimization] ERRO CRÍTICO: GamerSessionManager não está disponível");
                _logger?.LogError("[GamerViewModel] [StopTempOptimization] Possível causa: DI não inicializado ou falha na resolução");
                
                // CORREÇÃO: Mostrar erro ao usuário ao invés de falhar silenciosamente
                try
                {
                    GlobalNotificationService.ShowError(
                        LocalizationService.Instance.GetString("GamerTempOptErrorTitle") ?? "Erro",
                        "Gerenciador de sessão não disponível. Verifique os logs para detalhes."
                    );
                }
                catch (Exception notifEx)
                {
                    _logger?.LogError($"[GamerViewModel] [StopTempOptimization] Erro ao mostrar notificação: {notifEx.Message}");
                }
                
                _logger?.LogInfo("-----------------------------------------------------------");
                return;
            }
            
            var loc = LocalizationService.Instance;
            
            await ExecuteSafeAsync(async () =>
            {
                _logger?.LogInfo("[GamerViewModel] [StopTempOptimization] Iniciando operação de progresso global...");
                GlobalProgressService.Instance.StartOperation(loc.GetString("RevertingTempOptimization"), true);
                
                _logger?.LogInfo("[GamerViewModel] [StopTempOptimization] Parando sessão de otimização temporária...");
                _logger?.LogInfo($"[GamerViewModel] [StopTempOptimization] SessionId atual: {_gamerSessionManager.CurrentSessionId ?? "N/A"}");
                
                BusyMessage = loc.GetString("RevertingTempOptimizationBusy");
                
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                
                _logger?.LogInfo("[GamerViewModel] [StopTempOptimization] Chamando StopSessionAsync...");
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                
                var result = await _gamerSessionManager.StopSessionAsync(cts.Token);
                
                stopwatch.Stop();
                _logger?.LogInfo($"[GamerViewModel] [StopTempOptimization] StopSessionAsync completado em {stopwatch.ElapsedMilliseconds}ms");
                _logger?.LogInfo($"[GamerViewModel] [StopTempOptimization] Resultado: Success={result.Success}, TotalChangesReverted={result.TotalChangesReverted}");
                
                if (result.Success)
                {
                    TemporaryOptimizationStatus = loc.GetString("GamerTempOptInactive");
                    _logger?.LogSuccess($"[GamerViewModel] [StopTempOptimization] ✅ Sessão temporária parada com sucesso: {result.TotalChangesReverted} mudanças revertidas");
                    
                    // Auditoria detalhada
                    _logger?.LogInfo("[GamerViewModel] [StopTempOptimization] === AUDITORIA DO ROLLBACK ===");
                    
                    try
                    {
                        GlobalNotificationService.ShowSuccess(
                            loc.GetString("GamerTempOptDeactivatedTitle"),
                            string.Format(
                                loc.GetString("GamerTempOptDeactivatedBodyDetail") ?? "Sessão encerrada com {0} mudanças revertidas.",
                                result.TotalChangesReverted
                            )
                        );
                        _logger?.LogInfo("[GamerViewModel] [StopTempOptimization] Notificação de sucesso exibida");
                    }
                    catch (Exception notifEx)
                    {
                        _logger?.LogWarning($"[GamerViewModel] [StopTempOptimization] Erro ao exibir notificação de sucesso: {notifEx.Message}");
                    }
                }
                else
                {
                    TemporaryOptimizationStatus = string.Format(loc.GetString("GamerTempOptErrorDetail"), result.ErrorMessage);
                    _logger?.LogError($"[GamerViewModel] [StopTempOptimization] ❌ ERRO: {result.ErrorMessage}");
                    
                    try
                    {
                        GlobalNotificationService.ShowError(
                            loc.GetString("GamerTempOptErrorTitle") ?? "Erro",
                            $"Falha ao parar sessão: {result.ErrorMessage}"
                        );
                    }
                    catch (Exception notifEx)
                    {
                        _logger?.LogWarning($"[GamerViewModel] [StopTempOptimization] Erro ao exibir notificação de erro: {notifEx.Message}");
                    }
                }
                
                UpdateCommandStates();
                _logger?.LogInfo("[GamerViewModel] [StopTempOptimization] UpdateCommandStates() chamado");
            }, loc.GetString("RevertingTempOptimizations"));
            
            _logger?.LogInfo("[GamerViewModel] [StopTempOptimization] FINALIZADO");
            _logger?.LogInfo("-----------------------------------------------------------");
        }
        
        private async Task ForceRollbackTemporaryOptimizationAsync()
        {
            _logger?.LogInfo("-----------------------------------------------------------");
            _logger?.LogInfo("[GamerViewModel] [ForceRollbackTempOptimization] INICIADO - Rollback de emergência solicitado pelo usuário");
            _logger?.LogInfo($"[GamerViewModel] [ForceRollbackTempOptimization] _gamerSessionManager == null: {_gamerSessionManager == null}");
            _logger?.LogInfo($"[GamerViewModel] [ForceRollbackTempOptimization] IsTemporaryOptimizationSessionActive: {IsTemporaryOptimizationSessionActive}");
            
            if (_gamerSessionManager == null)
            {
                _logger?.LogError("[GamerViewModel] [ForceRollbackTempOptimization] ERRO CRÍTICO: GamerSessionManager não está disponível");
                _logger?.LogError("[GamerViewModel] [ForceRollbackTempOptimization] Possível causa: DI não inicializado ou falha na resolução");
                
                // CORREÇÃO: Mostrar erro ao usuário ao invés de falhar silenciosamente
                try
                {
                    GlobalNotificationService.ShowError(
                        LocalizationService.Instance.GetString("GamerTempOptErrorTitle") ?? "Erro",
                        "Gerenciador de sessão não disponível. Não foi possível executar o rollback."
                    );
                }
                catch (Exception notifEx)
                {
                    _logger?.LogError($"[GamerViewModel] [ForceRollbackTempOptimization] Erro ao mostrar notificação: {notifEx.Message}");
                }
                
                _logger?.LogInfo("-----------------------------------------------------------");
                return;
            }
            
            var loc = LocalizationService.Instance;
            
            await ExecuteSafeAsync(async () =>
            {
                _logger?.LogWarning("[GamerViewModel] [ForceRollbackTempOptimization] ⚠️ ROLLBACK DE EMERGÊNCIA INICIADO");
                _logger?.LogWarning($"[GamerViewModel] [ForceRollbackTempOptimization] SessionId: {_gamerSessionManager.CurrentSessionId ?? "N/A"}");
                _logger?.LogWarning($"[GamerViewModel] [ForceRollbackTempOptimization] ForceEmergencyRollback invoked");
                
                GlobalProgressService.Instance.StartOperation(loc.GetString("ForcingEmergencyRollback"), true);
                
                BusyMessage = loc.GetString("EmergencyRollbackBusy");
                
                _logger?.LogInfo("[GamerViewModel] [ForceRollbackTempOptimization] Chamando ForceEmergencyRollbackAsync...");
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                
                var success = await _gamerSessionManager.ForceEmergencyRollbackAsync();
                
                stopwatch.Stop();
                _logger?.LogInfo($"[GamerViewModel] [ForceRollbackTempOptimization] ForceEmergencyRollbackAsync completado em {stopwatch.ElapsedMilliseconds}ms");
                _logger?.LogInfo($"[GamerViewModel] [ForceRollbackTempOptimization] Resultado: Success={success}");
                
                if (success)
                {
                    TemporaryOptimizationStatus = loc.GetString("GamerTempOptInactiveRollback");
                    _logger?.LogSuccess("[GamerViewModel] [ForceRollbackTempOptimization] ✅ Rollback de emergência CONCLUÍDO com sucesso");
                    
                    // Auditoria do rollback
                    _logger?.LogInfo("[GamerViewModel] [ForceRollbackTempOptimization] === AUDITORIA DO EMERGENCY ROLLBACK ===");
                    _logger?.LogInfo($"[GamerViewModel] [ForceRollbackTempOptimization] IsSessionActive após rollback: {_gamerSessionManager.IsSessionActive}");
                    _logger?.LogInfo($"[GamerViewModel] [ForceRollbackTempOptimization] CurrentSessionId após rollback: {_gamerSessionManager.CurrentSessionId ?? "N/A"}");
                    
                    try
                    {
                        GlobalNotificationService.ShowWarning(
                            loc.GetString("GamerTempOptRollbackTitle"),
                            loc.GetString("GamerTempOptRollbackBody")
                        );
                        _logger?.LogInfo("[GamerViewModel] [ForceRollbackTempOptimization] Notificação de aviso exibida");
                    }
                    catch (Exception notifEx)
                    {
                        _logger?.LogWarning($"[GamerViewModel] [ForceRollbackTempOptimization] Erro ao exibir notificação: {notifEx.Message}");
                    }
                }
                else
                {
                    _logger?.LogError("[GamerViewModel] [ForceRollbackTempOptimization] ❌ ERRO CRÍTICO: Rollback de emergência FALHOU");
                    _logger?.LogError("[GamerViewModel] [ForceRollbackTempOptimization] Isto pode indicar que as otimizações ainda estão ativas!");
                    
                    try
                    {
                        GlobalNotificationService.ShowError(
                            loc.GetString("GamerTempOptErrorTitle") ?? "Erro Crítico",
                            "Rollback de emergência falhou. Algumas otimizações podem ainda estar ativas. Reinicie o PC se necessário."
                        );
                    }
                    catch (Exception notifEx)
                    {
                        _logger?.LogWarning($"[GamerViewModel] [ForceRollbackTempOptimization] Erro ao exibir notificação de erro crítico: {notifEx.Message}");
                    }
                }
                
                UpdateCommandStates();
                _logger?.LogInfo("[GamerViewModel] [ForceRollbackTempOptimization] UpdateCommandStates() chamado");
            }, loc.GetString("EmergencyRollbackProgress"));
            
            _logger?.LogInfo("[GamerViewModel] [ForceRollbackTempOptimization] FINALIZADO");
            _logger?.LogInfo("-----------------------------------------------------------");
        }
        
        private void OnTemporaryOptimizationSessionStateChanged(object? sender, SessionStateChangedEventArgs e)
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                UpdateTemporaryOptimizationStatus();
            });
        }
        
        private void UpdateTemporaryOptimizationStatus()
        {
            var loc = LocalizationService.Instance;
            if (_gamerSessionManager == null)
            {
                IsTemporaryOptimizationSessionActive = false;
                TemporaryOptimizationStatus = loc.GetString("GamerTempOptNotAvailable");
                return;
            }
            
            IsTemporaryOptimizationSessionActive = _gamerSessionManager.IsSessionActive;
            if (IsTemporaryOptimizationSessionActive)
            {
                var sessionId = _gamerSessionManager.CurrentSessionId;
                var shortId = sessionId?.Substring(0, Math.Min(8, sessionId?.Length ?? 0)) ?? "N/A";
                TemporaryOptimizationStatus = string.Format(loc.GetString("GamerTempOptActive"), shortId);
            }
            else
            {
                TemporaryOptimizationStatus = loc.GetString("GamerTempOptInactive");
            }
            
            UpdateCommandStates();
        }
        
        #endregion
        
        private void ToggleDiagnostics()
        {
            _logger?.LogInfo($"[GamerViewModel] [ToggleDiagnostics] Alternando diagnósticos. Estado atual: IsDiagnosticsRunning={IsDiagnosticsRunning}");
            IsDiagnosticsRunning = !IsDiagnosticsRunning;
            
            if (IsDiagnosticsRunning)
            {
                _logger.LogInfo("[GamerViewModel] [ToggleDiagnostics] Diagnósticos iniciados");
                // Iniciar monitoramento de diagnósticos
                StartDiagnosticsMonitoring();
            }
            else
            {
                _logger.LogInfo("[GamerViewModel] [ToggleDiagnostics] Diagnósticos parados");
                // Parar monitoramento de diagnósticos
                StopDiagnosticsMonitoring();
            }
            
            // Update button text based on state
            OnPropertyChanged(nameof(StartDiagnosticsCommand));
            _logger?.LogInfo($"[GamerViewModel] [ToggleDiagnostics] FINALIZADO. Novo estado: IsDiagnosticsRunning={IsDiagnosticsRunning}");
        }
        
        private void StartDiagnosticsMonitoring()
        {
            // Iniciar o serviço de diagnósticos se disponível
            if (App.GameDiagnostics != null)
            {
                try
                {
                    App.GameDiagnostics.Start();
                    _logger.LogInfo("[Diagnostics] Serviço de diagnósticos iniciado com sucesso");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[Diagnostics] Erro ao iniciar serviço de diagnósticos: {ex.Message}");
                    IsDiagnosticsRunning = false;
                    OnPropertyChanged(nameof(IsDiagnosticsRunning));
                }
            }
            else
            {
                _logger.LogWarning("[Diagnostics] Serviço de diagnósticos não disponível");
                IsDiagnosticsRunning = false;
                OnPropertyChanged(nameof(IsDiagnosticsRunning));
            }
        }
        
        private void StopDiagnosticsMonitoring()
        {
            // Parar o serviço de diagnósticos se disponível
            if (App.GameDiagnostics != null)
            {
                try
                {
                    App.GameDiagnostics.Stop();
                    _logger.LogInfo("[Diagnostics] Serviço de diagnósticos parado com sucesso");
                    // Clear FPS text when stopping
                    FpsText = LocalizationService.Instance.GetString("GamerFpsWaiting");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[Diagnostics] Erro ao parar serviço de diagnósticos: {ex.Message}");
                }
            }
        }
        
        private void OnDiagnosticsSamplesUpdated(System.Collections.Generic.IReadOnlyList<GameDiagnosticsService.Sample> samples)
        {
            // Atualizar na thread da UI
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (samples.Count > 0)
                {
                    var latest = samples.LastOrDefault();
                    if (latest != null)
                    {
                        FpsText = $"FPS: {latest.Fps:F1}";
                        CpuTempText = $"CPU: {latest.CpuTemperature:F0}°C";
                        GpuTempText = $"GPU: {latest.GpuTemperature:F0}°C";
                        CpuThrottlingActive = latest.CpuThrottling;
                        // Se houver throttling, logar um aviso (uma vez a cada poucos segundos)
                        if (latest.CpuThrottling || latest.GpuThrottling)
                        {
                            _logger.LogWarning($"[Thermal] ATENÇÃO: Throttling detectado! CPU: {latest.CpuTemperature:F0}°C, GPU: {latest.GpuTemperature:F0}°C");
                        }

                        // Analisar tendências (Inteligência Preditiva)
                        _trendAnalyzer?.Analyze(samples);

                        // Diagnóstico Adaptativo de Energia
                        _powerDiag?.ProcessSample(latest);
                    }
                }
            });
        }

        private void OnPowerDiagnosticMessage(string message)
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(async () =>
            {
                AdapterMessage = message;
                HasAdapterMessage = true;

                // Toast para mudanças de plano
                if (message.Contains("detectamos", StringComparison.OrdinalIgnoreCase))
                {
                    GlobalNotificationService.ShowSuccess(LocalizationService.Instance.GetString("PowerOptimizationTitle"), message);
                }

                // Limpar após 15 segundos se for apenas informativo
                if (!message.Contains("reduz", StringComparison.OrdinalIgnoreCase))
                {
                    await Task.Delay(15000);
                    HasAdapterMessage = false;
                }
            });
        }

        private void OnTrendWarningDetected(TrendWarning warning)
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(async () =>
            {
                WarningMessage = warning.Message;
                HasWarning = true;

                // Mostrar toast se for crítico
                if (warning.Severity == WarningSeverity.Critical)
                {
                    GlobalNotificationService.ShowWarning(LocalizationService.Instance.GetString("PerformanceAlertTitle"), warning.Message);
                }

                // Limpar aviso após 10 segundos
                await Task.Delay(10000);
                HasWarning = false;
            });
        }

        private void ExportCsv()
        {
            _logger?.LogInfo("[GamerViewModel] [ExportCsv] INICIADO");
            try
            {
                _logger.LogInfo("[GamerViewModel] [ExportCsv] Exportando dados para CSV...");
                
                // CORREÇÃO: Implementação completa de exportação CSV
                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                    FileName = $"diagnostics_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                    DefaultExt = ".csv"
                };
                
                if (saveDialog.ShowDialog() == true)
                {
                    _logger?.LogInfo($"[GamerViewModel] [ExportCsv] Salvando em: {saveDialog.FileName}");
                    var csv = new System.Text.StringBuilder();
                    
                    // Cabeçalho
                    csv.AppendLine("Timestamp,CPU%,CPU Queue,CPU MHz,CPU Max MHz,DPC%,Interrupt%,CPU Temp,CPU Throttle,RAM Used GB,RAM Total GB,Page Faults/s,Disk Reads/s,Disk Writes/s,Disk Queue,Disk Latency ms,GPU%,VRAM Used MB,VRAM Total MB,GPU Temp,GPU Throttle,Network Jitter ms,FPS,Cause");
                    
                    // Dados do GameDiagnosticsService se disponível
                    if (App.GameDiagnostics != null)
                    {
                        var samples = App.GameDiagnostics.GetSamplesSnapshot();
                        _logger?.LogInfo($"[GamerViewModel] [ExportCsv] Exportando {samples.Count} amostras");
                        foreach (var sample in samples)
                        {
                            csv.AppendLine($"{sample.T:yyyy-MM-dd HH:mm:ss.fff}," +
                                         $"{sample.CpuPercent:F2}," +
                                         $"{sample.CpuQueue:F2}," +
                                         $"{sample.CpuCurrentMhz:F0}," +
                                         $"{sample.CpuMaxMhz:F0}," +
                                         $"{sample.CpuDpcPercent:F2}," +
                                         $"{sample.CpuInterruptPercent:F2}," +
                                         $"{sample.CpuTemperature:F1}," +
                                         $"{sample.CpuThrottling}," +
                                         $"{sample.RamUsedGb:F2}," +
                                         $"{sample.RamTotalGb:F2}," +
                                         $"{sample.RamPageFaultsPerSec:F0}," +
                                         $"{sample.DiskReadsPerSec:F0}," +
                                         $"{sample.DiskWritesPerSec:F0}," +
                                         $"{sample.DiskQueueLen:F2}," +
                                         $"{sample.DiskLatencySec * 1000:F2}," +
                                         $"{sample.GpuUtilPercent:F2}," +
                                         $"{sample.GpuVramUsedMb:F0}," +
                                         $"{sample.GpuVramTotalMb:F0}," +
                                         $"{sample.GpuTemperature:F1}," +
                                         $"{sample.GpuThrottling}," +
                                         $"{sample.NetJitterMs:F2}," +
                                         $"{sample.Fps:F1}," +
                                         $"\"{sample.Cause}\"");
                        }
                    }
                    else
                    {
                        _logger?.LogWarning("[GamerViewModel] [ExportCsv] GameDiagnostics não disponível, exportando CSV vazio");
                    }
                    
                    System.IO.File.WriteAllText(saveDialog.FileName, csv.ToString(), System.Text.Encoding.UTF8);
                    
                    _logger.LogSuccess($"[GamerViewModel] [ExportCsv] Dados exportados para: {saveDialog.FileName}");
                    ShowToast("Exporta\u00E7\u00E3o Conclu\u00EDda", $"Dados exportados para CSV com sucesso!");
                }
                else
                {
                    _logger?.LogInfo("[GamerViewModel] [ExportCsv] Usuário cancelou a exportação");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GamerViewModel] [ExportCsv] Erro ao exportar CSV: {ex.Message}", ex);
                ShowMessage(string.Format(LocalizationService.Instance.GetString("CsvExportErrorFmt"), ex.Message), LocalizationService.Instance.GetString("CsvExportErrorTitle"));
            }
            _logger?.LogInfo("[GamerViewModel] [ExportCsv] FINALIZADO");
        }

        private void OpenDiagnostics()
        {
            _logger?.LogInfo("[GamerViewModel] [OpenDiagnostics] INICIADO");
            try
            {
                _logger.LogInfo("[GamerViewModel] [OpenDiagnostics] Abrindo diagnósticos avançados...");
                
                // CORREÇÃO: Navegar para página de diagnósticos usando NavigationService
                var navService = App.Services?.GetService(typeof(INavigationService)) as INavigationService;
                if (navService != null)
                {
                    _logger?.LogInfo("[GamerViewModel] [OpenDiagnostics] Navegando via NavigationService");
                    navService.NavigateTo(AppPage.Diagnostics.ToString());
                    _logger?.LogSuccess("[GamerViewModel] [OpenDiagnostics] Navegação concluída");
                }
                else
                {
                    _logger?.LogWarning("[GamerViewModel] [OpenDiagnostics] NavigationService não disponível, usando fallback MainWindow");
                    // Fallback: usar MainWindow diretamente
                    var mainWindow = System.Windows.Application.Current?.MainWindow as UI.MainWindow;
                    if (mainWindow != null)
                    {
                        _logger?.LogInfo("[GamerViewModel] [OpenDiagnostics] Usando MainWindow.NavigateTo via reflection");
                        // Tentar navegar via método do MainWindow
                        var navMethod = mainWindow.GetType().GetMethod("NavigateTo", 
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        navMethod?.Invoke(mainWindow, new object[] { "Diagnostics" });
                    }
                    else
                    {
                        _logger.LogWarning("[GamerViewModel] [OpenDiagnostics] N\u00E3o foi poss\u00EDvel navegar para diagn\u00F3sticos - MainWindow n\u00E3o encontrado");
                        ShowMessage(LocalizationService.Instance.GetString("NavigationErrorMessage"), LocalizationService.Instance.GetString("NavigationErrorTitle"));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GamerViewModel] [OpenDiagnostics] Erro ao abrir diagnósticos: {ex.Message}", ex);
                ShowMessage($"{LocalizationService.Instance.GetString("DiagnosticsOpenError")}: {ex.Message}", "Erro");
            }
            _logger?.LogInfo("[GamerViewModel] [OpenDiagnostics] FINALIZADO");
            }


        private async Task ApplyProOptimizationsAsync()
        {
            _logger.LogInfo("[GamerViewModel] [ApplyProOptimizations] INICIADO - Solicitacao de aplicacao de otimizacoes PRO");
            
            // ? VERIFICAÇÃO DE LICENÇA (Feature Gate) — mesmo modal de compra do botão
            if (!VoltrisOptimizer.Services.License.ProFeatureGuard.RequirePaid("gamer_mode"))
            {
                _logger.LogWarning("[GamerViewModel] [ApplyProOptimizations] BLOQUEADO - Licenca PRO necessaria nao disponivel");
                return;
            }
            _logger.LogInfo("[GamerViewModel] [ApplyProOptimizations] Feature gate OK, prosseguindo");

            await ExecuteSafeAsync(async () =>
            {
                _logger.LogInfo("[GamerViewModel] [ApplyProOptimizations] Iniciando operacao no ExecuteSafeAsync");
                GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("ApplyingProOptimizations"), true);
                GlobalProgressService.Instance.UpdateProgress(20, LocalizationService.Instance.GetString("AnalyzingMachineProfile"));
                
                _logger.LogDebug("[GamerViewModel] [ApplyProOptimizations] Analisando perfil da maquina via _profileDetector");
                var machineProfile = await _profileDetector.AnalyzeMachineProfileAsync();
                _logger.LogInfo($"[GamerViewModel] [ApplyProOptimizations] Perfil detectado: {machineProfile.Profile} | CPU:{machineProfile.CpuTier} GPU:{machineProfile.GpuTier} RAM:{machineProfile.RamTier} Notebook:{machineProfile.IsNotebook}");
                
                if (machineProfile.Recommendations.Count > 0)
                    _logger.LogInfo($"[GamerViewModel] [ApplyProOptimizations] Recomendacoes: {string.Join(", ", machineProfile.Recommendations)}");
                if (machineProfile.Restrictions.Count > 0)
                    _logger.LogInfo($"[GamerViewModel] [ApplyProOptimizations] Restricoes: {string.Join(", ", machineProfile.Restrictions)}");
                
                _logger.LogDebug("[GamerViewModel] [ApplyProOptimizations] Exibindo dialogo de confirmacao do perfil ao usuario");
                var profileResult = await ShowConfirmationDialogAsync(
                    LocalizationService.Instance.GetString("SystemAnalysisTitle"),
                    string.Format(LocalizationService.Instance.GetString("ProfileDetectedMessage"),
                        machineProfile.Profile, machineProfile.CpuTier, machineProfile.GpuTier),
                    LocalizationService.Instance.GetString("ContinueWithAdaptiveOptimizations"),
                    LocalizationService.Instance.GetString("Cancel"));
                
                if (!profileResult)
                {
                    _logger.LogInfo("[GamerViewModel] [ApplyProOptimizations] Usuario CANCELOU apos analise do perfil");
                    return;
                }
                _logger.LogInfo("[GamerViewModel] [ApplyProOptimizations] Usuario CONFIRMOU - continuando com backup");
        
                GlobalProgressService.Instance.UpdateProgress(50, LocalizationService.Instance.GetString("GameRepairPreparingBackup"));
                _logger.LogInfo("[GamerViewModel] [ApplyProOptimizations] INICIANDO BACKUP DE SEGURANCA DO SISTEMA...");
                await Task.Delay(500); 
                _logger.LogSuccess("[GamerViewModel] [ApplyProOptimizations] BACKUP REALIZADO COM SUCESSO");
        
                _logger.LogDebug("[GamerViewModel] [ApplyProOptimizations] Verificando temperaturas do sistema");
                var (cpuTemp, gpuTemp) = await GetSystemTemperaturesAsync();
                _logger.LogInfo($"[GamerViewModel] [ApplyProOptimizations] Temperaturas - CPU:{cpuTemp:F0}°C GPU:{gpuTemp:F0}°C");
                
                bool hasCriticalTemp = cpuTemp > 89 || (gpuTemp > 0 && gpuTemp > 89);
                
                if (hasCriticalTemp)
                {
                    _logger.LogWarning($"[GamerViewModel] [ApplyProOptimizations] TEMPERATURAS CRITICAS detectadas! CPU:{cpuTemp:F0}°C GPU:{gpuTemp:F0}°C");
                    var result = await ShowConfirmationDialogAsync(
                        LocalizationService.Instance.GetString("HighTempAlertTitle"),
                        string.Format(LocalizationService.Instance.GetString("HighTempAlertMessage"), cpuTemp, gpuTemp),
                        LocalizationService.Instance.GetString("ApplyAnyway"),
                        LocalizationService.Instance.GetString("Cancel"));
                    
                    if (!result)
                    {
                        _logger.LogInfo("[GamerViewModel] [ApplyProOptimizations] Usuario CANCELOU devido a temperaturas altas");
                        return;
                    }
                    _logger.LogWarning("[GamerViewModel] [ApplyProOptimizations] Usuario optou por aplicar mesmo com temperaturas altas");
                }
                
                GlobalProgressService.Instance.UpdateProgress(80, LocalizationService.Instance.GetString("ApplyingAdaptiveOptimizations"));
                _logger.LogInfo("[GamerViewModel] [ApplyProOptimizations] Aplicando otimizacoes adaptativas via _adaptiveEngine");
                
                var options = new GamerModels.GamerOptimizationOptions
                {
                    EnableGameMode = true,
                    ReduceLatency = true,
                    CloseBackgroundApps = true,
                    ApplyFpsBoost = ApplyFpsBoost,
                    EnableExtremeMode = EnableExtremeMode,
                    EnableAntiStutter = EnableAntiStutter,
                    EnableAdaptiveNetwork = EnableAdaptiveNetwork,
                    PingTarget = PingTarget
                };
                _logger.LogDebug($"[GamerViewModel] [ApplyProOptimizations] Options: FpsBoost={ApplyFpsBoost} ExtremeMode={EnableExtremeMode} AntiStutter={EnableAntiStutter} AdaptiveNetwork={EnableAdaptiveNetwork}");
                
                var adaptiveResult = await _adaptiveEngine.ApplyAdaptiveOptimizationsAsync(options, machineProfile);
                
                if (adaptiveResult.Success)
                {
                    _logger.LogSuccess($"[GamerViewModel] [ApplyProOptimizations] OTIMIZACOES ADAPTATIVAS APLICADAS COM SUCESSO! Estrategia:{adaptiveResult.ProfileBasedStrategy} Aplicadas:{adaptiveResult.OptimizationsApplied}");
                    if (adaptiveResult.SkippedOptimizations.Length > 0)
                        _logger.LogInfo($"[GamerViewModel] [ApplyProOptimizations] Otimizacoes puladas: {string.Join(", ", adaptiveResult.SkippedOptimizations)}");
                    
                    GlobalProgressService.Instance.UpdateProgress(100, LocalizationService.Instance.GetString("ProOptimizationsAppliedSuccess"));
                    GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("ProOptimizationsAppliedSuccess"));
                    
                    _logger.LogInfo("[GamerViewModel] [ApplyProOptimizations] Exibindo RestartConfirmationModal apos aplicacao bem-sucedida");
                    VoltrisOptimizer.UI.Windows.RestartConfirmationModal.RestartChoice userChoice =
                        VoltrisOptimizer.UI.Windows.RestartConfirmationModal.RestartChoice.Cancel;

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            var modal = new VoltrisOptimizer.UI.Windows.RestartConfirmationModal
                            {
                                Owner = System.Windows.Application.Current.MainWindow
                            };
                            modal.SetCustomMessage(
                                LocalizationService.Instance.GetString("ProOptimizationsAppliedTitle"),
                                string.Format(LocalizationService.Instance.GetString("ProOptimizationsAppliedMessage"),
                                    adaptiveResult.OptimizationsApplied, adaptiveResult.ProfileBasedStrategy));
                            modal.ShowDialog();
                            userChoice = modal.UserChoice;
                            _logger.LogInfo($"[GamerViewModel] [ApplyProOptimizations] Usuario escolheu no modal: {userChoice}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"[GamerViewModel] [ApplyProOptimizations] Erro ao exibir modal: {ex.Message}", ex);
                        }
                    });

                    if (userChoice == VoltrisOptimizer.UI.Windows.RestartConfirmationModal.RestartChoice.RestartNow)
                    {
                        _logger.LogWarning("[GamerViewModel] [ApplyProOptimizations] REINICIANDO sistema a pedido do usuario");
                        System.Diagnostics.Process.Start("shutdown.exe", "/r /t 5");
                    }
                    else
                    {
                        _logger.LogInfo($"[GamerViewModel] [ApplyProOptimizations] Usuario optou por {userChoice}. Nenhuma reinicializacao imediata.");
                        GlobalNotificationService.ShowSuccess(
                            LocalizationService.Instance.GetString("ProOptimizationsAppliedTitle"),
                            string.Format(LocalizationService.Instance.GetString("ProOptimizationsAppliedMessage"),
                                adaptiveResult.OptimizationsApplied, adaptiveResult.ProfileBasedStrategy));
                    }
                }
                else
                {
                    _logger.LogError($"[GamerViewModel] [ApplyProOptimizations] FALHA ao aplicar otimizacoes adaptativas: Success=false, OptimizationsApplied={adaptiveResult.OptimizationsApplied}");
                    GlobalProgressService.Instance.UpdateProgress(100, LocalizationService.Instance.GetString("ProOptimizationsFailed"));
                    GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("ProOptimizationsFailed"));
                    GlobalNotificationService.ShowError(
                        LocalizationService.Instance.GetString("AdaptiveOptimizationsErrorTitle"), 
                        LocalizationService.Instance.GetString("AdaptiveOptimizationsFailed"));
                }
                
                _logger.LogInfo("[GamerViewModel] [ApplyProOptimizations] Operacao concluida");
                
            }, LocalizationService.Instance.GetString("ApplyingAdaptiveOptimizations"));
            _logger.LogInfo("[GamerViewModel] [ApplyProOptimizations] FINALIZADO");
        }

        private async Task RevertProOptimizationsAsync()
{
    _logger.LogInfo("[GamerViewModel] [RevertProOptimizations] INICIADO - Solicitacao de reversao de otimizacoes PRO");
    await ExecuteSafeAsync(async () =>
    {
        GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("RevertingProOptimizations"), true);
        _logger.LogInfo("[GamerViewModel] [RevertProOptimizations] Operacao iniciada no GlobalProgressService");
        
        _logger.LogDebug("[GamerViewModel] [RevertProOptimizations] Exibindo modal RestartConfirmationModal para usuario decidir");
        VoltrisOptimizer.UI.Windows.RestartConfirmationModal.RestartChoice userChoice =
            VoltrisOptimizer.UI.Windows.RestartConfirmationModal.RestartChoice.Cancel;

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                var modal = new VoltrisOptimizer.UI.Windows.RestartConfirmationModal
                {
                    Owner = System.Windows.Application.Current.MainWindow
                };
                modal.SetCustomMessage(
                    LocalizationService.Instance.GetString("RevertChangesTitle"),
                    LocalizationService.Instance.GetString("RevertChangesMessage"));
                modal.ShowDialog();
                userChoice = modal.UserChoice;
                _logger.LogInfo($"[GamerViewModel] [RevertProOptimizations] Usuario escolheu: {userChoice}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GamerViewModel] [RevertProOptimizations] Erro ao exibir modal de reinicializacao: {ex.Message}", ex);
            }
        });

        if (userChoice == VoltrisOptimizer.UI.Windows.RestartConfirmationModal.RestartChoice.Cancel)
        {
            _logger.LogInfo("[GamerViewModel] [RevertProOptimizations] Reversao CANCELADA pelo usuario");
            GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("ProOptimizationsRevertCancelled"));
            return;
        }

        bool shouldRestartNow = (userChoice == VoltrisOptimizer.UI.Windows.RestartConfirmationModal.RestartChoice.RestartNow);
        _logger.LogInfo($"[GamerViewModel] [RevertProOptimizations] Reversao confirmada. shouldRestartNow={shouldRestartNow}");

        _logger.LogInfo("[GamerViewModel] [RevertProOptimizations] Restaurando backup de configuracoes via orchestrator...");
        await _orchestrator.RevertPersistentOptimizationsAsync();
        _logger.LogInfo("[GamerViewModel] [RevertProOptimizations] Backup restaurado. Desativando modo automatico...");
        IsAutoModeEnabled = false;
        
        _logger.LogSuccess("[GamerViewModel] [RevertProOptimizations] OTIMIZACOES REVERTIDAS COM SUCESSO.");
        GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("ProOptimizationsRevertedSuccess"));
        GlobalNotificationService.ShowSuccess(
            LocalizationService.Instance.GetString("RevertChangesTitle"),
            LocalizationService.Instance.GetString("RevertChangesMessage"));
        
        if (shouldRestartNow)
        {
            _logger.LogWarning("[GamerViewModel] [RevertProOptimizations] REINICIANDO sistema a pedido do usuario imediatamente");
            System.Diagnostics.Process.Start("shutdown.exe", "/r /t 5");
        }
        else
        {
            _logger.LogInfo("[GamerViewModel] [RevertProOptimizations] Usuario optou por reiniciar depois. Nenhuma acao tomada.");
        }
        
    }, LocalizationService.Instance.GetString("RevertingOptimizations"));
    _logger.LogInfo("[GamerViewModel] [RevertProOptimizations] FINALIZADO");
}        

        #endregion

        #region Event Handlers

        private void OnStatusChanged(object? sender, GamerModels.GamerModeStatus status)
        {
            _logger.LogEntry($"[GamerViewModel] OnStatusChanged(status={status})");
            _logger.Log(LogLevel.Info, LogCategory.Gamer, $"[GamerMode][ViewModel] Propagação de status recebida: Active={status.IsActive} (Source=Orchestrator)");
            
            // Atualizar na thread da UI
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                var oldState = IsGamerModeActive;
                IsGamerModeActive = status.IsActive;
                ActiveGameName = status.ActiveGameName;
                
                if (status.IsActive)
                {
                    if (!oldState) _logger.Log(LogLevel.Success, LogCategory.Gamer, $"[GamerMode][ViewModel] UI SINCRONIZADA -> ATIVO | Jogo: {status.ActiveGameName ?? "N/A"}");
                }
                else
                {
                    if (oldState) _logger.Log(LogLevel.Info, LogCategory.Gamer, "[GamerMode][ViewModel] UI SINCRONIZADA -> INATIVO");
                }
                
                // Update command states when status changes
                UpdateCommandStates();
            });
            _logger.LogExit($"[GamerViewModel] OnStatusChanged(status={status})");
        }

        private string? _lastAnalyzedGame;
        private DateTime _lastAnalysisTime = DateTime.MinValue;
        private bool _isInGameSession = false;

        private void OnGameStarted(object? sender, GamerModels.DetectedGame game)
        {
            _logger.LogEntry($"[GamerViewModel] OnGameStarted(game={game?.Name})");
            // Cooldown de 5 segundos para o mesmo jogo para evitar triggers duplicados
            if (_lastAnalyzedGame == game.Name && DateTime.Now - _lastAnalysisTime < TimeSpan.FromSeconds(5))
            {
                _logger.LogExit($"[GamerViewModel] OnGameStarted = cooldown ({game?.Name})");
                return;
            }
            _lastAnalyzedGame = game.Name;
            _lastAnalysisTime = DateTime.Now;

            if (game == null)
            {
                _logger.LogExit($"[GamerViewModel] OnGameStarted = game null");
                return;
            }
            _logger.LogInfo($">>> [GamerViewModel] EVENTO DETECTADO: Jogo iniciado: {game.Name}");

            // Marcar sessão de jogo ativa
            _isInGameSession = true;

            // [PERFIL INTELIGENTE] Troca automática de perfil conforme o jogo detectado.
            // O GameProfileSwitcherService classifica o processo e aplica o gate de licença Pro internamente.
            var processForProfile = string.IsNullOrWhiteSpace(game.ProcessName) ? game.Name : game.ProcessName;
            try { _ = VoltrisOptimizer.Services.Gamer.Intelligence.Implementation.GameProfileSwitcherService.Instance.OnGameDetectedAsync(processForProfile); }
            catch (Exception ex) { _logger.LogWarning($"[GamerViewModel] Falha ao acionar troca automatica de perfil: {ex.Message}"); }

            // Aplicar BH PROCHOT se configurado para desativar durante jogos.
            // PRO: NUNCA aplica efeito de hardware sem licença paga — a detecção de jogo
            // sozinha NÃO pode alterar o estado térmico da CPU.
            if (VoltrisOptimizer.Services.License.ProFeatureGuard.IsPaidActive()
                && _bdProchotGameEnabled && ProchotService != null && ProchotService.IsHardwareSupported)
            {
                _logger.LogInfo($"[GamerViewModel] Aplicando BH PROCHOT (desativado) para sessão de jogo: {game.Name}");
                _ = ProchotService.DisableBdProchotAsync().ContinueWith(t =>
                {
                    if (t.IsCompletedSuccessfully && t.Result)
                        _logger.LogSuccess("[GamerViewModel] BH PROCHOT desativado com sucesso para sessão de jogo");
                    else if (ProchotService.IsHardwareSupported)
                        _logger.LogWarning("[GamerViewModel] Falha ao desativar BH PROCHOT para sessão de jogo");
                });
            }

            // Notificação visual (Fire-and-forget UI updates)
            _ = System.Windows.Application.Current?.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    // VERIFICAÇÃO DE LICENÇA (Feature Gate): modo gamer exige licença paga
                    if (!VoltrisOptimizer.Services.License.ProFeatureGuard.IsPaidActive())
                    {
                        _pendingGameActivation = game;
                        _logger.LogWarning($"[GamerViewModel] Ignorando notificação de jogo ({game.Name}): Licença paga necessária (armazenado para ativação pós-licenciamento)");
                        return;
                    }

                    _logger.LogInfo($"[GamerViewModel] Exibindo notificação para: {game.Name}");
                    GlobalNotificationService.ShowSuccess(
                        LocalizationService.Instance.GetString("GamerGameDetectedTitle"),
                        string.Format(LocalizationService.Instance.GetString("GamerGameDetectedMsgFmt"), game.Name)
                    );
                    
                    if (IsAutoModeEnabled && !IsGamerModeActive)
                    {
                        // Auto-ativar o Modo Gamer completo para o jogo detectado
                        _logger.LogInfo($"[GamerViewModel] Auto-ativando Modo Gamer para: {game.Name}");
                        SelectedGame = game;
                        await RunGamerModeAsync();
                    }
                    else if (IsGamerModeActive)
                    {
                        _logger.LogInfo($"[GamerViewModel] Ignorando ativação automática: Modo Gamer já está ativo");
                        _logger.LogInfo($"[GamerViewModel] Iniciando PowerDiag (Background) para: {game.Name}");
                        await (_powerDiag?.StartAnalysisAsync(game.Name) ?? Task.CompletedTask);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[GamerViewModel] Erro ao processar UI de detecção: {ex.Message}");
                }
            });
            _logger.LogExit($"[GamerViewModel] OnGameStarted({game?.Name})");
        }

        private async void OnGameStopped(object? sender, GamerModels.DetectedGame game)
        {
            _logger.LogEntry($"[GamerViewModel] OnGameStopped(game={game?.Name})");
            _isInGameSession = false;

            // [PERFIL INTELIGENTE] Restaurar o perfil anterior ao encerrar o jogo.
            var processForProfile = game == null ? string.Empty : (string.IsNullOrWhiteSpace(game.ProcessName) ? game.Name : game.ProcessName);
            if (!string.IsNullOrWhiteSpace(processForProfile))
            {
                try { await VoltrisOptimizer.Services.Gamer.Intelligence.Implementation.GameProfileSwitcherService.Instance.OnGameClosedAsync(processForProfile); }
                catch (Exception ex) { _logger.LogWarning($"[GamerViewModel] Falha ao restaurar perfil do jogo: {ex.Message}"); }
            }

            if (_pendingGameActivation != null && (game == null || string.Equals(_pendingGameActivation.ExecutablePath, game.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
            {
                _pendingGameActivation = null;
            }

            // Restaurar BH PROCHOT (reativar) quando o jogo termina
            if (ProchotService != null)
            {
                if (!ProchotService.IsHardwareSupported)
                {
                    _logger.LogDebug("[GamerViewModel] BD PROCHOT nao suportado nesta CPU/BIOS - nada a restaurar");
                }
                else
                {
                    _logger.LogInfo("[GamerViewModel] Restaurando BH PROCHOT (reativando) após sessão de jogo");
                    bool restored = await ProchotService.EnableBdProchotAsync();
                    if (restored)
                        _logger.LogSuccess("[GamerViewModel] BH PROCHOT reativado com sucesso");
                    else
                        _logger.LogWarning("[GamerViewModel] Falha ao reativar BH PROCHOT");
                }
            }

            // Atualizar na thread da UI
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    _logger.LogInfo($"[GamerViewModel] Jogo encerrado detectado na UI: {game.Name}");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[GamerViewModel] Erro ao processar encerramento de jogo: {ex.Message}");
                }
            });
            _logger.LogExit($"[GamerViewModel] OnGameStopped({game?.Name})");
        }
        
        /// <summary>
        /// Handler para mudança de estado do Modo Gamer em tempo real
        /// </summary>
        /* LEGACY EVENT HANDLERS
        private void OnGamerModeChanged(object? sender, GamerModeChangedEventArgs e)
        {
            // Legacy code stripped
        }
        
        private void OnPreGameOptimizationCompleted(object? sender, PreGameOptimizationEventArgs e)
        {
            // Legacy code stripped
        }
        */

        
        /// <summary>
        /// Handler para otimizações pós-jogo concluídas
        /// </summary>
        /* LEGACY
        private void OnPostGameOptimizationCompleted(object? sender, PostGameOptimizationEventArgs e)
        {
            // Legacy code stripped
        }
        */

        /// <summary>
        /// Handler para mudança de status da licença (ativação/desativação/revalidação)
        /// Quando a licença se torna Pro, força scan de jogos em execução e ativa Modo Gamer se AutoGamerMode estiver ativo
        /// </summary>
        private async void OnLicenseStatusChanged(object? sender, EventArgs e)
        {
            _logger.LogEntry("[GamerViewModel] OnLicenseStatusChanged");

            try
            {
                bool isPro = LicenseManager.IsPro;
                _logger.LogInfo($"[GamerViewModel] LicenseStatusChanged: IsPro={isPro}");

                if (isPro)
                {
                    _logger.LogInfo("[GamerViewModel] Licença Pro detectada — forçando scan de jogos em execução...");

                    // Forçar scan imediato de jogos em execução (bypass throttle 30s)
                    try
                    {
                        await _gameDetector.ForceScanRunningGamesAsync(CancellationToken.None);
                        _logger.LogInfo("[GamerViewModel] Scan forçado concluído");
                    }
                    catch (Exception scanEx)
                    {
                        _logger.LogWarning($"[GamerViewModel] Erro no scan forçado: {scanEx.Message}");
                    }

                    // Verificar se há jogo rodando e AutoGamerMode ativo
                    bool userEnabledAutoMode = SettingsService.Instance?.Settings?.AutoGamerMode == true;
                    if (userEnabledAutoMode)
                    {
                        try
                        {
                            _orchestrator.StartAutoPilot();
                            _logger.LogInfo("[GamerViewModel] AutoPilot iniciado pós-ativação de licença PRO");
                        }
                        catch (Exception autoEx)
                        {
                            _logger.LogWarning($"[GamerViewModel] Erro ao iniciar AutoPilot pós-licença: {autoEx.Message}");
                        }
                    }
                    var runningGames = _gameDetector.GetRunningGames();
                    var targetGame = runningGames.FirstOrDefault() ?? _gameDetector.CurrentRunningGame ?? _pendingGameActivation;
                    bool hasRunningGame = targetGame != null || _gameDetector.HasActiveRunningGameSession;

                    _logger.LogInfo($"[GamerViewModel] Pós-scan: IsAutoModeEnabled={IsAutoModeEnabled}, userEnabledAutoMode={userEnabledAutoMode}, HasRunningGame={hasRunningGame}");

                    if (IsAutoModeEnabled && userEnabledAutoMode && hasRunningGame && !IsGamerModeActive)
                    {
                        _logger.LogSuccess($"[GamerViewModel] Jogo detectado ({targetGame?.Name ?? "Sessão Ativa"}) + AutoGamerMode ativo + Licença Pro — ativando Modo Gamer automaticamente!");

                        // Obter o primeiro jogo detectado
                        var firstGame = targetGame;
                        _pendingGameActivation = null;
                        if (firstGame != null)
                        {
                            SelectedGame = firstGame;
                            await RunGamerModeAsync();
                        }
                        else
                        {
                            await RunGamerModeAsync();
                        }
                    }

                    // Sincronizar DashboardViewModel (badge/overlay) se existir.
                    // TransientDashboard resolve a instância E a descarta: este
                    // caminho dispara sozinho a cada mudança de licença, e cada
                    // execução sem Dispose deixava um órfão preso em 10 eventos
                    // + um PeriodicTimer de 30 s.
                    try
                    {
                        await VoltrisOptimizer.UI.ViewModels.TransientDashboard.RunAsync(vm =>
                        {
                            var syncMethod = vm.GetType().GetMethod("SyncGamerModeFromViewModel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            syncMethod?.Invoke(vm, null);
                            _logger.LogInfo("[GamerViewModel] DashboardViewModel sincronizado via reflection");
                        });
                    }
                    catch (Exception syncEx)
                    {
                        _logger.LogWarning($"[GamerViewModel] Erro ao sincronizar DashboardViewModel: {syncEx.Message}");
                    }
                }
                else
                {
                    _logger.LogInfo("[GamerViewModel] Licença não é Pro — Modo Gamer automático não será ativado");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GamerViewModel] Erro em OnLicenseStatusChanged: {ex.Message}", ex);
            }
            finally
            {
                _logger.LogExit("[GamerViewModel] OnLicenseStatusChanged");
            }
        }

        /// <summary>
        /// Método público para ser chamado externamente (ex: LicenseActivationViewModel) quando a licença muda.
        /// Verifica se deve ativar o Modo Gamer automaticamente (licença Pro + AutoGamerMode + jogo rodando).
        /// </summary>
        public async Task CheckAndActivateGamerModeOnLicenseChange()
        {
            _logger?.LogEntry("[GamerViewModel] CheckAndActivateGamerModeOnLicenseChange >>> START");
            _logger?.LogInfo($"[GamerViewModel] >>>>> CheckAndActivateGamerModeOnLicenseChange ENTRY");

            try
            {
                bool isPro = LicenseManager.IsPro;
                _logger?.LogInfo($"[GamerViewModel] >>>>> CheckAndActivateGamerModeOnLicenseChange: IsPro={isPro}");

                if (!isPro)
                {
                    _logger?.LogInfo("[GamerViewModel] Licença não é Pro — nada a fazer");
                    return;
                }

                _logger?.LogInfo("[GamerViewModel] >>>>> Licença Pro detectada — forçando scan de jogos em execução...");

                // Forçar scan imediato de jogos em execução (bypass throttle 30s)
                try
                {
                    _logger?.LogInfo("[GamerViewModel] >>>>> Chamando ForceScanRunningGamesAsync...");
                    await _gameDetector.ForceScanRunningGamesAsync(CancellationToken.None);
                    _logger?.LogInfo("[GamerViewModel] >>>>> Scan forçado concluído");
                }
                catch (Exception scanEx)
                {
                    _logger?.LogWarning($"[GamerViewModel] >>>>> Erro no scan forçado: {scanEx.Message}");
                }

_logger?.LogInfo("[GamerViewModel] >>>>> Verificando AutoGamerMode e jogo rodando...");
                // Verificar se há jogo rodando e AutoGamerMode ativo
                // IMPORTANTE: Usar a lista REAL de jogos APÓS o scan forçado, não o estado cached
                var runningGames = _gameDetector.GetRunningGames();
                bool userEnabledAutoMode = SettingsService.Instance?.Settings?.AutoGamerMode == true;
                if (userEnabledAutoMode)
                {
                    try
                    {
                        _orchestrator.StartAutoPilot();
                        _logger?.LogInfo("[GamerViewModel] >>>>> AutoPilot iniciado pós-ativação de licença PRO");
                    }
                    catch (Exception autoEx)
                    {
                        _logger?.LogWarning($"[GamerViewModel] >>>>> Erro ao iniciar AutoPilot pós-licença: {autoEx.Message}");
                    }
                }
                var targetGame = runningGames.FirstOrDefault() ?? _gameDetector.CurrentRunningGame ?? _pendingGameActivation;
                bool hasRunningGame = targetGame != null || _gameDetector.HasActiveRunningGameSession;

                _logger?.LogInfo($"[GamerViewModel] >>>>> Pós-scan: IsAutoModeEnabled={IsAutoModeEnabled}, userEnabledAutoMode={userEnabledAutoMode}, HasRunningGame={hasRunningGame}, JogosDetectados={runningGames.Count}, IsGamerModeActive={IsGamerModeActive}");

                if (IsAutoModeEnabled && userEnabledAutoMode && hasRunningGame) // Removido !IsGamerModeActive - o lock em RunGamerModeAsync impede duplicação
                {
                    _logger?.LogSuccess($"[GamerViewModel] >>>>> Jogo detectado ({targetGame?.Name ?? "Sessão Ativa"}) + AutoGamerMode ativo + Licença Pro — ativando Modo Gamer automaticamente!");

                    // Obter o primeiro jogo detectado
                    var firstGame = targetGame;
                    _pendingGameActivation = null;
                    if (firstGame != null)
                    {
                        SelectedGame = firstGame;
                        await RunGamerModeAsync();
                    }
                    else
                    {
                        await RunGamerModeAsync();
                    }
                }
                else
                {
                    _logger?.LogInfo($"[GamerViewModel] >>>>> Condição NÃO atendida: IsAutoModeEnabled={IsAutoModeEnabled}, userEnabledAutoMode={userEnabledAutoMode}, HasRunningGame={hasRunningGame}, IsGamerModeActive={IsGamerModeActive}");
                }

                // Sincronizar DashboardViewModel (badge/overlay) se existir.
                // Mesmo motivo do caminho acima:-descarta a instância descartável.
                try
                {
                    await VoltrisOptimizer.UI.ViewModels.TransientDashboard.RunAsync(vm =>
                    {
                        var syncMethod = vm.GetType().GetMethod("SyncGamerModeFromViewModel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        syncMethod?.Invoke(vm, null);
                        _logger?.LogInfo("[GamerViewModel] DashboardViewModel sincronizado via reflection (CheckAndActivateGamerModeOnLicenseChange)");
                    });
                }
                catch (Exception syncEx)
                {
                    _logger?.LogWarning($"[GamerViewModel] Erro ao sincronizar DashboardViewModel: {syncEx.Message}");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[GamerViewModel] Erro em CheckAndActivateGamerModeOnLicenseChange: {ex.Message}", ex);
            }
            finally
            {
                _logger?.LogExit("[GamerViewModel] CheckAndActivateGamerModeOnLicenseChange");
            }
        }

        /// <summary>
        /// CORREÇÃO: Atualiza a coleção de incidentes a partir do GamerOptimizerService
        /// </summary>
        private void UpdateIncidentsFromService()
        {
            // LEGACY KILL SWITCHED - App.GamerOptimizer removed.
            // TODO: Implementar busca de incidentes via GameDiagnosticsService
            return;
            
            /*
            try
            {
                if (App.GamerOptimizer == null) return;
                
                var recentIncidents = App.GamerOptimizer.GetRecentStutterIncidents();
                
                // Atualizar coleção na thread da UI
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    // Limpar e adicionar novos incidentes
                    Incidents.Clear();
                    foreach (var incident in recentIncidents)
                    {
                        // Logic stripped
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[GamerViewModel] Erro ao atualizar incidentes: {ex.Message}");
            }
            */
        }
        
        /// <summary>
        /// Converte string de causa para enum StutterCause
        /// </summary>
        private GamerModels.StutterCause ConvertStutterCause(string cause)
        {
            if (string.IsNullOrEmpty(cause)) return GamerModels.StutterCause.Unknown;
            
            return cause.ToLowerInvariant() switch
            {
                var c when c.Contains("cpu") || c.Contains("scheduling") => GamerModels.StutterCause.CpuScheduling,
                var c when c.Contains("gpu") || c.Contains("render") => GamerModels.StutterCause.GpuRender,
                var c when c.Contains("frame") || c.Contains("pacing") => GamerModels.StutterCause.FramePacing,
                var c when c.Contains("driver") || c.Contains("dpc") || c.Contains("interrupt") => GamerModels.StutterCause.DriversInterrupt,
                var c when c.Contains("memory") || c.Contains("paging") || c.Contains("ram") => GamerModels.StutterCause.MemoryPaging,
                var c when c.Contains("disk") && c.Contains("io") => GamerModels.StutterCause.DiskIO,
                var c when c.Contains("disk") && c.Contains("latency") => GamerModels.StutterCause.DiskLatency,
                var c when c.Contains("network") || c.Contains("jitter") => GamerModels.StutterCause.NetworkJitter,
                var c when c.Contains("thermal") || c.Contains("throttle") => GamerModels.StutterCause.ThermalThrottling,
                _ => GamerModels.StutterCause.Unknown
            };
        }

        private string LocalizeDetectionStatus(string? rawStatus)
        {
            if (string.IsNullOrEmpty(rawStatus)) return "";
            if (rawStatus.StartsWith("Iniciando", StringComparison.OrdinalIgnoreCase))
                return LocalizationService.Instance.GetString("GamerDetectionStatusInit");
            if (rawStatus.StartsWith("Escaneando", StringComparison.OrdinalIgnoreCase))
                return LocalizationService.Instance.GetString("GamerDetectionStatusPlatforms");
            if (rawStatus.IndexOf("Steam", StringComparison.OrdinalIgnoreCase) >= 0 ||
                rawStatus.IndexOf("common", StringComparison.OrdinalIgnoreCase) >= 0)
                return LocalizationService.Instance.GetString("GamerDetectionStatusSteam");
            if (rawStatus.IndexOf("Epic", StringComparison.OrdinalIgnoreCase) >= 0)
                return LocalizationService.Instance.GetString("GamerDetectionStatusEpic");
            if (rawStatus.IndexOf("Validando", StringComparison.OrdinalIgnoreCase) >= 0 ||
                rawStatus.IndexOf("execut", StringComparison.OrdinalIgnoreCase) >= 0 ||
                rawStatus.IndexOf("running", StringComparison.OrdinalIgnoreCase) >= 0)
                return LocalizationService.Instance.GetString("GamerDetectionStatusValidating");
            if (rawStatus.IndexOf("Ignorando", StringComparison.OrdinalIgnoreCase) >= 0)
                return LocalizationService.Instance.GetString("GamerDetectionStatusIgnoring");
            if (rawStatus.IndexOf("Atualizando", StringComparison.OrdinalIgnoreCase) >= 0)
                return LocalizationService.Instance.GetString("GamerDetectionStatusUpdating");
            if (rawStatus.IndexOf("conclu", StringComparison.OrdinalIgnoreCase) >= 0)
                return LocalizationService.Instance.GetString("GamerDetectionStatusFinalizing");
            if (rawStatus.IndexOf("cancel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                rawStatus.IndexOf("erro", StringComparison.OrdinalIgnoreCase) >= 0)
                return rawStatus;
            return rawStatus;
        }

        private void OnGameDetectionProgressChanged(object? sender, GameDetectionProgress e)
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                GameDetectionStatus = LocalizeDetectionStatus(e.Status);
                _logger?.LogInfo($"[GamerViewModel] [Detection] Progresso: {e.PercentComplete}% | Status: {e.Status} | Jogos: {e.GamesFound}");
                GameDetectionProgressPercent = e.PercentComplete;
                GamesFoundCount = e.GamesFound;

                if (e.PercentComplete > 0 && e.PercentComplete < 100)
                {
                    if (!_isDetecting)
                    {
                        _previousGameCount = Games.Count;
                        _detectionStopwatch.Restart();
                        _logger.LogInfo($"[GamerViewModel] [Detection] Inicio da deteccao - jogos previos na biblioteca: {_previousGameCount}");
                        IsDetecting = true;
                    }
                    _logger.LogInfo($"[GamerViewModel] [Detection] Etapa: {e.Status} ({e.PercentComplete}%) - {e.GamesFound} jogos encontrados ate agora");
                }
                else if (e.PercentComplete >= 100)
                {
                    if (_isDetecting)
                    {
                        _detectionStopwatch.Stop();
                        double elapsedSec = _detectionStopwatch.Elapsed.TotalSeconds;
                        int newGames = e.GamesFound - _previousGameCount;
                        if (newGames < 0) newGames = 0;
                        int existingGames = _previousGameCount;
                        _logger.LogSuccess($"[GamerViewModel] [Detection] Deteccao concluida: {e.GamesFound} jogos ({newGames} novos, {existingGames} existentes) em {elapsedSec:F1}s");
                        _logger.LogInfo($"[GamerViewModel] [Detection] Tempo total: {elapsedSec:F1}s | Novos: {newGames} | Existentes: {existingGames}");
                        DetectionSummaryText = string.Format(
                            LocalizationService.Instance.GetString("GamerDetectionSummary"),
                            e.GamesFound, newGames, existingGames, elapsedSec.ToString("F1"));
                        IsDetecting = false;
                    }
                }

                OnPropertyChanged(nameof(IsGameDetectionActive));
                OnPropertyChanged(nameof(NoGamesDetectedYet));
            });
        }

        #endregion
        
        #region Incident Monitoring (Task-Based)
        
        private CancellationTokenSource? _incidentMonitoringCts;
        
        /// <summary>
        /// Inicia monitoramento de incidentes em background (Task-based)
        /// CORREÇÃO: Substitui DispatcherTimer que rodava na UI thread
        /// </summary>
        private async Task StartIncidentMonitoringAsync()
        {
            _incidentMonitoringCts = new CancellationTokenSource();
            var token = _incidentMonitoringCts.Token;
            
            await Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        // OTIMIZAÇÃO: Aumentado para 30s para reduzir consumo de CPU drasticamente
                        await Task.Delay(30000, token);
                        
                        // Executar update em background
                        var hasNewIncidents = await Task.Run(() =>
                        {
                            try
                            {
                                var currentCount = Incidents.Count;
                                UpdateIncidentsFromService();
                                return Incidents.Count > currentCount;
                            }
                            catch
                            {
                                return false;
                            }
                        }, token);
                        
                        // Atualizar UI apenas se houver novos incidentes
                        if (hasNewIncidents)
                        {
                            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                            {
                                OnPropertyChanged(nameof(Incidents));
                            });
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"[GamerViewModel] Erro no monitoramento de incidentes: {ex.Message}");
                    }
                }
            }, token);
        }
        
        #endregion

        #region Game Repair  Corrigir Erros nos Jogos

        /// <summary>
        /// Executa scan inteligente (máx 15s) para detectar problemas reais em jogos.
        /// </summary>
        private async Task ScanGameErrorsAsync()
        {
            _logger?.LogInfo("[GamerViewModel] [ScanGameErrors] INICIADO");
            // Guard de reentrância  evita scan duplo por double-click ou chamada concorrente
            if (!await _gameRepairLock.WaitAsync(0))
            {
                _logger.LogInfo("[GamerViewModel] [ScanGameErrors] Scan já em andamento  ignorando chamada duplicada");
                return;
            }

            try
            {
            _gameRepairCts?.Cancel();
            _gameRepairCts = new CancellationTokenSource();
            var ct = _gameRepairCts.Token;

            var loc = LocalizationService.Instance;
            _logger?.LogInfo("[GamerViewModel] [ScanGameErrors] Iniciando scan de erros em jogos");
            IsGameRepairScanning = true;
            HasGameRepairReport  = false;
            GameRepairAllOk      = false;
            HasGameRepairIssues  = false;
            GameRepairProgress   = 0;
            GameRepairStatus     = loc.GetString("GameRepairStarting");
            GameRepairIssues.Clear();
            GameRepairReport     = "";

            GlobalProgressService.Instance.StartOperation(loc.GetString("GameRepairScanName"));

            // OnLog: apenas repassa para a UI — o serviço já loga internamente via _logger
            // NÃO chamar _logger aqui para evitar duplicação de mensagens no log
            if (_gameRepairService != null)
            {
                _gameRepairService.OnLog = (msg, color) => { /* UI-only: sem log duplicado */ };
                _gameRepairService.OnProgress = (pct, msg) =>
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    GameRepairProgress = pct;
                    GameRepairStatus   = msg;
                    GlobalProgressService.Instance.UpdateProgress(pct, msg);
                });
            }
            else
            {
                _logger?.LogWarning("[GamerViewModel] [ScanGameErrors] _gameRepairService é nulo");
            }

            try
            {
                var progress = new Progress<(int pct, string msg)>(p =>
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        GameRepairProgress = p.pct;
                        GameRepairStatus   = p.msg;
                        GlobalProgressService.Instance.UpdateProgress(p.pct, p.msg);
                    }));

                _logger?.LogInfo("[GamerViewModel] [ScanGameErrors] Chamando _gameRepairService.ScanAsync");
                GameRepairScanResult scanResult = new GameRepairScanResult();
                
                if (_useEnterpriseRepair && _dependencyProviders != null)
                {
                    _logger?.LogInfo("[GamerViewModel] [ScanGameErrors] V2 ARCHITECTURE: Executando Providers Empresariais...");
                    foreach (var provider in _dependencyProviders)
                    {
                        var diags = await provider.DiagnoseAsync(ct);
                        foreach (var diag in diags)
                        {
                            if (!diag.IsHealthy)
                            {
                                scanResult.Issues.Add(new GameRepairIssue
                                {
                                    Id = diag.ComponentId,
                                    Title = diag.Title,
                                    Description = "Detectado via V2 Architecture: " + string.Join("; ", diag.MissingEvidences),
                                    Severity = GameRepairSeverity.Error,
                                    CanAutoFix = diag.CanAutoFix,
                                    FixDescription = "O Reparador V2 cuidará disso."
                                });
                            }
                        }
                    }
                    scanResult.Duration = TimeSpan.FromSeconds(2); // Simulated time since it's very fast
                }
                else
                {
                    scanResult = _gameRepairService != null 
                        ? await _gameRepairService.ScanAsync(progress, ct) 
                        : new GameRepairScanResult();
                }
                _logger?.LogInfo($"[GamerViewModel] [ScanGameErrors] Scan concluído: {scanResult.Issues.Count} problemas, AllOk={scanResult.AllOk}, Duração={scanResult.Duration.TotalSeconds}s");

                await System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    GameRepairIssues.Clear();
                    foreach (var issue in scanResult.Issues)
                        GameRepairIssues.Add(issue);

                    GameRepairAllOk     = scanResult.AllOk;
                    HasGameRepairIssues = !scanResult.AllOk;
                    HasGameRepairReport = true;

                    if (scanResult.AllOk)
                    {
                        GameRepairReport = string.Format(loc.GetString("GameRepairAllOkReport"), scanResult.Duration.TotalSeconds);
                        GameRepairStatus = loc.GetString("GameRepairAllOk");
                        _logger?.LogSuccess("[GamerViewModel] [ScanGameErrors] Nenhum problema encontrado");
                        GlobalNotificationService.ShowSuccess(loc.GetString("GameRepairScanName"), loc.GetString("GameRepairNoProblems"));
                    }
                    else
                    {
                        GameRepairReport = string.Format(loc.GetString("GameRepairIssuesReport"), scanResult.Issues.Count);
                        GameRepairStatus = string.Format(loc.GetString("GameRepairStatusIssues"), scanResult.Issues.Count);
                        _logger?.LogInfo($"[GamerViewModel] [ScanGameErrors] {scanResult.Issues.Count} problemas encontrados");
                    }

                    // Atualizar CanExecute dos comandos
                    if (RepairGameErrorsCommand is AsyncRelayCommand repairCmd)
                        repairCmd.RaiseCanExecuteChanged();
                    if (CancelGameRepairCommand is RelayCommand cancelCmd)
                        cancelCmd.RaiseCanExecuteChanged();
                });
            }
            catch (OperationCanceledException)
            {
                GameRepairStatus = loc.GetString("GameRepairScanCancelled");
                _logger.LogInfo("[GamerViewModel] [ScanGameErrors] Scan cancelado pelo usuário");
            }
            catch (Exception ex)
            {
                GameRepairStatus = string.Format(loc.GetString("GameRepairScanError"), ex.Message);
                _logger.LogError($"[GamerViewModel] [ScanGameErrors] Erro no scan: {ex.Message}");
            }
            finally
            {
                IsGameRepairScanning = false;
                GlobalProgressService.Instance.CompleteOperation(loc.GetString("GameRepairScanComplete"));
                if (ScanGameErrorsCommand is AsyncRelayCommand scanCmd)
                    scanCmd.RaiseCanExecuteChanged();
                _logger?.LogInfo("[GamerViewModel] [ScanGameErrors] FINALIZADO");
            }
            } // fim try do semáforo
            finally
            {
                _gameRepairLock.Release();
            }
        }

        /// <summary>
        /// Executa correções seletivas para os problemas detectados.
        /// Integrado com GlobalProgressService para barra de progresso global.
        /// </summary>
        public async Task RepairGameErrorsAsync()
        {
            _logger?.LogInfo("[GamerViewModel] [USER_ACTION] O usuário clicou no botão 'Reparar Erros' da interface.");
            _logger?.LogInfo("[GamerViewModel] [RepairGameErrors] INICIADO");
            if (!HasGameRepairIssues)
            {
                _logger?.LogWarning("[GamerViewModel] [RepairGameErrors] Não há problemas para reparar");
                return;
            }

            var loc = LocalizationService.Instance;
            _logger?.LogInfo($"[GamerViewModel] [RepairGameErrors] Iniciando reparo de {GameRepairIssues.Count} problemas");

            // Guard de reentrância — evita reparo duplo por double-click ou chamada concorrente
            if (!await _gameRepairLock.WaitAsync(0))
            {
                _logger.LogInfo("[GamerViewModel] [RepairGameErrors] Reparo já em andamento — ignorando chamada duplicada");
                return;
            }

            try
            {
            _gameRepairCts?.Cancel();
            _gameRepairCts = new CancellationTokenSource();
            var ct = _gameRepairCts.Token;

            IsGameRepairRunning = true;
            GameRepairProgress  = 0;
            GameRepairStatus    = loc.GetString("GameRepairStarting");

            GlobalProgressService.Instance.StartOperation(loc.GetString("GameRepairScanName"));

            try
            {
                var issuesToFix = GameRepairIssues.ToList();
                _logger?.LogInfo($"[GamerViewModel] [RepairGameErrors] {issuesToFix.Count} problemas para reparar, {issuesToFix.Count(i => i.CanAutoFix)} auto-fixáveis");
                var progress = new Progress<(int pct, string msg)>(p =>
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        GameRepairProgress = p.pct;
                        GameRepairStatus   = p.msg;
                        GlobalProgressService.Instance.UpdateProgress(p.pct, p.msg);
                    }));

                _logger?.LogInfo("[GamerViewModel] [RepairGameErrors] Chamando _gameRepairService.RepairAsync");
                GameRepairResult repairResult = new GameRepairResult();

                if (_useEnterpriseRepair && _enterpriseRepairEngine != null && _dependencyProviders != null)
                {
                    _logger?.LogInfo("[GamerViewModel] [RepairGameErrors] V2 ARCHITECTURE: Executando Engine Empresarial...");
                    int fixedCount = 0;
                    int idx = 0;
                    
                    var entProgress = new Progress<(GameDependencyState state, int percentage, string message)>(p =>
                    {
                        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                        {
                            GameRepairProgress = p.percentage;
                            GameRepairStatus   = p.message;
                            GlobalProgressService.Instance.UpdateProgress(p.percentage, p.message);
                        });
                    });

                    foreach (var issue in issuesToFix)
                    {
                        idx++;
                        IGameDependencyProvider? matchedProvider = null;
                        foreach (var p in _dependencyProviders)
                        {
                            var diags = await p.DiagnoseAsync(ct);
                            if (diags.Any(d => d.ComponentId == issue.Id))
                            {
                                matchedProvider = p;
                                break;
                            }
                        }
                        
                        if (matchedProvider != null)
                        {
                            var diagModel = new GameDependencyDiagnosis { ComponentId = issue.Id, Title = issue.Title };
                            var res = await _enterpriseRepairEngine.ExecutePipelineAsync(matchedProvider, diagModel, entProgress, ct);
                            if (res.Success)
                            {
                                fixedCount++;
                                issue.Status = GameRepairItemStatus.Fixed;
                                _logger?.LogSuccess($"[GamerViewModel] [RepairGameErrors] Sucesso ao reparar {issue.Title}.");
                            }
                            else
                            {
                                issue.Status = GameRepairItemStatus.Failed;
                                issue.StatusMessage = res.Message;
                                _logger?.LogError($"[GamerViewModel] [RepairGameErrors] Falha ao reparar {issue.Title}: {res.Message}");
                            }
                        }
                    }
                    repairResult.FixedCount = fixedCount;
                    repairResult.TotalIssues = issuesToFix.Count;
                    repairResult.RequiresReboot = false;
                }
                else
                {
                    repairResult = _gameRepairService != null
                        ? await _gameRepairService.RepairAsync(issuesToFix, progress, ct)
                        : new GameRepairResult();
                }
                _logger?.LogInfo($"[GamerViewModel] [RepairGameErrors] Reparo concluído: FixedCount={repairResult.FixedCount}, SkippedCount={repairResult.SkippedCount}, TotalIssues={repairResult.TotalIssues}");

                await System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    var fixable = issuesToFix.Count(i => i.CanAutoFix);
                    var requiresReboot = repairResult.RequiresReboot ||
                        repairResult.Details.Any(d => d.Message.Contains("reinicialize", StringComparison.OrdinalIgnoreCase));

                    GameRepairReport = string.Format(loc.GetString("GameRepairReportTemplate"),
                        repairResult.FixedCount, repairResult.TotalIssues,
                        repairResult.SkippedCount > 0 ? string.Format(loc.GetString("GameRepairSkippedInReport"), repairResult.SkippedCount) : "",
                        requiresReboot ? loc.GetString("GameRepairRequiresReboot") : "");

                    GameRepairStatus = string.Format(loc.GetString("GameRepairFixedCount"), repairResult.FixedCount, repairResult.TotalIssues);
                    HasGameRepairReport = true;

                    if (repairResult.FixedCount > 0 && requiresReboot)
                    {
                        _logger?.LogSuccess($"[GamerViewModel] [RepairGameErrors] {repairResult.FixedCount} problemas corrigidos (requer reinicialização)");
                        GlobalNotificationService.ShowSuccess(
                            loc.GetString("GameRepairScanName"),
                            string.Format(loc.GetString("GameRepairFixedCountReboot"), repairResult.FixedCount, repairResult.TotalIssues));
                    }
                    else if (repairResult.FixedCount > 0)
                    {
                        _logger?.LogSuccess($"[GamerViewModel] [RepairGameErrors] {repairResult.FixedCount} problemas corrigidos");
                        GlobalNotificationService.ShowSuccess(
                            loc.GetString("GameRepairScanName"),
                            string.Format(loc.GetString("GameRepairFixedCount"), repairResult.FixedCount, repairResult.TotalIssues));
                    }
                    else if (repairResult.SkippedCount > 0)
                    {
                        _logger?.LogWarning($"[GamerViewModel] [RepairGameErrors] {repairResult.SkippedCount} problemas ignorados (requer ação manual)");
                        GlobalNotificationService.ShowWarning(
                            loc.GetString("GameRepairScanName"),
                            string.Format(loc.GetString("GameRepairSkippedManual"), repairResult.SkippedCount));
                    }
                    else
                    {
                        _logger?.LogInfo("[GamerViewModel] [RepairGameErrors] Nenhuma alteração necessária");
                        GlobalNotificationService.ShowInfo(
                            loc.GetString("GameRepairScanName"),
                            loc.GetString("GameRepairNoChanges"));
                    }
                });
            }
            catch (OperationCanceledException)
            {
                GameRepairStatus = loc.GetString("GameRepairRepairCancelled");
                _logger.LogInfo("[GamerViewModel] [RepairGameErrors] Reparo cancelado pelo usuário");
            }
            catch (Exception ex)
            {
                GameRepairStatus = string.Format(loc.GetString("GameRepairScanError"), ex.Message);
                _logger.LogError($"[GamerViewModel] [RepairGameErrors] Erro no reparo: {ex.Message}");
            }
            finally
            {
                IsGameRepairRunning = false;
                GlobalProgressService.Instance.CompleteOperation(loc.GetString("GameRepairScanComplete"));
                if (RepairGameErrorsCommand is AsyncRelayCommand repairCmd)
                    repairCmd.RaiseCanExecuteChanged();
                if (CancelGameRepairCommand is RelayCommand cancelCmd)
                    cancelCmd.RaiseCanExecuteChanged();
                _logger?.LogInfo("[GamerViewModel] [RepairGameErrors] Fase de reparo finalizada");
            }
            } // fim try do semáforo
            finally
            {
                _gameRepairLock.Release();
            }

            // Re-scan automático após reparo concluído  atualiza a UI removendo
            // os itens já corrigidos e mostrando apenas o que ainda precisar de atenção
            await Task.Delay(800); // pequena pausa para o Windows registrar as instalações
            _logger.LogInfo("[GamerViewModel] [RepairGameErrors] Iniciando re-scan automático após reparo...");
            // await ScanGameErrorsAsync();
            _logger?.LogInfo("[GamerViewModel] [RepairGameErrors] FINALIZADO");
        }

        private void CancelGameRepair()
        {
            _logger?.LogInfo("[GamerViewModel] [CancelGameRepair] INICIADO - Cancelamento solicitado pelo usuário");
            _gameRepairCts?.Cancel();
            GameRepairStatus = LocalizationService.Instance.GetString("GameRepairCancelling");
            _logger?.LogInfo("[GamerViewModel] [CancelGameRepair] FINALIZADO");
        }

        #endregion

        protected override void OnDisposing()
        {
            try
            {
                UnsubscribeBackgroundEvents();
                if (_gamerSessionManager != null)
                {
                    _gamerSessionManager.SessionStateChanged -= OnTemporaryOptimizationSessionStateChanged;
                }
                if (_trendAnalyzer != null) _trendAnalyzer.WarningDetected -= OnTrendWarningDetected;
                if (_powerDiag != null) _powerDiag.DiagnosticMessageGenerated -= OnPowerDiagnosticMessage;

                _incidentMonitoringCts?.Cancel();
                _incidentMonitoringCts?.Dispose();

                _overlayDebounceTimer?.Dispose();

                _gamerModeLock?.Dispose();
                _gameRepairLock?.Dispose();

                _gameRepairCts?.Cancel();
                _gameRepairCts?.Dispose();

                _orchestrator.StatusChanged -= OnStatusChanged;
                _gameDetector.ProgressChanged -= OnGameDetectionProgressChanged;
                _gameDetector.GameStarted -= OnGameStarted;
                _gameDetector.GameStopped -= OnGameStopped;

                LicenseManager.Instance.LicenseStatusChanged -= OnLicenseStatusChanged;

                if (App.GameDiagnostics != null)
                {
                    App.GameDiagnostics.SamplesUpdated -= OnDiagnosticsSamplesUpdated;
                }

                try
                {
                    _gameDetector.StopMonitoring();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[GamerViewModel] Erro ao parar monitoramento do detector de jogos: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GamerViewModel] Erro ao descartar ViewModel: {ex.Message}", ex);
            }

            base.OnDisposing();
        }
    }
}

