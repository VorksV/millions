using VoltrisOptimizer.Utils;
using System;

using System.Collections.Generic;

using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using VoltrisOptimizer.Helpers;

using System.IO;

using System.Linq;

using System.Runtime.CompilerServices;

using System.Threading.Tasks;

using System.Windows;

using System.Windows.Input;

using System.Windows.Threading;

using VoltrisOptimizer.Core;

using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Performance;
using VoltrisOptimizer.Interfaces;

using VoltrisOptimizer.Services.Thermal;

using VoltrisOptimizer.Services.Thermal.Models;

using VoltrisOptimizer.Services.Monitoring;

using VoltrisOptimizer.Services.Gamer.Interfaces;
using VoltrisOptimizer.Services.Monitoring.Interfaces;
using VoltrisInsightService = VoltrisOptimizer.VoltrisGlobalInsightService;

namespace VoltrisOptimizer.UI.Widgets.ViewModels 
{
/// <summary>
/// ViewModel do widget flutuante(MVVM) - CORRIGIDO
/// MUDANÇAS:
/// - Usa SystemMetricsCache(mesma fonte que Dashboard) em vez de SafePerformanceCounter
/// - Usa GlobalThermalMonitorService para temperatura precisa
/// - Timer de atualização otimizado(500ms) lendo do cache já atualizado
/// </summary>
public class WidgetViewModel : INotifyPropertyChanged, IDisposable 
{
    private readonly ILoggingService _logger;
    private readonly VoltrisInsightService _insightService;

    private IGlobalThermalMonitorService? _thermalService;
    private bool _isOptimizing;
    private DateTime _lastMetricsUpdate = DateTime.MinValue;

    // Propriedades bindáveis
    private double _cpuUsage;

    public double CpuUsage
    {
        get => _cpuUsage;
        set
        {
            if (Math.Abs(_cpuUsage - value) < 0.1) return;
            _cpuUsage = value;
            OnPropertyChanged();
        }
    }

    private double _ramUsage;

    public double RamUsage
    {
        get => _ramUsage;
        set
        {
            if (Math.Abs(_ramUsage - value) < 0.1) return;
            _ramUsage = value;
            OnPropertyChanged();
        }
    }

    private double _diskUsage;

    public double DiskUsage
    {
        get => _diskUsage;
        set
        {
            if (Math.Abs(_diskUsage - value) < 0.1) return;
            _diskUsage = value;
            OnPropertyChanged();
        }
    }

    private double _gpuUsage;

    public double GpuUsage
    {
        get => _gpuUsage;
        set
        {
            if (Math.Abs(_gpuUsage - value) < 0.1) return;
            _gpuUsage = value;
            OnPropertyChanged();
        }
    }

    // ... (rest of properties)

    private bool _showGpu = true;
    public bool ShowGpu
    {
        get => _showGpu;
        set { _showGpu = value; OnPropertyChanged(); OnPropertyChanged(nameof(GpuVisible)); SaveSettings(); }
    }
    public bool GpuVisible => _showGpu;

    private double? _cpuTemperature;

    public double? CpuTemperature
    {
        get => _cpuTemperature;
        set
        {
            if (_cpuTemperature == value) return;
            bool wasVisible = _cpuTemperature.HasValue;
            _cpuTemperature = value;
            OnPropertyChanged();
            
            if (wasVisible != value.HasValue)
            {
                OnPropertyChanged(nameof(TemperatureVisible));
                OnPropertyChanged(nameof(TempMenuVisible));
            }
        }
    }

    public bool TemperatureVisible => CpuTemperature.HasValue;

    private double _currentFps;

    public double CurrentFps
    {
        get => _currentFps;
        set
        {
            if (Math.Abs(_currentFps - value) < 0.1) return;
            _currentFps = value;
            OnPropertyChanged();
        }
    }

    private double _onePercentLowFps;
    public double OnePercentLowFps
    {
        get => _onePercentLowFps;
        set { if (Math.Abs(_onePercentLowFps - value) < 0.1) return; _onePercentLowFps = value; OnPropertyChanged(); }
    }

    private double _averageFrametimeMs;
    public double AverageFrametimeMs
    {
        get => _averageFrametimeMs;
        set { if (Math.Abs(_averageFrametimeMs - value) < 0.1) return; _averageFrametimeMs = value; OnPropertyChanged(); }
    }

    private bool _isStuttering;
    public bool IsStuttering
    {
        get => _isStuttering;
        set { if (_isStuttering == value) return; _isStuttering = value; OnPropertyChanged(); }
    }

    private bool _fpsAvailable = false;
    public bool FpsAvailable
    {
        get => _fpsAvailable;
        private set { if (_fpsAvailable == value) return; _fpsAvailable = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Verdadeiro quando: FPS monitor está ativo E o usuário não ocultou via menu de contexto.
    /// INDEPENDENTE do toggle "Modo Gamer Automático" da página Gamer.
    /// </summary>
    public bool FpsVisible => FpsAvailable && _showFpsMetric;

    private bool _isVisible = true;

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible == value) return;
            _isVisible = value;
            OnPropertyChanged();

            // CORREÇÃO BUG REATIVAÇÃO: Ao se tornar visível após Hide/Show,
            // forçar atualização imediata de métricas.
            // Antes desta correção, UpdateMetricsFromCache() retornava em
            // "if (!IsVisible) return" logo na primeira linha — deixando o
            // widget em branco exibindo apenas o efeito acrylic até o próximo
            // evento externo (ex: clique no botão de otimização de memória).
            if (value && _monitoringStarted)
            {
                UpdateMetricsFromCache(null, EventArgs.Empty);
            }
        }
    }

    //Debug counter para FPS
    private int _fpsUpdateCount = 0;

    public bool IsOptimizing
    {
        get => _isOptimizing;
        set
        {
            _isOptimizing = value;
            OnPropertyChanged();
        }
    }

    private string _activeProfileName = "";
    public string ActiveProfileName
    {
        get => _activeProfileName;
        set
        {
            _activeProfileName = value;
            OnPropertyChanged();
        }
    }

    // FASE 7: Brain V2 Metrics
    private string _brainStatus = "---";
    public string BrainStatus
    {
        get => _brainStatus;
        set
        {
            _brainStatus = value;
            OnPropertyChanged();
        }
    }

    private string _brainAction = "";
    public string BrainAction
    {
        get => _brainAction;
        set
        {
            _brainAction = value;
            OnPropertyChanged();
        }
    }

    private double _progressValue;
    public double ProgressValue
    {
        get => _progressValue;
        set
        {
            _progressValue = value;
            OnPropertyChanged();
        }
    }



    private bool _smartMode = true;

    public bool SmartMode
    {
        get => _smartMode;
        set
        {
            _smartMode = value;
            OnPropertyChanged();
            SmartModeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool _gamerOnlyMode = false;

    public bool GamerOnlyMode
    {
        get => _gamerOnlyMode;
        set
        {
            _gamerOnlyMode = value;
            OnPropertyChanged();
        }
    }

    // ========================= VISIBILIDADE INDIVIDUAL DAS MÉTRICAS =========================

    private bool _showCpu = true;
    public bool ShowCpu
    {
        get => _showCpu;
        set { _showCpu = value; OnPropertyChanged(); OnPropertyChanged(nameof(CpuVisible)); SaveSettings(); }
    }
    public bool CpuVisible => _showCpu;

    private bool _showRam = true;
    public bool ShowRam
    {
        get => _showRam;
        set { _showRam = value; OnPropertyChanged(); OnPropertyChanged(nameof(RamVisible)); SaveSettings(); }
    }
    public bool RamVisible => _showRam;

    private bool _showDisk = true;
    public bool ShowDisk
    {
        get => _showDisk;
        set { _showDisk = value; OnPropertyChanged(); OnPropertyChanged(nameof(DiskVisible)); SaveSettings(); }
    }
    public bool DiskVisible => _showDisk;

    private bool _showTemp = true;
    public bool ShowTemp
    {
        get => _showTemp;
        set { _showTemp = value; OnPropertyChanged(); OnPropertyChanged(nameof(TempMenuVisible)); SaveSettings(); }
    }
	// TempMenuVisible: visibilidade do item TEMP controlada APENAS pelo toggle do menu.
	//
	// CORREÇÃO: antes era `_showTemp && TemperatureVisible`, e TemperatureVisible
	// dependia de CpuTemperature.HasValue. Efeito: sem sensor, a LINHA INTEIRA era
	// removida do widget e a métrica "sumia" — o usuário reportou exatamente isso.
	//
	// A métrica é um recurso que o usuário liga; ela deve continuar visível e
	// mostrar "N/D" quando não há sensor. Ocultar o item e retirar a escolha do
	// usuário é diferente de informar ausência de leitura. A distinção entre
	// "sem dado" e "dado normal" é feita pelo TEXTO, não pela visibilidade.
	public bool TempMenuVisible => _showTemp;

    private bool _showFpsMetric = true;
    public bool ShowFpsMetric
    {
        get => _showFpsMetric;
        set { _showFpsMetric = value; OnPropertyChanged(); OnPropertyChanged(nameof(FpsVisible)); SaveSettings(); }
    }
    // FpsVisible agora combina: FPS disponível E usuário não ocultou
    // (sobrescreve a definição simples anterior)

    private bool _startWithWindows = false;

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            _startWithWindows = value;
            OnPropertyChanged();
        }
    }

    //Comandos
    public ICommand OptimizeRamCommand { get; }
    public ICommand CloseWidgetCommand { get; }
    public ICommand OpenDashboardCommand { get; }

    //NOVOS COMANDOS DO MENU DE CONTEXTO
    public ICommand ToggleSmartModeCommand { get; }
    public ICommand ToggleGamerOnlyModeCommand { get; }
    public ICommand ToggleStartupCommand { get; }

    // COMANDOS DE VISIBILIDADE DE MÉTRICAS
    public ICommand ToggleCpuCommand { get; }
    public ICommand ToggleRamCommand { get; }
    public ICommand ToggleDiskCommand { get; }
    public ICommand ToggleGpuCommand { get; }
    public ICommand ToggleTempCommand { get; }
    public ICommand ToggleFpsMetricCommand { get; }

    public event PropertyChangedEventHandler PropertyChanged;

    public WidgetViewModel(ILoggingService logger, VoltrisInsightService insightService)
    {
        _logger = logger;
        try { VoltrisDiagnosticSystem.Instance.Timeline("WIDGET", "WidgetViewModel: Construtor INÍCIO"); } catch { }
        _insightService = insightService;
        
        _logger?.LogDebug("[WIDGET] Iniciando WidgetViewModel");

        // Handlers NOMEADOS, e não lambdas anônimas.
        // Estas fontes vivem mais que a View (BrainMetricsCache é static readonly,
        // SettingsService e GlobalProgressService são singletons). Com lambda, não
        // existe como fazer -=: cada reabertura do widget deixava um WidgetViewModel
        // preso, ainda recebendo PropertyChanged e fazendo Dispatcher.BeginInvoke
        // para sempre. Agora o Dispose realmente solta.
        SettingsService.Instance.ProfileChanged += OnSettingsProfileChanged;
        ActiveProfileName = GetLocalizedProfileName(SettingsService.Instance.Settings.IntelligentProfile);

        OptimizeRamCommand = new AsyncRelayCommand(async _ => await OptimizeRamAsync(), _ => !IsOptimizing);

        CloseWidgetCommand = new RelayCommand(_ => { });
        OpenDashboardCommand = new RelayCommand(_ => OpenDashboardRequested?.Invoke(this, EventArgs.Empty));


        ToggleSmartModeCommand = new RelayCommand(_ => ToggleSmartMode());
        ToggleGamerOnlyModeCommand = new RelayCommand(_ => GamerOnlyMode = !GamerOnlyMode);
        ToggleStartupCommand = new RelayCommand(_ => ToggleStartup());

        ToggleCpuCommand  = new RelayCommand(_ => ShowCpu  = !ShowCpu);
        ToggleRamCommand  = new RelayCommand(_ => ShowRam  = !ShowRam);
        ToggleDiskCommand = new RelayCommand(_ => ShowDisk = !ShowDisk);
        ToggleGpuCommand  = new RelayCommand(_ => ShowGpu  = !ShowGpu);
        ToggleTempCommand = new RelayCommand(_ => ShowTemp = !ShowTemp);
        ToggleFpsMetricCommand = new RelayCommand(_ => ShowFpsMetric = !ShowFpsMetric);

        LoadSettings();

        // FASE 7: Brain V2 Metrics — cache singleton para widget
        try
        {
            var brainMetrics = Core.Brain.V2.BrainMetricsCache.Instance;
            var brain = App.Services?.GetService<Core.Brain.V2.VoltrisBrainV2>();
            if (brain != null && !string.IsNullOrEmpty(brainMetrics.BrainStatus))
            {
                brainMetrics.Initialize(brain);
                BrainStatus = brainMetrics.BrainStatus;
                brainMetrics.PropertyChanged += OnBrainMetricsPropertyChanged;
            }
        }
        catch { /* Brain não disponível — widget funciona sem */ }

        // Inscrever para atualizações de progresso globais (Otimização Rápida)
        GlobalProgressService.Instance.ProgressChanged += OnGlobalProgressChanged;
        GlobalProgressService.Instance.OperationCompleted += OnGlobalOperationCompleted;

        _logger?.LogDebug("[WIDGET] WidgetViewModel inicializado");
        try { VoltrisDiagnosticSystem.Instance.Timeline("WIDGET", "WidgetViewModel: Construtor CONCLUÍDO"); } catch { }
    }

    // ── Handlers das assinaturas de longa duração ──────────────────────────
    // Precisam existir como membros para que o Dispose consiga desassinar.

    private void OnSettingsProfileChanged(object? sender, VoltrisOptimizer.Services.IntelligentProfileType profile)
    {
        ActiveProfileName = GetLocalizedProfileName(profile);
    }

    private void OnBrainMetricsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Core.Brain.V2.BrainMetricsCache.BrainStatus) &&
            e.PropertyName != nameof(Core.Brain.V2.BrainMetricsCache.ActiveAction))
            return;

        System.Windows.Application.Current?.Dispatcher?.InvokeAsync(() =>
        {
            var brainMetrics = Core.Brain.V2.BrainMetricsCache.Instance;
            if (e.PropertyName == nameof(Core.Brain.V2.BrainMetricsCache.BrainStatus))
                BrainStatus = brainMetrics.BrainStatus;
            else
                BrainAction = brainMetrics.ActiveAction;
        });
    }

    /// <summary>
    /// True enquanto a barra do widget está sendo movida por uma otimeração.
    /// Separa as duas responsabilidades que brigavam pela mesma
    /// ProgressValue: o progresso da otimização e o uso de CPU.
    ///
    /// Antes, isso dependia de GlobalProgressService.IsOperationRunning e do
    /// nome da operação. Uma tarefa que sobrasse no serviço deixava
    /// IsOperationRunning travado em true, e a barra ficava cheia para sempre
    /// — só voltava ao normal ao reiniciar o app.
    /// </summary>
    private bool _progressDrivenByOperation;

    private void OnGlobalProgressChanged(object? sender, VoltrisOptimizer.Services.ProgressEventArgs e)
    {
        try
        {
            if (!IsOptimizing) return;

            // Só uma operação REALMENTE em andamento pode tomar a barra.
            if (!GlobalProgressService.Instance.IsOperationRunning) return;

            _progressDrivenByOperation = true;
            ProgressValue = e.Percentage;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug($"[WidgetViewModel] Erro ao tratar progresso global: {ex.Message}");
        }
    }

    private void OnGlobalOperationCompleted(object? sender, EventArgs e)
    {
        // A operação acabou: a barra volta a ser o uso de CPU imediatamente.
        _progressDrivenByOperation = false;
        // Força uma atualização imediata para restaurar CPU
        UpdateMetricsFromCache(null, EventArgs.Empty);
    }

    private bool _isThermalSubscribed = false;

    private void SubscribeThermalEvents()
    {
        if (_isThermalSubscribed) return;
        try
        {
            _thermalService = VoltrisOptimizer.Services.Thermal.GlobalThermalMonitorService.Instance;
            if (_thermalService != null)
            {
                _thermalService.MetricsUpdated += OnThermalMetricsUpdated;
                _isThermalSubscribed = true;
                _logger?.LogInfo("[WIDGET] Inscrito no GlobalThermalMonitorService");
                var metrics = _thermalService.CurrentMetrics;
                if (metrics != null)
                    UpdateTemperatureFromMetrics(metrics);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError($"[WIDGET] Erro ao inscrever no serviço térmico: {ex.Message}");
        }
    }

    private void UnsubscribeThermalEvents()
    {
        if (!_isThermalSubscribed) return;
        try
        {
            if (_thermalService != null)
            {
                _thermalService.MetricsUpdated -= OnThermalMetricsUpdated;
                _isThermalSubscribed = false;
                _logger?.LogInfo("[WIDGET] Desinscrito do GlobalThermalMonitorService");
            }
        }
        catch { }
    }

    private ThermalMetrics? _pendingWidgetMetrics;
    private bool _widgetUiUpdatePending;

    private void OnThermalMetricsUpdated(object? sender, ThermalMetrics metrics)
    {
        try
        {
            if (metrics == null)
                return;

            _pendingWidgetMetrics = metrics;
            if (_widgetUiUpdatePending) return;
            _widgetUiUpdatePending = true;

            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                _widgetUiUpdatePending = false;
                var latest = _pendingWidgetMetrics;
                if (latest == null) return;
                UpdateTemperatureFromMetrics(latest);
            }, DispatcherPriority.Normal);
        }
        catch (Exception ex)
        {
            _logger?.LogError($"[WIDGET] Erro ao processar métricas térmicas: {ex.Message}");
        }
    }

    private void UpdateTemperatureFromMetrics(ThermalMetrics metrics)
    {
        try
        {
            if (metrics == null)
            {
                CpuTemperature = null;
                return;
            }

            var temp = metrics.CpuTemperature;
            if (!double.IsNaN(temp) && temp > 0 && temp < 150)
                CpuTemperature = Math.Round(temp, 1);
            else
                CpuTemperature = null;
        }
        catch
        {
            CpuTemperature = null;
        }
    }

    /// <summary>
    /// Inicializa detecção de jogos. FPS Monitor só roda quando há jogo ativo.
    /// Resiliente: se o GameDetectionService ainda não estiver pronto, faz retry.
    /// </summary>
    public void InitializeFPSMonitor()
    {
        if (_gameDetectionHookInstalled)
            return;

        _ = TryInstallGameDetectionHookAsync(attempt: 1);
    }

    private async Task TryInstallGameDetectionHookAsync(int attempt)
    {
        if (_gameDetectionHookInstalled)
            return;

        var detector = App.Services?.GetService<IGameDetector>();

        if (detector == null)
        {
            if (attempt >= 20)
                return;

            await Task.Delay(500);
            await TryInstallGameDetectionHookAsync(attempt + 1);
            return;
        }

        if (!detector.IsMonitoring)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    detector.StartMonitoring();
                }
                catch { }
            });
        }

        detector.GameStarted += OnGameStartedForFps;
        detector.GameStopped += OnGameStoppedForFps;
        _gameDetector = detector;

        _gameDetectionHookInstalled = true;
        _logger?.LogDebug("[FPS] Hook GameDetection instalado");

        HookOrchestrator();

        _ = Task.Run(() =>
        {
            try
            {
                var processes = System.Diagnostics.Process.GetProcesses();
                foreach (var p in processes)
                {
                    try
                    {
                        if (p.Id <= 4 || p.HasExited) continue;
                        if (detector.IsKnownGame(p.ProcessName))
                        {
                            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                            {
                                StartFpsMonitor($"Game {p.ProcessName} already running");
                            });
                            break;
                        }
                    }
                    catch { }
                }
            }
            catch { }
        });
    }

    private bool _gameDetectionHookInstalled = false;
    private int _activeGamePid = 0;
    private IGameDetector? _gameDetector;

    /// <summary>
    /// O monitoramento de FPS é INDEPENDENTE do Modo Gamer automático.
    /// HookOrchestrator foi removido intencionalmente: o FPS é ativado
    /// exclusivamente pelo IGameDetector (detecção de processo), não pelo
    /// toggle de "Ativação automática do Modo Gamer" da página Gamer.
    /// </summary>
    private void HookOrchestrator()
    {
        // No-op intencional: FPS não depende de IGamerModeOrchestrator.
        // A detecção de jogos é feita via IGameDetector em TryInstallGameDetectionHook().
    }

    private void OnGamerStatusChanged(object? sender, VoltrisOptimizer.Services.Gamer.Models.GamerModeStatus st)
    {
    }

    private void OnGameStartedForFps(object? sender, VoltrisOptimizer.Services.Gamer.Models.DetectedGame game)
    {
        _activeGamePid = game.ProcessId;
        _logger?.LogDebug($"[FPS] Gaming detected: {game.Name} (PID={game.ProcessId})");
        // Pular ETW/FPS monitor se o jogo usa anti-cheat (BattlEye, EAC, Vanguard)
        var processName = !string.IsNullOrEmpty(game.ProcessName) ? game.ProcessName
            : !string.IsNullOrEmpty(game.ExecutablePath) ? System.IO.Path.GetFileNameWithoutExtension(game.ExecutablePath)
            : game.Name;
        var antiCheat = VoltrisOptimizer.Services.Gamer.Intelligence.Implementation.AntiCheatCompatibilityService.Instance.GetAntiCheatForGame(processName);
        if (antiCheat is not null)
        {
            _logger?.LogInfo($"[FPS] Anti-cheat '{antiCheat}' detectado para '{game.Name}' — pulando ETW FrameTimeMonitor");
            return;
        }
        StartFpsMonitor("game-started");
    }

    private void OnGameStoppedForFps(object? sender, VoltrisOptimizer.Services.Gamer.Models.DetectedGame game)
    {
        _ = Task.Run(() =>
        {
            try
            {
                var detector = _gameDetector;
                if (detector != null)
                {
                    var processes = System.Diagnostics.Process.GetProcesses();
                    bool gameStillRunning = false;
                    foreach (var p in processes)
                    {
                        try
                        {
                            if (p.Id <= 4 || p.HasExited) continue;
                            if (detector.IsKnownGame(p.ProcessName))
                            {
                                gameStillRunning = true;
                                break;
                            }
                        }
                        catch { }
                    }
                    if (gameStillRunning)
                        return;
                }
            }
            catch { }

            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                StopFpsMonitor("game-stopped");
            });
        });
    }

    private VoltrisOptimizer.Services.Monitoring.Interfaces.IEtwFrameTimeMonitor? _fpsMonitor;

    public void StopFpsMonitorForAntiCheat()
    {
        if (_fpsMonitor != null)
        {
            _logger?.LogInfo("[FPS] Parando ETW FrameTimeMonitor por detecção de anti-cheat");
            StopFpsMonitor("anti-cheat");
        }
    }

    private void StartFpsMonitor(string reason)
    {
        if (_fpsMonitor != null)
        {
            // Já temos referência, apenas reativar visibilidade
            FpsAvailable = true;
            OnPropertyChanged(nameof(FpsVisible));
            _logger?.LogDebug($"[FPS] Monitor já ativo, apenas reexibindo (reason: {reason})");
            return;
        }

        _fpsMonitor = ServiceLocator.GetRequiredService<IEtwFrameTimeMonitor>();
        _fpsMonitor.MetricsUpdated += OnFpsUpdated;
        _ = _fpsMonitor.StartAsync();

        FpsAvailable = true;
        OnPropertyChanged(nameof(FpsVisible));
        _logger?.LogInfo($"[FPS] Monitor iniciado (reason: {reason})");
    }

    private void StopFpsMonitor(string reason)
    {
        if (_fpsMonitor == null)
            return;

        try
        {
            _fpsMonitor.MetricsUpdated -= OnFpsUpdated;
        }
        catch (Exception ex)
        {
            _logger?.LogError($"[FPS] Erro ao parar monitor: {ex.Message}");
        }

        // NÃO zera _fpsMonitor para evitar race condition com GameDetector
        // NÃO zera CurrentFps/OnePercentLowFps para não mostrar 0 na UI
        // Apenas esconde o indicador visual — os valores persistem para transição suave
        FpsAvailable = false;
        OnPropertyChanged(nameof(FpsVisible));
        _logger?.LogInfo($"[FPS] Monitor parado (reason: {reason}). FPS oculto, valores preservados.");
    }

    private void OnFpsUpdated(object? sender, VoltrisOptimizer.Services.Monitoring.Interfaces.FrameMetrics metrics)
    {
        if (metrics != null && metrics.CurrentFps > 0)
        {
            CurrentFps = metrics.CurrentFps;
            OnePercentLowFps = metrics.OnePercentLowFps;
            AverageFrametimeMs = metrics.AverageFrametimeMs;
            IsStuttering = metrics.IsStuttering;
        }
    }

    /// <summary>
    /// Atualiza métricas lendo do SystemMetricsCache centralizado (Single Source of Truth)
    /// </summary>
    private void UpdateMetricsFromCache(object? sender, EventArgs e)
    {
        try
        {
            if (!IsVisible)
                return;

            var cache = VoltrisOptimizer.Core.SystemMetricsCache.Instance;

            CpuUsage = Math.Round(cache.CpuPercent, 0);
            RamUsage = Math.Round(cache.MemoryUsedPercent, 0);
            DiskUsage = Math.Round(cache.DiskUsagePercent, 0);
            GpuUsage = Math.Round(cache.GpuUsagePercent, 0);
            
            // Sincronizar Temperatura da CPU a partir do cache centralizado
            if (!double.IsNaN(cache.CpuTemperature) && cache.CpuTemperature > 0 && cache.CpuTemperature < 150)
            {
                CpuTemperature = Math.Round(cache.CpuTemperature, 1);
            }

            // Sincronizar FPS do cache centralizado (unificado com ETW)
            // CORREÇÃO: Sempre sincronizar FpsAvailable do cache para detectar quando jogo fecha
            FpsAvailable = cache.FpsAvailable;
            
            if (cache.FpsAvailable)
            {
                CurrentFps = cache.Fps;
                OnePercentLowFps = cache.FpsOnePercentLow;
                AverageFrametimeMs = cache.FpsAverageFrametimeMs;
                IsStuttering = cache.FpsIsStuttering;
            }
            else
            {
                // Zerar valores quando FPS não está disponível (jogo fechado)
                CurrentFps = 0;
                OnePercentLowFps = 0;
                AverageFrametimeMs = 0;
                IsStuttering = false;
            }
            
    // A barra mostra o uso de CPU sempre que nenhuma otimização estiver em
    // curso. Não consulta mais IsOperationRunning: uma tarefa pendente no
    // GlobalProgressService não pode mais congelar a barra do widget.
    if (!_progressDrivenByOperation)
    {
        ProgressValue = CpuUsage;
    }

            _lastMetricsUpdate = DateTime.Now;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug($"[WidgetViewModel] Erro ao atualizar métricas: {ex.Message}");
        }
    }

    // MÉTODOS DE CONTROLE DO MENU DE CONTEXTO
    private void ToggleSmartMode()
    {
        SmartMode = !SmartMode;
        _logger?.LogInfo($"[WidgetViewModel] SmartMode alterado para: {SmartMode}");
        SaveSettings();
    }

    private void ToggleStartup()
    {
        StartWithWindows = !StartWithWindows;
        _logger?.LogInfo($"[WidgetViewModel] StartWithWindows alterado para: {StartWithWindows}");
        SaveSettings();
        StartWithWindowsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Carrega configurações do arquivo
    /// </summary>
    private void LoadSettings()
    {
        try
    {
        var config = WidgetConfig.Load();

        SmartMode = config.SmartMode;
        StartWithWindows = config.StartWithWindows;
        _showCpu  = config.WidgetShowCpu;
        _showRam  = config.WidgetShowRam;
        _showDisk = config.WidgetShowDisk;
        _showTemp = config.WidgetShowTemp;
        _showFpsMetric = config.WidgetShowFps;
        _logger?.LogInfo("[WidgetViewModel] Configurações carregadas");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning($"[WidgetViewModel] Erro ao carregar configurações: {ex.Message}");
        }
    }

    /// <summary>
    /// Salva configurações no arquivo
    /// </summary>
    public void SaveSettings()
    {
        try
    {
        var config = WidgetConfig.Load();

        config.SmartMode = SmartMode;
        config.StartWithWindows = StartWithWindows;
        config.WidgetShowCpu  = _showCpu;
        config.WidgetShowRam  = _showRam;
        config.WidgetShowDisk = _showDisk;
        config.WidgetShowTemp = _showTemp;
        config.WidgetShowFps  = _showFpsMetric;
        config.Save();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning($"[WidgetViewModel] Erro ao salvar configurações: {ex.Message}");
        }
    }

    /// <summary>
    /// Inicia o monitoramento - timer de cache + assinatura térmica + FPS
    /// </summary>
    private bool _monitoringStarted = false;
    private readonly object _startLock = new object();

    public void StartMonitoring()
    {
        // GUARDA DE IDEMPOTÊNCIA: evita múltiplas inicializações
        lock (_startLock)
        {
            if (_monitoringStarted)
            {
                _logger?.LogWarning($"[WidgetViewModel] StartMonitoring IGNORADO, já iniciado (thread = {System.Threading.Thread.CurrentThread.ManagedThreadId})");
                return;
            }

            _monitoringStarted = true;

            _logger?.LogDebug("[WidgetViewModel] StartMonitoring");

            // Forçar primeira atualização imediata (leitura inicial do cache já existente)
            UpdateMetricsFromCache(null, EventArgs.Empty);

            // Subscrever ao MetricsUpdated do SystemMetricsCache para atualização em tempo real perfeitamente alinhada
            SystemMetricsCache.Instance.MetricsUpdated += OnGlobalMetricsUpdated;

            // Iniciar assinatura do serviço térmico
            SubscribeThermalEvents();

            // Iniciar monitor de FPS (precisa de DispatcherTimer, então fica na UI thread)
            try
            {
                InitializeFPSMonitor();
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[WidgetViewModel] FPS Monitor falhou: {ex.Message}");
            }

            _logger?.LogSuccess("[WidgetViewModel] Monitoramento iniciado com SUCESSO");
            try { VoltrisDiagnosticSystem.Instance.Timeline("WIDGET", "WidgetViewModel: StartMonitoring concluído com sucesso"); } catch { }
        }
    }

    private void OnGlobalMetricsUpdated(object? sender, EventArgs e)
    {
        try
        {
            // Rate limiting: Update at most every 500ms, even if cache updates faster
            if ((DateTime.Now - _lastMetricsUpdate).TotalMilliseconds < 500)
                return;

            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                UpdateMetricsFromCache(null, EventArgs.Empty);
            }, DispatcherPriority.Background);
        }
        catch { }
    }

    /// <summary>
    /// Para o monitoramento
    /// </summary>
    public void StopMonitoring()
    {
        lock (_startLock)
        {
            if (_monitoringStarted)
            {
                SystemMetricsCache.Instance.MetricsUpdated -= OnGlobalMetricsUpdated;
                UnsubscribeThermalEvents();
                _monitoringStarted = false;
            }
        }
        // FPS monitor cleanup handled in Dispose
    }

    private async Task OptimizeRamAsync()
    {
        if (IsOptimizing) return;

        try
        {
            IsOptimizing = true;
            _logger?.LogInfo("[WIDGET] Solicitando otimização rápida através do Dashboard (Pipeline Unificado)...");

            var dashboardVM = VoltrisOptimizer.UI.ViewModels.ViewModelLocator.Instance.DashboardVM;
            if (dashboardVM != null)
            {
                await dashboardVM.QuickOptimizeAsync("Widget");
            }
            else
            {
                _logger?.LogWarning("[WIDGET] DashboardViewModel não disponível para executar otimização.");
                GlobalNotificationService.ShowError(LocalizationService.Instance.GetString("Error"), LocalizationService.Instance.GetString("OptimizationSystemUnavailable"));
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError($"[WIDGET] Erro ao delegar otimização rápida: {ex.Message}");
            GlobalNotificationService.ShowError(LocalizationService.Instance.GetString("OptimizationError"), LocalizationService.Instance.GetString("QuickOptimizationFailed"));
        }
        finally
        {
            IsOptimizing = false;
            // Libera a barra para o uso de CPU assim que a ação do widget termina.
            _progressDrivenByOperation = false;
            UpdateMetricsFromCache(null, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Libera memória de trabalho de processos não críticos via Body Orchestrator
    /// </summary>
    private async Task ReleaseMemoryImmediatelyAsync()
    {
        try
        {
            var body = App.Body;

            if (body != null)
            {
                var action = new Core.Brain.V2.BrainActionV2
                {
                    Kind = Core.Brain.V2.BrainActionKind.TrimWorkingSet,
                    Reason = "Widget UI Request"
                };

                var decision = new Core.Body.BrainDecision
                {
                    ActionType = Core.Body.DecisionActionType.TrimWorkingSet,
                    Justification = action.Reason
                };

                // TODO: DispatchActionAsync não existe em IVoltrisBody
                // await body.DispatchActionAsync(decision).ConfigureAwait(false);
                _logger?.LogDebug("[Widget] Memory trim desabilitado temporariamente.");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError($"[Widget] Erro ao despachar TrimWorkingSet: {ex.Message}");
        }
    }

    /// <summary>
    /// Formata bytes para exibição legível
    /// </summary>
    private string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;

        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }

        return $"{len:0.#} {sizes[order]}";
    }

    // Eventos para comunicação com a View
    public event EventHandler CloseRequested;
    public event EventHandler OpenDashboardRequested;

    // NOVOS EVENTOS DO MENU DE CONTEXTO

    public event EventHandler SmartModeChanged;
    public event EventHandler StartWithWindowsChanged;

    private string GetLocalizedProfileName(IntelligentProfileType profile)
    {
        string key = profile switch
        {
            IntelligentProfileType.GamerCompetitive => "ProfileGamerCompetitive",
            IntelligentProfileType.GamerSinglePlayer => "ProfileGamerSinglePlayer",
            IntelligentProfileType.GamerSimulation => "ProfileGamerSimulation",
            IntelligentProfileType.GamerMMO => "ProfileGamerMMO",
            IntelligentProfileType.GamerStrategy => "ProfileGamerStrategy",
            IntelligentProfileType.WorkOffice => "ProfileWorkOffice",
            IntelligentProfileType.CreativeVideoEditing => "ProfileCreativeVideoEditing",
            IntelligentProfileType.DeveloperProgramming => "ProfileDeveloperProgramming",
            IntelligentProfileType.EnterpriseSecure => "ProfileEnterpriseSecure",
            IntelligentProfileType.GeneralBalanced => "ProfileGeneralBalanced",
            _ => "ProfileGeneralBalanced"
        };
        
        string rawName = LocalizationService.Instance.GetString(key);
        return CleanEmojis(rawName);
    }

    private string CleanEmojis(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        string[] emojis = { "🎮", "🎯", "🏎️", "🏎", "⚔️", "⚔", "🛡️", "🛡", "💼", "🎬", "💻", "🏢", "⚖️", "⚖" };
        string clean = text;
        foreach (var emoji in emojis)
        {
            clean = clean.Replace(emoji, "");
        }
        return clean.Trim();
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

      public void Dispose()
      {
          StopMonitoring();
          if (_fpsMonitor != null)
          {
              _fpsMonitor.MetricsUpdated -= OnFpsUpdated;
              _fpsMonitor = null;
          }
  
          if (_thermalService != null)
              _thermalService.MetricsUpdated -= OnThermalMetricsUpdated;
  
          if (_gameDetector != null)
          {
              _gameDetector.GameStarted -= OnGameStartedForFps;
              _gameDetector.GameStopped -= OnGameStoppedForFps;
          }

          // Fontes que vivem mais que esta View. Sem estes -=, cada reabertura
          // do widget deixava este ViewModel preso em BrainMetricsCache (static),
          // SettingsService e GlobalProgressService.
          try
          {
              Core.Brain.V2.BrainMetricsCache.Instance.PropertyChanged -= OnBrainMetricsPropertyChanged;
              SettingsService.Instance.ProfileChanged -= OnSettingsProfileChanged;
              GlobalProgressService.Instance.ProgressChanged -= OnGlobalProgressChanged;
              GlobalProgressService.Instance.OperationCompleted -= OnGlobalOperationCompleted;
          }
          catch { }
      }
}

// RelayCommand simples
public class RelayCommand : ICommand
{
    private readonly Action<object> _execute;
    private readonly Predicate<object> _canExecute;

    public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool CanExecute(object parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object parameter) => _execute(parameter);

    public event EventHandler CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}

// AsyncRelayCommand simples
public class AsyncRelayCommand : ICommand
{
    private readonly Func<object, Task> _execute;
    private readonly Predicate<object> _canExecute;
    private bool _isExecuting;

    public AsyncRelayCommand(Func<object, Task> execute, Predicate<object> canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool CanExecute(object parameter) => !_isExecuting && (_canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object parameter)
    {
        if (_isExecuting)
            return;

        _isExecuting = true;
        RaiseCanExecuteChanged();

        try
        {
            await _execute(parameter);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RelayCommand] Erro em Execute: {ex.Message}");
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public event EventHandler CanExecuteChanged;

    private void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
}

