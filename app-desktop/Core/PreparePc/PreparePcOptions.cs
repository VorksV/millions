using System.Collections.Generic;
using VoltrisOptimizer.Core.PreparePc.Interfaces;

namespace VoltrisOptimizer.Core.PreparePc;

public class PreparePcOptions
{
	public PreparePcMode Mode { get; set; } = PreparePcMode.Recommended;


	public string? CustomBackupPath { get; set; }

	public bool CreateSystemRestore { get; set; } = false;


	public int TimeoutMinutes { get; set; } = 120;


	public bool AllowNetworkReset { get; set; } = false;


	public bool CleanBrowserCaches { get; set; } = true;


	public bool CleanShaderCaches { get; set; } = true;


	public bool OptimizeServices { get; set; } = true;


	public bool ApplyPowerPlan { get; set; } = true;


	public List<string> SkipSteps { get; set; } = new List<string>();

}
