using System;

namespace VoltrisOptimizer.Core.Body;

public sealed class SystemHealthState
{
	public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;


	public float CpuPercent { get; init; }

	public float CpuTemperatureCelsius { get; init; } = -1f;


	public float CpuClockMhz { get; init; }

	public int CpuCoreCount { get; init; }

	public float RamUsedPercent { get; init; }

	public long RamAvailableMb { get; init; }

	public long RamTotalMb { get; init; }

	public float GpuPercent { get; init; }

	public float GpuTemperatureCelsius { get; init; } = -1f;


	public string? GpuName { get; init; }

	public float DiskQueueLength { get; init; }

	public float DiskActiveTimePercent { get; init; }

	public bool IsOnBattery { get; init; }

	public int BatteryPercent { get; init; } = 100;


	public int ForegroundPid { get; init; }

	public string ForegroundProcessName { get; init; } = string.Empty;


	public float NetworkLatencyMs { get; init; }

	public float NetworkThroughputMbps { get; init; }

	public float ThermalThrottlingPercent { get; init; }

	public int CurrentEpp { get; init; }

	public string CurrentPowerPlan { get; init; } = string.Empty;


	public bool IsGamingMode { get; init; }

	public string? CurrentGame { get; init; }
}
