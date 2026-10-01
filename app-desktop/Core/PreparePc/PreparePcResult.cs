using System;
using System.Collections.Generic;
using System.Linq;
using VoltrisOptimizer.Core.PreparePc.Interfaces;

namespace VoltrisOptimizer.Core.PreparePc;

public class PreparePcResult
{
	public bool Success { get; set; }

	public PreparePcMode Mode { get; set; }

	public DateTime StartTime { get; set; }

	public DateTime EndTime { get; set; }

	public TimeSpan TotalDuration => EndTime - StartTime;

	public List<StepResult> StepResults { get; set; } = new List<StepResult>();


	public string BackupFolderPath { get; set; } = string.Empty;


	public string ReportPath { get; set; } = string.Empty;


	public int SuccessfulSteps => StepResults.Count((StepResult r) => r.Success);

	public int FailedSteps => StepResults.Count((StepResult r) => !r.Success && r.Status != StepStatus.Skipped);

	public int SkippedSteps => StepResults.Count((StepResult r) => r.Status == StepStatus.Skipped);

	public bool RequiresReboot => StepResults.Any((StepResult r) => r.RequiresReboot);

	public string[] AllLogs => StepResults.SelectMany((StepResult r) => r.Logs).ToArray();
}
