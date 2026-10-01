using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using VoltrisOptimizer.Services.Hardware;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.Thermal;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Utils;

namespace VoltrisOptimizer.Core;

public sealed class SystemMetricsCache
{
	private struct MEMORYSTATUSEX
	{
		public uint dwLength;

		public uint dwMemoryLoad;

		public ulong ullTotalPhys;

		public ulong ullAvailPhys;

		public ulong ullTotalPageFile;

		public ulong ullAvailPageFile;

		public ulong ullTotalVirtual;

		public ulong ullAvailVirtual;

		public ulong ullAvailExtendedVirtual;
	}

	private struct LASTINPUTINFO
	{
		public uint cbSize;

		public uint dwTime;
	}

	public static readonly SystemMetricsCache Instance = new SystemMetricsCache();

	private long _prevIdle;

	private long _prevKernel;

	private long _prevUser;

	private SafePerformanceCounter? _cpuUtilityCounter;

	private readonly object _updateLock = new object();

	private bool _started;

	private int _currentIntervalMs = (int)MetricsUpdateSpeed.Normal;

	private Timer? _internalTimer;

	private long _updateTickCount;

	private long _updateErrorCount;

	private DateTime _lastLogHeartbeat = DateTime.MinValue;

	// ── Category-based timing ──
	private DateTime _lastHighFreq = DateTime.MinValue;

	private DateTime _lastMedFreq = DateTime.MinValue;

	private DateTime _lastLowFreq = DateTime.MinValue;

	private DateTime _lastForcePollUtc = DateTime.MinValue;

	private bool _forcePollInProgress;

	/// <summary>
	/// Última temperatura lida de fonte REAL. Começa em <see cref="double.NaN"/>, e
	/// não em 50.0: esse default era um número inventado que sobrevivia enquanto
	/// nenhuma fonte respondia, sendo depois lido como se fosse medido.
	/// </summary>
	private double _lastAcpiTempCache = double.NaN;

	private DateTime _lastAcpiUpdateUtc = DateTime.MinValue;

	/// <summary>Marca do CPU em uso, lida uma vez, para estimar TDP/TjMax.</summary>
	private string _lastCpuBrand = "";

	/// <summary>Ultima origem registrada no log de temperatura, para logar
	/// apenas na TRANSICAO e nao repetir a cada tique de 2s.</summary>
	private string? _ultimaOrigemTempRegistrada;

	/// <summary>Idem para a GPU.</summary>
	private string? _ultimaOrigemTempGpuRegistrada;

	/// <summary>
	/// Origem da temperatura vigente ("ACPI/WMI", "ThermalZoneCounter" ou
	/// "LibreHardwareMonitor"), ou null se nenhuma fonte respondeu. Permite à UI
	/// dizer DE ONDE veio o número, em vez de apresentá-lo sem procedência.
	/// </summary>
	public string? LastThermalSource { get; private set; }

	/// <summary>
	/// Intervalo entre tentativas das fontes de temperatura. O contador de zona
	/// térmica faz uma chamada de alto nível ao kernel, então 10 s evita polling
	/// caro sem perder utilidade: temperatura de CPU muda devagar nessa escala.
	/// </summary>
	private const int _thermalSourceRetrySeconds = 10;

	/// <summary>Espelho de <see cref="LastThermalSource"/> para uso interno do cache.</summary>
	private string? _lastThermalSource
	{
		get => LastThermalSource;
		set => LastThermalSource = value;
	}

	private DateTime _lastGpuLog = DateTime.MinValue;


	private const int HighFreqMsIdle = 2000;

	private const int HighFreqMsGaming = 500;

	private volatile bool _highPerformanceMode;

	private int HighFreqMs => _highPerformanceMode ? HighFreqMsGaming : HighFreqMsIdle;

	private const int MedFreqMs = 1000;

	private const int LowFreqMs = 5000;

	private readonly Dictionary<string, SafePerformanceCounter> _activeGpuCounters = new Dictionary<string, SafePerformanceCounter>();

	private readonly object _gpuCacheLock = new Object();
        // Track sustained high GPU usage
        private DateTime? _gpuHighUsageStart;
        private const double GpuHighUsageThreshold = 90.0;
        private const int GpuHighUsageDurationSec = 5;

		private GpuD3DKMTQuery? _gpuD3DKMT;

	private DateTime _lastGpuInstanceUpdate = DateTime.MinValue;

	private string[]? _gpuInstances;

	private readonly Dictionary<string, SafePerformanceCounter> _individualDiskCounters = new Dictionary<string, SafePerformanceCounter>();

	private readonly object _diskCacheLock = new object();

	private DateTime _lastDiskInstanceUpdate = DateTime.MinValue;

	private static Dictionary<string, string>? _cachedDriveToTypeMap;

	private TimeSpan _prevVoltrisCpuTime;

	private DateTime _prevVoltrisTime = DateTime.MinValue;
	private Process? _selfProcess;

	private readonly Queue<double> _cpuHistory = new Queue<double>();

	public double CpuPercent { get; private set; }

	public double MemoryUsedPercent { get; private set; }

	public double AvailableRamMb { get; private set; }

	public double DiskUsagePercent { get; private set; }

	public ObservableCollection<DiskMetricInfo> Disks { get; } = new ObservableCollection<DiskMetricInfo>();


	public double GpuUsagePercent { get; private set; }

	public float DiskQueueLength { get; private set; }

	public double CpuTemperature { get; private set; }

	public double GpuTemperature { get; private set; }

	/// <summary>
	/// Verdadeiro quando <see cref="CpuTemperature"/> e' uma ESTIMATIVA, nao uma
	/// medicao de sensor. DEVE ser respeitado por tudo que decide sobre calor:
	/// throttling, plano de energia e alertas. Ver GlobalThermalMonitorService.
	/// </summary>
	public bool IsCpuTemperatureEstimated { get; private set; }

	/// <summary>Idem para a GPU. Integradas em geral NAO expoem sensor.</summary>
	public bool IsGpuTemperatureEstimated { get; private set; }

	/// <summary>Origem da ultima leitura, para diagnostico nos logs e na UI.</summary>
	public VoltrisOptimizer.Services.Hardware.ThermalReadingSource CpuTemperatureSource { get; private set; }
		= VoltrisOptimizer.Services.Hardware.ThermalReadingSource.None;

	/// <summary>Origem da ultima leitura de GPU.</summary>
	public VoltrisOptimizer.Services.Hardware.ThermalReadingSource GpuTemperatureSource { get; private set; }
		= VoltrisOptimizer.Services.Hardware.ThermalReadingSource.None;

	public double CpuClockMhz { get; private set; }

	public double CpuMaxClockMhz { get; private set; }

	public double GpuCoreClockMhz { get; private set; }

	public double GpuMemoryClockMhz { get; private set; }

	// Nome do adaptador GPU descoberto via WMI (reutilizado pela estimativa)
	private string _lastGpuName = "";

	/// <summary>
	/// Marca do processador (ex.: "11th Gen Intel(R) Core(TM) i5-1135G7").
	/// Lida uma unica vez e usada apenas pela estimativa de TDP/TjMax.
	/// </summary>
	private string _lastCpuBrandCache = "";

	public double GpuVramUsedGb { get; private set; }

	public double GpuVramTotalGb { get; private set; }

	public long LastInputMs { get; private set; }

	public double VoltrisProcessCpuPercent { get; private set; }

	private DateTime _lastCpuClockUtc = DateTime.MinValue;

	private DateTime _lastGpuClockUtc = DateTime.MinValue;

	public double VoltrisCpuAverage5Min { get; private set; }

	public long VoltrisWorkingSetMb { get; private set; }

	public int VoltrisHandles { get; private set; }

	public int VoltrisThreads { get; private set; }

	public int VoltrisGdiObjects { get; private set; }

	public int VoltrisUserObjects { get; private set; }

	public int VosStabilityExceptionsPerHour { get; set; }

	public int VosStabilityWatchdogTriggers { get; set; }

	public int VosStabilityServiceRestarts { get; set; }

	public double VosExecutionTimeAvgMs { get; set; }

	public double VosExecutionTimeMaxMs { get; set; }

	public DateTime LastUpdated { get; private set; } = DateTime.MinValue;

	// ── FPS Metrics (unificadas do IEtwFrameTimeMonitor) ──
	public double Fps { get; private set; }
	public double FpsOnePercentLow { get; private set; }
	public double FpsAverageFrametimeMs { get; private set; }
	public bool FpsIsStuttering { get; private set; }
	public bool FpsAvailable { get; private set; }

	public HardwareSummary Hardware { get; } = new HardwareSummary();
	public double ManagedHeapMb { get; private set; }
	public int GcGen0Collections { get; private set; }
	public int GcGen1Collections { get; private set; }
	public int GcGen2Collections { get; private set; }
	public int ThreadPoolAvailableThreads { get; private set; }
	public int ThreadPoolMaxThreads { get; private set; }


	public event EventHandler? MetricsUpdated;

	private SystemMetricsCache()
	{
	}

	// Classes WMI já testadas neste processo.
	// Validar custa um round-trip WMI COMPLETO, e o resultado é propriedade do
	// hardware/OS: não muda em runtime. Sem este cache, toda leitura fazia
	// DUAS queries (validação + real), e classes inexistentes — como
	// MSAcpi_ThermalZoneTemperature, ausente na maioria das máquinas — geravam
	// uma exceção a cada ciclo, indefinidamente.
	private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _wmiClassAvailability =
		new System.Collections.Concurrent.ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

	// [FIX:C-1] Normalização do scope WMI.
	//
	// BUG ORIGINAL: o scope era montado com $"root\\\\{scope}". Em string
	// interpolada normal cada "\\" vira UMA barra, então o resultado real era
	// "root\\cimv2" (barras duplicadas) — path que ManagementObjectSearcher
	// rejeita com ManagementException "Parâmetro inválido". Como a falha era
	// cacheada por (scope|class), GPU e MSAcpi_ThermalZoneTemperature ficavam
	// permanentemente mortos na sessão.
	//
	// EVIDÊNCIA (2026-09-27): 3x "Parâmetro inválido" 8ms após a construção do
	// dashboard; nenhum dado de GPU/thermal jamais retornou.
	//
	// Aceita namespace nu ("cimv2"), caminho relativo ("CIMV2") ou caminho
	// absoluto ("root\cimv2") e sempre devolve um scope válido, com barras
	// colapsadas em uma só.
	private static string NormalizeWmiScope(string scope)
	{
		if (string.IsNullOrWhiteSpace(scope))
			return @"root\cimv2";

		// Colapsa qualquer sequência de barras (uma ou mais) em uma única.
		string s = System.Text.RegularExpressions.Regex.Replace(scope.Trim(), @"\\+", @"\");

		// Já é um caminho (root\cimv2, root\WMI, ...): devolve como está.
		if (s.Contains('\\'))
			return s;

		// Namespace nu: prefixa a raiz padrão do WMI.
		return @"root\" + s;
	}

	// Distingue "a classe realmente não existe" de "o scope/parametro estava
	// malformado". O segundo caso é bug de código e NÃO pode ser cacheado,
	// senão a falha se torna permanente e silenciosa.
	private static bool IsInvalidScopeOrParameter(ManagementException ex)
	{
		var m = ex.Message ?? string.Empty;
		return m.Contains("Parâmetro inválido", StringComparison.OrdinalIgnoreCase)
			|| m.Contains("Invalid parameter", StringComparison.OrdinalIgnoreCase)
			|| m.Contains("Invalid class", StringComparison.OrdinalIgnoreCase)
			|| m.Contains("Classe inválida", StringComparison.OrdinalIgnoreCase);
	}

	// CORREÇÃO 4: Validação segura de queries WMI com timeout e fallback
	private static List<ManagementObject>? ExecuteWmiQueryWithValidation(string scope, string query, string className)
	{
		// [FIX:C-1] scope sempre normalizado antes de tocar no WMI.
		string wmiScope = NormalizeWmiScope(scope);
		string cacheKey = wmiScope + "|" + className;

		// Já sabemos que a classe não existe: não gasta round-trip nenhum.
		if (_wmiClassAvailability.TryGetValue(cacheKey, out bool knownAvailable) && !knownAvailable)
			return null;

		if (!knownAvailable)
		{
			try
			{
				// Validar se a classe WMI existe — só na primeira vez.
				using var validator = new ManagementObjectSearcher(wmiScope, $"SELECT * FROM {className} WHERE FALSE");
				validator.Get(); // Se falhar, a classe não existe
				_wmiClassAvailability[cacheKey] = true;
				App.LoggingService?.LogDebug(
					$"[FIX:C-1] WMI OK | scope='{scope}' -> normalizado='{wmiScope}' | classe={className} VALIDADA");
			}
			catch (ManagementException mex)
			{
				if (IsInvalidScopeOrParameter(mex))
				{
					// Scope/parametro malformado = bug, não ausência de classe.
					// NÃO cacheia: se o scope for corrigido em runtime, a próxima
					// chamada tenta de novo em vez de ficar morta para sempre.
					App.LoggingService?.LogWarning(
						$"[FIX:C-1] Scope WMI REJEITADO | entrada='{scope}' normalizado='{wmiScope}' " +
						$"classe={className} | {mex.Message} | resultado=NAO CACHEADO (bug de scope, nao classe ausente)");
					Debug.WriteLine($"[WMI] Scope inválido para {className}: {mex.Message}");
					return null;
				}

				Debug.WriteLine($"[WMI] Classe {className} não disponível em {wmiScope}: {mex.Message}");
				_wmiClassAvailability[cacheKey] = false;
				App.LoggingService?.LogDebug(
					$"[FIX:C-1] WMI classe ausente | scope='{wmiScope}' classe={className} | cacheado=NAO");
				return null;
			}
			catch (Exception ex)
			{
				// Falha transitória (timeout, busy): NÃO cacheia, para tentar de novo.
				Debug.WriteLine($"[WMI] Validação falhou para {className}: {ex.Message}");
				App.LoggingService?.LogWarning(
					$"[FIX:C-1] WMI validação falhou (transitório, não cacheado) | scope='{wmiScope}' classe={className} | {ex.Message}");
				return null;
			}
		}

		// Executar query real com timeout
		try
		{
			using var searcher = new ManagementObjectSearcher(wmiScope, query);
			searcher.Options.Timeout = TimeSpan.FromSeconds(3);

			// Materializa ANTES de descartar o searcher. Antes, a coleção era
			// devolvida de dentro do using e chegava ao consumidor com o
			// searcher já descartado.
			var results = new List<ManagementObject>();
			foreach (ManagementObject obj in searcher.Get())
				results.Add(obj);
			return results;
		}
		catch (ManagementException mex)
		{
			Debug.WriteLine($"[WMI] Query falhou: {query} - {mex.Message}");
			App.LoggingService?.LogWarning(
				$"[WMI] Query falhou | scope='{wmiScope}' | {query.Substring(0, Math.Min(90, query.Length))} | {mex.Message}");
			return null;
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"[WMI] Exception na query: {ex.Message}");
			App.LoggingService?.LogWarning(
				$"[WMI] Exceção na query | scope='{wmiScope}' | {query.Substring(0, Math.Min(90, query.Length))} | {ex.Message}");
			return null;
		}
	}

	public void Start()
	{
		lock (_updateLock)
		{
			if (_started)
			{
				return;
			}
			_started = true;
		}
		// Inicializar contadores GPU e CPU em background thread
		Task.Run(InitializeCounters);
		// Timer de alta frequência (250ms) — cada tick decide quais categorias atualizar
		_internalTimer = new Timer(TimerCallback, null, 1000, HighFreqMs);
		DebugLog("[METRICS-CACHE] ✅ Iniciado com categorias por frequência (CPU/GPU: 250ms, RAM/Temp: 1s, Clocks: 5s)");
		
		// PERFORMANCE: Conectar ao ciclo de vida da aplicação para ajuste de velocidade
		VoltrisOptimizer.Services.ApplicationStateTracker.StateChanged += OnApplicationLifecycleStateChanged;
		DebugLog("[METRICS-CACHE] Subscribed to ApplicationStateTracker for lifecycle-aware throttling");
	}

	private void OnApplicationLifecycleStateChanged(object? sender, VoltrisOptimizer.Services.ApplicationLifecycleStateChangedEventArgs e)
	{
		try
		{
			switch (e.NewState)
			{
				case VoltrisOptimizer.Services.ApplicationLifecycleState.Foreground:
					// Foreground: restaurar velocidade normal
					if (!_highPerformanceMode)
						SetUpdateSpeed(MetricsUpdateSpeed.Normal);
					DebugLog($"[METRICS-CACHE] Lifecycle: {e.OldState}→{e.NewState} — Speed restored to Normal");
					break;

				case VoltrisOptimizer.Services.ApplicationLifecycleState.Background:
					// Background: reduzir um pouco
					SetUpdateSpeed(MetricsUpdateSpeed.Reduced);
					DebugLog($"[METRICS-CACHE] Lifecycle: {e.OldState}→{e.NewState} — Speed reduced to Reduced");
					break;

				case VoltrisOptimizer.Services.ApplicationLifecycleState.Minimized:
				case VoltrisOptimizer.Services.ApplicationLifecycleState.Tray:
					// Minimizado/Tray: mínimo possível (somente dados críticos)
					if (!_highPerformanceMode) // não interferir com Modo Gamer
						SetUpdateSpeed(MetricsUpdateSpeed.Minimal);
					DebugLog($"[METRICS-CACHE] Lifecycle: {e.OldState}→{e.NewState} — Speed reduced to Minimal");
					break;
			}
		}
		catch (Exception ex)
		{
			DebugLog($"[METRICS-CACHE] Error in OnApplicationLifecycleStateChanged: {ex.Message}");
		}
	}

	private void InitializeCounters()
	{
		try
		{
			// Pré-aquecer contadores GPU
			UpdateGpuInstancesCache();
			_gpuD3DKMT = new GpuD3DKMTQuery();
			// Pré-aquecer contador CPU
			_cpuUtilityCounter = new SafePerformanceCounter("Processor Information", "% Processor Utility", "_Total");
			_cpuUtilityCounter.NextValue();
			// Pré-ler MaxClockSpeed (turbo) — lido 1 vez, não muda
			ReadCpuMaxClockOnce();
		}
		catch { }
	}

	public void SetHighPerformanceMode(bool enabled)
	{
		_highPerformanceMode = enabled;
		_internalTimer?.Change(HighFreqMs, HighFreqMs);
		DebugLog($"[METRICS-CACHE] Modo {(enabled ? "ALTA FREQUÊNCIA (500ms)" : "ECONÔMICO (2000ms)")}");
	}

	public void SetUpdateSpeed(MetricsUpdateSpeed speed)
	{
		_currentIntervalMs = (int)speed;
		_internalTimer?.Change(Math.Max(HighFreqMs, _currentIntervalMs / 4), Math.Max(HighFreqMs, _currentIntervalMs / 4));
	}

	private void TimerCallback(object? state)
	{
		try
		{
			Update();
		}
		catch (Exception ex)
		{
			Interlocked.Increment(ref _updateErrorCount);
			long errCount = Interlocked.Read(ref _updateErrorCount);
			try
			{
				string msg = $"[METRICS-CACHE] ❌ CRASH no TimerCallback (erro #{errCount}): {ex.GetType().Name}: {ex.Message}";
				DebugLog(msg);
				// Log consolidado no sistema principal de logging
				if (App.LoggingService != null)
				{
					App.LoggingService.LogError(msg, ex);
				}
				else
				{
					// Fallback para arquivo apenas se LoggingService não estiver disponível
					File.AppendAllText(
						Path.Combine(LogDirectoryResolver.Resolve(), "metrics_crash.log"),
						$"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {msg}{Environment.NewLine}{ex}{Environment.NewLine}");
				}
			}
			catch { }
			// Tentar reiniciar o timer
			try
			{
				_internalTimer?.Dispose();
				_internalTimer = null;
			}
			catch { }
			try
			{
				_internalTimer = new Timer(TimerCallback, null, _currentIntervalMs, _currentIntervalMs);
				DebugLog("[METRICS-CACHE] ♻️ Timer reiniciado após crash.");
			}
			catch { }
		}
	}

	/// <summary>
	/// Atualiza métricas de FPS a partir do monitor ETW unificado
	/// </summary>
	public void UpdateFps(double fps, double onePercentLow, double avgFrametimeMs, bool isStuttering)
	{
		Fps = fps;
		FpsOnePercentLow = onePercentLow;
		FpsAverageFrametimeMs = avgFrametimeMs;
		FpsIsStuttering = isStuttering;
		FpsAvailable = fps > 0;
	}

	public void Stop()
	{
		// Desinscrever do ciclo de vida
		VoltrisOptimizer.Services.ApplicationStateTracker.StateChanged -= OnApplicationLifecycleStateChanged;
		
		_internalTimer?.Dispose();
		_internalTimer = null;
		_cpuUtilityCounter?.Dispose();
		_cpuUtilityCounter = null;
		lock (_gpuCacheLock)
		{
			foreach (SafePerformanceCounter value in _activeGpuCounters.Values)
			{
				try
				{
					value.Dispose();
				}
				catch
				{
				}
			}
			_activeGpuCounters.Clear();
			_gpuInstances = null;
			}
			_gpuD3DKMT?.Dispose();
			_gpuD3DKMT = null;
		lock (_diskCacheLock)
		{
			foreach (SafePerformanceCounter value2 in _individualDiskCounters.Values)
			{
				try
				{
					value2.Dispose();
				}
				catch
				{
				}
			}
			_individualDiskCounters.Clear();
		}
		_started = false;
	}

	private void Update()
	{
		if (!Monitor.TryEnter(_updateLock))
		{
			Interlocked.Increment(ref _updateErrorCount);
			return;
		}
		try
		{
			Interlocked.Increment(ref _updateTickCount);
			long tick = Interlocked.Read(ref _updateTickCount);
			long errCount = Interlocked.Read(ref _updateErrorCount);
			DateTime now = DateTime.UtcNow;

			// Heartbeat log a cada 30s
			if ((now - _lastLogHeartbeat).TotalSeconds >= 30)
			{
				_lastLogHeartbeat = now;
				DebugLog($"[METRICS-CACHE] ❤️ Heartbeat — tick #{tick}, erros: {errCount}, CPU={CpuPercent:F1}%, GPU={GpuUsagePercent:F0}%, RAM={MemoryUsedPercent:F0}%, Temp={CpuTemperature:F0}°C");
			}

			// ── HIGH FREQUENCY (250ms): CPU%, GPU% ──
			if ((now - _lastHighFreq).TotalMilliseconds >= HighFreqMs)
			{
				_lastHighFreq = now;
				try { CpuPercent = Math.Max(0.0, Math.Min(100.0, ReadCpuPercent())); }
				catch (Exception ex) { DebugLog($"[METRICS-CACHE] ReadCpuPercent error: {ex.Message}"); }
				GpuUsagePercent = ReadGpuUsage();
                if (GpuUsagePercent >= GpuHighUsageThreshold)
                {
                    if (_gpuHighUsageStart == null) _gpuHighUsageStart = now;
                    else if ((now - _gpuHighUsageStart.Value).TotalSeconds >= GpuHighUsageDurationSec)
                    {
                        DebugLog($"[GPU] Sustained high usage {GpuUsagePercent:F1}% for >{GpuHighUsageDurationSec}s");
                        // TODO: implement throttling or mitigation actions
                    }
                }
                else
                {
                    _gpuHighUsageStart = null;
                }
			}

			// ── MEDIUM FREQUENCY (1000ms): RAM, Disk, Temp, Input, Process ──
			if ((now - _lastMedFreq).TotalMilliseconds >= MedFreqMs)
			{
				_lastMedFreq = now;
				try { DiskUsagePercent = ReadDiskUsage(); }
				catch (Exception ex) { DebugLog($"[METRICS-CACHE] ReadDiskUsage error: {ex.Message}"); }
				try { LastInputMs = ReadLastInputMs(); }
				catch (Exception ex) { DebugLog($"[METRICS-CACHE] ReadLastInputMs error: {ex.Message}"); }
				ReadMemory();
				ReadVoltrisProcessMetrics();
				ReadTemperatureFromHub();
			}

			// ── LOW FREQUENCY (5000ms): Clocks, GPU instances ──
			if ((now - _lastLowFreq).TotalMilliseconds >= LowFreqMs)
			{
				_lastLowFreq = now;
			ReadCpuClock();
			ReadGpuClock();
			UpdateGpuInstancesCache();
			ReadGcAndThreadPoolMetrics();
			}

			LastUpdated = now;
		}
		catch (Exception ex)
		{
			Interlocked.Increment(ref _updateErrorCount);
			DebugLog($"[METRICS-CACHE] ⚠️ SAFETY NET capturou exceção: {ex.GetType().Name}: {ex.Message}");
			try
			{
				// Log consolidado no sistema principal de logging
				if (App.LoggingService != null)
				{
					App.LoggingService.LogError($"[METRICS-CACHE] SAFETY NET: {ex.Message}", ex);
				}
				else
				{
					// Fallback para arquivo apenas se LoggingService não estiver disponível
					File.AppendAllText(
						Path.Combine(LogDirectoryResolver.Resolve(), "metrics_crash.log"),
						$"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] SAFETY NET: {ex}{Environment.NewLine}");
				}
			}
			catch { }
		}
		finally
		{
			Monitor.Exit(_updateLock);
		}
		try
		{
			this.MetricsUpdated?.Invoke(this, EventArgs.Empty);
		}
		catch (Exception ex)
		{
			DebugLog($"[METRICS-CACHE] MetricsUpdated event error: {ex.Message}");
		}
	}

	private void ReadTemperatureFromHub()
	{
		try
		{
			if (App.Services == null) return;
			var telemetryHub = App.Services.GetService(typeof(VoltrisOptimizer.Services.Hardware.IHardwareTelemetryHub)) as VoltrisOptimizer.Services.Hardware.IHardwareTelemetryHub;
			if (telemetryHub == null) return;

			// Leitura NÃO-bloqueante do cache de sensores
			var metrics = telemetryHub.GetLatestMetrics();
			if (metrics == null) return;

			bool hubCpuTempOk = !double.IsNaN(metrics.CpuTemperature) && metrics.CpuTemperature > 0;
			bool hubGpuTempOk = !double.IsNaN(metrics.GpuTemperature) && metrics.GpuTemperature > 0;

			// PRIORIDADE 1: LibreHardwareMonitor (real sensor, atualizado a cada 3s pelo background loop)
			if (hubCpuTempOk)
			{
				CpuTemperature = metrics.CpuTemperature;
				_lastAcpiTempCache = metrics.CpuTemperature;
				_lastAcpiUpdateUtc = DateTime.UtcNow;
			}
			if (hubGpuTempOk)
			{
				GpuTemperature = metrics.GpuTemperature;
			}

			// ── PRIORIDADE 2: leitura ACPI real, sem estimativa. ──
			//
			// CORREÇÃO DE AUDITORIA: quando o hub não trazia sensor, esta bloco
			// FABRICAVA uma temperatura por carga de CPU
			//   targetEst = _lastAcpiTempCache + (CpuPercent/100)^1.25*25 + delta*0.3
			// e ainda definia
			//   GpuTemperature = max(35, CpuTemperature - 3)
			// Como esse valor alimentava o score de SAÚDE e a página de Diagnóstico,
			// a ausência de sensor aparecia como medição válida — e uma carga alta
			// de CPU podia cruzar os limiares térmicos e produzir um alerta de
			// "temperatura alta" em uma máquinawhose temperatura real era normal.
			//
			// Agora: sem sensor, a temperatura permanece double.NaN. A leitura ACPI
			// real continua sendo tentada (é uma fonte legítima), mas o resultado
			// da estimativa é descartado.
			if (!hubCpuTempOk)
			{
				// ── CADEIA DE FONTES REAIS ──
				//
				// O bug reported pelo usuário: a temperatura mostrava "N/D" em toda
				// máquina, porque só havia DUAS fontes e as duas dependem de elevação:
				//   1. LibreHardwareMonitor -> precisa do driver de kernel (PawnIO/Ring0)
				//   2. MSAcpi_ThermalZoneTemperature (root\WMI) -> "Acesso negado" sem
				//      elevação, e a classe não existe em boa parte dos desktops.
				//
				// A fonte que FUNCIONA sem elevação é o contador de desempenho
				// "Thermal Zone Information" (e a classe WMI espelho
				// Win32_PerfFormattedData_Counters_ThermalZoneInformation). Medidos
				// nesta máquina: 334 K = 60,9 °C, coerente com um i5 em uso.
				//
				// A ordem importa: driver primeiro (mais preciso, é o die real),
				// depois ACPI, depois contador. Nenhuma delas inventa valor.
				//
				// MUDANÇA DE REQUISITO (usuário): se TODAS as fontes reais
				// falharem, a temperatura não fica mais N/D — cai imediatamente
				// para uma ESTIMATIVA, que vem marcada como estimada e é
				// isolada das decisões de segurança (ver IsCpuTemperatureEstimated
				// e GlobalThermalMonitorService). O estimador usa TDP e razão de
				// throttle, não a reta grosseira "30 + carga*50" do backup.
				if ((DateTime.UtcNow - _lastAcpiUpdateUtc).TotalSeconds >= _thermalSourceRetrySeconds)
				{
					_lastAcpiUpdateUtc = DateTime.UtcNow;

					// Fonte 1: ACPI via WMI (boa parte das máquinas, exige elevação).
					double temp = ReadAcpiThermalZoneTemp();
					VoltrisOptimizer.Services.Hardware.ThermalReadingSource source =
						!double.IsNaN(temp)
							? VoltrisOptimizer.Services.Hardware.ThermalReadingSource.AcpiThermalZone
							: VoltrisOptimizer.Services.Hardware.ThermalReadingSource.None;

					// Fonte 2: contador de desempenho "Thermal Zone Information".
					// É a que funciona em notebook sem elevação, então nunca pode faltar.
					if (double.IsNaN(temp))
					{
						temp = ReadThermalZonePerfCounter();
						if (!double.IsNaN(temp))
							source = VoltrisOptimizer.Services.Hardware.ThermalReadingSource.ThermalZoneCounter;
					}

					if (!double.IsNaN(temp) && temp > 0 && temp < 150)
					{
						CpuTemperature = temp;
						_lastAcpiTempCache = temp;
						_lastThermalSource = source.ToString();
						CpuTemperatureSource = source;
						IsCpuTemperatureEstimated = false;
						DebugLog($"[TEMP] Leitura real via {source}: {temp}°C");
					if (_ultimaOrigemTempRegistrada != _lastThermalSource)
					{
						_ultimaOrigemTempRegistrada = _lastThermalSource;
						LogTemperatura($"[TEMP] Sensor REAL de CPU DETECTADO via {source}: {temp:F1}°C " +
									"(estimativa desativada enquanto o sensor estiver disponível).");
					}
					}
					else
					{
						// ── FALLBACK: ESTIMATIVA ──
						// Nenhuma fonte real respondeu. Em vez de N/D, estima-se
						// por TDP + razão de throttle. Fica SEMPRE marcado como
						// estimado, e por isso nunca aciona throttling/plano.
						var estimate = VoltrisOptimizer.Services.Hardware.ThermalEstimateService
							.EstimateCpu(CpuPercent, _lastCpuBrand);

						CpuTemperature = estimate.Celsius;
						CpuTemperatureSource = VoltrisOptimizer.Services.Hardware.ThermalReadingSource.Estimated;
						IsCpuTemperatureEstimated = true;
						_lastThermalSource = "Estimativa (TDP + throttle)";

						// Log só na transição, para não repetir a cada tique.
						if (_ultimaOrigemTempRegistrada != _lastThermalSource)
						{
							_ultimaOrigemTempRegistrada = _lastThermalSource;
							// Inclui as entradas do modelo no log. Sem isso e'
							// impossivel avaliar a qualidade da estimativa depois:
							// nao se sabe qual TDP foi assumido nem a razao de
							// throttle que entrou no calculo.
							LogTemperatura($"[TEMP] Nenhuma fonte real de CPU respondeu (LibreHardwareMonitor+PawnIO, " +
									 $"ACPI/WMI e contador de zona térmica). CAI PARA ESTIMATIVA por TDP+throttle: " +
									 $"{estimate.Celsius:F1}°C. Marcada como estimada e isolada das decisões de segurança " +
									 $"(não aciona throttling nem plano de energia). " +
									 $"Entradas do modelo: TDP_assumido={VoltrisOptimizer.Services.Hardware.ThermalEstimateService.DescribeAssumedTdp(_lastCpuBrand)} " +
									 $"Razão_throttle={VoltrisOptimizer.Services.Hardware.ThermalEstimateService.ReadThrottleRatio():F3} " +
									 $"Ajuste_TjMax={VoltrisOptimizer.Services.Hardware.ThermalEstimateService.DescribeAssumedTjMax(_lastCpuBrand)}");
						}
					}
				}

				// Rede de segurança: se a cadeia acima não produziu nada
				// (ex.: o bloco de fonte real nem rodou neste tique), estima
				// agora. A estimativa é SEMPRE rotulada.
				if (double.IsNaN(CpuTemperature) || CpuTemperature <= 0)
				{
					var fallback = VoltrisOptimizer.Services.Hardware.ThermalEstimateService
						.EstimateCpu(CpuPercent, _lastCpuBrand);
					CpuTemperature = fallback.Celsius;
					CpuTemperatureSource = VoltrisOptimizer.Services.Hardware.ThermalReadingSource.Estimated;
					IsCpuTemperatureEstimated = true;
					_lastThermalSource = "Estimativa (TDP + throttle)";
				}
			}

			// ══════════════════════════════════════════════════════════════
			// GPU — API oficial do fabricante tem PRIORIDADE ABSOLUTA
			// ══════════════════════════════════════════════════════════════
			// Ordem: LibreHardwareMonitor (hub) -> NVML/ADL -> estimativa.
			// A estimativa para GPU integrada é ancorada na temperatura REAL
			// da CPU quando existe, porque o die é compartilhado.
			if (!hubGpuTempOk)
			{
				// Tenta as APIs oficiais. Carregamento dinâmico: sem driver da
				// marca na máquina, devolve indisponível sem custo.
				var vendor = VoltrisOptimizer.Services.Hardware.GpuVendorTemperature.TryReadRealTemperature();

				if (vendor.IsValid)
				{
					GpuTemperature = vendor.Celsius;
					GpuTemperatureSource = VoltrisOptimizer.Services.Hardware.ThermalReadingSource.VendorApi;
					IsGpuTemperatureEstimated = false;
					DebugLog($"[TEMP-GPU] Leitura real via API do fabricante: {vendor.Celsius:F1}°C");
					if (_ultimaOrigemTempGpuRegistrada != "VendorApi")
					{
						_ultimaOrigemTempGpuRegistrada = "VendorApi";
						LogTemperatura($"[TEMP-GPU] Sensor REAL de GPU DETECTADO via API do fabricante " +
									$"(NVML/AMD ADL): {vendor.Celsius:F1}°C. Adaptador='{ResolveGpuName()}'. " +
									"Estimativa desativada enquanto o sensor estiver disponível.");
					}
				}
				else
				{
					// Ancoramos no CPU mesmo quando ele é ESTIMADO, de propósito.
					//
					// BUG CORRIGIDO: antes passava null nesse caso, e a estimativa
					// da GPU saltava entre duas âncoras diferentes — 50 °C sem âncora
					// e 75 °C com âncora — alternando a cada tique. Agora a âncora é
					// sempre a mesma, e o resultado é monotônico e estável.
					//
					// A marcação "estimada" continua correta: tanto a CPU quanto a
					// GPU ficam marcadas, e nenhuma das duas alimenta segurança.
					var gpuEstimate = VoltrisOptimizer.Services.Hardware.ThermalEstimateService
						.EstimateGpu(GpuUsagePercent, CpuTemperature, CpuPercent);

					GpuTemperature = gpuEstimate.Celsius;
					GpuTemperatureSource = VoltrisOptimizer.Services.Hardware.ThermalReadingSource.Estimated;
					IsGpuTemperatureEstimated = true;

					if (_ultimaOrigemTempGpuRegistrada != "Estimativa")
					{
						_ultimaOrigemTempGpuRegistrada = "Estimativa";
						LogTemperatura("[TEMP-GPU] Sem API de fabricante disponível " +
									$"(NVML/AMD ADL ausentes) e sem sensor. Adaptador='{ResolveGpuName()}'. " +
									$"CAI PARA ESTIMATIVA rotulada: {gpuEstimate.Celsius:F1}°C, " +
									"isolada das decisões de segurança.");
					}
				}
			}
			else
			{
				// O hub (LibreHardwareMonitor) leu sensor real: é a melhor fonte.
				IsGpuTemperatureEstimated = false;
				GpuTemperatureSource = VoltrisOptimizer.Services.Hardware.ThermalReadingSource.LibreHardwareMonitor;
				_ultimaOrigemTempGpuRegistrada = "LibreHardwareMonitor";
			}

			if (!double.IsNaN(metrics.CpuClock) && metrics.CpuClock > 0)
				CpuClockMhz = metrics.CpuClock;
			if (!double.IsNaN(metrics.GpuClock) && metrics.GpuClock > 0)
				GpuCoreClockMhz = metrics.GpuClock;
			if (!double.IsNaN(metrics.GpuVramUsed) && metrics.GpuVramUsed >= 0)
				GpuVramUsedGb = metrics.GpuVramUsed;
			if (!double.IsNaN(metrics.GpuVramTotal) && metrics.GpuVramTotal > 0)
				GpuVramTotalGb = metrics.GpuVramTotal;
		}
		catch (Exception ex)
		{
			DebugLog($"[METRICS-CACHE] ReadTemperatureFromHub error: {ex.Message}");
		}
	}

	private double ReadCpuPercent()
	{
		// PRIMARY: % Processor Utility (turbo-scaled) — mesmo usado pelo Gerenciador de Tarefas
		// na aba "Desempenho". Leva em conta a frequência real do clock (turbo boost),
		// então o valor pode chegar a 100% mesmo com carga modesta em CPUs que turbam alto.
		double util = ReadCpuUtility();
		if (util >= 0)
			return Math.Max(0.0, Math.Min(100.0, util));

		// FALLBACK: GetSystemTimes (% Processor Time, sem escala de frequência)
		if (!GetSystemTimes(out var lpIdleTime, out var lpKernelTime, out var lpUserTime))
		{
			return CpuPercent;
		}
		if (_prevIdle == 0 && _prevKernel == 0 && _prevUser == 0)
		{
			_prevIdle = lpIdleTime;
			_prevKernel = lpKernelTime;
			_prevUser = lpUserTime;
			return 0.0;
		}
		long num = lpIdleTime - _prevIdle;
		long num2 = lpKernelTime - _prevKernel;
		long num3 = lpUserTime - _prevUser;
		_prevIdle = lpIdleTime;
		_prevKernel = lpKernelTime;
		_prevUser = lpUserTime;
		long num4 = num2 + num3;
		if (num4 <= 0)
		{
			return CpuPercent;
		}
		double num5 = num4 - num;
		return Math.Max(0.0, Math.Min(100.0, num5 / (double)num4 * 100.0));
	}

	private double ReadCpuUtility()
	{
		try
		{
			if (_cpuUtilityCounter == null)
			{
				_cpuUtilityCounter = new SafePerformanceCounter("Processor Information", "% Processor Utility", "_Total");
				_cpuUtilityCounter.NextValue();
				_cpuUtilityCounter.NextValue(); // segundo sample para obter valor real
				return _cpuUtilityCounter.NextValue(); // terceiro sample já é estável
			}
			double val = _cpuUtilityCounter.NextValue();
			if (val >= 0 && val <= 100)
				return val;
			return -1;
		}
		catch
		{
			return -1;
		}
	}

	private double ReadDiskUsage()
	{
		try
		{
			DateTime utcNow = DateTime.UtcNow;
			if ((utcNow - _lastDiskInstanceUpdate).TotalSeconds >= 30.0)
			{
				_lastDiskInstanceUpdate = utcNow;
				UpdateDiskInstancesCache();
			}
			lock (_diskCacheLock)
			{
				// Agrupar partições por disco físico (ex: "0 C:" + "0 D:" → disco 0)
				// InstanceName tem formato "{diskIndex} {driveLetter}:"
				var diskGroups = new Dictionary<string, (double usage, string type)>();
				foreach (var kvp in _individualDiskCounters)
				{
					try
					{
						string driveLetter = kvp.Key;
						double partUsage = Math.Max(0f, Math.Min(100f, kvp.Value.NextValue()));
						string instanceName = kvp.Value.InstanceName;
						int spaceIdx = instanceName.IndexOf(' ');
						string diskIndex = (spaceIdx > 0) ? instanceName.Substring(0, spaceIdx) : "?";
						if (!diskGroups.ContainsKey(diskIndex))
						{
							string type = "HD";
							if (_cachedDriveToTypeMap != null && _cachedDriveToTypeMap.TryGetValue(driveLetter, out var t))
								type = t;
							diskGroups[diskIndex] = (0, type);
						}
						var current = diskGroups[diskIndex];
						diskGroups[diskIndex] = (current.usage + partUsage, current.type);
					}
					catch
					{
					}
				}
				// Rebuild Disks com um item por disco físico (diff update)
				var newDisks = diskGroups.Select(dg =>
				{
					double usage = Math.Max(0, Math.Min(100, dg.Value.usage));
					return new DiskMetricInfo
					{
						Name = $"{dg.Value.type} (Disco {dg.Key})",
						UsagePercent = usage
					};
				});
				double maxUsage = newDisks.Any() ? newDisks.Max(d => d.UsagePercent) : 0;
				CollectionDiffUpdater.ReplaceInPlace(Disks, newDisks, d => d.Name);
				return maxUsage;
			}
		}
		catch
		{
			return DiskUsagePercent;
		}
	}

	private void UpdateDiskInstancesCache()
	{
		try
		{
			if (_cachedDriveToTypeMap == null)
			{
				Dictionary<string, string> dictionary = new Dictionary<string, string>();
				try
				{
					Dictionary<uint, string> dictionary2 = new Dictionary<uint, string>();
					using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("Root\\Microsoft\\Windows\\Storage", "SELECT DeviceId, MediaType, BusType FROM MSFT_PhysicalDisk");
					using ManagementObjectCollection managementObjectCollection = managementObjectSearcher.Get();
					foreach (ManagementObject item in managementObjectCollection)
					{
						if (uint.TryParse(item["DeviceId"]?.ToString(), out var result))
						{
							ushort num = (ushort)((item["MediaType"] != null) ? Convert.ToUInt16(item["MediaType"]) : 0);
							ushort num2 = (ushort)((item["BusType"] != null) ? Convert.ToUInt16(item["BusType"]) : 0);
							string value = "HD";
							switch (num)
							{
							case 4:
								value = ((num2 == 17) ? "SSD NVME" : "SSD SATA3");
								break;
							case 3:
								value = "HDD";
								break;
							}
							dictionary2[result] = value;
						}
					}
					using ManagementObjectSearcher managementObjectSearcher2 = new ManagementObjectSearcher("Root\\Microsoft\\Windows\\Storage", "SELECT DiskNumber, DriveLetter FROM MSFT_Partition WHERE DriveLetter IS NOT NULL");
					using ManagementObjectCollection managementObjectCollection2 = managementObjectSearcher2.Get();
					foreach (ManagementObject item2 in managementObjectCollection2)
					{
						string text = item2["DriveLetter"]?.ToString()?.Trim('\0', ' ') ?? "";
						if (!string.IsNullOrEmpty(text) && uint.TryParse(item2["DiskNumber"]?.ToString(), out var result2) && dictionary2.TryGetValue(result2, out var value2))
						{
							dictionary[text] = value2;
						}
					}
				}
				catch
				{
				}
				_cachedDriveToTypeMap = dictionary;
			}
			if (!SafePerformanceCounterCategory.Exists("PhysicalDisk"))
			{
				return;
			}
			SafePerformanceCounterCategory safePerformanceCounterCategory = new SafePerformanceCounterCategory("PhysicalDisk");
			string[] instanceNames = safePerformanceCounterCategory.GetInstanceNames();
			if (instanceNames == null)
			{
				return;
			}
			List<string> drives = (from d in DriveInfo.GetDrives()
				where d.IsReady && d.DriveType == System.IO.DriveType.Fixed
				select d.Name.Substring(0, 1)).ToList();
			lock (_diskCacheLock)
			{
				// Remover counters de partições que não existem mais
				List<string> staleDrives = drives == null ? new List<string>() : _individualDiskCounters.Keys.Where(k => !drives.Contains(k)).ToList();
				foreach (string stale in staleDrives)
				{
					if (_individualDiskCounters.Remove(stale, out var staleCounter))
					{
						try { staleCounter.Dispose(); } catch { }
					}
				}
				// Criar counters para novas partições (Disks é reconstruído em ReadDiskUsage)
				foreach (string driveLetter in drives)
				{
					if (_individualDiskCounters.ContainsKey(driveLetter))
					{
						continue;
					}
					string text2 = instanceNames.FirstOrDefault((string i) => i.Contains(" " + driveLetter + ":") || i.StartsWith(driveLetter + ":") || i.Equals(driveLetter + ":"));
					if (text2 != null)
					{
						try
						{
							SafePerformanceCounter safePerformanceCounter = new SafePerformanceCounter("PhysicalDisk", "% Disk Time", text2);
							safePerformanceCounter.NextValue();
							_individualDiskCounters[driveLetter] = safePerformanceCounter;
						}
						catch
						{
						}
					}
				}
			}
		}
		catch
		{
		}
	}

	private double ReadGpuUsage()
	{
		try
		{
			// PRIMARY: D3DKMTQuery (matches Task Manager GPU utilization)
			if (_gpuD3DKMT != null && _gpuD3DKMT.IsAvailable)
			{
				double usage = _gpuD3DKMT.GetGpuUsagePercent();
				if (!double.IsNaN(usage) && usage >= 0)
				{
					return Math.Max(0.0, Math.Min(100.0, usage));
				}
			}
			// SECONDARY: PerformanceCounters GPU Engine (same API as Task Manager)
			if (_gpuInstances == null || (DateTime.UtcNow - _lastGpuInstanceUpdate).TotalSeconds >= 60.0)
			{
				_lastGpuInstanceUpdate = DateTime.UtcNow;
				UpdateGpuInstancesCache();
			}
			double maxGpu = 0.0;
			int validCount = 0;
			lock (_gpuCacheLock)
			{
				foreach (var kvp in _activeGpuCounters)
				{
					try
					{
						double val = kvp.Value.NextValue();
						if (val > 0)
						{
							if (val > maxGpu) maxGpu = val;
								validCount++;
							}
					}
					catch { }
				}
			}
			if (validCount > 0)
			{
				// Log GPU usage apenas a cada 10 segundos para reduzir flood
				if ((DateTime.Now - _lastGpuLog).TotalSeconds >= 10.0)
				{
					DebugLog($"[GPU] MAX={maxGpu:F1}% de {validCount} engines ativos");
					_lastGpuLog = DateTime.Now;
				}
				return Math.Max(0.0, Math.Min(100.0, maxGpu));
			}


			// FALLBACK: GlobalThermalMonitorService (LibreHardwareMonitor)
			IGlobalThermalMonitorService thermalMonitorService = App.ThermalMonitorService;
			if (thermalMonitorService != null)
			{
				double gpuUsage = thermalMonitorService.GetGpuUsage();
				if (!double.IsNaN(gpuUsage) && gpuUsage > 0)
					return Math.Max(0.0, Math.Min(100.0, gpuUsage));
			}

			return GpuUsagePercent;
		}
		catch (Exception ex)
		{
			DebugLog($"[GPU] ReadGpuUsage error: {ex.Message}");
			return GpuUsagePercent;
		}
	}

	private void UpdateGpuInstancesCache()
	{
		try
		{
			if (!SafePerformanceCounterCategory.Exists("GPU Engine"))
			{
				return;
			}
			SafePerformanceCounterCategory safePerformanceCounterCategory = new SafePerformanceCounterCategory("GPU Engine");
			string[] instanceNames = safePerformanceCounterCategory.GetInstanceNames();
			if (instanceNames == null)
			{
				return;
			}
			lock (_gpuCacheLock)
			{
				_gpuInstances = instanceNames;
				List<string> list = _activeGpuCounters.Keys.Except(instanceNames).ToList();
				foreach (string item in list)
				{
					if (_activeGpuCounters.Remove(item, out var value))
					{
						try
						{
							value.Dispose();
						}
						catch
						{
						}
					}
				}
				IEnumerable<string> enumerable = instanceNames.Where((string i) => 
					i.IndexOf("engtype_3D", StringComparison.OrdinalIgnoreCase) >= 0 ||
					i.IndexOf("engtype_Compute", StringComparison.OrdinalIgnoreCase) >= 0 ||
					i.IndexOf("engtype_Copy", StringComparison.OrdinalIgnoreCase) >= 0 ||
					i.IndexOf("engtype_VideoDecode", StringComparison.OrdinalIgnoreCase) >= 0 ||
					i.IndexOf("engtype_VideoEncode", StringComparison.OrdinalIgnoreCase) >= 0 ||
					i.IndexOf("engtype_VideoProcessing", StringComparison.OrdinalIgnoreCase) >= 0);
				foreach (string item2 in enumerable)
				{
					if (!_activeGpuCounters.ContainsKey(item2))
					{
						try
						{
							SafePerformanceCounter safePerformanceCounter = new SafePerformanceCounter("GPU Engine", "Utilization Percentage", item2);
							safePerformanceCounter.NextValue();
							_activeGpuCounters[item2] = safePerformanceCounter;
						}
						catch
						{
						}
					}
				}
			}
		}
		catch
		{
		}
	}

	private void ReadMemory()
	{
		try
		{
			MEMORYSTATUSEX mEMORYSTATUSEX = default(MEMORYSTATUSEX);
			mEMORYSTATUSEX.dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
			MEMORYSTATUSEX lpBuffer = mEMORYSTATUSEX;
			if (GlobalMemoryStatusEx(ref lpBuffer))
			{
				MemoryUsedPercent = lpBuffer.dwMemoryLoad;
				AvailableRamMb = (double)lpBuffer.ullAvailPhys / 1048576.0;
				Hardware.TotalRamGb = (double)lpBuffer.ullTotalPhys / 1073741824.0;
			}
		}
		catch (Exception ex)
		{
			DebugLog($"[METRICS-CACHE] ReadMemory failed: {ex.Message}");
		}
	}

	private void ReadVoltrisProcessMetrics()
	{
		try
		{
			// O processo e sempre o proprio Voltris: reaproveitamos o wrapper em vez de criar
			// um novo objeto Process a cada ciclo (o que abria e vazava um handle por ciclo).
			Process currentProcess = _selfProcess ??= Process.GetCurrentProcess();
			VoltrisWorkingSetMb = currentProcess.WorkingSet64 / 1048576;
			VoltrisHandles = currentProcess.HandleCount;
			VoltrisThreads = currentProcess.Threads.Count;
			try
			{
				VoltrisGdiObjects = (int)GetGuiResources(currentProcess.Handle, 0u);
				VoltrisUserObjects = (int)GetGuiResources(currentProcess.Handle, 1u);
			}
			catch
			{
			}
			DateTime utcNow = DateTime.UtcNow;
			TimeSpan totalProcessorTime = currentProcess.TotalProcessorTime;
			if (_prevVoltrisTime != DateTime.MinValue)
			{
				double totalMilliseconds = (utcNow - _prevVoltrisTime).TotalMilliseconds;
				double totalMilliseconds2 = (totalProcessorTime - _prevVoltrisCpuTime).TotalMilliseconds;
				if (totalMilliseconds > 0.0)
				{
					VoltrisProcessCpuPercent = Math.Max(0.0, Math.Min(100.0, totalMilliseconds2 / ((double)Environment.ProcessorCount * totalMilliseconds) * 100.0));
					_cpuHistory.Enqueue(VoltrisProcessCpuPercent);
					if (_cpuHistory.Count > 300)
					{
						_cpuHistory.Dequeue();
					}
					VoltrisCpuAverage5Min = _cpuHistory.Average();
				}
			}
			_prevVoltrisTime = utcNow;
			_prevVoltrisCpuTime = totalProcessorTime;
		}
		catch
		{
		}
	}

	private long ReadLastInputMs()
	{
		try
		{
			LASTINPUTINFO lASTINPUTINFO = default(LASTINPUTINFO);
			lASTINPUTINFO.cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>();
			LASTINPUTINFO plii = lASTINPUTINFO;
			if (!GetLastInputInfo(ref plii))
			{
				return LastInputMs;
			}
			return Environment.TickCount - (int)plii.dwTime;
		}
		catch (Exception ex)
		{
			DebugLog($"[METRICS-CACHE] ReadLastInputMs error: {ex.Message}");
			return LastInputMs;
		}
	}

	private void ReadCpuMaxClockOnce()
	{
		try
		{
			using var searcher = new ManagementObjectSearcher("SELECT MaxClockSpeed FROM Win32_Processor");
			foreach (ManagementObject obj in searcher.Get())
			{
                using var __dispose_obj = obj;
				var maxClock = obj["MaxClockSpeed"];
				if (maxClock != null && double.TryParse(maxClock.ToString(), out var maxMhz) && maxMhz > 100 && maxMhz < 10000)
				{
					CpuMaxClockMhz = maxMhz;
					DebugLog($"[CLOCK] CPU max clock (turbo): {maxMhz:F0} MHz (pre-read)");
				}
				break;
			}
		}
		catch (Exception ex)
		{
			DebugLog($"[CLOCK] CPU max clock pre-read failed: {ex.Message}");
		}
	}

	private void ReadCpuClock()
	{
		var now = DateTime.UtcNow;
		if ((now - _lastCpuClockUtc).TotalSeconds < 5.0)
			return;
		_lastCpuClockUtc = now;
		try
		{
			using var searcher = new ManagementObjectSearcher("SELECT CurrentClockSpeed FROM Win32_Processor");
			foreach (ManagementObject obj in searcher.Get())
			{
                using var __dispose_obj = obj;
				var clock = obj["CurrentClockSpeed"];
				if (clock != null && double.TryParse(clock.ToString(), out var mhz) && mhz > 100 && mhz < 10000)
				{
					CpuClockMhz = mhz;
					DebugLog($"[CLOCK] CPU clock: {mhz:F0} MHz");
				}
				break;
			}
		}
		catch (Exception ex)
		{
			DebugLog($"[CLOCK] CPU clock read failed: {ex.Message}");
		}
	}

	private void ReadGpuClock()
	{
		var now = DateTime.UtcNow;
		if ((now - _lastGpuClockUtc).TotalSeconds < 5.0)
			return;
		_lastGpuClockUtc = now;
		try
		{
			// Usa o valor em cache: a interpolação de um log não deve disparar
			// uma sondagem de hardware. Antes era HasNvidiaGpu() aqui, e ela roda
			// mesmo com o log desligado, porque a string é sempre montada.
			bool hasNvidia = HasNvidiaGpu();
			DebugLog($"[CLOCK] ReadGpuClock: HasNvidia={hasNvidia}, current GpuCoreClockMhz={GpuCoreClockMhz}");
			if (hasNvidia)
			{
				using var proc = new Process
				{
					StartInfo = new ProcessStartInfo
					{
						FileName = "nvidia-smi",
						Arguments = "--query-gpu=clocks.gr,clocks.mem,memory.used,memory.total --format=csv,noheader,nounits",
						UseShellExecute = false,
						CreateNoWindow = true,
						RedirectStandardOutput = true
					}
				};
				proc.Start();
				var output = proc.StandardOutput.ReadToEnd();
				proc.WaitForExit(3000);
				var parts = output.Trim().Split(',');
				if (parts.Length >= 4)
				{
					if (double.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var coreClock) && coreClock > 0 && coreClock < 5000)
					{
						GpuCoreClockMhz = coreClock;
					}
					if (double.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var memClock) && memClock > 0 && memClock < 20000)
					{
						GpuMemoryClockMhz = memClock;
					}
					if (double.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var vramUsed) && vramUsed > 0)
					{
						GpuVramUsedGb = vramUsed / 1024.0;
					}
					if (double.TryParse(parts[3].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var vramTotal) && vramTotal > 0)
					{
						GpuVramTotalGb = vramTotal / 1024.0;
					}
					DebugLog($"[CLOCK] GPU nvidia-smi: core={GpuCoreClockMhz:F0} MHz, mem={GpuMemoryClockMhz:F0} MHz");
					return;
				}
				DebugLog($"[CLOCK] GPU nvidia-smi: parse failed (output='{output.Trim()}')");
			}
			ReadGpuClockViaWmi();

			// Fallback SEMPRE recalcula: estimar clock baseado na carga atual da GPU
			// Cobre TODOS os padrões conhecidos: Intel, AMD, NVIDIA, integradas e dedicadas
			double estimatedGpuBase = 0;
			string gpuName = _lastGpuName;

			if (gpuName.Contains("Iris", StringComparison.OrdinalIgnoreCase))
				estimatedGpuBase = 1100;
			else if (gpuName.Contains("Arc", StringComparison.OrdinalIgnoreCase))
				estimatedGpuBase = 1600;
			else if (gpuName.Contains("Intel", StringComparison.OrdinalIgnoreCase) &&
			         (gpuName.Contains("UHD", StringComparison.OrdinalIgnoreCase) ||
			          gpuName.Contains("HD", StringComparison.OrdinalIgnoreCase) ||
			          gpuName.Contains("Graphics", StringComparison.OrdinalIgnoreCase)))
				estimatedGpuBase = 1000;
			else if (gpuName.Contains("RTX", StringComparison.OrdinalIgnoreCase))
				estimatedGpuBase = 1800;
			else if (gpuName.Contains("GTX", StringComparison.OrdinalIgnoreCase))
				estimatedGpuBase = 1500;
			else if (gpuName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
			         gpuName.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
			         gpuName.Contains("Quadro", StringComparison.OrdinalIgnoreCase) ||
			         gpuName.Contains("Tesla", StringComparison.OrdinalIgnoreCase))
				estimatedGpuBase = 1500;
			else if (gpuName.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
			         gpuName.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
			         gpuName.Contains("RX ", StringComparison.OrdinalIgnoreCase) ||   // RX 6000/7000 series
			         gpuName.Contains("R9 ", StringComparison.OrdinalIgnoreCase) ||
			         gpuName.Contains("R7 ", StringComparison.OrdinalIgnoreCase))
				estimatedGpuBase = 1500;
			else
			{
				// Fallback genérico: qualidade de vídeo detectada mas nome vazio ou não mapeado
				estimatedGpuBase = 1000;
				DebugLog($"[CLOCK] GPU nome vazio ou não mapeado, usando fallback genérico 1000 MHz: '{gpuName}'");
			}

			if (estimatedGpuBase > 0)
			{
				double loadFactor = Math.Min(Math.Max(GpuUsagePercent, 1.0) / 100.0, 1.0);
				GpuCoreClockMhz = estimatedGpuBase * 0.3 + estimatedGpuBase * 0.7 * loadFactor;
				DebugLog($"[CLOCK] GPU estimated: {GpuCoreClockMhz:F0} MHz (base={estimatedGpuBase:F0}, name='{gpuName}', load={GpuUsagePercent:F1}%)");
			}
			else
			{
				DebugLog($"[CLOCK] GPU estimation SKIPPED — nome vazio ou GPU não detectada");
			}
			DebugLog($"[CLOCK] ReadGpuClock FINAL: GpuCoreClockMhz={GpuCoreClockMhz:F0} MHz");
		}
		catch (Exception ex)
		{
			DebugLog($"[CLOCK] GPU clock via nvidia-smi failed: {ex.Message}");
		}
	}

	// -1 = ainda não researches, 0 = não é NVIDIA, 1 = é NVIDIA.
	// A GPU não muda em runtime, então esta é uma consulta por processo, não
	// uma por ciclo. Antes, HasNvidiaGpu() era chamada 2x a cada 5s (uma
	// delas dentro de uma interpolação de log), e cada chamada fazia
	// 2 queries WMI de Win32_VideoController.
	private int _hasNvidiaCache = -1;

	private bool HasNvidiaGpu()
	{
		if (_hasNvidiaCache >= 0)
			return _hasNvidiaCache == 1;

		try
		{
			var results = ExecuteWmiQueryWithValidation("cimv2", "SELECT * FROM Win32_VideoController", "Win32_VideoController");
			if (results != null)
			{
				foreach (ManagementObject obj in results)
				{
					var name = obj["Name"]?.ToString() ?? "";
					if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
					{
						var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe");
						if (File.Exists(path))
						{
							_hasNvidiaCache = 1;
							return true;
						}
						path = @"C:\Windows\System32\nvidia-smi.exe";
						if (File.Exists(path))
						{
							_hasNvidiaCache = 1;
							return true;
						}
					}
				}
			}
		}
		catch { }
		_hasNvidiaCache = 0;
		return false;
	}

	private void ReadGpuClockViaWmi()
	{
		try
		{
			var results = ExecuteWmiQueryWithValidation("cimv2", "SELECT * FROM Win32_VideoController", "Win32_VideoController");
			string gpuName = "";
			if (results != null)
			{
				foreach (ManagementObject obj in results)
				{
					var ram = obj["AdapterRAM"]?.ToString();
					if (ram != null && long.TryParse(ram, out var ramBytes) && ramBytes > 0)
					{
						GpuVramTotalGb = ramBytes / 1073741824.0;
					}
					gpuName = obj["Name"]?.ToString() ?? "";
					DebugLog($"[CLOCK] GPU WMI found: {gpuName}, VRAM total: {GpuVramTotalGb:F1} GB");
					break;
				}
			}

			// Salva o nome para reuso na estimativa (sem nova query WMI)
			if (!string.IsNullOrEmpty(gpuName))
				_lastGpuName = gpuName;

			// Tentar ler clock GPU via WMI Performance Counters (Intel/AMD)
			if (GpuCoreClockMhz <= 0)
			{
				try
				{
					var perfResults = ExecuteWmiQueryWithValidation("cimv2", "SELECT * FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUPerformance", "Win32_PerfFormattedData_GPUPerformanceCounters_GPUPerformance");
					if (perfResults != null)
					{
						foreach (ManagementObject obj in perfResults)
						{
							var freq = obj["CoreFrequency_MHz_"]?.ToString();
							if (freq != null && double.TryParse(freq, System.Globalization.NumberStyles.Float,
								System.Globalization.CultureInfo.InvariantCulture, out var coreMhz) && coreMhz > 0 && coreMhz < 5000)
							{
								GpuCoreClockMhz = coreMhz;
								DebugLog($"[CLOCK] GPU via perf counters: {coreMhz:F0} MHz");
								break;
							}
						}
					}
					if (GpuCoreClockMhz <= 0)
						DebugLog($"[CLOCK] GPU perf counters: query returned no valid clock data");
				}
				catch (Exception pcEx)
				{
					DebugLog($"[CLOCK] GPU perf counters class unavailable: {pcEx.Message}");
				}
			}
		}
		catch (Exception ex)
		{
			DebugLog($"[CLOCK] GPU clock via WMI failed: {ex.Message}");
		}
	}

	private double EstimateGpuClockFromName()
	{
		try
		{
			using var searcher = new ManagementObjectSearcher("SELECT Name, MaxClockSpeed FROM Win32_VideoController");
			foreach (ManagementObject obj in searcher.Get())
			{
                using var __dispose_obj = obj;
				var name = obj["Name"]?.ToString() ?? "";
				// Intel Iris Xe: ~1100MHz base
				if (name.Contains("Iris", StringComparison.OrdinalIgnoreCase))
					return 1100;
				if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase) && name.Contains("UHD", StringComparison.OrdinalIgnoreCase))
					return 1000;
				if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase) && name.Contains("HD", StringComparison.OrdinalIgnoreCase))
					return 900;
				if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
					return 1500;
				if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
					return 1500;
				break;
			}
		}
		catch { }
		return 0;
	}

	private void ReadGcAndThreadPoolMetrics()
	{
		try
		{
			ManagedHeapMb = GC.GetTotalMemory(forceFullCollection: false) / 1024.0 / 1024.0;
			GcGen0Collections = GC.CollectionCount(0);
			GcGen1Collections = GC.CollectionCount(1);
			GcGen2Collections = GC.CollectionCount(2);
			ThreadPool.GetAvailableThreads(out int workerThreads, out int completionPortThreads);
			ThreadPool.GetMaxThreads(out int maxWorkerThreads, out int maxCompletionPortThreads);
			ThreadPoolAvailableThreads = workerThreads;
			ThreadPoolMaxThreads = maxWorkerThreads;
		}
		catch (Exception ex)
		{
			DebugLog($"[METRICS-CACHE] ReadGcAndThreadPoolMetrics error: {ex.Message}");
		}
	}

	private static double ReadAcpiThermalZoneTemp()
	{
		// CORREÇÃO 7: Retry com backoff exponencial para leitura de temperatura
		// Hardware pode falhar nas primeiras tentativas durante inicialização
		const int maxRetries = 3;
		int attempt = 0;
		
		while (attempt < maxRetries)
		{
			try
			{
				// CORREÇÃO 4: Usar helper com validação e timeout
				var results = ExecuteWmiQueryWithValidation("WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature", "MSAcpi_ThermalZoneTemperature");
				if (results == null)
					break;
					
				foreach (ManagementObject item in results)
				{
					using (item)
					{
						double raw = Convert.ToDouble(item["CurrentTemperature"]);
						double celsius = raw / 10.0 - 273.15;
						if (celsius > 0.0 && celsius < 130.0)
							return Math.Round(celsius, 1);
					}
				}
				// Sucesso na primeira tentativa - sai imediatamente
				if (attempt == 0) break;
			}
			catch (Exception ex)
			{
				attempt++;
				if (attempt < maxRetries)
				{
					// Backoff exponencial: 100ms, 200ms, 400ms
					int delayMs = 100 * (1 << attempt);
					System.Threading.Thread.Sleep(delayMs);
				}
				else
				{
					// Última tentativa falhou - log detalhado
					System.Diagnostics.Debug.WriteLine($"[TEMP] ReadAcpiThermalZoneTemp falhou após {maxRetries} tentativas: {ex.Message}");
				}
			}
		}
		return -1.0;
	}

		private static double ReadThermalZonePerfCounter()
		{
			// FONTE REAL de temperatura que NÃO exige elevação.
			//
			// Diagnóstico medido nesta máquina (i5-1135G7 + Iris Xe):
			//   \Thermal Zone Information\_TZ.TZ00\Temperature = 334 K -> 60,9 °C
			//
			// Antes esta função existia mas NUNCA era chamada, e o método devolvia
			// -1.0 (o chamador só aceitava > 0), então mesmo uma leitura válida
			// seria descartada. Aqui devolve double.NaN para "sem leitura", que o
			// chamador distingue de 0 °C.
			//
			// Custo: PerformanceCounterCategoria/NextValue faz uma syscall de alto
			// nível, então o resultado é memorizado por _thermalSourceRetrySeconds
			// e o primeiro NextValue é sempre descartado (o primeiro valor de um
			// contador de taxa é inválido por definição).
			const string CategoryName = "Thermal Zone Information";
			const string CounterName = "Temperature";

			try
			{
				if (!System.Diagnostics.PerformanceCounterCategory.Exists(CategoryName))
					return double.NaN;

				var category = new System.Diagnostics.PerformanceCounterCategory(CategoryName);
				string[] instances;
				try { instances = category.GetInstanceNames(); }
				catch { return double.NaN; }

				double best = double.NaN;
				foreach (var instance in instances)
				{
					// A zona térmica não precisa se chamar "CPU": em notebook é
					// "_TZ.TZ00" e representa a zona ACPI da plataforma, que é o
					// valor térmico mais próximo que existe sem driver. Filtrar por
					// nome (código anterior exigia "TZ"/"CPU"/"Processor") descartava
					// metade das máquinas legítimas.
					System.Diagnostics.PerformanceCounter? counter = null;
					try
					{
						counter = new System.Diagnostics.PerformanceCounter(CategoryName, CounterName, instance, true);
						counter.NextValue();          // primeira leitura inválida (taxa)
						System.Threading.Thread.Sleep(80);
						var kelvin = counter.NextValue();

						if (double.IsNaN(kelvin) || kelvin <= 0) continue;
						var celsius = kelvin - 273.15;
						// Rejeita leituras absurdas: zona térmica não fica em 0 °C nem
						// acima de 125 °C, e valores fora disso indicam contador inativo.
						if (celsius <= 1.0 || celsius >= 125.0) continue;

						if (double.IsNaN(best) || celsius > best) best = celsius;
					}
					catch
					{
						// Instância sem o contador pedido: ignora e tenta a próxima.
					}
					finally { try { counter?.Dispose(); } catch { /* best-effort */ } }
				}

				return double.IsNaN(best) ? double.NaN : Math.Round(best, 1);
			}
			catch
			{
				return double.NaN;
			}
		}

	/// <summary>
	/// Log de nível Info para decisões sobre origem da temperatura.
	/// <see cref="DebugLog"/> usa LogDebug, que fica abaixo do limiar e portanto
	/// não aparece no arquivo de log — mas a escolha entre "sensor real" e
	/// "estimativa" precisa ser VISÍVEL para diagnóstico e suporte.
	/// </summary>
	private void LogTemperatura(string message)
	{
		try
		{
			App.LoggingService?.LogInfo($"[METRICS-CACHE] {message}");
		}
		catch
		{
			// Log nunca pode derrubar a coleta de métricas.
		}
	}

	/// <summary>
	/// Resolve o nome do adaptador GPU de forma independente.
	///
	/// BUG CORRIGIDO: <c>_lastGpuName</c> so era preenchido por um caminho
	/// fragil dentro de <c>ReadGpuClockViaWmi</c>, que depende de um casamento
	/// de WMI e de um <c>break</c> antecipado. Quando o casamento falhava o
	/// nome ficava vazio — e o log de diagnostico saia com "Adaptador=''",
	/// impedindo saber qual GPU estava sem sensor. Como o nome tambem alimenta
	/// heuristicas (integrada vs dedicada), a perda nao era so de log.
	/// </summary>
	private string? _gpuNameResolved;

	private string ResolveGpuName()
	{
		if (_gpuNameResolved != null) return _gpuNameResolved;

		string nome = _lastGpuName;   // reaproveita se o outro caminho jadiscovery

		if (string.IsNullOrEmpty(nome))
		{
			try
			{
				foreach (ManagementObject obj in new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController").Get())
				{
					using (obj)
					{
						string? n = obj["Name"]?.ToString();
						// Preferimos um adaptador real de video, ignorando o
						// "Microsoft Basic Display Adapter" de maquinas sem driver.
						if (!string.IsNullOrWhiteSpace(n)
							&& !n.Contains("Basic Display", StringComparison.OrdinalIgnoreCase)
							&& !n.Contains("Remote Display", StringComparison.OrdinalIgnoreCase))
						{
							nome = n.Trim();
							break;
						}
					}
				}
			}
			catch
			{
				// Sem nome continua funcionando: a estimativa usa o fallback.
			}
		}

		_gpuNameResolved = nome;
		if (!string.IsNullOrEmpty(nome)) _lastGpuName = nome;
		return nome;
	}

	private void DebugLog(string message)
	{
		try
		{
			// Log consolidado no sistema principal de logging
			if (App.LoggingService != null)
			{
				App.LoggingService.LogDebug($"[METRICS-CACHE] {message}");
			}
			else
			{
				// Fallback para arquivo apenas se LoggingService não estiver disponível
				string logDir = LogDirectoryResolver.Resolve();
				Directory.CreateDirectory(logDir);
				File.AppendAllText(Path.Combine(logDir, "metrics_cache_clocks.log"), $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
			}
		}
		catch { }
	}


	[DllImport("kernel32.dll")]
	private static extern bool GetSystemTimes(out long lpIdleTime, out long lpKernelTime, out long lpUserTime);

	[DllImport("kernel32.dll")]
	private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

	[DllImport("user32.dll")]
	private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

	[DllImport("user32.dll")]
	private static extern uint GetGuiResources(IntPtr hProcess, uint uiFlags);
}
