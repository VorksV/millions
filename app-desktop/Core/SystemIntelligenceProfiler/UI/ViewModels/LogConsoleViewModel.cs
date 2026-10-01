using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace VoltrisOptimizer.Core.SystemIntelligenceProfiler.UI.ViewModels;

public class LogConsoleViewModel : INotifyPropertyChanged
{
	public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();


	public ICommand ExportCommand { get; }

	public ICommand ClearCommand { get; }

	public event PropertyChangedEventHandler? PropertyChanged;

	public LogConsoleViewModel()
	{
		ExportCommand = new RelayCommand(delegate
		{
			Export();
		});
		ClearCommand = new RelayCommand(delegate
		{
			Clear();
		});
		try
		{
			if (App.LoggingService != null)
			{
				string[] logs = App.LoggingService!.GetLogs();
				foreach (string item in logs)
				{
					Logs.Add(item);
				}
				App.LoggingService!.LogEntryAdded += OnLogEntryAdded;
			}
		}
		catch
		{
		}
	}

	private void OnLogEntryAdded(object? sender, string e)
	{
		string e2 = e;
		try
		{
			Application current = Application.Current;
			if (current == null)
			{
				return;
			}
			Dispatcher dispatcher = ((DispatcherObject)current).Dispatcher;
			if (dispatcher != null)
			{
				dispatcher.BeginInvoke((Delegate)(Action)delegate
				{
					Logs.Add(e2);
				}, Array.Empty<object>());
			}
		}
		catch
		{
		}
	}

	private void Export()
	{
		try
		{
			string text = Path.Combine(Path.GetTempPath(), "voltris_logs_export.txt");
			App.LoggingService?.ExportLogs(text);
			Process.Start(new ProcessStartInfo
			{
				FileName = text,
				UseShellExecute = true
			});
		}
		catch
		{
		}
	}

	private void Clear()
	{
		try
		{
			App.LoggingService?.ClearLogs();
			Logs.Clear();
		}
		catch
		{
		}
	}

	private void OnPropertyChanged(string name)
	{
		this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
	}
}
