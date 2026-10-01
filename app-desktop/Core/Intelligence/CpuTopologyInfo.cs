using System;

namespace VoltrisOptimizer.Core.Intelligence;

public class CpuTopologyInfo
{
	public int[] PCoreIndices { get; set; } = Array.Empty<int>();


	public int[] ECoreIndices { get; set; } = Array.Empty<int>();


	public long PCoreMask { get; set; }

	public long ECoreMask { get; set; }

	public bool HasHeterogeneousCores { get; set; }
}
