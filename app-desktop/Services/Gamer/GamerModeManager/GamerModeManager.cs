using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Services.Gamer.Interfaces;
using VoltrisOptimizer.Services.Gamer.Optimization;
using VoltrisOptimizer.Services.Power;
using VoltrisOptimizer.Services.Thermal;
using VoltrisOptimizer.Services.Thermal.Models;
using VoltrisOptimizer.Services.Gamer.GamerModeManager.Services;
using VoltrisOptimizer.Services.Performance.CpuTuning.Core.Interfaces;
using VoltrisOptimizer.Services.Performance.CpuTuning.Features;
using VoltrisOptimizer.Utils;

namespace VoltrisOptimizer.Services.Gamer.GamerModeManager
{
    /// <summary>
    /// GamerModeManager - Implementação robusta do modo gamer
    /// 
    /// Funcionalidades:
    /// - Ativação automática ao detectar jogos
    /// - Otimização de plano de energia
    /// - Priorização de processos
    /// - Otimização GPU (NVIDIA/AMD)
    /// - Monitoramento térmico com rollback automático
    /// - Restauração completa após crash
    /// </summary>
    public class GamerModeManager : IGamerModeManager
    {
        #region Fields
        
		private readonly ILoggingService _logger;

		/// <summary>
		/// Ajustes de runtime do Modo Gamer (GPU, NVAPI, rebaixamento de
		/// processos de fundo). Criado sob demanda e mantido entre ativações
		/// para que a desativação consiga restaurar as prioridades originais.
		/// </summary>
		private VoltrisOptimizer.Services.Gamer.GamerRuntimeTweaksService? _runtimeTweaks;
        private readonly IPowerPlanService _powerPlanService;
        private readonly IGpuOptimizationService _gpuService;
        private readonly IThermalMonitorService _thermalMonitor;
        private readonly IGameDetectionService _gameDetection;
        private readonly IProcessOptimizationService _processService;
        private readonly ITimerResolutionService? _timerResolution;

        // [FIX:GAMERMODE-SEM-ENERGIA] O Modo Gamer não tem mais nenhuma
        // dependência de escrita de energia.
        //
        // O campo `_cpuProfile` apontava para o `o servico legado`,
        // um serviço legado que gravava EPP e estados de processador no esquema
        // ATIVO. Ele foi removido: hoje quem decide energia é o Perfil
        // Inteligente (`ProfilePowerCoordinator`), que já conhece o perfil, o
        // tier de hardware e se a máquina está na tomada.
        //
        // O Modo Gamer continua responsável por tudo o que é latência e não
        // compete pelo orçamento do processador: Game Mode, prioridade do
        // processo, timer de 1ms, GPU, rede, memória e serviços do Windows.
        
        // [CPU] CPU TUNING FEATURES (FIVR, PROCHOT, SpeedShift, Advanced MSR)
        public FivrService? FivrService { get; }
        public ProchotService? ProchotService { get; }
        public SpeedShiftService? SpeedShiftService { get; }
        public AdvancedMsrService? AdvancedMsrService { get; }
        
        // [BOOST] NOVOS SERVIÇOS SURREAIS
        private readonly INetworkOptimizerService? _networkOptimizer;
        private readonly IMemoryOptimizerService? _memoryOptimizer;
        private readonly IKernelOptimizerService? _kernelOptimizer;
        private readonly DisplayOptimizerService? _displayOptimizer;
        private readonly AudioOptimizerService? _audioOptimizer;
        private readonly AdvancedCpuSchedulerService? _cpuScheduler;
        private readonly HardwareSafetyIntelligenceService? _safetyService;
        private readonly AdvancedThermalMonitorService? _advancedThermalMonitor;
        
        // [SERVICE] OTIMIZAÇÃO DE SERVIÇOS DO WINDOWS
        private readonly WindowsServiceOptimizer? _serviceOptimizer;
        
        // [TARGET] OVERLAY SERVICE - FPS EM TEMPO REAL
        private readonly VoltrisOptimizer.Services.Gamer.Overlay.Implementation.OverlayService? _overlayService;

        // MMCSS handle para restauração
        private IntPtr _mmcssHandle = IntPtr.Zero;
        private uint _mmcssTaskIndex = 0;
        
        private GamerModeState _state = new();
        private GamerModeConfig _config = new();
        private HardwareMetrics _metrics = new();
        
        private readonly ConcurrentQueue<GamerModeLogEntry> _logEntries = new();
        private readonly object _stateLock = new();
        private readonly SemaphoreSlim _activationLock = new(1, 1);
        private readonly SemaphoreSlim _thermalLock = new(1, 1);
        private CancellationTokenSource? _monitoringCts;
        private Task? _thermalMonitorTask;
        private volatile bool _isDisposed;
        private volatile HardwareMetrics _metricsVolatile;
        
        // [OPÇÃO C] Persistência de sessão gaming - mantém otimizações enquanto processo existir
        private System.Threading.Timer? _gameSessionPersistenceTimer;
        private readonly TimeSpan _sessionPersistenceCheckInterval = TimeSpan.FromSeconds(10);
        private readonly TimeSpan _sessionPersistenceTimeout = TimeSpan.FromMinutes(5);
        private DateTime? _gameLostFocusTime;
        private int _cachedGamePid = 0;
        
        // Paths
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Voltris", "GamerMode", "config.json");
        
        private static readonly string StatePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Voltris", "GamerMode", "state.json");
        
        private const int MAX_LOG_ENTRIES = 1000;
        
        #endregion
        
        #region Properties
        
        public GamerModeState CurrentState
        {
            get { lock (_stateLock) return _state.Clone(); }
        }
        
        public bool IsActive
        {
            get { lock (_stateLock) return _state.IsActive; }
        }
        
        public HardwareMetrics CurrentMetrics => _metricsVolatile;
        
        public GamerModeConfig Config
        {
            get { lock (_stateLock) return _config; }
        }
        
        #endregion
        
        #region Events
        
        public event EventHandler<GamerModeState>? StateChanged;
        public event EventHandler<HardwareMetrics>? MetricsUpdated;
        public event EventHandler<SafetyRollbackEventArgs>? SafetyRollbackTriggered;
        public event EventHandler<GameDetectedEventArgs>? GameDetected;
        public event EventHandler<GamerModeLogEntry>? LogEntry;
        
        #endregion
        
        #region Constructor
        
        public GamerModeManager(ILoggingService logger, 
            IGameDetectionService gameDetectionService,
            IThermalMonitorService thermalMonitor,
            
            ITimerResolutionService? timerResolution = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _gameDetection = gameDetectionService ?? throw new ArgumentNullException(nameof(gameDetectionService));
            _thermalMonitor = thermalMonitor ?? throw new ArgumentNullException(nameof(thermalMonitor));
            _timerResolution = timerResolution ?? Core.ServiceLocator.GetService<ITimerResolutionService>();

            // Inicializar serviços internos
            _powerPlanService = new PowerPlanService(logger);
            _gpuService = new GpuOptimizationService(logger);
            // PERFORMANCE: Não instanciar um novo ThermalMonitorService (caro/WMI).
            // A interface IThermalMonitorService agora é atendida pelo GlobalThermalMonitorService.
            _processService = new ProcessOptimizationService(logger);
            
            // [BOOST] INICIALIZAR SERVIÇOS SURREAIS
            _networkOptimizer = new NetworkOptimizerService(logger);
            _memoryOptimizer = new MemoryOptimizerService(logger);
            _kernelOptimizer = new KernelOptimizerService(logger);
            _displayOptimizer = new DisplayOptimizerService(logger);
            _audioOptimizer = new AudioOptimizerService(logger);
            _cpuScheduler = new AdvancedCpuSchedulerService(logger);
            _safetyService = new HardwareSafetyIntelligenceService(logger);
            _advancedThermalMonitor = new AdvancedThermalMonitorService(logger, _safetyService);
            
            // [SERVICE] INICIALIZAR OTIMIZAÇÃO DE SERVIÇOS DO WINDOWS
            _serviceOptimizer = new WindowsServiceOptimizer(logger);
            
            // [CPU] INICIALIZAR CPU TUNING FEATURES (FIVR, PROCHOT, SpeedShift, Advanced MSR)
            try
            {
                var backend = Core.ServiceLocator.GetService<IHardwareBackend>();
                if (backend != null)
                {
                    FivrService = new FivrService(backend, logger);
                    ProchotService = new ProchotService(backend, logger);
                    SpeedShiftService = new SpeedShiftService(backend, logger);
                    AdvancedMsrService = new AdvancedMsrService(backend, logger);
                }
            }
            catch (Exception ex)
            {
                Log(LogLevel.Warning, $"Falha ao iniciar CPU tuning features: {ex.Message}", "CpuTuning");
            }

            // [TARGET] INICIALIZAR OVERLAY SERVICE COM FPS EM TEMPO REAL
            _overlayService = new VoltrisOptimizer.Services.Gamer.Overlay.Implementation.OverlayService(logger);

            // Configurar eventos do detector de jogos
            _gameDetection.GameStarted += OnGameStarted;
            _gameDetection.GameStopped += OnGameStopped;

            // [OPÇÃO C] Timer de persistência de sessão gaming
            _gameSessionPersistenceTimer = new System.Threading.Timer(
                OnGameSessionPersistenceTick, 
                null, 
                _sessionPersistenceCheckInterval, 
                _sessionPersistenceCheckInterval);

            // Carregar configuração (síncrono para evitar race na ativação)
            LoadConfig();

            // Verificar estado anterior (crash recovery) — síncrono
            RestorePreviousState();

            // Proteção de Trial (Lockdown Seguro)
            VoltrisOptimizer.Services.LicenseManager.Instance.LicenseStatusChanged += OnLicenseStatusChanged;

            Log(LogLevel.Info, "GamerModeManager inicializado com GameDetectionService compartilhado", "Init");
        }
        
        #endregion
        
        #region Activation/Deactivation
        
        public async Task<bool> ActivateAsync(string? gameExecutable = null, CancellationToken ct = default)
        {
            // PRO: modo gamer exige licença paga (Standard/Pro/Enterprise)
            if (!VoltrisOptimizer.Services.License.ProFeatureGuard.IsPaidActive())
            {
                Log(LogLevel.Warning, "[GamerModeManager] Ativação bloqueada: Licença paga necessária.", "License");
                return false;
            }

            if (IsActive)
            {
                Log(LogLevel.Warning, "Modo gamer já está ativo", "Activate");
                return false;
            }
            
            try
            {
                Log(LogLevel.Info, "Iniciando ativação do modo gamer...", "Activate");

                // ── AJUSTES COM EVIDÊNCIA MEDIDA ──────────────────────────
                // Aplicados aqui porque são de alta confiança, reversíveis e
                // não tocam segurança:
                //   • captura em background do Game Bar (consome CPU/GPU
                //     durante a partida sem o usuário pedir)
                //   • Game Mode ligado (impede instalação de driver pelo
                //     Windows Update durante o jogo, reduz contenção de threads)
                //
                // Memory Integrity (HVCI) NÃO é alterada aqui: desligá-la é
                // redução de segurança e exige reiniciar. O serviço apenas
                // detecta e reporta, para o usuário decidir.
                try
                {
                    var tweaks = new VoltrisOptimizer.Services.Gamer.GamerSystemTweaksService(_logger);
                    var cap = tweaks.DisableBackgroundCapture();
                    Log(cap.Applied ? LogLevel.Info : LogLevel.Warning,
                        $"Captura em background: {cap.Detail}", "GamerTweaks");
                    var gm = tweaks.EnsureGameMode();
                    Log(gm.Applied ? LogLevel.Info : LogLevel.Warning,
                        $"Game Mode: {gm.Detail}", "GamerTweaks");
                    Log(LogLevel.Info, tweaks.BuildDiagnosticReport(), "GamerTweaks");
                }
                catch (Exception tweakEx)
                {
                    Log(LogLevel.Warning, $"Falha nos ajustes de modo gamer: {tweakEx.Message}", "GamerTweaks");
                }

                // ── AJUSTES DE RUNTIME (GPU, NVAPI, rebaixamento de fundo) ───
                // Tudo imediato e reversível, sem reinício. O relatório sai
                // inteiro no log para diagnóstico.
                _runtimeTweaks ??= new VoltrisOptimizer.Services.Gamer.GamerRuntimeTweaksService(_logger);
                try
                {
                    string report = await _runtimeTweaks.ApplyOnActivateAsync();
                    Log(LogLevel.Info, report, "GamerRuntime");
                }
                catch (Exception rtEx)
                {
                    Log(LogLevel.Warning, $"Falha nos ajustes de runtime: {rtEx.Message}", "GamerRuntime");
                }
                
                // Salvar estado para crash recovery
                await SaveStateAsync();
                
                // 1. Detectar e salvar plano de energia atual
                if (_config.OptimizePowerPlan)
                {
                    Log(LogLevel.Info, "Configurando plano de energia...", "PowerPlan");
                    var (originalGuid, originalName) = _powerPlanService.GetActivePowerPlan();
                    
                    UpdateState(s =>
                    {
                        s.OriginalPowerPlanGuid = originalGuid;
                    });
                    
                    // 1.0. Inicializar sistema de segurança inteligente
                    if (_safetyService != null)
                    {
                        Log(LogLevel.Info, "Inicializando sistema de segurança inteligente...", "Safety");
                        await _safetyService.InitializeAsync();
                        
                        // Validar hardware antes de aplicar otimizações
                        var safeOptimizations = _safetyService.GetSafeOptimizations();
                        var unsafeOptimizations = _safetyService.GetUnsafeOptimizations();
                        
                        Log(LogLevel.Info, $"[OK] Otimizações seguras: {safeOptimizations.Count}", "Safety");
                        Log(LogLevel.Info, $"[FAIL] Otimizações inseguras: {unsafeOptimizations.Count}", "Safety");
                        
                        // Desativar otimizações inseguras na configuração
                        await DisableUnsafeOptimizationsAsync(unsafeOptimizations);
                    }
                    
                    ct.ThrowIfCancellationRequested();

                    // 1.1. PLANO DE ENERGIA — [FIX:GAMERMODE-DELEGADO-POWER]
                    //
                    // O que este bloco fazia: trocava o plano para "Alto Desempenho"
                    // ou "Ultimate Performance", com a justificativa de que era "a
                    // otimização mais impactante e segura".
                    //
                    // Não era. Nestas duas máquinas, medir a FRIO, mostrou que o
                    // plano "Alto Desempenho" é IDÊNTICO ao "Equilibrado" em todos
                    // os settings de processador (EPP 20, mínimo 5, máximo 100,
                    // boost 2, estacionamento 100) — ou seja, trocar para ele não
                    // dava NENHUM ganho, apenas tirava do ar o plano que o Perfil
                    // Inteligente tinha configurado com cuidado.
                    //
                    // E era pior do que neutro, porque a troca vinha acompanhada de
                    // os serviços legacy gravarem valores destrutivos por cima
                    // (estacionamento 0, núcleo mínimo 100). O usuário relatava
                    // exatamente isso: os GHz CAIAM no "Alto Desempenho".
                    //
                    // AGORA: o Modo Gamer não escolhe plano de energia. O dono
                    // exclusivo é o Perfil Inteligente (`ProfilePowerCoordinator`),
                    // que já considera o jogo, o tier de hardware da máquina e a
                    // fonte de alimentação. Este bloco apenas REGISTRA o estado
                    // para o painel, sem mandar em nada.
                    if (VoltrisOptimizer.Utils.Win32.PowerNativeMethods.TryGetActiveSchemeGuid(out Guid activePlan))
                    {
                        string activeName = VoltrisOptimizer.Utils.Win32.PowerNativeMethods.GetPlanName(activePlan);
                        UpdateState(s =>
                        {
                            s.CurrentPowerPlanGuid = activePlan.ToString();
                            s.CurrentPowerPlanName = activeName;
                        });
                        Log(LogLevel.Info,
                            $"Plano de energia: '{activeName}' (decidido pelo Perfil Inteligente, " +
                            "não pelo Modo Gamer).", "PowerPlan");
                    }
                }
                
                ct.ThrowIfCancellationRequested();
                
                // 1.7. [BOOST] OTIMIZAÇÕES SURREAIS - Network, Memory, Kernel
                if (_config.OptimizeNetwork && _networkOptimizer != null)
                {
                    Log(LogLevel.Info, "Otimizando rede para gaming...", "Network");
                    var networkResult = await _networkOptimizer.OptimizeNetworkAsync();
                    if (networkResult)
                    {
                        Log(LogLevel.Info, $"[OK] Rede otimizada - Latência reduzida!", "Network");
                    }
                }
                
                ct.ThrowIfCancellationRequested();
                
                if (_config.OptimizeMemory && _memoryOptimizer != null)
                {
                    Log(LogLevel.Info, "Otimizando memória avançada...", "Memory");
                    var memoryResult = await _memoryOptimizer.OptimizeMemoryAsync();
                    if (memoryResult)
                    {
                        Log(LogLevel.Info, $"[OK] Memória otimizada - Large pages ativadas!", "Memory");
                    }
                }
                
                ct.ThrowIfCancellationRequested();
                
                if (_config.OptimizeKernel && _kernelOptimizer != null)
                {
                    Log(LogLevel.Info, "Otimizando kernel para gaming...", "Kernel");
                    var kernelResult = await _kernelOptimizer.OptimizeKernelAsync();
                    if (kernelResult)
                    {
                        Log(LogLevel.Info, $"[OK] Kernel otimizado - Sistema em modo gaming!", "Kernel");
                    }
                }
                
                ct.ThrowIfCancellationRequested();
                
                // 1.8. [BOOST] OTIMIZAÇÕES ADICIONAIS DE NÍVEL DEUS
                if (_config.OptimizeDisplay && _displayOptimizer != null)
                {
                    // Verificar se é seguro pelo sistema de segurança
                    if (_safetyService?.IsOptimizationSafe("Display-Optimization") == true)
                    {
                        Log(LogLevel.Info, "Otimizando display para gaming...", "Display");
                        var displayResult = await _displayOptimizer.OptimizeDisplayAsync();
                        if (displayResult)
                        {
                            Log(LogLevel.Info, $"[OK] Display otimizado - Performance visual máxima!", "Display");
                        }
                    }
                    else
                    {
                        Log(LogLevel.Warning, "[FAIL] Display optimization não é segura para este hardware", "Display");
                    }
                }
                
                ct.ThrowIfCancellationRequested();
                
                if (_config.OptimizeAudio && _audioOptimizer != null)
                {
                    // Verificar se é seguro pelo sistema de segurança
                    if (_safetyService?.IsOptimizationSafe("Audio-Optimization") == true)
                    {
                        Log(LogLevel.Info, "Otimizando áudio para gaming...", "Audio");
                        var audioResult = await _audioOptimizer.OptimizeAudioAsync();
                        if (audioResult)
                        {
                            Log(LogLevel.Info, $"[OK] Áudio otimizado - Sem interferência!", "Audio");
                        }
                    }
                    else
                    {
                        Log(LogLevel.Warning, "[FAIL] Audio optimization não é segura para este hardware", "Audio");
                    }
                }
                
                ct.ThrowIfCancellationRequested();
                
                if (_config.OptimizeCpuScheduler && _cpuScheduler != null)
                {
                    // Verificar se é seguro pelo sistema de segurança
                    if (_safetyService?.IsOptimizationSafe("CPU-Advanced-Scheduler") == true)
                    {
                        Log(LogLevel.Info, "Otimizando scheduler avançado...", "CPU-Scheduler");
                        var schedulerResult = await _cpuScheduler.OptimizeCpuSchedulerAsync();
                        if (schedulerResult)
                        {
                            Log(LogLevel.Info, $"[OK] Scheduler avançado otimizado - Performance máxima!", "CPU-Scheduler");
                        }
                    }
                    else
                    {
                        Log(LogLevel.Warning, "[FAIL] CPU Scheduler optimization não é segura para este hardware", "CPU-Scheduler");
                    }
                }
                
                ct.ThrowIfCancellationRequested();
                
                // Iniciar monitoramento térmico avançado
                if (_advancedThermalMonitor != null)
                {
                    Log(LogLevel.Info, "Iniciando monitoramento térmico avançado...", "Thermal");
                    var thermalStarted = await _advancedThermalMonitor.StartMonitoringAsync();
                    if (thermalStarted)
                    {
                        Log(LogLevel.Info, $"[OK] Monitoramento térmico avançado ativo", "Thermal");
                    }
                }
                
                // [TARGET] INICIAR OVERLAY COM FPS EM TEMPO REAL
                if (_overlayService != null && _state.ActiveGameProcessId.HasValue)
                {
                    Log(LogLevel.Info, "[TARGET] Iniciando overlay com FPS em tempo real...", "Overlay");
                    var overlayStarted = await _overlayService.StartAsync(_state.ActiveGameProcessId.Value);
                    if (overlayStarted)
                    {
                        Log(LogLevel.Info, "[OK] Overlay com FPS em tempo real iniciado", "Overlay");
                    }
                    else
                    {
                        Log(LogLevel.Warning, "[WARN] Overlay não pôde ser iniciado (verifique configurações)", "Overlay");
                    }
                }

                // 1.5. Timer Resolution + MMCSS — ativar IMEDIATAMENTE após plano de energia.
                // NtSetTimerResolution(0.5ms) reduz scheduling jitter de 15.6ms para ~0.5ms.
                // MMCSS registra o processo na classe "Games" para prioridade de scheduler garantida.
                // Ambos têm impacto real e mensurável — não condicionais.
                ActivateTimerAndMmcss();

                ct.ThrowIfCancellationRequested();

                // 1.6. Configurações de CPU (min/max state, core parking, boost mode,
                // EPP) — [FIX:GAMERMODE-SEM-ENERGIA] BLOCO REMOVIDO.
                //
                // Este passo gravava esses quatro valores no esquema ATIVO por
                // `powrprof.dll`, através do `o servico legado`. Era
                // a segunda fonte de escrita de energia do Modo Gamer, e ela
                // escrevia por baixo do Perfil Inteligente, que já tinha aplicado
                // a tabela do perfil segundos antes.
                //
                // Valores como EPP 0, boost agressivo e núcleo mínimo 100% num
                // notebook de 15W não dão desempenho: eles impedem o processador
                // de baixar o clock, gastam o orçamento térmico antes da carga
                // chegar e derrubam o clock sustentado. Foi esse o defeito medido
                // nesta máquina.
                //
                // Hoje o Gamer Mode não escreve nada de energia. Quem decide é o
                // Perfil Inteligente, e o que o Gamer Mode ajusta agora é só o que
                // é latência sem custo de orçamento: Game Mode, prioridade do
                // processo, MMCSS, timer de 1ms, GPU, rede e memória.
                Log(LogLevel.Info,
                    "Energia: delegada ao Perfil Inteligente (Gamer Mode não grava mais EPP/plano).",
                    "CpuProfile");

                ct.ThrowIfCancellationRequested();
                if (_config.OptimizeGpu)
                {
                    Log(LogLevel.Info, "Otimizando GPU...", "GPU");
                    var gpuResult = await _gpuService.OptimizeAsync(_config.ForceMaxPerformance, ct);
                    
                    UpdateState(s =>
                    {
                        s.GpuVendor = gpuResult.Vendor;
                        s.GpuName = gpuResult.Name;
                        s.GpuOptimized = gpuResult.Success;
                        s.GpuPowerMode = gpuResult.PowerMode;
                    });
                    
                    if (gpuResult.Success)
                    {
                        Log(LogLevel.Info, $"GPU {gpuResult.Name} otimizada: {gpuResult.PowerMode}", "GPU");
                    }
                    else
                    {
                        Log(LogLevel.Warning, $"Falha ao otimizar GPU: {gpuResult.ErrorMessage}", "GPU");
                    }
                }
                
                ct.ThrowIfCancellationRequested();
                
                // 3. Otimizar processo do jogo
                if (!string.IsNullOrEmpty(gameExecutable))
                {
                    await OptimizeGameProcessAsync(gameExecutable, ct);
                }
                
                ct.ThrowIfCancellationRequested();
                
                // 4. Fechar apps de background
                if (_config.CloseBackgroundApps && _config.AppsToClose.Any())
                {
                    Log(LogLevel.Info, "Fechando aplicativos em segundo plano...", "Process");
                    _processService.CloseProcesses(_config.AppsToClose);
                }
                
                ct.ThrowIfCancellationRequested();
                
                // 5. OTIMIZAR SERVIÇOS DO WINDOWS (Search, Fax, Agendadores, etc.) — CONDICIONAL à flag DisableHeavyServices
                if (_config.DisableHeavyServices && _serviceOptimizer != null)
                {
                    Log(LogLevel.Info, "Otimizando serviços do Windows (Search, Fax, Updates, etc.)...", "Service");
                    try
                    {
                        await _serviceOptimizer.ActivateOptimizationAsync();
                        Log(LogLevel.Info, $"[OK] Serviços otimizados - Redução de interferência do sistema!", "Service");
                    }
                    catch (Exception ex)
                    {
                        Log(LogLevel.Warning, $"Erro ao otimizar serviços: {ex.Message}", "Service");
                    }
                }
                else if (!_config.DisableHeavyServices)
                {
                    Log(LogLevel.Info, "Otimização de serviços Windows DESATIVADA por configuração (DisableHeavyServices=false)", "Service");
                }
                
                // 6. Iniciar monitoramento térmico
                StartThermalMonitoring();
                
                // 6. Marcar como ativo
                UpdateState(s =>
                {
                    s.IsActive = true;
                    s.ActivatedAt = DateTime.Now;
                    s.ActiveGamePath = gameExecutable;
                });
                
                // Salvar estado final
                await SaveStateAsync();
                
                // CORREÇÃO: Enviar notificação Windows quando modo gamer é ativado
                try
                {
                    VoltrisOptimizer.Services.NotificationManager.ShowSuccess(
                        "Modo Gamer Ativado",
                        "Otimizações aplicadas com sucesso! [GAME]"
                    );
                }
                catch (Exception ex)
                {
                    Log(LogLevel.Warning, $"Erro ao enviar notificação: {ex.Message}", "Notification");
                }
                
                Log(LogLevel.Info, "✓ Modo gamer ativado com sucesso!", "Activate");
                return true;
            }
            catch (OperationCanceledException)
            {
                Log(LogLevel.Warning, "Ativação cancelada pelo usuário", "Activate");
                await DeactivateAsync(CancellationToken.None);
                return false;
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, $"Erro ao ativar modo gamer: {ex.Message}", "Activate");
                await DeactivateAsync(CancellationToken.None);
                return false;
            }
        }
        
	public async Task<bool> DeactivateAsync(CancellationToken ct = default)
	{
		try
		{
			Log(LogLevel.Info, "[STOP] DESATIVANDO MODO GAMER - INICIANDO PROCESSO COMPLETO...", "Deactivate");

			// Restaura as prioridades originais dos processos rebaixados.
			// Precisa vir PRIMEIRO, antes das demais etapas, para que o
			// sistema volte ao normal mesmo se o resto da rotina falhar.
			if (_runtimeTweaks != null)
			{
				try
				{
					string rtReport = await _runtimeTweaks.ApplyOnDeactivateAsync();
					Log(LogLevel.Info, rtReport, "GamerRuntime");
				}
				catch (Exception rtEx)
				{
					Log(LogLevel.Warning, $"Falha ao reverter ajustes de runtime: {rtEx.Message}", "GamerRuntime");
				}
			}

                
                // 0. VERIFICAÇÃO CRÍTICA - Evitar desativação múltipla
                if (!IsActive)
                {
                    Log(LogLevel.Warning, "[WARN] Modo gamer já está inativo - ignorando desativação", "Deactivate");
                    return true;
                }
                
                // 1. Parar monitoramento térmico
                Log(LogLevel.Info, "[TEMP] Parando monitoramento térmico...", "Deactivate");
                StopThermalMonitoring();
                
                // [TARGET] PARAR OVERLAY COM FPS EM TEMPO REAL
                if (_overlayService != null)
                {
                    Log(LogLevel.Info, "[TARGET] Parando overlay com FPS em tempo real...", "Deactivate");
                    await _overlayService.StopAsync();
                    Log(LogLevel.Info, "[OK] Overlay com FPS em tempo real parado", "Deactivate");
                }

                // 1.5. Restaurar Timer Resolution e MMCSS
                Log(LogLevel.Info, "⏰ Restaurando Timer Resolution e MMCSS...", "Deactivate");
                DeactivateTimerAndMmcss();
                
                // 1.6. [BOOST] RESTAURAR OTIMIZAÇÕES SURREAIS
                if (_config.OptimizeNetwork && _networkOptimizer != null)
                {
                    Log(LogLevel.Info, "[NET] Restaurando configurações de rede...", "Deactivate");
                    await _networkOptimizer.RestoreNetworkAsync();
                    Log(LogLevel.Info, "[OK] Rede restaurada com sucesso", "Deactivate");
                }
                
                if (_config.OptimizeMemory && _memoryOptimizer != null)
                {
                    Log(LogLevel.Info, "[MEM] Restaurando configurações de memória...", "Deactivate");
                    await _memoryOptimizer.RestoreMemoryAsync();
                    Log(LogLevel.Info, "[OK] Memória restaurada com sucesso", "Deactivate");
                }
                
                if (_config.OptimizeKernel && _kernelOptimizer != null)
                {
                    Log(LogLevel.Info, "[CONFIG] Restaurando configurações do kernel...", "Deactivate");
                    await _kernelOptimizer.RestoreKernelAsync();
                    Log(LogLevel.Info, "[OK] Kernel restaurado com sucesso", "Deactivate");
                }
                
                // 1.7. [BOOST] RESTAURAR OTIMIZAÇÕES ADICIONAIS
                if (_config.OptimizeDisplay && _displayOptimizer != null)
                {
                    Log(LogLevel.Info, "[DISPLAY] Restaurando configurações de display...", "Deactivate");
                    await _displayOptimizer.RestoreDisplayAsync();
                    Log(LogLevel.Info, "[OK] Display restaurado com sucesso", "Deactivate");
                }
                
                if (_config.OptimizeAudio && _audioOptimizer != null)
                {
                    Log(LogLevel.Info, "[AUDIO] Restaurando configurações de áudio...", "Deactivate");
                    await _audioOptimizer.RestoreAudioAsync();
                    Log(LogLevel.Info, "[OK] Áudio restaurado com sucesso", "Deactivate");
                }
                
                if (_config.OptimizeCpuScheduler && _cpuScheduler != null)
                {
                    Log(LogLevel.Info, "[POWER] Restaurando configurações do scheduler...", "Deactivate");
                    await _cpuScheduler.RestoreCpuSchedulerAsync();
                    Log(LogLevel.Info, "[OK] CPU Scheduler restaurado com sucesso", "Deactivate");
                }
                
                // 2. Restaurar prioridade e afinidade do processo
                if (_state.ProcessPrioritySet && _state.ActiveGameProcessId.HasValue)
                {
                    Log(LogLevel.Info, "[TARGET] Restaurando prioridade e afinidade do processo...", "Deactivate");
                    _processService.RestoreProcessPriority(_state.ActiveGameProcessId.Value);
                    _processService.RestoreProcessAffinity(_state.ActiveGameProcessId.Value);
                    Log(LogLevel.Info, "[OK] Prioridade e afinidade do processo restauradas", "Deactivate");
                }
                
                // 3. Restaurar GPU
                if (_state.GpuOptimized)
                {
                    Log(LogLevel.Info, "[GAME] Restaurando configurações da GPU...", "Deactivate");
                    await _gpuService.RestoreAsync(ct);
                    Log(LogLevel.Info, "[OK] GPU restaurada com sucesso", "Deactivate");
                }
                
                // 4. Restaurar serviços do Windows (Search, Fax, etc.) — CONDICIONAL à flag DisableHeavyServices
                if (_config.DisableHeavyServices && _serviceOptimizer != null)
                {
                    Log(LogLevel.Info, "[SERVICE] Restaurando serviços do Windows...", "Deactivate");
                    try
                    {
                        await _serviceOptimizer.DeactivateOptimizationAsync();
                        Log(LogLevel.Info, "[OK] Serviços restaurados com sucesso", "Deactivate");
                    }
                    catch (Exception ex)
                    {
                        Log(LogLevel.Warning, $"Erro ao restaurar serviços: {ex.Message}", "Deactivate");
                    }
                }
                else if (!_config.DisableHeavyServices)
                {
                    Log(LogLevel.Info, "Restauração de serviços Windows ignorada (DisableHeavyServices=false)", "Deactivate");
                }
                
                // 5. Liberar requisição do orquestrador e restaurar plano de energia
                
                if (!string.IsNullOrEmpty(_state.OriginalPowerPlanGuid))
                {
                    Log(LogLevel.Info, "[POWER] Restaurando plano de energia original...", "Deactivate");
                    _powerPlanService.SetPowerPlanByGuid(_state.OriginalPowerPlanGuid);
                    Log(LogLevel.Info, "[OK] Plano de energia restaurado", "Deactivate");
                }
                
                // 6. Resetar estado CRÍTICO
                Log(LogLevel.Info, "[RESET] Resetando estado completo do modo gamer...", "Deactivate");
                UpdateState(s =>
                {
                    s.IsActive = false;
                    s.ActivatedAt = null;
                    s.ActiveGameName = null;
                    s.ActiveGameProcessId = null;
                    s.ActiveGamePath = null;
                    s.GpuOptimized = false;
                    s.ProcessPrioritySet = false;
                    s.ProcessAffinitySet = false;
                    s.ThermalMonitoringActive = false;
                    s.CurrentPowerPlanGuid = null;
                    s.CurrentPowerPlanName = null;
                });
                
                // 6. Limpar arquivo de estado
                Log(LogLevel.Info, "[CLEAN] Limpando arquivo de estado...", "Deactivate");
                DeleteStateFile();
                
                // 7. NOTIFICAÇÃO CRÍTICA - Sucesso na desativação
                try
                {
                    VoltrisOptimizer.Services.NotificationManager.ShowSuccess(
                        "Modo Gamer Desativado",
                        "Todas as otimizações foram restauradas! [OK]"
                    );
                    Log(LogLevel.Info, "[NOTIFY] Notificação de desativação enviada com sucesso", "Deactivate");
                }
                catch (Exception ex)
                {
                    Log(LogLevel.Warning, $"[WARN] Erro ao enviar notificação de desativação: {ex.Message}", "Deactivate");
                }
                
                Log(LogLevel.Info, "[DONE] MODO GAMER DESATIVADO COM 100% DE SUCESSO!", "Deactivate");
                return true;
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, $"Erro ao desativar modo gamer: {ex.Message}", "Deactivate");
                return false;
            }
        }
        
        public async Task ForceEmergencyRollbackAsync()
        {
            Log(LogLevel.Critical, "[WARN] ROLLBACK DE EMERGÊNCIA INICIADO", "Safety");
            
            try
            {
                // Forçar rollback de tudo ignorando erros
                StopThermalMonitoring();
                try { DeactivateTimerAndMmcss(); } catch { }
                
                try { await _gpuService.RestoreAsync(CancellationToken.None); } catch { }
                
                
                if (!string.IsNullOrEmpty(_state.OriginalPowerPlanGuid))
                {
                    try { _powerPlanService.SetPowerPlanByGuid(_state.OriginalPowerPlanGuid); } catch { }
                }
                
                if (_state.ActiveGameProcessId.HasValue)
                {
                    try { _processService.RestoreProcessPriority(_state.ActiveGameProcessId.Value); } catch { }
                }
                
                UpdateState(s =>
                {
                    s.IsActive = false;
                    s.SafetyRollbackOccurred = true;
                });
                
                DeleteStateFile();
                
                Log(LogLevel.Info, "Rollback de emergência concluído", "Safety");
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, $"Erro durante rollback de emergência: {ex.Message}", "Safety");
            }
        }
        
        #endregion
        
        #region Timer Resolution + MMCSS

        // AUDITORIA: MMCSS P/Invoke removido — AvSetMmThreadCharacteristics só afeta a
        // thread chamadora, não o processo alvo. Causava contenção direta com o jogo.

        private void ActivateTimerAndMmcss()
        {
            // 1. NtSetTimerResolution → 0.5ms
            if (_timerResolution != null)
            {
                try
                {
                    bool ok = _timerResolution.SetMaximumResolution();
                    var (current, _) = _timerResolution.GetResolutionInfo();
                    Log(LogLevel.Info, ok
                        ? $"✓ Timer Resolution: {current:F2}ms (era ~15.6ms)"
                        : "Timer Resolution: fallback aplicado", "TimerRes");
                }
                catch (Exception ex)
                {
                    Log(LogLevel.Warning, $"Timer Resolution falhou: {ex.Message}", "TimerRes");
                }
            }

            // 2. MMCSS — registrar na classe "Games"
            // REMOVIDO PELA AUDITORIA TÉCNICA: O Voltris estava elevando a si mesmo e competindo
            // com o jogo por Cache L3 e ciclos de CPU.
            
            try
            {
                var curProc = System.Diagnostics.Process.GetCurrentProcess();
                Log(LogLevel.Info, $"[AUDITORIA MMCSS] Process Priority Atual: {curProc.PriorityClass}", "MMCSS");
                Log(LogLevel.Info, $"[AUDITORIA MMCSS] Total Threads: {curProc.Threads.Count}", "MMCSS");
                
                // Aplicar a nova filosofia: Otimizador em background com menos impacto.
                curProc.PriorityClass = System.Diagnostics.ProcessPriorityClass.BelowNormal;
                Log(LogLevel.Info, $"[AUDITORIA MMCSS] Prioridade ajustada para BelowNormal. (Fase 1 EcoQoS fallback)", "MMCSS");
            }
            catch(Exception ex)
            {
                Log(LogLevel.Warning, $"Falha ao ajustar prioridade do Voltris: {ex.Message}", "MMCSS");
            }
        }

        private void DeactivateTimerAndMmcss()
        {
            // Restaurar Timer Resolution
            if (_timerResolution != null)
            {
                try
                {
                    _timerResolution.ReleaseResolution();
                    Log(LogLevel.Info, "Timer Resolution restaurado ao padrão", "TimerRes");
                }
                catch { }
            }

            // AUDITORIA: MMCSS revert removido — AvSetMmThreadCharacteristics não é mais chamado
        }

        #endregion

        #region Game Detection
        
        public void StartGameDetection()
        {
            if (_state.GameDetectionActive)
                return;
            
            _gameDetection.Start(_config.WatchedGames.Select(g => g.ProcessName).ToList(), 
                                 _config.GameDetectionIntervalMs);
            
            UpdateState(s => s.GameDetectionActive = true);
            Log(LogLevel.Info, "Detecção de jogos iniciada", "GameDetection");
        }
        
        public void StopGameDetection()
        {
            _gameDetection.Stop();
            UpdateState(s => s.GameDetectionActive = false);
            Log(LogLevel.Info, "Detecção de jogos parada", "GameDetection");
        }
        
        private void OnGameStarted(object? sender, GameDetectedEventArgs e)
        {
            Log(LogLevel.Info, $"[GAME] Jogo detectado: {e.GameName}", "GameDetection");
            GameDetected?.Invoke(this, e);

            // [OPÇÃO C] Cachear PID do jogo para monitoramento de persistência
            try
            {
                var process = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(e.GameName))
                    .FirstOrDefault();
                if (process != null)
                {
                    _cachedGamePid = process.Id;
                    _gameLostFocusTime = null; // Jogo está em foreground
                    Log(LogLevel.Debug, $"[OPÇÃO C] PID do jogo cacheado: {_cachedGamePid}", "GameDetection");
                }
            }
            catch (Exception ex)
            {
                Log(LogLevel.Warning, $"Não foi possível cachear PID do jogo: {ex.Message}", "GameDetection");
            }

            if (_config.AutoActivateOnGameStart && !IsActive)
            {
                _ = SafeActivateAsync(e.ExecutablePath);
            }
        }

        private async Task SafeActivateAsync(string? executablePath)
        {
            try
            {
                await ActivateAsync(executablePath);
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, $"Erro ao ativar modo gamer via handler: {ex.Message}", "GameDetection");
            }
        }

        private void OnGameStopped(object? sender, GameDetectedEventArgs e)
        {
            Log(LogLevel.Info, $"[GAME] Jogo encerrado: {e.GameName}", "GameDetection");
            GameDetected?.Invoke(this, e);

            var game = _config.WatchedGames.FirstOrDefault(g =>
                g.ProcessName.Equals(e.GameName, StringComparison.OrdinalIgnoreCase));

            if (game != null)
            {
                game.LastPlayed = DateTime.Now;
                if (_state.ActivatedAt.HasValue)
                {
                    game.TotalPlayTime += DateTime.Now - _state.ActivatedAt.Value;
                }
                SaveConfig();
            }

            // [OPÇÃO C] NÃO desativar imediatamente!
            // O timer de persistência verificará se o processo realmente fechou
            // Isso previne desativação acidental em atualizações ou restarts do jogo
            
            // ⚠️ FIX: Marcar que o jogo perdeu foco para o timer de persistência
            // Este timestamp é usado pelo timer para determinar se foi ALT+TAB recente
            if (!_gameLostFocusTime.HasValue)
            {
                _gameLostFocusTime = DateTime.Now;
                Log(LogLevel.Info, "[OPÇÃO C] OnGameStopped: _gameLostFocusTime definido. Aguardando verificação de persistência...", "GameDetection");
            }
            else
            {
                Log(LogLevel.Debug, "[OPÇÃO C] OnGameStopped: _gameLostFocusTime já estava definido (ALT+TAB anterior)", "GameDetection");
            }
        }

        private async Task SafeDeactivateAsync()
        {
            try
            {
                await DeactivateAsync();
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, $"Erro ao desativar modo gamer via handler: {ex.Message}", "GameDetection");
            }
        }

        /// <summary>
        /// [OPÇÃO C] Timer de persistência de sessão gaming
        /// Verifica a cada 10s se o processo do jogo ainda existe.
        /// Mantém otimizações enquanto:
        /// - Processo estiver aberto, OU
        /// - ALT+TAB < 5 minutos
        /// Só desativa quando processo realmente fecha.
        /// </summary>
        private async void OnGameSessionPersistenceTick(object? state)
        {
            if (!IsActive || _isDisposed)
                return;

            try
            {
                // Verifica se processo do jogo ainda existe
                bool processoExiste = false;
                
                if (_cachedGamePid > 0)
                {
                    try
                    {
                        using (var proc = Process.GetProcessById(_cachedGamePid))
                        {
                            processoExiste = !proc.HasExited;
                        }
                    }
                    catch
                    {
                        // Processo não existe mais
                        processoExiste = false;
                    }
                }

                // Se não tem PID cacheado, tenta detectar jogo ativo
                if (!processoExiste && _state.ActiveGameProcessId.HasValue)
                {
                    _cachedGamePid = _state.ActiveGameProcessId.Value;
                    try
                    {
                        using (var proc = Process.GetProcessById(_cachedGamePid))
                        {
                            processoExiste = !proc.HasExited;
                        }
                    }
                    catch
                    {
                        processoExiste = false;
                    }
                }

                if (processoExiste)
                {
                    // Jogo ainda está aberto → Detectar ALT+TAB
                    var foregroundPid = GetForegroundWindowProcessId();
                    bool jogoEmForeground = (foregroundPid == _cachedGamePid);
                    
                    if (!jogoEmForeground)
                    {
                        // Jogo está em background (ALT+TAB)
                        if (!_gameLostFocusTime.HasValue)
                        {
                            _gameLostFocusTime = DateTime.Now;
                            Log(LogLevel.Info, $"[OPÇÃO C] ALT+TAB detectado (PID={_cachedGamePid}) - Mantendo otimizações por 5min", "Persistence");
                        }
                        
                        var tempoFora = DateTime.Now - _gameLostFocusTime.Value;
                        if (tempoFora < _sessionPersistenceTimeout)
                        {
                            Log(LogLevel.Debug, $"[OPÇÃO C] Jogo em background ({tempoFora.TotalSeconds:F0}s) - Mantendo otimizações", "Persistence");
                        }
                        else
                        {
                            Log(LogLevel.Info, $"[OPÇÃO C] Jogo em background >5min - Ainda mantendo otimizações (processo existe)", "Persistence");
                        }
                    }
                    else
                    {
                        // Jogo voltou para foreground
                        if (_gameLostFocusTime.HasValue)
                        {
                            var duracaoAltTab = DateTime.Now - _gameLostFocusTime.Value;
                            Log(LogLevel.Info, $"[OPÇÃO C] Jogo retornou do ALT+TAB após {duracaoAltTab.TotalSeconds:F0}s", "Persistence");
                        }
                        _gameLostFocusTime = null;
                    }
                }
                else
                {
                    // Processo fechou → Verifica se foi ALT+TAB recente
                    if (_gameLostFocusTime.HasValue)
                    {
                        var tempoFora = DateTime.Now - _gameLostFocusTime.Value;
                        
                        if (tempoFora < _sessionPersistenceTimeout)
                        {
                            Log(LogLevel.Info, $"[OPÇÃO C] Processo fechou, mas ALT+Tab <5min ({tempoFora.TotalSeconds:F0}s) - Aguardando...", "Persistence");
                            return; // Não desativa ainda
                        }
                    }

                    // Processo fechou e não é ALT+Tab recente → DESATIVAR
                    Log(LogLevel.Info, "[OPÇÃO C] Jogo realmente fechou - Desativando modo gamer...", "Persistence");
                    await SafeDeactivateAsync();
                }
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, $"Erro no timer de persistência: {ex.Message}", "Persistence");
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private static int GetForegroundWindowProcessId()
        {
            try
            {
                IntPtr fgWindow = GetForegroundWindow();
                if (fgWindow != IntPtr.Zero)
                {
                    GetWindowThreadProcessId(fgWindow, out uint pid);
                    return (int)pid;
                }
            }
            catch { }
            return 0;
        }
        
        public void AddGameToWatch(string executablePath, GameProfile? profile = null)
        {
            var name = Path.GetFileNameWithoutExtension(executablePath);
            
            var existing = _config.WatchedGames.FirstOrDefault(g => 
                g.ExecutablePath.Equals(executablePath, StringComparison.OrdinalIgnoreCase));
            
            if (existing != null)
            {
                existing.CustomProfile = profile;
            }
            else
            {
                _config.WatchedGames.Add(new WatchedGame
                {
                    Name = name,
                    ExecutablePath = executablePath,
                    ProcessName = name,
                    Enabled = true,
                    CustomProfile = profile
                });
            }
            
            Log(LogLevel.Info, $"Jogo adicionado: {name}", "Config");
            SaveConfig();
        }
        
        public void RemoveGameFromWatch(string executablePath)
        {
            var game = _config.WatchedGames.FirstOrDefault(g => 
                g.ExecutablePath.Equals(executablePath, StringComparison.OrdinalIgnoreCase));
            
            if (game != null)
            {
                _config.WatchedGames.Remove(game);
                Log(LogLevel.Info, $"Jogo removido: {game.Name}", "Config");
                SaveConfig();
            }
        }
        
        public IReadOnlyList<WatchedGame> GetWatchedGames() => _config.WatchedGames.AsReadOnly();
        
        #endregion
        
        #region Thermal Monitoring
        
        private void StartThermalMonitoring()
        {
            if (_state.ThermalMonitoringActive)
                return;
            
            _monitoringCts = new CancellationTokenSource();
            _thermalMonitorTask = Task.Run(() => ThermalMonitorLoop(_monitoringCts.Token));
            
            UpdateState(s => s.ThermalMonitoringActive = true);
            Log(LogLevel.Info, "Monitoramento térmico iniciado (intervalo: 2000ms - otimizado)", "Thermal");
        }
        
        private void StopThermalMonitoring()
        {
            if (!_state.ThermalMonitoringActive)
                return;
            
            _monitoringCts?.Cancel();
            
            try
            {
                _thermalMonitorTask = null;
            }
            catch { }
            
            _monitoringCts?.Dispose();
            _monitoringCts = null;
            _thermalMonitorTask = null;
            
            UpdateState(s => s.ThermalMonitoringActive = false);
            Log(LogLevel.Info, "Monitoramento térmico parado", "Thermal");
        }
        
        private async Task ThermalMonitorLoop(CancellationToken ct)
        {
            int consecutiveCpuOverheat = 0;
            int consecutiveGpuOverheat = 0;
            
            // OTIMIZAÇÃO CRÍTICA: Intervalo aumentado para 2000ms para reduzir CPU wakeups
            // Isso elimina micro-stutters causados por polling frequente
            const int ThermalCheckIntervalMs = 2000;
            
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // Coletar métricas do serviço global (JÁ CACHEADO - não causa WMI blocking)
                    var thermal = await _thermalMonitor.GetCurrentMetricsAsync().ConfigureAwait(false);
                    
                    // Mapear para o modelo interno HardwareMetrics para compatibilidade com o resto do GamerModeManager
                    var metrics = new HardwareMetrics 
                    {
                        CpuTemperature = thermal.CpuTemperature,
                        GpuTemperature = thermal.GpuTemperature,
                        CpuUsage = thermal.CpuUsage,
                        GpuUsage = thermal.GpuUsage,
                        RamUsagePercent = thermal.RamUsagePercent,
                        GpuVramUsed = thermal.GpuVramUsed,
                        GpuVramTotal = thermal.GpuVramTotal,
                        CpuThrottling = thermal.CpuThrottling,
                        GpuThrottling = thermal.GpuThrottling,
                        Timestamp = thermal.Timestamp
                    };

                    _metricsVolatile = metrics;
                    MetricsUpdated?.Invoke(this, metrics);
                    
                    // Verificar CPU
                    if (metrics.CpuTemperature >= _config.CpuMaxTemp)
                    {
                        consecutiveCpuOverheat++;
                        Log(LogLevel.Warning, $"[WARN] CPU quente: {metrics.CpuTemperature}°C (limite: {_config.CpuMaxTemp}°C)", "Thermal");
                        
                        if (consecutiveCpuOverheat >= 2 && _config.AutoRollbackOnOverheat) // 2 segundos (2 x 1000ms)
                        {
                            await TriggerSafetyRollback(SafetyTrigger.CpuOverheat, metrics.CpuTemperature, 
                                $"CPU ultrapassou {_config.CpuMaxTemp}°C");
                            break;
                        }
                    }
                    else
                    {
                        consecutiveCpuOverheat = 0;
                    }
                    
                    // Verificar GPU
                    if (metrics.GpuTemperature >= _config.GpuMaxTemp)
                    {
                        consecutiveGpuOverheat++;
                        Log(LogLevel.Warning, $"[WARN] GPU quente: {metrics.GpuTemperature}°C (limite: {_config.GpuMaxTemp}°C)", "Thermal");
                        
                        if (consecutiveGpuOverheat >= 2 && _config.AutoRollbackOnOverheat)
                        {
                            await TriggerSafetyRollback(SafetyTrigger.GpuOverheat, metrics.GpuTemperature,
                                $"GPU ultrapassou {_config.GpuMaxTemp}°C");
                            break;
                        }
                    }
                    else
                    {
                        consecutiveGpuOverheat = 0;
                    }
                    
                    // Verificar throttling
                    if (metrics.CpuThrottling)
                    {
                        Log(LogLevel.Warning, "CPU em thermal throttling detectado", "Thermal");
                    }
                    
                    if (metrics.GpuThrottling)
                    {
                        Log(LogLevel.Warning, "GPU em thermal throttling detectado", "Thermal");
                    }
                    
                    // OTIMIZAÇÃO: Intervalo aumentado de 500ms para 2000ms
                    // Reduz CPU wakeups em 75% durante gameplay
                    await Task.Delay(ThermalCheckIntervalMs, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log(LogLevel.Error, $"Erro no monitoramento térmico: {ex.Message}", "Thermal");
                    await Task.Delay(1000, ct).ConfigureAwait(false);
                }
            }
        }
        
        private async Task TriggerSafetyRollback(SafetyTrigger trigger, double? temperature, string reason)
        {
            Log(LogLevel.Critical, $"[STOP] ROLLBACK DE SEGURANÇA: {reason}", "Safety");
            
            UpdateState(s =>
            {
                s.SafetyRollbackOccurred = true;
                s.LastSafetyReason = reason;
            });
            
            var args = new SafetyRollbackEventArgs
            {
                Trigger = trigger,
                Temperature = temperature,
                Reason = reason
            };
            
            SafetyRollbackTriggered?.Invoke(this, args);
            
            await DeactivateAsync(CancellationToken.None);
        }
        
        #endregion
        
        #region Process Optimization
        
        private async Task OptimizeGameProcessAsync(string gameExecutable, CancellationToken ct)
        {
            var processName = Path.GetFileNameWithoutExtension(gameExecutable);
            var profile = _config.WatchedGames
                .FirstOrDefault(g => g.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase))
                ?.CustomProfile;
            
            // Aguardar processo iniciar — usando WaitForInputIdle para minimizar polling
            Process? gameProcess = null;
            for (int i = 0; i < 30 && gameProcess == null; i++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var processes = Process.GetProcessesByName(processName);
                    if (processes.Length > 0)
                    {
                        gameProcess = processes[0];
                        // Verificar se o processo está pronto
                        if (!gameProcess.Responding)
                        {
                            try { gameProcess.WaitForInputIdle(100); } catch { }
                        }
                    }
                }
                catch
                {
                    // Processo pode terminar entre GetProcessesByName e acesso
                }
                
                if (gameProcess == null)
                {
                    await Task.Delay(200, ct).ConfigureAwait(false);
                }
            }
            
            if (gameProcess == null)
            {
                Log(LogLevel.Warning, $"Processo {processName} não encontrado", "Process");
                return;
            }
            
            UpdateState(s =>
            {
                s.ActiveGameName = processName;
                s.ActiveGameProcessId = gameProcess.Id;
            });
            
            // SALVAR AFINIDADE ORIGINAL ANTES DE ALTERAR (para restore atômico)
            // [FIX:C-3] SafeProcess + registro em vez de catch vazio.
            // O catch { } anterior escondia a diferenca entre "o jogo encerrou"
            // (normal) e "nao consegui ler afinidade" (problema real que impede
            // o restore atomico depois).
            var jogoVivo = SafeProcess.WithProcess(gameProcess.Id, procForAffinity =>
            {
                _state.OriginalProcessAffinity = procForAffinity.ProcessorAffinity.ToInt32();
                _state.OriginalProcessPriority = (int)procForAffinity.PriorityClass;
            });
            if (!jogoVivo)
            {
                _logger?.LogDebug(
                    $"[GamerMode] Jogo PID {gameProcess.Id} encerrou antes de salvar afinidade/prioridade original. " +
                    $"O rollback usara os valores salvos anteriormente, se houver.");
            }
            
            // Definir prioridade
            if (_config.SetHighPriority)
            {
                // Delegado ao VoltrisBody via SystemGoal.MaximizeFps
                Log(LogLevel.Info, "Definição de prioridade delegada ao VoltrisBody", "Process");
            }
            
            // Definir afinidade
            if (_config.OptimizeAffinity)
            {
                if (profile?.SpecificCores != null)
                {
                    _processService.SetProcessAffinity(gameProcess.Id, profile.SpecificCores);
                }
                else if (profile?.UseAllCores ?? true)
                {
                    _processService.SetProcessAffinityAllCores(gameProcess.Id);
                }
                UpdateState(s => s.ProcessAffinitySet = true);
                Log(LogLevel.Info, "Afinidade do processo configurada", "Process");
            }
            
            // AUDITORIA FORENSE (CRÍTICO): MMCSS via AvSetMmThreadCharacteristics REMOVIDO.
            // Esta API só afeta a THREAD CHAMADORA, não o processo alvo.
            // O código anterior aplicava MMCSS na thread do Voltris pensando que afetava o jogo,
            // fazendo com que o otimizador COMPETISSE com o jogo por fatias de CPU.
            // Isso é a CAUSA RAIZ do stutter e piora de FPS com Modo Gamer ativado.
        }
        
        #endregion
        
        #region Configuration
        
        private void LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    _config = JsonSerializer.Deserialize<GamerModeConfig>(json, new JsonSerializerOptions { ReferenceHandler = ReferenceHandler.IgnoreCycles }) ?? new GamerModeConfig();
                    Log(LogLevel.Info, "Configuração carregada", "Config");
                }
            }
            catch (Exception ex)
            {
                Log(LogLevel.Warning, $"Erro ao carregar configuração: {ex.Message}", "Config");
                _config = new GamerModeConfig();
            }
        }

        public async Task LoadConfigAsync()
        {
            LoadConfig();
            await Task.CompletedTask;
        }
        
        private void SaveConfig()
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                
                var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true, ReferenceHandler = ReferenceHandler.IgnoreCycles });
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                Log(LogLevel.Warning, $"Erro ao salvar configuração: {ex.Message}", "Config");
            }
        }

        public async Task SaveConfigAsync()
        {
            SaveConfig();
            await Task.CompletedTask;
        }
        
        private async Task SaveStateAsync()
        {
            try
            {
                var dir = Path.GetDirectoryName(StatePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                // Capturar estado COMPLETO antes de qualquer mutação
                await CaptureFullStateAsync();

                var json = JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true, ReferenceHandler = ReferenceHandler.IgnoreCycles });
                await File.WriteAllTextAsync(StatePath, json);
                Log(LogLevel.Debug, $"Estado completo salvo em {StatePath}", "State");
            }
            catch (Exception ex)
            {
                Log(LogLevel.Warning, $"Erro ao salvar estado: {ex.Message}", "State");
            }
        }

        /// <summary>
        /// Captura snapshot COMPLETO do sistema antes de aplicar otimizações.
        /// Inclui: power plan, GPU registry, processo, serviços, kernel, rede, pagefile, audio, display, timer, BH PROCHOT.
        /// </summary>
        private async Task CaptureFullStateAsync()
        {
            try
            {
                // Power Plan original
                if (string.IsNullOrEmpty(_state.OriginalPowerPlanGuid))
                {
                    var (origGuid, _) = _powerPlanService.GetActivePowerPlan();
                    UpdateState(s => s.OriginalPowerPlanGuid = origGuid);
                }

                // GPU Registry backup
                await BackupGpuRegistryAsync();

                // Processo do jogo (prioridade/afinidade original)
                if (_state.ActiveGameProcessId.HasValue)
                {
                    await BackupGameProcessStateAsync(_state.ActiveGameProcessId.Value);
                }

                // Serviços Windows
                await BackupWindowsServicesAsync();

                // Kernel Registry
                await BackupKernelRegistryAsync();

                // Network Registry
                await BackupNetworkRegistryAsync();

                // Pagefile
                await BackupPagefileAsync();

                // Audio/Display
                await BackupAudioDisplayAsync();

                // Timer Resolution
                _state.TimerResolutionActive = _timerResolution?.GetResolutionInfo().current < 1.0;

                // BH PROCHOT
                if (ProchotService != null)
                {
                    try { _state.OriginalBdProchotEnabled = false; } catch { }
                }

                Log(LogLevel.Info, "Snapshot completo do sistema capturado para rollback", "State");
            }
            catch (Exception ex)
            {
                Log(LogLevel.Warning, $"Erro ao capturar estado completo: {ex.Message}", "State");
            }
        }

        private async Task BackupGpuRegistryAsync()
        {
            try
            {
                using var hklm = Registry.LocalMachine;
                
                // HAGS / GameDVR / FSO
                using var gameKey = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
                if (gameKey != null)
                {
                    _state.OriginalHwSchMode = gameKey.GetValue("HwSchMode") as int?;
                    _state.OriginalGameDVREnabled = gameKey.GetValue("GameDVR_Enabled") as int?;
                    _state.OriginalFsoBehaviorMode = gameKey.GetValue("HonorUserFSEBehaviorMode") as int?;
                    _state.OriginalDxgiHonorFse = gameKey.GetValue("DXGIHonorFSEWindowsCompatible") as int?;
                }

                // NVIDIA
                using var nvKey = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000");
                if (nvKey != null)
                {
                    _state.OriginalNvPowerMizerEnable = nvKey.GetValue("PowerMizerEnable") as int?;
                    _state.OriginalNvPowerMizerLevel = nvKey.GetValue("PowerMizerLevel") as int?;
                    _state.OriginalNvPerfLevelSrc = nvKey.GetValue("PerfLevelSrc") as int?;
                    _state.OriginalNvPowerMizerLevelAC = nvKey.GetValue("PowerMizerLevelAC") as int?;
                    _state.OriginalNvPowerMizerLevelDC = nvKey.GetValue("PowerMizerLevelDC") as int?;
                }

                // AMD
                using var amdKey = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0001");
                if (amdKey != null)
                {
                    _state.OriginalAmdPowerXpress = amdKey.GetValue("PowerXpress") as int?;
                    _state.OriginalAmdGlobalPerformance = amdKey.GetValue("GlobalPerformance") as int?;
                    _state.OriginalAmdPpTable = amdKey.GetValue("PP_PhmSoftPowerPlayTable") as string;
                }
            }
            catch (Exception ex)
            {
                Log(LogLevel.Warning, $"Backup GPU registry falhou: {ex.Message}", "State");
            }
        }

        private async Task BackupGameProcessStateAsync(int pid)
        {
            // [FIX:C-3] Backup do estado original do processo. O catch { } vazio
            // anterior escondia a falha de leitura; agora ela aparece.
            if (!SafeProcess.WithProcess(pid, proc =>
                {
                    _state.OriginalProcessPriority = (int)proc.PriorityClass;
                    _state.OriginalProcessAffinity = proc.ProcessorAffinity.ToInt32();
                }))
            {
                _logger?.LogDebug(
                    $"[GamerMode] PID {pid} encerrou antes do backup de prioridade/afinidade original.");
            }
        }

        private async Task BackupWindowsServicesAsync()
        {
            try
            {
                var servicesToBackup = new[]
                {
                    "SysMain", "wuauserv", "WSearch", "Bits", "DiagTrack", "WerSvc",
                    "PrintSpooler", "SharedAccess", "Fax", "CDPUserSvc", "OneSyncSvc",
                    "PimIndexMaintenanceSvc", "UnistoreSvc", "UserDataSvc", "WalletService"
                };

                foreach (var name in servicesToBackup)
                {
                    try
                    {
                        using var sc = new ServiceController(name);
                        _state.ServiceStates.Add(new ServiceStateInfo
                        {
                            ServiceName = name,
                            DisplayName = sc.DisplayName,
                            OriginalStartType = (int)sc.StartType,
                            OriginalStatus = sc.Status.ToString()
                        });
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Log(LogLevel.Warning, $"Backup serviços falhou: {ex.Message}", "State");
            }
        }

        private async Task BackupKernelRegistryAsync()
        {
            try
            {
                using var hklm = Registry.LocalMachine;
                using var key = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management");
                if (key != null)
                {
                    _state.OriginalDisablePagingExecutive = key.GetValue("DisablePagingExecutive") as int?;
                    _state.OriginalEnableCompression = key.GetValue("EnableCompression") as int?;
                    _state.OriginalLargePageMinimum = key.GetValue("LargePageMinimum") as int?;
                    _state.OriginalSystemPages = key.GetValue("SystemPages") as int?;
                }

                using var key2 = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\PriorityControl");
                if (key2 != null)
                {
                    _state.OriginalWin32PrioritySeparation = key2.GetValue("Win32PrioritySeparation") as int?;
                }

                // [FIX:GUID-MALFORMADO] Este era o OUTRO lugar com o GUID
                // malformado de core parking.
                //
                // O GUID usado aqui tem "dec355318583" onde o correto é
                // "dec35c318583". A chave não existe, então `key3` sempre
                // chegava null e `OriginalCoreParkingValue` nunca era
                // capturado — o backup nunca existiu.
                //
                // A diferença entre os dois lados importa: aqui a leitura
                // inofensiva e inútil; no outro lado, a escrita. Corrigir apenas
                // a escrita (o que a refatoração fez, neutralizando-a) deixaria
                // aqui um GUID morto que ainda PARECE correto para quem lê.
                // Por isso o valor malformado sai dos dois lados, e o correto
                // fica nomeado — uma constante em vez de uma string repetida é
                // o que impede a volta do erro.
                //
                // A leitura de core parking no registro global deixa de fazer
                // sentido: nenhuma porta de energia grava mais nessa chave, e
                // o dono da energia (Perfil Inteligente) trabalha no plano, não
                // no registro.
                _state.OriginalCoreParkingValue = null;

                // QuantumBoost
                using var key4 = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\kernel");
                if (key4 != null)
                {
                    _state.OriginalQuantumBoost = key4.GetValue("QuantumBoost") as int?;
                }

                // IO Priority, IRQ, CPU Latency
                using var key5 = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\kernel");
                if (key5 != null)
                {
                    _state.OriginalIoPriority = key5.GetValue("IoPriority") as int?;
                }

                using var key6 = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\IRQ8");
                if (key6 != null) _state.OriginalIrq8Priority = key6.GetValue("Priority") as int?;

                using var key7 = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\IRQ16");
                if (key7 != null) _state.OriginalIrq16Priority = key7.GetValue("Priority") as int?;

                using var key8 = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\PriorityControl");
                if (key8 != null) _state.OriginalIrqPriority = key8.GetValue("IRQPriority") as int?;

                using var key9 = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\kernel");
                if (key9 != null) _state.OriginalCpuLatency = key9.GetValue("CpuLatency") as int?;

                using var key10 = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management");
                if (key10 != null)
                {
                    _state.OriginalNumaAllocationPolicy = key10.GetValue("NumaAllocationPolicy") as int?;
                }
            }
            catch (Exception ex)
            {
                Log(LogLevel.Warning, $"Backup kernel registry falhou: {ex.Message}", "State");
            }
        }

        private async Task BackupNetworkRegistryAsync()
        {
            try
            {
                using var hklm = Registry.LocalMachine;
                using var key = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters");
                if (key != null)
                {
                    _state.OriginalNetworkThrottlingIndex = key.GetValue("NetworkThrottlingIndex") as int?;
                    _state.OriginalSystemResponsiveness = key.GetValue("SystemResponsiveness") as int?;
                    _state.OriginalTcpAckFrequency = key.GetValue("TcpAckFrequency") as int?;
                }
            }
            catch (Exception ex)
            {
                Log(LogLevel.Warning, $"Backup network registry falhou: {ex.Message}", "State");
            }
        }

        private async Task BackupPagefileAsync()
        {
            try
            {
                using var hklm = Registry.LocalMachine;
                using var key = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management");
                if (key != null)
                {
                    _state.OriginalPagingFiles = key.GetValue("PagingFiles") as string;
                }
            }
            catch (Exception ex)
            {
                Log(LogLevel.Warning, $"Backup pagefile falhou: {ex.Message}", "State");
            }
        }

        private async Task BackupAudioDisplayAsync()
        {
            try
            {
                // Audio Spatial
                using var hklm = Registry.LocalMachine;
                using var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Audio");
                if (key != null)
                {
                    _state.OriginalAllowSpatialAudio = key.GetValue("AllowSpatialAudio") as int?;
                }

                // Display Gamma Ramp (via WMI/PInvoke seria ideal, aqui placeholder)
                // _state.OriginalGammaRamp = GetCurrentGammaRamp();
            }
            catch (Exception ex)
            {
                Log(LogLevel.Warning, $"Backup audio/display falhou: {ex.Message}", "State");
            }
        }
        
        private void DeleteStateFile()
        {
            try
            {
                if (File.Exists(StatePath))
                    File.Delete(StatePath);
            }
            catch { }
        }
        
        private bool RestorePreviousState()
        {
            try
            {
                if (!File.Exists(StatePath))
                    return false;
                
                var json = File.ReadAllText(StatePath);
                var previousState = JsonSerializer.Deserialize<GamerModeState>(json, new JsonSerializerOptions { ReferenceHandler = ReferenceHandler.IgnoreCycles });
                
                if (previousState?.IsActive == true)
                {
                    Log(LogLevel.Warning, "Estado anterior encontrado - executando rollback de recuperação COMPLETO", "Recovery");
                    
                    // 1. Restaurar plano de energia
                    if (!string.IsNullOrEmpty(previousState.OriginalPowerPlanGuid))
                    {
                        try
                        {
                            _powerPlanService.SetPowerPlanByGuid(previousState.OriginalPowerPlanGuid);
                            Log(LogLevel.Info, "Plano de energia restaurado", "Recovery");
                        }
                        catch (Exception ex)
                        {
                            Log(LogLevel.Warning, $"Falha ao restaurar power plan: {ex.Message}", "Recovery");
                        }
                    }
                    
                    // 2. Restaurar GPU Registry (HAGS, GameDVR, FSO, NVIDIA, AMD)
                    try { RestoreGpuRegistry(previousState); } catch (Exception ex) { Log(LogLevel.Warning, $"GPU registry restore falhou: {ex.Message}", "Recovery"); }
                    
                    // 3. Restaurar processo do jogo (prioridade/afinidade)
                    if (previousState.ActiveGameProcessId.HasValue)
                    {
                        try { RestoreGameProcessState(previousState.ActiveGameProcessId.Value, previousState); } catch (Exception ex) { Log(LogLevel.Warning, $"Process restore falhou: {ex.Message}", "Recovery"); }
                    }
                    
                    // 4. Restaurar serviços Windows
                    try { RestoreWindowsServices(previousState); } catch (Exception ex) { Log(LogLevel.Warning, $"Services restore falhou: {ex.Message}", "Recovery"); }
                    
                    // 5. Restaurar Kernel Registry
                    try { RestoreKernelRegistry(previousState); } catch (Exception ex) { Log(LogLevel.Warning, $"Kernel registry restore falhou: {ex.Message}", "Recovery"); }
                    
                    // 6. Restaurar Network Registry
                    try { RestoreNetworkRegistry(previousState); } catch (Exception ex) { Log(LogLevel.Warning, $"Network registry restore falhou: {ex.Message}", "Recovery"); }
                    
                    // 7. Restaurar Pagefile
                    try { RestorePagefile(previousState); } catch (Exception ex) { Log(LogLevel.Warning, $"Pagefile restore falhou: {ex.Message}", "Recovery"); }
                    
                    // 8. Restaurar Audio/Display
                    try { RestoreAudioDisplay(previousState); } catch (Exception ex) { Log(LogLevel.Warning, $"Audio/Display restore falhou: {ex.Message}", "Recovery"); }
                    
                    // 9. Restaurar Timer Resolution
                    if (previousState.TimerResolutionActive)
                    {
                        try { _timerResolution?.ReleaseResolution(); } catch { }
                    }
                    
                    // 10. Restaurar BH PROCHOT
                    if (previousState.OriginalBdProchotEnabled && ProchotService != null)
                    {
                        try { _ = ProchotService.EnableBdProchotAsync(); } catch { }
                    }
                    
                    // 11. Restaurar GPU via service
                    if (previousState.GpuOptimized)
                    {
                        try
                        {
                            Task.Run(() => _gpuService.RestoreAsync(CancellationToken.None)).GetAwaiter().GetResult();
                            Log(LogLevel.Info, "Configurações de GPU restauradas via service", "Recovery");
                        }
                        catch { }
                    }
                    
                    DeleteStateFile();
                    Log(LogLevel.Info, "✓ Recuperação de crash COMPLETA concluída", "Recovery");
                    return true;
                }
                
                // Estado salvo sem ativacao pendente: nada a reverter
                DeleteStateFile();
                return false;
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, $"Erro na recuperação de crash: {ex.Message}", "Recovery");
                return false;
            }
        }

        private void RestoreGpuRegistry(GamerModeState state)
        {
            try
            {
                using var hklm = Registry.LocalMachine;
                
                // HAGS / GameDVR / FSO
                using var gameKey = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", true);
                if (gameKey != null)
                {
                    if (state.OriginalHwSchMode.HasValue) gameKey.SetValue("HwSchMode", state.OriginalHwSchMode.Value, RegistryValueKind.DWord);
                    if (state.OriginalGameDVREnabled.HasValue) gameKey.SetValue("GameDVR_Enabled", state.OriginalGameDVREnabled.Value, RegistryValueKind.DWord);
                    if (state.OriginalFsoBehaviorMode.HasValue) gameKey.SetValue("HonorUserFSEBehaviorMode", state.OriginalFsoBehaviorMode.Value, RegistryValueKind.DWord);
                    if (state.OriginalDxgiHonorFse.HasValue) gameKey.SetValue("DXGIHonorFSEWindowsCompatible", state.OriginalDxgiHonorFse.Value, RegistryValueKind.DWord);
                    Log(LogLevel.Info, "HAGS/GameDVR/FSO registry restaurado", "Recovery");
                }

                // NVIDIA
                using var nvKey = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000", true);
                if (nvKey != null)
                {
                    if (state.OriginalNvPowerMizerEnable.HasValue) nvKey.SetValue("PowerMizerEnable", state.OriginalNvPowerMizerEnable.Value, RegistryValueKind.DWord);
                    if (state.OriginalNvPowerMizerLevel.HasValue) nvKey.SetValue("PowerMizerLevel", state.OriginalNvPowerMizerLevel.Value, RegistryValueKind.DWord);
                    if (state.OriginalNvPerfLevelSrc.HasValue) nvKey.SetValue("PerfLevelSrc", state.OriginalNvPerfLevelSrc.Value, RegistryValueKind.DWord);
                    if (state.OriginalNvPowerMizerLevelAC.HasValue) nvKey.SetValue("PowerMizerLevelAC", state.OriginalNvPowerMizerLevelAC.Value, RegistryValueKind.DWord);
                    if (state.OriginalNvPowerMizerLevelDC.HasValue) nvKey.SetValue("PowerMizerLevelDC", state.OriginalNvPowerMizerLevelDC.Value, RegistryValueKind.DWord);
                    Log(LogLevel.Info, "NVIDIA registry restaurado", "Recovery");
                }

                // AMD
                using var amdKey = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0001", true);
                if (amdKey != null)
                {
                    if (state.OriginalAmdPowerXpress.HasValue) amdKey.SetValue("PowerXpress", state.OriginalAmdPowerXpress.Value, RegistryValueKind.DWord);
                    if (state.OriginalAmdGlobalPerformance.HasValue) amdKey.SetValue("GlobalPerformance", state.OriginalAmdGlobalPerformance.Value, RegistryValueKind.DWord);
                    if (!string.IsNullOrEmpty(state.OriginalAmdPpTable)) amdKey.SetValue("PP_PhmSoftPowerPlayTable", state.OriginalAmdPpTable, RegistryValueKind.String);
                    Log(LogLevel.Info, "AMD registry restaurado", "Recovery");
                }
            }
            catch (Exception ex)
            {
                Log(LogLevel.Warning, $"GPU registry restore falhou: {ex.Message}", "Recovery");
            }
        }

        private void RestoreGameProcessState(int pid, GamerModeState state)
        {
            // [FIX:C-3] Este é o RESTORE de prioridade/afinidade — o caminho que
            // devolve o sistema ao estado original. O catch { } vazio anterior
            // significava que uma falha de restore não deixava rastro nenhum: o
            // log dizia "rollback concluído, sistema limpo" enquanto o processo
            // continuava com a afinidade que o VOLTRIS tinha imposto. Agora
            // qualquer falha aqui é visível, porque é exatamente o tipo de
            // problema que o usuário só percebe como "o PC ficou estranho".
            var restaurado = SafeProcess.WithProcess(pid, proc =>
            {
                if (state.OriginalProcessPriority.HasValue && state.OriginalProcessPriority.Value != 0)
                {
                    proc.PriorityClass = (ProcessPriorityClass)state.OriginalProcessPriority.Value;
                }
                if (state.OriginalProcessAffinity.HasValue && state.OriginalProcessAffinity.Value != 0)
                {
                    proc.ProcessorAffinity = new IntPtr(state.OriginalProcessAffinity.Value);
                }
            });

            if (restaurado)
            {
                Log(LogLevel.Info, $"Processo {pid} prioridade/afinidade restaurados", "Recovery");
            }
            else
            {
                Log(LogLevel.Warning,
                    $"[FIX:C-3] NAO foi possivel restaurar prioridade/afinidade do PID {pid}: " +
                    $"o processo encerrou ou recusou a alteracao. Se o VOLTRIS tinha imposto afinidade " +
                    $"custom a este processo e ele continua vivo, o estado original precisa ser restaurado " +
                    $"manualmente.", "Recovery");
            }
        }

        private void RestoreWindowsServices(GamerModeState state)
        {
            try
            {
                foreach (var svcInfo in state.ServiceStates)
                {
                    try
                    {
                        using var sc = new ServiceController(svcInfo.ServiceName);
                        // Restaurar StartType
                        var originalStartType = (ServiceStartMode)svcInfo.OriginalStartType;
                        if (sc.StartType != originalStartType)
                        {
                            // Usar sc.exe para mudar start type (precisa admin)
                            var psi = new ProcessStartInfo("sc.exe", $"config \"{svcInfo.ServiceName}\" start= {GetStartTypeString(svcInfo.OriginalStartType)}")
                            {
                                Verb = "runas",
                                UseShellExecute = true,
                                CreateNoWindow = true,
                                WindowStyle = ProcessWindowStyle.Hidden
                            };
                            Process.Start(psi)?.WaitForExit(5000);
                        }
                        // Restaurar status se era Running
                        if (svcInfo.OriginalStatus == "Running" && sc.Status != ServiceControllerStatus.Running)
                        {
                            sc.Start();
                            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                        }
                        else if (svcInfo.OriginalStatus == "Stopped" && sc.Status != ServiceControllerStatus.Stopped)
                        {
                            sc.Stop();
                            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                        }
                    }
                    catch (Exception ex)
                    {
                        Log(LogLevel.Warning, $"Falha ao restaurar serviço {svcInfo.ServiceName}: {ex.Message}", "Recovery");
                    }
                }
                Log(LogLevel.Info, "Serviços Windows restaurados", "Recovery");
            }
            catch (Exception ex)
            {
                Log(LogLevel.Warning, $"Services restore falhou: {ex.Message}", "Recovery");
            }
        }

        private static string GetStartTypeString(int startType)
        {
            return startType switch
            {
                2 => "auto",
                3 => "demand",
                4 => "disabled",
                _ => "demand"
            };
        }

        private void RestoreKernelRegistry(GamerModeState state)
        {
            try
            {
                using var hklm = Registry.LocalMachine;
                
                // Memory Management
                using var key = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", true);
                if (key != null)
                {
                    if (state.OriginalDisablePagingExecutive.HasValue) key.SetValue("DisablePagingExecutive", state.OriginalDisablePagingExecutive.Value, RegistryValueKind.DWord);
                    if (state.OriginalEnableCompression.HasValue) key.SetValue("EnableCompression", state.OriginalEnableCompression.Value, RegistryValueKind.DWord);
                    if (state.OriginalLargePageMinimum.HasValue) key.SetValue("LargePageMinimum", state.OriginalLargePageMinimum.Value, RegistryValueKind.DWord);
                    if (state.OriginalSystemPages.HasValue) key.SetValue("SystemPages", state.OriginalSystemPages.Value, RegistryValueKind.DWord);
                }

                // PriorityControl
                using var key2 = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\PriorityControl", true);
                if (key2 != null && state.OriginalWin32PrioritySeparation.HasValue)
                {
                    key2.SetValue("Win32PrioritySeparation", state.OriginalWin32PrioritySeparation.Value, RegistryValueKind.DWord);
                }

                // Core Parking
                //
                // [FIX:UNICO-DONO-DE-ENERGIA] A escrita de core parking foi
                // removida, e com ela o BUG DO GUID MALFORMADO que havia aqui.
                //
                // O caminho usava este GUID:
                //
                //     0cc5b647-c1df-4637-891a-dec355318583
                //                     ^^^^^^^^^^^^^^^^ dec35"5"318583
                //
                // O correto, usado em SystemConstants.cs:263 e em
                // OptimizationValidator.cs:126, é:
                //
                //     0cc5b647-c1df-4637-891a-dec35c318583
                //                     ^^^^^^^^^^^^^^^^ dec35"c"318583
                //
                // Um "5" no lugar de um "c". O resultado: a chave NUNCA EXISTIU.
                // Todo o caminho de ativação e de restauração de core parking do
                // Modo Gamer rodou durante anos sem tocar em nada — abria a chave,
                // recebia null, e o código tratava null como "não suportado".
                // Nenhum erro, nenhum log de falha, nenhum sintoma. O código
                // parecia funcionar porque não fazia nada.
                //
                // Isso é pior que um bug que quebra: um bug que quebra é
                // encontrado. Um bug que desativa a si mesmo em silêncio é
                //找到 por acaso, e por isso a correção do GUID não interessa
                // aqui — a escrita não deveria existir de qualquer forma.
                //
                // O valor original continua disponível no estado, caso outra
                // porta legítima de energia precise dele no futuro.
                if (state.OriginalCoreParkingValue.HasValue)
                {
                    _logger.LogInfo(
                        "[GamerMode] Restauração de core parking ignorada: a gravação é do " +
                        $"Perfil Inteligente. (Valor original capturado: " +
                        $"{state.OriginalCoreParkingValue.Value}.)");
                }

                // QuantumBoost
                using var key4 = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", true);
                if (key4 != null && state.OriginalQuantumBoost.HasValue)
                {
                    key4.SetValue("QuantumBoost", state.OriginalQuantumBoost.Value, RegistryValueKind.DWord);
                }

                // IO Priority, IRQ, CPU Latency
                if (state.OriginalIoPriority.HasValue)
                {
                    using var key5 = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", true);
                    key5?.SetValue("IoPriority", state.OriginalIoPriority.Value, RegistryValueKind.DWord);
                }

                if (state.OriginalIrq8Priority.HasValue)
                {
                    using var key6 = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\IRQ8", true);
                    key6?.SetValue("Priority", state.OriginalIrq8Priority.Value, RegistryValueKind.DWord);
                }

                if (state.OriginalIrq16Priority.HasValue)
                {
                    using var key7 = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\IRQ16", true);
                    key7?.SetValue("Priority", state.OriginalIrq16Priority.Value, RegistryValueKind.DWord);
                }

                if (state.OriginalIrqPriority.HasValue)
                {
                    using var key8 = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\PriorityControl", true);
                    key8?.SetValue("IRQPriority", state.OriginalIrqPriority.Value, RegistryValueKind.DWord);
                }

                if (state.OriginalCpuLatency.HasValue)
                {
                    using var key9 = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", true);
                    key9?.SetValue("CpuLatency", state.OriginalCpuLatency.Value, RegistryValueKind.DWord);
                }

                if (state.OriginalNumaAllocationPolicy.HasValue)
                {
                    using var key10 = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", true);
                    key10?.SetValue("NumaAllocationPolicy", state.OriginalNumaAllocationPolicy.Value, RegistryValueKind.DWord);
                }

                Log(LogLevel.Info, "Kernel registry restaurado", "Recovery");
            }
            catch (Exception ex)
            {
                Log(LogLevel.Warning, $"Kernel registry restore falhou: {ex.Message}", "Recovery");
            }
        }

        private void RestoreNetworkRegistry(GamerModeState state)
        {
            try
            {
                using var hklm = Registry.LocalMachine;
                using var key = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", true);
                if (key != null)
                {
                    if (state.OriginalNetworkThrottlingIndex.HasValue) key.SetValue("NetworkThrottlingIndex", state.OriginalNetworkThrottlingIndex.Value, RegistryValueKind.DWord);
                    if (state.OriginalSystemResponsiveness.HasValue) key.SetValue("SystemResponsiveness", state.OriginalSystemResponsiveness.Value, RegistryValueKind.DWord);
                    if (state.OriginalTcpAckFrequency.HasValue) key.SetValue("TcpAckFrequency", state.OriginalTcpAckFrequency.Value, RegistryValueKind.DWord);
                    Log(LogLevel.Info, "Network registry restaurado", "Recovery");
                }
            }
            catch (Exception ex)
            {
                Log(LogLevel.Warning, $"Network registry restore falhou: {ex.Message}", "Recovery");
            }
        }

        private void RestorePagefile(GamerModeState state)
        {
            try
            {
                if (!string.IsNullOrEmpty(state.OriginalPagingFiles))
                {
                    using var hklm = Registry.LocalMachine;
                    using var key = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", true);
                    if (key != null)
                    {
                        key.SetValue("PagingFiles", state.OriginalPagingFiles, RegistryValueKind.MultiString);
                        Log(LogLevel.Info, "Pagefile restaurado", "Recovery");
                    }
                }
            }
            catch (Exception ex)
            {
                Log(LogLevel.Warning, $"Pagefile restore falhou: {ex.Message}", "Recovery");
            }
        }

        private void RestoreAudioDisplay(GamerModeState state)
        {
            try
            {
                // Audio Spatial
                if (state.OriginalAllowSpatialAudio.HasValue)
                {
                    using var hklm = Registry.LocalMachine;
                    using var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Audio", true);
                    if (key != null)
                    {
                        key.SetValue("AllowSpatialAudio", state.OriginalAllowSpatialAudio.Value, RegistryValueKind.DWord);
                    }
                }

                // Display Gamma (placeholder - requer PInvoke SetDeviceGammaRamp)
                // if (state.OriginalGammaRamp != null) SetDeviceGammaRamp(state.OriginalGammaRamp);

                Log(LogLevel.Info, "Audio/Display restaurados", "Recovery");
            }
            catch (Exception ex)
            {
                Log(LogLevel.Warning, $"Audio/Display restore falhou: {ex.Message}", "Recovery");
            }
        }

        public Task<bool> RestorePreviousStateAsync()
        {
            return Task.FromResult(RestorePreviousState());
        }
        
        #endregion
        
        #region Logging
        
        public IReadOnlyList<GamerModeLogEntry> GetRecentLogs(int count = 100)
        {
            return _logEntries.TakeLast(count).ToList().AsReadOnly();
        }
        
        private void Log(LogLevel level, string message, string category)
        {
            var entry = new GamerModeLogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Message = message,
                Category = category
            };
            
            _logEntries.Enqueue(entry);
            
            // Limitar tamanho do log
            while (_logEntries.Count > MAX_LOG_ENTRIES)
            {
                _logEntries.TryDequeue(out _);
            }
            
            // Log assíncrono — não bloquear caminho crítico com I/O
            try
            {
                switch (level)
                {
                    case LogLevel.Error:
                    case LogLevel.Critical:
                        _logger.LogError($"[GamerMode:{category}] {message}");
                        break;
                    case LogLevel.Warning:
                        _logger.LogWarning($"[GamerMode:{category}] {message}");
                        break;
                    default:
                        _logger.LogInfo($"[GamerMode:{category}] {message}");
                        break;
                }
            }
            catch
            {
                // Log não deve nunca quebrar o fluxo principal
            }
            
            // Evento de log disparado fora do caminho crítico
            try
            {
                LogEntry?.Invoke(this, entry);
            }
            catch
            {
                // Subscriber não deve quebrar o logger
            }
        }
        
        #endregion
        
        #region State Management
        
        /// <summary>
        /// Desativa otimizações inseguras na configuração
        /// </summary>
        private async Task DisableUnsafeOptimizationsAsync(List<string> unsafeOptimizations)
        {
            try
            {
                foreach (var optimization in unsafeOptimizations)
                {
                    switch (optimization)
                    {
                        case "CPU-Overclocking":
                            _config.OptimizeCpuScheduler = false;
                            Log(LogLevel.Warning, "[FAIL] CPU Overclocking desativado - hardware não suporta", "Safety");
                            break;
                            
                        case "GPU-Overclocking":
                            _config.OptimizeGpu = false;
                            Log(LogLevel.Warning, "[FAIL] GPU Overclocking desativado - hardware não suporta", "Safety");
                            break;
                            
                        case "Memory-XMP":
                            _config.OptimizeMemory = false;
                            Log(LogLevel.Warning, "[FAIL] Memory XMP desativado - hardware não suporta", "Safety");
                            break;
                            
                        case "Storage-Advanced-Optimization":
                            _config.OptimizeNetwork = false;
                            Log(LogLevel.Warning, "[FAIL] Storage Advanced desativado - hardware não suporta", "Safety");
                            break;
                            
                        case "BIOS-Overclocking":
                            _config.OptimizeKernel = false;
                            Log(LogLevel.Warning, "[FAIL] BIOS Overclocking desativado - hardware não suporta", "Safety");
                            break;
                    }
                }
                
                // Salvar configuração atualizada
                await SaveConfigAsync();
                
                Log(LogLevel.Info, $"[OK] {unsafeOptimizations.Count} otimizações inseguras desativadas", "Safety");
            }
            catch (Exception ex)
            {
                _logger.LogError("[Gamer-Mode] Erro ao desativar otimizações inseguras", ex);
            }
        }
        
        private void UpdateState(Action<GamerModeState> updateAction)
        {
            GamerModeState snapshot;
            lock (_stateLock)
            {
                updateAction(_state);
                snapshot = _state.Clone();
            }
            StateChanged?.Invoke(this, snapshot);
        }
        
        #endregion
        
        private async void OnLicenseStatusChanged(object? sender, EventArgs e)
        {
            try
            {
                if (!VoltrisOptimizer.Services.License.ProFeatureGuard.IsPaidActive() && IsActive)
                {
                    Log(LogLevel.Warning, "[GamerModeManager] Licença paga ausente. Forçando desativação do Modo Gamer.", "License");
                    await DeactivateAsync(CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, $"Erro no handler de licença: {ex.Message}", "License");
            }
        }

        #region IDisposable
        
        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            
            StopGameDetection();
            StopThermalMonitoring();
            
            // Encerramento limpo: se o modo gamer estiver ativo, reverter as otimizacoes
            // aplicadas. Isso deixa o sistema como estava e evita que a proxima inicializacao
            // interprete o arquivo de estado como "crash" e dispare recuperacao desnecessaria.
            try
            {
                if (IsActive)
                {
                    Log(LogLevel.Info, "Encerramento limpo com modo gamer ativo - revertendo otimizacoes", "Recovery");
                    RestorePreviousState();
                }
                else
                {
                    DeleteStateFile();
                }
            }
            catch (Exception exDispose)
            {
                Log(LogLevel.Warning, $"Falha ao reverter estado no encerramento: {exDispose.Message}", "Recovery");
            }
            
            _gameDetection.GameStarted -= OnGameStarted;
            _gameDetection.GameStopped -= OnGameStopped;
            
            // [OPÇÃO C] Cleanup do timer de persistência
            _gameSessionPersistenceTimer?.Dispose();
            
            (_powerPlanService as IDisposable)?.Dispose();
            (_gpuService as IDisposable)?.Dispose();
            (_thermalMonitor as IDisposable)?.Dispose();
            (_gameDetection as IDisposable)?.Dispose();
            (_processService as IDisposable)?.Dispose();
        }
        
        #endregion
    }
}

