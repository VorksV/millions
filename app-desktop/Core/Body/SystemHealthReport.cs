using System;

namespace VoltrisOptimizer.Core.Body;

public sealed class SystemHealthReport
{
	public DateTime TimestampUtc { get; init; }

	public bool BrainLoopActive { get; init; }

	public bool SensorLoopActive { get; init; }

	public bool LegsPatrolActive { get; init; }

	public float EppCurrent { get; init; }

	public float EppExpected { get; init; }

	public bool EppMatch { get; init; }

	public double CpuTempCelsius { get; init; }

	public int QTableStatesVisited { get; init; }

	public double SessionAverageReward { get; init; }
}
