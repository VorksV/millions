using VoltrisOptimizer.Services.SystemIntelligenceProfiler;

namespace VoltrisOptimizer.Core.Optimization;

public class SystemAnalysisResult
{
	public bool Success { get; set; }

	public HardwareProfile Profile { get; set; } = null;


	public SystemScores Scores { get; set; } = null;


	public string Error { get; set; } = string.Empty;

}
