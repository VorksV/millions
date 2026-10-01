namespace VoltrisOptimizer.Core.Brain.V2;

public class BrainConfidenceScore
{
	public double Score { get; set; }

	public string PrimaryReason { get; set; } = string.Empty;


	public bool IsCritical { get; set; }
}
