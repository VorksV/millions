using System;

namespace VoltrisOptimizer.Core.Benchmark;

public class BenchmarkFullResult
{
	public double CpuScore { get; set; }

	public double MemoryScore { get; set; }

	public double DiskScore { get; set; }

	public double SchedulerScore { get; set; }

	public double SchedulerLatencyUs { get; set; }

	public double UiScore { get; set; }

	public double UiLatencyMs { get; set; }

	public double OverallScore { get; set; }

	public DateTime Timestamp { get; set; } = DateTime.Now;


	public bool ThrottlingDetected { get; set; }

	public double MinCpuFreqPercent { get; set; }

	public double MaxCpuFreqPercent { get; set; }

	public string Label { get; set; } = "";

}
