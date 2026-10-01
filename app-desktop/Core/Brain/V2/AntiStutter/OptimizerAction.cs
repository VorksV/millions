using System;

namespace VoltrisOptimizer.Core.Brain.V2.AntiStutter;

public sealed class OptimizerAction
{
	public string Id { get; init; } = Guid.NewGuid().ToString("N").Substring(0, 8);


	public string Name { get; init; } = string.Empty;


	public string Description { get; init; } = string.Empty;


	public ActionCategory Category { get; init; }

	public ActionSafetyLevel SafetyLevel { get; init; }

	public BottleneckType[] TargetBottlenecks { get; init; } = Array.Empty<BottleneckType>();


	public bool RequiresAdmin { get; init; }

	public TimeSpan EstimatedDuration { get; init; } = TimeSpan.FromMilliseconds(100.0);


	public bool IsReversible { get; init; } = true;


	public int CooldownMs { get; init; } = 5000;


	public Func<AntiStutterSnapshot, BottleneckAnalysis, bool> CanExecute { get; init; } = (AntiStutterSnapshot _, BottleneckAnalysis _) => true;

}
