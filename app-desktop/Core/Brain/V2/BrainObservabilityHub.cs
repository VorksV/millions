using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Core.Brain.V2;

public static class BrainObservabilityHub
{
	private static readonly Channel<BrainObservabilityEvent> _channel;

	private static int _started;

	public static event EventHandler<BrainObservabilityEvent>? EventPublished;

	static BrainObservabilityHub()
	{
		_channel = Channel.CreateBounded<BrainObservabilityEvent>(new BoundedChannelOptions(1024)
		{
			SingleReader = true,
			SingleWriter = false,
			FullMode = BoundedChannelFullMode.DropOldest
		});
		EnsureStarted();
	}

	private static void EnsureStarted()
	{
		if (Interlocked.Exchange(ref _started, 1) == 1)
		{
			return;
		}
		Task.Run(async delegate
		{
			await foreach (BrainObservabilityEvent evt in _channel.Reader.ReadAllAsync().ConfigureAwait(continueOnCapturedContext: false))
			{
				try
				{
					BrainObservabilityHub.EventPublished?.Invoke(null, evt);
				}
				catch (Exception ex)
				{
					App.LoggingService?.LogError($"[BrainObservabilityHub] {ex.Message}", ex);
				}
			}
		});
	}

	public static void Publish(BrainObservabilityEvent evt)
	{
		EnsureStarted();
		_channel.Writer.TryWrite(evt);
	}

	public static void Publish(BrainEventSeverity severity, string source, string category, string message, string? context = null)
	{
		Publish(new BrainObservabilityEvent
		{
			TimestampUtc = DateTime.UtcNow,
			Severity = severity,
			Source = source,
			Category = category,
			Message = message,
			Context = context
		});
	}
}
