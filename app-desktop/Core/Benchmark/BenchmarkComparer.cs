using System;

namespace VoltrisOptimizer.Core.Benchmark;

public sealed class BenchmarkComparer
{
	public BenchmarkComparison Compare(BenchmarkFullResult before, BenchmarkFullResult after)
	{
		return new BenchmarkComparison
		{
			CpuImprovement = CalcImprovement(before.CpuScore, after.CpuScore),
			MemoryImprovement = CalcImprovement(before.MemoryScore, after.MemoryScore),
			DiskImprovement = CalcImprovement(before.DiskScore, after.DiskScore),
			SchedulerImprovement = CalcImprovement(before.SchedulerScore, after.SchedulerScore),
			UiImprovement = CalcImprovement(before.UiScore, after.UiScore),
			OverallImprovement = CalcImprovement(before.OverallScore, after.OverallScore),
			Before = before,
			After = after
		};
	}

	private double CalcImprovement(double before, double after)
	{
		if (Math.Abs(before) < 0.001)
		{
			return 0.0;
		}
		return (after - before) / before * 100.0;
	}
}
