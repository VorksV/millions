using System;

namespace VoltrisOptimizer;

public class InsightPoint
{
	public DateTime Timestamp { get; set; }

	public string Category { get; set; } = string.Empty;


	public double Value { get; set; }

	public string Unit { get; set; } = string.Empty;

}
