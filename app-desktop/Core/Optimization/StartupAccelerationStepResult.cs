namespace VoltrisOptimizer.Core.Optimization;

public sealed class StartupAccelerationStepResult
{
	public string Name { get; init; } = string.Empty;


	public bool Success { get; init; }

	public string ExpectedResult { get; init; } = string.Empty;


	public string ObtainedResult { get; init; } = string.Empty;


	public int EstimatedImpactPoints { get; init; }

	public int ExecutionTimeMs { get; init; }

	public string? Error { get; init; }
}
