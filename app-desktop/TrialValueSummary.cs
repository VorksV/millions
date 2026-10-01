using System;

namespace VoltrisOptimizer;

public class TrialValueSummary
{
	public int TotalOptimizations { get; set; }

	public double TotalCleanedGB { get; set; }

	public double AverageLatencyImprovement { get; set; }

	public int IntelligentDecisions { get; set; }

	public DateTime TrialStartDate { get; set; }
}
