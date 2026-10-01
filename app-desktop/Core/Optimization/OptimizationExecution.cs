namespace VoltrisOptimizer.Core.Optimization;

public class OptimizationExecution
{
	public bool Success { get; set; }

	public string Message { get; set; } = string.Empty;


	public long ExecutionTimeMs { get; set; }
}
