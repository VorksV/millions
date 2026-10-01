using VoltrisOptimizer.Services.SystemIntelligenceProfiler;

namespace VoltrisOptimizer.Core.Optimization;

public class PipelineResult
{
	public bool Success { get; set; }

	public string PipelineId { get; set; } = string.Empty;


	public long TotalTimeMs { get; set; }

	public int AppliedOptimizations { get; set; }

	public int FailedOptimizations { get; set; }

	public int SkippedOptimizations { get; set; }

	public HardwareProfile HardwareProfile { get; set; } = null;


	public OptimizationMode Mode { get; set; }

	public string Details { get; set; } = string.Empty;


	public string Error { get; set; } = string.Empty;


	public string Phase { get; set; } = string.Empty;

}
