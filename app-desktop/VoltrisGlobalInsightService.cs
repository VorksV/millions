using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services.Telemetry;

namespace VoltrisOptimizer;

public class VoltrisGlobalInsightService
{
	private readonly TelemetryService _telemetry;

	private readonly string _insightDataPath;

	public VoltrisGlobalInsightService(TelemetryService telemetry)
	{
		_telemetry = telemetry;
		_insightDataPath = AppDataPaths.GetPath("Insights/insights.json");
		AppDataPaths.EnsureDirectory(Path.GetDirectoryName(_insightDataPath));
	}

	public async Task RecordGainAsync(string category, double value, string unit)
	{
		try
		{
			List<InsightPoint> history = await LoadHistoryAsync();
			history.Add(new InsightPoint
			{
				Timestamp = DateTime.UtcNow,
				Category = category,
				Value = value,
				Unit = unit
			});
			if (history.Count > 100)
			{
				history.RemoveAt(0);
			}
			await File.WriteAllTextAsync(contents: JsonSerializer.Serialize(history, new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }), path: _insightDataPath);
		}
		catch
		{
		}
	}

	public async Task<TrialValueSummary> GenerateTrialSummaryAsync()
	{
		List<InsightPoint> history = await LoadHistoryAsync();
		object stats = await _telemetry.GenerateUsageStatisticsAsync(null, null);
		TrialValueSummary trialValueSummary = new TrialValueSummary();
		trialValueSummary.TotalOptimizations = ((dynamic)stats)?.Optimizations ?? ((object)0);
		trialValueSummary.TotalCleanedGB = history.Where((InsightPoint h) => h.Category == "Cleanup").Sum((InsightPoint h) => h.Value) / 1024.0;
		trialValueSummary.AverageLatencyImprovement = (from h in history
			where h.Category == "Latency"
			select h.Value).DefaultIfEmpty(0.0).Average();
		trialValueSummary.IntelligentDecisions = history.Count((InsightPoint h) => h.Category == "BrainDecision");
		trialValueSummary.TrialStartDate = ((history.Count > 0) ? history.Min((InsightPoint h) => h.Timestamp) : DateTime.UtcNow.AddDays(-7.0));
		return trialValueSummary;
	}

	private async Task<List<InsightPoint>> LoadHistoryAsync()
	{
		if (!File.Exists(_insightDataPath))
		{
			return new List<InsightPoint>();
		}
		try
		{
			return JsonSerializer.Deserialize<List<InsightPoint>>(await File.ReadAllTextAsync(_insightDataPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }) ?? new List<InsightPoint>();
		}
		catch
		{
			return new List<InsightPoint>();
		}
	}
}
