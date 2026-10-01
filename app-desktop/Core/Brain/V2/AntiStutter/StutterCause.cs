namespace VoltrisOptimizer.Core.Brain.V2.AntiStutter;

public enum StutterCause
{
	Unknown,
	CpuBound,
	GpuBound,
	RamPressure,
	DiskIo,
	ThermalThrottling,
	BackgroundProcess,
	ShaderCompilation,
	Network
}
