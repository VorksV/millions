using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Core;

public sealed class BackgroundScheduler : IDisposable
{
	public enum TaskPriority
	{
		Critical,
		High,
		Normal,
		Low,
		Idle
	}

	private sealed class ScheduledTask
	{
		public volatile bool IsRunning;

		public string Id { get; init; }

		public Func<CancellationToken, Task> Action { get; init; }

		public TimeSpan BaseInterval { get; init; }

		public TaskPriority Priority { get; init; }

		public TimeSpan InitialDelay { get; init; }

		public TimeSpan Jitter { get; init; }

		public DateTime NextRunAt { get; set; }

		public int ConsecutiveErrors { get; set; }

		public ScheduledTask()
		{
		}
	}

	private static readonly Lazy<BackgroundScheduler> _instance = new Lazy<BackgroundScheduler>(() => new BackgroundScheduler());

	private const double CpuHighThreshold = 60.0;

	private const double CpuCritThreshold = 80.0;

	private const int IdleThresholdSecs = 30;

	private static readonly double[,] IntervalMultipliers = new double[5, 3]
	{
		{ 1.0, 1.0, 1.0 },
		{ 1.0, 1.5, 2.0 },
		{ 1.0, 2.5, 4.0 },
		{ 1.0, 4.0, 8.0 },
		{ 5.0, 10.0, 20.0 }  // CORRIGIDO: era 0.0,0.0,0.0 — causava execução a cada 100ms
	};

	private readonly ConcurrentDictionary<string, ScheduledTask> _tasks = new ConcurrentDictionary<string, ScheduledTask>();

	private readonly CancellationTokenSource _cts = new CancellationTokenSource();

	private Task? _dispatchLoop;

	private volatile int _loadLevel = 0;

	private volatile bool _isIdle = false;

	private double _currentCpuPercent = 0.0;

	private DateTime _lastCpuSample = DateTime.MinValue;

	private const int CpuSampleIntervalMs = 2000;

	public static BackgroundScheduler Instance => _instance.Value;

	private BackgroundScheduler()
	{
	}

	public void Register(string id, Func<CancellationToken, Task> action, TimeSpan baseInterval, TaskPriority priority = TaskPriority.Normal, TimeSpan initialDelay = default(TimeSpan), TimeSpan jitter = default(TimeSpan))
	{
		ScheduledTask value = new ScheduledTask
		{
			Id = id,
			Action = action,
			BaseInterval = baseInterval,
			Priority = priority,
			InitialDelay = initialDelay,
			Jitter = ((jitter == default(TimeSpan)) ? TimeSpan.FromMilliseconds(baseInterval.TotalMilliseconds * 0.1) : jitter),
			NextRunAt = DateTime.UtcNow + initialDelay,
			IsRunning = false
		};
		_tasks[id] = value;
	}

	public void Unregister(string id)
	{
		_tasks.TryRemove(id, out var _);
	}

	public void Start()
	{
		if (_dispatchLoop == null)
		{
			_dispatchLoop = Task.Run(() => DispatchLoopAsync(_cts.Token), _cts.Token);
		}
	}

	public async Task StopAsync()
	{
		_cts.Cancel();
		if (_dispatchLoop != null)
		{
			try
			{
				await _dispatchLoop!.WaitAsync(TimeSpan.FromSeconds(5.0));
			}
			catch
			{
			}
		}
	}

	public SchedulerMetrics GetMetrics()
	{
		return new SchedulerMetrics
		{
			RegisteredTasks = _tasks.Count,
			CurrentCpuPct = _currentCpuPercent,
			LoadLevel = _loadLevel,
			IsIdle = _isIdle
		};
	}

	private async Task DispatchLoopAsync(CancellationToken ct)
	{
		while (!ct.IsCancellationRequested)
		{
			try
			{
				UpdateSystemState();
				DateTime now = DateTime.UtcNow;
				DateTime nextDue = DateTime.MaxValue;
				foreach (KeyValuePair<string, ScheduledTask> task2 in _tasks)
				{
					ScheduledTask task = task2.Value;
					if (task.IsRunning)
					{
						continue;
					}
					if (now >= task.NextRunAt)
					{
						if (task.Priority != TaskPriority.Idle || _isIdle)
						{
							_ = RunTaskSafeAsync(task, ct);
						}
					}
					else if (task.NextRunAt < nextDue)
					{
						nextDue = task.NextRunAt;
					}
				}
				int sleepMs = ((nextDue == DateTime.MaxValue) ? 2000 : ((int)Math.Clamp((nextDue - DateTime.UtcNow).TotalMilliseconds, 500.0, 2000.0)));
				await Task.Delay(sleepMs, ct);
			}
			catch (OperationCanceledException)
			{
				break;
			}
			catch
			{
			}
		}
	}

	private async Task RunTaskSafeAsync(ScheduledTask task, CancellationToken ct)
	{
		try
		{
			await RunTaskAsync(task, ct);
		}
		catch
		{
			// Evitar que exceções não tratadas cheguem ao TaskScheduler.UnobservedTaskException
		}
	}

	private async Task RunTaskAsync(ScheduledTask task, CancellationToken ct)
	{
		task.IsRunning = true;
		Stopwatch sw = Stopwatch.StartNew();
		try
		{
			await task.Action(ct);
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception)
		{
			task.ConsecutiveErrors++;
			TimeSpan backoff = TimeSpan.FromSeconds(Math.Min(300.0, Math.Pow(2.0, task.ConsecutiveErrors)));
			task.NextRunAt = DateTime.UtcNow + backoff;
			task.IsRunning = false;
			return;
		}
		task.ConsecutiveErrors = 0;
		sw.Stop();
		double multiplier = GetIntervalMultiplier(task.Priority);
		double baseMs = task.BaseInterval.TotalMilliseconds * multiplier;
		double jitterMs = (Random.Shared.NextDouble() - 0.5) * 2.0 * task.Jitter.TotalMilliseconds;
		task.NextRunAt = DateTime.UtcNow + TimeSpan.FromMilliseconds(Math.Max(100.0, baseMs + jitterMs));
		task.IsRunning = false;
	}

	private void UpdateSystemState()
	{
		DateTime utcNow = DateTime.UtcNow;
		if (!((utcNow - _lastCpuSample).TotalMilliseconds < 2000.0))
		{
			_lastCpuSample = utcNow;
			SystemMetricsCache instance = SystemMetricsCache.Instance;
			_currentCpuPercent = instance.CpuPercent;
			double currentCpuPercent = _currentCpuPercent;
			if (1 == 0)
			{
			}
			int loadLevel = ((currentCpuPercent >= 80.0) ? 2 : ((currentCpuPercent >= 60.0) ? 1 : 0));
			if (1 == 0)
			{
			}
			_loadLevel = loadLevel;
			_isIdle = instance.LastInputMs >= 30000 && _currentCpuPercent < 20.0;
		}
	}

	private double GetIntervalMultiplier(TaskPriority priority)
	{
		int num = Math.Clamp(_loadLevel, 0, 2);
		return IntervalMultipliers[(int)priority, num];
	}

	public void Dispose()
	{
		_cts.Cancel();
		_cts.Dispose();
	}
}


