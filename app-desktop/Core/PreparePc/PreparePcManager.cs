using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using VoltrisOptimizer.Core.PreparePc.Interfaces;
using VoltrisOptimizer.Core.PreparePc.Steps;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Utils;

namespace VoltrisOptimizer.Core.PreparePc;

public class PreparePcManager
{
	private readonly ILoggingService _logger;

	private readonly IRollbackManager _rollbackManager;

	private readonly List<IRepairStep> _steps;

	private readonly string _backupBasePath;

	private CancellationTokenSource? _cts;

	private bool _isRunning;

	public bool IsRunning => _isRunning;

	public IRollbackManager RollbackManager => _rollbackManager;

	public event EventHandler<PreparePcProgress>? ProgressChanged;

	public event EventHandler<string>? LogAdded;

	public PreparePcManager(ILoggingService logger)
	{
		_logger = logger ?? throw new ArgumentNullException("logger");
		_backupBasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Voltris", "Backups");
		_rollbackManager = new RollbackManager(logger, _backupBasePath);
		_steps = InitializeSteps();
	}

	private List<IRepairStep> InitializeSteps()
	{
		return new List<IRepairStep>
		{
			new PreCheckStep(_logger),
			new BackupStep(_logger, _backupBasePath),
			new SfcStep(_logger),
			new DismStep(_logger),
			new CacheCleanerStep(_logger),
			new NetworkResetStep(_logger),
			new ShaderCacheStep(_logger),
			new ServiceOptimizerStep(_logger),
			new PowerPlanStep(_logger, _backupBasePath)
		}.OrderBy((IRepairStep s) => s.Order).ToList();
	}

	public async Task<PreCheckResult> RunPreChecksAsync(CancellationToken ct = default(CancellationToken))
	{
		PreCheckResult result = new PreCheckResult
		{
			IsAdmin = AdminHelper.IsRunningAsAdministrator()
		};
		if (!result.IsAdmin)
		{
			result.Errors.Add("O Voltris precisa ser executado como Administrador para preparar o PC.");
		}
		(result.IsOnBattery, result.BatteryPercent) = await CheckBatteryStatusAsync();
		if (result.IsOnBattery && result.BatteryPercent < 40)
		{
			List<string> warnings = result.Warnings;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(58, 1);
			defaultInterpolatedStringHandler.AppendLiteral("Bateria baixa (");
			defaultInterpolatedStringHandler.AppendFormatted(result.BatteryPercent);
			defaultInterpolatedStringHandler.AppendLiteral("%). Algumas operações podem ser bloqueadas.");
			warnings.Add(defaultInterpolatedStringHandler.ToStringAndClear());
		}
		PreCheckResult preCheckResult = result;
		preCheckResult.FreeSpaceGB = await CheckFreeSpaceAsync();
		DriveInfo driveInfo = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:");
		double freePercent = result.FreeSpaceGB * 100.0 / ((double)driveInfo.TotalSize / 1073741824.0);
		if (freePercent < 10.0)
		{
			List<string> errors = result.Errors;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(69, 1);
			defaultInterpolatedStringHandler.AppendLiteral("Espaço em disco insuficiente (");
			defaultInterpolatedStringHandler.AppendFormatted(result.FreeSpaceGB, "F1");
			defaultInterpolatedStringHandler.AppendLiteral(" GB). Mínimo recomendado: 10% do disco.");
			errors.Add(defaultInterpolatedStringHandler.ToStringAndClear());
		}
		PreCheckResult preCheckResult2 = result;
		preCheckResult2.InstallerProcesses = await CheckInstallerProcessesAsync();
		if (result.InstallerProcesses.Any())
		{
			result.Warnings.Add("Processos de instalação detectados: " + string.Join(", ", result.InstallerProcesses) + ". Recomendado fechar antes de continuar.");
		}
		result.CanProceed = !result.Errors.Any();
		return result;
	}

	public async Task<PreparePcResult> ExecuteAsync(PreparePcOptions options, IProgress<PreparePcProgress>? progress = null, CancellationToken ct = default(CancellationToken))
	{
		PreparePcOptions options2 = options;
		IProgress<PreparePcProgress> progress2 = progress;
		if (_isRunning)
		{
			throw new InvalidOperationException("Prepare PC já está em execução.");
		}
		_isRunning = true;
		_cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		PreparePcResult result = new PreparePcResult
		{
			Mode = options2.Mode,
			StartTime = DateTime.Now
		};
		string backupFolder = options2.CustomBackupPath ?? Path.Combine(_backupBasePath, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
		Directory.CreateDirectory(backupFolder);
		result.BackupFolderPath = backupFolder;
		PreparePcManager preparePcManager = this;
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(30, 1);
		defaultInterpolatedStringHandler.AppendLiteral("[PreparePc] Iniciando em modo ");
		defaultInterpolatedStringHandler.AppendFormatted(options2.Mode);
		preparePcManager.Log(defaultInterpolatedStringHandler.ToStringAndClear());
		Log("[PreparePc] Pasta de backup: " + backupFolder);
		GlobalProgressService instance = GlobalProgressService.Instance;
		defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(19, 1);
		defaultInterpolatedStringHandler.AppendLiteral("Preparando PC (");
		defaultInterpolatedStringHandler.AppendFormatted(options2.Mode);
		defaultInterpolatedStringHandler.AppendLiteral(")...");
		instance.StartOperation(defaultInterpolatedStringHandler.ToStringAndClear());
		try
		{
			List<IRepairStep> stepsToRun = (from s in _steps
				where !options2.SkipSteps.Contains(s.Name)
				where ShouldRunStep(s, options2)
				select s).ToList();
			int totalSteps = stepsToRun.Count;
			DateTime startTime = DateTime.Now;
			for (int i = 0; i < stepsToRun.Count; i++)
			{
				if (_cts!.Token.IsCancellationRequested)
				{
					Log("[PreparePc] Execução cancelada pelo usuário");
					break;
				}
				IRepairStep step = stepsToRun[i];
				TimeSpan elapsed = DateTime.Now - startTime;
				double avgTimePerStep = ((i > 0) ? (elapsed.TotalSeconds / (double)i) : ((double)step.EstimatedTimeSeconds));
				TimeSpan remaining = TimeSpan.FromSeconds(avgTimePerStep * (double)(totalSteps - i));
				int basePercent = Math.Clamp(i * 100 / totalSteps, 0, 99);
				PreparePcProgress currentProgress = new PreparePcProgress
				{
					CurrentStepIndex = i + 1,
					TotalSteps = totalSteps,
					CurrentStepName = step.Name,
					OverallPercent = basePercent,
					StepPercent = 0,
					CurrentAction = "Iniciando " + step.Name + "...",
					CurrentStatus = StepStatus.Running,
					ElapsedTime = elapsed,
					EstimatedRemaining = remaining
				};
				progress2?.Report(currentProgress);
				this.ProgressChanged?.Invoke(this, currentProgress);
				GlobalProgressService instance2 = GlobalProgressService.Instance;
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(22, 3);
				defaultInterpolatedStringHandler.AppendLiteral("Preparando PC: ");
				defaultInterpolatedStringHandler.AppendFormatted(step.Name);
				defaultInterpolatedStringHandler.AppendLiteral(" (");
				defaultInterpolatedStringHandler.AppendFormatted(i + 1);
				defaultInterpolatedStringHandler.AppendLiteral("/");
				defaultInterpolatedStringHandler.AppendFormatted(totalSteps);
				defaultInterpolatedStringHandler.AppendLiteral(")...");
				instance2.UpdateProgress(basePercent, defaultInterpolatedStringHandler.ToStringAndClear());
				PreparePcManager preparePcManager2 = this;
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(31, 3);
				defaultInterpolatedStringHandler.AppendLiteral("[PreparePc] Executando step ");
				defaultInterpolatedStringHandler.AppendFormatted(i + 1);
				defaultInterpolatedStringHandler.AppendLiteral("/");
				defaultInterpolatedStringHandler.AppendFormatted(totalSteps);
				defaultInterpolatedStringHandler.AppendLiteral(": ");
				defaultInterpolatedStringHandler.AppendFormatted(step.Name);
				preparePcManager2.Log(defaultInterpolatedStringHandler.ToStringAndClear());
				var (canRun, reason) = await step.CanExecuteAsync(_cts!.Token);
				if (!canRun)
				{
					Log("[PreparePc] Step " + step.Name + " ignorado: " + reason);
					result.StepResults.Add(StepResult.Skipped(reason));
					continue;
				}
				if (!ShouldExecuteBasedOnRisk(step, options2.Mode))
				{
					PreparePcManager preparePcManager3 = this;
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(57, 2);
					defaultInterpolatedStringHandler.AppendLiteral("[PreparePc] Step ");
					defaultInterpolatedStringHandler.AppendFormatted(step.Name);
					defaultInterpolatedStringHandler.AppendLiteral(" ignorado por política de risco (modo: ");
					defaultInterpolatedStringHandler.AppendFormatted(options2.Mode);
					defaultInterpolatedStringHandler.AppendLiteral(")");
					preparePcManager3.Log(defaultInterpolatedStringHandler.ToStringAndClear());
					List<StepResult> stepResults = result.StepResults;
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(17, 1);
					defaultInterpolatedStringHandler.AppendLiteral("Ignorado no modo ");
					defaultInterpolatedStringHandler.AppendFormatted(options2.Mode);
					stepResults.Add(StepResult.Skipped(defaultInterpolatedStringHandler.ToStringAndClear()));
					continue;
				}
				int stepIndex = i;
				Progress<StepProgress> stepProgress = new Progress<StepProgress>(delegate(StepProgress sp)
				{
					int num = Math.Clamp(sp.PercentComplete, 0, 100);
					currentProgress.StepPercent = num;
					double num2 = 100.0 / (double)totalSteps;
					currentProgress.OverallPercent = Math.Clamp((int)((double)stepIndex * num2 + (double)num * num2 / 100.0), 0, 99);
					currentProgress.CurrentAction = sp.CurrentAction;
					progress2?.Report(currentProgress);
					this.ProgressChanged?.Invoke(this, currentProgress);
					GlobalProgressService.Instance.UpdateProgress(currentProgress.OverallPercent, step.Name + ": " + sp.CurrentAction);
					string[] recentLogs = sp.RecentLogs;
					foreach (string text in recentLogs)
					{
						Log(text);
						currentProgress.Logs.Add(text);
					}
				});
				DateTime stepStartTime = DateTime.Now;
				StepResult stepResult = await step.ExecuteAsync(options2.Mode, stepProgress, _cts!.Token);
				stepResult.Duration = DateTime.Now - stepStartTime;
				result.StepResults.Add(stepResult);
				if (stepResult.Success && !string.IsNullOrEmpty(stepResult.BackupPath))
				{
					_rollbackManager.RegisterAction(step.Name, stepResult.BackupPath, DateTime.Now);
				}
				PreparePcManager preparePcManager4 = this;
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(32, 3);
				defaultInterpolatedStringHandler.AppendLiteral("[PreparePc] Step ");
				defaultInterpolatedStringHandler.AppendFormatted(step.Name);
				defaultInterpolatedStringHandler.AppendLiteral(" concluído: ");
				defaultInterpolatedStringHandler.AppendFormatted(stepResult.Status);
				defaultInterpolatedStringHandler.AppendLiteral(" - ");
				defaultInterpolatedStringHandler.AppendFormatted(stepResult.Message);
				preparePcManager4.Log(defaultInterpolatedStringHandler.ToStringAndClear());
				if (!stepResult.Success && options2.Mode != 0)
				{
					double failedPercent = (double)result.FailedSteps * 100.0 / (double)(i + 1);
					if (failedPercent > 30.0)
					{
						Log("[PreparePc] Taxa de falha alta detectada. Recomendado rollback.");
					}
				}
			}
			result.Success = result.FailedSteps == 0;
			result.EndTime = DateTime.Now;
			PreparePcResult preparePcResult = result;
			preparePcResult.ReportPath = await GenerateReportAsync(result, backupFolder);
			GlobalProgressService instance3 = GlobalProgressService.Instance;
			string finalMessage;
			if (!result.Success)
			{
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(31, 1);
				defaultInterpolatedStringHandler.AppendLiteral("Preparação concluída com ");
				defaultInterpolatedStringHandler.AppendFormatted(result.FailedSteps);
				defaultInterpolatedStringHandler.AppendLiteral(" erros");
				finalMessage = defaultInterpolatedStringHandler.ToStringAndClear();
			}
			else
			{
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(38, 1);
				defaultInterpolatedStringHandler.AppendLiteral("Preparação concluída! ");
				defaultInterpolatedStringHandler.AppendFormatted(result.SuccessfulSteps);
				defaultInterpolatedStringHandler.AppendLiteral(" ações aplicadas");
				finalMessage = defaultInterpolatedStringHandler.ToStringAndClear();
			}
			instance3.CompleteOperation(finalMessage);
			PreparePcManager preparePcManager5 = this;
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(36, 1);
			defaultInterpolatedStringHandler.AppendLiteral("[PreparePc] Preparação concluída em ");
			defaultInterpolatedStringHandler.AppendFormatted(result.TotalDuration, "mm\\:ss");
			preparePcManager5.Log(defaultInterpolatedStringHandler.ToStringAndClear());
			PreparePcManager preparePcManager6 = this;
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(44, 3);
			defaultInterpolatedStringHandler.AppendLiteral("[PreparePc] Sucesso: ");
			defaultInterpolatedStringHandler.AppendFormatted(result.SuccessfulSteps);
			defaultInterpolatedStringHandler.AppendLiteral(", Falhas: ");
			defaultInterpolatedStringHandler.AppendFormatted(result.FailedSteps);
			defaultInterpolatedStringHandler.AppendLiteral(", Ignorados: ");
			defaultInterpolatedStringHandler.AppendFormatted(result.SkippedSteps);
			preparePcManager6.Log(defaultInterpolatedStringHandler.ToStringAndClear());
			if (result.RequiresReboot)
			{
				Log("[PreparePc] ⚠\ufe0f Reinicialização necessária para aplicar algumas alterações.");
			}
			return result;
		}
		catch (OperationCanceledException)
		{
			result.Success = false;
			result.EndTime = DateTime.Now;
			Log("[PreparePc] Operação cancelada");
			GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("PreparePcCanceled"));
			return result;
		}
		catch (Exception ex3)
		{
			Exception ex = ex3;
			result.Success = false;
			result.EndTime = DateTime.Now;
			Log("[PreparePc] Erro crítico: " + ex.Message);
			_logger.LogError("[PreparePc] Erro crítico", ex);
			GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("PreparePcError"));
			return result;
		}
		finally
		{
			_isRunning = false;
			_cts?.Dispose();
			_cts = null;
		}
	}

	public void Cancel()
	{
		if (_isRunning && _cts != null)
		{
			Log("[PreparePc] Cancelamento solicitado...");
			_cts!.Cancel();
		}
	}

	public async Task<bool> RollbackAsync(IProgress<PreparePcProgress>? progress = null, CancellationToken ct = default(CancellationToken))
	{
		Log("[PreparePc] Iniciando rollback...");
		Progress<StepProgress> stepProgress = new Progress<StepProgress>(delegate(StepProgress sp)
		{
			Log(sp.CurrentAction);
		});
		bool success = await _rollbackManager.RollbackAllAsync(stepProgress, ct);
		Log(success ? "[PreparePc] Rollback concluído com sucesso" : "[PreparePc] Rollback concluído com erros");
		return success;
	}

	public int GetEstimatedTimeMinutes(PreparePcOptions options)
	{
		PreparePcOptions options2 = options;
		IEnumerable<IRepairStep> source = from s in _steps
			where !options2.SkipSteps.Contains(s.Name)
			where ShouldRunStep(s, options2)
			select s;
		int num = source.Sum((IRepairStep s) => s.EstimatedTimeSeconds);
		return (int)Math.Ceiling((double)num / 60.0);
	}

	public IEnumerable<(string Name, string Description, RiskCategory Risk, int EstimatedSeconds)> GetStepsToRun(PreparePcOptions options)
	{
		PreparePcOptions options2 = options;
		return from s in _steps
			where !options2.SkipSteps.Contains(s.Name)
			where ShouldRunStep(s, options2)
			select (s.Name, s.Description, s.Risk, s.EstimatedTimeSeconds);
	}

	private bool ShouldRunStep(IRepairStep step, PreparePcOptions options)
	{
		if (step is NetworkResetStep && !options.AllowNetworkReset)
		{
			return false;
		}
		if (step is ShaderCacheStep && !options.CleanShaderCaches)
		{
			return false;
		}
		if (step is ServiceOptimizerStep && !options.OptimizeServices)
		{
			return false;
		}
		if (step is PowerPlanStep && !options.ApplyPowerPlan)
		{
			return false;
		}
		return true;
	}

	private bool ShouldExecuteBasedOnRisk(IRepairStep step, PreparePcMode mode)
	{
		if (1 == 0)
		{
		}
		bool result = mode switch
		{
			PreparePcMode.DryRun => true, 
			PreparePcMode.Recommended => step.Risk == RiskCategory.Safe, 
			PreparePcMode.Full => true, 
			_ => false};
		if (1 == 0)
		{
		}
		return result;
	}

	private async Task<(bool isOnBattery, int percent)> CheckBatteryStatusAsync()
	{
		return await Task.Run(delegate
		{
			try
			{
				PowerStatus powerStatus = SystemInformation.PowerStatus;
				bool item = powerStatus.PowerLineStatus == PowerLineStatus.Offline;
				int item2 = (int)(powerStatus.BatteryLifePercent * 100f);
				return (item, item2);
			}
			catch
			{
				return (false, 100);
			}
		});
	}

	private async Task<double> CheckFreeSpaceAsync()
	{
		return await Task.Run(delegate
		{
			try
			{
				string driveName = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
				DriveInfo driveInfo = new DriveInfo(driveName);
				return (double)driveInfo.AvailableFreeSpace / 1073741824.0;
			}
			catch
			{
				return 0.0;
			}
		});
	}

	private async Task<List<string>> CheckInstallerProcessesAsync()
	{
		string name;
		return await Task.Run(delegate
		{
			string[] source = new string[4] { "msiexec", "setup", "install", "update" };
			List<string> list = new List<string>();
			try
			{
				Process[] processes = Process.GetProcesses();
				foreach (Process process in processes)
				{
					try
					{
						name = process.ProcessName.ToLowerInvariant();
						if (source.Any((string i) => name.Contains(i)))
						{
							list.Add(process.ProcessName);
						}
					}
					catch
					{
					}
					finally
					{
						process.Dispose();
					}
				}
			}
			catch
			{
			}
			return list.Distinct().ToList();
		});
	}

	private async Task<string> GenerateReportAsync(PreparePcResult result, string backupFolder)
	{
		var report = new
		{
			GeneratedAt = DateTime.Now,
			Mode = result.Mode,
			StartTime = result.StartTime,
			EndTime = result.EndTime,
			Duration = result.TotalDuration.ToString(),
			Success = result.Success,
			SuccessfulSteps = result.SuccessfulSteps,
			FailedSteps = result.FailedSteps,
			SkippedSteps = result.SkippedSteps,
			RequiresReboot = result.RequiresReboot,
			BackupPath = result.BackupFolderPath,
			Steps = result.StepResults.Select((StepResult s) => new
			{
				Status = s.Status,
				Message = s.Message,
				BackupPath = s.BackupPath,
				Duration = s.Duration.ToString(),
				RequiresReboot = s.RequiresReboot,
				CanRollback = s.CanRollback,
				LogCount = s.Logs.Length
			}),
			AllLogs = result.AllLogs
		};
		string reportPath = Path.Combine(backupFolder, "prepare_pc_report.json");
		string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true,
			ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase
		});
		await File.WriteAllTextAsync(reportPath, json);
		return reportPath;
	}

	private void Log(string message)
	{
		_logger.LogInfo(message);
		this.LogAdded?.Invoke(this, message);
	}
}
