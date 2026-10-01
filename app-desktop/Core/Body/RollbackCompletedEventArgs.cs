using System;

namespace VoltrisOptimizer.Core.Body;

public sealed class RollbackCompletedEventArgs : EventArgs
{
	public RollbackResult Result { get; init; } = null;


	public string Trigger { get; init; } = string.Empty;

}
