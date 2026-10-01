namespace VoltrisOptimizer.Core.Body;

public sealed class SystemActionResult
{
	public bool Success { get; init; }

	public bool GuardBlocked { get; init; }

	public string GuardReason { get; init; } = string.Empty;

}
