namespace VoltrisOptimizer.Core.Body;

public sealed class HealthCheckResult
{
	public bool IsHealthy { get; set; }

	public string Message { get; set; } = string.Empty;


	public int ExpectedEpp { get; set; }

	public int ActualEpp { get; set; }

	public bool EppMatchesExpected { get; set; }

	public bool BrainLoopActive { get; set; }

	public bool SensorLoopActive { get; set; }

	public bool LegsPatrolActive { get; set; }
}
