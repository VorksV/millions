using System;
using System.Diagnostics;

namespace VoltrisOptimizer.Core.Optimization;

public sealed class VoltriScoreSnapshot
{
	public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;


	public double CpuPercent { get; init; }

	public double MemoryUsedPercent { get; init; }

	public double AvailableRamMb { get; init; }

	public float DiskQueueLength { get; init; }

	public long LastInputMs { get; init; }

	public ProcessPriorityClass CurrentProcessPriority { get; init; } = ProcessPriorityClass.Normal;

}
