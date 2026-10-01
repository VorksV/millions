using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VoltrisOptimizer.Core.Brain.V2;

public sealed class QTableFile
{
	[JsonPropertyName("version")]
	public int Version { get; set; } = 2;


	[JsonPropertyName("entries")]
	public List<QTableEntryDto> Entries { get; set; } = new List<QTableEntryDto>();


	[JsonPropertyName("savedUtc")]
	public DateTime SavedUtc { get; set; } = DateTime.UtcNow;


	[JsonPropertyName("epsilon")]
	public double Epsilon { get; set; } = 0.3;


	[JsonPropertyName("episodes")]
	public long Episodes { get; set; }
}
