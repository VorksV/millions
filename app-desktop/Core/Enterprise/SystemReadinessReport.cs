using System.Collections.Generic;
using VoltrisOptimizer.Services.Hardware;

namespace VoltrisOptimizer.Core.Enterprise;

public class SystemReadinessReport
{
	public bool ReadyForOptimization { get; set; }

	public string ErrorMessage { get; set; }

	public DetailedHardwareProfile HardwareAnalysis { get; set; }

	public bool HardwareReady { get; set; }

	public List<string> RunningGames { get; set; } = new List<string>();


	public bool GamingSessionActive { get; set; }

	public EnterpriseSystemLoad SystemLoad { get; set; }

	public bool SystemUnderHeavyLoad { get; set; }
}
