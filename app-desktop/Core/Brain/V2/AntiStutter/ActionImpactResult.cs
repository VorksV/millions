using System;

namespace VoltrisOptimizer.Core.Brain.V2.AntiStutter;

public sealed class ActionImpactResult
{
	public string ActionId { get; init; } = string.Empty;


	public string ActionName { get; init; } = string.Empty;


	public DateTime ExecutedAt { get; init; }

	public DateTime MeasuredAt { get; init; } = DateTime.UtcNow;


	public double BeforeSeverity { get; init; }

	public double AfterSeverity { get; init; }

	public double ImprovementPercent { get; init; }

	public bool ImpactVerified { get; init; }

	public bool ShouldRevert { get; init; }

	public BottleneckType TargetBottleneck { get; init; }

	public string Reasoning { get; init; } = string.Empty;

}
