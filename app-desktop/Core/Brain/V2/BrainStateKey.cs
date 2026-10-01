using System;
using System.Runtime.CompilerServices;

namespace VoltrisOptimizer.Core.Brain.V2;

public readonly struct BrainStateKey : IEquatable<BrainStateKey>
{
	public byte CpuBucket { get; init; }

	public byte ThermalBucket { get; init; }

	public byte WorkloadBucket { get; init; }

	public byte RamBucket { get; init; }

	public byte ContextBucket { get; init; }

	public BrainStateKey(byte cpuBucket = 0, byte tempBucket = 0, WorkloadCategory workload = WorkloadCategory.Idle, byte ramBucket = 0, byte contextBucket = 0)
	{
		CpuBucket = cpuBucket;
		ThermalBucket = tempBucket;
		WorkloadBucket = (byte)workload;
		RamBucket = ramBucket;
		ContextBucket = contextBucket;
	}

	public string CanonicalKey
	{
		get
		{
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(4, 5);
			defaultInterpolatedStringHandler.AppendFormatted(CpuBucket);
			defaultInterpolatedStringHandler.AppendLiteral("-");
			defaultInterpolatedStringHandler.AppendFormatted(ThermalBucket);
			defaultInterpolatedStringHandler.AppendLiteral("-");
			defaultInterpolatedStringHandler.AppendFormatted(WorkloadBucket);
			defaultInterpolatedStringHandler.AppendLiteral("-");
			defaultInterpolatedStringHandler.AppendFormatted(RamBucket);
			defaultInterpolatedStringHandler.AppendLiteral("-");
			defaultInterpolatedStringHandler.AppendFormatted(ContextBucket);
			return defaultInterpolatedStringHandler.ToStringAndClear();
		}
	}

	public static BrainStateKey FromSnapshot(SensorSnapshot s, byte contextBucket = 0)
	{
		byte cpuBucket = (byte)((!(s.CpuUsagePercent < 20.0)) ? ((s.CpuUsagePercent < 50.0) ? 1 : ((s.CpuUsagePercent < 75.0) ? 2 : 3)) : 0);
		byte thermalBucket = (byte)((s.CpuTemperatureC < 0.0) ? 1 : ((!(s.CpuTemperatureC < 60.0)) ? ((s.CpuTemperatureC < 80.0) ? 1 : 2) : 0));
		byte ramBucket = (byte)((!(s.RamUsagePercent < 60.0)) ? ((s.RamUsagePercent < 80.0) ? 1 : 2) : 0);
		BrainStateKey result = new BrainStateKey
		{
			CpuBucket = cpuBucket,
			ThermalBucket = thermalBucket,
			WorkloadBucket = (byte)s.Workload,
			RamBucket = ramBucket,
			ContextBucket = contextBucket
		};
		return result;
	}

	public override string ToString()
	{
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(28, 5);
		defaultInterpolatedStringHandler.AppendLiteral("cpu=");
		defaultInterpolatedStringHandler.AppendFormatted(CpuBucket);
		defaultInterpolatedStringHandler.AppendLiteral(", temp=");
		defaultInterpolatedStringHandler.AppendFormatted(ThermalBucket);
		defaultInterpolatedStringHandler.AppendLiteral(", wl=");
		defaultInterpolatedStringHandler.AppendFormatted(WorkloadBucket);
		defaultInterpolatedStringHandler.AppendLiteral(", ram=");
		defaultInterpolatedStringHandler.AppendFormatted(RamBucket);
		defaultInterpolatedStringHandler.AppendLiteral(", ctx=");
		defaultInterpolatedStringHandler.AppendFormatted(ContextBucket);
		return defaultInterpolatedStringHandler.ToStringAndClear();
	}

	public bool Equals(BrainStateKey other)
	{
		return CpuBucket == other.CpuBucket && ThermalBucket == other.ThermalBucket && WorkloadBucket == other.WorkloadBucket && RamBucket == other.RamBucket && ContextBucket == other.ContextBucket;
	}

	public override bool Equals(object? obj)
	{
		return obj is BrainStateKey other && Equals(other);
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(CpuBucket, ThermalBucket, WorkloadBucket, RamBucket, ContextBucket);
	}
}
