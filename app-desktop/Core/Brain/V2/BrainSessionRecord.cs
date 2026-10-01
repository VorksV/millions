using System;

namespace VoltrisOptimizer.Core.Brain.V2;

public class BrainSessionRecord
{
	public string SessionId { get; set; }

	public DateTime StartedUtc { get; set; }

	public DateTime EndedUtc { get; set; }

	public double TotalReward { get; set; }

	public double AvgCpuReducedPercent { get; set; }

	public int StutterEventsResolved { get; set; }

	public long RamFreedMB { get; set; }

	public string TopAction { get; set; }

	public long TotalCycles { get; set; }

	public long TotalActionsExecuted { get; set; }
}
