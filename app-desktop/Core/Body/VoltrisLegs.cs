using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.Brain.V2;
using VoltrisOptimizer.Core.Intelligence;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Core.Body;

public sealed class VoltrisLegs : IVoltrisLegs, IDisposable
{
	private readonly ILoggingService _logger;

	private readonly IBrainSensor _sensor;

	private readonly ITemporalPatternEngine _temporal;

	private readonly IActiveBackgroundDirector _abd;

	private OperationalContext _current = OperationalContext.Idle;

	private CancellationTokenSource? _patrolCts;

	private Task? _patrolTask;

	private OperationalContext _candidate = OperationalContext.Idle;

	private int _confirmCount;

	private string _lastProcess = string.Empty;

	private DateTime _lastThermalWarnLog = DateTime.MinValue;

	private DateTime _lastDetectContextLog = DateTime.MinValue;

	private DateTime _lastGamingTransition = DateTime.MinValue;

	private static readonly TimeSpan ContextCooldown = TimeSpan.FromSeconds(30.0);

	private OperationalContext _previousGamingContext = OperationalContext.Idle;

	private DateTime _gamingAltTabStart = DateTime.MinValue;

	private static readonly TimeSpan GamingAltTabTimeout = TimeSpan.FromSeconds(60.0);

	private static readonly DateTime _startupTime = DateTime.UtcNow;

	private static readonly HashSet<string> Games = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"cs2", "valorant", "fortnite", "apex", "overwatch", "cod", "warzone", "gta5", "lol", "dota2",
		"pubg", "minecraft", "tarkov", "rainbow6"
	};

	private static readonly HashSet<string> Streaming = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "obs64", "obs", "streamlabs", "xsplit", "vmix" };

	private static readonly HashSet<string> VideoEdit = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "premiere", "afterfx", "resolve", "vegas", "camtasia" };

	private static readonly HashSet<string> IDEs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "devenv", "vscode", "code", "windsurf", "cursor", "idea", "eclipse" };

	private static readonly HashSet<string> RenderEngines = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "blender", "handbrake", "ffmpeg", "3dsmax", "maya", "cinema4d", "keyshot", "render" };

	public TimeSpan PatrolInterval { get; set; } = TimeSpan.FromMilliseconds(2000.0);


	public int RequiredConfirmationCycles => 3;

	public OperationalContext DetectedContext => _current;

	public string LastDetectedProcess => _lastProcess;

	public int CurrentConfirmationCount => _confirmCount;

	public event EventHandler<ContextDetectedEventArgs>? OnContextDetected;

	public event EventHandler<ContextConfirmedEventArgs>? OnContextConfirmed;

	public VoltrisLegs(ILoggingService logger, IBrainSensor sensor, ITemporalPatternEngine temporal, IActiveBackgroundDirector abd)
	{
		_logger = logger ?? throw new ArgumentNullException("logger");
		_sensor = sensor ?? throw new ArgumentNullException("sensor");
		_temporal = temporal ?? throw new ArgumentNullException("temporal");
		_abd = abd ?? throw new ArgumentNullException("abd");
		_sensor.SnapshotProduced += OnSensorSnapshotProduced;
		_abd.OnForegroundContextChanged += Abd_OnForegroundContextChanged;
	}

	public Task StartAsync(CancellationToken ct = default(CancellationToken))
	{
		ILoggingService logger = _logger;
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(60, 1);
		defaultInterpolatedStringHandler.AppendLiteral("[LEGS] StartAsync (Event-driven + Patrol loop) | contexto = ");
		defaultInterpolatedStringHandler.AppendFormatted(_current);
		logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
		_patrolCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		_patrolTask = Task.Run(() => PatrolAsync(_patrolCts!.Token), _patrolCts!.Token);
		return Task.CompletedTask;
	}

	public async Task StopAsync()
	{
		_logger.LogInfo("[LEGS] StopAsync");
		try
		{
			_patrolCts?.Cancel();
		}
		catch
		{
		}
		try
		{
			if (_patrolTask != null)
			{
				await _patrolTask!.ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	public void Dispose()
	{
		try
		{
			_patrolCts?.Cancel();
		}
		catch
		{
		}
		_sensor.SnapshotProduced -= OnSensorSnapshotProduced;
		_abd.OnForegroundContextChanged -= Abd_OnForegroundContextChanged;
	}

	private async Task PatrolAsync(CancellationToken ct)
	{
		_logger.LogInfo("[LEGS] Patrol loop iniciado (intervalo = 2s)");
		while (!ct.IsCancellationRequested)
		{
			try
			{
				await Task.Delay(PatrolInterval, ct).ConfigureAwait(continueOnCapturedContext: false);
				SensorSnapshot snap = _sensor.CurrentSnapshot;
				if (snap != null)
				{
					EvaluateContext(snap);
				}
			}
			catch (OperationCanceledException)
			{
				break;
			}
			catch (Exception ex)
			{
				_logger.LogError("[LEGS] Erro no PatrolAsync: " + ex.Message);
			}
		}
		_logger.LogInfo("[LEGS] Patrol loop encerrado");
	}

	public Task<OperationalContext> ForceDetectionAsync()
	{
		SensorSnapshot currentSnapshot = _sensor.CurrentSnapshot;
		if (currentSnapshot == null)
		{
			return Task.FromResult(_current);
		}
		OperationalContext result = DetectContext(currentSnapshot);
		return Task.FromResult(result);
	}

	private void Abd_OnForegroundContextChanged(object? sender, string processName)
	{
		if (!string.IsNullOrEmpty(processName))
		{
			string text = Path.GetFileNameWithoutExtension(processName)!.ToLowerInvariant();
			if (Games.Contains(text) && _current != OperationalContext.Gaming)
			{
				_logger.LogInfo("[LEGS] Fast-path Gaming detectado via WinEventHook: " + text);
				ChangeContext(OperationalContext.Gaming, processName, 1, 0.0, 0.0, 0.0);
				return;
			}
		}
		EvaluateContext(_sensor.CurrentSnapshot);
	}

	private void OnSensorSnapshotProduced(object? sender, SensorSnapshot e)
	{
		EvaluateContext(e);
	}

	private void EvaluateContext(SensorSnapshot? snap)
	{
		if (snap != null)
		{
			OperationalContext operationalContext = DetectContext(snap);
			OperationalContext operationalContext2 = operationalContext;
			if (_current == OperationalContext.Gaming && operationalContext != OperationalContext.Gaming)
			{
				if (_gamingAltTabStart == DateTime.MinValue)
				{
					_gamingAltTabStart = DateTime.UtcNow;
				}
				if ((DateTime.UtcNow - _gamingAltTabStart) < GamingAltTabTimeout)
				{
					_logger.LogDebug("[LEGS] GamingAltTab: mantendo contexto Gaming (alt-tab < " + GamingAltTabTimeout.TotalSeconds + "s)");
					operationalContext = OperationalContext.Gaming;
				}
			}
			else
			{
				_gamingAltTabStart = DateTime.MinValue;
			}
			if (_current == OperationalContext.Gaming && operationalContext2 == OperationalContext.Gaming && operationalContext == OperationalContext.Gaming)
			{
				_lastGamingTransition = DateTime.UtcNow;
			}
			if (_current == OperationalContext.Gaming && operationalContext != OperationalContext.Gaming && (DateTime.UtcNow - _lastGamingTransition) < ContextCooldown)
			{
				double num = (ContextCooldown - (DateTime.UtcNow - _lastGamingTransition)).TotalSeconds;
				_logger.LogDebug("[LEGS] Cooldown de contexto: " + num.ToString("F1") + "s restantes — mantendo Gaming");
				operationalContext = OperationalContext.Gaming;
			}
			bool flag = operationalContext == OperationalContext.ThermalCrisis;
			if (operationalContext != _candidate)
			{
				_candidate = operationalContext;
				_confirmCount = ((!flag) ? 1 : RequiredConfirmationCycles);
				this.OnContextDetected?.Invoke(this, new ContextDetectedEventArgs
				{
					CandidateContext = operationalContext,
					CurrentContext = _current,
					CycleCount = _confirmCount,
					TriggerProcess = snap!.ForegroundProcessName,
					CpuPercent = snap!.CpuUsagePercent,
					GpuPercent = snap!.GpuUsagePercent,
					TempCelsius = snap!.CpuTemperatureC
				});
				ILoggingService logger = _logger;
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(48, 3);
				defaultInterpolatedStringHandler.AppendLiteral("[LEGS] Novo contexto candidato: ");
				defaultInterpolatedStringHandler.AppendFormatted(operationalContext);
				defaultInterpolatedStringHandler.AppendLiteral(" (confirmação ");
				defaultInterpolatedStringHandler.AppendFormatted(_confirmCount);
				defaultInterpolatedStringHandler.AppendLiteral("/");
				defaultInterpolatedStringHandler.AppendFormatted(RequiredConfirmationCycles);
				defaultInterpolatedStringHandler.AppendLiteral(")");
				logger.LogDebug(defaultInterpolatedStringHandler.ToStringAndClear());
			}
			else if (_candidate != _current)
			{
				_confirmCount++;
			}
			if (_confirmCount >= RequiredConfirmationCycles && _candidate != _current)
			{
				ChangeContext(_candidate, snap!.ForegroundProcessName, flag ? 1 : RequiredConfirmationCycles, snap!.CpuUsagePercent, snap!.GpuUsagePercent, snap!.CpuTemperatureC);
			}
		}
	}

	private void ChangeContext(OperationalContext newContext, string processName, int cycles, double cpu, double gpu, double temp)
	{
		OperationalContext current = _current;
		_current = newContext;
		_confirmCount = 0;
		_lastProcess = processName;
		if (newContext == OperationalContext.Gaming)
		{
			_lastGamingTransition = DateTime.UtcNow;
			_gamingAltTabStart = DateTime.MinValue;
		}
		ILoggingService logger = _logger;
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(72, 6);
		defaultInterpolatedStringHandler.AppendLiteral("[LEGS] ✅ CONTEXTO CONFIRMADO: ");
		defaultInterpolatedStringHandler.AppendFormatted(current);
		defaultInterpolatedStringHandler.AppendLiteral(" → ");
		defaultInterpolatedStringHandler.AppendFormatted(_current);
		defaultInterpolatedStringHandler.AppendLiteral(" | fg = ");
		defaultInterpolatedStringHandler.AppendFormatted(_lastProcess);
		defaultInterpolatedStringHandler.AppendLiteral(" | cpu = ");
		defaultInterpolatedStringHandler.AppendFormatted(cpu, "F0");
		defaultInterpolatedStringHandler.AppendLiteral("% | gpu = ");
		defaultInterpolatedStringHandler.AppendFormatted(gpu, "F0");
		defaultInterpolatedStringHandler.AppendLiteral("% | temp = ");
		defaultInterpolatedStringHandler.AppendFormatted(temp, "F0");
		defaultInterpolatedStringHandler.AppendLiteral("C");
		logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
		_temporal.RecordObservation(_current);
		this.OnContextConfirmed?.Invoke(this, new ContextConfirmedEventArgs
		{
			Context = _current,
			OldContext = current,
			TotalCycles = cycles,
			TriggerProcess = _lastProcess
		});
	}

	private OperationalContext DetectContext(SensorSnapshot snap)
	{
		string text = (snap.ForegroundProcessName ?? "").ToLowerInvariant();
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(text);
		
		// Log detect context apenas a cada 5 segundos para reduzir flood (profissional)
		if ((DateTime.Now - _lastDetectContextLog).TotalSeconds >= 5.0)
		{
			_logger?.LogDebug($"[LEGS] DetectContext: fg={text} | proc={fileNameWithoutExtension} | cpu={snap.CpuUsagePercent:F0}% | gpu={snap.GpuUsagePercent:F0}% | temp={snap.CpuTemperatureC:F0}C | ram={snap.RamUsagePercent:F0}%");
			_lastDetectContextLog = DateTime.Now;
		}
		
		if (snap.CpuTemperatureC > 88.0) {
            // Critical temperature override: ignore startup grace period if temp > 95°C
            if (snap.CpuTemperatureC > 95.0) {
                // CRISE REAL (>95°C): ignora grace period e força estado ThermalCrisis.
                // Com rate-limit de 30s — este bloco re-avalia a cada ciclo e o erro era
                // registrado a cada tick, inundando o log sem informação nova.
                DateTime utcNowCrisis = DateTime.UtcNow;
                if ((utcNowCrisis - _lastThermalWarnLog).TotalSeconds >= 30.0)
                {
                    _logger?.LogError("[LEGS] CRISE REAL — ignorando grace period!");
                    _lastThermalWarnLog = utcNowCrisis;
                }
                return OperationalContext.ThermalCrisis;
            }

            DateTime utcNow = DateTime.UtcNow;
            if ((utcNow - _lastThermalWarnLog).TotalSeconds >= 30.0)
            {
                _logger?.LogWarning($"[LEGS] CRISE TÉRMICA: {snap.CpuTemperatureC:F0}°C > 88°C" + (((utcNow - _startupTime).TotalSeconds < 120) ? " (suprimida — startup grace period de 120s)" : ""));
                _lastThermalWarnLog = utcNow;
            }
            if ((utcNow - _startupTime).TotalSeconds >= 120)
            {
                return OperationalContext.ThermalCrisis;
            }
        }
		if (SystemMetricsCache.Instance.LastInputMs > 300000)
		{
			_logger.LogDebug("[LEGS] UserAway: inatividade > 5min");
			return OperationalContext.UserAway;
		}
		if (snap.CpuUsagePercent > 85.0 || snap.RamUsagePercent > 90.0)
		{
			_logger?.LogDebug($"[LEGS] SystemStress: cpu={snap.CpuUsagePercent:F0}% ram={snap.RamUsagePercent:F0}%");
			return OperationalContext.SystemStress;
		}
		if (Games.Contains(fileNameWithoutExtension))
		{
			_logger.LogDebug("[LEGS] Gaming (lista): " + fileNameWithoutExtension);
			return OperationalContext.Gaming;
		}
		if (snap.CpuUsagePercent > 40.0 && snap.GpuUsagePercent > 60.0)
		{
			_logger?.LogDebug($"[LEGS] Gaming (heurística): cpu={snap.CpuUsagePercent:F0}% > 40% && gpu={snap.GpuUsagePercent:F0}% > 60%");
			return OperationalContext.Gaming;
		}
		if (Streaming.Contains(fileNameWithoutExtension))
		{
			_logger.LogDebug("[LEGS] Streaming: " + fileNameWithoutExtension);
			return OperationalContext.Streaming;
		}
		if ((VideoEdit.Contains(fileNameWithoutExtension) || RenderEngines.Contains(fileNameWithoutExtension)) && snap.CpuUsagePercent > 65.0)
		{
			_logger?.LogDebug($"[LEGS] Rendering: {fileNameWithoutExtension} cpu={snap.CpuUsagePercent:F0}% > 65%");
			return OperationalContext.Rendering;
		}
		if (VideoEdit.Contains(fileNameWithoutExtension))
		{
			_logger.LogDebug("[LEGS] VideoEditing: " + fileNameWithoutExtension);
			return OperationalContext.VideoEditing;
		}
		if (IDEs.Contains(fileNameWithoutExtension))
		{
			_logger.LogDebug("[LEGS] Work (IDE): " + fileNameWithoutExtension);
			return OperationalContext.Work;
		}
		if (snap.CpuUsagePercent < 5.0)
		{
			_logger?.LogDebug($"[LEGS] Idle: cpu={snap.CpuUsagePercent:F0}% < 5%");
			return OperationalContext.Idle;
		}
		_logger.LogDebug("[LEGS] Work (default): " + fileNameWithoutExtension);
		return OperationalContext.Work;
	}
}
