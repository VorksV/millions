using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using VoltrisOptimizer.Core.PreparePc.Interfaces;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Core.PreparePc.UI.Views;

public partial class PreparePcProgressView : UserControl
{
	private readonly PreparePcManager _manager;

	private readonly PreparePcOptions _options;

	private readonly StringBuilder _allLogs = new StringBuilder();

	private readonly Dictionary<string, Border> _stepBorders = new Dictionary<string, Border>();

	private PreparePcResult? _result;

	private CancellationTokenSource? _cts;

	private DateTime _startTime;

	private DispatcherTimer? _uiTimer;

	private EventHandler<string>? _logHandler;

	private EventHandler<PreparePcProgress>? _progressHandler;

	public event EventHandler<PreparePcResult>? OnCompleted;

	public event EventHandler? OnContinue;

	public event EventHandler? OnCancelled;

	public PreparePcProgressView(PreparePcOptions options)
	{
		InitializeComponent();
		_options = options;
		_manager = new PreparePcManager(App.LoggingService);
		_logHandler = delegate(object? s, string log)
		{
			AddLog(log);
		};
		_progressHandler = delegate(object? s, PreparePcProgress progress)
		{
			UpdateProgress(progress);
		};
		_manager.LogAdded += _logHandler;
		_manager.ProgressChanged += _progressHandler;
		base.Loaded += async delegate
		{
			ScrollViewer scrollViewer = FindParent<ScrollViewer>((DependencyObject)(object)this);
			if (scrollViewer != null)
			{
				scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
				scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
			}
			await StartPreparationAsync();
		};
		base.Unloaded += delegate
		{
			try
			{
				if (_logHandler != null)
				{
					_manager.LogAdded -= _logHandler;
				}
				if (_progressHandler != null)
				{
					_manager.ProgressChanged -= _progressHandler;
				}
			}
			catch
			{
			}
			try
			{
				_cts?.Cancel();
			}
			catch
			{
			}
			try
			{
				DispatcherTimer? uiTimer = _uiTimer;
				if (uiTimer != null)
				{
					uiTimer!.Stop();
				}
				_uiTimer = null;
			}
			catch
			{
			}
		};
	}

	private T? FindParent<T>(DependencyObject child) where T : DependencyObject
	{
		DependencyObject parent = VisualTreeHelper.GetParent(child);
		if (parent == null)
		{
			return default(T);
		}
		T val = (T)(object)((parent is T) ? parent : null);
		if (val != null)
		{
			return val;
		}
		return FindParent<T>(parent);
	}

	private async Task StartPreparationAsync()
	{
		_cts = new CancellationTokenSource();
		_startTime = DateTime.Now;
		InitializeStepsUI();
		PreparePcProgressView preparePcProgressView = this;
		DispatcherTimer val = new DispatcherTimer();
		val.Interval = TimeSpan.FromSeconds(1.0);
		preparePcProgressView._uiTimer = val;
		_uiTimer!.Tick += (EventHandler)delegate
		{
			TimeSpan value = DateTime.Now - _startTime;
			TextBlock elapsedTimeText = ElapsedTimeText;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(7, 1);
			defaultInterpolatedStringHandler.AppendLiteral("Tempo: ");
			defaultInterpolatedStringHandler.AppendFormatted(value, "mm\\:ss");
			elapsedTimeText.Text = defaultInterpolatedStringHandler.ToStringAndClear();
		};
		_uiTimer!.Start();
		try
		{
			Progress<PreparePcProgress> progress = new Progress<PreparePcProgress>(UpdateProgress);
			_result = await _manager.ExecuteAsync(_options, progress, _cts!.Token);
			DispatcherTimer? uiTimer = _uiTimer;
			if (uiTimer != null)
			{
				uiTimer!.Stop();
			}
			ShowCompletion(_result);
			this.OnCompleted?.Invoke(this, _result);
		}
		catch (OperationCanceledException)
		{
			DispatcherTimer? uiTimer2 = _uiTimer;
			if (uiTimer2 != null)
			{
				uiTimer2!.Stop();
			}
			ShowCancelled();
			this.OnCancelled?.Invoke(this, EventArgs.Empty);
		}
		catch (Exception ex3)
		{
			Exception ex = ex3;
			DispatcherTimer? uiTimer3 = _uiTimer;
			if (uiTimer3 != null)
			{
				uiTimer3!.Stop();
			}
			AddLog(string.Format(LocalizationService.Instance.GetString("PreparePcCriticalErrorLog"), ex.Message));
			ShowError(ex);
		}
	}

	private void InitializeStepsUI()
	{
		StepsStatusPanel.Children.Clear();
		_stepBorders.Clear();
		IEnumerable<(string, string, RiskCategory, int)> stepsToRun = _manager.GetStepsToRun(_options);
		foreach (var item4 in stepsToRun)
		{
			string item = item4.Item1;
			string item2 = item4.Item2;
			RiskCategory item3 = item4.Item3;
			Border border = new Border
			{
				Background = new SolidColorBrush(Color.FromRgb(26, 26, 46)),
				CornerRadius = new CornerRadius(6.0),
				Padding = new Thickness(12.0, 8.0, 12.0, 8.0),
				Margin = new Thickness(0.0, 0.0, 0.0, 6.0)
			};
			Grid grid = new Grid();
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(24.0)
			});
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(1.0, GridUnitType.Star)
			});
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = GridLength.Auto
			});
			TextBlock element = new TextBlock
			{
				Text = "⏳",
				FontSize = 14.0,
				VerticalAlignment = VerticalAlignment.Center
			};
			Grid.SetColumn(element, 0);
			TextBlock element2 = new TextBlock
			{
				Text = item,
				Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
				VerticalAlignment = VerticalAlignment.Center,
				FontSize = 12.0
			};
			Grid.SetColumn(element2, 1);
			TextBlock element3 = new TextBlock
			{
				Text = LocalizationService.Instance.GetString("Awaiting"),
				Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
				FontSize = 11.0,
				VerticalAlignment = VerticalAlignment.Center
			};
			Grid.SetColumn(element3, 2);
			grid.Children.Add(element);
			grid.Children.Add(element2);
			grid.Children.Add(element3);
			border.Child = grid;
			StepsStatusPanel.Children.Add(border);
			_stepBorders[item] = border;
		}
	}

	private void UpdateProgress(PreparePcProgress progress)
	{
		PreparePcProgress progress2 = progress;
		((DispatcherObject)this).Dispatcher.Invoke((Action)delegate
		{
			TextBlock currentStepText = CurrentStepText;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(8, 3);
			defaultInterpolatedStringHandler.AppendLiteral("Step ");
			defaultInterpolatedStringHandler.AppendFormatted(progress2.CurrentStepIndex);
			defaultInterpolatedStringHandler.AppendLiteral("/");
			defaultInterpolatedStringHandler.AppendFormatted(progress2.TotalSteps);
			defaultInterpolatedStringHandler.AppendLiteral(": ");
			defaultInterpolatedStringHandler.AppendFormatted(progress2.CurrentStepName);
			currentStepText.Text = defaultInterpolatedStringHandler.ToStringAndClear();
			CurrentActionText.Text = progress2.CurrentAction;
			OverallProgress.Value = progress2.OverallPercent;
			TextBlock percentText = PercentText;
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(1, 1);
			defaultInterpolatedStringHandler.AppendFormatted(progress2.OverallPercent);
			defaultInterpolatedStringHandler.AppendLiteral("%");
			percentText.Text = defaultInterpolatedStringHandler.ToStringAndClear();
			if (progress2.EstimatedRemaining.TotalSeconds > 0.0)
			{
				RemainingTimeText.Text = string.Format(LocalizationService.Instance.GetString("RemainingTime"), FormatTime(progress2.EstimatedRemaining));
			}
			UpdateStepStatus(progress2.CurrentStepName, progress2.CurrentStatus);
		});
	}

	private void UpdateStepStatus(string stepName, StepStatus status)
	{
		if (_stepBorders.TryGetValue(stepName, out var value) && value.Child is Grid grid)
		{
			TextBlock textBlock = grid.Children[0] as TextBlock;
			TextBlock textBlock2 = grid.Children[1] as TextBlock;
			TextBlock textBlock3 = grid.Children[2] as TextBlock;
			switch (status)
			{
			case StepStatus.Running:
				textBlock.Text = "\ud83d\udd04";
				textBlock2.Foreground = new SolidColorBrush(Colors.White);
				textBlock3.Text = LocalizationService.Instance.GetString("RunningStatus");
				textBlock3.Foreground = new SolidColorBrush(Color.FromRgb(108, 92, 231));
				value.Background = new SolidColorBrush(Color.FromRgb(37, 37, 66));
				break;
			case StepStatus.Completed:
				textBlock.Text = "✅";
				textBlock2.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80));
				textBlock3.Text = LocalizationService.Instance.GetString("CompletedStatus");
				textBlock3.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80));
				value.Background = new SolidColorBrush(Color.FromRgb(26, 42, 26));
				break;
			case StepStatus.Failed:
				textBlock.Text = "❌";
				textBlock2.Foreground = new SolidColorBrush(Color.FromRgb(244, 67, 54));
				textBlock3.Text = LocalizationService.Instance.GetString("FailedStatus");
				textBlock3.Foreground = new SolidColorBrush(Color.FromRgb(244, 67, 54));
				value.Background = new SolidColorBrush(Color.FromRgb(42, 26, 26));
				break;
			case StepStatus.Skipped:
				textBlock.Text = "⏭️";
				textBlock2.Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 128));
				textBlock3.Text = LocalizationService.Instance.GetString("SkippedStatus");
				textBlock3.Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 128));
				break;
			case StepStatus.Cancelled:
				textBlock.Text = "\ud83d\udeab";
				textBlock2.Foreground = new SolidColorBrush(Color.FromRgb(byte.MaxValue, 193, 7));
				textBlock3.Text = LocalizationService.Instance.GetString("CancelledStatus");
				textBlock3.Foreground = new SolidColorBrush(Color.FromRgb(byte.MaxValue, 193, 7));
				break;
			}
		}
	}

	private void AddLog(string log)
	{
		string log2 = log;
		((DispatcherObject)this).Dispatcher.Invoke((Action)delegate
		{
			_allLogs.AppendLine(log2);
			LogsTextBlock.Text = _allLogs.ToString();
			LogsScrollViewer.ScrollToEnd();
		});
	}

	private void ShowCompletion(PreparePcResult result)
	{
		CancelBtn.Visibility = Visibility.Collapsed;
		CompletedActions.Visibility = Visibility.Visible;
		if (result.Success)
		{
			StatusText.Text = LocalizationService.Instance.GetString("CompletedHeader");
			HeaderText.Text = LocalizationService.Instance.GetString("PreparationCompleted");
			SubHeaderText.Text = (result.RequiresReboot ? LocalizationService.Instance.GetString("PreparePcRebootRequiredDetail") : LocalizationService.Instance.GetString("PreparationSuccessDetail"));
		}
		else
		{
			StatusText.Text = LocalizationService.Instance.GetString("AttentionHeader");
			HeaderText.Text = LocalizationService.Instance.GetString("PreparationCompletedWithWarnings");
			SubHeaderText.Text = string.Format(LocalizationService.Instance.GetString("FailedStepsMessage"), result.FailedSteps);
			SubHeaderText.Foreground = new SolidColorBrush(Color.FromRgb(byte.MaxValue, 193, 7));
		}
		OverallProgress.Value = 100.0;
		PercentText.Text = "100%";
		TimeSpan timeSpan = DateTime.Now - _startTime;
		ElapsedTimeText.Text = string.Format(LocalizationService.Instance.GetString("TotalTime"), timeSpan.ToString("mm:ss"));
		RemainingTimeText.Visibility = Visibility.Collapsed;
		AddLog("");
		AddLog("═════════════════════════════════════════");
		AddLog(LocalizationService.Instance.GetString("PreparePcSummaryHeader"));
		AddLog("═════════════════════════════════════════");
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(6, 1);
		defaultInterpolatedStringHandler.AppendLiteral("Modo: ");
		defaultInterpolatedStringHandler.AppendFormatted(result.Mode);
		AddLog(defaultInterpolatedStringHandler.ToStringAndClear());
		defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(9, 1);
		defaultInterpolatedStringHandler.AppendLiteral("Duração: ");
		defaultInterpolatedStringHandler.AppendFormatted(result.TotalDuration, "mm\\:ss");
		AddLog(defaultInterpolatedStringHandler.ToStringAndClear());
		defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(9, 1);
		defaultInterpolatedStringHandler.AppendLiteral("Sucesso: ");
		defaultInterpolatedStringHandler.AppendFormatted(result.SuccessfulSteps);
		AddLog(defaultInterpolatedStringHandler.ToStringAndClear());
		defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(8, 1);
		defaultInterpolatedStringHandler.AppendLiteral("Falhas: ");
		defaultInterpolatedStringHandler.AppendFormatted(result.FailedSteps);
		AddLog(defaultInterpolatedStringHandler.ToStringAndClear());
		defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(11, 1);
		defaultInterpolatedStringHandler.AppendLiteral("Ignorados: ");
		defaultInterpolatedStringHandler.AppendFormatted(result.SkippedSteps);
		AddLog(defaultInterpolatedStringHandler.ToStringAndClear());
		if (result.RequiresReboot)
		{
			AddLog(LocalizationService.Instance.GetString("PreparePcRebootRequiredHeader"));
		}
		AddLog(string.Format(LocalizationService.Instance.GetString("PreparePcBackupLabel"), result.BackupFolderPath));
		AddLog("═════════════════════════════════════════");
		try
		{
			OptimizationHistory obj = new OptimizationHistory
			{
				Id = Guid.NewGuid().ToString(),
				ActionType = "Prepare PC"
			};
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(47, 2);
			defaultInterpolatedStringHandler.AppendLiteral("Preparação completa do PC (");
			defaultInterpolatedStringHandler.AppendFormatted(result.Mode);
			defaultInterpolatedStringHandler.AppendLiteral("): ");
			defaultInterpolatedStringHandler.AppendFormatted(result.SuccessfulSteps);
			defaultInterpolatedStringHandler.AppendLiteral(" steps concluídos");
			obj.Description = defaultInterpolatedStringHandler.ToStringAndClear();
			obj.Timestamp = DateTime.Now;
			obj.Duration = result.TotalDuration;
			obj.SpaceFreed = 0L;
			obj.Success = result.Success;
			obj.Details = new Dictionary<string, object>
			{
				{ "Mode", result.Mode },
				{ "SuccessfulSteps", result.SuccessfulSteps },
				{ "FailedSteps", result.FailedSteps },
				{ "SkippedSteps", result.SkippedSteps },
				{ "RequiresReboot", result.RequiresReboot },
				{
					"BackupPath",
					result.BackupFolderPath ?? "N/A"
				}
			};
			OptimizationHistory entry = obj;
			HistoryService.Instance.AddHistoryEntry(entry);
		}
		catch (Exception ex)
		{
			App.LoggingService?.LogWarning("Falha ao salvar histórico do Prepare PC: " + ex.Message);
		}
	}

	private void ShowCancelled()
	{
		CancelBtn.Visibility = Visibility.Collapsed;
		CompletedActions.Visibility = Visibility.Visible;
		RollbackBtn.Visibility = Visibility.Visible;
		StatusText.Text = LocalizationService.Instance.GetString("CancelledHeader");
		HeaderText.Text = LocalizationService.Instance.GetString("CancelledHeader");
		SubHeaderText.Text = LocalizationService.Instance.GetString("CancelledDetail");
		SubHeaderText.Foreground = new SolidColorBrush(Color.FromRgb(byte.MaxValue, 193, 7));
	}

	private void ShowError(Exception ex)
	{
		CancelBtn.Visibility = Visibility.Collapsed;
		CompletedActions.Visibility = Visibility.Visible;
		StatusText.Text = LocalizationService.Instance.GetString("ErrorHeader");
		HeaderText.Text = LocalizationService.Instance.GetString("ErrorDetail");
		SubHeaderText.Text = ex.Message;
		SubHeaderText.Foreground = new SolidColorBrush(Color.FromRgb(244, 67, 54));
	}

	private void CancelBtn_Click(object sender, RoutedEventArgs e)
	{
		MessageBoxResult messageBoxResult = MessageBox.Show(LocalizationService.Instance.GetString("CancelPreparationConfirm"), LocalizationService.Instance.GetString("CancelPreparationTitle"), MessageBoxButton.YesNo, MessageBoxImage.Exclamation);
		if (messageBoxResult == MessageBoxResult.Yes)
		{
			_manager.Cancel();
		}
	}

	private async void RollbackBtn_Click(object sender, RoutedEventArgs e)
	{
		MessageBoxResult result = MessageBox.Show(LocalizationService.Instance.GetString("RollbackConfirm"), LocalizationService.Instance.GetString("RollbackTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
		if (result == MessageBoxResult.Yes)
		{
			RollbackBtn.IsEnabled = false;
			RollbackBtn.Content = LocalizationService.Instance.GetString("PreparePcReverting");
			AddLog("");
			AddLog("═════════════════════════════════════════");
			AddLog(LocalizationService.Instance.GetString("PreparePcRollbackStartingLog"));
			AddLog("═════════════════════════════════════════");
			if (await _manager.RollbackAsync())
			{
				AddLog(LocalizationService.Instance.GetString("RollbackCompleted"));
				RollbackBtn.Content = LocalizationService.Instance.GetString("PreparePcReverted");
				MessageBox.Show(LocalizationService.Instance.GetString("PreparePcRollbackSuccessMsg"), LocalizationService.Instance.GetString("RollbackCompletedTitle"), MessageBoxButton.OK, MessageBoxImage.Asterisk);
			}
			else
			{
				AddLog(LocalizationService.Instance.GetString("RollbackPartial"));
				RollbackBtn.Content = LocalizationService.Instance.GetString("PreparePcPartial");
				RollbackBtn.IsEnabled = true;
				MessageBox.Show(LocalizationService.Instance.GetString("PreparePcRollbackPartialMsg"), LocalizationService.Instance.GetString("RollbackPartialTitle"), MessageBoxButton.OK, MessageBoxImage.Exclamation);
			}
		}
	}

	private void ViewReportBtn_Click(object sender, RoutedEventArgs e)
	{
		if (_result?.ReportPath != null && File.Exists(_result!.ReportPath))
		{
			try
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = _result!.ReportPath,
					UseShellExecute = true
				});
				return;
			}
			catch
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = System.IO.Path.GetDirectoryName(_result!.ReportPath),
					UseShellExecute = true
				});
				return;
			}
		}
		MessageBox.Show(LocalizationService.Instance.GetString("PreparePcReportNotFoundDlg"), LocalizationService.Instance.GetString("ErrorTitleShort"), MessageBoxButton.OK, MessageBoxImage.Hand);
	}

	private void ContinueBtn_Click(object sender, RoutedEventArgs e)
	{
		PreparePcResult? result = _result;
		if (result != null && result!.RequiresReboot)
		{
			switch (MessageBox.Show(LocalizationService.Instance.GetString("PreparePcRestartPrompt"), LocalizationService.Instance.GetString("NotificationRestartNeededTitle"), MessageBoxButton.YesNoCancel, MessageBoxImage.Question))
			{
			case MessageBoxResult.Yes:
				Process.Start("shutdown", "/r /t 10 /c \"Reiniciando para aplicar otimizações do Voltris\"");
				Application.Current.Shutdown();
				return;
			case MessageBoxResult.Cancel:
				return;
			}
		}
		this.OnContinue?.Invoke(this, EventArgs.Empty);
	}

	private void ExportLogsBtn_Click(object sender, RoutedEventArgs e)
	{
		SaveFileDialog obj = new SaveFileDialog
		{
			Filter = "Arquivo de Log (*.log)|*.log|Arquivo de Texto (*.txt)|*.txt"
		};
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(22, 1);
		defaultInterpolatedStringHandler.AppendLiteral("voltris_preparepc_");
		defaultInterpolatedStringHandler.AppendFormatted(DateTime.Now, "yyyyMMdd_HHmmss");
		defaultInterpolatedStringHandler.AppendLiteral(".log");
		obj.FileName = defaultInterpolatedStringHandler.ToStringAndClear();
		SaveFileDialog saveFileDialog = obj;
		if (saveFileDialog.ShowDialog().GetValueOrDefault())
		{
			try
			{
				File.WriteAllText(saveFileDialog.FileName, _allLogs.ToString());
				MessageBox.Show(string.Format(LocalizationService.Instance.GetString("PreparePcLogsExportedTo"), saveFileDialog.FileName), LocalizationService.Instance.GetString("LogsExportCompleteTitle"), MessageBoxButton.OK, MessageBoxImage.Asterisk);
			}
			catch (Exception ex)
			{
				MessageBox.Show(string.Format(LocalizationService.Instance.GetString("PreparePcExportLogsError"), ex.Message), LocalizationService.Instance.GetString("ErrorTitleShort"), MessageBoxButton.OK, MessageBoxImage.Hand);
			}
		}
	}

	private string FormatTime(TimeSpan time)
	{
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler;
		if (time.TotalHours >= 1.0)
		{
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(5, 2);
			defaultInterpolatedStringHandler.AppendFormatted((int)time.TotalHours);
			defaultInterpolatedStringHandler.AppendLiteral("h ");
			defaultInterpolatedStringHandler.AppendFormatted(time.Minutes);
			defaultInterpolatedStringHandler.AppendLiteral("min");
			return defaultInterpolatedStringHandler.ToStringAndClear();
		}
		if (time.TotalMinutes >= 1.0)
		{
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(3, 1);
			defaultInterpolatedStringHandler.AppendFormatted((int)time.TotalMinutes);
			defaultInterpolatedStringHandler.AppendLiteral("min");
			return defaultInterpolatedStringHandler.ToStringAndClear();
		}
		defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(1, 1);
		defaultInterpolatedStringHandler.AppendFormatted((int)time.TotalSeconds);
		defaultInterpolatedStringHandler.AppendLiteral("s");
		return defaultInterpolatedStringHandler.ToStringAndClear();
	}

}
