using System;
using System.Threading;

namespace VoltrisOptimizer.Core.Brain.V2;

public static class BrainSafetyPolicy
{
	private static int _consecutiveLoopErrors;

	public const int FallbackErrorThreshold = 5;

	public static bool AllowMemoryTrim { get; set; } = !EnvFlag("VOLTRIS_BRAIN_DISABLE_TRIM");


	public static bool AllowEppChanges { get; set; } = !EnvFlag("VOLTRIS_BRAIN_DISABLE_EPP");


	public static bool AllowPriorityBoost { get; set; } = !EnvFlag("VOLTRIS_BRAIN_DISABLE_PRIORITY");


	public static int ConsecutiveLoopErrors => Volatile.Read(ref _consecutiveLoopErrors);

	public static bool AggressiveFallbackActive => Volatile.Read(ref _consecutiveLoopErrors) >= FallbackErrorThreshold;

	public static bool ShouldAllowMemoryTrim => AllowMemoryTrim && !AggressiveFallbackActive;

	public static bool ShouldAllowEpp => AllowEppChanges && !AggressiveFallbackActive;

	public static bool ShouldAllowPriority => AllowPriorityBoost && !AggressiveFallbackActive;

	public static void RegisterLoopSuccess()
	{
		Interlocked.Exchange(ref _consecutiveLoopErrors, 0);
	}

	public static void RegisterLoopError()
	{
		Interlocked.Increment(ref _consecutiveLoopErrors);
	}

	private static bool EnvFlag(string name)
	{
		return string.Equals(Environment.GetEnvironmentVariable(name), "1", StringComparison.Ordinal) || string.Equals(Environment.GetEnvironmentVariable(name), "true", StringComparison.OrdinalIgnoreCase);
	}
}
