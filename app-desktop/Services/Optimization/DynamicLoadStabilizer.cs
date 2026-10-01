using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Models;
using VoltrisOptimizer.Services.Optimization.Engines;
using VoltrisOptimizer.Utils.Win32;
using VoltrisOptimizer.Services.Gamer.Implementation;
using VoltrisOptimizer.Core.Brain.V2;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Utils;

namespace VoltrisOptimizer.Services.Optimization
{
    /// <summary>
    /// DSL 5.0 - Motor de Estabilização Inteligente (Stability Agent).
    /// Integra todos os engines especializados em um loop unificado com telemetria do kernel.
    /// Foco em responsividade real: abertura de programas, troca de janelas, fluidez do Explorer.
    /// </summary>
    public class DynamicLoadStabilizer : IDynamicLoadStabilizer
    {
        private readonly object _lock = new();
        private CancellationTokenSource? _cts;
        private Task? _monitorTask;

        // Métricas e Estado
        private readonly ConcurrentDictionary<int, TimeSpan> _prevCpuTimes = new();
        private readonly HashSet<int> _throttled = new();
        private readonly ConcurrentDictionary<int, ProcessPriorityClass> _boostedProcesses = new();
        private DateTime _lastSampleTime = DateTime.MinValue;

        // Cache e Controle
        private DateTime _lastProcessRefresh = DateTime.MinValue;
        private DateTime _lastMemoryTrim = DateTime.MinValue;
        private Process[] _cachedProcesses = Array.Empty<Process>();
        private HashSet<int> _previousProcessPids = new();
        private int? _gamePid;
        private int? _lastForegroundPid;
        private bool _antiCheatActive;
        private IntelligentProfileType _currentProfile = IntelligentProfileType.GeneralBalanced;
        private readonly int _ownPid = Process.GetCurrentProcess().Id;

        // === DSL 5.0 ENGINES ===
        private readonly SharedTelemetryProvider _telemetry;
        private readonly DecisionEngine50 _decisionEngine;
        private readonly CpuGovernorEngine _cpuGovernor;
        private readonly IoSchedulerEngine _ioScheduler;
        private readonly InputLatencyEngine? _inputLatency;
        private readonly InterruptLatencyEngine _interruptLatency;
        private readonly ProcessBehaviorEngine _behaviorEngine;
        private readonly ThermalPowerEngine _thermalEngine;
        private readonly StabilityGuardEngine _stabilityGuard;
        private readonly HardwareDetectionEngine _hardwareDetection;
        private readonly ProcessLaunchAccelerator _launchAccelerator;
        private readonly SystemState50 _systemState = new();

        // Serviços base
        private readonly ILoggingService _logger;
        private readonly DlsLogger _dlsLogger;
        private readonly CoreLoadHeuristics _heuristics;
        private readonly CriticalProcessDetector _detector;
        private readonly Providers.IGpuLoadProvider? _gpuProvider;
        private readonly Providers.IProcessProvider? _processProvider;
        private readonly ProcessCacheService? _processCache;
        private readonly GameDetectionService? _gameDetection;
        private readonly StabilityEngineService? _stabilityEngine;
        private readonly VoltrisBrainV2 _brain; // ✅ FASE 5: Brain integration

        // === MÉTRICAS DE IMPACTO ===
        private long _totalCycles;
        private long _totalActionsApplied;
        private long _totalProcessesThrottled;
        private long _totalStartupBoosts;
        private double _cycleTimeAccumulatorMs;
        private readonly Stopwatch _cycleStopwatch = new();

        // Explorer responsiveness tracking
        private DateTime _lastExplorerCheck = DateTime.MinValue;
        private int _explorerStallCount;

        public bool IsRunning { get; private set; }
        public bool Enabled { get; set; } = true;
        public TimeSpan Interval { get; set; } = TimeSpan.FromMilliseconds(5000);

        public event EventHandler<ThrottledEventArgs>? OnProcessThrottled;
        public event EventHandler<ThrottledEventArgs>? OnProcessReleased;

        public IReadOnlyCollection<int> ThrottledProcessIds
        {
            get { lock (_lock) { return _throttled.ToList().AsReadOnly(); } }
        }

        public DynamicLoadStabilizer(
            ILoggingService? logger = null,
            Providers.ICpuCoreLoadProvider? cpuProvider = null,
            Providers.IGpuLoadProvider? gpuProvider = null,
            Providers.IProcessProvider? processProvider = null,
            CoreLoadHeuristics? heuristics = null,
            CriticalProcessDetector? detector = null,
            ProcessCacheService? processCache = null,
            GameDetectionService? gameDetection = null,
            StabilityEngineService? stabilityEngine = null,
            VoltrisBrainV2 brain = null) // ✅ FASE 5: Brain injection
        {
            _gpuProvider = gpuProvider;
            _processProvider = processProvider;
            _processCache = processCache ?? Core.ServiceLocator.GetService<ProcessCacheService>();
            _gameDetection = gameDetection ?? Core.ServiceLocator.GetService<GameDetectionService>();
            _stabilityEngine = stabilityEngine ?? Core.ServiceLocator.GetService<StabilityEngineService>();
            _brain = brain ?? Core.ServiceLocator.GetService<VoltrisBrainV2>(); // ✅ FASE 5

            _logger = logger ?? Core.ServiceLocator.GetService<ILoggingService>() ?? new NullLogger();
            _dlsLogger = new DlsLogger();
            _heuristics = heuristics ?? new CoreLoadHeuristics(cpuProvider);
            _detector = detector ?? new CriticalProcessDetector(processProvider);

            VoltrisOptimizer.Services.Gamer.Implementation.GamerNativeMethods.SetLogger(_logger);

            // === INICIALIZAR ENGINES DSL 5.0 ===
            _telemetry = new SharedTelemetryProvider(_logger);
            _decisionEngine = new DecisionEngine50();
            _cpuGovernor = new CpuGovernorEngine(_logger);
            _ioScheduler = new IoSchedulerEngine(_logger);
            _interruptLatency = new InterruptLatencyEngine(_logger);
            _behaviorEngine = new ProcessBehaviorEngine(_logger);
            _thermalEngine = new ThermalPowerEngine(_logger);
            _stabilityGuard = new StabilityGuardEngine(_logger);
            _hardwareDetection = new HardwareDetectionEngine(_logger);
            _launchAccelerator = new ProcessLaunchAccelerator(_logger);

            var timerService = Core.ServiceLocator.GetService<ITimerResolutionService>();
            _inputLatency = timerService != null ? new InputLatencyEngine(_logger, timerService) : null;

            _logger.LogInfo($"[DSL 5.0] Engines inicializados.");
            
            if (_brain != null)
            {
                _logger.LogInfo("[DSL 5.0] Brain V2 injetado - reportando decisões para Q-Learning");
            }
        }

        public void SetAntiCheatActive(bool active)
        {
            _antiCheatActive = active;
            _launchAccelerator.SetAntiCheatActive(active);
            if (active)
            {
                _logger.LogInfo("[DSL] Modo anti-cheat ativado — pulando otimizações de processo do jogo");
            }
            else
            {
                _logger.LogInfo("[DSL] Modo anti-cheat desativado — otimizações de processo liberadas");
            }
        }

        public void SetProfile(IntelligentProfileType profile)
        {
            lock (_lock)
            {
                _currentProfile = profile;
                _systemState.CurrentProfile = profile;
                Interval = profile switch
                {
                    IntelligentProfileType.GamerCompetitive    => TimeSpan.FromMilliseconds(3000),
                    IntelligentProfileType.CreativeVideoEditing => TimeSpan.FromMilliseconds(5000),
                    _                                           => TimeSpan.FromMilliseconds(8000)
                };
            }
        }

        public async Task StartGlobalAsync(CancellationToken ct = default)
        {
            if (!Enabled) return;
            lock (_lock)
            {
                if (IsRunning) return;
                _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                IsRunning = true;
                _monitorTask = Task.Run(() => { return MonitorLoopAsync(_cts.Token); }, _cts.Token);
            }
            _launchAccelerator.Start();
            _ = Task.Run(async () => await _hardwareDetection.InitializeAsync());
        }

        public async Task StartAsync(int? gameProcessId = null, CancellationToken ct = default)
        {
            _logger.LogEntry(nameof(StartAsync), ("gameProcessId", gameProcessId));
            if (!Enabled) return;
            lock (_lock)
            {
                if (IsRunning)
                {
                    _gamePid = gameProcessId;
                    return;
                }
                _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                _gamePid = gameProcessId;
                IsRunning = true;
                _monitorTask = Task.Run(() => { return MonitorLoopAsync(_cts.Token); }, _cts.Token);
            }
            _launchAccelerator.Start();
            _ = Task.Run(async () => await _hardwareDetection.InitializeAsync());
            _logger.LogExit(nameof(StartAsync));
        }

        public async Task StopAsync(CancellationToken ct = default)
        {
            _logger.LogEntry(nameof(StopAsync));
            lock (_lock)
            {
                if (!IsRunning) return;
                try { _cts?.Cancel(); } catch { }
            }

            try
            {
                if (_monitorTask != null)
                    await Task.WhenAny(_monitorTask, Task.Delay(3000, ct));
            }
            catch { }

            RestoreAll();

            lock (_lock)
            {
                IsRunning = false;
                _cts?.Dispose();
                _cts = null;
                _gamePid = null;
            }
            _logger.LogExit(nameof(StopAsync));
        }

        private async Task MonitorLoopAsync(CancellationToken ct)
        {
            _logger.LogEntry(nameof(MonitorLoopAsync));
            _logger.LogInfo("[DSL 5.0] Monitor Loop iniciado.");
            await Task.Delay(2000, ct);

            while (!ct.IsCancellationRequested)
            {
                VoltrisOptimizer.Services.Diagnostics.ThreadNameRegistry.Register("DSL-MonitorLoop");
                    _cycleStopwatch.Restart();
                    _logger.LogLoop("MonitorCycle", (int)_totalCycles);

                    try
                    {
                        // 1. TELEMETRIA CENTRALIZADA
                        _telemetry.Update(_systemState);
                        _logger.LogValue("CPU_Usage", $"{_systemState.CpuUsagePercent:F1}%");
                        _logger.LogValue("RAM_Usage", $"{_systemState.CommitChargePercent:F0}%");
                        _logger.LogValue("Disk_Queue", $"{_systemState.DiskQueueLength:F2}");
                    
                    // 2. SCAN DE PROCESSOS NATIVO (MUITO RÁPIDO)
                    var processInfos = _processCache?.GetCachedProcessInfos()?.ToArray() ?? Array.Empty<ProcessCacheService.CachedProcessInfo>();
                    
                    // 3. FOREGROUND TRACKING — Sincronizado com central tracker
                    _systemState.ForegroundPid = (int)Core.ForegroundWindowTracker.Instance.CurrentPid;

                    // 4. DECISION ENGINE (OTIMIZAÇÃO: Só processar se realmente necessário)
                    bool shouldCpu = _decisionEngine.ShouldOptimizeCpu(_systemState);
                    bool shouldIo = _decisionEngine.ShouldOptimizeIo(_systemState);
                    bool shouldMemory = _decisionEngine.ShouldOptimizeMemory(_systemState);

                    _logger.LogDecision("cycle_strategy",
                        shouldCpu || shouldIo || shouldMemory ? "active" : "quiet",
                        $"cpu={shouldCpu} io={shouldIo} mem={shouldMemory}");

                    if (!shouldCpu && !shouldIo && !shouldMemory && _totalCycles % 5 != 0)
                    {
                        // Quiet Mode: Sistema estável, pular ciclos pesados para poupar CPU
                        _cycleStopwatch.Stop();
                        VoltrisOptimizer.Services.Diagnostics.ThreadNameRegistry.Unregister();
                        await Task.Delay(Interval, ct);
                        continue;
                    }

                    // 5. ENGINES GLOBAIS
                    _thermalEngine.Update();

                    // ✅ ANTI-CHEAT: Pular latência de interrupção se o foreground for o jogo
                    bool skipInterrupt = _antiCheatActive && _gamePid.HasValue &&
                        _gamePid.Value == _systemState.ForegroundPid;
                    if (!skipInterrupt)
                    {
                        _interruptLatency.Update(
                            _systemState.ForegroundPid > 0 ? _systemState.ForegroundPid : (int?)null,
                            _systemState);
                    }

                    _stabilityGuard.CheckGlobalHealth(_systemState);

                    // 6. GOVERNANÇA POR PROCESSO (Filtrada por impacto)
                    foreach (var p in processInfos)
                    {
                        if (p.Id == _ownPid) continue;

                        // ✅ ANTI-CHEAT: NUNCA abrir handle do processo do jogo
                        if (_antiCheatActive && _gamePid.HasValue && p.Id == _gamePid.Value)
                            continue;

                        // OTIMIZAÇÃO CRÍTICA: Ignorar processos inativos (0% CPU) para não abrir handles desnecessários
                        if (p.CpuUsage < 0.1 && p.BasePriority <= 8) continue; 

                        // ANALIZAR COMPORTAMENTO (Background)
                        _behaviorEngine.Update(new[] { p }, _systemState.ForegroundPid);

                        // GOVERNANÇA CPU/IO/STABILITY
                        if (shouldCpu || shouldIo)
                        {
                            _cpuGovernor.Governance(p, _systemState);
                            _ioScheduler.Governance(p, _systemState);
                        }
                    }


                    // 7. MEMORY GUARDIAN
                    if (shouldMemory) MaintainMemoryPressure();

                    // 8. FOREGROUND BOOST — ✅ ANTI-CHEAT: Pular se o foreground for o jogo
                    bool skipBoost = _antiCheatActive && _gamePid.HasValue &&
                        _gamePid.Value == _systemState.ForegroundPid;
                    if (!skipBoost) ApplyForegroundBoost();

                    // 9. SHELL PROTECTION (Explorer boost) — unificado com logs
                    ProtectShell();

                    // 10. LOG DE ESTADO DETALHADO a cada ciclo (para DEBUG máximo)
                    _dlsLogger.Log($"Cycle={_totalCycles}|CPU={_systemState.CpuUsagePercent:F1}%|Mem={_systemState.CommitChargePercent:F0}%|DiskQ={_systemState.DiskQueueLength:F2}|FG={_systemState.ForegroundPid}|Prof={_currentProfile}|AntiCheat={_antiCheatActive}|GamePid={_gamePid}|CpuOpt={shouldCpu}|IoOpt={shouldIo}|MemOpt={shouldMemory}|Procs={processInfos.Length}|Elapsed={_cycleStopwatch.Elapsed.TotalMilliseconds:F1}ms");
                    
                    // ✅ FASE 5: REPORTAR DECISÃO PARA BRAIN APRENDER (a cada 10 ciclos)
                    if (_brain != null && _totalCycles % 10 == 0)
                    {
                        await ReportDecisionToBrainAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[DSL 5.0] Erro no loop de governança: {ex.Message}");
                    _dlsLogger.Log($"ERROR: {ex.GetType().Name}: {ex.Message}");
                }

                _cycleStopwatch.Stop();
                _totalCycles++;
                _cycleTimeAccumulatorMs += _cycleStopwatch.Elapsed.TotalMilliseconds;

                if (_totalCycles % 10 == 0)
                {
                    double avgMs = _cycleTimeAccumulatorMs / _totalCycles;
                    _logger.LogInfo($"[DSL 5.0] Performance: {_totalCycles} ciclos | Avg: {avgMs:F1}ms");
                    _dlsLogger.Log($"PERF: {_totalCycles} ciclos, media {avgMs:F1}ms/ciclo");
                }

                VoltrisOptimizer.Services.Diagnostics.ThreadNameRegistry.Unregister();
                await Task.Delay(Interval, ct);
            }
            _logger.LogExit(nameof(MonitorLoopAsync));
        }

        /// <summary>
        /// FASE 5: Reporta decisão DSL para Brain aprender
        /// PROTEÇÃO CRÍTICA: Bucketing agressivo + Normalização de reward
        /// </summary>
        private async Task ReportDecisionToBrainAsync()
        {
            if (_brain == null) return;

            try
            {
                // ✅ PROTEÇÃO CRÍTICA #1: Bucketing agressivo
                var brainState = new BrainStateKey(
                    workload: ClassifyWorkloadFromSystemState(_systemState),
                    cpuBucket: (byte)(_systemState.CpuUsagePercent / 10.0),
                    ramBucket: (byte)(_systemState.CommitChargePercent / 10.0),
                    tempBucket: 5,
                    contextBucket: 1  // DSL context
                );

                // Calcular reward baseado em throttling effectiveness
                double reward = CalculateDslReward(_systemState);
                // (A normalização para [-1.0, 1.0] é feita internamente por ReportExternalReward)

                // Observar estado no Brain
                _brain.Memory.Observe(brainState, new SensorSnapshot
                {
                    ForegroundProcessName = GetForegroundProcessName(_systemState.ForegroundPid),
                    Workload = (WorkloadCategory)brainState.WorkloadBucket,
                    CpuUsagePercent = _systemState.CpuUsagePercent,
                    RamUsagePercent = _systemState.CommitChargePercent,
                    CpuTemperatureC = 50.0
                });

                // Reportar reward
                _brain.ReportExternalReward("dsl_cycle", reward, new {
                    cpu = _systemState.CpuUsagePercent,
                    mem = _systemState.CommitChargePercent,
                    diskQ = _systemState.DiskQueueLength,
                    fgPid = _systemState.ForegroundPid,
                    throttled = _throttled.Count
                });

                _dlsLogger.Log($"BRAIN_REPORT: state={brainState.CanonicalKey} reward={reward:F3}");
            }
            catch (Exception ex)
            {
                _dlsLogger.Log($"BRAIN_REPORT_ERROR: {ex.Message}");
            }
        }

        // Métodos auxiliares para DSL → Brain integration
        private static WorkloadCategory ClassifyWorkloadFromSystemState(SystemState50 state)
        {
            if (state.ForegroundPid > 0)
            {
                // Verificar se é jogo (simplificado)
                return WorkloadCategory.Game;
            }
            return state.CpuUsagePercent > 50 ? WorkloadCategory.Work : WorkloadCategory.Idle;
        }

        private double CalculateDslReward(SystemState50 state)
        {
            double reward = 0.0;

            // Reward por CPU sob controle
            if (state.CpuUsagePercent < 70.0)
                reward += 1.0;

            // Reward por memória estável
            if (state.CommitChargePercent < 80.0)
                reward += 1.0;

            // Reward por baixa latência de disco
            if (state.DiskQueueLength < 1.0)
                reward += 1.0;

            // Penalizar stutter
            // Nota: SystemState50 não possui StutterDetected, desativado temporariamente
            // if (state.StutterDetected)
            //     reward -= 2.0;

            return Math.Clamp(reward, -2.0, 3.0);
        }

        private static string GetForegroundProcessName(int pid)
        {
            try
            {
                using var proc = SafeProcess.TryGet(pid);
                return proc.ProcessName;
            }
            catch
            {
                return "unknown";
            }
        }


        private void ProtectShell()
        {
            try
            {
                var processInfos = _processCache?.GetCachedProcessInfos() ?? Enumerable.Empty<ProcessCacheService.CachedProcessInfo>();
                var explorer = processInfos.FirstOrDefault(p => p.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase));
                if (explorer == null) return;

                IntPtr hExplorer = ProcessNativeMethods.OpenProcess(ProcessNativeMethods.PROCESS_SET_INFORMATION, false, explorer.Id);
                if (hExplorer != IntPtr.Zero)
                {
                    try
                    {
                        ProcessNativeMethods.SetPriorityClass(hExplorer, ProcessNativeMethods.ABOVE_NORMAL_PRIORITY_CLASS);
                        GamerNativeMethods.SetProcessIoPriority(hExplorer, GamerNativeMethods.IoPriorityLevel.IoPriorityHigh, out _);
                        _dlsLogger.Log($"SHELL: explorer.exe {explorer.Id} boosted to ABOVE_NORMAL + HighIO");
                    }
                    finally { ProcessNativeMethods.CloseHandle(hExplorer); }
                }
            }
            catch (Exception ex)
            {
                _dlsLogger.Log($"SHELL_ERROR: {ex.Message}");
            }
        }

        /// <summary>
        /// [FIX:A-4] Processos de infraestrutura/mainutenção que NUNCA podem ser
        /// promovidos pelo VOLTRIS.
        ///
        /// BUG ORIGINAL: <see cref="ApplyForegroundBoost"/> promovia o processo em
        /// primeiro plano sem olhar o que ele é. Se o usuário (ou um serviço)
        /// colocasse em primeiro plano um instalador, o VOLTRIS aplicava nele
        /// HIGH_PRIORITY_CLASS + IoPriorityHigh + PowerThrottling desligado.
        ///
        /// Isso não é "só um ajuste de prioridade" em cima de um instalador do
        /// Windows. TrustedInstaller, msiexec, TiWorker e friends operam sobre o
        /// Component Store com o TDR e o I/O de disco sob pressão; forçá-los para
        /// I/O alto e sem power throttling aumenta de forma concreta a chance de
        /// o Windows Update corromper o store e deixá-lo faltando arquivos. E o
        /// VOLTRIS tem histórico de aggressively reiniciar esses serviços
        /// (ver GamerModeOrchestrator e ExtremeOptimizationsService), então
        /// elevar a prioridade do instalador enquanto o VOLTRIS também mexe nos
        /// serviços dele é a combinação que produz Blue Screen de
        /// PAGE_FAULT_IN_NONPAGED_AREA.
        ///
        /// A lista é de infraestrutura, não de "apps undesirable": ela não
        ///-toucha jogos, navegadores nem programas do usuário. Um app comum que
        /// o usuário abriu continua sendo promovido como antes.
        /// </summary>
        private static readonly HashSet<string> _neverBoostProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            // Instaladores e manutenção do Windows
            "msiexec", "trustedinstaller", "tiworker", "wuauclt", "wusa",
            "setup", "installer", "dism", "dismhost", "sfc", "sfcmain",
            "compattelrunner", "mousocoreworker", "musnotificationux",
            // WMI: elevar o host do WMI compete com tudo que o Windows consulta por WMI
            "wmiapsrv", "wmiprvse", "wmiexec",
            // PowerShell / scripting: executados em foreground pelas ferramentas de manutencao
            "powershell", "pwsh", "cmd", "conhost",
            // Rede e entrega de conteúdo
            "backgroundtransferhost", "browserexe",
            // Infraestrutura de sessão e segurança
            "securityhealthservice", "securityhealthsystray", "msmpeng",
            // Rustos do kernel: nunca podem ser repriorizados por um otimizador de terceiros
            "system", "smss", "csrss", "wininit", "winlogon", "lsass", "services"
        };

        /// <summary>
        /// [FIX:A-4] Um serviço em foreground não é um app interativo. Serviços
        /// de sistema frequentemente "roubam" o foreground durante instalação
        /// (o instalador delega o progresso a um host de serviço), e elevar esse
        /// host é tão perigoso quanto elevar o instalador.
        /// </summary>
        private static bool IsInteractiveApplication(Process p)
        {
            try
            {
                // MainWindowHandle != 0 significa que o processo tem janela de
                // verdade. Serviços e workers headless têm zero.
                return p.MainWindowHandle != IntPtr.Zero;
            }
            catch
            {
                // Processo morreu entre GetProcess e a leitura: tratar como não-interativo.
                return false;
            }
        }

        private void ApplyForegroundBoost()
        {
            try
            {
                var pid = _systemState.ForegroundPid;
                if (pid <= 0 || pid == _ownPid || pid == _lastForegroundPid) return;

                // [FIX:A-4] Guardas de segurança ANTES de reservar o pid em
                // _lastForegroundPid e antes de abrir o handle do processo.
                string procName;
                bool isInteractive;
                using (var p = Process.GetProcessById(pid))
                {
                    procName = p.ProcessName;
                    isInteractive = IsInteractiveApplication(p);
                }

                if (_neverBoostProcesses.Contains(procName))
                {
                    _dlsLogger.Log(
                        $"[FIX:A-4] FG_BOOST_BLOQUEADO: PID={pid} processo='{procName}' e infraestrutura " +
                        $"do Windows — boost de prioridade NAO aplicado (proteger Component Store e " +
                        $"servicos de manutencao)");
                    return;
                }

                if (!isInteractive)
                {
                    _dlsLogger.Log(
                        $"[FIX:A-4] FG_BOOST_BLOQUEADO: PID={pid} processo='{procName}' nao tem janela " +
                        $"(provavelmente servico/worker em foreground) — boost nao aplicado");
                    return;
                }

                _lastForegroundPid = pid;

                IntPtr hProc = ProcessNativeMethods.OpenProcess(
                    ProcessNativeMethods.PROCESS_SET_INFORMATION | ProcessNativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, 
                    false, pid);
                
                if (hProc != IntPtr.Zero)
                {
                    try
                    {
                        uint targetPri = _currentProfile switch
                        {
                            IntelligentProfileType.GamerCompetitive => ProcessNativeMethods.HIGH_PRIORITY_CLASS,
                            _ => ProcessNativeMethods.ABOVE_NORMAL_PRIORITY_CLASS
                        };

                        ProcessNativeMethods.SetPriorityClass(hProc, targetPri);
                        GamerNativeMethods.SetProcessIoPriority(hProc, GamerNativeMethods.IoPriorityLevel.IoPriorityHigh, out _);
                        
                        var pState = new ProcessNativeMethods.PROCESS_POWER_THROTTLING_STATE { Version = 1, ControlMask = 0x01, StateMask = 0x00 };
                        ProcessNativeMethods.SetProcessInformation(hProc, 4, ref pState, Marshal.SizeOf(pState));

                        _dlsLogger.Log($"FG_BOOST: PID={pid} processo='{procName}' priority={targetPri} + HighIO + PowerThrottling=disabled");
                    }
                    finally { ProcessNativeMethods.CloseHandle(hProc); }
                }
            }
            catch (Exception ex)
            {
                _dlsLogger.Log($"FG_BOOST_ERROR: PID={_systemState.ForegroundPid} ex={ex.Message}");
            }
        }

        private void MaintainMemoryPressure()
        {
            if (_systemState.CommitChargePercent < 90) return;

            if ((DateTime.Now - _lastMemoryTrim).TotalMinutes < 2) return;
            _lastMemoryTrim = DateTime.Now;

            try
            {
                _logger.LogWarning($"[DSL] Memória Crítica ({_systemState.CommitChargePercent:F0}%)! Executando contenção de emergência.");
                _dlsLogger.Log($"MEM_CRITICAL: Commit={_systemState.CommitChargePercent:F0}% - trimming standby list");

                // Limpar standby list do Windows (cache de memória que pode ser liberado com segurança)
                // Isso é MUITO mais seguro que EmptyWorkingSet em processos aleatórios
                using var p = new System.Diagnostics.Process();
                p.StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c echo 1 > NUL & powercfg -h off & powercfg -h on",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                };
                p.Start();
                p.WaitForExit(5000);

                // Forçar coleta de lógica .NET no próprio processo
                // REMOVIDO: GC.Collect forçado causa stuttering em jogos
                // GC.Collect(2, GCCollectionMode.Optimized);
                // GC.WaitForPendingFinalizers();

                _dlsLogger.Log($"MEM_CRITICAL: standby trim completed (GC collect disabled)");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[DSL] Erro ao conter pressão de memória: {ex.Message}");
                _dlsLogger.Log($"MEM_ERROR: {ex.Message}");
            }
        }

        private void RestoreAll()
        {
            var processInfos = _processCache?.GetCachedProcessInfos()?.ToArray() ?? Array.Empty<ProcessCacheService.CachedProcessInfo>();
            foreach (var p in processInfos)
            {
                try
                {
                    IntPtr hProc = ProcessNativeMethods.OpenProcess(ProcessNativeMethods.PROCESS_SET_INFORMATION, false, p.Id);
                    if (hProc != IntPtr.Zero)
                    {
                        try
                        {
                            ProcessNativeMethods.SetPriorityClass(hProc, ProcessNativeMethods.NORMAL_PRIORITY_CLASS);
                            GamerNativeMethods.SetProcessIoPriority(hProc, GamerNativeMethods.IoPriorityLevel.IoPriorityNormal, out _);
                        }
                        finally { ProcessNativeMethods.CloseHandle(hProc); }
                    }
                }
                catch { }
            }
        }

        #region Win32 P/Invoke

        [DllImport("psapi.dll")]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenThread(uint dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetThreadPriority(IntPtr hThread, int nPriority);
        #endregion

        public void Dispose()
        {
            Task.Run(() => StopAsync()).GetAwaiter().GetResult();
            _dlsLogger?.Flush();
            _dlsLogger?.Dispose();
            _telemetry?.Dispose();
            _cpuGovernor?.Dispose();
            _ioScheduler?.Dispose();
            _inputLatency?.Dispose();
            _interruptLatency?.Dispose();
            _behaviorEngine?.Dispose();
            _thermalEngine?.Dispose();
            _hardwareDetection?.Dispose();
            _launchAccelerator?.Dispose();
            _logger.LogInfo("[DSL 5.0] DynamicLoadStabilizer disposed.");
        }

        private class NullLogger : ILoggingService
        {
            public void LogInfo(string message) { }
            public void LogSuccess(string message) { }
            public void LogWarning(string message) { }
            public void LogError(string message, Exception? exception = null) { }
            public void LogDebug(string message, string? source = null) { }
            public void LogTrace(string message, string? source = null) { }
            public void LogCritical(string message, Exception? exception = null, string? source = null) { }
            public void Log(LogLevel level, LogCategory category, string message, Exception? exception = null, string? source = null) { }
#pragma warning disable CS0067
            public event EventHandler<string>? LogEntryAdded;
#pragma warning restore CS0067
            public void Flush() { }
            public void ClearLogs() { }
            public void ExportLogs(string filePath) { }
            public string[] GetLogs() => Array.Empty<string>();
            public string GetLogDirectory() => string.Empty;
            public void Dispose() { }
        }
    }
}
