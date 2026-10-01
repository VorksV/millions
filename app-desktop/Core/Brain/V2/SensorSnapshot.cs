using System;

namespace VoltrisOptimizer.Core.Brain.V2;

public sealed class SensorSnapshot
{
	public DateTime TimestampUtc { get; init; }

	public double CpuUsagePercent { get; init; }

	public double CpuTemperatureC { get; init; }

	public uint CpuClockMhz { get; init; }

	public double RamUsagePercent { get; init; }

	public long AvailableRam { get; init; }

	public long RamTotalMB { get; init; }

	public double GpuUsagePercent { get; init; }

	public double GpuTemperatureC { get; init; }

	public int ForegroundPid { get; init; }

	public string ForegroundProcessName { get; init; } = string.Empty;


	public WorkloadCategory Workload { get; init; }

	public double FrameTimeVarianceMs { get; init; }

	public bool StutterDetected { get; init; }

	public bool IsOnBattery { get; init; }

	public double CurrentFps { get; init; }

	public double FpsVariance { get; init; }

	public double FpsPercentile1Low { get; init; }
}
