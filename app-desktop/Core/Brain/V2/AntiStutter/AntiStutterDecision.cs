using System;
using System.Collections.Generic;

namespace VoltrisOptimizer.Core.Brain.V2.AntiStutter;

public sealed class AntiStutterDecision
{
	public bool ShouldAct { get; init; }

	public IReadOnlyList<OptimizerAction> SelectedActions { get; init; } = Array.Empty<OptimizerAction>();


	public BottleneckAnalysis Analysis { get; init; } = new BottleneckAnalysis();


	public string Reasoning { get; init; } = string.Empty;


	public double ConfidenceScore { get; init; }

	public UserProfile ProfileUsed { get; init; } = UserProfile.Balanced;


	public bool IsEmergencyProtocol { get; init; }

	public DateTime DecidedAt { get; init; } = DateTime.UtcNow;

}
