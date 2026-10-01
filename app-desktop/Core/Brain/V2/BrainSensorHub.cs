using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Gamer.Interfaces;
using VoltrisOptimizer.Utils;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Core.Brain.V2;

public sealed class BrainSensorHub : IBrainSensor, IDisposable
{
	private struct SYSTEM_POWER_STATUS
	{
		public byte ACLineStatus;

		public byte BatteryFlag;

		public byte BatteryLifePercent;

		public byte SystemStatusFlag;

		public int BatteryLifeTime;

		public int BatteryFullLifeTime;
	}

	private readonly ILoggingService _logger;

	private int _isStarted;

	private long _totalRamMB;

	private int _stutterScore;

	private double _lastCpuSampleForSpike;

	private bool _thermalUnavailableLogged;

	private readonly List<SafePerformanceCounter> _thermalCounters = new List<SafePerformanceCounter>();

	private CancellationTokenSource? _cts;

	private Task? _loop;

	private Func<double>? _fpsSource;

	private readonly Queue<double> _fpsHistory = new Queue<double>();

	private const int FPS_HISTORY_SIZE = 30;

	private int _lastPid;

	private string _lastProcessName = "";

	private DateTime _lastProcessCheck = DateTime.MinValue;

	private static readonly HashSet<string> KnownGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"cs2", "csgo", "valorant", "valorant-win64-shipping", "rainbowsix", "r6", "fortniteclient-win64-shipping", "league of legends", "leagueclient", "dota2",
		"wow", "wowclassic", "overwatch", "apex_legends", "rdr2", "gta5", "gtav", "minecraft", "javaw", "rocketleague",
		"dayz", "pubg", "tslgame", "starcraft", "warzone", "modernwarfare", "cod", "halo", "destiny2", "eldenring",
		"cyberpunk2077", "witcher3", "battlefield", "bf1", "bf2042", "rust", "tarkov", "escapefromtarkov", "fc24", "fifa",
		"main", "l2", "l2.bin"
	};

	private static readonly HashSet<string> KnownBrowsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "chrome", "firefox", "msedge", "brave", "opera", "vivaldi", "iexplore", "safari" };

	private static readonly HashSet<string> KnownIDEs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"devenv", "code", "rider", "idea64", "pycharm64", "webstorm64", "phpstorm64", "clion64", "windsurf", "cursor",
		"sublime_text", "notepad++", "atom"
	};

	private static readonly HashSet<string> KnownVideo = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"vlc", "mpv", "mpc-hc64", "mpc-hc", "mpc-be64", "potplayer", "potplayermini64", "wmplayer", "netflix", "primevideo",
		"obs64", "streamlabs obs"
	};

	private static readonly HashSet<string> KnownOffice = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "winword", "excel", "powerpnt", "outlook", "onenote", "soffice", "soffice.bin" };

	public SensorSnapshot? CurrentSnapshot { get; private set; }

	public event EventHandler<SensorSnapshot>? SnapshotProduced;

	public void SetFpsSource(Func<double> fpsSource)
	{
		_fpsSource = fpsSource;
		_logger.LogInfo("[SENSOR] FPS source injetado no SensorHub");
	}

	public BrainSensorHub(ILoggingService logger)
	{
		_logger = logger ?? throw new ArgumentNullException("logger");
	}

	public Task StartAsync(CancellationToken ct)
	{
		_logger.LogEntry(nameof(StartAsync));
		if (Interlocked.CompareExchange(ref _isStarted, 1, 0) != 0)
			return Task.CompletedTask;
		_cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		_totalRamMB = ReadTotalRamMB();

		_logger.LogInfo($"[SENSOR] BrainSensorHub iniciado | RAM total: {_totalRamMB} MB | Dados via SystemMetricsCache (centralizado)");
		if (_fpsSource == null)
		{
			_logger.LogInfo("[SENSOR] Conectando FPS real (ETW -> SystemMetricsCache) ao Brain via SetFpsSource. Recompensa de FPS do Brain será avaliada a partir de agora.");
			SetFpsSource(() => SystemMetricsCache.Instance.Fps);
		}
		BrainObservabilityHub.Publish(BrainEventSeverity.Success, "BrainSensorHub", "StateCollection", "Coletor de sensores iniciado (cache centralizado)");
		_loop = Task.Run(() => SensorLoopAsync(_cts!.Token), _cts!.Token);
		return Task.CompletedTask;
	}

	public async Task StopAsync()
	{
		_logger.LogEntry(nameof(StopAsync));
		try
		{
			_cts?.Cancel();
		}
		catch (Exception ex)
		{
			App.LoggingService?.LogError($"[{nameof(BrainSensorHub)}] {ex.Message}", ex);
		}
		try
		{
			if (_loop != null)
			{
				await _loop!.ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		catch (OperationCanceledException)
		{
		}
		DisposeCounters();
		_logger.LogInfo("[SENSOR] BrainSensorHub parado.");
		BrainObservabilityHub.Publish(BrainEventSeverity.Info, "BrainSensorHub", "StateCollection", "Coletor de sensores parado");
	}

	public void Dispose()
	{
		try
		{
			_cts?.Cancel();
		}
		catch (Exception ex)
		{
			App.LoggingService?.LogError($"[{nameof(BrainSensorHub)}] {ex.Message}", ex);
		}
		DisposeCounters();
		_cts?.Dispose();
		_isStarted = 0;
	}

	private async Task SensorLoopAsync(CancellationToken ct)
	{
		int iter = 0;
		await Task.Delay(200, ct).ConfigureAwait(continueOnCapturedContext: false);
		while (!ct.IsCancellationRequested)
		{
			iter++;
			_logger.LogLoop("SensorLoop", iter);
			Stopwatch sw = Stopwatch.StartNew();
			try
			{
				SensorSnapshot snap = (CurrentSnapshot = CollectSnapshot());
				this.SnapshotProduced?.Invoke(this, snap);
				if (snap.CpuUsagePercent > 70.0 || snap.StutterDetected)
				{
					ILoggingService logger = _logger;
					DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(57, 8);
					defaultInterpolatedStringHandler.AppendLiteral("[SENSOR] CPU: ");
					defaultInterpolatedStringHandler.AppendFormatted(snap.CpuUsagePercent, "F1");
					defaultInterpolatedStringHandler.AppendLiteral("% TEMP: ");
					defaultInterpolatedStringHandler.AppendFormatted((snap.CpuTemperatureC >= 0.0) ? (snap.CpuTemperatureC.ToString("F0") + "C") : "n/a");
					defaultInterpolatedStringHandler.AppendLiteral(" ");
					defaultInterpolatedStringHandler.AppendLiteral("RAM: ");
					defaultInterpolatedStringHandler.AppendFormatted(snap.RamUsagePercent, "F0");
					defaultInterpolatedStringHandler.AppendLiteral("% GPU: ");
					defaultInterpolatedStringHandler.AppendFormatted(snap.GpuUsagePercent, "F0");
					defaultInterpolatedStringHandler.AppendLiteral("% FG: ");
					defaultInterpolatedStringHandler.AppendFormatted(snap.ForegroundProcessName);
					defaultInterpolatedStringHandler.AppendLiteral(" (");
					defaultInterpolatedStringHandler.AppendFormatted(snap.Workload);
					defaultInterpolatedStringHandler.AppendLiteral(") ");
					defaultInterpolatedStringHandler.AppendLiteral("FPS: ");
					defaultInterpolatedStringHandler.AppendFormatted(snap.CurrentFps, "F1");
					defaultInterpolatedStringHandler.AppendLiteral(" STUT: ");
					defaultInterpolatedStringHandler.AppendFormatted(snap.StutterDetected ? "YES" : "no");
					logger.LogDebug(defaultInterpolatedStringHandler.ToStringAndClear(), "BrainSensorHub");
				}
			}
			catch (Exception ex)
			{
				_logger.LogError("[SENSOR] Falha ao coletar snapshot: " + ex.Message, ex);
				BrainObservabilityHub.Publish(BrainEventSeverity.Error, "BrainSensorHub", "StateCollection", "Falha ao coletar estado do sistema: " + ex.Message);
			}
			sw.Stop();
			int baseInterval = 3000;
			if (CurrentSnapshot != null && CurrentSnapshot!.Workload == WorkloadCategory.Game)
			{
				baseInterval = 2000;
			}
			else if (CurrentSnapshot != null && CurrentSnapshot!.CpuUsagePercent > 50.0)
			{
				baseInterval = 2000;
			}
			int delayMs = Math.Max(100, (int)(baseInterval - sw.ElapsedMilliseconds));
			_logger.LogTimer("SensorInterval", delayMs);
			try
			{
				await Task.Delay(delayMs, ct).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (OperationCanceledException)
			{
				break;
			}
		}
	}

	private SensorSnapshot CollectSnapshot()
	{
		DateTime utcNow = DateTime.UtcNow;
		_logger.LogValue("CollectSnapshot.start", utcNow.ToString("O"));
		SystemMetricsCache instance = SystemMetricsCache.Instance;
		double cpuPercent = instance.CpuPercent;
		double memoryUsedPercent = instance.MemoryUsedPercent;
		double val = ((_totalRamMB > 0) ? ((double)_totalRamMB * (1.0 - memoryUsedPercent / 100.0)) : 0.0);
		double num = Math.Max(0.0, val);
		double num3 = instance.GpuUsagePercent;
		int currentPid = ForegroundWindowTracker.Instance.CurrentPid;
		string text = _lastProcessName;
		if (currentPid != _lastPid)
		{
			_lastPid = currentPid;
			try
			{
				if (currentPid > 4)
				{
					using Process process = Process.GetProcessById(currentPid);
					_lastProcessName = process.ProcessName;
					text = _lastProcessName;
				}
			}
			catch (ArgumentException)
			{
				text = "";
			}
			catch (InvalidOperationException)
			{
				text = "";
			}
		}
		WorkloadCategory current2 = ClassifyWorkload(text, cpuPercent, num3);
		current2 = ApplyBackgroundGameWorkloadHint(current2);
		bool flag = _lastCpuSampleForSpike > 1.0 && cpuPercent - _lastCpuSampleForSpike >= 28.0;
		_lastCpuSampleForSpike = cpuPercent;
		bool flag2 = instance.LastInputMs <= 350;
		bool flag3 = instance.DiskQueueLength >= 2.5f;
		bool flag4 = memoryUsedPercent >= 85.0 && num >= 0.0 && num <= 800.0;
		bool flag5 = cpuPercent >= 90.0 && current2 == WorkloadCategory.Game;
		bool stutterDetected = false;
		if (current2 == WorkloadCategory.Game && flag2 && (flag3 || (flag5 && flag4) || flag))
		{
			_stutterScore = Math.Min(10, _stutterScore + 2);
		}
		else
		{
			_stutterScore = Math.Max(0, _stutterScore - 1);
		}
		if (_stutterScore >= 4)
		{
			stutterDetected = true;
			ILoggingService logger = _logger;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(66, 5);
			defaultInterpolatedStringHandler.AppendLiteral("[STUTTER] Sinais reais: input = ");
			defaultInterpolatedStringHandler.AppendFormatted(instance.LastInputMs);
			defaultInterpolatedStringHandler.AppendLiteral(" ms diskQ = ");
			defaultInterpolatedStringHandler.AppendFormatted(instance.DiskQueueLength, "F1");
			defaultInterpolatedStringHandler.AppendLiteral(" cpu = ");
			defaultInterpolatedStringHandler.AppendFormatted(cpuPercent, "F0");
			defaultInterpolatedStringHandler.AppendLiteral("% ram = ");
			defaultInterpolatedStringHandler.AppendFormatted(memoryUsedPercent, "F0");
			defaultInterpolatedStringHandler.AppendLiteral("% fg = ");
			defaultInterpolatedStringHandler.AppendFormatted(text);
			logger.LogWarning(defaultInterpolatedStringHandler.ToStringAndClear());
		}
		double num4 = 0.0;
		double fpsVariance = 0.0;
		double fpsPercentile1Low = 0.0;
		try
		{
			if (_fpsSource != null)
			{
				num4 = _fpsSource!();
				if (num4 > 0.0)
				{
					_fpsHistory.Enqueue(num4);
					while (_fpsHistory.Count > 30)
					{
						_fpsHistory.Dequeue();
					}
					if (_fpsHistory.Count >= 5)
					{
						double[] array = _fpsHistory.ToArray();
						double avg = array.Average();
						fpsVariance = array.Sum((double s) => (s - avg) * (s - avg)) / (double)array.Length;
						int count = Math.Max(1, array.Length / 100);
						fpsPercentile1Low = array.OrderBy((double s) => s).Take(count).Average();
					}
				}
			}
		}
		catch (Exception fpsEx)
		{
			_logger.LogDebug($"[SENSOR] FPS source exception: {fpsEx.GetType().Name}: {fpsEx.Message}");
		}
		_logger.LogValue("CpuPercent", cpuPercent);
		_logger.LogValue("RamUsedPercent", memoryUsedPercent);
		_logger.LogValue("GpuPercent", num3);
		_logger.LogValue("ForegroundProcess", text);
		_logger.LogValue("Workload", current2);
		_logger.LogValue("StutterDetected", stutterDetected);
		_logger.LogValue("CurrentFps", num4);
		_logger.LogValue("CpuTemp", instance.CpuTemperature);
		return new SensorSnapshot
		{
			TimestampUtc = utcNow,
			CpuUsagePercent = cpuPercent,
			CpuTemperatureC = instance.CpuTemperature,
			CpuClockMhz = (uint)instance.CpuClockMhz,
			AvailableRam = (long)num,
			RamTotalMB = _totalRamMB,
			RamUsagePercent = memoryUsedPercent,
			GpuUsagePercent = num3,
			GpuTemperatureC = instance.GpuTemperature,
			ForegroundPid = currentPid,
			ForegroundProcessName = text,
			Workload = current2,
			FrameTimeVarianceMs = (_fpsHistory.Count >= 2) ? CalculateFrameTimeVariance(_fpsHistory) : 0.0,
			StutterDetected = stutterDetected,
			IsOnBattery = IsOnBattery(),
			CurrentFps = num4,
			FpsVariance = fpsVariance,
			FpsPercentile1Low = fpsPercentile1Low
		};
	}

	private static double CalculateFrameTimeVariance(Queue<double> fpsHistory)
	{
		try
		{
			int takeCount = Math.Min(10, fpsHistory.Count);
			double[] recentFps = fpsHistory.ToArray();
			double[] frametimes = recentFps.Skip(Math.Max(0, recentFps.Length - takeCount)).Take(takeCount).Select(fps => 1000.0 / fps).ToArray();
			if (frametimes.Length < 2)
			{
				return 0.0;
			}
			double avg = frametimes.Average();
			return frametimes.Sum(ft => (ft - avg) * (ft - avg)) / (double)frametimes.Length;
		}
		catch
		{
			return 0.0;
		}
	}

	private void InitializeCounters()
	{
		// Obsoleto: todos os dados de hardware vêm do SystemMetricsCache (cache centralizado).
		// Mantido como no-op para compatibilidade.
		_logger.LogInfo("[SENSOR] InitializeCounters: todos os sensores agora vêm do SystemMetricsCache (centralizado).");
	}

	private void DisposeCounters()
	{
		// No-op: todas as leituras vêm do SystemMetricsCache centralizado
	}

	private static long ReadTotalRamMB()
	{
		try
		{
			using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem");
			using ManagementObjectCollection managementObjectCollection = managementObjectSearcher.Get();
			using ManagementObjectCollection.ManagementObjectEnumerator managementObjectEnumerator = managementObjectCollection.GetEnumerator();
			if (managementObjectEnumerator.MoveNext())
			{
				ManagementObject managementObject = (ManagementObject)managementObjectEnumerator.Current;
				using (managementObject)
				{
					long num = Convert.ToInt64(managementObject["TotalVisibleMemorySize"]);
					return num / 1024;
				}
			}
		}
		catch (Exception ex)
		{
			App.LoggingService?.LogError($"[{nameof(BrainSensorHub)}] {ex.Message}", ex);
		}
		return 0L;
	}

	private (double tempC, uint mhz) ReadCpuTempAndClock()
	{
		double num = -1.0;
		if (App.ThermalMonitorService != null)
		{
			double cpuTemperature = App.ThermalMonitorService!.GetCpuTemperature();
			if (cpuTemperature > 0.0 && !double.IsNaN(cpuTemperature))
			{
				num = cpuTemperature;
			}
		}
		if (num < 0.0)
		{
			num = TryReadLibreHardwareMonitorCpuTemp();
		}
		if (num < 0.0)
		{
			num = TryReadAcpiThermalZoneTemp();
		}
		if (num < 0.0 && !_thermalUnavailableLogged)
		{
			_thermalUnavailableLogged = true;
			_logger.LogWarning("[SENSOR - THERMAL] Temperatura indisponível neste hardware, decisões térmicas desabilitadas");
		}
		uint item = 0u;
		try
		{
			using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("SELECT CurrentClockSpeed FROM Win32_Processor");
			using ManagementObjectCollection managementObjectCollection = managementObjectSearcher.Get();
			using ManagementObjectCollection.ManagementObjectEnumerator managementObjectEnumerator = managementObjectCollection.GetEnumerator();
			if (managementObjectEnumerator.MoveNext())
			{
				ManagementObject managementObject = (ManagementObject)managementObjectEnumerator.Current;
				using (managementObject)
				{
					item = Convert.ToUInt32(managementObject["CurrentClockSpeed"]);
				}
			}
		}
		catch (Exception ex)
		{
			App.LoggingService?.LogError($"[{nameof(BrainSensorHub)}] {ex.Message}", ex);
		}
		return (num, item);
	}

	private static double TryReadLibreHardwareMonitorCpuTemp()
	{
		try
		{
			using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("root\\LibreHardwareMonitor", "SELECT * FROM Sensor WHERE SensorType = 'Temperature'");
			using ManagementObjectCollection managementObjectCollection = managementObjectSearcher.Get();
			foreach (ManagementObject item in managementObjectCollection)
			{
				using (item)
				{
					string text = item["Name"]?.ToString() ?? "";
					object obj = item["Value"];
					if (text.IndexOf("CPU", StringComparison.OrdinalIgnoreCase) >= 0 && obj != null)
					{
						double num = Convert.ToDouble(obj);
						if (num > 0.0 && num < 130.0)
						{
							return num;
						}
					}
				}
			}
		}
		catch (Exception ex)
		{
			App.LoggingService?.LogError($"[{nameof(BrainSensorHub)}] {ex.Message}", ex);
		}
		return -1.0;
	}

	private static double TryReadAcpiThermalZoneTemp()
	{
		try
		{
			using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("root\\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
			using ManagementObjectCollection managementObjectCollection = managementObjectSearcher.Get();
			foreach (ManagementObject item in managementObjectCollection)
			{
				using (item)
				{
					double num = Convert.ToDouble(item["CurrentTemperature"]);
					double num2 = num / 10.0 - 273.15;
					if (num2 > 0.0 && num2 < 130.0)
					{
						return num2;
					}
				}
			}
		}
		catch (Exception ex)
		{
			App.LoggingService?.LogError($"[{nameof(BrainSensorHub)}] {ex.Message}", ex);
		}
		return -1.0;
	}

	private double ReadGpuTempWmi()
	{
		if (App.ThermalMonitorService != null)
		{
			double gpuTemperature = App.ThermalMonitorService!.GetGpuTemperature();
			if (gpuTemperature > 0.0 && !double.IsNaN(gpuTemperature))
			{
				return gpuTemperature;
			}
		}
		try
		{
			using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("root\\OpenHardwareMonitor", "SELECT * FROM Sensor WHERE SensorType = 'Temperature'");
			using ManagementObjectCollection managementObjectCollection = managementObjectSearcher.Get();
			foreach (ManagementObject item in managementObjectCollection)
			{
				using (item)
				{
					string text = item["Name"]?.ToString() ?? "";
					if (text.IndexOf("GPU", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						return Convert.ToDouble(item["Value"]);
					}
				}
			}
		}
		catch (Exception ex)
		{
			App.LoggingService?.LogError($"[{nameof(BrainSensorHub)}] {ex.Message}", ex);
		}
		return 0.0;
	}

	private (int pid, string name) ReadForegroundProcess()
	{
		try
		{
			int currentPid = ForegroundWindowTracker.Instance.CurrentPid;
			if (currentPid <= 0)
			{
				return (0, "");
			}
			if (currentPid == _lastPid && (DateTime.Now - _lastProcessCheck).TotalSeconds < 3.0)
			{
				return (_lastPid, _lastProcessName);
			}
			_lastPid = currentPid;
			_lastProcessCheck = DateTime.Now;
			using Process process = Process.GetProcessById(currentPid);
			_lastProcessName = process.ProcessName;
			return (_lastPid, _lastProcessName);
		}
		catch (ArgumentException)
		{
			return (0, "");
		}
		catch (InvalidOperationException)
		{
			return (0, "");
		}
	}

	private static WorkloadCategory ClassifyWorkload(string fgName, double cpu, double gpu)
	{
		if (string.IsNullOrWhiteSpace(fgName))
		{
			return WorkloadCategory.Idle;
		}
		if (KnownGames.Contains(fgName))
		{
			return WorkloadCategory.Game;
		}
		if (KnownBrowsers.Contains(fgName))
		{
			return WorkloadCategory.Browser;
		}
		if (KnownIDEs.Contains(fgName) || KnownOffice.Contains(fgName))
		{
			return WorkloadCategory.Work;
		}
		if (KnownVideo.Contains(fgName))
		{
			return WorkloadCategory.Video;
		}
		if (gpu > 40.0 && cpu > 25.0)
		{
			return WorkloadCategory.Game;
		}
		if (cpu < 5.0 && gpu < 5.0)
		{
			return WorkloadCategory.Idle;
		}
		return WorkloadCategory.Work;
	}

	private static WorkloadCategory ApplyBackgroundGameWorkloadHint(WorkloadCategory current)
	{
		if (current == WorkloadCategory.Game)
		{
			return current;
		}
		try
		{
			IGameDetector service = ServiceLocator.GetService<IGameDetector>();
			if (service != null && service.HasActiveRunningGameSession)
			{
				return WorkloadCategory.Game;
			}
		}
		catch (Exception ex)
		{
			App.LoggingService?.LogError($"[{nameof(BrainSensorHub)}] {ex.Message}", ex);
		}
		return current;
	}

	[DllImport("kernel32.dll")]
	private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS s);

	private static bool IsOnBattery()
	{
		try
		{
			SYSTEM_POWER_STATUS s;
			return GetSystemPowerStatus(out s) && s.ACLineStatus == 0;
		}
		catch
		{
			return false;
		}
	}
}
