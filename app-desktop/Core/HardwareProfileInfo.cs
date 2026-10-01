namespace VoltrisOptimizer.Core;

public class HardwareProfileInfo
{
	public string CPUName { get; set; } = string.Empty;


	public int CPUCores { get; set; }

	public int TotalRAMGB { get; set; }

	public bool HasSSD { get; set; }

	public bool HasNVMe { get; set; }

	public bool HasDedicatedGPU { get; set; }

	public string GPUName { get; set; } = string.Empty;


	public bool IsLaptop { get; set; }

	public string Tier { get; set; } = string.Empty;


	public int PerformanceScore { get; set; }

	public int StabilityScore { get; set; }

	public int RiskScore { get; set; }

	public int OverallScore { get; set; }
}
