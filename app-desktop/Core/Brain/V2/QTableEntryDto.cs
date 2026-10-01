using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VoltrisOptimizer.Core.Brain.V2;

public sealed class QTableEntryDto
{
	[JsonPropertyName("state")]
	public string StateKey { get; set; } = string.Empty;


	[JsonPropertyName("actions")]
	public Dictionary<string, double> ActionValues { get; set; } = new Dictionary<string, double>();


	[JsonPropertyName("visits")]
	public long Visits { get; set; }
}
