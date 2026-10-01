using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.SystemIntelligenceProfiler;

namespace VoltrisOptimizer.Core.Optimization;

public class UnifiedOptimizationPipeline
{
	private readonly ILoggingService _logger;

	private readonly InstantOptimizationEngine _instantEngine;

	private readonly OptimizationDecisionEngine _decisionEngine;

	private readonly SystemIntelligenceProfilerService _profiler;

	private readonly IServiceProvider _serviceProvider;

	private readonly Dictionary<string, DateTime> _lastExecutions = new Dictionary<string, DateTime>();

	private readonly object _executionLock = new object();

	public UnifiedOptimizationPipeline(ILoggingService logger, InstantOptimizationEngine instantEngine, OptimizationDecisionEngine decisionEngine, SystemIntelligenceProfilerService profiler, IServiceProvider serviceProvider)
	{
		_logger = logger ?? throw new ArgumentNullException("logger");
		_instantEngine = instantEngine ?? throw new ArgumentNullException("instantEngine");
		_decisionEngine = decisionEngine ?? throw new ArgumentNullException("decisionEngine");
		_profiler = profiler ?? throw new ArgumentNullException("profiler");
		_serviceProvider = serviceProvider ?? throw new ArgumentNullException("serviceProvider");
	}

	public async Task<PipelineResult> ExecuteOptimizationPipelineAsync(OptimizationMode mode, OptimizationContext context, IProgress<PipelineProgress>? progress = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		string pipelineId = Guid.NewGuid().ToString("N").Substring(0, 8);
		_logger.LogInfo(string.Format(LocalizationService.Instance.GetString("PipelineStarting"), pipelineId, mode));
		GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("PipelineExecuting"), isPriority: true);
		TrackedProgress<PipelineProgress> globalProgress = ProgressBridge.WrapExisting(LocalizationService.Instance.GetString("PipelineOptimizing"), progress);
		progress = globalProgress;
		try
		{
			progress?.Report(new PipelineProgress
			{
				Phase = LocalizationService.Instance.GetString("PipelinePhaseAnalysis"),
				Progress = 0,
				Message = LocalizationService.Instance.GetString("PipelineAnalyzingSystem")
			});
			SystemAnalysisResult analysisResult = await AnalyzeSystemAsync(cancellationToken);
			if (!analysisResult.Success)
			{
				return new PipelineResult
				{
					Success = false,
					PipelineId = pipelineId,
					TotalTimeMs = stopwatch.ElapsedMilliseconds,
					Error = analysisResult.Error,
					Phase = "Análise"
				};
			}
			progress?.Report(new PipelineProgress
			{
					Phase = LocalizationService.Instance.GetString("PipelinePhaseAnalysis"),
					Progress = 100,
					Message = LocalizationService.Instance.GetString("PipelineSystemAnalyzed")
			});
			progress?.Report(new PipelineProgress
			{
				Phase = LocalizationService.Instance.GetString("PipelinePhaseSelection"),
				Progress = 0,
				Message = LocalizationService.Instance.GetString("PipelineSelectingOptimizations")
			});
			List<SelectedOptimization> selectedOptimizations = await SelectOptimizationsAsync(mode, analysisResult.Profile, context);
			progress?.Report(new PipelineProgress
			{
				Phase = LocalizationService.Instance.GetString("PipelinePhaseSelection"),
				Progress = 100,
				Message = string.Format(LocalizationService.Instance.GetString("PipelineNSelected"), selectedOptimizations.Count)
			});
			progress?.Report(new PipelineProgress
			{
				Phase = LocalizationService.Instance.GetString("PipelinePhaseValidation"),
				Progress = 0,
				Message = LocalizationService.Instance.GetString("PipelineValidatingOptimizations")
			});
			Dictionary<OptimizationGroup, List<SelectedOptimization>> optimizationGroups = await GroupOptimizationsAsync(selectedOptimizations, analysisResult.Profile);
			progress?.Report(new PipelineProgress
			{
				Phase = LocalizationService.Instance.GetString("PipelinePhaseValidation"),
				Progress = 100,
				Message = LocalizationService.Instance.GetString("PipelineOptimizationsValidated")
			});
			progress?.Report(new PipelineProgress
			{
				Phase = LocalizationService.Instance.GetString("PipelinePhaseExecution"),
				Progress = 0,
				Message = LocalizationService.Instance.GetString("PipelineExecutingOptimizations")
			});
			ExecutionResult executionResult = await ExecuteOptimizationGroupsAsync(optimizationGroups, context, progress, cancellationToken);
			progress?.Report(new PipelineProgress
			{
				Phase = LocalizationService.Instance.GetString("PipelinePhaseExecution"),
				Progress = 100,
				Message = LocalizationService.Instance.GetString("PipelineExecutionCompleted")
			});
			progress?.Report(new PipelineProgress
			{
				Phase = LocalizationService.Instance.GetString("PipelinePhaseFinalization"),
				Progress = 0,
				Message = LocalizationService.Instance.GetString("PipelineFinalizing")
			});
			await PostProcessAsync(executionResult, analysisResult.Profile);
			progress?.Report(new PipelineProgress
			{
				Phase = LocalizationService.Instance.GetString("PipelinePhaseFinalization"),
				Progress = 100,
				Message = LocalizationService.Instance.GetString("PipelineCompleted")
			});
			stopwatch.Stop();
			PipelineResult obj2 = new PipelineResult
			{
				Success = (executionResult.SuccessCount > 0),
				PipelineId = pipelineId,
				TotalTimeMs = stopwatch.ElapsedMilliseconds,
				AppliedOptimizations = executionResult.SuccessCount,
				FailedOptimizations = executionResult.FailureCount,
				SkippedOptimizations = executionResult.SkippedCount,
				HardwareProfile = analysisResult.Profile,
				Mode = mode
			};
			obj2.Details = string.Format(LocalizationService.Instance.GetString("PipelineNApplied"), executionResult.SuccessCount, selectedOptimizations.Count);
			PipelineResult result = obj2;
			_logger.LogSuccess(string.Format(LocalizationService.Instance.GetString("PipelineCompletedLog"), pipelineId, stopwatch.ElapsedMilliseconds, executionResult.SuccessCount, selectedOptimizations.Count));
			GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("PipelineCompleted"));
			return result;
		}
		catch (OperationCanceledException)
		{
			_logger.LogWarning("[Pipeline: " + pipelineId + "] " + LocalizationService.Instance.GetString("PipelineCancelledByUser"));
			GlobalProgressService.Instance.FailOperation(LocalizationService.Instance.GetString("PipelineCancelled"));
			return new PipelineResult
			{
				Success = false,
				PipelineId = pipelineId,
				TotalTimeMs = stopwatch.ElapsedMilliseconds,
				Error = LocalizationService.Instance.GetString("PipelineCancelled"),
				Phase = LocalizationService.Instance.GetString("PipelineCancelled")
			};
		}
		catch (Exception ex3)
		{
			Exception ex = ex3;
			_logger.LogError("[Pipeline: " + pipelineId + "] " + LocalizationService.Instance.GetString("PipelineFatalError"), ex);
			GlobalProgressService.Instance.FailOperation(string.Format(LocalizationService.Instance.GetString("PipelineErrorPrefix"), ex.Message));
			return new PipelineResult
			{
				Success = false,
				PipelineId = pipelineId,
				TotalTimeMs = stopwatch.ElapsedMilliseconds,
				Error = ex.Message,
				Phase = LocalizationService.Instance.GetString("PipelineError")
			};
		}
	}

	public async Task<PipelineResult> ExecuteQuickPipelineAsync(OptimizationContext context)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		_logger.LogInfo("[QuickPipeline] " + LocalizationService.Instance.GetString("PipelineQuickStarting"));
		try
		{
			var instantResult = new
			{
				Success = false,
				OptimizationsApplied = 0,
				Details = LocalizationService.Instance.GetString("PipelineQuickDisabled")
			};
			stopwatch.Stop();
			return new PipelineResult
			{
				Success = instantResult.Success,
				PipelineId = "QUICK",
				TotalTimeMs = stopwatch.ElapsedMilliseconds,
				AppliedOptimizations = instantResult.OptimizationsApplied,
				Details = instantResult.Details
			};
		}
		catch (Exception ex2)
		{
			Exception ex = ex2;
			_logger.LogError("[QuickPipeline] " + LocalizationService.Instance.GetString("PipelineQuickError"), ex);
			return new PipelineResult
			{
				Success = false,
				PipelineId = "QUICK",
				TotalTimeMs = stopwatch.ElapsedMilliseconds,
				Error = ex.Message,
				Mode = OptimizationMode.Instant
			};
		}
	}

	public bool CanExecuteOptimization(string optimizationName, TimeSpan? cooldown = null)
	{
		TimeSpan timeSpan = cooldown ?? TimeSpan.FromMinutes(5.0);
		string key = optimizationName.ToLowerInvariant();
		lock (_executionLock)
		{
			if (_lastExecutions.TryGetValue(key, out var value))
			{
				return DateTime.UtcNow - value >= timeSpan;
			}
			return true;
		}
	}

	public void RegisterOptimizationExecution(string optimizationName)
	{
		lock (_executionLock)
		{
			_lastExecutions[optimizationName.ToLowerInvariant()] = DateTime.UtcNow;
		}
	}

	private async Task<SystemAnalysisResult> AnalyzeSystemAsync(CancellationToken cancellationToken)
	{
		try
		{
			await _profiler.EnsureProfileLoadedAsync();
			HardwareProfile profile = await _profiler.GenerateHardwareProfileAsync();
			SystemScores scores = _profiler.CalculateSystemScores(profile);
			HardwareProfile localProfile = profile;
			return new SystemAnalysisResult
			{
				Success = true,
				Profile = localProfile,
				Scores = scores
			};
		}
		catch (Exception ex2)
		{
			Exception ex = ex2;
			return new SystemAnalysisResult
			{
				Success = false,
				Error = ex.Message
			};
		}
	}

	private async Task<List<SelectedOptimization>> SelectOptimizationsAsync(OptimizationMode mode, HardwareProfile profile, OptimizationContext context)
	{
		List<OptimizationRecommendation> recommendations = await _decisionEngine.GetRecommendedOptimizationsAsync(context);
		List<SelectedOptimization> selected = new List<SelectedOptimization>();
		foreach (OptimizationRecommendation rec in recommendations)
		{
			if (IsOptimizationValidForMode(rec.Action, mode))
			{
				if (!CanExecuteOptimization(rec.Action))
				{
					_logger.LogInfo("[Pipeline] " + string.Format(LocalizationService.Instance.GetString("PipelineCooldown"), rec.Action));
					continue;
				}
				if (await _decisionEngine.IsOptimizationActiveAsync(rec.Action))
				{
					_logger.LogInfo("[Pipeline] Otimização já ativa: " + rec.Action);
					continue;
				}
				selected.Add(new SelectedOptimization
				{
					Name = rec.Action,
					Priority = rec.Priority,
					Impact = rec.Impact,
					Reason = rec.Reason,
					EstimatedTimeMs = EstimateExecutionTime(rec.Action, profile)
				});
			}
		}
		return (from o in selected
			orderby GetPriorityWeight(o.Priority) descending, GetImpactWeight(o.Impact) descending
			select o).ToList();
	}

	private async Task<Dictionary<OptimizationGroup, List<SelectedOptimization>>> GroupOptimizationsAsync(List<SelectedOptimization> optimizations, HardwareProfile profile)
	{
		Dictionary<OptimizationGroup, List<SelectedOptimization>> groups = new Dictionary<OptimizationGroup, List<SelectedOptimization>>();
		foreach (SelectedOptimization opt in optimizations)
		{
			OptimizationGroup group2 = ClassifyOptimizationGroup(opt.Name);
			if (!groups.ContainsKey(group2))
			{
				groups[group2] = new List<SelectedOptimization>();
			}
			groups[group2].Add(opt);
		}
		foreach (List<SelectedOptimization> group in groups.Values)
		{
			group.Sort((SelectedOptimization a, SelectedOptimization b) => GetPriorityWeight(b.Priority).CompareTo(GetPriorityWeight(a.Priority)));
		}
		return groups;
	}

	private async Task<ExecutionResult> ExecuteOptimizationGroupsAsync(Dictionary<OptimizationGroup, List<SelectedOptimization>> groups, OptimizationContext context, IProgress<PipelineProgress>? progress, CancellationToken cancellationToken)
	{
		ExecutionResult result = new ExecutionResult();
		int totalOptimizations = groups.Values.Sum((List<SelectedOptimization> g) => g.Count);
		int completedCount = 0;
		OptimizationGroup[] groupOrder = new OptimizationGroup[3]
		{
			OptimizationGroup.Safe,
			OptimizationGroup.Conditional,
			OptimizationGroup.Risky
		};
		OptimizationGroup[] array = groupOrder;
		foreach (OptimizationGroup groupType in array)
		{
			if (!groups.TryGetValue(groupType, out var optimizations))
			{
				continue;
			}
			cancellationToken.ThrowIfCancellationRequested();
			foreach (SelectedOptimization opt in optimizations)
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					progress?.Report(new PipelineProgress
					{
						Phase = "Execução",
						Progress = (int)((double)completedCount * 100.0 / (double)totalOptimizations),
						Message = "Executando: " + opt.Name
					});
					ExecutionResult execResult = await _decisionEngine.ExecuteOptimizationAsync(opt.Name, context);
					if (execResult.Success)
					{
						result.SuccessCount++;
						RegisterOptimizationExecution(opt.Name);
						ILoggingService logger = _logger;
						DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(16, 2);
						defaultInterpolatedStringHandler.AppendLiteral("[Pipeline] ");
						defaultInterpolatedStringHandler.AppendFormatted(opt.Name);
						defaultInterpolatedStringHandler.AppendLiteral(" (");
						defaultInterpolatedStringHandler.AppendFormatted(execResult.ExecutionTimeMs);
						defaultInterpolatedStringHandler.AppendLiteral("ms)");
						logger.LogSuccess(defaultInterpolatedStringHandler.ToStringAndClear());
					}
					else
					{
						result.FailureCount++;
						_logger.LogWarning("[Pipeline] " + opt.Name + ": " + execResult.Reason);
					}
				}
				catch (Exception ex)
				{
					result.FailureCount++;
					_logger.LogError("[Pipeline] Erro em " + opt.Name, ex);
				}
				completedCount++;
			}
			optimizations = null;
		}
		return result;
	}

	private async Task PostProcessAsync(ExecutionResult executionResult, HardwareProfile profile)
	{
		try
		{
			await CleanupOldExecutionsAsync();
			ILoggingService logger = _logger;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(51, 1);
			defaultInterpolatedStringHandler.AppendLiteral("[Pipeline] Pós-processamento concluído | Sucessos: ");
			defaultInterpolatedStringHandler.AppendFormatted(executionResult.SuccessCount);
			logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
		}
		catch (Exception ex2)
		{
			Exception ex = ex2;
			_logger.LogWarning("[Pipeline] Erro no pós-processamento: " + ex.Message);
		}
	}

	private async Task CleanupOldExecutionsAsync()
	{
		DateTime cutoff;
		await Task.Run(delegate
		{
			lock (_executionLock)
			{
				cutoff = DateTime.UtcNow - TimeSpan.FromHours(1.0);
				List<string> list = (from kvp in _lastExecutions
					where kvp.Value < cutoff
					select kvp.Key).ToList();
				foreach (string item in list)
				{
					_lastExecutions.Remove(item);
				}
			}
		});
	}

	private bool IsOptimizationValidForMode(string optimizationName, OptimizationMode mode)
	{
		string text = optimizationName.ToLowerInvariant();
		if (1 == 0)
		{
		}
		bool result;
		switch (mode)
		{
		case OptimizationMode.Instant:
		{
			bool flag;
			switch (text)
			{
			case "powerplan_highperformance":
			case "flush_dns":
			case "optimize_memory":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			result = flag;
			break;
		}
		case OptimizationMode.Safe:
			result = !text.Contains("risky") && !text.Contains("overclock");
			break;
		case OptimizationMode.Aggressive:
			result = true;
			break;
		case OptimizationMode.Gamer:
			result = text.Contains("powerplan") || text.Contains("memory") || text.Contains("network");
			break;
		default:
			result = true;
			break;
		}
		if (1 == 0)
		{
		}
		return result;
	}

	private OptimizationGroup ClassifyOptimizationGroup(string optimizationName)
	{
		string text = optimizationName.ToLowerInvariant();
		if (text.Contains("risky") || text.Contains("overclock") || text.Contains("disable_protections"))
		{
			return OptimizationGroup.Risky;
		}
		if (text.Contains("hibernation") || text.Contains("superfetch") || text.Contains("service"))
		{
			return OptimizationGroup.Conditional;
		}
		return OptimizationGroup.Safe;
	}

	private long EstimateExecutionTime(string optimizationName, HardwareProfile profile)
	{
		string text = optimizationName.ToLowerInvariant();
		if (1 == 0)
		{
		}
		int num = text switch
		{
			"powerplan_highperformance" => 1000, 
			"flush_dns" => 500, 
			"optimize_memory" => 2000, 
			"enable_trim" => 500, 
			"disable_hibernation" => 1000, 
			"visual_optimize" => 1500, 
			"optimize_services" => 3000, 
			_ => 2000};
		if (1 == 0)
		{
		}
		return num;
	}

	private int GetPriorityWeight(string priority)
	{
		string text = priority.ToLowerInvariant();
		if (1 == 0)
		{
		}
		int result = text switch
		{
			"high" => 3, 
			"medium" => 2, 
			"low" => 1, 
			_ => 0};
		if (1 == 0)
		{
		}
		return result;
	}

	private int GetImpactWeight(string impact)
	{
		string text = impact.ToLowerInvariant();
		if (1 == 0)
		{
		}
		int result = text switch
		{
			"alto" => 3, 
			"médio" => 2, 
			"baixo" => 1, 
			_ => 0};
		if (1 == 0)
		{
		}
		return result;
	}
}
