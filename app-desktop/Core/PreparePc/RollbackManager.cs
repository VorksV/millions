using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.PreparePc.Interfaces;
using VoltrisOptimizer.Core.PreparePc.Steps;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Core.PreparePc;

public class RollbackManager : IRollbackManager
{
	private class ServiceSnapshot
	{
		public List<ServiceState>? Services { get; set; }
	}

	private class ServiceState
	{
		public string Name { get; set; } = "";


		public bool WasRunning { get; set; }

		public string StartType { get; set; } = "";

	}

	private readonly ILoggingService _logger;

	private readonly string _backupBasePath;

	private readonly string _actionsFilePath;

	private readonly List<RollbackAction> _actions = new List<RollbackAction>();

	private readonly Dictionary<string, IRepairStep> _stepResolvers;

	public RollbackManager(ILoggingService logger, string backupBasePath)
	{
		_logger = logger ?? throw new ArgumentNullException("logger");
		_backupBasePath = backupBasePath;
		_actionsFilePath = Path.Combine(backupBasePath, "rollback_actions.json");
		_stepResolvers = new Dictionary<string, IRepairStep>
		{
			{
				"Backup",
				new BackupStep(logger, backupBasePath)
			},
			{
				"Power Plan",
				new PowerPlanStep(logger, backupBasePath)
			}
		};
		LoadActions();
	}

	public void RegisterAction(string stepName, string backupPath, DateTime timestamp)
	{
		RollbackAction item = new RollbackAction
		{
			StepName = stepName,
			BackupPath = backupPath,
			Timestamp = timestamp,
			Type = DetermineActionType(stepName),
			IsRolledBack = false
		};
		_actions.Add(item);
		SaveActions();
		_logger.LogInfo("[Rollback] Ação registrada: " + stepName + " -> " + backupPath);
	}

	public RollbackAction[] GetActions()
	{
		return _actions.Where((RollbackAction a) => !a.IsRolledBack).ToArray();
	}

	public async Task<bool> RollbackAllAsync(IProgress<StepProgress>? progress = null, CancellationToken ct = default(CancellationToken))
	{
		List<RollbackAction> actionsToRollback = (from a in _actions
			where !a.IsRolledBack
			orderby a.Timestamp descending
			select a).ToList();
		if (!actionsToRollback.Any())
		{
			_logger.LogInfo("[Rollback] Nenhuma ação para reverter");
			return true;
		}
		ILoggingService logger = _logger;
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(31, 1);
		defaultInterpolatedStringHandler.AppendLiteral("[Rollback] Revertendo ");
		defaultInterpolatedStringHandler.AppendFormatted(actionsToRollback.Count);
		defaultInterpolatedStringHandler.AppendLiteral(" ações...");
		logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
		int successCount = 0;
		int failCount = 0;
		for (int i = 0; i < actionsToRollback.Count; i++)
		{
			RollbackAction action = actionsToRollback[i];
			progress?.Report(new StepProgress
			{
				StepName = "Rollback",
				PercentComplete = (i + 1) * 100 / actionsToRollback.Count,
				CurrentAction = "Revertendo: " + action.StepName
			});
			if (await RollbackActionInternalAsync(action, ct))
			{
				successCount++;
				action.IsRolledBack = true;
			}
			else
			{
				failCount++;
				_logger.LogWarning("[Rollback] Falha ao reverter: " + action.StepName);
			}
		}
		SaveActions();
		ILoggingService logger2 = _logger;
		defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(39, 2);
		defaultInterpolatedStringHandler.AppendLiteral("[Rollback] Concluído: ");
		defaultInterpolatedStringHandler.AppendFormatted(successCount);
		defaultInterpolatedStringHandler.AppendLiteral(" sucesso, ");
		defaultInterpolatedStringHandler.AppendFormatted(failCount);
		defaultInterpolatedStringHandler.AppendLiteral(" falhas");
		logger2.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
		return failCount == 0;
	}

	public async Task<bool> RollbackActionAsync(string stepName, CancellationToken ct = default(CancellationToken))
	{
		string stepName2 = stepName;
		RollbackAction action = _actions.FirstOrDefault((RollbackAction a) => a.StepName == stepName2 && !a.IsRolledBack);
		if (action == null)
		{
			_logger.LogWarning("[Rollback] Ação não encontrada: " + stepName2);
			return false;
		}
		bool success = await RollbackActionInternalAsync(action, ct);
		if (success)
		{
			action.IsRolledBack = true;
			SaveActions();
		}
		return success;
	}

	public async Task CleanOldBackupsAsync(int daysToKeep = 7)
	{
		DateTime cutoffDate;
		await Task.Run(delegate
		{
			try
			{
				if (Directory.Exists(_backupBasePath))
				{
					cutoffDate = DateTime.Now.AddDays(-daysToKeep);
					List<string> list = new List<string>();
					string[] directories = Directory.GetDirectories(_backupBasePath);
					foreach (string text in directories)
					{
						DirectoryInfo directoryInfo = new DirectoryInfo(text);
						if (directoryInfo.CreationTime < cutoffDate)
						{
							list.Add(text);
						}
					}
					foreach (string item in list)
					{
						try
						{
							Directory.Delete(item, recursive: true);
							_logger.LogInfo("[Rollback] Backup antigo removido: " + item);
						}
						catch (Exception ex)
						{
							_logger.LogWarning("[Rollback] Erro ao remover backup: " + ex.Message);
						}
					}
					_actions.RemoveAll((RollbackAction a) => a.Timestamp < cutoffDate);
					SaveActions();
					ILoggingService logger = _logger;
					DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(48, 1);
					defaultInterpolatedStringHandler.AppendLiteral("[Rollback] Limpeza concluída: ");
					defaultInterpolatedStringHandler.AppendFormatted(list.Count);
					defaultInterpolatedStringHandler.AppendLiteral(" backups removidos");
					logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
				}
			}
			catch (Exception ex2)
			{
				_logger.LogError("[Rollback] Erro na limpeza: " + ex2.Message, ex2);
			}
		});
	}

	private async Task<bool> RollbackActionInternalAsync(RollbackAction action, CancellationToken ct)
	{
		try
		{
			_logger.LogInfo("[Rollback] Revertendo: " + action.StepName);
			if (!string.IsNullOrEmpty(action.BackupPath) && !File.Exists(action.BackupPath) && !Directory.Exists(action.BackupPath))
			{
				_logger.LogWarning("[Rollback] Backup não encontrado: " + action.BackupPath);
				return false;
			}
			if (_stepResolvers.TryGetValue(action.StepName, out var step))
			{
				return (await step.RollbackAsync(action.BackupPath, ct)).Success;
			}
			string type = action.Type;
			if (1 == 0)
			{
			}
			bool result = type switch
			{
				"registry" => await RollbackRegistryAsync(action.BackupPath, ct), 
				"powerplan" => await RollbackPowerPlanAsync(action.BackupPath, ct), 
				"service" => await RollbackServiceAsync(action.BackupPath, ct), 
				_ => false};
			if (1 == 0)
			{
			}
			return result;
		}
		catch (Exception ex2)
		{
			Exception ex = ex2;
			_logger.LogError("[Rollback] Erro: " + ex.Message, ex);
			return false;
		}
	}

	private async Task<bool> RollbackRegistryAsync(string backupPath, CancellationToken ct)
	{
		try
		{
			ProcessStartInfo psi = new ProcessStartInfo
			{
				FileName = "reg.exe",
				Arguments = "import \"" + backupPath + "\"",
				UseShellExecute = false,
				CreateNoWindow = true
			};
			using Process proc = Process.Start(psi);
			if (proc != null)
			{
				await proc.WaitForExitAsync(ct);
				return proc.ExitCode == 0;
			}
			return false;
		}
		catch
		{
			return false;
		}
	}

	private async Task<bool> RollbackPowerPlanAsync(string backupPath, CancellationToken ct)
	{
		try
		{
			ProcessStartInfo psi = new ProcessStartInfo
			{
				FileName = "powercfg",
				Arguments = "/import \"" + backupPath + "\"",
				UseShellExecute = false,
				CreateNoWindow = true
			};
			using Process proc = Process.Start(psi);
			if (proc != null)
			{
				await proc.WaitForExitAsync(ct);
				return proc.ExitCode == 0;
			}
			return false;
		}
		catch
		{
			return false;
		}
	}

	private async Task<bool> RollbackServiceAsync(string backupPath, CancellationToken ct)
	{
		try
		{
			if (!File.Exists(backupPath))
			{
				return false;
			}
			ServiceSnapshot snapshot = JsonSerializer.Deserialize<ServiceSnapshot>(await File.ReadAllTextAsync(backupPath, ct), new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
			if (snapshot?.Services == null)
			{
				return false;
			}
			foreach (ServiceState svc in snapshot.Services!)
			{
				try
				{
					if (svc.WasRunning)
					{
						ProcessStartInfo psi = new ProcessStartInfo
						{
							FileName = "sc",
							Arguments = "start " + svc.Name,
							UseShellExecute = false,
							CreateNoWindow = true
						};
						using Process proc = Process.Start(psi);
						proc?.WaitForExit(10000);
					}
				}
				catch
				{
				}
			}
			return true;
		}
		catch
		{
			return false;
		}
	}

	private string DetermineActionType(string stepName)
	{
		string text = stepName.ToLowerInvariant();
		if (1 == 0)
		{
		}
		if (text == null)
		{
			goto IL_0077;
		}
		string result;
		if (text.Contains("backup"))
		{
			result = "backup";
		}
		else
		{
			string text2 = text;
			if (text2.Contains("registry"))
			{
				result = "registry";
			}
			else
			{
				string text3 = text;
				if (text3.Contains("power"))
				{
					result = "powerplan";
				}
				else
				{
					string text4 = text;
					if (!text4.Contains("service"))
					{
						goto IL_0077;
					}
					result = "service";
				}
			}
		}
		goto IL_0080;
		IL_0077:
		result = "generic";
		goto IL_0080;
		IL_0080:
		if (1 == 0)
		{
		}
		return result;
	}

	private void LoadActions()
	{
		try
		{
			if (File.Exists(_actionsFilePath))
			{
				string json = File.ReadAllText(_actionsFilePath);
				List<RollbackAction> list = JsonSerializer.Deserialize<List<RollbackAction>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
				if (list != null)
				{
					_actions.Clear();
					_actions.AddRange(list);
				}
			}
		}
		catch (Exception ex)
		{
			_logger.LogWarning("[Rollback] Erro ao carregar ações: " + ex.Message);
		}
	}

	private void SaveActions()
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(_actionsFilePath));
			string contents = JsonSerializer.Serialize(_actions, new JsonSerializerOptions { WriteIndented = true,
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase
			});
			File.WriteAllText(_actionsFilePath, contents);
		}
		catch (Exception ex)
		{
			_logger.LogWarning("[Rollback] Erro ao salvar ações: " + ex.Message);
		}
	}
}
