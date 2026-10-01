using System.Collections.Generic;

namespace VoltrisOptimizer.Core.PreparePc;

public class PreCheckResult
{
	public bool CanProceed { get; set; }

	public bool IsAdmin { get; set; }

	public bool IsOnBattery { get; set; }

	public int BatteryPercent { get; set; }

	public double FreeSpaceGB { get; set; }

	public List<string> InstallerProcesses { get; set; } = new List<string>();


	public List<string> Errors { get; set; } = new List<string>();


	public List<string> Warnings { get; set; } = new List<string>();

}
