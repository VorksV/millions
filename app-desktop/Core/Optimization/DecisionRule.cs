using System;
using System.Collections.Generic;
using VoltrisOptimizer.Services.SystemIntelligenceProfiler;

namespace VoltrisOptimizer.Core.Optimization;

public class DecisionRule
{
	public string Name { get; set; } = string.Empty;


	public bool IsAutoRecommend { get; set; }

	public List<Func<HardwareProfile, SystemScores, OptimizationContext, bool>> Conditions { get; set; } = new List<Func<HardwareProfile, SystemScores, OptimizationContext, bool>>();


	public string Reason { get; set; } = string.Empty;


	public string Priority { get; set; } = string.Empty;


	public string Impact { get; set; } = string.Empty;

}
