using System;

namespace VoltrisOptimizer.Core.Body;

public sealed class BaselineState
{
	public int OriginalEpp { get; set; }

	public Guid OriginalPowerPlanGuid { get; set; }

	public int OriginalSystemResponsiveness { get; set; }

	public uint OriginalTimerResolution { get; set; }

	public bool OriginalGamingMode { get; set; }

	public DateTime CapturedAtUtc { get; set; }

	public object? OriginalHagsMode { get; set; }

	public object? OriginalNetworkThrottling { get; set; }
}
