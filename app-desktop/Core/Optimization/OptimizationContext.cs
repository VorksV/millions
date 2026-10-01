namespace VoltrisOptimizer.Core.Optimization;

public class OptimizationContext
{
	public bool IsGamerMode { get; set; }

	public bool IsBatteryPowered { get; set; }

	public bool IsThermalThrottling { get; set; }

	public string UserIntent { get; set; } = "performance";

}
