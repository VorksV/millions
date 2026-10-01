using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using VoltrisOptimizer.Core.PreparePc.UI.Views;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.UI.Windows;

namespace VoltrisOptimizer.Core.PreparePc;

public class PreparePcFlowController
{
	private readonly ILoggingService _logger;

	private readonly ContentControl _contentHost;

	private readonly Action? _onComplete;

	public event EventHandler? FlowCompleted;

	public PreparePcFlowController(ContentControl contentHost, ILoggingService? logger = null, Action? onComplete = null)
	{
		_contentHost = contentHost ?? throw new ArgumentNullException("contentHost");
		_logger = logger ?? App.LoggingService;
		_onComplete = onComplete;
	}

	public void StartAfterProfilerOptimizations(string[] appliedOptimizations)
	{
		_logger.LogInfo("[PreparePcFlow] Iniciando fluxo pós-otimizações do Profiler (Skipping Success View)");
		_logger.LogInfo("[PreparePcFlow] Fluxo automático DESABILITADO - aguardando ação do usuário");
	}

	public void StartManually()
	{
		_logger.LogInfo("[PreparePcFlow] Iniciando fluxo manualmente");
		ShowPreparePcIntroAsync();
	}

	private async Task ShowPreparePcIntroAsync()
	{
		try
		{
			_logger.LogInfo("[PreparePcFlow] Mostrando Intro do Prepare PC");
			await Task.Delay(100);
			PreparePcIntroView introView = null;
			DispatcherOperation op = ((DispatcherObject)Application.Current).Dispatcher.InvokeAsync((Action)delegate
			{
				introView = new PreparePcIntroView();
			});
			await op;
			if (introView == null)
			{
				throw new Exception("Falha ao criar PreparePcIntroView (null)");
			}
			introView.OnStartRequested += delegate
			{
				_logger.LogInfo("[PreparePcFlow] Usuário iniciou Prepare PC da Intro");
				ShowPreparePcConfirmationAsync();
			};
			introView.OnSkip += delegate
			{
				_logger.LogInfo("[PreparePcFlow] Usuário pulou da Intro");
				CompleteFlow();
			};
			introView.OnSchedule += delegate
			{
				_logger.LogInfo("[PreparePcFlow] Prepare PC agendado da Intro");
				CompleteFlow();
			};
			_contentHost.Content = introView;
		}
		catch (Exception ex2)
		{
			Exception ex = ex2;
			_logger.LogError("[PreparePcFlow] ERRO CRÍTICO ao mostrar Intro: " + ex.Message, ex);
			MessageBox.Show("Erro ao carregar a interface de preparação: " + ex.Message + "\n\nO sistema continuará para o Dashboard.", "Erro de Interface", MessageBoxButton.OK, MessageBoxImage.Hand);
			CompleteFlow();
		}
	}

	private async Task ShowPreparePcConfirmationAsync()
	{
		_logger.LogInfo("[PreparePcFlow] Mostrando confirmação do Prepare PC");
		PreparePcManager manager = new PreparePcManager(_logger);
		PreparePcConfirmationView confirmationView = new PreparePcConfirmationView(await manager.RunPreChecksAsync());
		confirmationView.OnProceed += delegate(object? s, PreparePcOptions options)
		{
			ILoggingService logger = _logger;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(39, 1);
			defaultInterpolatedStringHandler.AppendLiteral("[PreparePcFlow] Usuário escolheu modo: ");
			defaultInterpolatedStringHandler.AppendFormatted(options.Mode);
			logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
			StartPreparePcExecution(options);
		};
		confirmationView.OnSkip += delegate
		{
			_logger.LogInfo("[PreparePcFlow] Usuário pulou Prepare PC");
			CompleteFlow();
		};
		confirmationView.OnSchedule += delegate
		{
			_logger.LogInfo("[PreparePcFlow] Prepare PC agendado pelo modal");
			CompleteFlow();
		};
		_contentHost.Content = confirmationView;
	}

	private void StartPreparePcExecution(PreparePcOptions options)
	{
		PreparePcOptions options2 = options;
		_logger.LogInfo("[PreparePcFlow] Iniciando execução do Prepare PC em background");
		CompleteFlow();
		Task.Run(async delegate
		{
			try
			{
				PreparePcManager manager = new PreparePcManager(_logger);
				PreparePcResult result = await manager.ExecuteAsync(progress: new Progress<PreparePcProgress>(delegate
				{
				}), options: options2);
				if (result.RequiresReboot)
				{
					await ((DispatcherObject)Application.Current).Dispatcher.InvokeAsync((Action)delegate
					{
						RestartConfirmationModal restartConfirmationModal = new RestartConfirmationModal
						{
							Owner = Application.Current.MainWindow
						};
						restartConfirmationModal.ShowDialog();
					});
				}
				ILoggingService logger = _logger;
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(46, 1);
				defaultInterpolatedStringHandler.AppendLiteral("[PreparePcFlow] Prepare PC concluído: Success=");
				defaultInterpolatedStringHandler.AppendFormatted(result.Success);
				logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
			}
			catch (Exception ex2)
			{
				Exception ex = ex2;
				_logger.LogError("[PreparePcFlow] Erro durante execução do Prepare PC", ex);
				GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("PreparePcOptimizationError"));
			}
		});
		_logger.LogInfo("[PreparePcFlow] Otimização iniciada em background, usuário pode continuar navegando");
	}

	private void CompleteFlow()
	{
		_logger.LogInfo("[PreparePcFlow] Fluxo completado, redirecionando para Dashboard");
		MarkFirstRunComplete();
		_onComplete?.Invoke();
		this.FlowCompleted?.Invoke(this, EventArgs.Empty);
	}

	private void MarkFirstRunComplete()
	{
		try
		{
			string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Voltris", "first_run_complete.flag");
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			File.WriteAllText(path, DateTime.Now.ToString("o"));
		}
		catch (Exception ex)
		{
			_logger.LogWarning("[PreparePcFlow] Erro ao marcar primeira execução: " + ex.Message);
		}
	}

	public static bool HasPendingScheduledPreparePc()
	{
		try
		{
			string scheduleFilePath = GetScheduleFilePath();
			if (!File.Exists(scheduleFilePath))
			{
				return false;
			}
			string json = File.ReadAllText(scheduleFilePath);
			ScheduleData scheduleData = JsonSerializer.Deserialize<ScheduleData>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
			if (scheduleData == null)
			{
				return false;
			}
			if (scheduleData.IsNextStartup)
			{
				return true;
			}
			if (scheduleData.ScheduledTime > DateTime.Now)
			{
				return false;
			}
			return true;
		}
		catch
		{
			return false;
		}
	}

	public static void ClearScheduledPreparePc()
	{
		try
		{
			string scheduleFilePath = GetScheduleFilePath();
			if (File.Exists(scheduleFilePath))
			{
				File.Delete(scheduleFilePath);
			}
			ProcessStartInfo startInfo = new ProcessStartInfo
			{
				FileName = "schtasks",
				Arguments = "/delete /tn \"VoltrisPreparePc\" /f",
				UseShellExecute = false,
				CreateNoWindow = true
			};
			using Process process = Process.Start(startInfo);
			process?.WaitForExit(3000);
		}
		catch
		{
		}
	}

	public void ExecuteScheduledPreparePcIfPending()
	{
		ScheduleData pendingSchedule = GetPendingSchedule();
		if (pendingSchedule != null)
		{
			bool flag = false;
			if (pendingSchedule.IsNextStartup)
			{
				flag = true;
			}
			else if (pendingSchedule.ScheduledTime <= DateTime.Now)
			{
				flag = true;
			}
			if (flag)
			{
				_logger.LogInfo("[PreparePcFlow] Executando Prepare PC agendado");
				ClearScheduledPreparePc();
				ShowPreparePcConfirmationAsync();
			}
		}
	}

	public static (bool HasSchedule, DateTime? ScheduledTime, bool IsNextStartup) GetScheduleInfo()
	{
		ScheduleData pendingSchedule = GetPendingSchedule();
		if (pendingSchedule == null)
		{
			return (false, null, false);
		}
		return (true, pendingSchedule.ScheduledTime, pendingSchedule.IsNextStartup);
	}

	public static ScheduleData? GetPendingSchedule()
	{
		try
		{
			string scheduleFilePath = GetScheduleFilePath();
			if (!File.Exists(scheduleFilePath))
			{
				return null;
			}
			string json = File.ReadAllText(scheduleFilePath);
			return JsonSerializer.Deserialize<ScheduleData>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
		}
		catch
		{
			return null;
		}
	}

	private static string GetScheduleFilePath()
	{
		return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Voltris", "preparepc_schedule.json");
	}
}
