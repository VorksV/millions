using System;

namespace VoltrisOptimizer.Core.Body;

public sealed class DecisionExecutedEventArgs : EventArgs
{
	public BrainDecision Decision { get; init; } = null;


	public string ArmUsed { get; init; } = string.Empty;


	public bool Success { get; init; }

	public double ExecutionTimeMs { get; init; }
}
