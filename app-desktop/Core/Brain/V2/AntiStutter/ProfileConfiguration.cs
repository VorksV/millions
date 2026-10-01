namespace VoltrisOptimizer.Core.Brain.V2.AntiStutter;

public sealed class ProfileConfiguration
{
	public UserProfile Profile { get; init; }

	public string DisplayName { get; init; } = string.Empty;


	public string Description { get; init; } = string.Empty;


	public double MinSeverityToAct { get; init; }

	public double EmergencyThreshold { get; init; }

	public bool AllowRiskyActions { get; init; }

	public bool AllowServiceActions { get; init; }

	public bool AllowPowerPlanChanges { get; init; }

	public double RiskMultiplier { get; init; }

	public double GainMultiplier { get; init; }

	public double CompatibilityMultiplier { get; init; }

	public bool PreferForegroundBoost { get; init; }

	public bool RestrictActionsOnBattery { get; init; }
}
