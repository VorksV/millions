using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Core.Brain.V2;

public sealed class BrainContextMemory
{
	private readonly ILoggingService _logger;

	private readonly string _path;

	private readonly ConcurrentDictionary<string, BrainProcessProfile> _profiles = new ConcurrentDictionary<string, BrainProcessProfile>(StringComparer.OrdinalIgnoreCase);

	private const int MAX_PROFILES = 500;

	public int Count => _profiles.Count;

	public BrainContextMemory(ILoggingService logger)
	{
		_logger = logger ?? throw new ArgumentNullException("logger");
		string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Voltris", "Brain");
		try
		{
			Directory.CreateDirectory(text);
		}
		catch (Exception ex)
		{
			_logger?.LogError($"[{nameof(BrainContextMemory)}] {ex.Message}", ex);
		}
		_path = Path.Combine(text, "profiles.json");
	}

	public void Observe(BrainStateKey state, SensorSnapshot snap)
	{
		SensorSnapshot snap2 = snap;
		if (string.IsNullOrEmpty(snap2.ForegroundProcessName))
		{
			return;
		}
		BrainProcessProfile orAdd = _profiles.GetOrAdd(snap2.ForegroundProcessName, (string name) => new BrainProcessProfile
		{
			ProcessName = name,
			Category = snap2.Workload
		});
		orAdd.LastSeenUtc = DateTime.UtcNow;
		if (_profiles.Count <= 500)
		{
			return;
		}
		List<string> list = (from kv in (from kv in _profiles
				orderby kv.Value.RelevanceScore, kv.Value.LastSeenUtc
				select kv).Take(_profiles.Count - 500 + 50)
			select kv.Key).ToList();
		foreach (string item in list)
		{
			_profiles.TryRemove(item, out var _);
		}
		ILoggingService logger = _logger;
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(56, 3);
		defaultInterpolatedStringHandler.AppendLiteral("[MEMORY] Eviction: ");
		defaultInterpolatedStringHandler.AppendFormatted(list.Count);
		defaultInterpolatedStringHandler.AppendLiteral(" perfis removidos (cap=");
		defaultInterpolatedStringHandler.AppendFormatted(500);
		defaultInterpolatedStringHandler.AppendLiteral(") | restantes=");
		defaultInterpolatedStringHandler.AppendFormatted(_profiles.Count);
		logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
	}

	public BrainProcessProfile? GetProfile(string processName)
	{
		if (string.IsNullOrWhiteSpace(processName))
		{
			return null;
		}
		BrainProcessProfile value;
		return _profiles.TryGetValue(processName, out value) ? value : null;
	}

	public void RecordAction(string processName, WorkloadCategory category, BrainActionV2 action, double reward, double cpuAfter)
	{
		string processName2 = processName;
		if (string.IsNullOrWhiteSpace(processName2))
		{
			_logger.LogDebug("[MEMORY] RecordAction ignorado: processName vazio");
			return;
		}
		ILoggingService logger = _logger;
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(64, 4);
		defaultInterpolatedStringHandler.AppendLiteral("[MEMORY] RecordAction: process=");
		defaultInterpolatedStringHandler.AppendFormatted(processName2);
		defaultInterpolatedStringHandler.AppendLiteral(" | action=");
		defaultInterpolatedStringHandler.AppendFormatted(action.ActionId);
		defaultInterpolatedStringHandler.AppendLiteral(" | reward=");
		defaultInterpolatedStringHandler.AppendFormatted(reward, "F2");
		defaultInterpolatedStringHandler.AppendLiteral(" | cpuAfter=");
		defaultInterpolatedStringHandler.AppendFormatted(cpuAfter, "F1");
		defaultInterpolatedStringHandler.AppendLiteral("%");
		logger.LogDebug(defaultInterpolatedStringHandler.ToStringAndClear());
		BrainProcessProfile orAdd = _profiles.GetOrAdd(processName2, (string _) => new BrainProcessProfile
		{
			ProcessName = processName2,
			Category = category
		});
		orAdd.LastSeenUtc = DateTime.UtcNow;
		orAdd.Category = category;
		orAdd.TotalReward += reward;
		orAdd.Sessions++;
		int num = Math.Max(1, orAdd.Sessions);
		orAdd.AvgCpuAfterOptimize = (orAdd.AvgCpuAfterOptimize * (double)num + cpuAfter) / (double)(num + 1);
		if (reward >= 1.0)
		{
			if (!orAdd.SuccessfulActions.Contains(action.ActionId))
			{
				orAdd.SuccessfulActions.Add(action.ActionId);
			}
			orAdd.RelevanceScore = Math.Min(2.0, orAdd.RelevanceScore + 0.1);
			ILoggingService logger2 = _logger;
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(54, 3);
			defaultInterpolatedStringHandler.AppendLiteral("[MEMORY] RelevanceScore incrementado para ");
			defaultInterpolatedStringHandler.AppendFormatted(orAdd.ProcessName);
			defaultInterpolatedStringHandler.AppendLiteral(": ");
			defaultInterpolatedStringHandler.AppendFormatted(orAdd.RelevanceScore, "F2");
			defaultInterpolatedStringHandler.AppendLiteral(" (reward=");
			defaultInterpolatedStringHandler.AppendFormatted(reward, "F1");
			defaultInterpolatedStringHandler.AppendLiteral(")");
			logger2.LogDebug(defaultInterpolatedStringHandler.ToStringAndClear());
			if (action.Kind == BrainActionKind.SetEpp)
			{
				orAdd.BestEpp = action.Param;
			}
			if (action.Kind == BrainActionKind.SetForegroundPriority)
			{
				orAdd.BestPriority = (ProcessPriorityChoice)action.Param;
			}
		}
		else if (reward <= -1.0)
		{
			orAdd.SuccessfulActions.Remove(action.ActionId);
		}
	}

	public void IncrementSession(string processName)
	{
		if (_profiles.TryGetValue(processName, out var value))
		{
			value.Sessions++;
		}
	}

	public IReadOnlyCollection<BrainProcessProfile> All()
	{
		return _profiles.Values.ToList();
	}

	public async Task PruneAsync()
	{
		DateTime now = DateTime.UtcNow;
		int removed = 0;
		int reduced = 0;
		List<string> toRemove = new List<string>();
		foreach (KeyValuePair<string, BrainProcessProfile> kv in _profiles)
		{
			BrainProcessProfile p = kv.Value;
			double daysOld = (now - p.LastSeenUtc).TotalDays;
			_ = p.RelevanceScore;
			if (daysOld > 90.0)
			{
				toRemove.Add(kv.Key);
				removed++;
			}
			else if (daysOld > 30.0)
			{
				p.RelevanceScore *= 0.5;
				reduced++;
			}
			else if (daysOld > 7.0)
			{
				p.RelevanceScore *= 0.9;
				reduced++;
			}
		}
		foreach (string key in toRemove)
		{
			_profiles.TryRemove(key, out var _);
		}
		if (removed > 0 || reduced > 0)
		{
			ILoggingService logger = _logger;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(65, 3);
			defaultInterpolatedStringHandler.AppendLiteral("[MEMORY] Decay aplicado: ");
			defaultInterpolatedStringHandler.AppendFormatted(removed);
			defaultInterpolatedStringHandler.AppendLiteral(" removidos | ");
			defaultInterpolatedStringHandler.AppendFormatted(reduced);
			defaultInterpolatedStringHandler.AppendLiteral(" reduzidos | ");
			defaultInterpolatedStringHandler.AppendFormatted(_profiles.Count);
			defaultInterpolatedStringHandler.AppendLiteral(" perfis ativos");
			logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
			await SaveAsync();
		}
	}

	public async Task LoadAsync()
	{
		Stopwatch sw = Stopwatch.StartNew();
		long fileSize = 0L;
		try
		{
			if (!File.Exists(_path))
			{
				_logger.LogInfo("[MEMORY] Profiles não existe, começando do zero.");
				return;
			}
			fileSize = new FileInfo(_path).Length;
			ILoggingService logger = _logger;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(72, 2);
			defaultInterpolatedStringHandler.AppendLiteral("[MEMORY][LOAD] Iniciando carregamento de profiles.json (");
			defaultInterpolatedStringHandler.AppendFormatted(fileSize);
			defaultInterpolatedStringHandler.AppendLiteral(" bytes, Thread=");
			defaultInterpolatedStringHandler.AppendFormatted(Thread.CurrentThread.ManagedThreadId);
			defaultInterpolatedStringHandler.AppendLiteral(")");
			logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
			using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(10.0));
			using FileStream fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
			ProfilesFile dto = await JsonSerializer.DeserializeAsync<ProfilesFile>((Stream)fs, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }, cts.Token).ConfigureAwait(continueOnCapturedContext: false);
			if (dto == null)
			{
				_logger.LogWarning("[MEMORY][LOAD] Deserialização retornou null — arquivo pode estar vazio ou corrompido.");
				return;
			}
			int removedCount = 0;
			DateTime threshold = DateTime.UtcNow.AddDays(-90.0);
			foreach (KeyValuePair<string, BrainProcessProfile> kv in dto.Profiles)
			{
				if (kv.Value.LastSeenUtc < threshold)
				{
					removedCount++;
				}
				else
				{
					_profiles[kv.Key] = kv.Value;
				}
			}
			ILoggingService logger2 = _logger;
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(70, 3);
			defaultInterpolatedStringHandler.AppendLiteral("[MEMORY][LOAD] ");
			defaultInterpolatedStringHandler.AppendFormatted(_profiles.Count);
			defaultInterpolatedStringHandler.AppendLiteral(" perfis carregados. ");
			defaultInterpolatedStringHandler.AppendFormatted(removedCount);
			defaultInterpolatedStringHandler.AppendLiteral(" velhos (>90d) removidos. Tempo: ");
			defaultInterpolatedStringHandler.AppendFormatted(sw.ElapsedMilliseconds);
			defaultInterpolatedStringHandler.AppendLiteral("ms");
			logger2.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
		}
		catch (OperationCanceledException)
		{
			ILoggingService logger3 = _logger;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(114, 1);
			defaultInterpolatedStringHandler.AppendLiteral("[MEMORY][LOAD] TIMEOUT após 10s ao carregar profiles.json (");
			defaultInterpolatedStringHandler.AppendFormatted(fileSize);
			defaultInterpolatedStringHandler.AppendLiteral(" bytes). Arquivo pode estar corrompido ou muito grande.");
			logger3.LogError(defaultInterpolatedStringHandler.ToStringAndClear());
		}
		catch (Exception ex3)
		{
			Exception ex = ex3;
			ILoggingService logger4 = _logger;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(53, 3);
			defaultInterpolatedStringHandler.AppendLiteral("[MEMORY][LOAD] Falha ao carregar profiles: ");
			defaultInterpolatedStringHandler.AppendFormatted(ex.GetType().Name);
			defaultInterpolatedStringHandler.AppendLiteral(": ");
			defaultInterpolatedStringHandler.AppendFormatted(ex.Message);
			defaultInterpolatedStringHandler.AppendLiteral("\nStack: ");
			defaultInterpolatedStringHandler.AppendFormatted(ex.StackTrace);
			logger4.LogError(defaultInterpolatedStringHandler.ToStringAndClear(), ex);
		}
		finally
		{
			sw.Stop();
		}
	}

	public async Task SaveAsync()
	{
		Stopwatch sw = Stopwatch.StartNew();
		try
		{
			ProfilesFile dto = new ProfilesFile
			{
				SavedUtc = DateTime.UtcNow,
				Profiles = _profiles.ToDictionary<KeyValuePair<string, BrainProcessProfile>, string, BrainProcessProfile>((KeyValuePair<string, BrainProcessProfile> p) => p.Key, (KeyValuePair<string, BrainProcessProfile> p) => p.Value, StringComparer.OrdinalIgnoreCase)
			};
			string tmp = _path + ".tmp";
			using (FileStream fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
			{
				await JsonSerializer.SerializeAsync((Stream)fs, dto, new JsonSerializerOptions {  
					WriteIndented = false,
					ReferenceHandler = ReferenceHandler.IgnoreCycles,
					MaxDepth = 32,
					PropertyNamingPolicy = JsonNamingPolicy.CamelCase
				}, default(CancellationToken)).ConfigureAwait(continueOnCapturedContext: false);
			}
			File.Move(tmp, _path, overwrite: true);
			ILoggingService logger = _logger;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(35, 2);
			defaultInterpolatedStringHandler.AppendLiteral("[MEMORY][SAVE] ");
			defaultInterpolatedStringHandler.AppendFormatted(dto.Profiles.Count);
			defaultInterpolatedStringHandler.AppendLiteral(" perfis salvos em ");
			defaultInterpolatedStringHandler.AppendFormatted(sw.ElapsedMilliseconds);
			defaultInterpolatedStringHandler.AppendLiteral("ms");
			logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
		}
		catch (Exception ex2)
		{
			Exception ex = ex2;
			ILoggingService logger2 = _logger;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(51, 3);
			defaultInterpolatedStringHandler.AppendLiteral("[MEMORY][SAVE] Falha ao salvar profiles: ");
			defaultInterpolatedStringHandler.AppendFormatted(ex.GetType().Name);
			defaultInterpolatedStringHandler.AppendLiteral(": ");
			defaultInterpolatedStringHandler.AppendFormatted(ex.Message);
			defaultInterpolatedStringHandler.AppendLiteral("\nStack: ");
			defaultInterpolatedStringHandler.AppendFormatted(ex.StackTrace);
			logger2.LogError(defaultInterpolatedStringHandler.ToStringAndClear(), ex);
		}
		finally
		{
			sw.Stop();
		}
	}
}
