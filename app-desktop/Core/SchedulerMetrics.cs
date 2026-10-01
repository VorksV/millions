namespace VoltrisOptimizer.Core;

public sealed class SchedulerMetrics
{
	public int RegisteredTasks { get; init; }

	public double CurrentCpuPct { get; init; }

	public int LoadLevel { get; init; }

	public bool IsIdle { get; init; }
}
