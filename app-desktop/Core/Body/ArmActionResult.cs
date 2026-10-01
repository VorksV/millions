namespace VoltrisOptimizer.Core.Body;

public sealed class ArmActionResult
{
	public bool Success { get; init; }

	public string Message { get; init; } = string.Empty;


	public double ExecutionTimeMs { get; init; }
}
