namespace VoltrisOptimizer.Core.Body;

public sealed class DecisionResult
{
	public bool Success { get; init; }

	public string Detail { get; init; } = string.Empty;


	public double ExecutionTimeMs { get; init; }
}
