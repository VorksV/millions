using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.Brain.V2;
using VoltrisOptimizer.Core.Constants;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Utils.Win32;
using VoltrisOptimizer.Services.Power;

namespace VoltrisOptimizer.Core.Optimization;

public sealed class StartupAccelerationPipeline
{
	private readonly ILoggingService _logger;

	private readonly IServiceProvider? _services;

	private readonly VoltriScoreEvaluator _scoreEvaluator;

	private readonly ImmediateImpactEngine _immediateImpact;

	public StartupAccelerationPipeline(ILoggingService logger, IServiceProvider? services = null)
	{
		_logger = logger ?? throw new ArgumentNullException("logger");
		_services = services;
		_scoreEvaluator = new VoltriScoreEvaluator();
		_immediateImpact = new ImmediateImpactEngine(_logger, _services);
	}

	public async Task<StartupAccelerationResult> ExecuteAsync()
	{
		_logger.LogEntry(nameof(ExecuteAsync));
		DateTime startedAt = DateTime.UtcNow;
		Stopwatch execution = Stopwatch.StartNew();
		List<StartupAccelerationStepResult> steps = new List<StartupAccelerationStepResult>();
		_logger.LogTransition("IDLE", "EXECUTING", "ExecuteAsync called");
		PublishInfo("StartupAcceleration", "Pipeline", "StartupAccelerationPipeline iniciado.");
		LogStructured(LogLevel.Info, "startup_pipeline_started", new { startedAt });
		VoltriScoreSnapshot before = CaptureSnapshot();
		LogStructured(LogLevel.Debug, "startup_snapshot_before", before);
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler;
		try
		{
			IReadOnlyList<string> decision = await BuildDecisionLayerAsync(before).ConfigureAwait(continueOnCapturedContext: false);
			_logger.LogDecision("PipelineActions", "Plano definido", string.Join(",", decision));
			PublishInfo("StartupAcceleration", "Decision", "Plano definido: " + string.Join(",", decision));
			LogStructured(LogLevel.Debug, "startup_decision_layer", new
			{
				actions = decision
			});
			steps.AddRange(await _immediateImpact.ExecuteAsync(before).ConfigureAwait(continueOnCapturedContext: false));
			Task<StartupAccelerationStepResult>[] complementaryTasks = decision.Select((string action) => ExecuteDecisionActionAsync(action)).ToArray();
			if (complementaryTasks.Length != 0)
			{
				StartupAccelerationStepResult[] array = await Task.WhenAll(complementaryTasks).ConfigureAwait(continueOnCapturedContext: false);
				foreach (StartupAccelerationStepResult result in array)
				{
					steps.Add(result);
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(3, 3);
					defaultInterpolatedStringHandler.AppendFormatted(result.Name);
					defaultInterpolatedStringHandler.AppendLiteral(": ");
					defaultInterpolatedStringHandler.AppendFormatted(result.Success ? "concludo" : "falhou");
					defaultInterpolatedStringHandler.AppendLiteral(" ");
					defaultInterpolatedStringHandler.AppendFormatted(result.ObtainedResult);
					PublishInfo("StartupAcceleration", "Step", defaultInterpolatedStringHandler.ToStringAndClear());
					LogStructured(result.Success ? LogLevel.Info : LogLevel.Warning, "startup_step_result", result);
				}
			}
		}
		catch (Exception ex2)
		{
			Exception ex = ex2;
			PublishError("StartupAcceleration", "Pipeline", "Falha na execu o da pipeline: " + ex.Message);
			LogStructured(LogLevel.Error, "startup_pipeline_exception", new { ex.Message, ex.StackTrace });
		}
		if (steps.Any((StartupAccelerationStepResult s) => s.Success))
		{
			await Task.Delay(30).ConfigureAwait(continueOnCapturedContext: false);
		}
		VoltriScoreSnapshot after = CaptureSnapshot();
		VoltriScoreReport score = _scoreEvaluator.Evaluate(before, after, steps);
		LogStructured(LogLevel.Debug, "startup_snapshot_after", after);
		LogStructured(LogLevel.Info, "startup_voltriscore", score);
		defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(52, 5);
		defaultInterpolatedStringHandler.AppendLiteral("Impacto ");
		defaultInterpolatedStringHandler.AppendFormatted(score.ImpactLevel.ToString().ToUpperInvariant());
		defaultInterpolatedStringHandler.AppendLiteral("|estimado = ");
		defaultInterpolatedStringHandler.AppendFormatted(score.EstimatedGain, "F1");
		defaultInterpolatedStringHandler.AppendLiteral("%|real = ");
		defaultInterpolatedStringHandler.AppendFormatted(score.RealGain, "F1");
		defaultInterpolatedStringHandler.AppendLiteral("%|input = ");
		defaultInterpolatedStringHandler.AppendFormatted(score.InputLatencyImprovementMs, "F0");
		defaultInterpolatedStringHandler.AppendLiteral(" ms|ram = ");
		defaultInterpolatedStringHandler.AppendFormatted(score.AvailableRamGainMb, "F0");
		defaultInterpolatedStringHandler.AppendLiteral(" MB");
		PublishInfo("VoltriScore", "Impact", defaultInterpolatedStringHandler.ToStringAndClear());
		execution.Stop();
		_logger.LogTransition("EXECUTING", "COMPLETE", "Pipeline finished");
		_logger.LogExit(nameof(ExecuteAsync), steps.Count + " steps", execution.ElapsedMilliseconds);
		defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(27, 1);
		defaultInterpolatedStringHandler.AppendLiteral("Pipeline finalizado em ");
		defaultInterpolatedStringHandler.AppendFormatted(execution.ElapsedMilliseconds);
		defaultInterpolatedStringHandler.AppendLiteral(" ms.");
		PublishSuccess("StartupAcceleration", "Pipeline", defaultInterpolatedStringHandler.ToStringAndClear());
		return new StartupAccelerationResult
		{
			StartedAtUtc = startedAt,
			FinishedAtUtc = DateTime.UtcNow,
			Steps = steps,
			BeforeSnapshot = before,
			AfterSnapshot = after,
			ScoreReport = score
		};
	}

	private async Task<IReadOnlyList<string>> BuildDecisionLayerAsync(VoltriScoreSnapshot before)
	{
		_logger.LogEntry(nameof(BuildDecisionLayerAsync));
		List<string> decisions = new List<string> { "BrainQuickOptimization" };
		HardwareSummary hardware = await SystemMetricsCache.Instance.GetHardwareAsync().ConfigureAwait(continueOnCapturedContext: false);
		bool isLaptop = hardware.IsLaptop;
		bool hasSsd = hardware.HasSsd;
		long lastInput = before.LastInputMs;
		if (before.CpuPercent >= 45.0 || before.DiskQueueLength >= 2f || lastInput >= 800)
		{
			decisions.Add("BackgroundNoiseReduction");
		}
		if (!isLaptop && before.CpuPercent >= 65.0 && hasSsd)
		{
			decisions.Add("TransientPowerBoost");
		}
		_logger?.LogDecision("BackgroundNoiseReduction", $"Cpu={before.CpuPercent:F1}% DiskQ={before.DiskQueueLength:F2} LastInput={lastInput}ms", decisions.Contains("BackgroundNoiseReduction"));
		_logger?.LogDecision("TransientPowerBoost", $"IsLaptop={isLaptop} Cpu={before.CpuPercent:F1}% HasSsd={hasSsd}", decisions.Contains("TransientPowerBoost"));
		_logger.LogExit(nameof(BuildDecisionLayerAsync), string.Join(",", decisions));
		return decisions;
	}

	private Task<StartupAccelerationStepResult> ExecuteDecisionActionAsync(string action)
	{
		if (1 == 0)
		{
		}
		Task<StartupAccelerationStepResult> result = action switch
		{
			"BrainQuickOptimization" => ExecuteStepAsync("BrainQuickOptimization", "Delegar otimização rápida contextual ao Brain", 3, ApplyBrainQuickOptimizationAsync), 
			"BackgroundNoiseReduction" => ExecuteStepAsync("BackgroundNoiseReduction", "Reduzir ru do de processos em segundo plano com seguran a", 2, ApplyBackgroundNoiseReductionAsync), 
			"TransientPowerBoost" => ExecuteStepAsync("TransientPowerBoost", "Aplicar refor o tempor rio de energia e reverter automaticamente", 4, ApplyTransientPowerBoostAsync), 
			_ => Task.FromResult(new StartupAccelerationStepResult
			{
				Name = action,
				Success = false,
				ExpectedResult = "A o conhecida",
				ObtainedResult = "A o no reconhecida",
				EstimatedImpactPoints = 0,
				ExecutionTimeMs = 0,
				Error = "unknown - action"
			})};
		if (1 == 0)
		{
		}
		return result;
	}

	private static VoltriScoreSnapshot CaptureSnapshot()
	{
		SystemMetricsCache instance = SystemMetricsCache.Instance;
		ProcessPriorityClass currentProcessPriority = ProcessPriorityClass.Normal;
		try
		{
			currentProcessPriority = Process.GetCurrentProcess().PriorityClass;
		}
		catch
		{
		}
		return new VoltriScoreSnapshot
		{
			TimestampUtc = DateTime.UtcNow,
			CpuPercent = instance.CpuPercent,
			MemoryUsedPercent = instance.MemoryUsedPercent,
			AvailableRamMb = instance.AvailableRamMb,
			DiskQueueLength = instance.DiskQueueLength,
			LastInputMs = instance.LastInputMs,
			CurrentProcessPriority = currentProcessPriority
		};
	}

	internal static async Task<StartupAccelerationStepResult> ExecuteStepAsync(string name, string expectedResult, int impactPoints, Func<Task<(bool Success, string Message, string? Error)>> executor)
	{
		App.LoggingService?.LogDebug($"[ExecuteStepAsync] ENTRY name={name}");
		Stopwatch sw = Stopwatch.StartNew();
		try
		{
			(bool Success, string Message, string? Error) outcome = await executor().ConfigureAwait(continueOnCapturedContext: false);
			sw.Stop();
			return new StartupAccelerationStepResult
			{
				Name = name,
				Success = outcome.Success,
				ExpectedResult = expectedResult,
				ObtainedResult = outcome.Message,
				EstimatedImpactPoints = impactPoints,
				ExecutionTimeMs = (int)sw.ElapsedMilliseconds,
				Error = outcome.Error
			};
		}
		catch (Exception ex2)
		{
			Exception ex = ex2;
			sw.Stop();
			App.LoggingService?.LogDebug($"[ExecuteStepAsync] EXIT name={name} error={ex.Message}");
			return new StartupAccelerationStepResult
			{
				Name = name,
				Success = false,
				ExpectedResult = expectedResult,
				ObtainedResult = "Falha inesperada",
				EstimatedImpactPoints = impactPoints,
				ExecutionTimeMs = (int)sw.ElapsedMilliseconds,
				Error = ex.Message
			};
		}
	}

	private Task<(bool Success, string Message, string? Error)> ApplyTemporaryProcessPriorityBoostAsync()
	{
		try
		{
			Process process = Process.GetCurrentProcess();
			ProcessPriorityClass previous = process.PriorityClass;
			if (previous >= ProcessPriorityClass.AboveNormal)
			{
				return Task.FromResult<(bool, string, string)>((true, "Prioridade já estava elevada.", null));
			}
			process.PriorityClass = ProcessPriorityClass.AboveNormal;
			Task.Run(async delegate
			{
				await Task.Delay(TimeSpan.FromSeconds(45.0)).ConfigureAwait(continueOnCapturedContext: false);
				try
				{
					process.PriorityClass = previous;
				}
				catch
				{
				}
			});
			return Task.FromResult<(bool, string, string)>((true, "Prioridade elevada de forma temporária (45s).", null));
		}
		catch (Exception ex)
		{
			return Task.FromResult((false, "Não foi possível ajustar prioridade do processo.", ex.Message));
		}
	}

	private async Task<(bool Success, string Message, string? Error)> ApplyBrainQuickOptimizationAsync()
	{
		VoltrisBrainV2 brain = App.BrainV2;
		if (brain == null || !brain.IsRunning)
		{
			try
			{
				Process process = Process.GetCurrentProcess();
				ProcessPriorityClass previous = process.PriorityClass;
				if (previous < ProcessPriorityClass.AboveNormal)
				{
					process.PriorityClass = ProcessPriorityClass.AboveNormal;
					Task.Run(async delegate
					{
						await Task.Delay(TimeSpan.FromSeconds(45)).ConfigureAwait(continueOnCapturedContext: false);
						try { process.PriorityClass = previous; } catch { }
					});
					return (true, "Brain não disponível — fallback: prioridade do processo elevada temporariamente.", null);
				}
				return (true, "Brain não disponível — prioridade já estava elevada.", null);
			}
			catch (Exception ex)
			{
				return (false, "Falha no fallback de otimização rápida.", ex.Message);
			}
		}
		VoltrisBrainV2.BrainRequestResult result = await brain.RequestManualOptimizationAsync(VoltrisBrainV2.OptimizationIntent.Quick).ConfigureAwait(continueOnCapturedContext: false);
		if (result.Success)
		{
			return (true, "Brain executou otimização rápida (" + string.Join(",", result.ExecutedActions) + ").", null);
		}
		return (false, "Falha na otimização rápida do Brain.", result.Reason);
	}

	private async Task<(bool Success, string Message, string? Error)> ApplyBackgroundNoiseReductionAsync()
	{
		VoltrisBrainV2 brain = App.BrainV2;
		if (brain == null || !brain.IsRunning)
		{
			try
			{
				int reduced = 0;
				foreach (var proc in Process.GetProcesses())
				{
					try
					{
						string name = proc.ProcessName;
						if (name.Equals("svchost", StringComparison.OrdinalIgnoreCase) ||
							name.Equals("System", StringComparison.OrdinalIgnoreCase) ||
							name.Equals("Idle", StringComparison.OrdinalIgnoreCase) ||
							name.Equals("csrss", StringComparison.OrdinalIgnoreCase) ||
							name.Equals("wininit", StringComparison.OrdinalIgnoreCase) ||
							name.Equals("services", StringComparison.OrdinalIgnoreCase) ||
							name.Equals("lsass", StringComparison.OrdinalIgnoreCase) ||
							name.Equals("dwm", StringComparison.OrdinalIgnoreCase) ||
							name.IndexOf("voltris", StringComparison.OrdinalIgnoreCase) >= 0)
							continue;
						if (proc.HasExited) continue;
						if (proc.BasePriority > 4)
						{
							proc.PriorityClass = ProcessPriorityClass.BelowNormal;
							reduced++;
						}
					}
					catch { }
				}
				return (true, $"Brain não disponível — fallback: prioridade reduzida em {reduced} processos.", null);
			}
			catch (Exception ex)
			{
				return (false, "Falha no fallback de redução de ruído.", ex.Message);
			}
		}
		VoltrisBrainV2.BrainRequestResult result = await brain.SuspendBackgroundProcessesAsync().ConfigureAwait(continueOnCapturedContext: false);
		if (result.Success)
		{
			return (true, "Redução de ruído em background aplicada pelo Brain.", null);
		}
		if (string.Equals(result.Reason, "no-sensor-snapshot", StringComparison.OrdinalIgnoreCase))
		{
			return (true, "Brain ainda não tem snapshot de sensores, redução de ruído omitida (normal no arranque).", null);
		}
		return (false, "Redução de ruído não aplicada: " + result.Reason + ".", result.Reason);
	}

	private async Task<(bool Success, string Message, string? Error)> ApplyTransientPowerBoostAsync()
	{
		VoltrisBrainV2 brain = App.BrainV2;
		if (brain == null || !brain.IsRunning)
		{
			try
			{
				// [FIX:UNICO-DONO-DE-ENERGIA] Este fallback ligava o High Performance
				// quando o Brain nao estava pronto, e voltava ao Balanceado 75s depois.
				// Esse ciclo era um dos que brigavam com o Perfil: ligava um plano, o
				// Perfil reassertava, e o timer de 75s desligava de novo.
				//
				// "Fallback de performance" nao e uma coisa que este componente possa
				// decidir. Se o Brain nao subiu, o Perfil Inteligente continua
				// responsavel pelo plano - ele e' o dono, e funciona sem Brain.
				ProfilePowerAuthority.RequestProfileApply(
					"StartupAcceleration", "fallback: Brain indisponivel", _logger);
				return (true, "Brain nao disponivel: Perfil Inteligente responsavel pelo plano.", null);
			}
			catch (Exception ex)
			{
				return (false, "Falha no fallback de power boost.", ex.Message);
			}
		}
		VoltrisBrainV2.BrainRequestResult high = await brain.RequestPowerProfileAsync(VoltrisBrainV2.PowerProfileKind.HighPerformance).ConfigureAwait(continueOnCapturedContext: false);
		if (!high.Success)
		{
			return (false, "Power boost transitório rejeitado: " + high.Reason + ".", high.Reason);
		}
		Task.Run(async delegate
		{
			await Task.Delay(TimeSpan.FromSeconds(75.0)).ConfigureAwait(continueOnCapturedContext: false);
			try
			{
				await brain.RequestPowerProfileAsync(VoltrisBrainV2.PowerProfileKind.Balanced).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch
			{
			}
		});
		return (true, "Power boost transitório aplicado (reverso automático em 75s).", null);
	}

	private void LogStructured(LogLevel level, string eventName, object payload)
	{
		string message = JsonSerializer.Serialize(new
		{
			ts = DateTime.UtcNow.ToString("O"),
			source = "StartupAccelerationPipeline",
			evt = eventName,
			payload = payload
		}, new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
		_logger.Log(level, LogCategory.Intelligence, message, null, "StartupAccelerationPipeline");
	}

	internal static void PublishInfo(string source, string category, string message)
	{
		BrainObservabilityHub.Publish(BrainEventSeverity.Info, source, category, message);
	}

	internal static void PublishSuccess(string source, string category, string message)
	{
		BrainObservabilityHub.Publish(BrainEventSeverity.Success, source, category, message);
	}

	internal static void PublishError(string source, string category, string message)
	{
		BrainObservabilityHub.Publish(BrainEventSeverity.Error, source, category, message);
	}
}

