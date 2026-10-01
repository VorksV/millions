using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Core.Brain.V2.AntiStutter;

public sealed class AntiStutterMonitor : IDisposable
{
	private readonly ILoggingService _logger;

	private readonly AntiStutterProfiler _profiler;

	private readonly AntiStutterAnalyzer _analyzer;

	private readonly ConcurrentDictionary<string, ActionImpactResult> _impactHistory = new ConcurrentDictionary<string, ActionImpactResult>();

	private readonly ConcurrentQueue<ActionImpactResult> _recentResults = new ConcurrentQueue<ActionImpactResult>();

	private readonly OptimizationStats _stats = new OptimizationStats();

	private readonly CancellationTokenSource _cts = new CancellationTokenSource();

	private static readonly TimeSpan MeasurementDelay = TimeSpan.FromSeconds(3.0);

	private static readonly TimeSpan MeasurementWindow = TimeSpan.FromSeconds(5.0);

	private const double MIN_IMPROVEMENT_TO_KEEP = 5.0;

	private const double REGRESSION_THRESHOLD = -10.0;

	public event EventHandler<ActionImpactResult>? ImpactMeasured;

	public event EventHandler<string>? RevertRecommended;

	public AntiStutterMonitor(ILoggingService logger, AntiStutterProfiler profiler, AntiStutterAnalyzer analyzer)
	{
		_logger = logger ?? throw new ArgumentNullException("logger");
		_profiler = profiler ?? throw new ArgumentNullException("profiler");
		_analyzer = analyzer ?? throw new ArgumentNullException("analyzer");
	}

	public void RecordActionExecution(string actionId, string actionName, double beforeSeverity, BottleneckType bottleneck)
	{
		if (beforeSeverity <= 0.0)
		{
			beforeSeverity = 0.01;
		}
		ActionImpactResult value = new ActionImpactResult
		{
			ActionId = actionId,
			ActionName = actionName,
			ExecutedAt = DateTime.UtcNow,
			BeforeSeverity = beforeSeverity,
			TargetBottleneck = bottleneck
		};
		_impactHistory[actionId] = value;
		Interlocked.Increment(ref _stats.TotalActionsExecuted);
		ILoggingService logger = _logger;
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(35, 2);
		defaultInterpolatedStringHandler.AppendLiteral("[Monitor] Tracking: ");
		defaultInterpolatedStringHandler.AppendFormatted(actionName);
		defaultInterpolatedStringHandler.AppendLiteral(" | SevBefore = ");
		defaultInterpolatedStringHandler.AppendFormatted(beforeSeverity, "F2");
		logger.LogDebug(defaultInterpolatedStringHandler.ToStringAndClear());
		MeasureImpactAsync(actionId, _cts.Token);
	}

	private async Task MeasureImpactAsync(string actionId, CancellationToken ct)
	{
		try
		{
			await Task.Delay(MeasurementDelay, ct);
			List<double> samples = new List<double>();
			Stopwatch sw = Stopwatch.StartNew();
			while (sw.Elapsed < MeasurementWindow && !ct.IsCancellationRequested)
			{
				AntiStutterSnapshot snapshot = _profiler.CurrentSnapshot;
				if (snapshot != null)
				{
					BottleneckAnalysis analysis = _analyzer.Analyze(snapshot);
					samples.Add(analysis.SeverityScore);
				}
				await Task.Delay(400, ct);
			}
			if (!samples.Any())
			{
				return;
			}
			double after = samples.Average();
			if (_impactHistory.TryGetValue(actionId, out var original))
			{
				double improvement = (original.BeforeSeverity - after) / original.BeforeSeverity * 100.0;
				bool regression = improvement < -10.0;
				bool success = improvement >= 5.0;
				ActionImpactResult result = new ActionImpactResult
				{
					ActionId = original.ActionId,
					ActionName = original.ActionName,
					ExecutedAt = original.ExecutedAt,
					MeasuredAt = DateTime.UtcNow,
					BeforeSeverity = original.BeforeSeverity,
					AfterSeverity = after,
					ImprovementPercent = improvement,
					ImpactVerified = (success || regression),
					ShouldRevert = regression,
					TargetBottleneck = original.TargetBottleneck,
					Reasoning = BuildReasoning(improvement)
				};
				_impactHistory[actionId] = result;
				_recentResults.Enqueue(result);
				while (_recentResults.Count > 100)
				{
					_recentResults.TryDequeue(out var _);
				}
				UpdateStats(result);
				this.ImpactMeasured?.Invoke(this, result);
				if (regression)
				{
					ILoggingService logger = _logger;
					DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(24, 2);
					defaultInterpolatedStringHandler.AppendLiteral("[Monitor] REGRESSION ");
					defaultInterpolatedStringHandler.AppendFormatted(result.ActionName);
					defaultInterpolatedStringHandler.AppendLiteral(": ");
					defaultInterpolatedStringHandler.AppendFormatted(improvement, "F1");
					defaultInterpolatedStringHandler.AppendLiteral("%");
					logger.LogWarning(defaultInterpolatedStringHandler.ToStringAndClear());
					this.RevertRecommended?.Invoke(this, actionId);
				}
				else if (success)
				{
					ILoggingService logger2 = _logger;
					DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(21, 2);
					defaultInterpolatedStringHandler.AppendLiteral("[Monitor] SUCCESS ");
					defaultInterpolatedStringHandler.AppendFormatted(result.ActionName);
					defaultInterpolatedStringHandler.AppendLiteral(": ");
					defaultInterpolatedStringHandler.AppendFormatted(improvement, "F1");
					defaultInterpolatedStringHandler.AppendLiteral("%");
					logger2.LogSuccess(defaultInterpolatedStringHandler.ToStringAndClear());
				}
				else
				{
					ILoggingService logger3 = _logger;
					DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(21, 2);
					defaultInterpolatedStringHandler.AppendLiteral("[Monitor] NEUTRAL ");
					defaultInterpolatedStringHandler.AppendFormatted(result.ActionName);
					defaultInterpolatedStringHandler.AppendLiteral(": ");
					defaultInterpolatedStringHandler.AppendFormatted(improvement, "F1");
					defaultInterpolatedStringHandler.AppendLiteral("%");
					logger3.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
				}
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex3)
		{
			Exception ex = ex3;
			_logger.LogDebug("[Monitor] Error: " + ex.Message);
		}
	}

	private void UpdateStats(ActionImpactResult r)
	{
		ActionImpactResult r2 = r;
		if (r2.ImprovementPercent > 0.0)
		{
			Interlocked.Increment(ref _stats.SuccessfulOptimizations);
		}
		else if (r2.ShouldRevert)
		{
			Interlocked.Increment(ref _stats.FailedOptimizations);
			Interlocked.Increment(ref _stats.RevertedActions);
		}
		_stats.ActionSuccessCount.AddOrUpdate(r2.ActionName, (r2.ImprovementPercent > 0.0) ? 1 : 0, (string _, int old) => old + ((r2.ImprovementPercent > 0.0) ? 1 : 0));
		int totalActionsExecuted = _stats.TotalActionsExecuted;
		_stats.AverageImprovementPercent = (_stats.AverageImprovementPercent * (double)(totalActionsExecuted - 1) + r2.ImprovementPercent) / (double)totalActionsExecuted;
	}

	private static string BuildReasoning(double improvement)
	{
		if (improvement > 20.0)
		{
			return "High improvement";
		}
		if (improvement > 5.0)
		{
			return "Moderate improvement";
		}
		if (improvement > -5.0)
		{
			return "No significant change";
		}
		if (improvement > -10.0)
		{
			return "Minor regression";
		}
		return "Severe regression revert recommended";
	}

	public IReadOnlyList<ActionImpactResult> GetRecentResults()
	{
		return _recentResults.ToList().AsReadOnly();
	}

	public OptimizationStats GetStats()
	{
		return _stats;
	}

	public void Dispose()
	{
		_cts.Cancel();
		_cts.Dispose();
	}
}
