using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VoltrisOptimizer.Core.Brain.V2;

public sealed class ProfilesFile
{
	[JsonPropertyName("version")]
	public int Version { get; set; } = 2;


	[JsonPropertyName("profiles")]
	public Dictionary<string, BrainProcessProfile> Profiles { get; set; } = new Dictionary<string, BrainProcessProfile>();


	[JsonPropertyName("savedUtc")]
	public DateTime SavedUtc { get; set; } = DateTime.UtcNow;

}
