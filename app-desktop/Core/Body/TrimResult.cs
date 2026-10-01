namespace VoltrisOptimizer.Core.Body;

public sealed class TrimResult
{
	public int ProcessCount { get; init; }

	public long MbReleased { get; init; }

	public int SkippedByCriteria { get; init; }

	public int FailedCount { get; init; }

	public bool RequiresAdmin { get; init; }
}
