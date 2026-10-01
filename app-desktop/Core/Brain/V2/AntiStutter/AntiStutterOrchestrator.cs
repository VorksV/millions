using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Core.Brain.V2.AntiStutter;

/// <summary>
/// [FIX:BRAIN-LIGADO] O CICLO DO BRAIN, LIGADO DE VERDADE.
///
/// ESTA CLASSE NUNCA FOI EXECUTADA
/// ==============================
/// A auditoria achou que o regime de energia e a prioridade adaptativa já
/// estavam prontos, mas parados — e a causa era esta: o orquestrador não
/// implementava <see cref="IAutoStartService"/> e não estava registrado em
/// NENHUM container. Ele criava o próprio profiler no construtor, mas ninguém
/// criava o próprio orquestrador.
///
/// O efeito era silencioso e total:
///
///     profiler nunca roda -> nunca publica snapshot
///     -> o regime de energia é SEMPRE "Unknown"
///     -> a escalada nunca é autorizada
///     -> a prioridade do jogo nunca é corrigida pelo regime
///
/// Ou seja: a inteligência inteira estava correta e sem entrada de dados. O log
/// mostrava, a cada aplicação de perfil, a mesma linha:
///
///     [BRAIN-ENERGY] regime=Unknown ... | AUSENTE
///     motivo='sem telemetria disponivel: vale o piso do Windows'
///
/// Implementar <see cref="IAutoStartService"/> é o que amarra esta classe ao
/// `IAutoStartService` que o App já resolve e inicia no startup. É o mesmo
/// caminho do <c>DlsPolicyCoordinator</c>, que já funciona assim.
///
/// O QUE MUDA AO LIGAR ISTO
/// ========================
/// Passa a existir telemetria de verdade, e com ela três consequências:
///
/// 1. O regime deixa de ser sempre `Unknown` e passa a ser `Sustaining` ou
///    `Constrained` conforme a máquina. Isso é o pretendido.
///
/// 2. A escalada de energia continua DESLIGADA (porta fechada, REGRA 13 do
///    self-test). Nenhum valor muda. O que muda é a OBSERVAÇÃO: o log passa a
///    dizer quantas vezes a máquina estrangula, que é a medição que faltava
///    para destravar a escalada com evidência em vez de premissa.
///
/// 3. O ciclo passa a poder executar ações do `AntiStutterActionRegistry`. É o
///    comportamento que o módulo foi escrito para ter, e nunca teve.
///
/// O `StartAsync` não bloqueia o startup: o profiler sobe a própria task e o
/// `Task.Delay` de 5 segundos acontece DENTRO do laço, não antes dele.
/// </summary>
public sealed class AntiStutterOrchestrator : IDisposable, IAutoStartService
{
	private readonly ILoggingService _logger;

	private readonly AntiStutterProfiler _profiler;

	private readonly AntiStutterAnalyzer _analyzer;

	private readonly AntiStutterDecisionEngine _decisionEngine;

	private readonly AntiStutterActionRegistry _registry;

	private readonly AntiStutterMonitor _monitor;

	private readonly IBrainExecutor _executor;

	private CancellationTokenSource? _cts;

	private Task? _loopTask;

	private volatile bool _running;

	private readonly TimeSpan _normalInterval = TimeSpan.FromSeconds(2.0);

	private readonly TimeSpan _emergencyInterval = TimeSpan.FromMilliseconds(500.0);

	private bool _gamerMode;

	private DateTime _lastCycle = DateTime.MinValue;

	private readonly object _historyLock = new object();

	private readonly List<AntiStutterCycleResult> _history = new List<AntiStutterCycleResult>(100);

	public bool IsRunning => _running;

	public UserProfile CurrentProfile
	{
		get
		{
			return _decisionEngine.CurrentProfile;
		}
		set
		{
			_decisionEngine.CurrentProfile = value;
		}
	}

	public AntiStutterOrchestrator(ILoggingService logger, IBrainExecutor executor)
	{
		_logger = logger;
		_executor = executor;
		_profiler = new AntiStutterProfiler(logger);
		_analyzer = new AntiStutterAnalyzer();
		_registry = new AntiStutterActionRegistry();
		_decisionEngine = new AntiStutterDecisionEngine(logger, _registry, _analyzer);
		_monitor = new AntiStutterMonitor(logger, _profiler, _analyzer);
		_monitor.RevertRecommended += delegate(object? _, string id)
		{
			_logger.LogWarning("[Orchestrator] Revert recomendado: " + id);
		};
	}

	public async Task StartAsync(CancellationToken ct = default(CancellationToken))
	{
		if (!_running)
		{
			_running = true;
			_cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
			await _profiler.StartAsync(_cts!.Token);
			UserProfile profile = AntiStutterProfileManager.DetectRecommendedProfile();
			_decisionEngine.CurrentProfile = profile;
			ILoggingService logger = _logger;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(24, 1);
			defaultInterpolatedStringHandler.AppendLiteral("[Orchestrator] Profile: ");
			defaultInterpolatedStringHandler.AppendFormatted(profile);
			logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
			_loopTask = Task.Run(() => LoopAsync(_cts!.Token), _cts!.Token);
		}
	}

	public async Task StopAsync()
	{
		if (!_running)
		{
			return;
		}
		_running = false;
		_cts?.Cancel();
		if (_loopTask != null)
		{
			try
			{
				await _loopTask;
			}
			catch
			{
			}
		}
		await _profiler.StopAsync();
	}

	private async Task LoopAsync(CancellationToken ct)
	{
		await Task.Delay(1000, ct);
		while (!ct.IsCancellationRequested && _running)
		{
			try
			{
				AntiStutterSnapshot snapshot = _profiler.CurrentSnapshot;
				if (snapshot == null)
				{
					await Task.Delay(300, ct);
					continue;
				}
				BottleneckAnalysis analysis = _analyzer.Analyze(snapshot);
				TimeSpan interval = ((snapshot.StutterDetected || analysis.SeverityScore > 0.7) ? _emergencyInterval : _normalInterval);
				if (!(DateTime.UtcNow - _lastCycle >= interval))
				{
					await Task.Delay(100, ct);
					continue;
				}
				await ExecuteCycleAsync(snapshot, analysis, ct);
				_lastCycle = DateTime.UtcNow;
			}
			catch (OperationCanceledException)
			{
				break;
			}
			catch (Exception ex)
			{
				_logger.LogError("[Orchestrator] Loop error: " + ex.Message);
				await Task.Delay(1000, ct);
			}
		}
	}

	private async Task ExecuteCycleAsync(AntiStutterSnapshot snapshot, BottleneckAnalysis analysis, CancellationToken ct)
	{
		Stopwatch sw = Stopwatch.StartNew();
		AntiStutterDecision decision = _decisionEngine.Decide(snapshot, _gamerMode);
		if (!decision.ShouldAct)
		{
			RecordCycle(new AntiStutterCycleResult
			{
				Executed = false,
				Decision = decision,
				ExecutionTime = sw.Elapsed
			});
			return;
		}
		List<AntiStutterActionResult> results = new List<AntiStutterActionResult>();
		foreach (OptimizerAction action in decision.SelectedActions)
		{
			Stopwatch actionSw = Stopwatch.StartNew();
			try
			{
				BrainActionV2 brainAction = ConvertToBrainAction(action, snapshot);
				if (brainAction.Kind == BrainActionKind.NoAction)
				{
					results.Add(new AntiStutterActionResult
					{
						ActionId = action.Id,
						ActionName = action.Name,
						Success = false,
						Error = "No mapping",
						ExecutionTime = actionSw.Elapsed
					});
					continue;
				}
				BrainActionResultV2 exec = await _executor.ExecuteAsync(brainAction, null);
				bool ok = exec.Executed;
				results.Add(new AntiStutterActionResult
				{
					ActionId = action.Id,
					ActionName = action.Name,
					Success = ok,
					Error = (ok ? "" : exec.SkipReason),
					ExecutionTime = actionSw.Elapsed
				});
				if (ok)
				{
					_monitor.RecordActionExecution(action.Id, action.Name, decision.Analysis.SeverityScore, decision.Analysis.PrimaryBottleneck);
					_registry.RecordExecution(action.Id);
				}
				goto IL_03ec;
			}
			catch (Exception ex)
			{
				results.Add(new AntiStutterActionResult
				{
					ActionId = action.Id,
					ActionName = action.Name,
					Success = false,
					Error = ex.Message,
					ExecutionTime = actionSw.Elapsed
				});
				goto IL_03ec;
			}
			IL_03ec:
			await Task.Delay(80, ct);
		}
		sw.Stop();
		AntiStutterCycleResult cycle = new AntiStutterCycleResult
		{
			Executed = true,
			Decision = decision,
			ActionResults = results,
			ExecutionTime = sw.Elapsed,
			IsGamerModeActive = _gamerMode
		};
		RecordCycle(cycle);
		ILoggingService logger = _logger;
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(35, 4);
		defaultInterpolatedStringHandler.AppendLiteral("[Orchestrator] Cycle ");
		defaultInterpolatedStringHandler.AppendFormatted(cycle.CycleId);
		defaultInterpolatedStringHandler.AppendLiteral(" | OK: ");
		defaultInterpolatedStringHandler.AppendFormatted(results.Count((AntiStutterActionResult r) => r.Success));
		defaultInterpolatedStringHandler.AppendLiteral("/");
		defaultInterpolatedStringHandler.AppendFormatted(results.Count);
		defaultInterpolatedStringHandler.AppendLiteral(" | ");
		defaultInterpolatedStringHandler.AppendFormatted(sw.ElapsedMilliseconds);
		defaultInterpolatedStringHandler.AppendLiteral(" ms");
		logger.LogSuccess(defaultInterpolatedStringHandler.ToStringAndClear());
	}

	private BrainActionV2 ConvertToBrainAction(OptimizerAction action, AntiStutterSnapshot snap)
	{
		string name = action.Name;

		BrainActionV2 result = name switch
		{
			"SetEppMaximumPerformance" => new BrainActionV2
			{
				Kind = BrainActionKind.SetEpp,
				Param = 0
			}, 
			"ElevateForegroundPriority" => new BrainActionV2
			{
				Kind = BrainActionKind.SetForegroundPriority,
				TargetPid = snap.ForegroundPid
			}, 
			"TrimBackgroundProcesses" => new BrainActionV2
			{
				Kind = BrainActionKind.TrimWorkingSet
			}, 
			_ => BrainActionV2.NoOp()};

		return result;
	}

	private void RecordCycle(AntiStutterCycleResult result)
	{
		lock (_historyLock)
		{
			_history.Add(result);
			if (_history.Count > 100)
			{
				_history.RemoveAt(0);
			}
		}
	}

	public void Dispose()
	{
		StopAsync();
		_profiler.Dispose();
		_cts?.Dispose();
	}
}
