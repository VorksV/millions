namespace VoltrisOptimizer.Core.Optimization;

public class SelectedOptimization
{
	public string Name { get; set; } = string.Empty;


	public string Priority { get; set; } = string.Empty;


	public string Impact { get; set; } = string.Empty;


	public string Reason { get; set; } = string.Empty;


	public long EstimatedTimeMs { get; set; }
}
