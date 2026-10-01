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
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Core.Brain.V2;

public sealed class BrainSessionStats
{
	private readonly ILoggingService _logger;

	private readonly string _path;

	private readonly DateTime _startUtc = DateTime.UtcNow;

	private double _totalReward;

	private long _totalCycles;

	private long _totalActionsExecuted;

	private int _stutterEventsResolved;

	private int _stutterEventsRegistered;

	private long _ramFreedTotalMB;

	private double _cpuReductionSum;

	private int _cpuReductionSamples;

	private int _gamingCycles;

	private int _workCycles;

	private readonly Dictionary<string, (double sumReward, int count)> _actionRewards = new Dictionary<string, (double, int)>();

	private static readonly int DecisionIntervalSeconds = 10;

	public string SessionId { get; } = Guid.NewGuid().ToString("N");


	public double TotalReward => _totalReward;

	public long TotalCycles => _totalCycles;

	public long TotalActionsExecuted => _totalActionsExecuted;

	public int StutterEventsResolved => _stutterEventsResolved;

	public int StutterEventsRegistered => _stutterEventsRegistered;

	public long RamFreedMB => _ramFreedTotalMB;

	public double CPUUsageReducedPercent => (_cpuReductionSamples > 0) ? (_cpuReductionSum / (double)_cpuReductionSamples) : 0.0;

	public int GamingMinutes => _gamingCycles * DecisionIntervalSeconds / 60;

	public int WorkMinutes => _workCycles * DecisionIntervalSeconds / 60;

	public string TopActionOfSession
	{
		get
		{
			if (_actionRewards.Count == 0)
			{
				return "n/a";
			}
			(string, double) tuple = (from kv in _actionRewards
				select (kv.Key, (kv.Value.count > 0) ? (kv.Value.sumReward / (double)kv.Value.count) : 0.0) into x
				orderby x.Item2 descending
				select x).First();
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(9, 2);
			defaultInterpolatedStringHandler.AppendFormatted(tuple.Item1);
			defaultInterpolatedStringHandler.AppendLiteral(" (avg = ");
			defaultInterpolatedStringHandler.AppendFormatted(tuple.Item2, "F2");
			defaultInterpolatedStringHandler.AppendLiteral(")");
			return defaultInterpolatedStringHandler.ToStringAndClear();
		}
	}

	public BrainSessionStats(ILoggingService logger)
	{
		_logger = logger ?? throw new ArgumentNullException("logger");
		string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Voltris", "Brain");
		try
		{
			Directory.CreateDirectory(text);
		}
		catch (Exception ex)
		{
			_logger?.LogError($"[{nameof(BrainSessionStats)}] {ex.Message}", ex);
		}
		_path = Path.Combine(text, "history.json");
	}

	public void RecordCycle(OperationalContext context)
	{
		_logger.LogEntry(nameof(RecordCycle), ("context", context));
		_totalCycles++;
		switch (context)
		{
		case OperationalContext.Gaming:
			_gamingCycles++;
			break;
		case OperationalContext.Work:
			_workCycles++;
			break;
		}
	}

	public void RecordAction(BrainActionV2 action, double reward, double cpuBefore, double cpuAfter, long ramFreedMB)
	{
		_logger.LogEntry(nameof(RecordAction), ("action", action.ActionId), ("reward", reward));
		_totalReward += reward;
		_totalActionsExecuted++;
		if (ramFreedMB > 0)
		{
			_ramFreedTotalMB += ramFreedMB;
		}
		double num = cpuBefore - cpuAfter;
		if (Math.Abs(num) > 0.01)
		{
			_cpuReductionSum += num;
			_cpuReductionSamples++;
		}
		if (!_actionRewards.TryGetValue(action.ActionId, out var value))
		{
			value = (0.0, 0);
		}
		_actionRewards[action.ActionId] = (value.Item1 + reward, value.Item2 + 1);
	}

	public void RecordStutterResolved()
	{
		_logger.LogEntry(nameof(RecordStutterResolved));
		_stutterEventsResolved++;
		_logger.LogEvent("StutterResolved", $"Total resolved: {_stutterEventsResolved}");
	}

	public void RegisterStutter()
	{
		_logger.LogEntry(nameof(RegisterStutter));
		_stutterEventsRegistered++;
		_logger.LogEvent("Stutter", $"Total registered: {_stutterEventsRegistered}");
	}

	public BrainSessionRecord BuildRecord()
	{
		return new BrainSessionRecord
		{
			SessionId = SessionId,
			StartedUtc = _startUtc,
			EndedUtc = DateTime.UtcNow,
			TotalReward = _totalReward,
			AvgCpuReducedPercent = CPUUsageReducedPercent,
			StutterEventsResolved = _stutterEventsResolved,
			RamFreedMB = _ramFreedTotalMB,
			TopAction = TopActionOfSession,
			TotalCycles = _totalCycles,
			TotalActionsExecuted = _totalActionsExecuted
		};
	}

	public async Task PersistAsync()
	{
		try
		{
			HistoryFile file = new HistoryFile();
			if (File.Exists(_path))
			{
				try
				{
					using FileStream rs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
					HistoryFile loaded = await JsonSerializer.DeserializeAsync<HistoryFile>(rs, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }).ConfigureAwait(continueOnCapturedContext: false);
					if (loaded != null)
					{
						file = loaded;
					}
				}
				catch (Exception ex)
				{
					_logger?.LogError($"[{nameof(BrainSessionStats)}] {ex.Message}", ex);
				}
			}
			file.Sessions.Add(BuildRecord());
			if (file.Sessions.Count > 200)
			{
				file.Sessions = file.Sessions.Skip(file.Sessions.Count - 200).ToList();
			}
			string tmp = _path + ".tmp";
			using (FileStream fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
			{
				await JsonSerializer.SerializeAsync((Stream)fs, file, new JsonSerializerOptions { WriteIndented = true,
					PropertyNamingPolicy = JsonNamingPolicy.CamelCase
				}, default(CancellationToken)).ConfigureAwait(continueOnCapturedContext: false);
			}
			File.Move(tmp, _path, overwrite: true);
		_logger.LogEvent("SessionSummary", $"CPU-{CPUUsageReducedPercent:F1}% Stutters-{_stutterEventsResolved} RAM-{_ramFreedTotalMB}MB Reward-{_totalReward:F2}");
		ILoggingService logger = _logger;
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(101, 5);
		defaultInterpolatedStringHandler.AppendLiteral("[STATS] Resumo da sessão: CPU - ");
		defaultInterpolatedStringHandler.AppendFormatted(CPUUsageReducedPercent, "F1");
		defaultInterpolatedStringHandler.AppendLiteral("% | Stutter resolvidos: ");
		defaultInterpolatedStringHandler.AppendFormatted(_stutterEventsResolved);
		defaultInterpolatedStringHandler.AppendLiteral(" | RAM liberada: ");
		defaultInterpolatedStringHandler.AppendFormatted(_ramFreedTotalMB);
		defaultInterpolatedStringHandler.AppendLiteral(" MB | Reward total: ");
		defaultInterpolatedStringHandler.AppendFormatted(_totalReward, "F2");
		defaultInterpolatedStringHandler.AppendLiteral(" | Top: ");
		defaultInterpolatedStringHandler.AppendFormatted(TopActionOfSession);
		logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
		}
		catch (Exception ex2)
		{
			Exception ex = ex2;
			_logger.LogError("[STATS] Falha ao persistir history: " + ex.Message, ex);
		}
	}
}
