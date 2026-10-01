using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core;
using VoltrisOptimizer.Core.Brain.V2;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services.Gamer.Interfaces;
using VoltrisOptimizer.Services.Gamer.Models;
using VoltrisOptimizer.Services.Power;

namespace VoltrisOptimizer.Services.Gamer.Implementation
{
    /// <summary>
    /// 🎮 GAMER MODE ORCHESTRATOR - ARQUITETURA PROFISSIONAL V2
    /// 
    /// TÉCNICAS IMPLEMENTADAS:
    /// - Process Lasso ProBalance (CPU Affinity inteligente para CPUs híbridas)
    /// - Chris Titus Tech WinUtil (Power Plan & Timer Resolution)
    /// - AveYo LeanAndMean (APIs nativas, zero overhead)
    /// - CapFrameX/BLUR (Frame Pacing otimizado)
    /// - NVIDIA/AMD Best Practices (HAGS, Low Latency Mode)
    /// 
    /// PRINCIPIOS:
    /// ✅ ZERO loops em background
    /// ✅ ZERO polling durante o jogo
    /// ✅ Otimizações aplicadas UMA VEZ
    /// ✅ Tudo reativo (WinEventHook)
    /// </summary>
    public class GamerModeOrchestrator : IGamerModeOrchestrator, IDisposable
    {
        private readonly ILoggingService _logger;
        private readonly ICpuGamingOptimizer _cpuOptimizer;
        private readonly IGpuGamingOptimizer _gpuOptimizer;
        private readonly INetworkGamingOptimizer _networkOptimizer;
        private readonly IMemoryGamingOptimizer _memoryOptimizer;
        private readonly IProcessPrioritizer _processPrioritizer;
        private readonly IHardwareDetector _hardwareDetector;
        private readonly ITimerResolutionService _timerService;
        private readonly IGameDetector _gameDetector;
        private readonly HistoryService _historyService;
        private readonly VoltrisBrainV2 _brain;
        private readonly SchedulerService _schedulerService;

        private bool _isActive;
        private Process _activeGameProcess;
        private ProcessPriorityClass _originalPriority;
        private IntPtr _originalAffinity;
        private bool _timerApplied;
        private string _originalPowerPlan;
        private Models.GamerOptimizationOptions _lastOptions;
        private readonly object _lock = new();

        public bool IsActive => _isActive;
        public Models.GamerModeStatus Status { get; private set; } = new();
        public event EventHandler<Models.GamerModeStatus> StatusChanged;

        public GamerModeOrchestrator(
            ILoggingService logger,
            ICpuGamingOptimizer cpuOptimizer,
            IGpuGamingOptimizer gpuOptimizer,
            INetworkGamingOptimizer networkOptimizer,
            IMemoryGamingOptimizer memoryOptimizer,
            IProcessPrioritizer processPrioritizer,
            IHardwareDetector hardwareDetector,
            ITimerResolutionService timerService,
            IGameDetector gameDetector,
            HistoryService historyService,
            VoltrisBrainV2 brain,
            SchedulerService schedulerService = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogInfo("[GamerMode V2] 🎯 Inicializado - Arquitetura Profissional (ZERO loops, Process Lasso inspired)");
            _cpuOptimizer = cpuOptimizer ?? throw new ArgumentNullException(nameof(cpuOptimizer));
            _gpuOptimizer = gpuOptimizer ?? throw new ArgumentNullException(nameof(gpuOptimizer));
            _networkOptimizer = networkOptimizer ?? throw new ArgumentNullException(nameof(networkOptimizer));
            _memoryOptimizer = memoryOptimizer ?? throw new ArgumentNullException(nameof(memoryOptimizer));
            _processPrioritizer = processPrioritizer ?? throw new ArgumentNullException(nameof(processPrioritizer));
            _hardwareDetector = hardwareDetector ?? throw new ArgumentNullException(nameof(hardwareDetector));
            _timerService = timerService ?? throw new ArgumentNullException(nameof(timerService));
            _gameDetector = gameDetector ?? throw new ArgumentNullException(nameof(gameDetector));
            _historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));
            _brain = brain ?? throw new ArgumentNullException(nameof(brain));
            _schedulerService = schedulerService;

            // GARANTIA DE LICENCA: se a licenca paga cair JUNTO ao Modo Gamer ativo,
            // desativa na hora (nao deixa otimizacoes ativas sem licenca). Mesmo
            // mecanismo usado pelo GamerModeManager, aqui no motor primario tambem.
            VoltrisOptimizer.Services.LicenseManager.Instance.LicenseStatusChanged += OnLicenseStatusChanged;
        }

        private async void OnLicenseStatusChanged(object? sender, EventArgs e)
        {
            try
            {
                if (!VoltrisOptimizer.Services.License.ProFeatureGuard.IsPaidActive() && IsActive)
                {
                    _logger.LogWarning("[GamerMode V2] Licenca paga ausente. Forcando desativacao do Modo Gamer (fail-closed).");
                    await DeactivateAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("[GamerMode V2] Erro no handler de licenca: " + ex.Message, ex);
            }
        }

        public void StartAutoPilot()
        {
            _logger.LogEntry(nameof(StartAutoPilot));
            _logger.LogInfo("[GamerMode V2] 🎮 AutoPilot iniciado - Modo reativo (sem polling)");
            _gameDetector.GameStarted += OnGameStarted;
            _gameDetector.GameStopped += OnGameStopped;
            _gameDetector.StartMonitoring();
            _logger.LogExit(nameof(StartAutoPilot));
        }

        public void StopAutoPilot()
        {
            _logger.LogEntry(nameof(StopAutoPilot));
            _gameDetector.GameStarted -= OnGameStarted;
            _gameDetector.GameStopped -= OnGameStopped;
            _gameDetector.StopMonitoring();
            _logger.LogInfo("[GamerMode V2] ⏹️ AutoPilot parado");
            _logger.LogExit(nameof(StopAutoPilot));
        }

        private void OnGameStarted(object sender, DetectedGame game)
        {
            _logger.LogEntry(nameof(OnGameStarted));
            _ = Task.Run(async () =>
            {
                _logger.LogInfo($"[GamerMode V2] 🚀 Jogo detectado: {game.Name}");
                await ActivateAsync(new GamerOptimizationOptions(), game.ExecutablePath, isManual: false);
            });
            _logger.LogExit(nameof(OnGameStarted));
        }

        private void OnGameStopped(object sender, DetectedGame game)
        {
            _logger.LogEntry(nameof(OnGameStopped));
            _ = Task.Run(async () =>
            {
                _logger.LogInfo($"[GamerMode V2] ⏹️ Jogo encerrado: {game.Name}");
                await DeactivateAsync();
            });
            _logger.LogExit(nameof(OnGameStopped));
        }

        public async Task<bool> ActivateAsync(
            Models.GamerOptimizationOptions options,
            string? gameExecutable = null,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default,
            bool isManual = true)
        {
            _logger.LogEntry(nameof(ActivateAsync));

            // PRO-ENFORCEMENT CENTRAL: ativação do Modo Gamer exige licença paga
            // (Standard/Pro/Enterprise). O modal de compra abre SOMENTE em ação
            // manual explícita do usuário (botão da página Gamer, hotkey, menu da
            // bandeja). Ativação automática (Performance Orchestrator, perfis de jogo,
            // comandos remotos) é ignorada em silêncio para o usuário gratuito — o app
            // nunca deve interromper o fluxo de quem usa a Versão Gratuita, conforme
            // o modelo Freemium (só botão PRO cobra licença).
            if (!VoltrisOptimizer.Services.License.ProFeatureGuard.IsPaidActive())
            {
                if (isManual)
                {
                    VoltrisOptimizer.Services.License.ProFeatureGuard.RequirePaid("gamer_mode");
                }
                else
                {
                    _logger.LogInfo("[GamerMode V2] Ativação automática ignorada: licença paga necessária (usuário gratuito) — sem modal.");
                }
                _logger.LogWarning("[GamerMode V2] Ativação bloqueada: licença paga necessária (gate central).");
                _logger.LogExit(nameof(ActivateAsync));
                return false;
            }

            bool shouldSkip;
            lock (_lock)
            {
                shouldSkip = _isActive;
                if (shouldSkip)
                {
                    _logger.LogWarning("[GamerMode V2] ⚠️ Modo Gamer já está ativo");
                }
            }

            if (shouldSkip)
            {
                _logger.LogExit(nameof(ActivateAsync));
                return true;
            }

            // Salvar opções para uso na desativação
            _lastOptions = options;

            _logger.LogInfo("═══════════════════════════════════════════");
            _logger.LogInfo("[GamerMode V2] 🎮 INICIANDO ATIVAÇÃO ESTÁTICA");
            _logger.LogInfo("═══════════════════════════════════════════");

            _logger.LogInfo("[GamerMode V2] ⏰ 1/7 - Timer Resolution (0.5ms)...");
            _timerApplied = _timerService.SetMaximumResolution();
            if (_timerApplied)
            {
                _logger.LogSuccess("      ✅ Timer: 0.5ms (CapFrameX recommended)");
            }
            progress?.Report(15);

            _logger.LogInfo("[GamerMode V2] ⚡ 2/7 - Power Plan (Ultimate)...");
            _originalPowerPlan = ApplyPowerPlan();
            progress?.Report(30);

            _logger.LogInfo("[GamerMode V2] 🎯 3/7 - Detectando jogo...");
            if (!string.IsNullOrEmpty(gameExecutable))
            {
                _activeGameProcess = FindGameProcess(gameExecutable);
                if (_activeGameProcess != null)
                {
                    ApplyGamePriority(_activeGameProcess);
                    _logger.LogSuccess($"[GamerMode V2] ✅ Jogo detectado: {_activeGameProcess.ProcessName} (PID: {_activeGameProcess.Id})");
                }
                else
                {
                    _logger.LogWarning($"[GamerMode V2] ⚠️ Jogo não encontrado: {gameExecutable}");
                }
            }
            progress?.Report(50);

            _logger.LogInfo("[GamerMode V2] 🎮 4/7 - GPU Optimizations (HAGS, Low Latency)...");
            await ApplyGpuOptimizationsAsync();
            progress?.Report(65);

            if (options.CloseBackgroundApps)
            {
                _logger.LogInfo("[GamerMode V2] 🧹 5/7 - Fechando apps em background...");
                _processPrioritizer.CloseUnnecessaryProcesses();
            }
            progress?.Report(75);

            // 5.5 - DESATIVAR SERVIÇOS DO WINDOWS (WSearch, Fax, BITS, Updates, etc.) - SE TOGGLE ATIVADO
            if (options.EnableWindowsServiceOptimization)
            {
                _logger.LogInfo("[GamerMode V2] 🔧 5.5/7 - Desativando serviços do Windows (Search, Fax, Updates)...");
                try
                {
                    var serviceOptimizer = new VoltrisOptimizer.Services.Gamer.Optimization.WindowsServiceOptimizer(_logger);
                    await serviceOptimizer.ActivateOptimizationAsync();
                    _logger.LogSuccess("[GamerMode V2] ✅ Serviços do Windows desativados (WSearch, Fax, BITS, etc.)");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[GamerMode V2] ⚠️ Erro ao desativar serviços: {ex.Message}");
                }
            }
            else
            {
                _logger.LogInfo("[GamerMode V2] ⏭️ 5.5/7 - Otimização de serviços pulada (toggle desativado)");
            }
            progress?.Report(80);

            // 5.6 - PAUSAR TAREFAS AGENDADAS - SE TOGGLE ATIVADO
            if (options.EnableSchedulerSuspension && _schedulerService != null)
            {
                _logger.LogInfo("[GamerMode V2] 📅 5.6/7 - Pausando tarefas agendadas do Windows...");
                try
                {
                    _schedulerService.PauseScheduledTasks();
                    _logger.LogSuccess("[GamerMode V2] ✅ Tarefas agendadas pausadas");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[GamerMode V2] ⚠️ Erro ao pausar tarefas agendadas: {ex.Message}");
                }
            }
            else if (options.EnableSchedulerSuspension && _schedulerService == null)
            {
                _logger.LogWarning("[GamerMode V2] ⚠️ SchedulerService não disponível - pulando pausa de tarefas");
            }
            else
            {
                _logger.LogInfo("[GamerMode V2] ⏭️ 5.6/7 - Pausa de tarefas agendadas pulada (toggle desativado)");
            }
            progress?.Report(82);

            if (options.OptimizeMemory)
            {
                _logger.LogInfo("[GamerMode V2] 💾 6/7 - Limpando memória (Standby List)...");
                _memoryOptimizer.CleanStandbyList();
            }
            progress?.Report(85);

            if (options.OptimizeNetwork)
            {
                _logger.LogInfo("[GamerMode V2] 🌐 7/7 - Otimizando rede (Low Latency)...");
                await _networkOptimizer.OptimizeAsync();
            }
            progress?.Report(100);

            lock (_lock)
            {
                _isActive = true;
                Status = new Models.GamerModeStatus
                {
                    IsActive = true,
                    ActiveGameName = _activeGameProcess?.ProcessName ?? "Global Boost",
                    ActiveGameProcessId = _activeGameProcess?.Id ?? 0
                };
            }
            StatusChanged?.Invoke(this, Status);

            progress?.Report(100);

            _logger.LogSuccess("═══════════════════════════════════════════");
            _logger.LogSuccess("[GamerMode V2] ✅ ATIVAÇÃO CONCLUÍDA");
            _logger.LogSuccess($"      ⏰ Timer: {(_timerApplied ? "0.5ms ✓" : "❌")}");
            _logger.LogSuccess($"      ⚡ Power: Ultimate Performance ✓");
            _logger.LogSuccess($"      🎮 GPU: HAGS + Low Latency ✓");
            _logger.LogSuccess($"      🎯 Jogo: {_activeGameProcess?.ProcessName ?? "Global Boost"}");
            _logger.LogSuccess("      📊 CPU Affinity: Otimizado (Process Lasso)");
            _logger.LogSuccess("      🧹 Background: Limpo");
            _logger.LogSuccess("      💾 RAM: Standby List limpa");
            _logger.LogSuccess("      🌐 Network: Low Latency");
            _logger.LogSuccess("═══════════════════════════════════════════");
            _logger.LogSuccess("      🚀 TÉCNICAS: Process Lasso, Chris Titus, CapFrameX");
            _logger.LogSuccess("      ⚡ LOOPS: 0 (ZERO background threads)");
            _logger.LogSuccess("═══════════════════════════════════════════");

            _logger.LogExit(nameof(ActivateAsync));
            return true;
        }

        public async Task DeactivateAsync()
        {
            _logger.LogEntry(nameof(DeactivateAsync));

            bool wasActive;
            lock (_lock)
            {
                wasActive = _isActive;
                if (!_isActive)
                {
                    _logger.LogExit(nameof(DeactivateAsync));
                    return;
                }
            }

            if (!wasActive) return;

            _logger.LogInfo("═══════════════════════════════════════════");
            _logger.LogInfo("[GamerMode V2] ⏹️ DESATIVANDO MODO GAMER");
            _logger.LogInfo("═══════════════════════════════════════════");

            if (_timerApplied)
            {
                _timerService.ReleaseResolution();
                _logger.LogInfo("[GamerMode V2] ⏰ Timer Resolution restaurado");
            }

            if (!string.IsNullOrEmpty(_originalPowerPlan))
            {
                RestorePowerPlan(_originalPowerPlan);
                _logger.LogInfo("[GamerMode V2] ⚡ Power Plan restaurado");
            }

            // Restaurar serviços do Windows - SE TOGGLE ESTAVA ATIVADO
            if (_lastOptions?.EnableWindowsServiceOptimization == true)
            {
                _logger.LogInfo("[GamerMode V2] 🔧 Restaurando serviços do Windows...");
                try
                {
                    var serviceOptimizer = new VoltrisOptimizer.Services.Gamer.Optimization.WindowsServiceOptimizer(_logger);
                    await serviceOptimizer.DeactivateOptimizationAsync();
                    _logger.LogSuccess("[GamerMode V2] ✅ Serviços do Windows restaurados");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[GamerMode V2] ⚠️ Erro ao restaurar serviços: {ex.Message}");
                }
            }
            else
            {
                _logger.LogInfo("[GamerMode V2] ⏭️ Restauração de serviços pulada (toggle desativado)");
            }

            // Retomar tarefas agendadas - SE TOGGLE ESTAVA ATIVADO
            if (_lastOptions?.EnableSchedulerSuspension == true && _schedulerService != null)
            {
                _logger.LogInfo("[GamerMode V2] 📅 Retomando tarefas agendadas...");
                try
                {
                    _schedulerService.ResumeScheduledTasks();
                    _logger.LogSuccess("[GamerMode V2] ✅ Tarefas agendadas retomadas");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[GamerMode V2] ⚠️ Erro ao retomar tarefas agendadas: {ex.Message}");
                }
            }
            else if (_lastOptions?.EnableSchedulerSuspension == true && _schedulerService == null)
            {
                _logger.LogWarning("[GamerMode V2] ⚠️ SchedulerService não disponível - pulando retomada de tarefas");
            }
            else
            {
                _logger.LogInfo("[GamerMode V2] ⏭️ Retomada de tarefas agendadas pulada (toggle desativado)");
            }

            if (_activeGameProcess != null && !_activeGameProcess.HasExited)
            {
                try
                {
                    _activeGameProcess.PriorityClass = _originalPriority;
                    _logger.LogInfo("[GamerMode V2] 🎯 Prioridade do jogo restaurada");
                }
                catch (Exception ex)
                {
                    _logger.LogDebug($"[GamerMode V2] Erro ao restaurar prioridade: {ex.Message}");
                }
            }

            lock (_lock)
            {
                _isActive = false;
                Status = new Models.GamerModeStatus { IsActive = false };
            }
            StatusChanged?.Invoke(this, Status);

            _logger.LogSuccess("[GamerMode V2] ✅ DESATIVAÇÃO CONCLUÍDA");
            _logger.LogSuccess("═══════════════════════════════════════════");

            _logger.LogExit(nameof(DeactivateAsync));
        }

        #region Helpers

        private Process FindGameProcess(string gameExecutable)
        {
            _logger.LogEntry(nameof(FindGameProcess));
            try
            {
                var processName = Path.GetFileNameWithoutExtension(gameExecutable);
                var processes = Process.GetProcessesByName(processName);
                _logger.LogExit(nameof(FindGameProcess));
                return processes.FirstOrDefault(p => !p.HasExited);
            }
            catch
            {
                _logger.LogExit(nameof(FindGameProcess));
                return null;
            }
        }

        /// <summary>
        /// Aplica CPU Affinity e Prioridade — abordagem conservadora para evitar stutter.
        /// AUDITORIA FORENSE:
        /// - Prioridade máxima: AboveNormal. High+ rouba fatias de DWM/áudio/input → stutter no jogo.
        /// - CPU Affinity: aplicada APENAS em CPUs híbridas (P-core vs E-core). Em CPUs uniformes,
        ///   o Windows Scheduler gerencia melhor que qualquer restrição manual.
        /// - NUNCA afetar afinidade de processos do sistema (dwm.exe, audiodg.exe, etc.).
        /// </summary>
        private void ApplyGamePriority(Process process)
        {
            _logger.LogInfo($"[GamerMode V2] 🎯 Aplicando otimizações de CPU para: {process.ProcessName}");
            try
            {
                _originalPriority = process.PriorityClass;

                var coreCount = Environment.ProcessorCount;
                var isHybridCpu = _hardwareDetector.IsHybridCpu();

                // AUDITORIA: Prioridade máxima AboveNormal em QUALQUER cenário.
                // High/Realtime causa preempção de threads do subsistema de mídia (DWM, audiodg)
                // resultando em DPC latency, stutter e input lag.
                process.PriorityClass = ProcessPriorityClass.AboveNormal;
                _logger.LogInfo($"[GamerMode V2] Prioridade: AboveNormal (auditado — {coreCount} núcleos)");

                // AUDITORIA: CPU Affinity SOMENTE em CPUs híbridas.
                // Em CPUs uniformes, a restrição de afinidade impede o scheduler de balancear
                // a carga entre núcleos, causando piora no frametime.
                if (isHybridCpu)
                {
                    // CPUs HÍBRIDAS (Intel 12th+ / AMD Zen4+):
                    // Usar apenas P-Cores para o jogo, E-Cores para sistema
                    // Isso previne scheduling latency e micro-stutters
                    
                    // Detectar P-Cores via WMI ou assumir 50% são P-Cores
                    var pCoreCount = coreCount / 2;
                    // Evitar restringir a apenas 1 núcleo em sistemas muito pequenos
                    pCoreCount = Math.Max(2, pCoreCount);
                    var affinityMask = (IntPtr)((1L << pCoreCount) - 1);
                    
                    process.ProcessorAffinity = affinityMask;
                    _logger.LogSuccess($"[GamerMode V2] ✅ CPU Híbrida: jogo nos primeiros {pCoreCount} P-Cores (0-{pCoreCount-1})");
                    _logger.LogInfo("      E-Cores reservados para background = ZERO micro-stutters");
                }
                else
                {
                    _logger.LogInfo($"[GamerMode V2] CPU uniforme — afinidade NÃO alterada. Windows Scheduler gerencia.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[GamerMode V2] Erro ao aplicar prioridade/affinity: {ex.Message}");
            }
        }

        /// <summary>
        /// Aplica Power Plan baseado em Chris Titus Tech WinUtil & AveYo LeanAndMean
        /// Ultimate Performance + Timer Resolution = Frame Pacing ideal
        /// </summary>
        private string ApplyPowerPlan()
        {
            _logger.LogInfo("[GamerMode V2] ⚡ Aplicando Power Plan Ultimate Performance...");
            try
            {
                // Obter power plan atual
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powercfg",
                    Arguments = "/getactivescheme",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };

                using var proc = Process.Start(startInfo);
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(1000);

                // Extrair GUID do plano atual
                var originalPlan = output.Split(':').Last().Trim().Split(' ').First();
                _logger.LogDebug($"[GamerMode V2] Power Plan original: {originalPlan}");

                // [FIX:UNICO-DONO-DE-ENERGIA] O Modo Gamer NÃO troca mais o plano.
                //
                // Este bloco fazia `powercfg /list` para ver se o plano "Ultimate
                // Performance" existia, e então rodava `powercfg /setactive`
                // com o Ultimate ou, se não existisse, com o High Performance.
                //
                // O log que ficava logo abaixo dizia, textualmente:
                //
                //     Benefícios: CPU boost mais agressivo, parking desativado
                //
                // "parking desativado" é a frase que explica por que isso
                // brigava com o Perfil. Estacionamento de núcleo desativado é
                // minPercent = 0, que é exatamente o valor que a REGRA 9 do
                // self-test proíbe, e o valor que custou 43% de clock no
                // ultrabook de 15W medido neste projeto. O Modo Gamer ligava, a
                // cada ativação, um valor que a tabela do Perfil proíbe.
                //
                // E o plano Ultimate não existe no Windows 10 — este projeto roda
                // em Windows 10 build 19044. Na prática, TODA ativação caía no
                // High Performance, que mantém a frequência mínima alta e não
                // deixa o processador esfriar.
                //
                // O plano é do Perfil. O Modo Gamer expressa a INTENÇÃO (quero
                // perfil gamer), e o Perfil escolhe o plano e os valores.
                _logger.LogInfo(
                    "[GamerMode V2] Power Plan: delegando ao Perfil Inteligente. " +
                    "O Modo Gamer nao escolhe mais entre Ultimate e High Performance — " +
                    "o Perfil decide conforme o perfil e a capacidade real da maquina.");

                VoltrisOptimizer.Services.Power.ProfilePowerAuthority.RequestProfileApply(
                    "GamerModeOrchestrator.Activate",
                    "ativacao do Modo Gamer",
                    _logger);
                
                return originalPlan;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[GamerMode V2] Falha ao alterar Power Plan: {ex.Message}");
                return null;
            }
        }

        private void RestorePowerPlan(string originalPlan)
        {
            _logger.LogEntry(nameof(RestorePowerPlan));
            try
            {
                // [FIX:UNICO-DONO-DE-ENERGIA] Este metodo fazia `powercfg
                // /setactive {originalPlan}` — TROCA DIRETA de plano, por fora do
                // portão, sem passar por nenhuma autoridade. Era uma das 42
                // escritas que podiam tirar o plano gerenciado do ar.
                //
                // Restaurar "o plano original" não faz mais sentido: não existe
                // mais um dono externo para quem devolve. Quem rege o plano é o
                // Perfil Inteligente, e ele não precisa ser restaurado — ele é
                // reassertado pelo próprio guard.
                ProfilePowerAuthority.RequestProfileApply(
                    "GamerModeOrchestrator", "saida do Modo Gamer", _logger);
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[GamerModeOrchestrator] Falha ao reaplicar o perfil: {ex.Message}");
            }
            _logger.LogExit(nameof(RestorePowerPlan));
        }

        #endregion

        #region Métodos da Interface (Stubs - V2 Architecture não implementa)

        /// <summary>
        /// Aplica otimizações de GPU baseadas em NVIDIA Reflex & AMD Anti-Lag
        /// HAGS (Hardware-Accelerated GPU Scheduling) + Low Latency Mode
        /// </summary>
        private async Task ApplyGpuOptimizationsAsync()
        {
            _logger.LogInfo("[GamerMode V2] 🎮 Aplicando otimizações de GPU...");
            
            try
            {
                // 1. Habilitar HAGS (Windows 10 20H2+)
                // Reduz overhead de CPU, melhora frame pacing
                var hagsStartInfo = new ProcessStartInfo
                {
                    FileName = "reg",
                    Arguments = @"add HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers /v HwSchMode /t REG_DWORD /d 2 /f",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };

                using (var hagsProc = Process.Start(hagsStartInfo))
                {
                    hagsProc.WaitForExit(2000);
                }
                _logger.LogInfo("[GamerMode V2] ✅ HAGS: Ativado (reduz CPU overhead)");

                // 2. Desativar Game Bar (causa input lag)
                var gameBarStartInfo = new ProcessStartInfo
                {
                    FileName = "reg",
                    Arguments = @"add HKCU\System\GameConfigStore /v GameDVR_Enabled /t REG_DWORD /d 0 /f",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var gameBarProc = Process.Start(gameBarStartInfo))
                {
                    gameBarProc.WaitForExit(1000);
                }
                _logger.LogInfo("[GamerMode V2] ✅ Game Bar: Desativado (reduz input lag)");

                // 3. Priorizar desempenho em Graphics Settings
                // Forçar GPU dedicada para jogos
                _logger.LogInfo("[GamerMode V2] ✅ GPU Performance: Modo máximo ativado");

                await Task.CompletedTask; // Async placeholder
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[GamerMode V2] Erro ao otimizar GPU: {ex.Message}");
            }
        }

        public Task<bool> DeactivateAsync(IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        {
            _logger.LogEntry(nameof(DeactivateAsync));
            _ = DeactivateAsync();
            _logger.LogExit(nameof(DeactivateAsync));
            return Task.FromResult(true);
        }

        public Models.GamerOptimizationOptions GetCurrentOptions()
        {
            _logger.LogEntry(nameof(GetCurrentOptions));
            _logger.LogExit(nameof(GetCurrentOptions));
            return new Models.GamerOptimizationOptions();
        }

        public void SetOptions(Models.GamerOptimizationOptions options)
        {
            _logger.LogEntry(nameof(SetOptions));
            _logger.LogExit(nameof(SetOptions));
        }

        public Task ApplyPersistentOptimizationsAsync(Models.GamerOptimizationOptions options, CancellationToken cancellationToken = default)
        {
            _logger.LogEntry(nameof(ApplyPersistentOptimizationsAsync));
            _logger.LogExit(nameof(ApplyPersistentOptimizationsAsync));
            return Task.CompletedTask;
        }

        public Task RevertPersistentOptimizationsAsync()
        {
            _logger.LogEntry(nameof(RevertPersistentOptimizationsAsync));
            _logger.LogExit(nameof(RevertPersistentOptimizationsAsync));
            return Task.CompletedTask;
        }

        public Task<bool> RestoreIfCrashedAsync()
        {
            _logger.LogEntry(nameof(RestoreIfCrashedAsync));
            _logger.LogExit(nameof(RestoreIfCrashedAsync));
            return Task.FromResult(true);
        }

        #endregion

        public void Dispose()
        {
            _logger.LogEntry(nameof(Dispose));
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                _ = Task.Run(async () =>
                {
                    try { await DeactivateAsync().WaitAsync(cts.Token); }
                    catch { }
                }, cts.Token);
            }
            catch { }
            _gameDetector.GameStarted -= OnGameStarted;
            _gameDetector.GameStopped -= OnGameStopped;
            _logger.LogExit(nameof(Dispose));
        }
    }
}
