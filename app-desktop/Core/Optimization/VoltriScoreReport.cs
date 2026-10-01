using System;

namespace VoltrisOptimizer.Core.Optimization;

public sealed class VoltriScoreReport
{
	public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;


	public double EstimatedGain { get; init; }

	public double RealGain { get; init; }

	public VoltriImpactLevel ImpactLevel { get; init; } = VoltriImpactLevel.Low;


	public double CpuReductionPercent { get; init; }

	public double AvailableRamGainMb { get; init; }

	public double InputLatencyImprovementMs { get; init; }

	public string Summary { get; init; } = string.Empty;

}
