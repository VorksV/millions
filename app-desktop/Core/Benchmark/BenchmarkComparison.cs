namespace VoltrisOptimizer.Core.Benchmark;

public class BenchmarkComparison
{
	public double CpuImprovement { get; set; }

	public double MemoryImprovement { get; set; }

	public double DiskImprovement { get; set; }

	public double SchedulerImprovement { get; set; }

	public double UiImprovement { get; set; }

	public double OverallImprovement { get; set; }

	public BenchmarkFullResult Before { get; set; } = null;


	public BenchmarkFullResult After { get; set; } = null;

}
