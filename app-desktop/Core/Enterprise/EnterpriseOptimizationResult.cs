using System;
using System.Collections.Generic;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Hardware;

namespace VoltrisOptimizer.Core.Enterprise;

public class EnterpriseOptimizationResult
{
	public DateTime StartTime { get; set; }

	public DateTime EndTime { get; set; }

	public TimeSpan Duration { get; set; }

	public OptimizationMode Mode { get; set; }

	public bool PerformanceValidationEnabled { get; set; }

	public bool GameCompatibilityCheckEnabled { get; set; }

	public bool EffectivenessValidationEnabled { get; set; }

	public bool PerformanceDegradationDetected { get; set; }

	public DetailedHardwareProfile HardwareAnalysis { get; set; }

	public EnterpriseSystemLoad SystemLoad { get; set; }

	public List<string> RunningGames { get; set; } = new List<string>();


	public PerformanceAwareOptimizationResult ExecutionResult { get; set; }
}
