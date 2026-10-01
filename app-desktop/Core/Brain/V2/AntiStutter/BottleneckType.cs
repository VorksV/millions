namespace VoltrisOptimizer.Core.Brain.V2.AntiStutter;

public enum BottleneckType
{
	None,
	CpuSingleCore,
	CpuMultiCore,
	GpuSaturated,
	GpuThermal,
	RamPressure,
	RamCompression,
	DiskHdd,
	DiskSaturated,
	NetworkJitter,
	ThermalThrottling,
	BackgroundProcess,
	PowerPlan,
	Unknown
}
