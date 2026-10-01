using System;

namespace VoltrisOptimizer.Core.PreparePc;

public class ScheduleData
{
	public DateTime ScheduledTime { get; set; }

	public int ScheduleType { get; set; }

	public DateTime CreatedAt { get; set; }

	public bool IsNextStartup { get; set; }
}
