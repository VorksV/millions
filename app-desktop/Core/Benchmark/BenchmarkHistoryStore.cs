using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Core.Benchmark;

public sealed class BenchmarkHistoryStore
{
	private static readonly string HistoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoltrisOptimizer", "BenchmarkHistory.json");

	private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions { WriteIndented = true,
		ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	public async Task SaveResultAsync(BenchmarkFullResult result)
	{
		List<BenchmarkFullResult> history = await LoadHistoryAsync();
		history.Add(result);
		if (history.Count > 50)
		{
			history.RemoveRange(0, history.Count - 50);
		}
		string dir = Path.GetDirectoryName(HistoryPath);
		if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
		{
			Directory.CreateDirectory(dir);
		}
		await File.WriteAllTextAsync(contents: JsonSerializer.Serialize(history, JsonOpts), path: HistoryPath);
		ILoggingService? loggingService = App.LoggingService;
		if (loggingService != null)
		{
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(57, 1);
			defaultInterpolatedStringHandler.AppendLiteral("[BenchmarkHistory] Resultado salvo — Total no histórico: ");
			defaultInterpolatedStringHandler.AppendFormatted(history.Count);
			loggingService!.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
		}
	}

	public async Task<List<BenchmarkFullResult>> LoadHistoryAsync()
	{
		if (!File.Exists(HistoryPath))
		{
			App.LoggingService?.LogInfo("[BenchmarkHistory] Arquivo de histórico não encontrado, retornando lista vazia.");
			return new List<BenchmarkFullResult>();
		}
		try
		{
			List<BenchmarkFullResult> list = JsonSerializer.Deserialize<List<BenchmarkFullResult>>(await File.ReadAllTextAsync(HistoryPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }) ?? new List<BenchmarkFullResult>();
			ILoggingService? loggingService = App.LoggingService;
			if (loggingService != null)
			{
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(52, 1);
				defaultInterpolatedStringHandler.AppendLiteral("[BenchmarkHistory] Histórico carregado — ");
				defaultInterpolatedStringHandler.AppendFormatted(list.Count);
				defaultInterpolatedStringHandler.AppendLiteral(" registros.");
				loggingService!.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
			}
			return list;
		}
		catch (Exception ex)
		{
			App.LoggingService?.LogWarning("[BenchmarkHistory] Erro ao carregar histórico: " + ex.Message);
			return new List<BenchmarkFullResult>();
		}
	}
}
