using System;

namespace VoltrisOptimizer.Core.Body;

public sealed class RollbackResult
{
	public bool Success { get; set; }

	public string? Message { get; set; }

	public TimeSpan ExecutionTime { get; set; }

	public int ItemsRestored { get; set; }

	public int ItemsFailed { get; set; }

	public double DurationMs { get; set; }

	public string Details { get; set; } = string.Empty;

}
