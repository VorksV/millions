using System;

namespace VoltrisOptimizer.Core.Body;

public sealed class ContextChangedEventArgs : EventArgs
{
	public OperationalContext OldContext { get; init; }

	public OperationalContext NewContext { get; init; }

	public string TriggerProcessName { get; init; } = string.Empty;


	public int ConfirmationCycles { get; init; }
}
