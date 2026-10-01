using System;

namespace VoltrisOptimizer.Core.Body;

public sealed class ThermalAlertEventArgs : EventArgs
{
	public double TemperatureCelsius { get; init; }

	public bool IsCritical { get; init; }
}
