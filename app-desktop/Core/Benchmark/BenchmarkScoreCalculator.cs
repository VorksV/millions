namespace VoltrisOptimizer.Core.Benchmark;

public sealed class BenchmarkScoreCalculator
{
	public double Calculate(double cpuScore, double memoryScore, double diskScore, double schedulerScore, double uiScore)
	{
		return cpuScore * 0.35 + memoryScore * 0.2 + diskScore * 0.25 + schedulerScore * 0.1 + uiScore * 0.1;
	}
}
