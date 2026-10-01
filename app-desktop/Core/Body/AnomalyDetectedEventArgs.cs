using System;

namespace VoltrisOptimizer.Core.Body;

public sealed class AnomalyDetectedEventArgs : EventArgs
{
	public string AnomalyType { get; init; } = string.Empty;


	public string Expected { get; init; } = string.Empty;


	public string Actual { get; init; } = string.Empty;


	public bool AutoCorrected { get; init; }
}
