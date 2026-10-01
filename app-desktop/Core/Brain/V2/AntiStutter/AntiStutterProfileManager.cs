using System;
using System.Collections.Generic;

namespace VoltrisOptimizer.Core.Brain.V2.AntiStutter;

public static class AntiStutterProfileManager
{
	private static readonly Dictionary<UserProfile, ProfileConfiguration> _profiles;

	static AntiStutterProfileManager()
	{
		_profiles = new Dictionary<UserProfile, ProfileConfiguration>();
		Register();
	}

	public static ProfileConfiguration Get(UserProfile profile)
	{
		ProfileConfiguration value;
		return _profiles.TryGetValue(profile, out value) ? value : _profiles[UserProfile.Balanced];
	}

	public static UserProfile DetectRecommendedProfile()
	{
		SystemMetricsCache instance = SystemMetricsCache.Instance;
		HardwareSummary hardware = instance.Hardware;
		if (hardware != null && hardware.IsLaptop)
		{
			return UserProfile.NotebookThermal;
		}
		if ((hardware?.TotalRamGb ?? 0.0) >= 16.0 && Environment.ProcessorCount >= 8)
		{
			return UserProfile.GamerCompetitive;
		}
		return UserProfile.Balanced;
	}

	private static void Register()
	{
		_profiles[UserProfile.GamerCompetitive] = new ProfileConfiguration
		{
			Profile = UserProfile.GamerCompetitive,
			DisplayName = "Gamer Competitivo",
			Description = "Máximo desempenho e menor latência possível",
			MinSeverityToAct = 0.35,
			EmergencyThreshold = 0.75,
			AllowRiskyActions = true,
			AllowServiceActions = true,
			AllowPowerPlanChanges = true,
			RiskMultiplier = 0.8,
			GainMultiplier = 1.2,
			CompatibilityMultiplier = 1.0,
			PreferForegroundBoost = true,
			RestrictActionsOnBattery = true
		};
		_profiles[UserProfile.QualityVisual] = new ProfileConfiguration
		{
			Profile = UserProfile.QualityVisual,
			DisplayName = "Qualidade Visual",
			Description = "Foco em estabilidade e suavidade visual",
			MinSeverityToAct = 0.45,
			EmergencyThreshold = 0.8,
			AllowRiskyActions = false,
			AllowServiceActions = true,
			AllowPowerPlanChanges = true,
			RiskMultiplier = 1.3,
			GainMultiplier = 1.0,
			CompatibilityMultiplier = 1.1,
			PreferForegroundBoost = false,
			RestrictActionsOnBattery = true
		};
		_profiles[UserProfile.Balanced] = new ProfileConfiguration
		{
			Profile = UserProfile.Balanced,
			DisplayName = "Balanceado",
			Description = "Equilíbrio entre performance e segurança",
			MinSeverityToAct = 0.4,
			EmergencyThreshold = 0.8,
			AllowRiskyActions = false,
			AllowServiceActions = true,
			AllowPowerPlanChanges = true,
			RiskMultiplier = 1.0,
			GainMultiplier = 1.0,
			CompatibilityMultiplier = 1.0,
			PreferForegroundBoost = false,
			RestrictActionsOnBattery = true
		};
		_profiles[UserProfile.NotebookThermal] = new ProfileConfiguration
		{
			Profile = UserProfile.NotebookThermal,
			DisplayName = "Notebook (Térmico)",
			Description = "Modo conservador para notebooks",
			MinSeverityToAct = 0.5,
			EmergencyThreshold = 0.85,
			AllowRiskyActions = false,
			AllowServiceActions = false,
			AllowPowerPlanChanges = false,
			RiskMultiplier = 1.4,
			GainMultiplier = 0.9,
			CompatibilityMultiplier = 1.2,
			PreferForegroundBoost = false,
			RestrictActionsOnBattery = true
		};
	}
}
