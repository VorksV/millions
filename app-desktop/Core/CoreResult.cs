namespace VoltrisOptimizer.Core;

public class CoreResult
{
	public bool Success { get; set; }

	public long ExecutionTimeMs { get; set; }

	public int OptimizationsApplied { get; set; }

	public string Message { get; set; } = string.Empty;


	public string Mode { get; set; } = string.Empty;


	public HardwareProfileInfo? HardwareProfile { get; set; }
}
