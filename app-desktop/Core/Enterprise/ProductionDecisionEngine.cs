using System.Collections.Generic;
using System.Runtime.CompilerServices;
using VoltrisOptimizer.Core.SystemIntelligenceProfiler;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Adaptive;
using VoltrisOptimizer.Services.Validation;

namespace VoltrisOptimizer.Core.Enterprise;

public class ProductionDecisionEngine : IDecisionEngine
{
	private readonly AdaptiveOptimizationProfile _adaptiveProfile;

	private readonly OptimizationEffectivenessValidator _effectivenessValidator;

	private readonly ILoggingService _logger;

	public ProductionDecisionEngine(AdaptiveOptimizationProfile adaptiveProfile, OptimizationEffectivenessValidator effectivenessValidator, ILoggingService logger)
	{
		_adaptiveProfile = adaptiveProfile;
		_effectivenessValidator = effectivenessValidator;
		_logger = logger;
	}

	public ProfilerReport Evaluate(AuditData audit, UserAnswers answers)
	{
		_logger.Log(LogLevel.Info, LogCategory.General, "Production intelligent evaluation started", null, "ProductionDecisionEngine");
		List<ActionRecommendation> list = new List<ActionRecommendation>();
		VoltrisOptimizer.Services.Adaptive.SystemLoad currentLoad = new VoltrisOptimizer.Services.Adaptive.SystemLoad
		{
			CpuUsagePercent = 20.0,
			MemoryUsagePercent = 40.0
		};
		OptimizationIntensity optimizationIntensity = _adaptiveProfile.CalculateOptimalIntensity(audit.HardwareProfile, currentLoad, new UserPreference());
		ILoggingService logger = _logger;
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(40, 2);
		defaultInterpolatedStringHandler.AppendLiteral("Determined optimal intensity: CPU=");
		defaultInterpolatedStringHandler.AppendFormatted(optimizationIntensity.CpuOptimization);
		defaultInterpolatedStringHandler.AppendLiteral(", RAM=");
		defaultInterpolatedStringHandler.AppendFormatted(optimizationIntensity.MemoryOptimization);
		logger.Log(LogLevel.Debug, LogCategory.General, defaultInterpolatedStringHandler.ToStringAndClear(), null, "ProductionDecisionEngine");
		if (optimizationIntensity.CpuOptimization >= OptimizationLevel.Moderate || audit.PerfTier <= PerformanceTier.MidRange)
		{
			list.Add(new ActionRecommendation
			{
				Type = ActionType.General_Optimize,
				Name = "General System Optimization",
				Explanation = "System hardware classification and intensity analysis requires optimization for stable performance.",
				ExpectedGainScore = 70,
				Category = RecommendationCategory.Safe
			});
		}
		if (optimizationIntensity.MemoryOptimization >= OptimizationLevel.Moderate || audit.Ram.AvailableMb < 2048)
		{
			list.Add(new ActionRecommendation
			{
				Type = ActionType.SystemCleanup,
				Name = "System Memory Cleanup",
				Explanation = "Memory usage patterns and available resources suggest a cleanup will improve responsiveness.",
				ExpectedGainScore = 85,
				Category = RecommendationCategory.Safe
			});
		}
		if (audit.HardwareProfile.Storage.Type != "SSD" && audit.HardwareProfile.Storage.Type != "NVMe")
		{
			list.Add(new ActionRecommendation
			{
				Type = ActionType.Storage_Defrag,
				Name = "HDD Performance Optimization",
				Explanation = "Mechanical drive detected. Defragmentation recommended for faster boot times.",
				ExpectedGainScore = 60,
				Category = RecommendationCategory.Risky
			});
		}
		return new ProfilerReport
		{
			Audit = audit,
			Answers = answers,
			Status = "Evaluated",
			Recommendations = list
		};
	}
}
