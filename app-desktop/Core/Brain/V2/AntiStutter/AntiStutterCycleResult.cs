using System;
using System.Collections.Generic;

namespace VoltrisOptimizer.Core.Brain.V2.AntiStutter;

public sealed class AntiStutterCycleResult
{
	public bool Executed { get; init; }

	public DateTime Timestamp { get; init; } = DateTime.UtcNow;


	public AntiStutterDecision Decision { get; init; } = new AntiStutterDecision();


	public List<AntiStutterActionResult> ActionResults { get; init; } = new List<AntiStutterActionResult>();


	public TimeSpan ExecutionTime { get; init; }

	public bool IsGamerModeActive { get; init; }

	public string CycleId { get; init; } = Guid.NewGuid().ToString("N").Substring(0, 8);

}
