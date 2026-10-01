namespace VoltrisOptimizer.Core.Enterprise;

public class EnterpriseSystemLoad
{
	public double CpuUsagePercent { get; set; }

	public double MemoryUsagePercent { get; set; }

	public double GpuUsagePercent { get; set; }

	public bool IsUnderHeavyLoad { get; set; }
}
