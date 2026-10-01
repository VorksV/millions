using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.Body;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Core.Intelligence;

public sealed class TemporalPatternEngine : ITemporalPatternEngine, IAutoStartService
{
	private readonly ILoggingService _logger;

	private readonly string _storagePath;

	private readonly Dictionary<string, TemporalPattern> _patterns = new Dictionary<string, TemporalPattern>();

	private readonly object _lock = new object();

	public TemporalPatternEngine(ILoggingService logger)
	{
		_logger = logger ?? throw new ArgumentNullException("logger");
		_storagePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Voltris", "Brain", "temporal_patterns.json");
	}

	public async Task StartAsync(CancellationToken ct = default(CancellationToken))
	{
		_logger.LogInfo("[TEMPORAL] Carregando padrões temporais...");
		await LoadAsync();
	}

	public void RecordObservation(OperationalContext context)
	{
		if (context == OperationalContext.Idle && _patterns.Count == 0)
		{
			return;
		}
		DateTime now = DateTime.Now;
		DayOfWeek dayOfWeek = now.DayOfWeek;
		int hour = now.Hour;
		string text = $"{dayOfWeek}-{hour}-{context}";
		lock (_lock)
		{
			if (!_patterns.TryGetValue(text, out var value))
			{
				value = new TemporalPattern
				{
					DayOfWeek = dayOfWeek,
					Hour = hour,
					Context = context,
					Occurrences = 0
				};
				_patterns[text] = value;
				_logger.LogDebug("[TEMPORAL] Novo padrão: " + text + " (primeira observação)");
			}
			value.Occurrences++;
			value.LastUpdated = DateTime.UtcNow;
			UpdateProbabilities(dayOfWeek, hour);
			if (value.Occurrences >= 2)
			{
				_logger.LogDebug($"[TEMPORAL] Padrão {text}: {value.Occurrences} ocorrências, prob={value.Probability:P1} | threshold=2 ✓ ativo para PreWarm");
			}
		}
	}

	public double GetProbability(DayOfWeek day, int hour, OperationalContext context)
	{
		string key = $"{day}-{hour}-{context}";
		lock (_lock)
		{
			if (!_patterns.TryGetValue(key, out var value))
			{
				return 0.0;
			}
			if (value.Occurrences < 2)
			{
				return 0.0;
			}
			return value.Probability;
		}
	}

	public OperationalContext GetLikelyContext(DateTime time)
	{
		DayOfWeek day = time.DayOfWeek;
		int hour = time.Hour;
		lock (_lock)
		{
			return (from p in _patterns.Values
				where p.DayOfWeek == day && p.Hour == hour && p.Occurrences >= 2
				orderby p.Probability descending
				select p).FirstOrDefault()?.Context ?? OperationalContext.Idle;
		}
	}

	public async Task PruneAsync()
	{
		DateTime now = DateTime.UtcNow;
		int removed = 0;
		int decayed = 0;
		lock (_lock)
		{
			List<string> keys = _patterns.Keys.ToList();
			foreach (string key in keys)
			{
				TemporalPattern p2 = _patterns[key];
				double daysOld = (now - p2.LastUpdated).TotalDays;
				if (daysOld > 90.0)
				{
					_patterns.Remove(key);
					removed++;
				}
				else if (daysOld > 30.0)
				{
					p2.Occurrences = (int)((double)p2.Occurrences * 0.5);
					decayed++;
				}
				else if (daysOld > 7.0)
				{
					p2.Occurrences = (int)((double)p2.Occurrences * 0.9);
					decayed++;
				}
			}
			if (removed > 0 || decayed > 0)
			{
				IEnumerable<(DayOfWeek DayOfWeek, int Hour)> slots = _patterns.Values.Select((TemporalPattern p) => (p.DayOfWeek, p.Hour)).Distinct();
				foreach (var slot in slots)
				{
					UpdateProbabilities(slot.DayOfWeek, slot.Hour);
				}
			}
		}
		if (removed > 0 || decayed > 0)
		{
			_logger.LogInfo($"[TEMPORAL] Decay concluído: {removed} removidos, {decayed} enfraquecidos.");
			await SaveAsync();
		}
	}

	private void UpdateProbabilities(DayOfWeek day, int hour)
	{
		List<TemporalPattern> list = _patterns.Values.Where((TemporalPattern p) => p.DayOfWeek == day && p.Hour == hour).ToList();
		int num = list.Sum((TemporalPattern p) => p.Occurrences);
		if (num == 0)
		{
			return;
		}
		foreach (TemporalPattern item in list)
		{
			item.Probability = (double)item.Occurrences / (double)num;
		}
	}

	public async Task LoadAsync()
	{
		try
		{
			if (!File.Exists(_storagePath))
			{
				return;
			}
			using FileStream fs = new FileStream(_storagePath, FileMode.Open, FileAccess.Read);
			List<TemporalPattern> loaded = await JsonSerializer.DeserializeAsync<List<TemporalPattern>>(fs, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
			if (loaded == null)
			{
				return;
			}
			lock (_lock)
			{
				_patterns.Clear();
				foreach (TemporalPattern p in loaded)
				{
					_patterns[p.Key] = p;
				}
			}
			_logger.LogInfo($"[TEMPORAL] {loaded.Count} padrões temporais carregados com sucesso.");
		}
		catch (Exception ex2)
		{
			Exception ex = ex2;
			_logger.LogError("[TEMPORAL] Erro ao carregar padrões: " + ex.Message);
		}
	}

	public async Task SaveAsync()
	{
		try
		{
			List<TemporalPattern> toSave;
			lock (_lock)
			{
				toSave = _patterns.Values.ToList();
			}
			string dir = Path.GetDirectoryName(_storagePath);
			if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
			{
				Directory.CreateDirectory(dir);
			}
			using FileStream fs = new FileStream(_storagePath, FileMode.Create, FileAccess.Write);
			await JsonSerializer.SerializeAsync((Stream)fs, toSave, new JsonSerializerOptions { WriteIndented = true,
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase
			}, default(CancellationToken));
			_logger.LogInfo($"[TEMPORAL] {toSave.Count} padrões persistidos.");
		}
		catch (Exception ex2)
		{
			Exception ex = ex2;
			_logger.LogError("[TEMPORAL] Erro ao salvar padrões: " + ex.Message);
		}
	}
}
