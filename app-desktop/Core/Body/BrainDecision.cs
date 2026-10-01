using System;
using VoltrisOptimizer.Core.Brain.V2;

namespace VoltrisOptimizer.Core.Body;

public sealed class BrainDecision
{
	public Guid Id { get; } = Guid.NewGuid();


	public DateTime CreatedAtUtc { get; } = DateTime.UtcNow;


	public DecisionActionType ActionType { get; init; }

	public int ActionValue { get; init; }

	public int? SecondaryValue { get; init; }

	public string? Target { get; init; }

	public BrainStateKey SourceState { get; init; }

	public double QValue { get; init; }

	public string Justification { get; init; } = string.Empty;


	public double Confidence { get; init; }

	public SensorSnapshot? ContextSnapshot { get; init; }

	public bool CanExecuteImmediate { get; init; }

	public bool RequiresConfirmation { get; init; }
}
