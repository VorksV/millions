using System;
using VoltrisOptimizer.Core.Brain.V2;

namespace VoltrisOptimizer.Core.Body;

public sealed class BrainDecisionReadyEventArgs : EventArgs
{
	public BrainDecision Decision { get; init; } = null;


	public BrainStateKey State { get; init; }

	public SensorSnapshot Snapshot { get; init; } = null;

}
