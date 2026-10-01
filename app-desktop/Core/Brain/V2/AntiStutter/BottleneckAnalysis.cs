using System.Collections.Generic;

namespace VoltrisOptimizer.Core.Brain.V2.AntiStutter;

public sealed class BottleneckAnalysis
{
	public bool HasBottleneck { get; init; }

	public BottleneckType PrimaryBottleneck { get; init; }

	public BottleneckType SecondaryBottleneck { get; init; }

	public double SeverityScore { get; init; }

	public List<BottleneckDetail> Details { get; init; } = new List<BottleneckDetail>();


	public bool IsThermalThrottling { get; init; }

	public string Diagnosis { get; init; } = "Sistema saudável";


	public string Recommendation { get; init; } = "Nenhuma ação necessária";

}
