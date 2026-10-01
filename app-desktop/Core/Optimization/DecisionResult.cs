using VoltrisOptimizer.Services.SystemIntelligenceProfiler;

namespace VoltrisOptimizer.Core.Optimization;

public class DecisionResult
{
	public bool ShouldApply { get; set; }

	public ActionClassification Classification { get; set; }

	public string Reason { get; set; } = string.Empty;


	public string Priority { get; set; } = string.Empty;


	public string EstimatedImpact { get; set; } = string.Empty;

}
