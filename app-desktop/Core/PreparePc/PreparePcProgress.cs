using System;
using System.Collections.Generic;
using VoltrisOptimizer.Core.PreparePc.Interfaces;

namespace VoltrisOptimizer.Core.PreparePc;

public class PreparePcProgress
{
	public int CurrentStepIndex { get; set; }

	public int TotalSteps { get; set; }

	public string CurrentStepName { get; set; } = string.Empty;


	public int OverallPercent { get; set; }

	public int StepPercent { get; set; }

	public string CurrentAction { get; set; } = string.Empty;


	public StepStatus CurrentStatus { get; set; }

	public List<string> Logs { get; set; } = new List<string>();


	public TimeSpan ElapsedTime { get; set; }

	public TimeSpan EstimatedRemaining { get; set; }
}
