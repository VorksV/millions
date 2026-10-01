namespace VoltrisOptimizer.Core.Brain.V2;

public sealed class BrainActionResultV2
{
	public BrainActionV2 Action { get; init; } = BrainActionV2.NoOp();


	public bool Executed { get; init; }

	public bool Skipped { get; init; }

	public string SkipReason { get; init; } = string.Empty;


	public bool ImpactVerified { get; init; }

	public string ImpactDetail { get; init; } = string.Empty;


	public double ExecutionMs { get; init; }
}
