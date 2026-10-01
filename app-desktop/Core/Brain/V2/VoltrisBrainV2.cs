using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.Body;
using VoltrisOptimizer.Core.Intelligence;
using VoltrisOptimizer.Core.Intelligence.Heuristics.Abstractions;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Intelligence.VPIS.Engines;
using VoltrisOptimizer.Models;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Core.Brain.V2;

public sealed class VoltrisBrainV2 : IDisposable, IAsyncDisposable
{
	public enum PowerProfileKind
	{
		BatterySaver,
		Balanced,
		HighPerformance,
		UltraPerformance
	}

	public enum OptimizationIntent
	{
		Quick,
		Standard,
		Aggressive,
		ThermalRelief
	}

	public enum ProfileType
	{
		Economic,
		Balanced,
		Performance,
		Gamer
	}

	public sealed class BrainRequestResult
	{
		public bool Success { get; init; }

		public string Reason { get; init; } = string.Empty;


		public IReadOnlyList<string> ExecutedActions { get; init; } = Array.Empty<string>();


		public DateTime Timestamp { get; init; } = DateTime.UtcNow;

	}

	private sealed class ActiveProfileState
	{
		public ProfileType Profile { get; set; }

		public DateTime SavedUtc { get; set; }
	}

	private readonly ILoggingService _logger;

	private readonly IBrainSensor _sensor;

	private readonly IBrainDecision _decision;

	private readonly IBrainExecutor _executor;

	private readonly BrainContextMemory _memory;

	private readonly BrainSessionStats _stats;

	private readonly IBehaviorScoreEngine _behavior;

	private readonly IPatternRecognitionService _patternRecognition;

	private readonly ILearningLogger _learningDb;

	private readonly StutterPreventionModule _stutter;

	private readonly BrainStateOrchestrator _stateOrchestrator;

	private IVoltrisBody? _body;

	private CancellationTokenSource? _cts;

	private Task? _loop;

	private Task? _autosaveTask;

	private volatile bool _running;

	private readonly object _brainStateLock = new();
	private SensorSnapshot? _previousSnapshot;

	private BrainStateKey _previousState;

	private BrainActionV2? _previousAction;

	private bool _hasPrevious;

	private bool _thermalUnavailableLogged;

	private int _decisionCycleCounter;

	private int _idleCycleSkipCounter;

	private WorkloadCategory _lastThrottledWorkload;

	private int _executedActionsObsCounter;

private static readonly TimeSpan DecisionInterval = TimeSpan.FromSeconds(10.0); // [OPTIMIZED] OTIMIZAÇÃO: De 3s para 10s (reduz 70% do overhead)
	
	private static readonly TimeSpan AutosaveInterval = TimeSpan.FromMinutes(10.0); // [OPTIMIZED] OTIMIZAÇÃO: De 5min para 10min
	
	private const int GcIntervalCycles = 20; // [OPTIMIZED] OTIMIZAÇÃO: De 10 para 20 (GC menos frequente)

	private const int MaxIdleSkipsBeforeForceGc = 5;

	private DateTime _lastPriorityDispatch = DateTime.MinValue;
	private DateTime _lastExternalApiCall = DateTime.MinValue;

	private static readonly TimeSpan PriorityDispatchCooldown = TimeSpan.FromSeconds(25.0);
	private static readonly TimeSpan ExternalApiCooldown = TimeSpan.FromMinutes(5.0);

	private static readonly string _activeProfilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Voltris", "Brain", "active_profile.json");

	private static readonly string _legacyActiveProfilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Voltris", "active_profile.json");

	public BrainStateKey CurrentState { get; private set; }

	public BrainActionV2? LastAction { get; private set; }

	public double LastReward { get; private set; }

	public string CurrentRecommendation { get; private set; } = "Aguardando telemetria";


	public double SessionImprovementScore => _stats.TotalReward;

	public bool IsRunning => _running;

	public BrainSessionStats Stats => _stats;

	public BrainContextMemory Memory => _memory;

	public IBrainDecision Decision => _decision;

	public BrainStateOrchestrator Orchestrator => _stateOrchestrator;

	public IVoltrisBody? Body
	{
		get
		{
			return _body;
		}
		set
		{
			_body = value;
		}
	}

	public IBrainExecutor Executor => _executor;

	public ProfileType ActiveProfile { get; private set; } = ProfileType.Balanced;


	public int ProfileMinEpp { get; private set; } = 25;


	public int ProfileMaxEpp { get; private set; } = 75;


	public int ProfileSystemResponsiveness { get; private set; } = 20;


	public string ProfilePowerPlanGuid { get; private set; } = "381b4222-f694-41f0-9685-ff5bb260df2e";


	public event EventHandler<BrainDecisionReadyEventArgs>? OnDecisionReady;

	public event EventHandler<BrainActionResultV2>? ActionExecuted;

	private (double cpu, long availRamMb) CaptureSystemPulse()
	{
		SensorSnapshot currentSnapshot = _sensor.CurrentSnapshot;
		if (currentSnapshot != null)
		{
			return (currentSnapshot.CpuUsagePercent, currentSnapshot.AvailableRam / 1024 / 1024);
		}
		return (0.0, 0L);
	}

	private async Task<BrainActionResultV2> ExecuteViaBodyAsync(BrainDecision decision, SensorSnapshot snap)
	{
		if (_body == null)
		{
			return await _executor.ExecuteAsync(ConvertActionFromDecision(decision, snap), snap);
		}
		this.OnDecisionReady?.Invoke(this, new BrainDecisionReadyEventArgs
		{
			Decision = decision,
			Snapshot = snap
		});
		return await _executor.ExecuteAsync(ConvertActionFromDecision(decision, snap), snap);
	}

	private static BrainActionV2 ConvertActionFromDecision(BrainDecision decision, SensorSnapshot snap)
	{
		int param = decision.ActionValue;
		BrainActionKind kind = decision.ActionType switch
		{
			DecisionActionType.SetEpp => BrainActionKind.SetEpp,
			DecisionActionType.SetProcessPriority => BrainActionKind.SetForegroundPriority,
			DecisionActionType.SetSystemResponsiveness => BrainActionKind.SetSystemResponsiveness,
			DecisionActionType.EnableGamingMode => BrainActionKind.EnableGamingMode,
			DecisionActionType.TrimWorkingSet => BrainActionKind.TrimWorkingSet,
			_ => BrainActionKind.SetEpp};
		return new BrainActionV2
		{
			Kind = kind,
			Param = param,
			TargetPid = snap.ForegroundPid > 4 ? snap.ForegroundPid : null,
			TargetProcessName = snap.ForegroundProcessName,
			Reason = decision.Justification ?? "body-dispatch"
		};
	}

	private byte GetContextBucketFromBody()
	{
		if (_body == null)
		{
			return 0;
		}
		OperationalContext currentContext = _body!.CurrentContext;

		byte result = currentContext switch
		{
			OperationalContext.Idle => 0, 
			OperationalContext.Work => 1, 
			OperationalContext.Gaming => 2, 
			OperationalContext.Streaming => 3, 
			OperationalContext.ThermalCrisis => 4, 
			_ => 0};

		return result;
	}

	private BrainDecision ConvertToBrainDecision(BrainActionV2 action, SensorSnapshot snap)
	{
		BrainActionKind kind = action.Kind;
		DecisionActionType actionType = kind switch
		{
			BrainActionKind.SetEpp => DecisionActionType.SetEpp, 
			BrainActionKind.SetForegroundPriority => DecisionActionType.SetProcessPriority, 
			BrainActionKind.SetSystemResponsiveness => DecisionActionType.SetSystemResponsiveness, 
			BrainActionKind.EnableGamingMode => DecisionActionType.EnableGamingMode, 
			BrainActionKind.TrimWorkingSet => DecisionActionType.TrimWorkingSet, 
			_ => DecisionActionType.None};
		string target;
		if (action.Kind != BrainActionKind.SetForegroundPriority)
		{
			target = action.TargetProcessName;
		}
		else
		{
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(1, 2);
			defaultInterpolatedStringHandler.AppendFormatted(action.TargetPid.GetValueOrDefault());
			defaultInterpolatedStringHandler.AppendLiteral(":");
			defaultInterpolatedStringHandler.AppendFormatted(action.TargetProcessName);
			target = defaultInterpolatedStringHandler.ToStringAndClear();
		}
		BrainDecision brainDecision = new BrainDecision
		{
			ActionType = actionType,
			ActionValue = action.Param,
			Target = target,
			ContextSnapshot = snap,
			Justification = action.Reason,
			Confidence = 1.0,
			CanExecuteImmediate = true
		};
		return brainDecision;
	}

	public VoltrisBrainV2(ILoggingService logger, IBehaviorScoreEngine behavior, IBrainExecutor executor, IBrainSensor sensor, IPatternRecognitionService patternRecognition, IHeuristicsPipeline heuristics, IEnumerable<IVpisDiagnosticEngine> vpisEngines, ILearningLogger learningDb)
	{
		_logger = logger ?? throw new ArgumentNullException("logger");
		_behavior = behavior ?? throw new ArgumentNullException("behavior");
		_executor = executor ?? throw new ArgumentNullException("executor");
		_sensor = sensor ?? throw new ArgumentNullException("sensor");
		_patternRecognition = patternRecognition ?? throw new ArgumentNullException("patternRecognition");
		_learningDb = learningDb ?? throw new ArgumentNullException("learningDb");
		_decision = new BrainDecisionEngineV2(logger, patternRecognition, heuristics, vpisEngines);
		_memory = new BrainContextMemory(logger);
		_stats = new BrainSessionStats(logger);
		_stutter = new StutterPreventionModule(logger, _executor);
		_stateOrchestrator = new BrainStateOrchestrator(logger);
	}

	public async Task StartAsync(CancellationToken ct = default(CancellationToken))
	{
		_logger.LogEntry(nameof(StartAsync));
		if (!_running)
		{
			_running = true;
			_cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
			await Task.WhenAll(_decision.LoadAsync(), _memory.LoadAsync()).ConfigureAwait(continueOnCapturedContext: false);
			RestoreActiveProfile();
			ILoggingService logger = _logger;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(95, 4);
			defaultInterpolatedStringHandler.AppendLiteral("[BRAIN] VoltrisBrain v2 iniciado | Q-Table: ");
			defaultInterpolatedStringHandler.AppendFormatted(_decision.KnownStates);
			defaultInterpolatedStringHandler.AppendLiteral(" estados | Perfis: ");
			defaultInterpolatedStringHandler.AppendFormatted(_memory.Count);
			defaultInterpolatedStringHandler.AppendLiteral(" processos | ε = ");
			defaultInterpolatedStringHandler.AppendFormatted(_decision.Epsilon, "F3");
			defaultInterpolatedStringHandler.AppendLiteral(" | episódios = ");
			defaultInterpolatedStringHandler.AppendFormatted(_decision.Episodes);
			logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(34, 3);
			defaultInterpolatedStringHandler.AppendLiteral("states = ");
			defaultInterpolatedStringHandler.AppendFormatted(_decision.KnownStates);
			defaultInterpolatedStringHandler.AppendLiteral("; profiles = ");
			defaultInterpolatedStringHandler.AppendFormatted(_memory.Count);
			defaultInterpolatedStringHandler.AppendLiteral("; epsilon = ");
			defaultInterpolatedStringHandler.AppendFormatted(_decision.Epsilon, "F3");
			BrainObservabilityHub.Publish(BrainEventSeverity.Success, "VoltrisBrainV2", "Lifecycle", "Brain v2 iniciado com sucesso", defaultInterpolatedStringHandler.ToStringAndClear());
			await _sensor.StartAsync(_cts!.Token).ConfigureAwait(continueOnCapturedContext: false);
			_loop = Task.Run(() => DecisionLoopAsync(_cts!.Token), _cts!.Token);
			_autosaveTask = Task.Run(() => AutosaveLoopAsync(_cts!.Token), _cts!.Token);
			BackgroundScheduler.Instance.Register("Brain.TemporalDecay", async delegate
			{
				await _memory.PruneAsync();
			}, TimeSpan.FromHours(12.0), BackgroundScheduler.TaskPriority.Idle, TimeSpan.FromHours(1.0));
		}
		_logger.LogExit(nameof(StartAsync));
	}

	public async Task StopAsync()
	{
		_logger.LogEntry(nameof(StopAsync));
		if (!_running)
		{
			_logger.LogExit(nameof(StopAsync));
			return;
		}
		_running = false;
		try
		{
			_cts?.Cancel();
		}
		catch (Exception ex)
		{
			_logger?.LogError($"[{nameof(VoltrisBrainV2)}] {ex.Message}", ex);
		}
		try
		{
			if (_loop != null)
			{
				await _loop!.ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		catch (OperationCanceledException)
		{
		}
		try
		{
			if (_autosaveTask != null)
			{
				await _autosaveTask!.ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		catch (OperationCanceledException)
		{
		}
		await _sensor.StopAsync().ConfigureAwait(continueOnCapturedContext: false);
		await _executor.RestoreOriginalsAsync().ConfigureAwait(continueOnCapturedContext: false);
		await _decision.SaveAsync().ConfigureAwait(continueOnCapturedContext: false);
		await _memory.SaveAsync().ConfigureAwait(continueOnCapturedContext: false);
		await _stats.PersistAsync().ConfigureAwait(continueOnCapturedContext: false);
		BackgroundScheduler.Instance.Unregister("Brain.TemporalDecay");
		_logger.LogInfo("[BRAIN] VoltrisBrain v2 parado - Q-Table e perfis salvos");
		_logger.LogExit(nameof(StopAsync));
	}

	private async Task DecisionLoopAsync(CancellationToken ct)
	{
		_logger.LogInfo("[BRAIN] Loop de decisão iniciado");
		int cycle = 0;
		bool _lastLoopIterationFailed = false;
		while (!ct.IsCancellationRequested && _running)
		{
			cycle++;
			if (_lastLoopIterationFailed)
			{
				BrainSafetyPolicy.RegisterLoopSuccess();
				_lastLoopIterationFailed = false;
				_logger.LogInfo("[BRAIN-SAFETY] Loop de decisão recuperou — contador de falhas consecutive zerado. " +
					$"AggressiveFallbackAtivo={BrainSafetyPolicy.AggressiveFallbackActive}");
			}
			_logger.LogLoop("DecisionLoop", cycle);
			try
			{
				_logger.LogTimer("DecisionInterval", (long)DecisionInterval.TotalMilliseconds);
				await Task.Delay(DecisionInterval, ct);
				SensorSnapshot snapshot = _sensor.CurrentSnapshot;
				if (snapshot == null)
				{
					_logger.LogDebug("[BRAIN] Snapshot nulo, pulando ciclo");
					continue;
				}
				BrainStateKey state = (CurrentState = BrainStateKey.FromSnapshot(snapshot, 0));
				_logger.LogState("CurrentState", state.CanonicalKey);
				_memory.Observe(state, snapshot);
				_stateOrchestrator.Observe(snapshot);
				if (App.Body != null)
				{
					_stats.RecordCycle(App.Body!.CurrentContext);
				}
				bool stutterDetected = _stutter.DetectStutter(snapshot);
				_logger.LogDecision("StutterCheck", stutterDetected ? "stutter_detected" : "no_stutter", $"cpu={snapshot.CpuUsagePercent:F1}%");
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler;
				if (stutterDetected)
				{
					_stats.RegisterStutter();
					ILoggingService logger = _logger;
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(42, 3);
					defaultInterpolatedStringHandler.AppendLiteral("[BRAIN] Stutter detectado! CPU=");
					defaultInterpolatedStringHandler.AppendFormatted(snapshot.CpuUsagePercent, "F1");
					defaultInterpolatedStringHandler.AppendLiteral("% GPU=");
					defaultInterpolatedStringHandler.AppendFormatted(snapshot.GpuUsagePercent, "F1");
					defaultInterpolatedStringHandler.AppendLiteral("% FG=");
					defaultInterpolatedStringHandler.AppendFormatted(snapshot.ForegroundProcessName);
					logger.LogWarning(defaultInterpolatedStringHandler.ToStringAndClear());
				}
				// Decision throttling: skip full Q-Learning when workload/CPU is stable
				bool shouldSkipDecision = _hasPrevious && _previousSnapshot != null && !stutterDetected
					&& snapshot.Workload == _previousSnapshot.Workload
					&& Math.Abs(snapshot.CpuUsagePercent - _previousSnapshot.CpuUsagePercent) < 5.0
					&& _decisionCycleCounter > 0;
				if (shouldSkipDecision)
				{
					_logger.LogDecision("SkipCycle", "stable_workload", $"cpu={snapshot.CpuUsagePercent:F1}% skip={_idleCycleSkipCounter}");
					_idleCycleSkipCounter++;
					_decisionCycleCounter++;
					await ApplyKnownProfileAsync(snapshot);
			if (_idleCycleSkipCounter >= MaxIdleSkipsBeforeForceGc)
				{
					_idleCycleSkipCounter = 0;
				}
					continue;
				}
				_idleCycleSkipCounter = 0;
				BrainActionV2 action = (LastAction = await _decision.DecideAsync(state, snapshot));
				CurrentRecommendation = action.Reason;
	if (action.Kind == BrainActionKind.SetForegroundPriority)
			{
				TimeSpan elapsed = DateTime.UtcNow - _lastPriorityDispatch;
				if (elapsed < PriorityDispatchCooldown)
				{
					ILoggingService logger2 = _logger;
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(87, 2);
					defaultInterpolatedStringHandler.AppendLiteral("[BRAIN] SetForegroundPriority suprimido — cooldown de ");
					defaultInterpolatedStringHandler.AppendFormatted(PriorityDispatchCooldown.TotalSeconds);
					defaultInterpolatedStringHandler.AppendLiteral("s ativo (");
					defaultInterpolatedStringHandler.AppendFormatted(elapsed.TotalSeconds, "F1");
					defaultInterpolatedStringHandler.AppendLiteral("s desde último dispatch)");
					logger2.LogDebug(defaultInterpolatedStringHandler.ToStringAndClear());
					_decisionCycleCounter++;
					continue;
				}
				_lastPriorityDispatch = DateTime.UtcNow;
			}
				BrainDecision brainDecision = ConvertToBrainDecision(action, snapshot);
				ILoggingService logger3 = _logger;
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(86, 5);
				defaultInterpolatedStringHandler.AppendLiteral("[BRAIN] Emitindo OnDecisionReady: action=");
				defaultInterpolatedStringHandler.AppendFormatted(action.ActionId);
				defaultInterpolatedStringHandler.AppendLiteral(" | type=");
				defaultInterpolatedStringHandler.AppendFormatted(brainDecision.ActionType);
				defaultInterpolatedStringHandler.AppendLiteral(" | value=");
				defaultInterpolatedStringHandler.AppendFormatted(brainDecision.ActionValue);
				defaultInterpolatedStringHandler.AppendLiteral(" | target=");
				defaultInterpolatedStringHandler.AppendFormatted(brainDecision.Target ?? "none");
				defaultInterpolatedStringHandler.AppendLiteral(" | canal=");
				EventHandler<BrainDecisionReadyEventArgs>? onDecisionReady = this.OnDecisionReady;
				defaultInterpolatedStringHandler.AppendFormatted((onDecisionReady != null) ? onDecisionReady!.GetInvocationList().Length : 0);
				defaultInterpolatedStringHandler.AppendLiteral(" handlers");
				logger3.LogDebug(defaultInterpolatedStringHandler.ToStringAndClear());
				this.OnDecisionReady?.Invoke(this, new BrainDecisionReadyEventArgs
				{
					Decision = brainDecision,
					Snapshot = snapshot
				});
				double reward = (LastReward = CalculateReward(snapshot, action, stutterDetected));
				_logger.LogEvent("Reward", $"reward={reward:F3} state={state.CanonicalKey} action={action.ActionId}");
				ILoggingService logger4 = _logger;
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(70, 7);
				defaultInterpolatedStringHandler.AppendLiteral("[BRAIN] Reward: ");
				defaultInterpolatedStringHandler.AppendFormatted(reward, "+0.00;-0.00");
				defaultInterpolatedStringHandler.AppendLiteral(" | state=");
				defaultInterpolatedStringHandler.AppendFormatted(state.CanonicalKey);
				defaultInterpolatedStringHandler.AppendLiteral(" | action=");
				defaultInterpolatedStringHandler.AppendFormatted(action.ActionId);
				defaultInterpolatedStringHandler.AppendLiteral(" | stutter=");
				defaultInterpolatedStringHandler.AppendFormatted(stutterDetected);
				defaultInterpolatedStringHandler.AppendLiteral(" | fps=");
				defaultInterpolatedStringHandler.AppendFormatted(snapshot.CurrentFps, "F1");
				defaultInterpolatedStringHandler.AppendLiteral(" | cpu=");
				defaultInterpolatedStringHandler.AppendFormatted(snapshot.CpuUsagePercent, "F1");
				defaultInterpolatedStringHandler.AppendLiteral("% | temp=");
				defaultInterpolatedStringHandler.AppendFormatted(snapshot.CpuTemperatureC, "F0");
				defaultInterpolatedStringHandler.AppendLiteral("C");
				logger4.LogDebug(defaultInterpolatedStringHandler.ToStringAndClear());
				SnapshotAndLearn(snapshot, state, action, reward);
				SetBrainState(snapshot, state, action);
				await RecordTrainingDataAsync(snapshot, action, reward, stutterDetected);
				_decisionCycleCounter++;
				await ApplyKnownProfileAsync(snapshot);
				ILoggingService logger6 = _logger;
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(43, 4);
				defaultInterpolatedStringHandler.AppendLiteral("[BRAIN] Ciclo #");
				defaultInterpolatedStringHandler.AppendFormatted(_decisionCycleCounter);
				defaultInterpolatedStringHandler.AppendLiteral(" | Estado=");
				defaultInterpolatedStringHandler.AppendFormatted(state.CanonicalKey);
				defaultInterpolatedStringHandler.AppendLiteral(" | Ação=");
				defaultInterpolatedStringHandler.AppendFormatted(action.ActionId);
				defaultInterpolatedStringHandler.AppendLiteral(" | Reward=");
				defaultInterpolatedStringHandler.AppendFormatted(reward, "+0.00;-0.00");
				logger6.LogDebug(defaultInterpolatedStringHandler.ToStringAndClear());
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(6, 1);
				defaultInterpolatedStringHandler.AppendLiteral("Ciclo ");
				defaultInterpolatedStringHandler.AppendFormatted(_decisionCycleCounter);
				string message = defaultInterpolatedStringHandler.ToStringAndClear();
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(22, 3);
				defaultInterpolatedStringHandler.AppendLiteral("state=");
				defaultInterpolatedStringHandler.AppendFormatted(state.CanonicalKey);
				defaultInterpolatedStringHandler.AppendLiteral(" action=");
				defaultInterpolatedStringHandler.AppendFormatted(action.ActionId);
				defaultInterpolatedStringHandler.AppendLiteral(" reward=");
				defaultInterpolatedStringHandler.AppendFormatted(reward, "+0.00;-0.00");
				BrainObservabilityHub.Publish(BrainEventSeverity.Success, "VoltrisBrainV2", "DecisionCycle", message, defaultInterpolatedStringHandler.ToStringAndClear());
			}
			catch (OperationCanceledException)
			{
				break;
			}
			catch (Exception ex)
			{
				BrainSafetyPolicy.RegisterLoopError();
				_lastLoopIterationFailed = true;
				int consecutiveErrors = BrainSafetyPolicy.ConsecutiveLoopErrors;
				_logger.LogError($"[BRAIN-SAFETY] Erro no loop de decisão (falhas consecutivas={consecutiveErrors}/{BrainSafetyPolicy.FallbackErrorThreshold}): " + ex.Message, ex);
				if (consecutiveErrors == BrainSafetyPolicy.FallbackErrorThreshold)
				{
					_logger.LogWarning($"[BRAIN-SAFETY] FALLBACK AGRESSIVO ATIVADO após {consecutiveErrors} falhas consecutivas. " +
						"EPP, prioridade de processos e TRIM de memória serão BLOQUEADOS até o loop se recuperar.");
				}
			}
		}
		_logger.LogInfo("[BRAIN] Loop de decisão encerrado");
	}

	private async Task AutosaveLoopAsync(CancellationToken ct)
	{
		while (!ct.IsCancellationRequested && _running)
		{
			try
			{
				await Task.Delay(AutosaveInterval, ct);
				await _decision.SaveAsync().ConfigureAwait(continueOnCapturedContext: false);
				await _memory.SaveAsync().ConfigureAwait(continueOnCapturedContext: false);
				await _stats.PersistAsync().ConfigureAwait(continueOnCapturedContext: false);
				_logger.LogInfo("[BRAIN] Autosave concluído");
			}
			catch (OperationCanceledException)
			{
				break;
			}
			catch (Exception ex)
			{
				_logger.LogError("[BRAIN] Erro no autosave: " + ex.Message, ex);
			}
		}
	}

	private double CalculateReward(SensorSnapshot current, BrainActionV2 action, bool stutter)
	{
		double num = 0.0;
		if (stutter)
		{
			num = ((current.Workload != WorkloadCategory.Game) ? (num - 0.5) : (num - 2.0));
		}
		else
		{
			SensorSnapshot? previousSnapshot = _previousSnapshot;
			if (previousSnapshot != null && previousSnapshot!.StutterDetected && current.Workload == WorkloadCategory.Game)
			{
				num += 4.0;
				_logger.LogInfo("[BRAIN] Reward: +4.0 | stutter resolvido em Gaming");
			}
		}
		if (action.Kind == BrainActionKind.NoAction && _previousSnapshot != null && _previousSnapshot!.Workload != current.Workload)
		{
			// Penalidade reduzida — não forçar ação desnecessária se configurações atuais já são adequadas
			num -= 0.5;
			_logger.LogDebug("[BRAIN] Reward: -0.5 | NoAction leve: contexto mudou mas configurações atuais podem ser adequadas");
		}
		if (_previousSnapshot != null && current.CpuTemperatureC >= 0.0 && _previousSnapshot!.CpuTemperatureC >= 0.0)
		{
			double num2 = _previousSnapshot!.CpuTemperatureC - current.CpuTemperatureC;
			if (current.CpuTemperatureC > 80.0 && num2 > 2.0)
			{
				num += 5.0;
				ILoggingService logger = _logger;
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(56, 2);
				defaultInterpolatedStringHandler.AppendLiteral("[BRAIN] Reward: +5.0 | temperatura caiu ");
				defaultInterpolatedStringHandler.AppendFormatted(num2, "F1");
				defaultInterpolatedStringHandler.AppendLiteral("°C (crítica: ");
				defaultInterpolatedStringHandler.AppendFormatted(current.CpuTemperatureC, "F0");
				defaultInterpolatedStringHandler.AppendLiteral("°C)");
				logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
			}
		}
		if (current.Workload == WorkloadCategory.Game)
		{
			BrainActionV2? previousAction = _previousAction;
			if (previousAction != null && previousAction!.Kind == BrainActionKind.SetEpp)
			{
				BrainActionV2? previousAction2 = _previousAction;
				if (previousAction2 != null && previousAction2!.Param == 100)
				{
					num -= 4.0;
					_logger.LogInfo("[BRAIN] Reward: -4.0 | EPP=100 aplicado durante Gaming");
				}
			}
		}
		if (_previousSnapshot != null && current.Workload == WorkloadCategory.Game && current.CurrentFps > 0.0 && _previousSnapshot!.CurrentFps > 0.0)
		{
			double num3 = (current.CurrentFps - _previousSnapshot!.CurrentFps) / _previousSnapshot!.CurrentFps;
			if (num3 >= 0.15)
			{
				num += 3.0;
				_logger.LogInfo("[BRAIN] Reward: +3.0 | FPS aumentou 15%+ em Gaming");
			}
		}
		if (_previousSnapshot != null && (current.Workload == WorkloadCategory.Game || current.Workload == WorkloadCategory.Work))
		{
			double num4 = _previousSnapshot!.CpuUsagePercent - current.CpuUsagePercent;
			double num5 = current.AvailableRam - _previousSnapshot!.AvailableRam;
			if (num4 > 5.0 || num5 > 200.0)
			{
				num += 2.5;
				ILoggingService logger2 = _logger;
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(80, 2);
				defaultInterpolatedStringHandler.AppendLiteral("[BRAIN] Reward: +2.5 | Supressão/ABD bem-sucedida: CPU liberada ");
				defaultInterpolatedStringHandler.AppendFormatted(num4, "F1");
				defaultInterpolatedStringHandler.AppendLiteral("%, RAM livre +");
				defaultInterpolatedStringHandler.AppendFormatted(num5, "F0");
				defaultInterpolatedStringHandler.AppendLiteral("MB");
				logger2.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
			}
		}
		if (_previousSnapshot != null && current.Workload == WorkloadCategory.Idle)
		{
			double num6 = _previousSnapshot!.CpuUsagePercent - current.CpuUsagePercent;
			double num7 = _previousSnapshot!.GpuUsagePercent - current.GpuUsagePercent;
			num += (num6 + num7) * 0.01;
		}
		return Math.Clamp(num, -5.0, 5.0);
	}

	private async Task ApplyKnownProfileAsync(SensorSnapshot snapshot)
	{
		if (string.IsNullOrWhiteSpace(snapshot.ForegroundProcessName))
		{
			_logger.LogDebug("[MEMORY] ApplyKnownProfile ignorado: foreground vazio");
			return;
		}
		BrainProcessProfile profile = _memory.GetProfile(snapshot.ForegroundProcessName);
		if (profile == null)
		{
			_logger.LogDebug("[MEMORY] ApplyKnownProfile: nenhum perfil para " + snapshot.ForegroundProcessName);
			return;
		}
		ILoggingService logger = _logger;
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(75, 5);
		defaultInterpolatedStringHandler.AppendLiteral("[MEMORY] ApplyKnownProfile: ");
		defaultInterpolatedStringHandler.AppendFormatted(snapshot.ForegroundProcessName);
		defaultInterpolatedStringHandler.AppendLiteral(" | relevance=");
		defaultInterpolatedStringHandler.AppendFormatted(profile.RelevanceScore, "F2");
		defaultInterpolatedStringHandler.AppendLiteral(" | actions=");
		defaultInterpolatedStringHandler.AppendFormatted(profile.SuccessfulActions.Count);
		defaultInterpolatedStringHandler.AppendLiteral(" | bestEpp=");
		defaultInterpolatedStringHandler.AppendFormatted(profile.BestEpp);
		defaultInterpolatedStringHandler.AppendLiteral(" | sessions=");
		defaultInterpolatedStringHandler.AppendFormatted(profile.Sessions);
		logger.LogDebug(defaultInterpolatedStringHandler.ToStringAndClear());

		// Não sobrescrever configurações definidas por API externa recentemente
		if (DateTime.UtcNow - _lastExternalApiCall < ExternalApiCooldown)
		{
			_logger.LogInfo($"[MEMORY] ApplyKnownProfile ignorado: API externa chamada há {(DateTime.UtcNow - _lastExternalApiCall).TotalSeconds:F0}s (cooldown de {ExternalApiCooldown.TotalMinutes}min)");
			return;
		}

		if (profile.RelevanceScore >= 0.3 && profile.SuccessfulActions.Count > 0)
		{
			ILoggingService logger2 = _logger;
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(73, 4);
			defaultInterpolatedStringHandler.AppendLiteral("[MEMORY] [OK] Perfil ATIVO para ");
			defaultInterpolatedStringHandler.AppendFormatted(snapshot.ForegroundProcessName);
			defaultInterpolatedStringHandler.AppendLiteral(": ");
			defaultInterpolatedStringHandler.AppendFormatted(profile.SuccessfulActions.Count);
			defaultInterpolatedStringHandler.AppendLiteral(" ações de sucesso | relevância=");
			defaultInterpolatedStringHandler.AppendFormatted(profile.RelevanceScore, "F2");
			defaultInterpolatedStringHandler.AppendLiteral(" | BestEPP=");
			defaultInterpolatedStringHandler.AppendFormatted(profile.BestEpp);
			logger2.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
			if (profile.BestEpp != 50)
			{
				ILoggingService logger3 = _logger;
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(37, 2);
				defaultInterpolatedStringHandler.AppendLiteral("[MEMORY] Aplicando EPP=");
				defaultInterpolatedStringHandler.AppendFormatted(profile.BestEpp);
				defaultInterpolatedStringHandler.AppendLiteral(" do perfil de ");
				defaultInterpolatedStringHandler.AppendFormatted(snapshot.ForegroundProcessName);
				logger3.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
				BrainActionV2 eppAction = new BrainActionV2
				{
					Kind = BrainActionKind.SetEpp,
					Param = profile.BestEpp,
					Reason = "profile: " + snapshot.ForegroundProcessName
				};
				BrainActionResultV2 result = await _executor.ExecuteAsync(eppAction, snapshot);
				ILoggingService logger4 = _logger;
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(61, 4);
				defaultInterpolatedStringHandler.AppendLiteral("[MEMORY] Resultado aplicação EPP=");
				defaultInterpolatedStringHandler.AppendFormatted(profile.BestEpp);
				defaultInterpolatedStringHandler.AppendLiteral(": executed=");
				defaultInterpolatedStringHandler.AppendFormatted(result.Executed);
				defaultInterpolatedStringHandler.AppendLiteral(" skipped=");
				defaultInterpolatedStringHandler.AppendFormatted(result.Skipped);
				defaultInterpolatedStringHandler.AppendLiteral(" reason=");
				defaultInterpolatedStringHandler.AppendFormatted(result.SkipReason);
				logger4.LogDebug(defaultInterpolatedStringHandler.ToStringAndClear());
			}
			else
			{
				ILoggingService logger5 = _logger;
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(61, 1);
				defaultInterpolatedStringHandler.AppendLiteral("[MEMORY] BestEpp=");
				defaultInterpolatedStringHandler.AppendFormatted(profile.BestEpp);
				defaultInterpolatedStringHandler.AppendLiteral(" é default (50), não aplicando EPP do perfil");
				logger5.LogDebug(defaultInterpolatedStringHandler.ToStringAndClear());
			}
		}
		else if (profile.SuccessfulActions.Count > 0 && profile.RelevanceScore < 0.3)
		{
			ILoggingService logger6 = _logger;
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(88, 2);
			defaultInterpolatedStringHandler.AppendLiteral("[MEMORY] Perfil ");
			defaultInterpolatedStringHandler.AppendFormatted(snapshot.ForegroundProcessName);
			defaultInterpolatedStringHandler.AppendLiteral(" ignorado: relevância=");
			defaultInterpolatedStringHandler.AppendFormatted(profile.RelevanceScore, "F2");
			defaultInterpolatedStringHandler.AppendLiteral(" (<0.30) — ações seriam aplicáveis mas score baixo");
			logger6.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
		}
	}

	public void Dispose()
	{
		Task.Run(async () => await DisposeAsync().AsTask().ConfigureAwait(false)).GetAwaiter().GetResult();
	}

	public async ValueTask DisposeAsync()
	{
		_logger.LogEntry(nameof(DisposeAsync));
		if (_running)
		{
			await StopAsync().ConfigureAwait(continueOnCapturedContext: false);
		}
		_cts?.Dispose();
		_sensor?.Dispose();
		_executor?.Dispose();
		GC.SuppressFinalize(this);
		_logger.LogExit(nameof(DisposeAsync));
	}

	public async Task<BrainRequestResult> RequestGamingProfile(string processName)
	{
		_logger.LogEntry(nameof(RequestGamingProfile), ("processName", processName));
		if (!_running)
		{
			return Reject("brain-not-running");
		}
		if (string.IsNullOrWhiteSpace(processName))
		{
			processName = "unknown.exe";
		}
		SensorSnapshot snap = _sensor.CurrentSnapshot;
		if (snap == null)
		{
			return Reject("no-sensor-snapshot");
		}
		if (!_stateOrchestrator.RequestStateTransition(BrainOperationalState.GameMode, snap))
		{
			_logger.LogInfo("[BRAIN-API] Bypassing histerese para ativação explícita de GameMode: " + processName);
			_stateOrchestrator.ForceStateTransition(BrainOperationalState.GameMode);
		}
		_logger.LogInfo("[BRAIN-API] RequestGamingProfile('" + processName + "') aplicando bundle gaming");
		_lastExternalApiCall = DateTime.UtcNow;
		List<string> executed = new List<string>();
		BrainActionV2[] bundle = new BrainActionV2[4]
		{
			BuildAction(BrainActionKind.SetEpp, 0, snap, "api:gaming-profile"),
			BuildAction(BrainActionKind.SetSystemResponsiveness, 10, snap, "api:gaming-profile"),
			BuildAction(BrainActionKind.EnableGamingMode, 1, snap, "api:gaming-profile"),
			BuildAction(BrainActionKind.SetForegroundPriority, 2, snap, "api:gaming-profile")
		};
		BrainActionV2[] array = bundle;
		foreach (BrainActionV2 act in array)
		{
			BrainActionResultV2 r = await _executor.ExecuteAsync(act, snap);
			if (r.Executed)
			{
				executed.Add(act.ActionId);
			}
			this.ActionExecuted?.Invoke(this, r);
		}
		_logger.LogExit(nameof(RequestGamingProfile), Ok("", executed).Reason);
		return Ok("gaming-profile-applied for " + processName, executed);
	}

	/// <summary>
	/// [API EXTERNA] PatternRecognitionService chama quando detecta risco IMINENTE de stutter
	/// Aplica bundle preventivo EMERGENCIAL para evitar travamento
	/// </summary>
	public async Task<BrainRequestResult> RequestStutterPreventionAsync(float spikeRisk, SystemSpikeFeatures features)
	{
		_logger.LogInfo($"[BRAIN-API] 🚨 STUTTER PREVENTION solicitado! Risco: {spikeRisk * 100:F1}%");
		
		if (!_running)
		{
			return Reject("brain-not-running");
		}
		
		SensorSnapshot snap = _sensor.CurrentSnapshot;
		if (snap == null)
		{
			return Reject("no-sensor-snapshot");
		}
		
		// Determinar intensidade da resposta baseada no risco
		int eppTarget = spikeRisk switch
		{
			> 0.85f => 0,      // Risco crítico → Performance máxima
			> 0.7f => 33,      // Risco alto → Quase performance
			_ => 75            // Risco moderado → Conservador
		};
		
		int srTarget = spikeRisk switch
		{
			> 0.85f => 0,      // Máxima responsividade
			> 0.7f => 10,
			_ => 20
		};
		
		_logger.LogInfo($"[BRAIN-API] Aplicando bundle preventivo: EPP={eppTarget}, SR={srTarget}");
		
		List<string> executed = new List<string>();
		
		// Bundle preventivo de emergência
		BrainActionV2[] bundle = new BrainActionV2[5]
		{
			BuildAction(BrainActionKind.SetEpp, eppTarget, snap, "stutter-prevention"),
			BuildAction(BrainActionKind.SetSystemResponsiveness, srTarget, snap, "stutter-prevention"),
			// BUG CORRIGIDO: usava 3, que nao existe em ProcessPriorityChoice
			// (Normal=0, AboveNormal=1, High=2). O proprio comentario dizia
			// "High", que e o ordinal 2. Com 3, a acao era descartada por
			// InvalidEnumArgumentException ao aplicar a prioridade.
			BuildAction(BrainActionKind.SetForegroundPriority, 2, snap, "stutter-prevention"), // High
			BuildAction(BrainActionKind.EnableGamingMode, 1, snap, "stutter-prevention"),
			BuildAction(BrainActionKind.TrimWorkingSet, 0, snap, "stutter-prevention") // Limpar memória
		};
		
		BrainActionV2[] array = bundle;
		foreach (BrainActionV2 act in array)
		{
			try
			{
				BrainActionResultV2 r = await _executor.ExecuteAsync(act, snap);
				if (r.Executed)
				{
					executed.Add(act.ActionId);
					_logger.LogDebug($"[BRAIN-API] ✅ Ação executada: {act.ActionId}");
				}
				else if (r.Skipped)
				{
					_logger.LogDebug($"[BRAIN-API] ⚠️ Ação skipada: {act.ActionId} - {r.SkipReason}");
				}
				else
				{
					_logger.LogDebug($"[BRAIN-API] ⚠️ Ação falhou: {act.ActionId}");
				}
				this.ActionExecuted?.Invoke(this, r);
			}
			catch (Exception ex)
			{
				_logger.LogError($"[BRAIN-API] Erro ao executar {act.ActionId}: {ex.Message}");
			}
		}
		
		// Registrar decisão no learning.db
		await _learningDb.LogBrainDecisionAsync(
			"StutterPreventionTriggered",
			spikeRisk,
			$"Risco={spikeRisk * 100:F1}% | CPU={features.CpuLoad:F1}% RAM={features.RamUsedGB:F1}GB GPU={features.GpuLoad:F1}% FPS={features.CurrentFps:F0}",
			Environment.MachineName);
		
		string resultMsg = $"Stutter prevention applied: {executed.Count} ações executadas";
		return Ok(resultMsg, executed);
	}

	public async Task<BrainRequestResult> RequestManualOptimization(OptimizationIntent intent)
	{
		_logger.LogEntry(nameof(RequestManualOptimization), ("intent", intent));
		if (!_running)
		{
			return Reject("brain-not-running");
		}
		SensorSnapshot snap = _sensor.CurrentSnapshot;
		if (snap == null)
		{
			return Reject("no-sensor-snapshot");
		}

		BrainOperationalState brainOperationalState = intent switch
		{
			OptimizationIntent.Quick => BrainOperationalState.IdleOptimization, 
			OptimizationIntent.Standard => BrainOperationalState.Balanced, 
			OptimizationIntent.Aggressive => BrainOperationalState.PerformanceBurst, 
			OptimizationIntent.ThermalRelief => BrainOperationalState.ThermalRelief, 
			_ => BrainOperationalState.Balanced};

		BrainOperationalState targetState = brainOperationalState;
		ILoggingService logger = _logger;
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(53, 1);
		defaultInterpolatedStringHandler.AppendLiteral("[BRAIN-API] Forçando transição manual de estado para ");
		defaultInterpolatedStringHandler.AppendFormatted(targetState);
		logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
		if (!_stateOrchestrator.RequestStateTransition(targetState, snap))
		{
			ILoggingService logger2 = _logger;
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(92, 1);
			defaultInterpolatedStringHandler.AppendLiteral("[BRAIN-API] Histerese tentou bloquear transição manual para ");
			defaultInterpolatedStringHandler.AppendFormatted(targetState);
			defaultInterpolatedStringHandler.AppendLiteral(". Ignorando e forçando execução.");
			logger2.LogWarning(defaultInterpolatedStringHandler.ToStringAndClear());
		}
		ILoggingService logger3 = _logger;
		defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(56, 3);
		defaultInterpolatedStringHandler.AppendLiteral("[BRAIN-API] RequestManualOptimization(intent=");
		defaultInterpolatedStringHandler.AppendFormatted(intent);
		defaultInterpolatedStringHandler.AppendLiteral(") fg=");
		defaultInterpolatedStringHandler.AppendFormatted(snap.ForegroundProcessName);
		defaultInterpolatedStringHandler.AppendLiteral(" cpu=");
		defaultInterpolatedStringHandler.AppendFormatted(snap.CpuUsagePercent, "F1");
		defaultInterpolatedStringHandler.AppendLiteral("%");
		logger3.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());

		BrainActionV2[] array = intent switch
		{
			OptimizationIntent.Quick => new BrainActionV2[1] { BuildAction(BrainActionKind.TrimWorkingSet, 0, snap, "api:manual-quick") }, 
			OptimizationIntent.Standard => new BrainActionV2[2]
			{
				BuildAction(BrainActionKind.SetEpp, 50, snap, "api:manual-standard"),
				BuildAction(BrainActionKind.TrimWorkingSet, 0, snap, "api:manual-standard")
			}, 
			OptimizationIntent.Aggressive => new BrainActionV2[4]
			{
				BuildAction(BrainActionKind.SetEpp, 0, snap, "api:manual-aggressive"),
				BuildAction(BrainActionKind.SetSystemResponsiveness, 10, snap, "api:manual-aggressive"),
				BuildAction(BrainActionKind.SetForegroundPriority, 2, snap, "api:manual-aggressive"),
				BuildAction(BrainActionKind.TrimWorkingSet, 0, snap, "api:manual-aggressive")
			}, 
			OptimizationIntent.ThermalRelief => new BrainActionV2[3]
			{
				BuildAction(BrainActionKind.SetEpp, 80, snap, "api:manual-thermal"),
				BuildAction(BrainActionKind.SetForegroundPriority, 0, snap, "api:manual-thermal"),
				BuildAction(BrainActionKind.TrimWorkingSet, 0, snap, "api:manual-thermal")
			}, 
			_ => Array.Empty<BrainActionV2>()};

		BrainActionV2[] bundle = array;
		List<string> executed = new List<string>();
		BrainActionV2[] array2 = bundle;
		foreach (BrainActionV2 act in array2)
		{
			BrainActionResultV2 r = await _executor.ExecuteAsync(act, snap);
			if (r.Executed)
			{
				executed.Add(act.ActionId);
			}
			this.ActionExecuted?.Invoke(this, r);
		}
		defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(7, 1);
		defaultInterpolatedStringHandler.AppendLiteral("manual-");
		defaultInterpolatedStringHandler.AppendFormatted(intent);
		_logger.LogExit(nameof(RequestManualOptimization));
		return Ok(defaultInterpolatedStringHandler.ToStringAndClear(), executed);
	}

	public async Task<BrainRequestResult> RequestPowerProfile(PowerProfileKind profile)
	{
		_logger.LogEntry(nameof(RequestPowerProfile), ("profile", profile));
		if (!_running)
		{
			return Reject("brain-not-running");
		}
		SensorSnapshot snap = _sensor.CurrentSnapshot;
		if (snap == null)
		{
			return Reject("no-sensor-snapshot");
		}

		int num = profile switch
		{
			PowerProfileKind.BatterySaver => 100, 
			PowerProfileKind.Balanced => 50, 
			PowerProfileKind.HighPerformance => 25, 
			PowerProfileKind.UltraPerformance => 0, 
			_ => 50};

		int epp = num;
		ILoggingService logger = _logger;
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(45, 3);
		defaultInterpolatedStringHandler.AppendLiteral("[BRAIN-API] [");
		defaultInterpolatedStringHandler.AppendFormatted(DateTime.Now, "HH:mm:ss.fff");
		defaultInterpolatedStringHandler.AppendLiteral("] RequestPowerProfile(");
		defaultInterpolatedStringHandler.AppendFormatted(profile);
		defaultInterpolatedStringHandler.AppendLiteral(") SetEpp(");
		defaultInterpolatedStringHandler.AppendFormatted(epp);
		defaultInterpolatedStringHandler.AppendLiteral(")");
		logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
		defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(18, 1);
		defaultInterpolatedStringHandler.AppendLiteral("api:power-profile-");
		defaultInterpolatedStringHandler.AppendFormatted(profile);
		BrainActionV2 act = BuildAction(BrainActionKind.SetEpp, epp, snap, defaultInterpolatedStringHandler.ToStringAndClear());
		BrainActionResultV2 r = await _executor.ExecuteAsync(act, snap);
		this.ActionExecuted?.Invoke(this, r);
		if (r.Executed)
		{
			if (1 == 0)
			{
			}
			string text = profile switch
			{
				PowerProfileKind.BatterySaver => "a1841308-3541-4fab-bc81-f71556f20b4a", 
				PowerProfileKind.Balanced => "381b4222-f694-41f0-9685-ff5bb260df2e", 
				PowerProfileKind.HighPerformance => "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", 
				PowerProfileKind.UltraPerformance => "e9a42b02-d5df-448d-aa00-03f14749eb61", 
				_ => "381b4222-f694-41f0-9685-ff5bb260df2e"};
			if (1 == 0)
			{
			}
			string planGuid = text;
			IBrainExecutor executor = _executor;
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(27, 1);
			defaultInterpolatedStringHandler.AppendLiteral("Brain.RequestPowerProfile(");
			defaultInterpolatedStringHandler.AppendFormatted(profile);
			defaultInterpolatedStringHandler.AppendLiteral(")");
			executor.ExecutePowerPlan(planGuid, defaultInterpolatedStringHandler.ToStringAndClear());
		}
		BrainRequestResult result;
		if (!r.Executed)
		{
			result = Reject(string.IsNullOrEmpty(r.SkipReason) ? "executor-failed" : r.SkipReason);
		}
		else
		{
		defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(4, 1);
		defaultInterpolatedStringHandler.AppendLiteral("epp-");
		defaultInterpolatedStringHandler.AppendFormatted(epp);
		result = Ok(defaultInterpolatedStringHandler.ToStringAndClear(), new string[1] { act.ActionId });
		}
		_logger.LogExit(nameof(RequestPowerProfile), result);
		return result;
	}

	public BrainRequestResult SetActiveProfile(ProfileType profile)
	{
		_logger.LogEntry(nameof(SetActiveProfile), ("profile", profile));
		_logger.LogState("ActiveProfile", profile);
		ActiveProfile = profile;

		(int, int, int, string) tuple = profile switch
		{
			ProfileType.Economic => (50, 100, 20, "a1841308-3541-4fab-bc81-f71556f20b4a"), 
			ProfileType.Balanced => (25, 75, 20, "381b4222-f694-41f0-9685-ff5bb260df2e"), 
			ProfileType.Performance => (0, 25, 15, "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"), 
			ProfileType.Gamer => (0, 0, 10, "e9a42b02-d5df-448d-aa00-03f14749eb61"), 
			_ => (25, 75, 20, "381b4222-f694-41f0-9685-ff5bb260df2e")};

		(ProfileMinEpp, ProfileMaxEpp, ProfileSystemResponsiveness, ProfilePowerPlanGuid) = tuple;
		PersistActiveProfile();
		ILoggingService logger = _logger;
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(43, 4);
		defaultInterpolatedStringHandler.AppendLiteral("[BRAIN] Perfil ativo: ");
		defaultInterpolatedStringHandler.AppendFormatted(ActiveProfile);
		defaultInterpolatedStringHandler.AppendLiteral(" | Limites: EPP=");
		defaultInterpolatedStringHandler.AppendFormatted(ProfileMinEpp);
		defaultInterpolatedStringHandler.AppendLiteral("/");
		defaultInterpolatedStringHandler.AppendFormatted(ProfileMaxEpp);
		defaultInterpolatedStringHandler.AppendLiteral(" SR=");
		defaultInterpolatedStringHandler.AppendFormatted(ProfileSystemResponsiveness);
		logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
		IBrainExecutor executor = _executor;
		string profilePowerPlanGuid = ProfilePowerPlanGuid;
		defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(24, 1);
		defaultInterpolatedStringHandler.AppendLiteral("Brain.SetActiveProfile(");
		defaultInterpolatedStringHandler.AppendFormatted(profile);
		defaultInterpolatedStringHandler.AppendLiteral(")");
		bool flag = executor.ExecutePowerPlan(profilePowerPlanGuid, defaultInterpolatedStringHandler.ToStringAndClear());
		string reason;
		if (!flag)
		{
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(35, 1);
			defaultInterpolatedStringHandler.AppendLiteral("profile-");
			defaultInterpolatedStringHandler.AppendFormatted(profile);
			defaultInterpolatedStringHandler.AppendLiteral("-applied-powerplan-deferred");
			reason = defaultInterpolatedStringHandler.ToStringAndClear();
		}
		else
		{
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(16, 1);
			defaultInterpolatedStringHandler.AppendLiteral("profile-");
			defaultInterpolatedStringHandler.AppendFormatted(profile);
			defaultInterpolatedStringHandler.AppendLiteral("-applied");
			reason = defaultInterpolatedStringHandler.ToStringAndClear();
		}
		object obj;
		if (flag)
		{
			obj = new string[1] { "PowerPlan(" + ProfilePowerPlanGuid + ")" };
		}
		else
		{
			obj = new string[2];
			object obj2 = obj;
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(6, 2);
			defaultInterpolatedStringHandler.AppendLiteral("EPP(");
			defaultInterpolatedStringHandler.AppendFormatted(ProfileMinEpp);
			defaultInterpolatedStringHandler.AppendLiteral("-");
			defaultInterpolatedStringHandler.AppendFormatted(ProfileMaxEpp);
			defaultInterpolatedStringHandler.AppendLiteral(")");
			((object[])obj2)[0] = defaultInterpolatedStringHandler.ToStringAndClear();
			object obj3 = obj;
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(4, 1);
			defaultInterpolatedStringHandler.AppendLiteral("SR(");
			defaultInterpolatedStringHandler.AppendFormatted(ProfileSystemResponsiveness);
			defaultInterpolatedStringHandler.AppendLiteral(")");
			((object[])obj3)[1] = defaultInterpolatedStringHandler.ToStringAndClear();
		}
		_logger.LogExit(nameof(SetActiveProfile), reason);
		return new BrainRequestResult
		{
			Success = true,
			Reason = reason,
			ExecutedActions = (IReadOnlyList<string>)obj
		};
	}

	public BrainRequestResult SetActiveProfile(IntelligentProfileType intelligentProfile)
	{
		_logger.LogEntry(nameof(SetActiveProfile), ("intelligentProfile", intelligentProfile));

		ProfileType profileType = intelligentProfile switch
		{
			IntelligentProfileType.GamerCompetitive => ProfileType.Gamer, 
			IntelligentProfileType.GamerSinglePlayer => ProfileType.Gamer, 
			IntelligentProfileType.WorkOffice => ProfileType.Balanced, 
			IntelligentProfileType.CreativeVideoEditing => ProfileType.Performance, 
			IntelligentProfileType.DeveloperProgramming => ProfileType.Balanced, 
			IntelligentProfileType.GeneralBalanced => ProfileType.Balanced, 
			IntelligentProfileType.EnterpriseSecure => ProfileType.Economic, 
			_ => ProfileType.Balanced};

		ProfileType activeProfile = profileType;
		var result = SetActiveProfile(activeProfile);
		_logger.LogExit(nameof(SetActiveProfile), result);
		return result;
	}

	internal void RestoreActiveProfile()
	{
		_logger.LogEntry(nameof(RestoreActiveProfile));
		try
		{
			string pathToRead = File.Exists(_activeProfilePath) ? _activeProfilePath : _legacyActiveProfilePath;
			if (File.Exists(pathToRead))
			{
				string json = File.ReadAllText(pathToRead);
				ActiveProfileState activeProfileState = JsonSerializer.Deserialize<ActiveProfileState>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
				if (activeProfileState != null)
				{
					SetActiveProfile(activeProfileState.Profile);
					_logger.LogInfo($"[BRAIN] Perfil restaurado do disco: {activeProfileState.Profile} (origem={pathToRead})");
					if (pathToRead == _legacyActiveProfilePath)
					{
						_logger.LogInfo("[BRAIN] Perfil migrado do caminho legado para o caminho canônico Voltris\\Brain\\active_profile.json");
						PersistActiveProfile();
					}
				}
				else
				{
					_logger.LogWarning($"[BRAIN] active_profile.json em {pathToRead} não pôde ser desserializado — perfil mantido no padrão");
				}
			}
		}
		catch (Exception ex)
		{
			_logger.LogWarning("[BRAIN] Falha ao restaurar perfil ativo: " + ex.Message);
		}
		_logger.LogExit(nameof(RestoreActiveProfile));
	}

	public async Task<BrainRequestResult> RequestStutterProtocol()
	{
		if (!_running)
		{
			return Reject("brain-not-running");
		}
		SensorSnapshot snap = _sensor.CurrentSnapshot;
		if (snap == null)
		{
			return Reject("no-sensor-snapshot");
		}
		_logger.LogInfo("[BRAIN-API] RequestStutterProtocol disparando protocolo manual");
		bool fired = await _stutter.TriggerAsync(snap);
		if (fired)
		{
			_stats.RecordStutterResolved();
		}
		return fired ? Ok("stutter-fired", Array.Empty<string>()) : Reject("stutter-on-cooldown-or-not-applicable");
	}

	public async Task<BrainRequestResult> SuspendBackgroundProcesses()
	{
		if (!_running)
		{
			return Reject("brain-not-running");
		}
		SensorSnapshot snap = _sensor.CurrentSnapshot;
		if (snap == null)
		{
			return Reject("no-sensor-snapshot");
		}
		_logger.LogInfo("[BRAIN-API] SuspendBackgroundProcesses TrimWorkingSet global");
		BrainActionV2 act = BuildAction(BrainActionKind.TrimWorkingSet, 0, snap, "api:suspend-background");
		BrainActionResultV2 r = await _executor.ExecuteAsync(act, snap);
		this.ActionExecuted?.Invoke(this, r);
		return r.Executed ? Ok("trim-ws-global", new string[1] { act.ActionId }) : Reject(string.IsNullOrEmpty(r.SkipReason) ? "trim-failed" : r.SkipReason);
	}

	public Task<BrainRequestResult> RequestGamingProfileAsync(string processName)
	{
		return RequestGamingProfile(processName);
	}

	public Task<BrainRequestResult> RequestManualOptimizationAsync(OptimizationIntent intent)
	{
		return RequestManualOptimization(intent);
	}

	public Task<BrainRequestResult> RequestPowerProfileAsync(PowerProfileKind profile)
	{
		return RequestPowerProfile(profile);
	}

	public Task<BrainRequestResult> RequestStutterProtocolAsync()
	{
		return RequestStutterProtocol();
	}

	public Task<BrainRequestResult> SuspendBackgroundProcessesAsync()
	{
		return SuspendBackgroundProcesses();
	}

	public async Task TriggerImmediateDecisionAsync(string reason = "context-change")
	{
		_logger.LogEntry(nameof(TriggerImmediateDecisionAsync), ("reason", reason));
		if (!_running)
		{
			_logger.LogWarning("[BRAIN] TriggerImmediateDecisionAsync ignorado - Brain não está rodando");
			_logger.LogExit(nameof(TriggerImmediateDecisionAsync));
			return;
		}

		// Aguardar lock do estado para evitar race com DecisionLoopAsync
		lock (_brainStateLock)
		{
			// Apenas sincronizar o acesso — o trabalho real continua fora do lock
		}
		SensorSnapshot snap = _sensor.CurrentSnapshot;
		if (snap == null)
		{
			_logger.LogWarning("[BRAIN] TriggerImmediateDecisionAsync ignorado - sem snapshot de sensor");
			return;
		}
		ILoggingService logger = _logger;
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(60, 3);
		defaultInterpolatedStringHandler.AppendLiteral("[BRAIN] TriggerImmediateDecisionAsync acionado: ");
		defaultInterpolatedStringHandler.AppendFormatted(reason);
		defaultInterpolatedStringHandler.AppendLiteral(" | fg=");
		defaultInterpolatedStringHandler.AppendFormatted(snap.ForegroundProcessName);
		defaultInterpolatedStringHandler.AppendLiteral(" cpu=");
		defaultInterpolatedStringHandler.AppendFormatted(snap.CpuUsagePercent, "F1");
		defaultInterpolatedStringHandler.AppendLiteral("%");
		logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
		try
		{
			byte contextBucket = GetContextBucketFromBody();
			BrainStateKey state = (CurrentState = BrainStateKey.FromSnapshot(snap, contextBucket));
			if (snap.StutterDetected && await _stutter.TriggerAsync(snap))
			{
				_stats.RecordStutterResolved();
			}
			if (_hasPrevious && _previousSnapshot != null && _previousAction != null)
			{
				double reward = (LastReward = ComputeReward(_previousSnapshot, snap, _previousAction));
				_decision.Learn(_previousState, _previousAction, reward, state);
				_stats.RecordAction(_previousAction, reward, _previousSnapshot!.CpuUsagePercent, snap.CpuUsagePercent, Math.Max(0L, snap.AvailableRam - _previousSnapshot!.AvailableRam));
			}
			BrainActionV2 action = await _decision.DecideAsync(state, snap);
			VoltrisBrainV2 voltrisBrainV = this;
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(36, 3);
			defaultInterpolatedStringHandler.AppendLiteral("Ação contextual: ");
			defaultInterpolatedStringHandler.AppendFormatted(action.ActionId);
			defaultInterpolatedStringHandler.AppendLiteral(" | workload=");
			defaultInterpolatedStringHandler.AppendFormatted(snap.Workload);
			defaultInterpolatedStringHandler.AppendLiteral(" | ctx=");
			defaultInterpolatedStringHandler.AppendFormatted(reason);
			voltrisBrainV.CurrentRecommendation = defaultInterpolatedStringHandler.ToStringAndClear();
			(double cpu, long availRamMb) sysBefore = CaptureSystemPulse();
			BrainActionResultV2 result = await _executor.ExecuteAsync(action, snap);
			if (_body != null)
			{
				this.OnDecisionReady?.Invoke(this, new BrainDecisionReadyEventArgs
				{
					Decision = ConvertToBrainDecision(action, snap),
					Snapshot = snap
				});
			}
			LastAction = action;
			this.ActionExecuted?.Invoke(this, result);
			if (result.Executed)
			{
				Task.Run(async delegate
				{
					try
					{
						await Task.Delay(250).ConfigureAwait(continueOnCapturedContext: false);
						(double cpu, long availRamMb) sysAfter = CaptureSystemPulse();
						double deltaCpu = sysBefore.cpu - sysAfter.cpu;
						long deltaRam = sysAfter.availRamMb - sysBefore.availRamMb;
						ILoggingService logger3 = _logger;
						DefaultInterpolatedStringHandler defaultInterpolatedStringHandler2 = new DefaultInterpolatedStringHandler(42, 3);
						defaultInterpolatedStringHandler2.AppendLiteral("[BRAIN] Impacto imediato: ");
						defaultInterpolatedStringHandler2.AppendFormatted(action.ActionId);
						defaultInterpolatedStringHandler2.AppendLiteral(" | cpu=");
						defaultInterpolatedStringHandler2.AppendFormatted(deltaCpu, "F1");
						defaultInterpolatedStringHandler2.AppendLiteral("% ram=");
						defaultInterpolatedStringHandler2.AppendFormatted(deltaRam, "F0");
						defaultInterpolatedStringHandler2.AppendLiteral(" MB");
						logger3.LogInfo(defaultInterpolatedStringHandler2.ToStringAndClear());
					}
					catch (Exception ex)
					{
			_logger?.LogError($"[{nameof(VoltrisBrainV2)}] {ex.Message}", ex);
					}
				});
			}
			ILoggingService logger2 = _logger;
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(51, 3);
			defaultInterpolatedStringHandler.AppendLiteral("[BRAIN] Decisão imediata: ");
			defaultInterpolatedStringHandler.AppendFormatted(action.ActionId);
			defaultInterpolatedStringHandler.AppendLiteral(" | Executado: ");
			defaultInterpolatedStringHandler.AppendFormatted(result.Executed);
			defaultInterpolatedStringHandler.AppendLiteral(" | Motivo: ");
			defaultInterpolatedStringHandler.AppendFormatted(reason);
			logger2.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
			SetBrainState(snap, state, action);
		}
		catch (Exception ex)
		{
			_logger.LogError("[BRAIN] Erro em TriggerImmediateDecisionAsync: " + ex.Message);
		}
		_logger.LogExit(nameof(TriggerImmediateDecisionAsync));
	}

	private static BrainActionV2 BuildAction(BrainActionKind kind, int param, SensorSnapshot snap, string reason)
	{
		return new BrainActionV2
		{
			Kind = kind,
			Param = param,
			TargetPid = ((snap.ForegroundPid > 4) ? new int?(snap.ForegroundPid) : null),
			TargetProcessName = snap.ForegroundProcessName,
			Reason = reason
		};
	}

	private static BrainRequestResult Ok(string reason, IReadOnlyList<string> actions)
	{
		return new BrainRequestResult
		{
			Success = true,
			Reason = reason,
			ExecutedActions = actions
		};
	}

	private static BrainRequestResult Reject(string reason)
	{
		return new BrainRequestResult
		{
			Success = false,
			Reason = reason
		};
	}

	private void PersistActiveProfile()
	{
		try
		{
			string directoryName = Path.GetDirectoryName(_activeProfilePath);
			if (!string.IsNullOrWhiteSpace(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			ActiveProfileState value = new ActiveProfileState
			{
				Profile = ActiveProfile,
				SavedUtc = DateTime.UtcNow
			};
			File.WriteAllText(_activeProfilePath, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true,
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase
			}));
		}
		catch (Exception ex)
		{
			_logger.LogWarning("[BRAIN] Falha ao persistir perfil ativo: " + ex.Message);
		}
	}

	private void SnapshotAndLearn(SensorSnapshot snapshot, BrainStateKey state, BrainActionV2 action, double reward)
	{
		lock (_brainStateLock)
		{
			if (_hasPrevious && _previousAction != null && _previousSnapshot != null)
			{
				_decision.Learn(_previousState, _previousAction, reward, state);
				long ramFreedMB = 0L;
				double deltaRam = snapshot.AvailableRam - _previousSnapshot!.AvailableRam;
				if (deltaRam > 0.0)
				{
					ramFreedMB = (long)deltaRam;
				}
				_stats.RecordAction(_previousAction, reward, _previousSnapshot!.CpuUsagePercent, snapshot.CpuUsagePercent, ramFreedMB);
			}
		}
	}

	private void SetBrainState(SensorSnapshot snapshot, BrainStateKey state, BrainActionV2 action)
	{
		lock (_brainStateLock)
		{
			_previousSnapshot = snapshot;
			_previousState = state;
			_previousAction = action;
			_hasPrevious = true;
		}
	}

	private bool TryGetPreviousState(out SensorSnapshot? snap, out BrainStateKey state, out BrainActionV2? action)
	{
		lock (_brainStateLock)
		{
			snap = _previousSnapshot;
			state = _previousState;
			action = _previousAction;
			return _hasPrevious;
		}
	}

	// Reward unificado — TriggerImmediateDecisionAsync agora usa CalculateReward como única função de reward
	private double ComputeReward(SensorSnapshot old, SensorSnapshot current, BrainActionV2 action)
	{
		bool stutter = current.StutterDetected;
		return CalculateReward(current, action, stutter);
	}

	private async Task RecordTrainingDataAsync(SensorSnapshot snapshot, BrainActionV2 action, double reward, bool stutterDetected)
	{
		try
		{
			var features = new SystemSpikeFeatures
			{
				CpuLoad = (float)snapshot.CpuUsagePercent,
				RamUsedGB = (float)(snapshot.RamTotalMB - snapshot.AvailableRam) / 1024f,
				GpuLoad = (float)snapshot.GpuUsagePercent,
				CurrentFps = (float)snapshot.CurrentFps,
				DiskQueueLength = SystemMetricsCache.Instance.DiskQueueLength
			};
			await _patternRecognition.RecordEventAsync(features, stutterDetected);
			string hardwareHash = Environment.MachineName;
			await _learningDb.LogBrainDecisionAsync(
				action.Reason ?? "q-learning",
				reward,
				$"state={snapshot.CpuUsagePercent:F0}%cpu {snapshot.RamUsagePercent:F0}%ram fps={snapshot.CurrentFps:F1} stutter={stutterDetected}",
				hardwareHash
			);
		}
		catch (Exception ex)
		{
			_logger.LogError("[BRAIN-DATA] Erro ao registrar dados de treinamento: " + ex.Message);
		}
	}

	#region Métodos de Unificação da IA (Fase 1)

	/// <summary>
	/// Reporta reward externo normalizado (ex: sessão de jogo, decisão DSL, network optimization)
	/// PROTEÇÃO CRÍTICA: Normaliza reward para [-1.0, 1.0] independente da escala temporal
	/// </summary>
	public void ReportExternalReward(string context, double reward, object? metadata = null)
	{
		_logger.LogEntry(nameof(ReportExternalReward), ("context", context), ("reward", reward));
		if (!_running)
		{
			_logger.LogWarning("[BRAIN] ReportExternalReward ignorado - Brain não está rodando");
			_logger.LogExit(nameof(ReportExternalReward));
			return;
		}

		try
		{
			// [OK] PROTEÇÃO CRÍTICA #3: Normalização de reward entre escalas temporais diferentes
			// GamerMode (sessões longas): reward original pode ser +5.0
			// DSL (ciclos rápidos): reward original pode ser +1.0
			// SOLUÇÃO: Normalizar estritamente para [-1.0, 1.0]
			double normalizedReward = Math.Clamp(reward / 5.0, -1.0, 1.0);

			_logger.LogInfo($"[BRAIN-UNIFICACAO] Reward externo reportado: {context} = {reward:F2} (normalizado: {normalizedReward:F3})");

			// Criar estado baseado no contexto
			var state = new BrainStateKey(
				workload: WorkloadCategory.Game,
				cpuBucket: 3,  // 30-40% (bucket médio)
				ramBucket: 3,
				tempBucket: 5,
				contextBucket: 2  // External reward context
			);

			// Se houver ação anterior, fazer learn com reward normalizado
			if (_hasPrevious && _previousAction != null)
			{
				_decision.Learn(_previousState, _previousAction, normalizedReward, state);
				_logger.LogDebug($"[BRAIN-UNIFICACAO] Q-Learn externo: s={_previousState.CanonicalKey} a={_previousAction.ActionId} r={normalizedReward:F3}");
			}

			_logger.LogDecision("ExternalReward", context, normalizedReward);
			// Publicar evento no observability hub
			BrainObservabilityHub.Publish(
				BrainEventSeverity.Success,
				"VoltrisBrainV2",
				"ExternalReward",
				$"Reward reportado: {context}",
				$"reward={reward:F2} normalized={normalizedReward:F3} metadata={metadata}"
			);
		}
		catch (Exception ex)
		{
			_logger.LogError($"[BRAIN-UNIFICACAO] Erro ao reportar reward externo: {ex.Message}");
		}
		_logger.LogExit(nameof(ReportExternalReward));
	}

	/// <summary>
	/// Inicializa Q-Table com baseline do Onboarding
	/// PROTEÇÃO CRÍTICA: Bucketing agressivo para evitar explosão de estados
	/// </summary>
	public void InitializeFromOnboarding(OnboardingResult result, double baselineReward)
	{
		_logger.LogEntry(nameof(InitializeFromOnboarding), ("score", result.CurrentScore), ("reward", baselineReward));
		if (!_running)
		{
			_logger.LogWarning("[BRAIN] InitializeFromOnboarding ignorado - Brain não está rodando");
			_logger.LogExit(nameof(InitializeFromOnboarding));
			return;
		}

		try
		{
			_logger.LogInfo($"[BRAIN-UNIFICACAO] Inicializando Q-Table com baseline do Onboarding (score={result.CurrentScore})");

			// [OK] PROTEÇÃO CRÍTICA #1: Bucketing agressivo para evitar explosão de estados
			// Em vez de usar valores brutos, normalizar em buckets de 10%
			var baselineState = new BrainStateKey(
				workload: WorkloadCategory.Idle,
				cpuBucket: 2,  // 20-30% (bucket agressivo)
				ramBucket: 3,  // 30-40%
				tempBucket: 4, // 40-50C
				contextBucket: 0  // Onboarding context
			);

            // Observar estado baseline.
            // Sem sensor neste ponto do fluxo: double.NaN, e não a constante 45.0.
            // Um número térmico inventado realimenta o cérebro como se fosse uma
            // medição e pode influenciar decisões de otimização.
            _memory.Observe(baselineState, new SensorSnapshot
            {
                Workload = WorkloadCategory.Idle,
                CpuUsagePercent = 25.0,  // Valor normalizado
                RamUsagePercent = 35.0,
                CpuTemperatureC = double.NaN,
                ForegroundProcessName = "onboarding_baseline"
            });

			// [OK] PROTEÇÃO CRÍTICA #3: Normalizar reward do onboarding
			// Score do benchmark pode ser 0-1000, normalizar para [-1.0, 1.0]
			double normalizedBaselineReward = Math.Clamp(baselineReward / 1000.0, -1.0, 1.0);

			// Inicializar Q-Table com estado baseline
			_decision.Learn(_previousState, _previousAction ?? new BrainActionV2 { Kind = BrainActionKind.NoAction, Reason = "onboarding" }, normalizedBaselineReward, baselineState);

			_logger.LogInfo($"[BRAIN-UNIFICACAO] Baseline inicializada: state={baselineState.CanonicalKey} reward={normalizedBaselineReward:F3}");

			// Publicar evento
			BrainObservabilityHub.Publish(
				BrainEventSeverity.Success,
				"VoltrisBrainV2",
				"OnboardingBaseline",
				"Q-Table inicializada com baseline do Onboarding",
				$"score={result.CurrentScore} normalized_reward={normalizedBaselineReward:F3}"
			);
		}
		catch (Exception ex)
		{
			_logger.LogError($"[BRAIN-UNIFICACAO] Erro ao inicializar Q-Table do Onboarding: {ex.Message}");
		}
		_logger.LogExit(nameof(InitializeFromOnboarding));
	}

	/// <summary>
	/// Registra decisão de sistema externo (DSL, Network, etc.) para aprendizado
	/// PROTEÇÃO CRÍTICA: Bucketing agressivo no contexto do sistema
	/// </summary>
	public void RegisterExternalDecision(string system, string decision, string context)
	{
		_logger.LogEntry(nameof(RegisterExternalDecision), ("system", system), ("decision", decision));
		if (!_running)
		{
			_logger.LogWarning("[BRAIN] RegisterExternalDecision ignorado - Brain não está rodando");
			_logger.LogExit(nameof(RegisterExternalDecision));
			return;
		}

		try
		{
			_logger.LogDebug($"[BRAIN-UNIFICACAO] Decisão externa registrada: {system} -> {decision} (context={context})");

			// [OK] PROTEÇÃO CRÍTICA #1: Mapear sistema para contextBucket específico
			byte contextBucket = system.ToLower() switch
			{
				"dsl" => 1,
				"gamermode" => 2,
				"network" or "watchdogs" => 3,
				"profiler" => 4,
				"fluidity" => 5,
				_ => 0
			};

			// Criar estado com bucketing agressivo
			var state = new BrainStateKey(
				workload: WorkloadCategory.Work,
				cpuBucket: 5,  // 50-60% (bucket conservador)
				ramBucket: 5,
				tempBucket: 5,
				contextBucket: contextBucket
			);

			// Observar estado (sem reward imediato - será calculado depois)
			_memory.Observe(state, new SensorSnapshot
			{
				Workload = WorkloadCategory.Work,
				ForegroundProcessName = $"system:{system}",
				CpuUsagePercent = 50.0,
				RamUsagePercent = 50.0
			});

			// Publicar evento
			BrainObservabilityHub.Publish(
				BrainEventSeverity.Info,
				"VoltrisBrainV2",
				"ExternalDecision",
				$"Decisão externa registrada: {system}",
				$"decision={decision} context={context} bucket={contextBucket}"
			);
			_logger.LogDecision("ExternalDecisionRegistered", decision, context);
		}
		catch (Exception ex)
		{
			_logger.LogError($"[BRAIN-UNIFICACAO] Erro ao registrar decisão externa: {ex.Message}");
		}
		_logger.LogExit(nameof(RegisterExternalDecision));
	}

	/// <summary>
	/// Solicita decisão otimizada baseada em Q-Table para contexto específico
	/// </summary>
	public async Task<BrainDecision> RequestOptimalDecisionAsync(WorkloadCategory workload, string context)
	{
		_logger.LogEntry(nameof(RequestOptimalDecisionAsync), ("workload", workload), ("context", context));
		if (!_running)
		{
			_logger.LogWarning("[BRAIN] RequestOptimalDecision ignorado - Brain não está rodando");
			_logger.LogExit(nameof(RequestOptimalDecisionAsync), "brain-not-running");
            return new BrainDecision { ActionType = DecisionActionType.None, Justification = "brain-not-running" };
		}

		try
		{
			var snapshot = _sensor.CurrentSnapshot ?? new SensorSnapshot
			{
				Workload = workload,
				CpuUsagePercent = 50.0,
				RamUsagePercent = 50.0,
				CpuTemperatureC = 50.0,
				ForegroundProcessName = context
			};

			byte cpuBucket = (byte)(workload switch
			{
				WorkloadCategory.Game => 2,
				WorkloadCategory.Work => 5,
				WorkloadCategory.Idle => 1,
				_ => 5
			});

			var state = new BrainStateKey(
				workload: workload,
				cpuBucket: cpuBucket,
				ramBucket: 5,
				tempBucket: 5,
				contextBucket: 0
			);

			var action = await _decision.DecideAsync(state, snapshot);

			_logger.LogDebug($"[BRAIN-UNIFICACAO] Decisão ótima solicitada: workload={workload} context={context} -> action={action.ActionId}");
			_logger.LogDecision("OptimalDecision", action.Reason, action.ActionId);
			_logger.LogExit(nameof(RequestOptimalDecisionAsync), action.ActionId);
			return ConvertToBrainDecision(action, snapshot);
		}
		catch (Exception ex)
		{
			_logger.LogError($"[BRAIN-UNIFICACAO] Erro ao solicitar decisão ótima: {ex.Message}");
			_logger.LogExit(nameof(RequestOptimalDecisionAsync), $"error: {ex.Message}");
            return new BrainDecision { ActionType = DecisionActionType.None, Justification = $"error: {ex.Message}" };
		}
	}

	/// <summary>
	/// Aplica perfil aprendido para processo específico (já existente em ApplyKnownProfileAsync)
	/// Este método é um wrapper público para uso externo
	/// </summary>
	public async Task<bool> ApplyLearnedProfileAsync(string processName)
	{
		_logger.LogEntry(nameof(ApplyLearnedProfileAsync), ("processName", processName));
		if (!_running)
		{
			_logger.LogWarning("[BRAIN] ApplyLearnedProfileAsync ignorado - Brain não está rodando");
			_logger.LogExit(nameof(ApplyLearnedProfileAsync), false);
			return false;
		}

		if (string.IsNullOrWhiteSpace(processName))
		{
			_logger.LogDebug("[BRAIN] ApplyLearnedProfileAsync ignorado: processName vazio");
			_logger.LogExit(nameof(ApplyLearnedProfileAsync), false);
			return false;
		}

		try
		{
			var snapshot = _sensor.CurrentSnapshot;
			if (snapshot == null)
			{
				snapshot = new SensorSnapshot
				{
					ForegroundProcessName = processName,
					Workload = WorkloadCategory.Game,
					CpuUsagePercent = 50.0,
					RamUsagePercent = 50.0
				};
			}
			else
			{
				// Atualizar nome do processo no snapshot (criar novo devido a init-only)
				snapshot = new SensorSnapshot
				{
					TimestampUtc = snapshot.TimestampUtc,
					ForegroundProcessName = processName,
					Workload = snapshot.Workload,
					CpuUsagePercent = snapshot.CpuUsagePercent,
					CpuTemperatureC = snapshot.CpuTemperatureC,
					RamUsagePercent = snapshot.RamUsagePercent,
					ForegroundPid = snapshot.ForegroundPid,
					GpuUsagePercent = snapshot.GpuUsagePercent,
					GpuTemperatureC = snapshot.GpuTemperatureC,
					FrameTimeVarianceMs = snapshot.FrameTimeVarianceMs,
					StutterDetected = snapshot.StutterDetected,
					IsOnBattery = snapshot.IsOnBattery,
					CurrentFps = snapshot.CurrentFps,
					FpsVariance = snapshot.FpsVariance,
					FpsPercentile1Low = snapshot.FpsPercentile1Low,
					CpuClockMhz = snapshot.CpuClockMhz,
					AvailableRam = snapshot.AvailableRam,
					RamTotalMB = snapshot.RamTotalMB
				};
			}

			await ApplyKnownProfileAsync(snapshot);

			var profile = _memory.GetProfile(processName);
			if (profile != null && profile.RelevanceScore >= 0.3)
			{
				_logger.LogInfo($"[BRAIN-UNIFICACAO] Perfil aprendido aplicado: {processName} (relevance={profile.RelevanceScore:F2})");
				_logger.LogDecision("LearnedProfileApplied", $"relevance={profile.RelevanceScore:F2}", processName);
				_logger.LogExit(nameof(ApplyLearnedProfileAsync), true);
				return true;
			}

			_logger.LogDebug($"[BRAIN-UNIFICACAO] Perfil não encontrado ou relevância baixa: {processName}");
			_logger.LogExit(nameof(ApplyLearnedProfileAsync), false);
			return false;
		}
		catch (Exception ex)
		{
			_logger.LogError($"[BRAIN-UNIFICACAO] Erro ao aplicar perfil aprendido: {ex.Message}");
			_logger.LogExit(nameof(ApplyLearnedProfileAsync), false);
			return false;
		}
	}

	#endregion
}
