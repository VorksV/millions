using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace VoltrisOptimizer.Core.Async;

public static class AsyncSafeHelper
{
	public static Task InvokeOnUIThreadAsync(Action action, DispatcherPriority priority = DispatcherPriority.Normal)
	{
		//IL_004f: Unknown result type (might be due to invalid IL or missing references)
		Application current = Application.Current;
		if (((current != null) ? ((DispatcherObject)current).Dispatcher : null) == null)
		{
			return Task.CompletedTask;
		}
		if (((DispatcherObject)Application.Current).Dispatcher.CheckAccess())
		{
			action();
			return Task.CompletedTask;
		}
		return ((DispatcherObject)Application.Current).Dispatcher.InvokeAsync(action, priority).Task;
	}

	public static Task<T> InvokeOnUIThreadAsync<T>(Func<T> func, DispatcherPriority priority = DispatcherPriority.Normal)
	{
		//IL_0057: Unknown result type (might be due to invalid IL or missing references)
		Application current = Application.Current;
		if (((current != null) ? ((DispatcherObject)current).Dispatcher : null) == null)
		{
			return Task.FromResult(default(T));
		}
		if (((DispatcherObject)Application.Current).Dispatcher.CheckAccess())
		{
			return Task.FromResult(func());
		}
		return ((DispatcherObject)Application.Current).Dispatcher.InvokeAsync<T>(func, priority).Task;
	}

	public static async Task<T> AwaitSafeAsync<T>(Task<T> task, CancellationToken cancellationToken = default(CancellationToken))
	{
		Application current = Application.Current;
		int num;
		if (current == null)
		{
			num = 0;
		}
		else
		{
			Dispatcher dispatcher = ((DispatcherObject)current).Dispatcher;
			num = (((dispatcher != null) ? new bool?(dispatcher.CheckAccess()) : null).GetValueOrDefault() ? 1 : 0);
		}
		if (num != 0)
		{
			return await task.ConfigureAwait(continueOnCapturedContext: false);
		}
		return await task.ConfigureAwait(continueOnCapturedContext: true);
	}

	public static async Task AwaitSafeAsync(Task task, CancellationToken cancellationToken = default(CancellationToken))
	{
		Application current = Application.Current;
		int num;
		if (current == null)
		{
			num = 0;
		}
		else
		{
			Dispatcher dispatcher = ((DispatcherObject)current).Dispatcher;
			num = (((dispatcher != null) ? new bool?(dispatcher.CheckAccess()) : null).GetValueOrDefault() ? 1 : 0);
		}
		if (num != 0)
		{
			await task.ConfigureAwait(continueOnCapturedContext: false);
		}
		else
		{
			await task.ConfigureAwait(continueOnCapturedContext: true);
		}
	}

	public static async Task<T> RunInBackgroundAsync<T>(Func<CancellationToken, Task<T>> backgroundAction, TimeSpan timeout, CancellationToken cancellationToken = default(CancellationToken))
	{
		Func<CancellationToken, Task<T>> backgroundAction2 = backgroundAction;
		CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		try
		{
			timeoutCts.CancelAfter(timeout);
			try
			{
				return await Task.Run(() => backgroundAction2(timeoutCts.Token), timeoutCts.Token);
			}
			catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
			{
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(34, 1);
				defaultInterpolatedStringHandler.AppendLiteral("Operation timed out after ");
				defaultInterpolatedStringHandler.AppendFormatted(timeout.TotalSeconds);
				defaultInterpolatedStringHandler.AppendLiteral(" seconds");
				throw new TimeoutException(defaultInterpolatedStringHandler.ToStringAndClear());
			}
		}
		finally
		{
			if (timeoutCts != null)
			{
				((IDisposable)timeoutCts).Dispose();
			}
		}
	}

	public static async Task RunInBackgroundAsync(Func<CancellationToken, Task> backgroundAction, TimeSpan timeout, CancellationToken cancellationToken = default(CancellationToken))
	{
		Func<CancellationToken, Task> backgroundAction2 = backgroundAction;
		CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		try
		{
			timeoutCts.CancelAfter(timeout);
			try
			{
				await Task.Run(() => backgroundAction2(timeoutCts.Token), timeoutCts.Token);
			}
			catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
			{
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(34, 1);
				defaultInterpolatedStringHandler.AppendLiteral("Operation timed out after ");
				defaultInterpolatedStringHandler.AppendFormatted(timeout.TotalSeconds);
				defaultInterpolatedStringHandler.AppendLiteral(" seconds");
				throw new TimeoutException(defaultInterpolatedStringHandler.ToStringAndClear());
			}
		}
		finally
		{
			if (timeoutCts != null)
			{
				((IDisposable)timeoutCts).Dispose();
			}
		}
	}

	public static Task CreateSafeTask(Action action, Action<Exception>? onError = null)
	{
		Action action2 = action;
		Action<Exception> onError2 = onError;
		return Task.Run(delegate
		{
			try
			{
				action2();
			}
			catch (Exception obj)
			{
				onError2?.Invoke(obj);
			}
		});
	}

	public static Task<T?> CreateSafeTask<T>(Func<T> func, Action<Exception>? onError = null)
	{
		Func<T> func2 = func;
		Action<Exception> onError2 = onError;
		return Task.Run(delegate
		{
			try
			{
				return func2();
			}
			catch (Exception obj)
			{
				onError2?.Invoke(obj);
				return default(T);
			}
		});
	}

	public static bool IsOnUIThread()
	{
		Application current = Application.Current;
		int result;
		if (current == null)
		{
			result = 0;
		}
		else
		{
			Dispatcher dispatcher = ((DispatcherObject)current).Dispatcher;
			result = (((dispatcher != null) ? new bool?(dispatcher.CheckAccess()) : null).GetValueOrDefault() ? 1 : 0);
		}
		return (byte)result != 0;
	}

	public static async Task<T> ForceAsync<T>(Func<T> func)
	{
		if (IsOnUIThread())
		{
			await Task.Yield();
			return await Task.Run(func).ConfigureAwait(continueOnCapturedContext: false);
		}
		return func();
	}

	public static async Task<T[]> WhenAllLimited<T>(IEnumerable<Func<Task<T>>> taskFactories, int maxConcurrency, CancellationToken cancellationToken = default(CancellationToken))
	{
		SemaphoreSlim semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
		List<Task<T>> tasks = new List<Task<T>>();
		foreach (Func<Task<T>> factory in taskFactories)
		{
			tasks.Add(Task.Run(async delegate
			{
				await semaphore.WaitAsync(cancellationToken);
				try
				{
					return await factory();
				}
				finally
				{
					semaphore.Release();
				}
			}, cancellationToken));
		}
		return await Task.WhenAll(tasks);
	}

	public static async Task<T> RetryAsync<T>(Func<Task<T>> operation, int maxAttempts = 3, TimeSpan delay = default(TimeSpan), CancellationToken cancellationToken = default(CancellationToken))
	{
		if (delay == default(TimeSpan))
		{
			delay = TimeSpan.FromMilliseconds(500.0);
		}
		for (int attempt = 1; attempt <= maxAttempts; attempt++)
		{
			try
			{
				return await operation();
			}
			catch (Exception) when (attempt < maxAttempts)
			{
				await Task.Delay(delay * attempt, cancellationToken);
			}
		}
		return await operation();
	}

	public static async Task<T> ToAsyncSafe<T>(Func<T> syncOperation, CancellationToken cancellationToken = default(CancellationToken))
	{
		return await Task.Run(syncOperation, cancellationToken);
	}
}


