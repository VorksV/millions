using Microsoft.ML.Data;

namespace VoltrisOptimizer.Core.Intelligence;

public class SystemSpikeFeatures
{
	[LoadColumn(0)]
	public float CpuLoad { get; set; }

	[LoadColumn(1)]
	public float RamUsedGB { get; set; }

	[LoadColumn(2)]
	public float GpuLoad { get; set; }

	[LoadColumn(3)]
	public float CurrentFps { get; set; }

	[LoadColumn(4)]
	public float DiskQueueLength { get; set; }

	[LoadColumn(5)]
	[ColumnName("Label")]
	public bool SpikeOcurred { get; set; }
}
