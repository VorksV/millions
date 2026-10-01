using System;

namespace VoltrisOptimizer.Core.Body;

public sealed class ContextConfirmedEventArgs : EventArgs
{
	public OperationalContext Context { get; init; }

	public OperationalContext OldContext { get; init; }

	public int TotalCycles { get; init; }

	public string TriggerProcess { get; init; } = string.Empty;

}
