using System.Collections.Concurrent;

namespace VoltrisOptimizer.Core.Brain.V2.AntiStutter;

public sealed class OptimizationStats
{
	public int TotalActionsExecuted;

	public int SuccessfulOptimizations;

	public int FailedOptimizations;

	public int RevertedActions;

	public double AverageImprovementPercent;

	public ConcurrentDictionary<string, int> ActionSuccessCount = new ConcurrentDictionary<string, int>();
}
