using System;
using System.Collections.Concurrent;
using System.Linq;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Gamer.Interfaces;
using VoltrisOptimizer.Services.SystemContext;

namespace VoltrisOptimizer.Services.Optimization.Unification
{
    public class OptimizationOrchestrator
    {
        private readonly ILoggingService _logger;
        public SystemBehaviorStateMachine StateMachine { get; }
        public TimerResolutionManager TimerManager { get; }
        public AffinityDirector Affinity { get; }
        public EcoQoSController EcoQoS { get; }
        public BurstAccelerationEngine BurstEngine { get; }
        public IntelligentDecisionEngine DecisionEngine { get; }
        private readonly IGameDetector _gameDetector;

        public OptimizationOrchestrator(
            ILoggingService logger, 
            IntelligentDecisionEngine decisionEngine,
            IHardwareContextSingleton hardwareContext,
            IGameDetector gameDetector,
            ITimerResolutionService timerResolutionService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            DecisionEngine = decisionEngine ?? throw new ArgumentNullException(nameof(decisionEngine));
            _gameDetector = gameDetector ?? throw new ArgumentNullException(nameof(gameDetector));
            
            StateMachine = new SystemBehaviorStateMachine(logger);
            TimerManager = new TimerResolutionManager(logger, timerResolutionService);
            Affinity = new AffinityDirector(logger);
            EcoQoS = new EcoQoSController(logger);
            BurstEngine = new BurstAccelerationEngine(logger, TimerManager, this, DecisionEngine);

            StateMachine.StateChanged += OnStateChanged;
            
            // Ativa os subsistemas de forma segura
            BurstEngine.Start();
        }

        private void OnStateChanged(object? sender, SystemState newState)
        {
            _logger.LogInfo($"[Orchestrator] Estado global alterado para: {newState}");
            if (newState == SystemState.Idle)
            {
                // Limpar todas as otimizações pesadas
                TimerManager.ForceDefault();
            }
        }

        public bool IsGame(string processName)
        {
            bool isGame = _gameDetector.IsKnownGame(processName);
            if (isGame) _logger.LogDebug($"[Orchestrator] Processo identificado como jogo: {processName}");
            return isGame;
        }

        public void RestoreSafeState()
        {
            _logger.LogWarning($"[Orchestrator] 🚨 Restaurando estado seguro (WatchDog / Fail-safe)");
            StateMachine.TransitionTo(SystemState.Interactive, "RestoreSafeState requested");
            TimerManager.ForceDefault();
            _logger.LogInfo("[Orchestrator] Estado seguro restaurado com sucesso.");
        }

        public void ProcessDlsRecommendation(int pid, string action, bool applyEcoQos)
        {
            _logger.LogDebug($"[Orchestrator] DLS Recommendation: PID={pid}, Action={action}, ApplyEcoQoS={applyEcoQos}");
            
            if (StateMachine.CurrentState == SystemState.BatteryMode) 
            {
                _logger.LogInfo($"[Orchestrator] DLS bloqueado (Modo Bateria ativo). PID={pid}");
                return; 
            }

            if (action == "THROTTLE")
            {
                if (StateMachine.CurrentState == SystemState.Gaming || StateMachine.CurrentState == SystemState.HeavyApp)
                {
                    if (applyEcoQos) 
                    {
                        _logger.LogInfo($"[Orchestrator] Aplicando EcoQoS (Throttle) via DLS no PID: {pid}");
                        EcoQoS.ApplyEcoQoS(pid);
                    }
                }
            }
            else if (action == "RELEASE")
            {
                _logger.LogInfo($"[Orchestrator] Liberando processo (Release) via DLS no PID: {pid}");
                EcoQoS.RemoveEcoQoS(pid);
            }
        }
        
        public void VerifyProcessState(int pid)
        {
            // Verificação atômica se um processo em focus tentou ser asfixiado por DLS antigo
            if (StateMachine.CurrentState == SystemState.Interactive)
            {
                _logger.LogTrace($"[Orchestrator] Verificando estado do processo em foco (PID: {pid}). Removendo possíveis asfixias de EcoQoS.");
                EcoQoS.RemoveEcoQoS(pid);
            }
        }
    }
}
