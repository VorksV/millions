using System;

namespace VoltrisOptimizer.Core.Brain.V2;

public sealed class BrainObservabilityEvent
{
	public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;


	public BrainEventSeverity Severity { get; init; } = BrainEventSeverity.Info;


	public string Source { get; init; } = "Brain";


	public string Category { get; init; } = "General";


	public string Message { get; init; } = string.Empty;


	public string? Context { get; init; }
}
