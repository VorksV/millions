using System;

namespace VoltrisOptimizer.Core.Body;

public sealed class HealthCheckEventArgs : EventArgs
{
	public HealthCheckResult Result { get; init; } = null;


	public TimeSpan Uptime { get; init; }
}
