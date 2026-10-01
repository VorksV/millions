using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Core.Intelligence;

[Obsolete("Use SystemMetricsCache.Instance (Fps/FpsOnePercentLow/FpsAverageFrametimeMs) instead. This class generates random fake data.")]
public class FpsMonitor : IFpsMonitor
{
	private readonly ILoggingService _logger;

	private readonly CircularBuffer<float> _fpsHistory;

	private bool _isMonitoring;

	private Task _monitorTask;

	private CancellationTokenSource _cts;

	private float _currentFps;

	private readonly Random _random = new Random();

	public FpsMonitor(ILoggingService logger)
	{
		_logger = logger;
		_fpsHistory = new CircularBuffer<float>(60);
	}

	public void StartMonitoring()
	{
		if (!_isMonitoring)
		{
			_isMonitoring = true;
			_cts = new CancellationTokenSource();
			_monitorTask = Task.Run(() => MonitorLoopAsync(_cts.Token), _cts.Token);
			_logger.LogInfo("[FPS] Monitoramento DXGI/Present de Fps iniciado.");
		}
	}

	public void StopMonitoring()
	{
		if (_isMonitoring)
		{
			_isMonitoring = false;
			_cts?.Cancel();
			_fpsHistory.Clear();
			_logger.LogInfo("[FPS] Monitoramento interrompido.");
		}
	}

	private async Task MonitorLoopAsync(CancellationToken ct)
	{
		while (!ct.IsCancellationRequested)
		{
			_currentFps = 110f + (float)(_random.NextDouble() * 30.0 - 15.0);
			_fpsHistory.Add(_currentFps);
			await Task.Delay(1000, ct);
		}
	}

	public float GetCurrentFps()
	{
		return _currentFps;
	}

	public float GetAverageFps()
	{
		if (_fpsHistory.Count == 0)
		{
			return 0f;
		}
		return _fpsHistory.GetItems().Average();
	}

	public float Get1PercentLowFps()
	{
		if (_fpsHistory.Count < 5)
		{
			return GetCurrentFps();
		}
		List<float> list = (from x in _fpsHistory.GetItems()
			orderby x
			select x).ToList();
		int index = Math.Max(0, (int)((double)list.Count * 0.01));
		return list[index];
	}

	public bool HasSignificantVariance()
	{
		if (_fpsHistory.Count < 10)
		{
			return false;
		}
		float averageFps = GetAverageFps();
		float num = Get1PercentLowFps();
		bool flag = (double)num < (double)averageFps * 0.7;
		if (flag)
		{
			ILoggingService logger = _logger;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(50, 2);
			defaultInterpolatedStringHandler.AppendLiteral("[FPS-VAR] Stuttering Detectado! Média: ");
			defaultInterpolatedStringHandler.AppendFormatted(averageFps, "F0");
			defaultInterpolatedStringHandler.AppendLiteral(" | 1% Low: ");
			defaultInterpolatedStringHandler.AppendFormatted(num, "F0");
			logger.LogWarning(defaultInterpolatedStringHandler.ToStringAndClear());
		}
		return flag;
	}
}
