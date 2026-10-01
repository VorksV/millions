using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Performance;
using VoltrisOptimizer.Services.Gamer.Interfaces;
using VoltrisOptimizer.UI.ViewModels.Performance.Models;
using VoltrisOptimizer.Services.Performance.Models;
using VoltrisOptimizer.Services.License;
using VoltrisOptimizer.Services.License.Interfaces;
using VoltrisOptimizer.UI.Commands;

namespace VoltrisOptimizer.UI.ViewModels
{
    /// <summary>
    /// ViewModel para a página de Desempenho
    /// Gerencia otimizações de performance do sistema
    /// </summary>
    public partial class PerformanceViewModel : ViewModelBase
    {
        private readonly ILoggingService? _logger;
        private readonly IPerformanceOptimizationService? _performanceService;
        private readonly IGamerModeOrchestrator? _gamerOrchestrator;
        private readonly IntelligentPerformanceCoordinator? _intelligentCoordinator;
        private readonly CancellationTokenSource _intelligenceCts = new();
        private bool _isOptimizing;
        private string _optimizationStatus = "";
        private VoltrisOptimizer.Services.Performance.Models.HardwareTier _hardwareTier = VoltrisOptimizer.Services.Performance.Models.HardwareTier.MidRange;
        
        // System Profile Properties
        private string _profileCpu = "";
        private string _profileRam = "";
        private string _profileStorage = "";
        private string _profileGpu = "";
        private string _profileType = "";
        private string _profileTypeIcon = "🖥️";
        
        // Categories
        private ObservableCollection<PerfCategoryViewModel>? _categories;
        
        // Intelligent Recommendations (NEW)
        private string _intelligentRecommendation = "";
        private bool _hasIntelligentRecommendation;
        private string _recommendationIcon = "🧠";
        private string _intelligentStatusMessage = "";
        
        public ICommand AutoOptimizeCommand { get; }
        public ICommand RevertAllCommand { get; }
        public ICommand ApplyIntelligentRecommendationCommand { get; }
        public ICommand IntelligentOptimizeCommand { get; }
        
        public bool IsOptimizing
        {
            get => _isOptimizing;
            set { SetProperty(ref _isOptimizing, value); }
        }
        
        public string OptimizationStatus
        {
            get => _optimizationStatus;
            set { SetProperty(ref _optimizationStatus, value); }
        }
        
        // System Profile Properties
        public string ProfileCpu
        {
            get => _profileCpu;
            set { SetProperty(ref _profileCpu, value); }
        }
        
        public string ProfileRam
        {
            get => _profileRam;
            set { SetProperty(ref _profileRam, value); }
        }
        
        public string ProfileStorage
        {
            get => _profileStorage;
            set { SetProperty(ref _profileStorage, value); }
        }
        
        public string ProfileGpu
        {
            get => _profileGpu;
            set { SetProperty(ref _profileGpu, value); }
        }
        
        public string ProfileType
        {
            get => _profileType;
            set { SetProperty(ref _profileType, value); }
        }
        
        public string ProfileTypeIcon
        {
            get => _profileTypeIcon;
            set { SetProperty(ref _profileTypeIcon, value); }
        }
        
        public ObservableCollection<PerfCategoryViewModel> Categories
        {
            get
            {
                if (_categories == null)
                {
                    _categories = new ObservableCollection<PerfCategoryViewModel>();
                }
                return _categories;
            }
            set
            {
                SetProperty(ref _categories, value);
            }
        }
        
        // Intelligent Recommendation Properties (NEW)
        public string IntelligentRecommendation
        {
            get => _intelligentRecommendation;
            set { SetProperty(ref _intelligentRecommendation, value); }
        }
        
        public bool HasIntelligentRecommendation
        {
            get => _hasIntelligentRecommendation;
            set { SetProperty(ref _hasIntelligentRecommendation, value); }
        }
        
        public string RecommendationIcon
        {
            get => _recommendationIcon;
            set { SetProperty(ref _recommendationIcon, value); }
        }

        public string IntelligentStatusMessage
        {
            get => _intelligentStatusMessage;
            set { SetProperty(ref _intelligentStatusMessage, value); }
        }

        // Full properties for tooltips (to avoid truncation)
        private string _fullProfileCpu = "";
        private string _fullProfileRam = "";
        private string _fullProfileStorage = "";
        private string _fullProfileGpu = "";

        public string FullProfileCpu
        {
            get => _fullProfileCpu;
            set { SetProperty(ref _fullProfileCpu, value); }
        }

        public string FullProfileRam
        {
            get => _fullProfileRam;
            set { SetProperty(ref _fullProfileRam, value); }
        }

        public string FullProfileStorage
        {
            get => _fullProfileStorage;
            set { SetProperty(ref _fullProfileStorage, value); }
        }

        public string FullProfileGpu
        {
            get => _fullProfileGpu;
            set { SetProperty(ref _fullProfileGpu, value); }
        }

        public PerformanceViewModel()
        {
            _logger = App.LoggingService;
            
            // Inicializações localizadas dinâmicas
            _optimizationStatus = LocalizationService.Instance.GetString("CommonReady");
            _profileCpu = LocalizationService.Instance.GetString("NetworkLoading");
            _profileRam = LocalizationService.Instance.GetString("NetworkLoading");
            _profileStorage = LocalizationService.Instance.GetString("NetworkLoading");
            _profileGpu = LocalizationService.Instance.GetString("NetworkLoading");
            _profileType = LocalizationService.Instance.GetString("CommonDesktop");
            _intelligentRecommendation = LocalizationService.Instance.GetString("NetworkLoading");
            _intelligentStatusMessage = LocalizationService.Instance.GetString("IntelligentReadyForAnalysis");
            _fullProfileCpu = LocalizationService.Instance.GetString("NetworkLoading");
            _fullProfileRam = LocalizationService.Instance.GetString("NetworkLoading");
            _fullProfileStorage = LocalizationService.Instance.GetString("NetworkLoading");
            _fullProfileGpu = LocalizationService.Instance.GetString("NetworkLoading");
            
            // Tentar obter via DI, fallback para App estático
            try
            {
                var serviceProvider = App.Services;
                if (serviceProvider != null)
                {
                    _performanceService = serviceProvider.GetService(typeof(IPerformanceOptimizationService)) as IPerformanceOptimizationService;
                    _gamerOrchestrator = serviceProvider.GetService(typeof(IGamerModeOrchestrator)) as IGamerModeOrchestrator;
                    _intelligentCoordinator = serviceProvider.GetService(typeof(IntelligentPerformanceCoordinator)) as IntelligentPerformanceCoordinator;
                    _logger?.LogInfo($"[PerformanceVM] Serviço obtido via DI: Perf={_performanceService != null}, Gamer={_gamerOrchestrator != null}, AI={_intelligentCoordinator != null}");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[PerformanceVM] Erro ao obter serviço via DI: {ex.Message}");
            }
            
            // Fallback: criar wrapper se UltraPerformance estiver disponível
            if (_performanceService == null && App.UltraPerformance != null && _logger != null)
            {
                try
                {
                    _performanceService = new PerformanceOptimizationService(
                        App.UltraPerformance, 
                        _logger);
                    _logger?.LogInfo("[PerformanceVM] Serviço criado via fallback (App.UltraPerformance)");
                }
                catch (Exception ex)
                {
                    _logger?.LogError($"[PerformanceVM] Erro ao criar serviço fallback: {ex.Message}", ex);
                }
            }
            
            if (_performanceService == null)
            {
                _logger?.LogWarning("[PerformanceVM] ⚠️ Serviço de performance não disponível!");
            }
            
            AutoOptimizeCommand = new AsyncRelayCommand(AutoOptimizeAsync, () => _performanceService != null && !IsOptimizing);
            RevertAllCommand = new AsyncRelayCommand(RevertAllAsync, () => _performanceService != null && !IsOptimizing);
            ApplyIntelligentRecommendationCommand = new AsyncRelayCommand(ApplyIntelligentRecommendationAsync, () => _intelligentCoordinator != null && HasIntelligentRecommendation && !IsOptimizing);

            // CORREÇÃO PRO: IntelligentOptimizeCommand agora usa LicensedCommand — mesmo padrão do SmartRepair.
            // Sem licença paga (Standard/Pro/Enterprise) → bloqueia e abre o modal de compra.
            // Com licença válida → executa AutoOptimizeAsync normalmente.
            var licenseGuard = App.Services?.GetService(typeof(ILicenseGuard)) as ILicenseGuard;
            var licenseDialogService = App.Services?.GetService(typeof(ILicenseDialogService)) as ILicenseDialogService;

            if (licenseGuard != null && licenseDialogService != null)
            {
                IntelligentOptimizeCommand = new LicensedCommand(
                    new AsyncRelayCommand(AutoOptimizeAsync, () => _performanceService != null && !IsOptimizing),
                    licenseGuard,
                    "intelligent_optimization",
                    licenseDialogService);
                _logger?.LogInfo("[PerformanceVM] IntelligentOptimizeCommand configurado como LicensedCommand (PRO gate ativo).");
            }
            else
            {
                // Fallback seguro: se DI não estiver disponível, usa ProFeatureGuard síncrono (FAIL-CLOSED)
                _logger?.LogWarning("[PerformanceVM] LicenseGuard/DialogService não disponíveis via DI — usando ProFeatureGuard como fallback.");
                IntelligentOptimizeCommand = new AsyncRelayCommand(
                    async () =>
                    {
                        if (!ProFeatureGuard.RequirePaid("intelligent_optimization"))
                            return;
                        await AutoOptimizeAsync();
                    },
                    () => _performanceService != null && !IsOptimizing);
            }
            
            // Start intelligent analysis in background (non-blocking)
            if (_intelligentCoordinator != null)
            {
                var coordinator = _intelligentCoordinator;
                var logger = _logger;
                _ = Task.Run(async () =>
                {
                    await Task.Delay(10000); // CORREÇÃO: Wait 10s (era 5s) para UI carregar completamente
                    await this.StartIntelligentAnalysisLoopAsync(coordinator, logger, _intelligenceCts.Token);
                });
            }

            InitializeBenchmarkIntegration();
        }
        
        /// <summary>
        /// Carrega os dados do sistema e categorias de otimização
        /// </summary>
        public async Task LoadDataAsync()
        {
            try
            {
                _logger?.LogInfo("[PerformanceVM] Iniciando carregamento de dados...");
                
                // Verificar se o serviço está disponível
                if (_performanceService == null)
                {
                    _logger?.LogWarning("[PerformanceVM] Serviço de performance não disponível");
                    
                    // Atualizar UI com mensagem de erro (não-bloqueante)
                    await System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        ProfileCpu = LocalizationService.Instance.GetString("CommonServiceUnavailable");
                        ProfileRam = "N/A";
                        ProfileStorage = "N/A";
                        ProfileGpu = "N/A";
                        ProfileType = LocalizationService.Instance.GetString("CommonError");
                        ProfileTypeIcon = "⚠️";
                        OptimizationStatus = LocalizationService.Instance.GetString("CommonServiceUnavailable");
                    });
                    return;
                }
 
                // Carregar perfil do sistema em background thread
                PerformanceSystemProfile? profile = null;
                await Task.Run(() =>
                {
                    try
                    {
                        profile = _performanceService.DetectSystemProfile();
                        // Classificar hardware
                        if (profile != null)
                        {
                            _hardwareTier = VoltrisOptimizer.Services.UltraPerformanceService.ClassifyHardware(profile);
                            _logger?.LogInfo($"[PerformanceVM] Perfil detectado: {profile.CPUName}, Tier: {_hardwareTier}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError($"[PerformanceVM] Erro ao detectar perfil: {ex.Message}", ex);
                    }
                });
 
                // Atualizar UI na thread principal (não-bloqueante)
                if (profile != null)
                {
                    await System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            LoadSystemProfile(profile);
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError($"[PerformanceVM] Erro ao atualizar UI do perfil: {ex.Message}", ex);
                        }
                    });
                }
 
                // Carregar categorias em background thread
                IReadOnlyList<PerformanceCategory>? categories = null;
                await Task.Run(() =>
                {
                    try
                    {
                        categories = _performanceService.GetOptimizationCategories();
                        _logger?.LogInfo($"[PerformanceVM] {categories.Count} categorias obtidas");
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError($"[PerformanceVM] Erro ao obter categorias: {ex.Message}", ex);
                    }
                });
 
                // Atualizar UI na thread principal (não-bloqueante)
                if (categories != null)
                {
                    await System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            LoadCategories(categories);
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError($"[PerformanceVM] Erro ao atualizar UI das categorias: {ex.Message}", ex);
                        }
                    });
                }
 
                _logger?.LogSuccess("[PerformanceVM] Dados carregados com sucesso");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[PerformanceVM] Erro crítico ao carregar dados: {ex.Message}", ex);
                
                // Atualizar UI com mensagem de erro (não-bloqueante)
                await System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    ProfileCpu = LocalizationService.Instance.GetString("CommonLoadError");
                    ProfileRam = LocalizationService.Instance.GetString("CommonError");
                    ProfileStorage = LocalizationService.Instance.GetString("CommonError");
                    ProfileGpu = LocalizationService.Instance.GetString("CommonError");
                    ProfileType = LocalizationService.Instance.GetString("CommonError");
                    ProfileTypeIcon = "⚠️";
                    OptimizationStatus = $"{LocalizationService.Instance.GetString("CommonError")}: {ex.Message}";
                });
            }
        }
        
        private void LoadSystemProfile(PerformanceSystemProfile profile)
        {
            try
            {
                // Store full names for tooltips
                FullProfileCpu = profile.CPUName ?? LocalizationService.Instance.GetString("CommonUnknown");
                FullProfileRam = $"{profile.TotalRAMGB:F0} GB";
                FullProfileGpu = profile.GPUName ?? LocalizationService.Instance.GetString("CommonUnknown");
                
                // Storage Full Info
                if (profile.HasNVMe)
                    FullProfileStorage = $"NVMe SSD - {profile.DiskModel}";
                else if (profile.HasSSD)
                    FullProfileStorage = $"SATA SSD - {profile.DiskModel}";
                else
                    FullProfileStorage = $"HDD - {profile.DiskModel}";
 
                // CPU Display (Truncated)
                var cpuShort = FullProfileCpu;
                if (cpuShort.Length > 20)
                {
                    if (cpuShort.Contains("Intel"))
                        cpuShort = cpuShort.Split('@')[0].Replace("Intel(R) Core(TM)", "").Replace("CPU", "").Trim();
                    else if (cpuShort.Contains("AMD"))
                        cpuShort = cpuShort.Split('@')[0].Replace("AMD", "").Trim();
                    
                    if (cpuShort.Length > 15)
                        cpuShort = cpuShort.Substring(0, 15) + "...";
                }
                ProfileCpu = cpuShort;
 
                // RAM Display
                ProfileRam = FullProfileRam;
 
                // Storage Display
                if (profile.HasNVMe)
                    ProfileStorage = "NVMe SSD";
                else if (profile.HasSSD)
                    ProfileStorage = "SSD";
                else
                    ProfileStorage = "HDD";
 
                // GPU Display (Truncated)
                var gpuShort = FullProfileGpu;
                if (gpuShort.Length > 15)
                {
                    if (gpuShort.Contains("NVIDIA"))
                        gpuShort = gpuShort.Replace("NVIDIA", "").Replace("GeForce", "").Trim();
                    else if (gpuShort.Contains("AMD"))
                        gpuShort = gpuShort.Replace("AMD", "").Replace("Radeon", "").Trim();
                    
                    if (gpuShort.Length > 12)
                        gpuShort = gpuShort.Substring(0, 12) + "...";
                }
                ProfileGpu = gpuShort;
 
                // Tipo de PC
                if (profile.IsGamingPC)
                {
                    ProfileType = LocalizationService.Instance.GetString("CommonGamingPC");
                    ProfileTypeIcon = "🎮";
                }
                else if (profile.IsWorkstation)
                {
                    ProfileType = LocalizationService.Instance.GetString("CommonWorkstation");
                    ProfileTypeIcon = "💼";
                }
                else if (profile.IsLaptop)
                {
                    ProfileType = LocalizationService.Instance.GetString("CommonNotebook");
                    ProfileTypeIcon = "💻";
                }
                else
                {
                    ProfileType = LocalizationService.Instance.GetString("CommonDesktop");
                    ProfileTypeIcon = "🖥️";
                }
 
                _logger?.LogInfo($"[PerformanceVM] Perfil carregado: {ProfileType}");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[PerformanceVM] Erro ao processar perfil: {ex.Message}", ex);
                ProfileCpu = LocalizationService.Instance.GetString("CommonError");
                ProfileRam = LocalizationService.Instance.GetString("CommonError");
                ProfileStorage = LocalizationService.Instance.GetString("CommonError");
                ProfileGpu = LocalizationService.Instance.GetString("CommonError");
            }
        }
        
        private void LoadCategories(IReadOnlyList<PerformanceCategory> categories)
        {
            try
            {
                // ✅ OTIMIZAÇÃO: Batch update - construir coleção offline para evitar múltiplas notificações
                var newCategories = new ObservableCollection<PerfCategoryViewModel>();
                var isGamerModeActive = CheckGamerModeActive();
                var profile = _performanceService?.DetectSystemProfile();

                var colors = new Dictionary<string, Color>
                {
                    { "🧠", Color.FromRgb(139, 92, 246) },   // Purple - Memory
                    { "⚡", Color.FromRgb(245, 158, 11) },   // Orange - CPU
                    { "💾", Color.FromRgb(59, 130, 246) },   // Blue - Disk
                    { "🌐", Color.FromRgb(16, 185, 129) },   // Green - Network
                    { "🎨", Color.FromRgb(236, 72, 153) },   // Pink - Visual
                    { "🚀", Color.FromRgb(99, 102, 241) },   // Indigo - Startup
                    { "⚙️", Color.FromRgb(107, 114, 128) },  // Gray - Services
                    { "🔋", Color.FromRgb(234, 179, 8) },    // Yellow - Power
                    { "📁", Color.FromRgb(34, 197, 94) },    // Green - Explorer
                    { "🔧", Color.FromRgb(168, 85, 247) },   // Purple - Essential
                    { "🖱️", Color.FromRgb(14, 165, 233) },   // Sky Blue - Responsiveness
                };

                var iconPaths = new Dictionary<string, string>
                {
                    { "🧠", "M21.33,12.91C21.42,12.15 21.5,11.39 21.5,10.5C21.5,9.61 21.42,8.85 21.33,8.09L23.27,6.58C23.51,6.39 23.56,6.06 23.4,5.79L21.54,2.71C21.38,2.44 21.06,2.34 20.78,2.44L18.54,3.34C17.94,2.87 17.3,2.5 16.58,2.18L16.22,0.37C16.17,0.15 15.97,0 15.74,0H12C11.77,0 11.57,0.15 11.53,0.37L11.16,2.18C10.44,2.5 9.8,2.87 9.2,3.34L6.96,2.44C6.68,2.34 6.36,2.44 6.2,2.71L4.34,5.79C4.18,6.06 4.23,6.39 4.47,6.58L6.41,8.09C6.32,8.85 6.24,9.61 6.24,10.5C6.24,11.39 6.32,12.15 6.41,12.91L4.47,14.42C4.23,14.61 4.18,14.94 4.34,15.21L6.2,18.29C6.36,18.56 6.68,18.66 6.96,18.56L9.2,17.66C9.8,18.13 10.44,18.5 11.16,18.82L11.53,20.63C11.57,20.85 11.77,21 12,21H15.74C15.97,21 16.17,20.85 16.22,20.63L16.58,18.82C17.3,18.5 17.94,18.13 18.54,17.66L20.78,18.56C21.06,18.66 21.38,18.56 21.54,18.29L23.4,15.21C23.56,14.94 23.51,14.61 23.27,14.42L21.33,12.91M13.87,14.5C11.66,14.5 9.87,12.71 9.87,10.5C9.87,8.29 11.66,6.5 13.87,6.5C16.08,6.5 17.87,8.29 17.87,10.5C17.87,12.71 16.08,14.5 13.87,14.5Z" },
                    { "⚡", "M7,2V13H10V22L17,10H13L17,2H7Z" },
                    { "💾", "M6,2H18A2,2 0 0,1 20,4V20A2,2 0 0,1 18,22H6A2,2 0 0,1 4,20V4A2,2 0 0,1 6,2M12,18A3,3 0 0,0 15,15A3,3 0 0,0 12,12A3,3 0 0,0 9,15A3,3 0 0,0 12,18M14,8H6V4H14V8Z" },
                    { "🌐", "M16.36,14C16.44,13.34 16.5,12.68 16.5,12C16.5,11.32 16.44,10.66 16.36,10H19.74C19.9,10.64 20,11.31 20,12C20,12.69 19.9,13.36 19.74,14M14.59,19.56C15.19,18.45 15.65,17.25 15.97,16H18.92C17.96,17.65 16.43,18.93 14.59,19.56M14.34,14H9.66C9.56,13.34 9.5,12.68 9.5,12C9.5,11.32 9.56,10.65 9.66,10H14.34C14.43,10.65 14.5,11.32 14.5,12C14.5,12.68 14.43,13.34 14.34,14M12,19.96C11.17,18.76 10.5,17.43 10.09,16H13.91C13.5,17.43 12.83,18.76 12,19.96M8,8H5.08C6.03,6.34 7.57,5.06 9.4,4.44C8.8,5.55 8.35,6.75 8,8M5.08,16H8C8.35,17.25 8.8,18.45 9.4,19.56C7.57,18.93 6.03,17.65 5.08,16M4.26,14C4.1,13.36 4,12.69 4,12C4,11.31 4.1,10.64 4.26,10H7.64C7.56,10.66 7.5,11.32 7.5,12C7.5,12.68 7.56,13.34 7.64,14M12,4.03C12.83,5.23 13.5,6.57 13.91,8H10.09C10.5,6.57 11.17,5.23 12,4.03M18.92,8H15.97C15.65,6.75 15.19,5.55 14.59,4.44C16.43,5.07 17.96,6.34 18.92,8M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2Z" },
                    { "🎨", "M12,22A10,10 0 0,1 2,12A10,10 0 0,1 12,2C17.52,2 22,6 22,11A6,6 0 0,1 16,17H14.2C13.9,17 13.6,17.1 13.4,17.3C13.2,17.5 13.1,17.7 13.1,18C13.1,18.3 13.2,18.6 13.4,18.8C13.6,19 13.7,19.2 13.7,19.5A1.5,1.5 0 0,1 12.2,21C12.13,21 12.07,21 12,21V22M12,4A8,8 0 0,0 4,12A8,8 0 0,0 12,20C12,19.7 11.8,19.4 11.6,19.2C11.4,19 11.3,18.7 11.3,18.4C11.3,18.1 11.4,17.8 11.6,17.6C11.8,17.4 12.1,17.1 12.4,16.9C12.7,16.7 13,16.6 13.4,16.5C13.8,16.4 14.1,16.3 14.5,16.2C14.9,16.1 15.2,16 15.5,16H16A4,4 0 0,0 20,12C20,7.58 16.42,4 12,4M6.5,10A1.5,1.5 0 0,1 8,11.5A1.5,1.5 0 0,1 6.5,13A1.5,1.5 0 0,1 5,11.5A1.5,1.5 0 0,1 6.5,10M9.5,6A1.5,1.5 0 0,1 11,7.5A1.5,1.5 0 0,1 9.5,9A1.5,1.5 0 0,1 8,7.5A1.5,1.5 0 0,1 9.5,6M14.5,6A1.5,1.5 0 0,1 16,7.5A1.5,1.5 0 0,1 14.5,9A1.5,1.5 0 0,1 13,7.5A1.5,1.5 0 0,1 14.5,6M17.5,10A1.5,1.5 0 0,1 19,11.5A1.5,1.5 0 0,1 17.5,13A1.5,1.5 0 0,1 16,11.5A1.5,1.5 0 0,1 17.5,10Z" },
                    { "🚀", "M13.13,22.19L11.5,18.36C13.07,17.78 14.54,17 15.9,16.09L13.13,22.19M5.64,12.5L1.81,10.87L7.91,8.1C7,9.46 6.22,10.93 5.64,12.5M19.22,4C19.5,4 19.75,4 19.96,4.05C20.13,5.44 19.94,8.3 16.66,11.58C14.96,13.29 12.93,14.6 10.65,15.47L8.5,13.37C9.42,11.06 10.73,9.03 12.42,7.34C14.73,5.03 17.3,4.08 19.22,4M4.15,17.54C5,18.38 5.96,18.5 6.47,18.5C7.16,18.5 7.69,18.24 7.86,18.07L9.7,16.22C9.19,16.39 8.66,16.5 8.11,16.55L7.63,16.59L7.17,16.95C6.64,17.36 6.36,17.5 6.08,17.5C5.77,17.5 5.37,17.35 5.05,17.03C4.5,16.47 4.41,15.88 4.97,15.33L6.32,13.97L6.61,14.25C6.62,13.95 6.66,13.66 6.71,13.36L4.15,15.93C3.59,16.5 3.29,16.69 4.15,17.54M21.56,2.46C21.56,2.46 19.37,0.28 11.32,8.34C9.75,9.91 8.5,11.77 7.6,13.87L10.14,16.41C12.24,15.5 14.1,14.26 15.66,12.68C23.72,4.63 21.56,2.46 21.56,2.46Z" },
                    { "⚙️", "M12,15.5A3.5,3.5 0 0,1 8.5,12A3.5,3.5 0 0,1 12,8.5A3.5,3.5 0 0,1 15.5,12A3.5,3.5 0 0,1 12,15.5M19.43,12.97C19.47,12.65 19.5,12.33 19.5,12C19.5,11.67 19.47,11.34 19.43,11L21.54,9.37C21.73,9.22 21.78,8.95 21.66,8.73L19.66,5.27C19.54,5.05 19.27,4.96 19.05,5.05L16.56,6.05C16.04,5.66 15.5,5.32 14.87,5.07L14.5,2.42C14.46,2.18 14.25,2 14,2H10C9.75,2 9.54,2.18 9.5,2.42L9.13,5.07C8.5,5.32 7.96,5.66 7.44,6.05L4.95,5.05C4.73,4.96 4.46,5.05 4.34,5.27L2.34,8.73C2.21,8.95 2.27,9.22 2.46,9.37L4.57,11C4.53,11.34 4.5,11.67 4.5,12C4.5,12.33 4.53,12.65 4.57,12.97L2.46,14.63C2.27,14.78 2.21,15.05 2.34,15.27L4.34,18.73C4.46,18.95 4.73,19.03 4.95,18.95L7.44,17.94C7.96,18.34 8.5,18.68 9.13,18.93L9.5,21.58C9.54,21.82 9.75,22 10,22H14C14.25,22 14.46,21.82 14.5,21.58L14.87,18.93C15.5,18.67 16.04,18.34 16.56,17.94L19.05,18.95C19.27,19.03 19.54,18.95 19.66,18.73L21.66,15.27C21.78,15.05 21.73,14.78 21.54,14.63L19.43,12.97Z" },
                    { "🔋", "M16,20H8V6H16M16.67,4H15V2H9V4H7.33A1.33,1.33 0 0,0 6,5.33V20.67C6,21.4 6.6,22 7.33,22H16.67A1.33,1.33 0 0,0 18,20.67V5.33C18,4.6 17.4,4 16.67,4Z" },
                    { "📁", "M20,18H4V8H20M20,6H12L10,4H4C2.89,4 2,4.89 2,6V18A2,2 0 0,0 4,20H20A2,2 0 0,0 22,18V8C22,6.89 21.1,6 20,6Z" },
                    { "🔧", "M22.7,19L13.6,9.9C14.5,7.6 14,4.9 12.1,3C10.1,1 7.1,0.6 4.7,1.7L9,6L6,9L1.6,4.7C0.4,7.1 0.9,10.1 2.9,12.1C4.8,14 7.5,14.5 9.8,13.6L18.9,22.7C19.3,23.1 19.9,23.1 20.3,22.7L22.6,20.4C23.1,20 23.1,19.3 22.7,19Z" },
                    { "🖱️", "M13,1.07V9H21C20.47,5.24 17.24,2.03 13,1.07M4,12A8,8 0 0,0 12,20A8,8 0 0,0 20,12H4M11,1.07C6.76,2.03 3.53,5.24 3,9H11V1.07Z" },
                };

                foreach (var cat in categories)
                {
                    try
                    {
                        var currentLanguage = LocalizationService.Instance.CurrentLanguage;
                        var catName = cat.Name ?? LocalizationService.Instance.GetString("CommonNoName");
                        var catDesc = cat.Description ?? "";
                        var localizedCatName = TranslateText(catName, currentLanguage);
                        var localizedCatDesc = TranslateText(catDesc, currentLanguage);

                        var catVm = new PerfCategoryViewModel
                        {
                            Name = localizedCatName,
                            Icon = cat.Icon ?? "⚙️",
                            IconPath = iconPaths.TryGetValue(cat.Icon ?? "", out var ip) ? ip : iconPaths["⚙️"],
                            Description = localizedCatDesc,
                            CategoryColor = colors.TryGetValue(cat.Icon ?? "", out var c) ? c : Color.FromRgb(107, 114, 128),
                            IsExpanded = false
                        };
 
                        if (cat.Optimizations != null)
                        {
                            foreach (var opt in cat.Optimizations)
                            {
                                try
                                {
                                    // Verificar compatibilidade com hardware (apenas para informação)
                                    var isCompatible = IsOptimizationCompatible(opt, profile, isGamerModeActive);
                                    
                                    // Verificar se é seguro para hardware atual (apenas para informação)
                                    var isSafe = IsOptimizationSafe(opt);
                                    
                                    // TODAS as otimizações devem vir selecionadas e habilitadas por padrão
                                    // Usuário solicitou que NADA venha desmarcado, sem exceção
 
                                    var optName = opt.Name ?? LocalizationService.Instance.GetString("CommonNoName");
                                    var optDesc = opt.Description ?? "";
                                    var localizedOptName = TranslateText(optName, currentLanguage);
                                    var localizedOptDesc = TranslateText(optDesc, currentLanguage);
                                    var localizedIncompatibility = !string.IsNullOrEmpty(opt.IncompatibilityReason) ? TranslateText(opt.IncompatibilityReason, currentLanguage) : null;

                                    var optVm = new PerfOptimizationViewModel
                                    {
                                        Name = localizedOptName,
                                        BackendName = optName,
                                        Description = localizedOptDesc,
                                        IsSelected = true,  // SEMPRE marcada por padrão
                                        IsRecommended = opt.IsRecommended && isCompatible,
                                        Impact = opt.Impact,
                                        ApplyAction = opt.ApplyAction,
                                        RevertAction = opt.RevertAction,
                                        IsEnabled = true,  // SEMPRE habilitada
                                        IncompatibilityReason = !isCompatible ? localizedIncompatibility : null
                                    };

                                    optVm.PropertyChanged += (s, e) => UpdateCategoryStats(catVm);
                                    catVm.Optimizations.Add(optVm);
                                }
                                catch (Exception ex)
                                {
                                    _logger?.LogWarning($"[PerformanceVM] Erro ao processar otimização {opt.Name}: {ex.Message}");
                                }
                            }
                        }

                        UpdateCategoryStats(catVm);
                        
                        // Wiring category-level Apply and Revert commands
                        catVm.ApplyCommand = new AsyncRelayCommand(async () => await ApplyCategoryAsync(catVm), () => !IsOptimizing);
                        catVm.RevertCommand = new AsyncRelayCommand(async () => await RevertCategoryAsync(catVm), () => !IsOptimizing);
                        
                        newCategories.Add(catVm);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning($"[PerformanceVM] Erro ao processar categoria {cat.Name}: {ex.Message}");
                    }
                }

                // ✅ OTIMIZAÇÃO: Atribuição única = 1 notificação PropertyChanged em vez de N
                Categories = newCategories;
                _logger?.LogInfo($"[PerformanceVM] {newCategories.Count} categorias carregadas (Tier: {_hardwareTier}, Gamer: {isGamerModeActive})");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[PerformanceVM] Erro ao carregar categorias: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Verifica se o Modo Gamer está ativo
        /// </summary>
        private bool CheckGamerModeActive()
        {
            try
            {
                return _gamerOrchestrator?.IsActive ?? false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Verifica se uma otimização é compatível com o hardware atual
        /// </summary>
        private bool IsOptimizationCompatible(PerformanceOptimization opt, PerformanceSystemProfile? profile, bool gamerModeActive)
        {
            if (profile == null)
                return true; // Se não temos perfil, permitir (será verificado depois)
 
            // Verificar tier mínimo
            if (opt.MinimumTier.HasValue && _hardwareTier < opt.MinimumTier.Value)
            {
                opt.IncompatibilityReason = string.Format(LocalizationService.Instance.GetString("PerfReqStrongerHardware"), opt.MinimumTier.Value);
                return false;
            }
 
            // Verificar GPU dedicada
            if (opt.RequiresDedicatedGPU && !profile.HasDedicatedGPU)
            {
                opt.IncompatibilityReason = LocalizationService.Instance.GetString("PerfReqDedicatedGpu");
                return false;
            }
 
            // Verificar SSD
            if (opt.RequiresSSD && !profile.HasSSD && !profile.HasNVMe)
            {
                opt.IncompatibilityReason = LocalizationService.Instance.GetString("PerfReqSsdNvme");
                return false;
            }
 
            // Verificar RAM mínima
            if (opt.MinimumRAMGB.HasValue && profile.TotalRAMGB < opt.MinimumRAMGB.Value)
            {
                opt.IncompatibilityReason = string.Format(LocalizationService.Instance.GetString("PerfReqMinRam"), opt.MinimumRAMGB.Value);
                return false;
            }
 
            // Verificar núcleos mínimos
            if (opt.MinimumCores.HasValue && profile.CPUCores < opt.MinimumCores.Value)
            {
                opt.IncompatibilityReason = string.Format(LocalizationService.Instance.GetString("PerfReqMinCores"), opt.MinimumCores.Value);
                return false;
            }
 
            // Verificar laptops
            if (opt.NotForLaptops && profile.IsLaptop)
            {
                opt.IncompatibilityReason = LocalizationService.Instance.GetString("PerfNotRecommendedLaptop");
                return false;
            }
 
            // Verificar conflito com Modo Gamer
            if (opt.ConflictsWithGamerMode && gamerModeActive)
            {
                opt.IncompatibilityReason = LocalizationService.Instance.GetString("PerfConflictGamerMode");
                return false;
            }
 
            // Se chegou aqui, é compatível
            opt.IncompatibilityReason = null;
            return true;
        }

        /// <summary>
        /// Verifica se uma otimização é segura para o hardware atual
        /// </summary>
        private bool IsOptimizationSafe(PerformanceOptimization opt)
        {
            // Otimizações avançadas requerem confirmação adicional
            if (opt.Safety == OptimizationSafety.Advanced)
            {
                // TODO: Implementar lógica para otimizações avançadas
                return true; // Por enquanto, permitir todas
            }

            return true;
        }

        private void UpdateCategoryStats(PerfCategoryViewModel category)
        {
            // TODO: Fix missing properties
            // category.SelectedCount = category.Optimizations.Count(o => o.IsSelected);
            // category.TotalCount = category.Optimizations.Count;
            // category.IsEnabled = category.Optimizations.Any(o => o.IsEnabled);
        }

        /// <summary>
        /// Aplica otimizações de uma categoria específica
        /// </summary>
        private async Task ApplyCategoryAsync(PerfCategoryViewModel category)
        {
            if (_performanceService == null || IsOptimizing) return;

            var selected = category.Optimizations
                .Where(o => o.IsSelected)
                .Select(o => string.IsNullOrEmpty(o.BackendName) ? o.Name : o.BackendName)
                .ToList();

            if (selected.Count == 0)
            {
                OptimizationStatus = LocalizationService.Instance.GetString("PerfNoOptSelectedCategory");
                return;
            }
 
            try
            {
                _logger?.LogInfo($"[PerformanceVM] Aplicando {selected.Count} otimizações da categoria '{category.Name}'");
 
                category.IsWorking = true;
                category.StatusText = LocalizationService.Instance.GetString("NetworkApplying");
                IsOptimizing = true;
 
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                var result = await _performanceService.ApplyOptimizationsAsync(selected, cts.Token);
 
                if (result.Success && result.TotalApplied > 0)
                {
                    category.StatusText = string.Format(LocalizationService.Instance.GetString("PerfAppliedCount"), result.TotalApplied);
                    OptimizationStatus = string.Format(LocalizationService.Instance.GetString("PerfAppliedMsg"), result.TotalApplied, category.Name);
                    HistoryService.RecordActivity("System Optimization", $"{result.TotalApplied} otimizações de '{category.Name}' aplicadas.");
                    _logger?.LogSuccess($"[PerformanceVM] Categoria '{category.Name}': {result.TotalApplied} aplicadas");
                    GlobalNotificationService.ShowSuccess(LocalizationService.Instance.GetString("PerfOperationName"), LocalizationService.Instance.GetString("CommonSuccess"));
                }
                else
                {
                    category.StatusText = LocalizationService.Instance.GetString("CommonNoChanges");
                    OptimizationStatus = LocalizationService.Instance.GetString("PerfNoAdditionalOptNeeded");
                    GlobalNotificationService.ShowWarning(LocalizationService.Instance.GetString("PerfOperationName"), LocalizationService.Instance.GetString("PerfNoOptApplied"));
                }
            }
            catch (Exception ex)
            {
                category.StatusText = "❌ " + LocalizationService.Instance.GetString("CommonError");
                OptimizationStatus = $"❌ {LocalizationService.Instance.GetString("CommonError")}: {ex.Message}";
                _logger?.LogError($"[PerformanceVM] Erro ao aplicar categoria '{category.Name}': {ex.Message}", ex);
                GlobalNotificationService.ShowError(LocalizationService.Instance.GetString("PerfOperationName"), $"{LocalizationService.Instance.GetString("CommonError")}: {ex.Message}");
            }
            finally
            {
                IsOptimizing = false;
                category.IsWorking = false;
            }
        }

        /// <summary>
        /// Reverte otimizações de uma categoria específica.
        /// Exibe o modal de reinicialização (mesmo modal do botão principal).
        /// </summary>
        private async Task RevertCategoryAsync(PerfCategoryViewModel category)
        {
            if (_performanceService == null || IsOptimizing) return;

            try
            {
                _logger?.LogInfo($"[PerformanceVM] Revertendo categoria '{category.Name}'");

                // Exibir modal de reinicialização
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
                        modal.ShowDialog();
                        userChoice = modal.UserChoice;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError($"[PerformanceVM] Erro ao exibir modal: {ex.Message}", ex);
                    }
                });

                if (userChoice == VoltrisOptimizer.UI.Windows.RestartConfirmationModal.RestartChoice.Cancel)
                {
                    _logger?.LogInfo($"[PerformanceVM] Reversão da categoria '{category.Name}' cancelada pelo usuário");
                    return;
                }

                bool shouldRestartNow = (userChoice == VoltrisOptimizer.UI.Windows.RestartConfirmationModal.RestartChoice.RestartNow);

                category.IsWorking = true;
                category.StatusText = LocalizationService.Instance.GetString("NetworkRestoring");
                IsOptimizing = true;
 
                // Reverter cada otimização da categoria que tem RevertAction
                int revertedCount = 0;
                foreach (var opt in category.Optimizations)
                {
                    if (opt.RevertAction != null)
                    {
                        try
                        {
                            opt.RevertAction.Invoke();
                            revertedCount++;
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning($"[PerformanceVM] Erro ao reverter '{opt.Name}': {ex.Message}");
                        }
                    }
                }
 
                if (revertedCount > 0)
                {
                    category.StatusText = string.Format(LocalizationService.Instance.GetString("PerfRevertedCount"), revertedCount);
                    OptimizationStatus = string.Format(LocalizationService.Instance.GetString("PerfRevertedMsg"), revertedCount, category.Name);
                    _logger?.LogSuccess($"[PerformanceVM] Categoria '{category.Name}': {revertedCount} revertidas");
                    GlobalNotificationService.ShowSuccess(LocalizationService.Instance.GetString("PerfOperationName"), LocalizationService.Instance.GetString("CommonSuccess"));
 
                    if (shouldRestartNow)
                    {
                        _logger?.LogInfo("[PerformanceVM] Reiniciando após reversão de categoria...");
                        try
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "shutdown",
                                Arguments = $"/r /t 5 /c \"{LocalizationService.Instance.GetString("PerfShutdownRevertText")}\"",
                                CreateNoWindow = true,
                                UseShellExecute = false
                            });
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError($"[PerformanceVM] Erro ao disparar shutdown: {ex.Message}", ex);
                        }
                    }
                }
                else
                {
                    category.StatusText = LocalizationService.Instance.GetString("CommonNothingToRevert");
                    OptimizationStatus = LocalizationService.Instance.GetString("PerfNoOptToRevertCategory");
                }
            }
            catch (Exception ex)
            {
                category.StatusText = "❌ " + LocalizationService.Instance.GetString("CommonError");
                OptimizationStatus = $"❌ {LocalizationService.Instance.GetString("CommonError")}: {ex.Message}";
                _logger?.LogError($"[PerformanceVM] Erro ao reverter categoria '{category.Name}': {ex.Message}", ex);
                GlobalNotificationService.ShowError(LocalizationService.Instance.GetString("PerfOperationName"), $"{LocalizationService.Instance.GetString("CommonError")}: {ex.Message}");
            }
            finally
            {
                IsOptimizing = false;
                category.IsWorking = false;
            }
        }

        /// <summary>
        /// Aplica as otimizações selecionadas (ou as recomendadas, se nada for selecionado)
        /// CORREÇÃO CRÍTICA: Modal de reinício é exibido ANTES de aplicar otimizações
        /// </summary>
        private async Task AutoOptimizeAsync()
        {
            if (_performanceService == null || IsOptimizing)
                return;
 
            // SaaS-LEVEL: Feature Gate centralizado para bloqueio profissional
            _logger?.LogInfo("[PerformanceVM] INICIANDO VERIFICAÇÃO DE FEATURE GATE PARA OTIMIZAÇÃO DE PERFORMANCE...");
            
            // VERIFICAÇÃO ADICIONAL DE SEGURANÇA: Garantir que se a licença foi revogada, a Otimização Inteligente falhe.
            // A Otimização Normal é permitida. Como AutoOptimizeAsync é usado por ambos, verificamos se o Caller queria PRO.
            // Para ser seguro, delegamos a responsabilidade primária de bloqueio ao LicensedCommand,
            // mas mantemos o Feature Gate de IsOptimizationEnabled para controle geral.
            var isOptimizationEnabled = VoltrisOptimizer.Services.License.LicenseOrchestrationService.Features.IsOptimizationEnabled;
            _logger?.LogInfo($"[PerformanceVM] Feature Gate Result: IsOptimizationEnabled={isOptimizationEnabled}");
            
            if (!isOptimizationEnabled)
            {
                _logger?.LogWarning("[PerformanceVM] FEATURE GATE BLOQUEADO - Otimização de performance não está habilitada");
                
                var licenseState = await VoltrisOptimizer.Services.License.LicenseOrchestrationService.Instance.GetCurrentStateAsync();
                _logger?.LogInfo($"[PerformanceVM] Estado: {licenseState.FormattedStatus} ({licenseState.LicenseType})");
                
                var message = string.Format(LocalizationService.Instance.GetString("PerfLicenseGateMsg"), licenseState.FormattedStatus);
                
                VoltrisOptimizer.UI.Controls.LicenseRequiredMessageBox.Show(
                    message,
                    LocalizationService.Instance.GetString("LicenseRequiredTitle"));
                
                _logger?.LogInfo("[PerformanceVM] Otimização de performance BLOQUEADA e finalizada");
                return;
            }
            
            _logger?.LogInfo("[PerformanceVM] FEATURE GATE APROVADO - Otimização de performance habilitada, continuing...");
 
            // Executar com skipIsBusy: true para deixar o GlobalProgressService brilhar no footer
            await ExecuteSafeAsync(async () =>
            {
                _logger?.LogInfo("[PerformanceVM] ========== INÍCIO: Executar Análise ==========");
                _logger?.LogInfo($"[PerformanceVM] Perfil Inteligente Ativo: {SettingsService.Instance.Settings.IntelligentProfile}");
 
                // ETAPA 1: Coletar otimizações selecionadas
                var selectedOptimizations = Categories?
                    .SelectMany(c => c.Optimizations)
                    .Where(o => o.IsSelected)
                    .Select(o => string.IsNullOrEmpty(o.BackendName) ? o.Name : o.BackendName)
                    .Distinct()
                    .ToList() ?? new List<string>();
 
                var optimizationCount = selectedOptimizations.Count > 0 
                    ? selectedOptimizations.Count 
                    : Categories?.SelectMany(c => c.Optimizations).Count(o => o.IsRecommended) ?? 0;
 
                _logger?.LogInfo($"[PerformanceVM] Otimizações a aplicar: {optimizationCount}");
 
                // ETAPA 2: Exibir Modal de Reinício ANTES de aplicar
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
                        modal.ShowDialog();
                        userChoice = modal.UserChoice;
                        _logger?.LogInfo($"[PerformanceVM] Modal resultado: UserChoice={userChoice}");
                    }
                    catch (Exception modalEx)
                    {
                        _logger?.LogError($"[PerformanceVM] Erro ao exibir modal: {modalEx.Message}", modalEx);
                    }
                });
                
                if (userChoice == VoltrisOptimizer.UI.Windows.RestartConfirmationModal.RestartChoice.Cancel)
                {
                    _logger?.LogInfo("[PerformanceVM] ========== CANCELADO: Usuário cancelou no modal ==========");
                    
                    OptimizationStatus = LocalizationService.Instance.GetString("PerfProgressCancelled");
                    IntelligentStatusMessage = LocalizationService.Instance.GetString("ProfCancel");
                    
                    App.TelemetryService?.TrackEvent("PERFORMANCE_OPTIMIZE_CANCELLED", "User", "Modal", forceFlush: true);
                    
                    // Mostrar na barra de progresso global que foi cancelado
                    global::VoltrisOptimizer.Services.GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("PerfOperationName"), isPriority: true);
                    global::VoltrisOptimizer.Services.GlobalProgressService.Instance.UpdateProgress(0, "❌ " + LocalizationService.Instance.GetString("PerfProgressCancelled"));
                    global::VoltrisOptimizer.Services.GlobalProgressService.Instance.CompleteOperation("❌ " + LocalizationService.Instance.GetString("PerfProgressCancelledTitle"));
                    return;
                }
                
                bool shouldRestartNow = (userChoice == VoltrisOptimizer.UI.Windows.RestartConfirmationModal.RestartChoice.RestartNow);
                bool shouldRestartLater = (userChoice == VoltrisOptimizer.UI.Windows.RestartConfirmationModal.RestartChoice.RestartLater);
                _logger?.LogInfo($"[PerformanceVM] Usuário prosseguiu. RestartNow={shouldRestartNow}, RestartLater={shouldRestartLater}");
 
                // ETAPA 3: Preparar aplicação
                IsOptimizing = true;
                OptimizationStatus = LocalizationService.Instance.GetString("PerfApplyingOptimizations");
                IntelligentStatusMessage = LocalizationService.Instance.GetString("PerfAnalyzingSystem");
                
                string choiceLabel = shouldRestartNow ? LocalizationService.Instance.GetString("RestartNowLabel") : LocalizationService.Instance.GetString("RestartLaterLabel");
                
                global::VoltrisOptimizer.Services.GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("PerfOperationName"), isPriority: true);
                global::VoltrisOptimizer.Services.GlobalProgressService.Instance.UpdateProgress(5, string.Format(LocalizationService.Instance.GetString("PerfIniciando"), choiceLabel));
 
                _logger?.LogInfo("[PerformanceVM] Iniciando aplicação de otimizações...");
 
                // ETAPA: Criar ponto de restauração antes de qualquer alteração
                global::VoltrisOptimizer.Services.GlobalProgressService.Instance.UpdateProgress(10, LocalizationService.Instance.GetString("CreatingRestorePointMsg"));
                _logger?.LogInfo("[PerformanceVM] Criando ponto de restauração do sistema...");
                try
                {
                    var systemTools = new VoltrisOptimizer.Services.SystemToolsService(_logger!);
                    var restorePointCreated = await systemTools.CreateSystemRestorePointAsync("Otimizacao Inteligente - Voltris", silent: true);
                    if (restorePointCreated)
                    {
                        _logger?.LogSuccess("[PerformanceVM] Ponto de restauração criado com sucesso.");
                        global::VoltrisOptimizer.Services.GlobalProgressService.Instance.UpdateProgress(15, LocalizationService.Instance.GetString("RestorePointCreatedSuccess"));
                    }
                    else
                    {
                        _logger?.LogWarning("[PerformanceVM] Ponto de restauração não foi criado (prosseguindo mesmo assim).");
                        global::VoltrisOptimizer.Services.GlobalProgressService.Instance.UpdateProgress(15, LocalizationService.Instance.GetString("PerfWarningNoRestorePoint"));
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError($"[PerformanceVM] Erro ao criar ponto de restauração: {ex.Message}");
                    global::VoltrisOptimizer.Services.GlobalProgressService.Instance.UpdateProgress(15, LocalizationService.Instance.GetString("PerfWarningNoRestorePoint"));
                }
 
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
 
                VoltrisOptimizer.Services.Performance.Models.PerformanceOptimizationResult result;
 
                if (selectedOptimizations.Count > 0)
                {
                    _logger?.LogInfo($"[PerformanceVM] Aplicando {selectedOptimizations.Count} otimizações selecionadas pelo usuário...");
                    
                    IntelligentStatusMessage = string.Format(LocalizationService.Instance.GetString("PerfApplyingOptimizationsCount"), selectedOptimizations.Count);
                    global::VoltrisOptimizer.Services.GlobalProgressService.Instance.UpdateProgress(20, string.Format(LocalizationService.Instance.GetString("PerfApplyingOptimizationsCount"), selectedOptimizations.Count));
                    
                    App.TelemetryService?.TrackEvent("PERFORMANCE_OPTIMIZE", "Manual", "Start", metadata: new { Count = selectedOptimizations.Count }, forceFlush: true);
 
                    result = await _performanceService.ApplyOptimizationsAsync(selectedOptimizations, cts.Token);
                }
                else
                {
                    _logger?.LogInfo("[PerformanceVM] Nenhuma otimização selecionada manualmente. Aplicando pacote recomendado...");
                    
                    IntelligentStatusMessage = LocalizationService.Instance.GetString("PerfApplyingRecommended");
                    global::VoltrisOptimizer.Services.GlobalProgressService.Instance.UpdateProgress(20, LocalizationService.Instance.GetString("PerfApplyingRecommended"));
                    
                    App.TelemetryService?.TrackEvent("PERFORMANCE_OPTIMIZE", "Recommended", "Start", forceFlush: true);
 
                    result = await _performanceService.ApplyRecommendedOptimizationsAsync(cts.Token);
                }
 
                // ETAPA 4: Processar resultado
                _logger?.LogInfo($"[PerformanceVM] Resultado: Success={result.Success}, TotalApplied={result.TotalApplied}");
                
                App.TelemetryService?.TrackEvent("PERFORMANCE_OPTIMIZE_END", selectedOptimizations.Count > 0 ? "Manual" : "Recommended", "End", success: result.Success, metadata: new { Count = result.TotalApplied }, forceFlush: true);
 
                if (result.Success || result.TotalApplied > 0)
                {
                    if (result.TotalApplied > 0)
                    {
                        var errorCount = result.Errors?.Count ?? 0;
                        string globalMsg;
                        
                        if (errorCount > 0)
                        {
                            OptimizationStatus = string.Format(LocalizationService.Instance.GetString("PerfAppliedShort"), result.TotalApplied, errorCount);
                            IntelligentStatusMessage = string.Format(LocalizationService.Instance.GetString("PerfAppliedCount"), result.TotalApplied);
                            globalMsg = $"{string.Format(LocalizationService.Instance.GetString("PerfAppliedShort"), result.TotalApplied, errorCount)} — {choiceLabel}";
                            _logger?.LogWarning($"[PerformanceVM] SUCESSO PARCIAL: {result.TotalApplied} aplicadas, {errorCount} avisos/falhas.");
                        }
                        else
                        {
                            OptimizationStatus = string.Format(LocalizationService.Instance.GetString("PerfAppliedSuccessMsg"), result.TotalApplied);
                            IntelligentStatusMessage = string.Format(LocalizationService.Instance.GetString("PerfAppliedCount"), result.TotalApplied);
                            globalMsg = $"{string.Format(LocalizationService.Instance.GetString("PerfAppliedCount"), result.TotalApplied)} — {choiceLabel}";
                            _logger?.LogSuccess($"[PerformanceVM] ========== SUCESSO: {result.TotalApplied} otimizações aplicadas ==========");
                            GlobalNotificationService.ShowSuccess(LocalizationService.Instance.GetString("PerfAppliedSuccessTitle"), string.Format(LocalizationService.Instance.GetString("PerfAppliedSuccessMsg"), result.TotalApplied));
                        }
 
                        HistoryService.RecordActivity("System Optimization", $"Otimização completa: {result.TotalApplied} melhorias aplicadas.");
                        global::VoltrisOptimizer.Services.GlobalProgressService.Instance.UpdateProgress(90, globalMsg);
 
                        // ETAPA 5: Reiniciar sistema caso usuário tenha escolhido RestartNow
                        if (shouldRestartNow)
                        {
                            global::VoltrisOptimizer.Services.GlobalProgressService.Instance.UpdateProgress(95, LocalizationService.Instance.GetString("PerfRestartingShort"));
                            _logger?.LogInfo("[PerformanceVM] Otimizações concluídas. Reiniciando o sistema...");
                            try
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = "shutdown",
                                    Arguments = $"/r /t 5 /c \"{LocalizationService.Instance.GetString("PerfShutdownSuccessText")}\"",
                                    CreateNoWindow = true,
                                    UseShellExecute = false
                                });
                            }
                            catch (Exception ex)
                            {
                                _logger?.LogError($"[PerformanceVM] Erro crítico ao tentar disparar o shutdown: {ex.Message}", ex);
                            }
                        }
                        
                        global::VoltrisOptimizer.Services.GlobalProgressService.Instance.CompleteOperation(globalMsg);
                    }
                    else
                    {
                        OptimizationStatus = LocalizationService.Instance.GetString("PerfNoAdditionalOptNeeded");
                        IntelligentStatusMessage = LocalizationService.Instance.GetString("PerfSystemOptimizedNoActionShort");
                        
                        global::VoltrisOptimizer.Services.GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("PerfSystemAlreadyOptimizedShort"));
                        _logger?.LogInfo("[PerformanceVM] ========== CONCLUÍDO: Sistema já otimizado ==========");
                        GlobalNotificationService.ShowInfo(LocalizationService.Instance.GetString("PerfSystemOptimizedTitle"), LocalizationService.Instance.GetString("PerfSystemAlreadyOptimizedMsg"));
                    }
                }
                else
                {
                    var errorCount = result.Errors?.Count ?? 0;
                    
                    if (result.Success && result.TotalApplied == 0)
                    {
                        OptimizationStatus = LocalizationService.Instance.GetString("PerfRestrictedByProfile");
                        IntelligentStatusMessage = LocalizationService.Instance.GetString("PerfRestrictedByProfileShort");
                        global::VoltrisOptimizer.Services.GlobalProgressService.Instance.CompleteOperation($"ℹ️ {LocalizationService.Instance.GetString("PerfRestrictedByProfileShort")} ({SettingsService.Instance.Settings.IntelligentProfile})");
                    }
                    else
                    {
                        OptimizationStatus = errorCount > 0
                            ? string.Format(LocalizationService.Instance.GetString("PerfOptimizationError"), errorCount)
                            : LocalizationService.Instance.GetString("PerfOptimizationFailed");
                        IntelligentStatusMessage = LocalizationService.Instance.GetString("PerfOptimizationErrorShort");
 
                        global::VoltrisOptimizer.Services.GlobalProgressService.Instance.CompleteOperation(errorCount > 0
                            ? string.Format(LocalizationService.Instance.GetString("PerfOptimizationError"), errorCount)
                            : LocalizationService.Instance.GetString("PerfOptimizationFailed"));
                        _logger?.LogError($"[PerformanceVM] ========== ERRO: Falha ao aplicar otimizações ==========");
                        if (result.Errors != null)
                        {
                            foreach (var err in result.Errors) _logger?.LogError($"[PerformanceVM] Detalhe do Erro: {err}");
                        }
                    }
                }
                
                // CORREÇÃO CRÍTICA: Garantir que IsOptimizing seja definido como false após conclusão
                IsOptimizing = false;
                if (result.Success && result.TotalApplied > 0)
                {
                    OptimizationStatus = string.Format(LocalizationService.Instance.GetString("PerfAppliedMsgShort"), result.TotalApplied);
                    IntelligentStatusMessage = LocalizationService.Instance.GetString("PerfOptimizationsAlreadyApplied");
                }
                else
                {
                    OptimizationStatus = LocalizationService.Instance.GetString("CommonReady");
                    IntelligentStatusMessage = LocalizationService.Instance.GetString("PerfReadyForAnalysis");
                }
            });
        }

        private async Task RevertAllAsync()
        {
            // SaaS-LEVEL: Feature Gate centralizado para bloqueio profissional
            _logger?.LogInfo("[PerformanceVM] INICIANDO VERIFICAÇÃO DE FEATURE GATE PARA REVERSÃO DE OTIMIZAÇÕES...");
            
            var isOptimizationEnabled = VoltrisOptimizer.Services.License.LicenseOrchestrationService.Features.IsOptimizationEnabled;
            _logger?.LogInfo($"[PerformanceVM] Feature Gate Result: IsOptimizationEnabled={isOptimizationEnabled}");
            
            if (!isOptimizationEnabled)
            {
                _logger?.LogWarning("[PerformanceVM] FEATURE GATE BLOQUEADO - Reversão de otimizações não está habilitada");
                
                var licenseState = await VoltrisOptimizer.Services.License.LicenseOrchestrationService.Instance.GetCurrentStateAsync();
                _logger?.LogInfo($"[PerformanceVM] Estado: {licenseState.FormattedStatus} ({licenseState.LicenseType})");
                
                var message = string.Format(LocalizationService.Instance.GetString("PerfRevertLicenseGateMsg"), licenseState.FormattedStatus);
                
                VoltrisOptimizer.UI.Controls.LicenseRequiredMessageBox.Show(
                    message,
                    LocalizationService.Instance.GetString("LicenseRequiredTitle"));
                
                _logger?.LogInfo("[PerformanceVM] Reversão de otimizações BLOQUEADA e finalizada");
                return;
            }
            
            _logger?.LogInfo("[PerformanceVM] FEATURE GATE APROVADO - Reversão de otimizações habilitada, continuando...");
 
            if (_performanceService == null || IsOptimizing)
                return;
 
            try
            {
                _logger?.LogInfo("[PerformanceVM] ========== INÍCIO: Reverter Tudo ==========");
 
                // ETAPA 1: Exibir modal de reinicialização ANTES de reverter
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
                        modal.ShowDialog();
                        userChoice = modal.UserChoice;
                        _logger?.LogInfo($"[PerformanceVM] Modal reversão resultado: UserChoice={userChoice}");
                    }
                    catch (Exception modalEx)
                    {
                        _logger?.LogError($"[PerformanceVM] Erro ao exibir modal de reversão: {modalEx.Message}", modalEx);
                    }
                });
 
                if (userChoice == VoltrisOptimizer.UI.Windows.RestartConfirmationModal.RestartChoice.Cancel)
                {
                    _logger?.LogInfo("[PerformanceVM] ========== CANCELADO: Usuário cancelou reversão no modal ==========");
                    OptimizationStatus = LocalizationService.Instance.GetString("PerfRevertCancelled");
                    IntelligentStatusMessage = LocalizationService.Instance.GetString("ProfCancel");
                    App.TelemetryService?.TrackEvent("PERFORMANCE_REVERT_CANCELLED", "User", "Modal", forceFlush: true);
                    
                    // Mostrar na barra de progresso global que foi cancelado
                    global::VoltrisOptimizer.Services.GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("PerfReversionOperationName"), isPriority: true);
                    global::VoltrisOptimizer.Services.GlobalProgressService.Instance.UpdateProgress(0, "❌ " + LocalizationService.Instance.GetString("PerfRevertCancelled"));
                    global::VoltrisOptimizer.Services.GlobalProgressService.Instance.CompleteOperation("❌ " + LocalizationService.Instance.GetString("PerfRevertCancelledShort"));
                    return;
                }
 
                bool shouldRestartNow = (userChoice == VoltrisOptimizer.UI.Windows.RestartConfirmationModal.RestartChoice.RestartNow);
                bool shouldRestartLater = (userChoice == VoltrisOptimizer.UI.Windows.RestartConfirmationModal.RestartChoice.RestartLater);
                string choiceLabel = shouldRestartNow ? LocalizationService.Instance.GetString("RestartNowLabel") : LocalizationService.Instance.GetString("RestartLaterLabel");
                _logger?.LogInfo($"[PerformanceVM] Usuário confirmou reversão. RestartNow={shouldRestartNow}");
 
                // ETAPA 2: Executar reversão
                IsOptimizing = true;
                OptimizationStatus = LocalizationService.Instance.GetString("PerfRevertingOptimizations");
                IntelligentStatusMessage = LocalizationService.Instance.GetString("PerfRevertingOptimizationsShort");
 
                global::VoltrisOptimizer.Services.GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("PerfRevertingOptimizationsTitle"), isPriority: true);
                global::VoltrisOptimizer.Services.GlobalProgressService.Instance.UpdateProgress(10, string.Format(LocalizationService.Instance.GetString("PerfRevertingProgress"), choiceLabel));
 
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
 
                App.TelemetryService?.TrackEvent("PERFORMANCE_REVERT", "All", "Start", forceFlush: true);
 
                var result = await _performanceService.RevertAllOptimizationsAsync(cts.Token);
 
                App.TelemetryService?.TrackEvent("PERFORMANCE_REVERT_END", "All", "End", success: result.Success, metadata: new { Count = result.TotalApplied }, forceFlush: true);
 
                if (result.Success)
                {
                    if (result.TotalApplied > 0)
                    {
                        OptimizationStatus = string.Format(LocalizationService.Instance.GetString("PerfRevertSuccessMsg"), result.TotalApplied);
                        IntelligentStatusMessage = string.Format(LocalizationService.Instance.GetString("PerfRevertSuccessShort"), result.TotalApplied);
                        string globalMsg = $"{string.Format(LocalizationService.Instance.GetString("PerfRevertSuccessShort"), result.TotalApplied)} — {choiceLabel}";
                        HistoryService.RecordActivity("System Optimization", $"{result.TotalApplied} otimizações do sistema foram restauradas.");
                        global::VoltrisOptimizer.Services.GlobalProgressService.Instance.UpdateProgress(90, globalMsg);
 
                        // ETAPA 3: Reiniciar se o usuário escolheu
                        if (shouldRestartNow)
                        {
                            global::VoltrisOptimizer.Services.GlobalProgressService.Instance.UpdateProgress(95, LocalizationService.Instance.GetString("PerfRestartingShort"));
                            _logger?.LogInfo("[PerformanceVM] Reversão concluída. Reiniciando o sistema...");
                            try
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = "shutdown",
                                    Arguments = $"/r /t 5 /c \"{LocalizationService.Instance.GetString("PerfShutdownRevertText")}\"",
                                    CreateNoWindow = true,
                                    UseShellExecute = false
                                });
                            }
                            catch (Exception ex)
                            {
                                _logger?.LogError($"[PerformanceVM] Erro ao disparar shutdown após reversão: {ex.Message}", ex);
                            }
                        }
                        
                        global::VoltrisOptimizer.Services.GlobalProgressService.Instance.CompleteOperation(globalMsg);
                        GlobalNotificationService.ShowSuccess(LocalizationService.Instance.GetString("PerfOperationName"), LocalizationService.Instance.GetString("PerfRevertAllSuccessMsg"));
                    }
                    else
                    {
                        OptimizationStatus = LocalizationService.Instance.GetString("PerfNoActiveOptToRevert");
                        IntelligentStatusMessage = LocalizationService.Instance.GetString("CommonNothingToRevert");
                        global::VoltrisOptimizer.Services.GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("PerfNoOptToRevertShort"));
                        _logger?.LogInfo("[PerformanceVM] ========== CONCLUÍDO: Nenhuma otimização para reverter ==========");
                    }
                }
                else
                {
                    var errorCount = result.Errors?.Count ?? 0;
                    OptimizationStatus = errorCount > 0
                        ? string.Format(LocalizationService.Instance.GetString("PerfRevertError"), errorCount)
                        : LocalizationService.Instance.GetString("PerfRevertFailed");
                    IntelligentStatusMessage = LocalizationService.Instance.GetString("PerfRevertErrorShort");
 
                    global::VoltrisOptimizer.Services.GlobalProgressService.Instance.CompleteOperation(errorCount > 0
                        ? string.Format(LocalizationService.Instance.GetString("PerfRevertError"), errorCount)
                        : LocalizationService.Instance.GetString("PerfRevertFailed"));
                    _logger?.LogError($"[PerformanceVM] ========== ERRO: Falha ao reverter. Revertidas: {result.TotalApplied}, Erros: {errorCount} ==========");
                }
            }
            catch (OperationCanceledException)
            {
                OptimizationStatus = LocalizationService.Instance.GetString("PerfRevertTimeout");
                IntelligentStatusMessage = LocalizationService.Instance.GetString("PerfRevertTimeoutShort");
                _logger?.LogWarning("[PerformanceVM] ========== TIMEOUT: Reversão cancelada ==========");
                global::VoltrisOptimizer.Services.GlobalProgressService.Instance.CompleteOperation("❌ " + LocalizationService.Instance.GetString("PerfRevertCancelledShort") + " (Timeout)");
            }
            catch (Exception ex)
            {
                OptimizationStatus = $"❌ {LocalizationService.Instance.GetString("CommonError")}: {ex.Message}";
                IntelligentStatusMessage = LocalizationService.Instance.GetString("PerfRevertErrorShort");
                _logger?.LogError("[PerformanceVM] ========== EXCEÇÃO: Erro na reversão ==========", ex);
                global::VoltrisOptimizer.Services.GlobalProgressService.Instance.CompleteOperation($"❌ {LocalizationService.Instance.GetString("PerfRevertErrorShort")}: {ex.Message}");
                GlobalNotificationService.ShowError(LocalizationService.Instance.GetString("PerfOperationName"), $"{LocalizationService.Instance.GetString("PerfRevertErrorShort")}: {ex.Message}");
            }
            finally
            {
                IsOptimizing = false;
                _logger?.LogInfo("[PerformanceVM] ========== FIM: Reverter Tudo ==========");
            }
        }
    }
}
