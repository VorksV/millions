using System;

namespace VoltrisOptimizer.Core.Body;

public sealed class ContextDetectedEventArgs : EventArgs
{
	public OperationalContext CandidateContext { get; init; }

	public OperationalContext CurrentContext { get; init; }

	public int CycleCount { get; init; }

	public string TriggerProcess { get; init; } = string.Empty;


	public double CpuPercent { get; init; }

	public double GpuPercent { get; init; }

	public double TempCelsius { get; init; }
}
