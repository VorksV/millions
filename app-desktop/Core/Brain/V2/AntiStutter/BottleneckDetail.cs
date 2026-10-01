namespace VoltrisOptimizer.Core.Brain.V2.AntiStutter;

public sealed class BottleneckDetail
{
	public BottleneckType Type { get; init; }

	public double Severity { get; init; }

	public string Metric { get; init; } = string.Empty;


	public double Value { get; init; }

	public string Description { get; init; } = string.Empty;

}
