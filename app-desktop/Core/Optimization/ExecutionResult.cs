using VoltrisOptimizer.Services.SystemIntelligenceProfiler;

namespace VoltrisOptimizer.Core.Optimization;

public class ExecutionResult
{
	public bool Success { get; set; }

	public string Reason { get; set; } = string.Empty;


	public ActionClassification Classification { get; set; }

	public string Impact { get; set; } = string.Empty;


	public long ExecutionTimeMs { get; set; }

	public int SuccessCount { get; set; }

	public int FailureCount { get; set; }

	public int SkippedCount { get; set; }
}
