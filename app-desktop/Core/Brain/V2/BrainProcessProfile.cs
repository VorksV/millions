using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VoltrisOptimizer.Core.Brain.V2;

public sealed class BrainProcessProfile
{
	[JsonPropertyName("processName")]
	public string ProcessName { get; set; } = string.Empty;


	[JsonPropertyName("category")]
	public WorkloadCategory Category { get; set; }

	[JsonPropertyName("bestEpp")]
	public int BestEpp { get; set; } = 50;


	[JsonPropertyName("bestPriority")]
	public ProcessPriorityChoice BestPriority { get; set; } = ProcessPriorityChoice.Normal;


	[JsonPropertyName("avgCpuAfter")]
	public double AvgCpuAfterOptimize { get; set; }

	[JsonPropertyName("avgFpsStability")]
	public double AvgFpsStability { get; set; }

	[JsonPropertyName("successfulActions")]
	public List<string> SuccessfulActions { get; set; } = new List<string>();


	[JsonPropertyName("sessions")]
	public int Sessions { get; set; }

	[JsonPropertyName("firstSeenUtc")]
	public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;


	[JsonPropertyName("lastSeenUtc")]
	public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;


	[JsonPropertyName("totalReward")]
	public double TotalReward { get; set; }

	[JsonPropertyName("relevanceScore")]
	public double RelevanceScore { get; set; } = 1.0;

}
