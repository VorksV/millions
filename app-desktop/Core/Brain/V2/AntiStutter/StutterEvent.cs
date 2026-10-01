using System;

namespace VoltrisOptimizer.Core.Brain.V2.AntiStutter;

public sealed class StutterEvent
{
	public DateTime Timestamp { get; init; }

	public double FrameTimeSpikeMs { get; init; }

	public double NormalFrameTimeMs { get; init; }

	public StutterCause DetectedCause { get; init; }
}
