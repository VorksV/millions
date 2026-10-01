using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace VoltrisOptimizer.Core.Async;

public static class AsyncExtensions
{
	public static async Task<T> AwaitSafe<T>(this Task<T> task, CancellationToken cancellationToken = default(CancellationToken))
	{
		return await AsyncSafeHelper.AwaitSafeAsync(task, cancellationToken);
	}

	public static async Task AwaitSafe(this Task task, CancellationToken cancellationToken = default(CancellationToken))
	{
		await AsyncSafeHelper.AwaitSafeAsync(task, cancellationToken);
	}

	public static async Task<T> InvokeOnUIAsync<T>(this Func<T> func, DispatcherPriority priority = DispatcherPriority.Normal)
	{
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		return await AsyncSafeHelper.InvokeOnUIThreadAsync(func, priority);
	}

	public static async Task InvokeOnUIAsync(this Action action, DispatcherPriority priority = DispatcherPriority.Normal)
	{
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		await AsyncSafeHelper.InvokeOnUIThreadAsync(action, priority);
	}

	public static async Task<T> WithTimeout<T>(this Task<T> task, TimeSpan timeout)
	{
		using CancellationTokenSource cts = new CancellationTokenSource(timeout);
		if (await Task.WhenAny(task, Task.Delay(timeout, cts.Token)) == task)
		{
			cts.Cancel();
			return await task;
		}
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(34, 1);
		defaultInterpolatedStringHandler.AppendLiteral("Operation timed out after ");
		defaultInterpolatedStringHandler.AppendFormatted(timeout.TotalSeconds);
		defaultInterpolatedStringHandler.AppendLiteral(" seconds");
		throw new TimeoutException(defaultInterpolatedStringHandler.ToStringAndClear());
	}

	public static async Task WithTimeout(this Task task, TimeSpan timeout)
	{
		using CancellationTokenSource cts = new CancellationTokenSource(timeout);
		if (await Task.WhenAny(task, Task.Delay(timeout, cts.Token)) == task)
		{
			cts.Cancel();
			await task;
			return;
		}
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(34, 1);
		defaultInterpolatedStringHandler.AppendLiteral("Operation timed out after ");
		defaultInterpolatedStringHandler.AppendFormatted(timeout.TotalSeconds);
		defaultInterpolatedStringHandler.AppendLiteral(" seconds");
		throw new TimeoutException(defaultInterpolatedStringHandler.ToStringAndClear());
	}
}


