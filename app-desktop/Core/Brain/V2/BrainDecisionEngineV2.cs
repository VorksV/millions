using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.Intelligence;
using VoltrisOptimizer.Core.Intelligence.Heuristics.Abstractions;
using VoltrisOptimizer.Core.Intelligence.Heuristics.Models;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Hardware;
using VoltrisOptimizer.Services.Intelligence.VPIS.Engines;
using VoltrisOptimizer.Services.Intelligence.VPIS.Models;
using VoltrisOptimizer.Services.SystemIntelligenceProfiler;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Core.Brain.V2;

public sealed class BrainDecisionEngineV2 : IBrainDecision
{
	private readonly ILoggingService _logger;

	private readonly string _qTablePath;

	private const double Alpha = 0.1;

	private const double Gamma = 0.9;

	private const double EpsilonInitial = 0.3;

	private const double EpsilonDecay = 0.0003;

	private const double EpsilonMin = 0.05;

	private const long ReExplorationThreshold = 1500L;

	private const double ReExplorationEpsilon = 0.15;

	private double _epsilon = 0.3;

	private long _episodes;

	private long _episodesSinceEpsilonMin;

	private bool _hasReachedEpsilonMin;

	private readonly System.Random _rng = System.Random.Shared;

	private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, double>> _q = new ConcurrentDictionary<string, ConcurrentDictionary<string, double>>();
        // Upper bound for Q‑table entries to prevent unbounded memory growth
        private const int MaxQTableEntries = 200_000; // adjustable based on memory budget

	private readonly ConcurrentDictionary<string, long> _visits = new ConcurrentDictionary<string, long>();

	private HardwareProfile? _cachedHardwareProfile;

	private bool _hardwareProfileResolved;

	private static readonly BrainActionV2[] ActionCatalog = new BrainActionV2[16]
	{
		new BrainActionV2
		{
			Kind = BrainActionKind.NoAction,
			Param = 0,
			Reason = "no-op"
		},
		new BrainActionV2
		{
			Kind = BrainActionKind.SetEpp,
			Param = 0,
			Reason = "epp = 0 max performance"
		},
		new BrainActionV2
		{
			Kind = BrainActionKind.SetEpp,
			Param = 25,
			Reason = "epp = 25 balanced perf"
		},
		new BrainActionV2
		{
			Kind = BrainActionKind.SetEpp,
			Param = 50,
			Reason = "epp = 50 balanced"
		},
		new BrainActionV2
		{
			Kind = BrainActionKind.SetEpp,
			Param = 75,
			Reason = "epp = 75 balanced eff"
		},
		new BrainActionV2
		{
			Kind = BrainActionKind.SetForegroundPriority,
			Param = 0,
			Reason = "fg priority normal"
		},
		new BrainActionV2
		{
			Kind = BrainActionKind.SetForegroundPriority,
			Param = 1,
			Reason = "fg priority above-normal"
		},
		new BrainActionV2
		{
			Kind = BrainActionKind.SetForegroundPriority,
			Param = 2,
			Reason = "fg priority high"
		},
		new BrainActionV2
		{
			Kind = BrainActionKind.SetSystemResponsiveness,
			Param = 10,
			Reason = "sysresp = 10 (gaming)"
		},
		new BrainActionV2
		{
			Kind = BrainActionKind.SetSystemResponsiveness,
			Param = 20,
			Reason = "sysresp = 20 (default)"
		},
		new BrainActionV2
		{
			Kind = BrainActionKind.EnableGamingMode,
			Param = 1,
			Reason = "gaming-mode on"
		},
		new BrainActionV2
		{
			Kind = BrainActionKind.EnableGamingMode,
			Param = 0,
			Reason = "gaming-mode off"
		},
		new BrainActionV2
		{
			Kind = BrainActionKind.TrimWorkingSet,
			Param = 0,
			Reason = "trim background working-set"
		},
		new BrainActionV2
		{
			Kind = BrainActionKind.LockMemoryHard,
			Param = 1,
			Reason = "lock foreground memory hard"
		},
		new BrainActionV2
		{
			Kind = BrainActionKind.ForceTopologyPcores,
			Param = 1,
			Reason = "force p-cores / v-cache"
		},
		new BrainActionV2
		{
			Kind = BrainActionKind.SuspendJitterProcesses,
			Param = 1,
			Reason = "zero jitter mode on"
		}
	};

	private readonly IPatternRecognitionService _patternRecognition;

	private readonly IHeuristicsPipeline _heuristics;

	private readonly IEnumerable<IVpisDiagnosticEngine> _vpisEngines;

	public double Epsilon => _epsilon;

	public long Episodes => _episodes;

	public int KnownStates => _q.Count;

	public BrainDecisionEngineV2(ILoggingService logger, IPatternRecognitionService patternRecognition, IHeuristicsPipeline heuristics, IEnumerable<IVpisDiagnosticEngine> vpisEngines)
	{
		_logger = logger ?? throw new ArgumentNullException("logger");
		_patternRecognition = patternRecognition;
		_heuristics = heuristics;
		_vpisEngines = vpisEngines;
		string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Voltris", "Brain");
		try
		{
			Directory.CreateDirectory(text);
		}
		catch (Exception ex)
		{
			_logger?.LogError($"[{nameof(BrainDecisionEngineV2)}] {ex.Message}", ex);
		}
		_qTablePath = Path.Combine(text, "qtable.json");
	}

	public async Task<BrainActionV2> DecideAsync(BrainStateKey state, SensorSnapshot snapshot)
	{
		_logger.LogEntry(nameof(DecideAsync), ("state", state.CanonicalKey), ("workload", snapshot.Workload));
		_episodes++;
		DecayEpsilon();
		string canonicalKey = state.CanonicalKey;
		_visits.AddOrUpdate(canonicalKey, 1L, (string _, long v) => v + 1);
		ConcurrentDictionary<string, double> orAdd = _q.GetOrAdd(canonicalKey, (string _) => InitActionMap());
		List<BrainActionV2> availableActions = GetAvailableActions(snapshot);
		bool flag = _rng.NextDouble() < _epsilon;
		BrainActionV2 brainActionV;
		if (flag)
		{
			int index = _rng.Next(availableActions.Count);
			brainActionV = availableActions[index];
		}
		else
		{
			string bestId = "";
			double num = double.NegativeInfinity;
			foreach (BrainActionV2 item in availableActions)
			{
				if (orAdd.TryGetValue(item.ActionId, out var value) && value > num)
				{
					num = value;
					bestId = item.ActionId;
				}
			}
			brainActionV = availableActions.FirstOrDefault((BrainActionV2 a) => a.ActionId == bestId) ?? availableActions[0];
		}
		BrainActionV2 brainActionV2 = new BrainActionV2
		{
			Kind = brainActionV.Kind,
			Param = brainActionV.Param,
			TargetPid = ((snapshot.ForegroundPid > 4) ? new int?(snapshot.ForegroundPid) : null),
			TargetProcessName = snapshot.ForegroundProcessName,
			Reason = (flag ? "explore|" : "exploit|") + brainActionV.Reason
		};
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler;
		if (_patternRecognition != null)
		{
			SystemSpikeFeatures currentFeatures = new SystemSpikeFeatures
			{
				CpuLoad = (float)snapshot.CpuUsagePercent,
				RamUsedGB = (float)(snapshot.RamTotalMB - snapshot.AvailableRam) / 1024f,
				GpuLoad = (float)snapshot.GpuUsagePercent,
				CurrentFps = (float)snapshot.CurrentFps,
				DiskQueueLength = SystemMetricsCache.Instance.DiskQueueLength
			};
			float num2 = _patternRecognition.PredictSpikeRisk(currentFeatures);
			if (num2 > 0.8f)
			{
				ILoggingService logger = _logger;
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(91, 1);
				defaultInterpolatedStringHandler.AppendLiteral("[BRAIN-ML] Risco de congelamento agudo previsto (");
				defaultInterpolatedStringHandler.AppendFormatted(num2 * 100f, "F1");
				defaultInterpolatedStringHandler.AppendLiteral("%). Sobrepondo Q-Table com Ação Defensiva.");
				logger.LogWarning(defaultInterpolatedStringHandler.ToStringAndClear());
				return new BrainActionV2
				{
					Kind = BrainActionKind.SetForegroundPriority,
					Param = 2,
					TargetPid = brainActionV2.TargetPid,
					TargetProcessName = brainActionV2.TargetProcessName,
					Reason = "ML_SPIKE_PREVENTION"
				};
			}
		}
		if (_heuristics != null && brainActionV2.Kind != 0)
		{
			HardwareProfile hardwareProfile = await ResolveHardwareProfileAsync();
			HeuristicsContext context = new HeuristicsContext
			{
				TargetOptimization = brainActionV2.Kind.ToString(),
				HardwareProfile = hardwareProfile,
				Telemetry = new HardwareTelemetryMetrics
				{
					CpuLoad = snapshot.CpuUsagePercent,
					SystemRamUsedGB = (double)(snapshot.RamTotalMB - snapshot.AvailableRam) / 1024.0,
					GpuLoad = snapshot.GpuUsagePercent
				}
			};
			if (hardwareProfile == null)
			{
				ILoggingService logger2 = _logger;
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(114, 1);
				defaultInterpolatedStringHandler.AppendLiteral("[BRAIN-HEURISTICS] [WARN] HardwareProfile ainda não disponível. Heurísticas podem vetar ação ");
				defaultInterpolatedStringHandler.AppendFormatted(brainActionV2.Kind);
				defaultInterpolatedStringHandler.AppendLiteral(" por contexto incompleto.");
				logger2.LogWarning(defaultInterpolatedStringHandler.ToStringAndClear());
			}
			else
			{
				ILoggingService logger3 = _logger;
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(72, 5);
				defaultInterpolatedStringHandler.AppendLiteral("[BRAIN-HEURISTICS] [OK] Contexto injetado: CPU=");
				defaultInterpolatedStringHandler.AppendFormatted(hardwareProfile.CPUName);
				defaultInterpolatedStringHandler.AppendLiteral(", RAM=");
				defaultInterpolatedStringHandler.AppendFormatted(hardwareProfile.TotalRAMGB);
				defaultInterpolatedStringHandler.AppendLiteral("GB, GPU=");
				defaultInterpolatedStringHandler.AppendFormatted(hardwareProfile.GPUName);
				defaultInterpolatedStringHandler.AppendLiteral(", NVMe=");
				defaultInterpolatedStringHandler.AppendFormatted(hardwareProfile.HasNVMe);
				defaultInterpolatedStringHandler.AppendLiteral(", Tier=");
				defaultInterpolatedStringHandler.AppendFormatted(hardwareProfile.Tier);
				logger3.Log(LogLevel.Debug, LogCategory.Intelligence, defaultInterpolatedStringHandler.ToStringAndClear(), null, "BrainDecisionEngineV2");
			}
			OptimizationDecision result = await _heuristics.EvaluateOptimizationAsync(brainActionV2.Kind.ToString(), context);
			if (result.Recommendation == DecisionRecommendation.DoNothing)
			{
				ILoggingService logger4 = _logger;
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(64, 2);
				defaultInterpolatedStringHandler.AppendLiteral("[BRAIN-HEURISTICS] Veto! Ação ");
				defaultInterpolatedStringHandler.AppendFormatted(brainActionV2.Kind);
				defaultInterpolatedStringHandler.AppendLiteral(" foi bloqueada pelas heurísticas: ");
				defaultInterpolatedStringHandler.AppendFormatted(string.Join(" | ", result.AuditTrail?.ExecutedRules?.Select((RuleResult r) => r.RuleName) ?? Array.Empty<string>()));
				logger4.LogWarning(defaultInterpolatedStringHandler.ToStringAndClear());
				return new BrainActionV2
				{
					Kind = BrainActionKind.NoAction,
					TargetPid = brainActionV2.TargetPid,
					TargetProcessName = brainActionV2.TargetProcessName,
					Reason = "HEURISTIC_VETO"
				};
			}
			ILoggingService logger5 = _logger;
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(81, 4);
			defaultInterpolatedStringHandler.AppendLiteral("[BRAIN-HEURISTICS] [OK] Ação ");
			defaultInterpolatedStringHandler.AppendFormatted(brainActionV2.Kind);
			defaultInterpolatedStringHandler.AppendLiteral(" APROVADA pelas heurísticas. Score=");
			defaultInterpolatedStringHandler.AppendFormatted(result.FinalScore, "F1");
			defaultInterpolatedStringHandler.AppendLiteral(", Confidence=");
			defaultInterpolatedStringHandler.AppendFormatted(result.FinalConfidence);
			defaultInterpolatedStringHandler.AppendLiteral("%, Rec=");
			defaultInterpolatedStringHandler.AppendFormatted(result.Recommendation);
			logger5.Log(LogLevel.Debug, LogCategory.Intelligence, defaultInterpolatedStringHandler.ToStringAndClear(), null, "BrainDecisionEngineV2");
		}
		if (_vpisEngines != null && _vpisEngines.Any())
		{
			foreach (IVpisDiagnosticEngine vpisEngine in _vpisEngines)
			{
				PerformanceInsightEvent performanceInsightEvent = vpisEngine.AnalyzeTick();
				if (performanceInsightEvent != null && performanceInsightEvent.Severity == DiagnosticSeverity.Critical)
				{
					ILoggingService logger6 = _logger;
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(75, 2);
					defaultInterpolatedStringHandler.AppendLiteral("[BRAIN-VPIS] ");
					defaultInterpolatedStringHandler.AppendFormatted(vpisEngine.EngineName);
					defaultInterpolatedStringHandler.AppendLiteral(" detectou gargalo crítico: ");
					defaultInterpolatedStringHandler.AppendFormatted(performanceInsightEvent.Diagnosis);
					defaultInterpolatedStringHandler.AppendLiteral(". Abortando ação para evitar danos!");
					logger6.LogWarning(defaultInterpolatedStringHandler.ToStringAndClear());
					return new BrainActionV2
					{
						Kind = BrainActionKind.NoAction,
						TargetPid = brainActionV2.TargetPid,
						TargetProcessName = brainActionV2.TargetProcessName,
						Reason = "VPIS_CRITICAL_BLOCK"
					};
				}
			}
		}
		ILoggingService logger7 = _logger;
		defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(39, 5);
		defaultInterpolatedStringHandler.AppendLiteral("[BRAIN] Estado: (");
		defaultInterpolatedStringHandler.AppendFormatted(state);
		defaultInterpolatedStringHandler.AppendLiteral(") Ação: ");
		defaultInterpolatedStringHandler.AppendFormatted(brainActionV2.ActionId);
		defaultInterpolatedStringHandler.AppendLiteral(" e = ");
		defaultInterpolatedStringHandler.AppendFormatted(_epsilon, "F3");
		defaultInterpolatedStringHandler.AppendLiteral(" ep = ");
		defaultInterpolatedStringHandler.AppendFormatted(_episodes);
		defaultInterpolatedStringHandler.AppendLiteral(" (");
		defaultInterpolatedStringHandler.AppendFormatted(flag ? "EXPLORE" : "EXPLOIT");
		defaultInterpolatedStringHandler.AppendLiteral(")");
		logger7.Log(LogLevel.Debug, LogCategory.Intelligence, defaultInterpolatedStringHandler.ToStringAndClear(), null, "BrainDecisionEngineV2");
		_logger.LogDecision("ActionSelected", brainActionV2.Reason, brainActionV2.ActionId);
		return brainActionV2;
	}

	public void Learn(BrainStateKey from, BrainActionV2 action, double reward, BrainStateKey to)
	{
		_logger.LogEntry(nameof(Learn), ("from", from.CanonicalKey), ("action", action.ActionId), ("reward", reward));
		string canonicalKey = from.CanonicalKey;
		string canonicalKey2 = to.CanonicalKey;
		string actionId = action.ActionId;
		ConcurrentDictionary<string, double> orAdd = _q.GetOrAdd(canonicalKey, (string _) => InitActionMap());
		ConcurrentDictionary<string, double> orAdd2 = _q.GetOrAdd(canonicalKey2, (string _) => InitActionMap());
		double num = ((orAdd2.Count > 0) ? orAdd2.Values.Max() : 0.0);
		double value;
		double num2 = (orAdd.TryGetValue(actionId, out value) ? value : 0.0);
		double value2 = (orAdd[actionId] = num2 + 0.1 * (reward + 0.9 * num - num2));
		ILoggingService logger = _logger;
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(41, 6);
		defaultInterpolatedStringHandler.AppendLiteral("[BRAIN] Learn s: (");
		defaultInterpolatedStringHandler.AppendFormatted(from);
		defaultInterpolatedStringHandler.AppendLiteral(") a: ");
		defaultInterpolatedStringHandler.AppendFormatted(actionId);
		defaultInterpolatedStringHandler.AppendLiteral(" r: ");
		defaultInterpolatedStringHandler.AppendFormatted(reward, "+0.00;-0.00");
		defaultInterpolatedStringHandler.AppendLiteral(" s': (");
		defaultInterpolatedStringHandler.AppendFormatted(to);
		defaultInterpolatedStringHandler.AppendLiteral(") Q: ");
		defaultInterpolatedStringHandler.AppendFormatted(num2, "F3");
		defaultInterpolatedStringHandler.AppendLiteral(" ? ");
		defaultInterpolatedStringHandler.AppendFormatted(value2, "F3");
		logger.Log(LogLevel.Debug, LogCategory.Intelligence, defaultInterpolatedStringHandler.ToStringAndClear(), null, "BrainDecisionEngineV2");
			_logger.LogExit(nameof(Learn));
	}

	private void DecayEpsilon()
	{
		if (_epsilon > 0.05)
		{
			_epsilon = Math.Max(0.05, _epsilon - 0.0003);
			return;
		}
		if (!_hasReachedEpsilonMin)
		{
			_hasReachedEpsilonMin = true;
			_episodesSinceEpsilonMin = 0L;
			ILoggingService logger = _logger;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(44, 2);
			defaultInterpolatedStringHandler.AppendLiteral("[BRAIN] e = ");
			defaultInterpolatedStringHandler.AppendFormatted(0.05, "F3");
			defaultInterpolatedStringHandler.AppendLiteral(" (mínimo atingido em ");
			defaultInterpolatedStringHandler.AppendFormatted(_episodes);
			defaultInterpolatedStringHandler.AppendLiteral(" episódios)");
			logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
		}
		_episodesSinceEpsilonMin++;
		if (_episodesSinceEpsilonMin >= 1500)
		{
			_epsilon = 0.15;
			_hasReachedEpsilonMin = false;
			_episodesSinceEpsilonMin = 0L;
			ILoggingService logger2 = _logger;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(84, 2);
			defaultInterpolatedStringHandler.AppendLiteral("[BRAIN] Re-exploration: e resetado para ");
			defaultInterpolatedStringHandler.AppendFormatted(0.15, "F2");
			defaultInterpolatedStringHandler.AppendLiteral(" após ");
			defaultInterpolatedStringHandler.AppendFormatted(_episodes);
			defaultInterpolatedStringHandler.AppendLiteral(" episódios | nova janela de exploração");
			logger2.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
		}
	}

	private static ConcurrentDictionary<string, double> InitActionMap()
	{
		ConcurrentDictionary<string, double> concurrentDictionary = new ConcurrentDictionary<string, double>();
		BrainActionV2[] actionCatalog = ActionCatalog;
		foreach (BrainActionV2 brainActionV in actionCatalog)
		{
			concurrentDictionary[brainActionV.ActionId] = 0.0;
		}
		return concurrentDictionary;
	}

	private async Task<HardwareProfile?> ResolveHardwareProfileAsync()
	{
		if (_cachedHardwareProfile != null)
		{
			return _cachedHardwareProfile;
		}
		if (_hardwareProfileResolved)
		{
			return _cachedHardwareProfile;
		}
		_hardwareProfileResolved = true;
		try
		{
			SystemIntelligenceProfilerService service = ServiceLocator.GetService<SystemIntelligenceProfilerService>();
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler;
			if (service != null)
			{
				_cachedHardwareProfile = await service.GenerateHardwareProfileAsync();
				if (_cachedHardwareProfile != null)
				{
					ILoggingService logger = _logger;
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(61, 4);
					defaultInterpolatedStringHandler.AppendLiteral("[BRAIN] [OK] HardwareProfile resolvido: ");
					defaultInterpolatedStringHandler.AppendFormatted(_cachedHardwareProfile!.CPUName);
					defaultInterpolatedStringHandler.AppendLiteral(" | ");
					defaultInterpolatedStringHandler.AppendFormatted(_cachedHardwareProfile!.TotalRAMGB);
					defaultInterpolatedStringHandler.AppendLiteral("GB RAM | GPU=");
					defaultInterpolatedStringHandler.AppendFormatted(_cachedHardwareProfile!.GPUName);
					defaultInterpolatedStringHandler.AppendLiteral(" | Tier=");
					defaultInterpolatedStringHandler.AppendFormatted(_cachedHardwareProfile!.Tier);
					logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
					return _cachedHardwareProfile;
				}
				_logger.LogWarning("[BRAIN] [WARN] HardwareProfile retornou null. Usando fallback.");
			}
			else
			{
				_logger.LogWarning("[BRAIN] [WARN] SystemIntelligenceProfilerService não encontrado no DI. HardwareProfile será fallback.");
			}
			_cachedHardwareProfile = new HardwareProfile
			{
				CPUName = "Fallback CPU",
				CPUCores = Environment.ProcessorCount,
				LogicalProcessors = Environment.ProcessorCount,
				TotalRAMGB = (int)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024 / 1024),
				HasSSD = true,
				HasNVMe = false,
				HasDedicatedGPU = false,
				GPUName = "Fallback GPU",
				IsLaptop = false,
				Tier = HardwareTier.Mid,
				TrimEnabled = false
			};
			ILoggingService logger2 = _logger;
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(59, 2);
			defaultInterpolatedStringHandler.AppendLiteral("[BRAIN] ℹ\ufe0f Usando HardwareProfile fallback: ");
			defaultInterpolatedStringHandler.AppendFormatted(_cachedHardwareProfile!.TotalRAMGB);
			defaultInterpolatedStringHandler.AppendLiteral("GB RAM, ");
			defaultInterpolatedStringHandler.AppendFormatted(_cachedHardwareProfile!.CPUCores);
			defaultInterpolatedStringHandler.AppendLiteral(" cores.");
			logger2.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
			return _cachedHardwareProfile;
		}
		catch (Exception ex)
		{
			_logger.LogError("[BRAIN] [FAIL] Erro ao resolver HardwareProfile: " + ex.Message);
			return null;
		}
	}

	private List<BrainActionV2> GetAvailableActions(SensorSnapshot snapshot)
	{
		List<BrainActionV2> result;
		if (snapshot.Workload == WorkloadCategory.Game || (snapshot.CpuUsagePercent > 60.0 && snapshot.GpuUsagePercent > 60.0))
		{
			result = ActionCatalog.ToList();
		}
		else
		{
			result = ActionCatalog.Where((BrainActionV2 a) => a.Kind != BrainActionKind.EnableGamingMode || a.Param != 1).ToList();
			_logger.LogInfo("[BRAIN] GamingMode bloqueado: workload não Game (atual: " + snapshot.Workload + ")");
			if (snapshot.Workload == WorkloadCategory.Idle)
			{
				result = result.Where(a => a.Kind != BrainActionKind.EnableGamingMode).ToList();
				_logger?.LogWarning("[BRAIN] Bloqueando EnableGamingMode em contexto Idle — ação inválida");
			}
		}
		result = result.Where(a => a.Kind != BrainActionKind.NoAction).ToList();
		return result;
	}

	public async Task LoadAsync()
	{
		Stopwatch sw = Stopwatch.StartNew();
		long fileSize = 0L;
		try
		{
			if (!File.Exists(_qTablePath))
			{
				_logger.LogInfo("[BRAIN][LOAD] Q-Table não existe, primeira execução, exploração pura.");
				return;
			}
			fileSize = new FileInfo(_qTablePath).Length;
			ILoggingService logger = _logger;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(69, 2);
			defaultInterpolatedStringHandler.AppendLiteral("[BRAIN][LOAD] Iniciando carregamento de qtable.json (");
			defaultInterpolatedStringHandler.AppendFormatted(fileSize);
			defaultInterpolatedStringHandler.AppendLiteral(" bytes, Thread=");
			defaultInterpolatedStringHandler.AppendFormatted(Thread.CurrentThread.ManagedThreadId);
			defaultInterpolatedStringHandler.AppendLiteral(")");
			logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
			using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(10.0));
			using FileStream fs = new FileStream(_qTablePath, FileMode.Open, FileAccess.Read, FileShare.Read);
			QTableFile dto = await JsonSerializer.DeserializeAsync<QTableFile>((Stream)fs, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }, cts.Token).ConfigureAwait(continueOnCapturedContext: false);
			if (dto == null)
			{
				_logger.LogWarning("[BRAIN][LOAD] Q-Table deserializou como null — arquivo pode estar vazio ou corrompido. Recomeçando.");
				return;
			}
			_epsilon = Math.Clamp(dto.Epsilon, 0.05, 0.3);
			_episodes = dto.Episodes;
			foreach (QTableEntryDto entry in dto.Entries)
			{
				ConcurrentDictionary<string, double> map = new ConcurrentDictionary<string, double>();
				foreach (KeyValuePair<string, double> kv in entry.ActionValues)
				{
					map[kv.Key] = kv.Value;
				}
				BrainActionV2[] actionCatalog = ActionCatalog;
				foreach (BrainActionV2 a in actionCatalog)
				{
					map.TryAdd(a.ActionId, 0.0);
				}
				_q[entry.StateKey] = map;
				_visits[entry.StateKey] = entry.Visits;
			}
			ILoggingService logger2 = _logger;
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(75, 4);
			defaultInterpolatedStringHandler.AppendLiteral("[BRAIN][LOAD] Q-Table carregada: ");
			defaultInterpolatedStringHandler.AppendFormatted(_q.Count);
			defaultInterpolatedStringHandler.AppendLiteral(" estados | e = ");
			defaultInterpolatedStringHandler.AppendFormatted(_epsilon, "F3");
			defaultInterpolatedStringHandler.AppendLiteral(" | episódios = ");
			defaultInterpolatedStringHandler.AppendFormatted(_episodes);
			defaultInterpolatedStringHandler.AppendLiteral(" | Tempo: ");
			defaultInterpolatedStringHandler.AppendFormatted(sw.ElapsedMilliseconds);
			defaultInterpolatedStringHandler.AppendLiteral("ms");
			logger2.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
		}
		catch (OperationCanceledException)
		{
			ILoggingService logger3 = _logger;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(111, 1);
			defaultInterpolatedStringHandler.AppendLiteral("[BRAIN][LOAD] TIMEOUT após 10s ao carregar qtable.json (");
			defaultInterpolatedStringHandler.AppendFormatted(fileSize);
			defaultInterpolatedStringHandler.AppendLiteral(" bytes). Arquivo pode estar corrompido ou muito grande.");
			logger3.LogError(defaultInterpolatedStringHandler.ToStringAndClear());
		}
		catch (Exception ex3)
		{
			Exception ex = ex3;
			ILoggingService logger4 = _logger;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(51, 3);
			defaultInterpolatedStringHandler.AppendLiteral("[BRAIN][LOAD] Falha ao carregar Q-Table: ");
			defaultInterpolatedStringHandler.AppendFormatted(ex.GetType().Name);
			defaultInterpolatedStringHandler.AppendLiteral(": ");
			defaultInterpolatedStringHandler.AppendFormatted(ex.Message);
			defaultInterpolatedStringHandler.AppendLiteral("\nStack: ");
			defaultInterpolatedStringHandler.AppendFormatted(ex.StackTrace);
			logger4.LogError(defaultInterpolatedStringHandler.ToStringAndClear(), ex);
		}
		finally
		{
			sw.Stop();
		}
	}

	public async Task SaveAsync()
	{
		Stopwatch sw = Stopwatch.StartNew();
		try
		{
			string tmp = _qTablePath + ".tmp";
			using (FileStream fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
			using (Utf8JsonWriter writer = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = false }))
			{
				writer.WriteStartObject();
				writer.WriteNumber("version", 2);
				writer.WriteNumber("epsilon", _epsilon);
				writer.WriteNumber("episodes", _episodes);
				writer.WriteString("savedUtc", DateTime.UtcNow);

				writer.WriteStartArray("entries");
				foreach (var kv in _q)
				{
					writer.WriteStartObject();
					writer.WriteString("state", kv.Key);
					writer.WriteStartObject("actions");
					foreach (var p in kv.Value)
						writer.WriteNumber(p.Key, p.Value);
					writer.WriteEndObject();
					writer.WriteNumber("visits", _visits.TryGetValue(kv.Key, out long visitCount) ? visitCount : 0);
					writer.WriteEndObject();
				}
				writer.WriteEndArray();
				writer.WriteEndObject();
				await writer.FlushAsync().ConfigureAwait(false);
			}
			File.Move(tmp, _qTablePath, overwrite: true);
			ILoggingService logger = _logger;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(64, 4);
			defaultInterpolatedStringHandler.AppendLiteral("[BRAIN][SAVE] Q-Table salva: ");
			defaultInterpolatedStringHandler.AppendFormatted(_q.Count);
			defaultInterpolatedStringHandler.AppendLiteral(" estados | e = ");
			defaultInterpolatedStringHandler.AppendFormatted(_epsilon, "F3");
			defaultInterpolatedStringHandler.AppendLiteral(" | ep = ");
			defaultInterpolatedStringHandler.AppendFormatted(_episodes);
			defaultInterpolatedStringHandler.AppendLiteral(" | Tempo: ");
			defaultInterpolatedStringHandler.AppendFormatted(sw.ElapsedMilliseconds);
			defaultInterpolatedStringHandler.AppendLiteral("ms");
			logger.Log(LogLevel.Trace, LogCategory.Intelligence, defaultInterpolatedStringHandler.ToStringAndClear(), null, "BrainDecisionEngineV2");
		}
		catch (Exception ex2)
		{
			Exception ex = ex2;
			ILoggingService logger2 = _logger;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(49, 3);
			defaultInterpolatedStringHandler.AppendLiteral("[BRAIN][SAVE] Falha ao salvar Q-Table: ");
			defaultInterpolatedStringHandler.AppendFormatted(ex.GetType().Name);
			defaultInterpolatedStringHandler.AppendLiteral(": ");
			defaultInterpolatedStringHandler.AppendFormatted(ex.Message);
			defaultInterpolatedStringHandler.AppendLiteral("\nStack: ");
			defaultInterpolatedStringHandler.AppendFormatted(ex.StackTrace);
			logger2.LogError(defaultInterpolatedStringHandler.ToStringAndClear(), ex);
		}
		finally
		{
			sw.Stop();
		}
	}
}
