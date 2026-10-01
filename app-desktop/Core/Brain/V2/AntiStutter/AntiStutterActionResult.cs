using System;

namespace VoltrisOptimizer.Core.Brain.V2.AntiStutter;

public sealed class AntiStutterActionResult
{
	public string ActionId { get; init; } = string.Empty;


	public string ActionName { get; init; } = string.Empty;


	public bool Success { get; init; }

	public string Error { get; init; } = string.Empty;


	public TimeSpan ExecutionTime { get; init; }
}
