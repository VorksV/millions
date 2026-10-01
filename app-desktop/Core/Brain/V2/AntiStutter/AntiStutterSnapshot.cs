using System;

namespace VoltrisOptimizer.Core.Brain.V2.AntiStutter;

public sealed class AntiStutterSnapshot
{
	public DateTime TimestampUtc { get; init; }

	public double CpuUsageTotal { get; init; }

	public double[] CpuUsagePerCore { get; init; } = Array.Empty<double>();


	public double CpuMaxCoreUsage { get; init; }

	public bool CpuSingleCoreBound => CpuMaxCoreUsage > 85.0 && CpuUsageTotal / (double)Math.Max(1, CpuUsagePerCore.Length) < 50.0;

	public double CpuTemperatureC { get; init; } = -1.0;


	public uint CpuClockMhz { get; init; }

	public bool CpuThermalThrottling { get; init; }

	public double RamUsagePercent { get; init; }

	public long RamAvailableMB { get; init; }

	public long RamTotalMB { get; init; }

	public long PageFileUsageMB { get; init; }

	public bool RamPressure => RamUsagePercent > 85.0 || PageFileUsageMB > 2048;

	public double GpuUsagePercent { get; init; }

	public double GpuTemperatureC { get; init; }

	public long GpuVramUsedMB { get; init; }

	public long GpuVramTotalMB { get; init; }

	public bool GpuSaturated => GpuUsagePercent > 95.0;

	public double DiskQueueLength { get; init; }

	public double DiskReadMBps { get; init; }

	public double DiskWriteMBps { get; init; }

	public bool DiskIsSsd { get; init; }

	public bool DiskBottleneck => !DiskIsSsd && DiskQueueLength > 2.0;

	public double NetworkLatencyMs { get; init; }

	public double NetworkJitterMs { get; init; }

	public bool NetworkUnstable => NetworkJitterMs > 50.0;

	public int ForegroundPid { get; init; }

	public string ForegroundProcessName { get; init; } = string.Empty;


	public string ForegroundWindowTitle { get; init; } = string.Empty;


	public WorkloadCategory Workload { get; init; }

	public bool IsGame => Workload == WorkloadCategory.Game;

	public double FrameTimeMs { get; init; }

	public double FrameTimeVarianceMs { get; init; }

	public double Fps => (FrameTimeMs > 0.0) ? (1000.0 / FrameTimeMs) : 0.0;

	public bool StutterDetected { get; init; }

	public Guid CurrentPowerPlan { get; init; }

	public bool IsHighPerformance => CurrentPowerPlan == new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

	public bool SysMainRunning { get; init; }

	public bool WindowsSearchRunning { get; init; }

	public bool DiagTrackRunning { get; init; }

	public bool IsOnBattery { get; init; }

	public int BatteryPercent { get; init; }
}
