using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Optimization.Unification;
using VoltrisOptimizer.Services.Responsiveness;

namespace VoltrisOptimizer.Core.Optimization;

internal sealed class ImmediateImpactEngine
{
	private readonly ILoggingService _logger;

	private readonly IServiceProvider? _services;

	private static readonly ActionHistory _history = new ActionHistory();

	public ImmediateImpactEngine(ILoggingService logger, IServiceProvider? services)
	{
		_logger = logger;
		_services = services;
	}

	public async Task<List<StartupAccelerationStepResult>> ExecuteAsync(VoltriScoreSnapshot before)
	{
		List<StartupAccelerationStepResult> results = new List<StartupAccelerationStepResult>();
		Stopwatch budget = Stopwatch.StartNew();
		StartupAccelerationPipeline.PublishInfo("StartupAcceleration", "Pipeline", "ImmediateImpactEngine iniciado (impacto imediato).");
		List<Task<StartupAccelerationStepResult>> tasks = new List<Task<StartupAccelerationStepResult>>();
		if (!_history.ShouldSkip("TimerResolutionPulse", TimeSpan.FromSeconds(25.0)))
		{
			tasks.Add(ExecuteTimerResolutionPulseAsync());
		}
		else
		{
			results.Add(Skipped("TimerResolutionPulse", "Timer resolution já ajustado recentemente (anti-redundância).", 4));
		}
		if (!_history.ShouldSkip("ForegroundBoost", TimeSpan.FromSeconds(12.0)) && before.LastInputMs <= 900)
		{
			tasks.Add(ExecuteForegroundBoostAsync());
		}
		else
		{
			results.Add(Skipped("ForegroundBoost", "Foreground boost não necessário ou aplicado recentemente.", 3));
		}
		if (!_history.ShouldSkip("OwnWorkingSetTrim", TimeSpan.FromSeconds(20.0)) && before.MemoryUsedPercent >= 55.0)
		{
			tasks.Add(ExecuteOwnWorkingSetTrimAsync());
		}
		else
		{
			results.Add(Skipped("OwnWorkingSetTrim", "Working set trim não necessário ou aplicado recentemente.", 3));
		}
		if (!_history.ShouldSkip("StartupNoiseReduction", TimeSpan.FromSeconds(30.0)) && (before.CpuPercent >= 45.0 || before.DiskQueueLength >= 2f || before.LastInputMs >= 800))
		{
			tasks.Add(ExecuteStartupNoiseReductionAsync());
		}
		else
		{
			results.Add(Skipped("StartupNoiseReduction", "Noise reduction não necessária no contexto atual.", 2));
		}
		StartupAccelerationStepResult[] completed;
		try
		{
			completed = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromMilliseconds(220.0)).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (TimeoutException)
		{
			StartupAccelerationPipeline.PublishInfo("StartupAcceleration", "Step", "ImmediateImpactEngine: budget atingido, concluindo com o que já foi aplicado.");
			completed = Array.Empty<StartupAccelerationStepResult>();
		}
		results.AddRange(completed);
		budget.Stop();
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(39, 1);
		defaultInterpolatedStringHandler.AppendLiteral("ImmediateImpactEngine concluído em ");
		defaultInterpolatedStringHandler.AppendFormatted(budget.ElapsedMilliseconds);
		defaultInterpolatedStringHandler.AppendLiteral(" ms.");
		StartupAccelerationPipeline.PublishInfo("StartupAcceleration", "Pipeline", defaultInterpolatedStringHandler.ToStringAndClear());
		return results;
	}

	private Task<StartupAccelerationStepResult> ExecuteTimerResolutionPulseAsync()
	{
		TimerResolutionManager mgr;
		SafeTimerResolutionService safe;
		return StartupAccelerationPipeline.ExecuteStepAsync("TimerResolutionPulse", "Ativar timer resolution de alta precisão (pulso) com rollback seguro", 4, async delegate
		{
			try
			{
				mgr = _services?.GetService(typeof(TimerResolutionManager)) as TimerResolutionManager;
				if (mgr == null)
				{
					safe = _services?.GetService(typeof(SafeTimerResolutionService)) as SafeTimerResolutionService;
					if (safe == null)
					{
						return (false, "Serviço de Timer Resolution indisponível.", "timer-service-missing");
					}
					if (!safe.SetMaximumResolution())
					{
						return (false, "Timer resolution não pôde ser ativado.", "timer-activate-failed");
					}
					_history.Mark("TimerResolutionPulse");
					Task.Run(async delegate
					{
						await Task.Delay(TimeSpan.FromSeconds(18.0)).ConfigureAwait(continueOnCapturedContext: false);
						try
						{
							safe.ReleaseResolution();
						}
						catch
						{
						}
					});
					double cur = safe.GetResolutionInfo().current;
					DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(32, 1);
					defaultInterpolatedStringHandler.AppendLiteral("Timer resolution aplicado (~");
					defaultInterpolatedStringHandler.AppendFormatted(cur, "F2");
					defaultInterpolatedStringHandler.AppendLiteral(" ms)");
					return (true, defaultInterpolatedStringHandler.ToStringAndClear(), null);
				}
				mgr.RequestHighPrecision("StartupImmediateImpact");
				_history.Mark("TimerResolutionPulse");
				Task.Run(async delegate
				{
					await Task.Delay(TimeSpan.FromSeconds(18.0)).ConfigureAwait(continueOnCapturedContext: false);
					try
					{
						mgr.ReleaseHighPrecision("StartupImmediateImpact");
					}
					catch
					{
					}
				});
				return (true, "Timer resolution de alta precisão ativado por 18s (com referência e rollback).", null);
			}
			catch (Exception ex2)
			{
				Exception ex = ex2;
				return (false, "Falha ao aplicar timer resolution.", ex.Message);
			}
		});
	}

	private Task<StartupAccelerationStepResult> ExecuteStartupNoiseReductionAsync()
	{
		return StartupAccelerationPipeline.ExecuteStepAsync("StartupNoiseReduction", "Reduzir I/O priority de processos idle (sem matar processos) e restaurar automaticamente", 2, async delegate
		{
			try
			{
				if (!(_services?.GetService(typeof(IImmediateResponsivenessEngine)) is IImmediateResponsivenessEngine ire))
				{
					return (false, "Motor de responsividade (IRE) indisponível.", "ire-missing");
				}
				Stopwatch sw = Stopwatch.StartNew();
				await ire.RunStartupModeAsync().ConfigureAwait(continueOnCapturedContext: false);
				sw.Stop();
				_history.Mark("StartupNoiseReduction");
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(51, 1);
				defaultInterpolatedStringHandler.AppendLiteral("Noise reduction aplicado (IRE Startup Mode) em ");
				defaultInterpolatedStringHandler.AppendFormatted(sw.ElapsedMilliseconds);
				defaultInterpolatedStringHandler.AppendLiteral(" ms.");
				return (true, defaultInterpolatedStringHandler.ToStringAndClear(), null);
			}
			catch (Exception ex2)
			{
				Exception ex = ex2;
				return (false, "Falha ao aplicar noise reduction.", ex.Message);
			}
		});
	}

	private Task<StartupAccelerationStepResult> ExecuteForegroundBoostAsync()
	{
		Process p;
		ProcessPriorityClass original;
		ProcessPriorityClass target;
		return StartupAccelerationPipeline.ExecuteStepAsync("ForegroundBoost", "Elevar prioridade do app em foco por curta janela e reverter", 3, async delegate
		{
			try
			{
				int pid = ForegroundWindowTracker.Instance.CurrentPid;
				if (pid <= 4)
				{
					return (false, "Foreground não detectado ou PID inválido.", "invalid-pid");
				}
				p = Process.GetProcessById(pid);
				try
				{
					original = p.PriorityClass;
					target = ProcessPriorityClass.AboveNormal;
					if (original >= target)
					{
						return (true, "Processo em foco já está com prioridade elevada.", null);
					}
					try
					{
						p.PriorityClass = target;
					}
					catch
					{
						return (false, "Não foi possível elevar prioridade do processo em foco.", "priority-set-failed");
					}
					_history.Mark("ForegroundBoost");
					Task.Run(async delegate
					{
						await Task.Delay(TimeSpan.FromSeconds(2.0)).ConfigureAwait(continueOnCapturedContext: false);
						try
						{
							if (!p.HasExited && p.PriorityClass == target)
							{
								p.PriorityClass = original;
							}
						}
						catch
						{
						}
					});
					return (true, "Foreground boost aplicado para " + p.ProcessName + " por 2s (rollback automático).", null);
				}
				finally
				{
					if (p != null)
					{
						((IDisposable)p).Dispose();
					}
				}
			}
			catch (Exception ex2)
			{
				Exception ex = ex2;
				return (false, "Falha ao aplicar foreground boost.", ex.Message);
			}
		});
	}

	private Task<StartupAccelerationStepResult> ExecuteOwnWorkingSetTrimAsync()
	{
		return StartupAccelerationPipeline.ExecuteStepAsync("OwnWorkingSetTrim", "Reduzir working set do próprio processo (seguro e instantâneo)", 3, async delegate
		{
			try
			{
				using Process p = Process.GetCurrentProcess();
				long beforeWs = p.WorkingSet64;
				bool ok = false;
				try { p.MinWorkingSet = p.MinWorkingSet; ok = true; } catch {}
				_history.Mark("OwnWorkingSetTrim");
				p.Refresh();
				long afterWs = p.WorkingSet64;
				double freedMb = Math.Max(0.0, (double)(beforeWs - afterWs) / 1048576.0);
				(bool, string, string) result;
				if (!ok)
				{
					result = (false, "TrimWorkingSet não pôde ser aplicado.", "trim-failed");
				}
				else
				{
					DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(37, 1);
					defaultInterpolatedStringHandler.AppendLiteral("Working set do Voltris reduzido (");
					defaultInterpolatedStringHandler.AppendFormatted(freedMb, "F0");
					defaultInterpolatedStringHandler.AppendLiteral(" MB)");
					result = (true, defaultInterpolatedStringHandler.ToStringAndClear(), null);
				}
				return result;
			}
			catch (Exception ex2)
			{
				Exception ex = ex2;
				return (false, "Falha ao aplicar working set trim.", ex.Message);
			}
		});
	}

	private static StartupAccelerationStepResult Skipped(string name, string reason, int impactPoints)
	{
		return new StartupAccelerationStepResult
		{
			Name = name,
			Success = true,
			ExpectedResult = "Aplicar ação se necessário",
			ObtainedResult = "Ignorado com segurança: " + reason,
			EstimatedImpactPoints = 0,
			ExecutionTimeMs = 0,
			Error = null
		};
	}
}
