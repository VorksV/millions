using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Core.Brain.V2;

public sealed class BrainStateOrchestrator
{
	private readonly ILoggingService _logger;

	private readonly object _lock = new object();

	private BrainOperationalState _currentState = BrainOperationalState.Balanced;

	private DateTime _lastStateChangeUtc = DateTime.MinValue;

	private readonly Queue<SensorSnapshot> _history = new Queue<SensorSnapshot>();

	private const int HistoryMaxItems = 60;

	public BrainOperationalState CurrentState
	{
		get
		{
			lock (_lock)
			{
				return _currentState;
			}
		}
	}

	public BrainStateOrchestrator(ILoggingService logger)
	{
		_logger = logger ?? throw new ArgumentNullException("logger");
	}

	public void Observe(SensorSnapshot snap)
	{
		_logger.LogEntry(nameof(Observe), ("workload", snap.Workload), ("fg", snap.ForegroundProcessName));
		lock (_lock)
		{
			_history.Enqueue(snap);
			while (_history.Count > 60)
			{
				_history.Dequeue();
			}
		}
	}

	public BrainConfidenceScore EvaluateConfidenceForState(BrainOperationalState targetState, SensorSnapshot current)
	{
		_logger.LogEntry(nameof(EvaluateConfidenceForState), ("target", targetState));
		double score = 0.0;
		bool isCritical = false;
		string primaryReason = "Sem histórico suficiente";
		List<SensorSnapshot> historySnapshot;
		lock (_lock)
		{
			if (_history.Count < 3)
			{
				return new BrainConfidenceScore
				{
					Score = 0.0,
					PrimaryReason = primaryReason,
					IsCritical = false
				};
			}
			historySnapshot = _history.ToList();
		}
		List<SensorSnapshot> source = historySnapshot.TakeLast(5).ToList();
		List<SensorSnapshot> source2 = historySnapshot.Take(10).ToList();
		double num = source.Average((SensorSnapshot s) => Math.Max(s.CpuTemperatureC, s.GpuTemperatureC));
		double num2 = source2.Average((SensorSnapshot s) => Math.Max(s.CpuTemperatureC, s.GpuTemperatureC));
		double num3 = num - num2;
		double num4 = source.Average((SensorSnapshot s) => s.FrameTimeVarianceMs);
		double num5 = source2.Average((SensorSnapshot s) => s.FrameTimeVarianceMs);
		switch (targetState)
		{
		case BrainOperationalState.ThermalRelief:
			if (num > 95.0)
			{
				score = 100.0;
				isCritical = true;
				primaryReason = "Temperatura crítica sustentada > 95C";
			}
			else if (num > 85.0 && num3 > 2.0)
			{
				score = 70.0;
				primaryReason = "Tendência de aquecimento severa";
			}
			else if (num > 85.0 && num3 <= 0.0)
			{
				score = 30.0;
				primaryReason = "Temperatura alta mas estabilizada";
			}
			else
			{
				score = 10.0;
				primaryReason = "Temperatura normal/Spike isolado";
			}
			break;
		case BrainOperationalState.GameMode:
			if (current.Workload == WorkloadCategory.Game && num < 85.0)
			{
				score = 90.0;
				primaryReason = "Gaming detectado com boa margem térmica";
			}
			break;
		}
		return new BrainConfidenceScore
		{
			Score = score,
			PrimaryReason = primaryReason,
			IsCritical = isCritical
		};
	}

	public bool RequestStateTransition(BrainOperationalState targetState, SensorSnapshot current)
	{
		_logger.LogEntry(nameof(RequestStateTransition), ("target", targetState), ("current", _currentState));
		lock (_lock)
		{
			if (_currentState == targetState)
			{
				return false;
			}
			BrainConfidenceScore brainConfidenceScore = EvaluateConfidenceForState(targetState, current);
			TimeSpan timeSpan = TimeSpan.FromSeconds(30.0);
			if (brainConfidenceScore.IsCritical)
			{
				timeSpan = TimeSpan.Zero;
			}
			else if (targetState == BrainOperationalState.GameMode)
			{
				timeSpan = TimeSpan.FromSeconds(5.0);
			}
			else if (_currentState == BrainOperationalState.ThermalRelief && targetState == BrainOperationalState.GameMode)
			{
				timeSpan = TimeSpan.FromSeconds(45.0);
			}
			TimeSpan timeSpan2 = DateTime.UtcNow - _lastStateChangeUtc;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler;
			if (timeSpan2 < timeSpan && !brainConfidenceScore.IsCritical)
			{
				_logger.LogDecision("TransitionDenied", $"Hysteresis: {brainConfidenceScore.PrimaryReason}", targetState);
				ILoggingService logger = _logger;
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(113, 3);
				defaultInterpolatedStringHandler.AppendLiteral("[ORCHESTRATOR] Bloqueado por Histerese (Anti-Bouncing): Transição para ");
				defaultInterpolatedStringHandler.AppendFormatted(targetState);
				defaultInterpolatedStringHandler.AppendLiteral(" ignorada. Cooldown restante: ");
				defaultInterpolatedStringHandler.AppendFormatted((timeSpan - timeSpan2).TotalSeconds, "F1");
				defaultInterpolatedStringHandler.AppendLiteral("s | Motivo: ");
				defaultInterpolatedStringHandler.AppendFormatted(brainConfidenceScore.PrimaryReason);
				logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
				return false;
			}
			if (brainConfidenceScore.Score < 60.0 && !brainConfidenceScore.IsCritical)
			{
				_logger.LogDecision("TransitionDenied", $"Low confidence: {brainConfidenceScore.PrimaryReason}", targetState);
				ILoggingService logger2 = _logger;
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(72, 3);
				defaultInterpolatedStringHandler.AppendLiteral("[ORCHESTRATOR] Confiança insuficiente (");
				defaultInterpolatedStringHandler.AppendFormatted(brainConfidenceScore.Score, "F1");
				defaultInterpolatedStringHandler.AppendLiteral("%) para transição para ");
				defaultInterpolatedStringHandler.AppendFormatted(targetState);
				defaultInterpolatedStringHandler.AppendLiteral(". Motivo: ");
				defaultInterpolatedStringHandler.AppendFormatted(brainConfidenceScore.PrimaryReason);
				logger2.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
				return false;
			}
			ILoggingService logger3 = _logger;
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(64, 4);
			defaultInterpolatedStringHandler.AppendLiteral("[ORCHESTRATOR] Transição de ");
			defaultInterpolatedStringHandler.AppendFormatted(_currentState);
			defaultInterpolatedStringHandler.AppendLiteral(" para ");
			defaultInterpolatedStringHandler.AppendFormatted(targetState);
			defaultInterpolatedStringHandler.AppendLiteral(" aprovada! Score: ");
			defaultInterpolatedStringHandler.AppendFormatted(brainConfidenceScore.Score, "F1");
			defaultInterpolatedStringHandler.AppendLiteral("% | Motivo: ");
			defaultInterpolatedStringHandler.AppendFormatted(brainConfidenceScore.PrimaryReason);
			logger3.LogSuccess(defaultInterpolatedStringHandler.ToStringAndClear());
			_logger.LogTransition(_currentState.ToString(), targetState.ToString(), brainConfidenceScore.PrimaryReason);
			_currentState = targetState;
			_lastStateChangeUtc = DateTime.UtcNow;
			return true;
		}
	}

	public void ForceStateTransition(BrainOperationalState targetState)
	{
		_logger.LogEntry(nameof(ForceStateTransition), ("target", targetState), ("current", _currentState));
		lock (_lock)
		{
			if (_currentState != targetState)
			{
				_logger.LogTransition(_currentState.ToString(), targetState.ToString(), "forced");
				ILoggingService logger = _logger;
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(43, 2);
				defaultInterpolatedStringHandler.AppendLiteral("[ORCHESTRATOR] Transição FORÇADA de ");
				defaultInterpolatedStringHandler.AppendFormatted(_currentState);
				defaultInterpolatedStringHandler.AppendLiteral(" para ");
				defaultInterpolatedStringHandler.AppendFormatted(targetState);
				defaultInterpolatedStringHandler.AppendLiteral("!");
				logger.LogSuccess(defaultInterpolatedStringHandler.ToStringAndClear());
				_currentState = targetState;
				_lastStateChangeUtc = DateTime.UtcNow;
			}
		}
	}

	public void Dispose()
	{
	}
}
