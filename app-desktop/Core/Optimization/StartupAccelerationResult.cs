using System;
using System.Collections.Generic;

namespace VoltrisOptimizer.Core.Optimization;

public sealed class StartupAccelerationResult
{
	public DateTime StartedAtUtc { get; init; } = DateTime.UtcNow;


	public DateTime FinishedAtUtc { get; init; } = DateTime.UtcNow;


	public List<StartupAccelerationStepResult> Steps { get; init; } = new List<StartupAccelerationStepResult>();


	public VoltriScoreSnapshot? BeforeSnapshot { get; init; }

	public VoltriScoreSnapshot? AfterSnapshot { get; init; }

	public VoltriScoreReport? ScoreReport { get; init; }
}
