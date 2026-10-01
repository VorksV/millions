using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Core.Intelligence;

public sealed class ActiveBackgroundDirector : IActiveBackgroundDirector, IAutoStartService, IDisposable
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

	private class SuppressedProcessInfo
	{
		public int Pid { get; set; }

		public string Name { get; set; } = string.Empty;


		public uint OriginalPriority { get; set; }

		public IntPtr OriginalAffinity { get; set; }

		public bool IoSuppressed { get; set; }
	}

	private readonly ILoggingService _logger;

	private readonly SettingsService _settingsService;

	private const int SystemMemoryListInformation = 80;

	private const int MemoryPurgeStandbyList = 4;

	private const uint PROCESS_SET_INFORMATION = 512u;

	private const uint PROCESS_QUERY_INFORMATION = 1024u;

	private const uint IDLE_PRIORITY_CLASS = 64u;

	private const uint NORMAL_PRIORITY_CLASS = 32u;

	private const uint PROCESS_MODE_BACKGROUND_BEGIN = 1048576u;

	private const uint PROCESS_MODE_BACKGROUND_END = 2097152u;

	private CancellationTokenSource? _cts;

	private bool _isGamingOptimizationsActive = false;

	private readonly object _lock = new object();

	private string _lastForegroundProcess = string.Empty;

	private DateTime _lastForegroundChange = DateTime.MinValue;

	private string _confirmedForegroundProcess = string.Empty;

	private bool _isTimerSet = false;

	private DateTime _lastMemoryFlush = DateTime.MinValue;

	private Task? _memoryMonitorTask;

	private readonly ConcurrentDictionary<int, SuppressedProcessInfo> _suppressedProcesses = new ConcurrentDictionary<int, SuppressedProcessInfo>();

	private static readonly HashSet<string> _backgroundProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"SearchIndexer", "msedge", "chrome", "firefox", "brave",
		"GoogleUpdate", "MicrosoftEdgeUpdate", "OneDrive", "Dropbox", "BackupService", "svchost_wu",
		"TiWorker", "Discord", "Spotify"
	};

	private static readonly HashSet<string> _ioIntensiveProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "TiWorker", "SearchIndexer", "OneDrive", "Dropbox", "BackupService", "wuauclt", "MsMpEng" };

	private static readonly HashSet<string> _securityBlacklist = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "csrss", "lsass", "winlogon", "explorer", "dwm", "services", "System", "Idle", "svchost", "VoltrisOptimizer" };

	private static readonly HashSet<string> _knownHeavyApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"cs2", "valorant", "fortnite", "apex", "overwatch", "cod", "warzone", "gta5", "lol", "dota2",
		"pubg", "minecraft", "tarkov", "rainbow6", "cyberpunk2077", "witcher3", "eldenring", "rdr2", "premiere", "afterfx",
		"resolve", "vegas", "obs64", "blender"
	};

	public CpuTopologyInfo DetectedTopology { get; private set; } = new CpuTopologyInfo();


	public long MemoryFreedMbSession { get; private set; } = 0L;


	public bool IsActive => _isGamingOptimizationsActive;

	public int SuppressedProcessCount => _suppressedProcesses.Count;

	public event EventHandler<BackgroundDirectorEventArgs>? OnActionTaken;

	public event EventHandler<string>? OnForegroundContextChanged;

	[DllImport("ntdll.dll")]
	private static extern int NtSetTimerResolution(uint DesiredResolution, bool SetResolution, ref uint CurrentResolution);

	[DllImport("ntdll.dll")]
	private static extern int NtQueryTimerResolution(ref uint MinimumResolution, ref uint MaximumResolution, ref uint CurrentResolution);

	[DllImport("ntdll.dll")]
	private static extern int NtSetSystemInformation(int SystemInformationClass, IntPtr SystemInformation, int SystemInformationLength);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool CloseHandle(IntPtr hObject);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool SetPriorityClass(IntPtr handle, uint priorityClass);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern uint GetPriorityClass(IntPtr handle);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool SetProcessAffinityMask(IntPtr hProcess, IntPtr dwProcessAffinityMask);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool GetProcessAffinityMask(IntPtr hProcess, out IntPtr lpProcessAffinityMask, out IntPtr lpSystemAffinityMask);

	public ActiveBackgroundDirector(ILoggingService logger, SettingsService settingsService)
	{
		_logger = logger ?? throw new ArgumentNullException("logger");
		_settingsService = settingsService ?? throw new ArgumentNullException("settingsService");
		DetectCpuTopology();
	}

	private void LogAction(string message)
	{
		_logger.LogInfo(message);
		this.OnActionTaken?.Invoke(this, new BackgroundDirectorEventArgs
		{
			Summary = message
		});
	}

	private void DetectCpuTopology()
	{
		try
		{
			int processorCount = Environment.ProcessorCount;
			if (processorCount <= 2)
			{
				DetectedTopology = new CpuTopologyInfo
				{
					PCoreIndices = new int[2] { 0, 1 },
					ECoreIndices = new int[2] { 0, 1 },
					PCoreMask = 3L,
					ECoreMask = 3L,
					HasHeterogeneousCores = false
				};
				return;
			}

			int gameCoreCount = Math.Max(1, processorCount / 2);
			int systemCoreCount = processorCount - gameCoreCount;

			List<int> gameCores = new List<int>();
			List<int> systemCores = new List<int>();
			long gameMask = 0L;
			long systemMask = 0L;

			for (int i = 0; i < processorCount; i++)
			{
				if (i >= systemCoreCount)
				{
					gameCores.Add(i);
					gameMask |= 1L << i;
				}
				else
				{
					systemCores.Add(i);
					systemMask |= 1L << i;
				}
			}

			DetectedTopology = new CpuTopologyInfo
			{
				PCoreIndices = gameCores.ToArray(),
				ECoreIndices = systemCores.ToArray(),
				PCoreMask = gameMask,
				ECoreMask = systemMask,
				HasHeterogeneousCores = false
			};

			LogAction($"[ABD] Topologia CPU: Game-Cores=[{string.Join(",", gameCores)}] System-Cores=[{string.Join(",", systemCores)}] | split={gameCoreCount}+{systemCoreCount}");
		}
		catch (Exception ex)
		{
			_logger.LogError("[ABD] Falha ao detectar topologia: " + ex.Message);
		}
	}

	public Task StartAsync(CancellationToken ct)
	{
		_cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		ForegroundWindowTracker.Instance.Start();
		ForegroundWindowTracker.Instance.ForegroundChanged += OnForegroundChanged;
		LogAction("[ABD] ForegroundWindowTracker subscrito");
		_memoryMonitorTask = Task.Run(() => MemoryMonitorLoop(_cts!.Token), _cts!.Token);
		return Task.CompletedTask;
	}

	public Task StopAsync()
	{
		_cts?.Cancel();
		ForegroundWindowTracker.Instance.ForegroundChanged -= OnForegroundChanged;
		LogAction("[ABD] ForegroundWindowTracker desinscrito");
		RestoreAllAsync();
		return Task.CompletedTask;
	}

	private void OnForegroundChanged(object? sender, int pid)
	{
            try
            {
                if (pid <= 4)
                {
                    return;
                }
                string procName = null;
                try
                {
                    using var proc = Process.GetProcessById(pid);
                    if (proc != null)
                    {
                        procName = proc.ProcessName;
                    }
                }
                catch
                {
                    procName = null;
                }
                if (string.IsNullOrEmpty(procName) || procName == _lastForegroundProcess)
                {
                    return;
                }
                _lastForegroundProcess = procName;
                _lastForegroundChange = DateTime.UtcNow;
                string category = (_knownHeavyApps.Contains(procName) ? "Heavy" : "Light/Unknown");
                _logger.LogDebug("[ABD] Foreground mudou: → " + procName + " | debounce=3s | aguardando confirmação");
                Task.Delay(3000).ContinueWith(_ =>
                {
                    if (_lastForegroundProcess == procName && (DateTime.UtcNow - _lastForegroundChange).TotalSeconds >= 2.9)
                    {
                        _confirmedForegroundProcess = procName;
                        LogAction("[ABD] Foreground confirmado após 3s: processo=" + procName + " | categoria=" + category);
                        this.OnForegroundContextChanged?.Invoke(this, procName);
                    }
                });
            }
            catch (Exception)
            {
            }
	}

	public Task ApplyGamingOptimizationsAsync(string process)
	{
		string process2 = process;
		_logger.LogInfo("[ABD] Recebida solicitação ApplyGamingOptimizationsAsync para processo: '" + process2 + "'");
		return Task.Run(delegate
		{
			lock (_lock)
			{
				_logger.LogDebug("[ABD] Lock obtido em ApplyGamingOptimizationsAsync");
				_isGamingOptimizationsActive = true;
				ApplyBackgroundSuppression(process2);
				SetGamingTimerResolution();
				_logger.LogDebug("[ABD] ApplyGamingOptimizationsAsync finalizado com sucesso.");
			}
		});
	}

	public Task RestoreAllAsync()
	{
		_logger.LogInfo("[ABD] Recebida solicitação RestoreAllAsync");
		return Task.Run(delegate
		{
			lock (_lock)
			{
				_logger.LogDebug("[ABD] Lock obtido em RestoreAllAsync");
				RestoreBackgroundProcesses();
				RestoreTimerResolution();
				_isGamingOptimizationsActive = false;
				_logger.LogDebug("[ABD] RestoreAllAsync finalizado com sucesso.");
			}
		});
	}

	private bool IsEnterpriseMode()
	{
		return _settingsService.Settings.IntelligentProfile == IntelligentProfileType.EnterpriseSecure;
	}

	private void ApplyBackgroundSuppression(string foregroundApp)
	{
		string foregroundApp2 = foregroundApp;
		try
		{
			int num = 0;
			Process[] processes = Process.GetProcesses();
			bool flag = IsEnterpriseMode();
			if (flag)
			{
				LogAction("[ABD] EnterpriseMode ATIVO: proteções corporativas aplicadas");
			}
			IEnumerable<Process> enumerable = processes.Where((Process p) => !_securityBlacklist.Contains(p.ProcessName) && !string.Equals(p.ProcessName, foregroundApp2, StringComparison.OrdinalIgnoreCase) && (_backgroundProcesses.Contains(p.ProcessName) || _ioIntensiveProcesses.Contains(p.ProcessName)));
			List<string> list = new List<string>();
			foreach (Process item in enumerable)
			{
				ILoggingService logger = _logger;
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(49, 2);
				defaultInterpolatedStringHandler.AppendLiteral("[ABD] Avaliando processo para supressão: ");
				defaultInterpolatedStringHandler.AppendFormatted(item.ProcessName);
				defaultInterpolatedStringHandler.AppendLiteral(" (PID: ");
				defaultInterpolatedStringHandler.AppendFormatted(item.Id);
				defaultInterpolatedStringHandler.AppendLiteral(")");
				logger.LogDebug(defaultInterpolatedStringHandler.ToStringAndClear());
				if (flag && (item.ProcessName.Equals("MsMpEng", StringComparison.OrdinalIgnoreCase) || item.ProcessName.Equals("wuauserv", StringComparison.OrdinalIgnoreCase) || item.ProcessName.Equals("TiWorker", StringComparison.OrdinalIgnoreCase)))
				{
					_logger.LogDebug("[ABD] EnterpriseMode: Pulando processo crítico de segurança/update " + item.ProcessName);
					continue;
				}
				try
				{
					IntPtr intPtr = OpenProcess(1536u, bInheritHandle: false, item.Id);
					if (intPtr == IntPtr.Zero)
					{
						continue;
					}
					SuppressedProcessInfo suppressedProcessInfo = new SuppressedProcessInfo
					{
						Pid = item.Id,
						Name = item.ProcessName
					};
					bool flag2 = false;
					if (_backgroundProcesses.Contains(item.ProcessName))
					{
						suppressedProcessInfo.OriginalPriority = GetPriorityClass(intPtr);
						if (suppressedProcessInfo.OriginalPriority != 64)
						{
							SetPriorityClass(intPtr, 64u);
							flag2 = true;
						}
						if (GetProcessAffinityMask(intPtr, out var lpProcessAffinityMask, out var _))
						{
							suppressedProcessInfo.OriginalAffinity = lpProcessAffinityMask;
							SetProcessAffinityMask(intPtr, new IntPtr(DetectedTopology.ECoreMask));
						}
					}
					if (_ioIntensiveProcesses.Contains(item.ProcessName))
					{
						SetPriorityClass(intPtr, 1048576u);
						suppressedProcessInfo.IoSuppressed = true;
						flag2 = true;
						_logger.LogDebug("[ABD] IO suprimido: " + item.ProcessName + " | IO=Normal→Background");
					}
					if (flag2 && !_suppressedProcesses.ContainsKey(item.Id))
					{
						_suppressedProcesses.TryAdd(item.Id, suppressedProcessInfo);
						list.Add(item.ProcessName);
						num++;
						ILoggingService logger2 = _logger;
						defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(73, 3);
						defaultInterpolatedStringHandler.AppendLiteral("[ABD] Processo ");
						defaultInterpolatedStringHandler.AppendFormatted(item.ProcessName);
						defaultInterpolatedStringHandler.AppendLiteral(": prio=Normal→Idle | IO=Normal→Background | Affinity=0x");
						defaultInterpolatedStringHandler.AppendFormatted(suppressedProcessInfo.OriginalAffinity.ToInt64(), "X");
						defaultInterpolatedStringHandler.AppendLiteral("→0x");
						defaultInterpolatedStringHandler.AppendFormatted(DetectedTopology.ECoreMask, "X");
						logger2.LogDebug(defaultInterpolatedStringHandler.ToStringAndClear());
					}
					CloseHandle(intPtr);
				}
				catch (Exception ex)
				{
					ILoggingService logger3 = _logger;
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(50, 3);
					defaultInterpolatedStringHandler.AppendLiteral("[ABD] Acesso negado ou erro ao suprimir ");
					defaultInterpolatedStringHandler.AppendFormatted(item.ProcessName);
					defaultInterpolatedStringHandler.AppendLiteral(" (PID: ");
					defaultInterpolatedStringHandler.AppendFormatted(item.Id);
					defaultInterpolatedStringHandler.AppendLiteral("): ");
					defaultInterpolatedStringHandler.AppendFormatted(ex.Message);
					logger3.LogDebug(defaultInterpolatedStringHandler.ToStringAndClear());
				}
			}
			if (num > 0)
			{
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(46, 2);
				defaultInterpolatedStringHandler.AppendLiteral("[ABD] Suprimindo ");
				defaultInterpolatedStringHandler.AppendFormatted(num);
				defaultInterpolatedStringHandler.AppendLiteral(" processos para Gaming: [");
				defaultInterpolatedStringHandler.AppendFormatted(string.Join(", ", list.Take(5)));
				defaultInterpolatedStringHandler.AppendLiteral("...]");
				LogAction(defaultInterpolatedStringHandler.ToStringAndClear());
			}
			_logger.LogDebug("[ABD] PULADO (blacklist): csrss, lsass, etc");
			Process[] array = processes;
			foreach (Process process in array)
			{
				process.Dispose();
			}
		}
		catch (Exception ex2)
		{
			_logger.LogError("[ABD] Erro em ApplyBackgroundSuppression: " + ex2.Message);
		}
	}

	private void RestoreBackgroundProcesses()
	{
		try
		{
			int num = 0;
			foreach (KeyValuePair<int, SuppressedProcessInfo> suppressedProcess in _suppressedProcesses)
			{
				try
				{
					SuppressedProcessInfo value = suppressedProcess.Value;
					IntPtr intPtr = OpenProcess(1536u, bInheritHandle: false, value.Pid);
					if (intPtr != IntPtr.Zero)
					{
						if (value.OriginalPriority != 0)
						{
							SetPriorityClass(intPtr, value.OriginalPriority);
						}
						if (value.OriginalAffinity != IntPtr.Zero)
						{
							SetProcessAffinityMask(intPtr, value.OriginalAffinity);
						}
						if (value.IoSuppressed)
						{
							SetPriorityClass(intPtr, 2097152u);
						}
						ILoggingService logger = _logger;
						DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(56, 3);
						defaultInterpolatedStringHandler.AppendLiteral("[ABD] Processo ");
						defaultInterpolatedStringHandler.AppendFormatted(value.Name);
						defaultInterpolatedStringHandler.AppendLiteral(" restaurado: Idle→Normal | Affinity=0x");
						defaultInterpolatedStringHandler.AppendFormatted(DetectedTopology.ECoreMask, "X");
						defaultInterpolatedStringHandler.AppendLiteral("→0x");
						defaultInterpolatedStringHandler.AppendFormatted(value.OriginalAffinity.ToInt64(), "X");
						logger.LogDebug(defaultInterpolatedStringHandler.ToStringAndClear());
						CloseHandle(intPtr);
						num++;
					}
					else
					{
						ILoggingService logger2 = _logger;
						DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(58, 2);
						defaultInterpolatedStringHandler.AppendLiteral("[ABD] Restore: Não foi possível obter handle para ");
						defaultInterpolatedStringHandler.AppendFormatted(value.Name);
						defaultInterpolatedStringHandler.AppendLiteral(" (PID: ");
						defaultInterpolatedStringHandler.AppendFormatted(value.Pid);
						defaultInterpolatedStringHandler.AppendLiteral(")");
						logger2.LogDebug(defaultInterpolatedStringHandler.ToStringAndClear());
					}
				}
				catch (Exception ex)
				{
					ILoggingService logger3 = _logger;
					DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(81, 3);
					defaultInterpolatedStringHandler.AppendLiteral("[ABD] Restore: Processo ");
					defaultInterpolatedStringHandler.AppendFormatted(suppressedProcess.Value.Name);
					defaultInterpolatedStringHandler.AppendLiteral(" (PID: ");
					defaultInterpolatedStringHandler.AppendFormatted(suppressedProcess.Value.Pid);
					defaultInterpolatedStringHandler.AppendLiteral(") pode ter sido encerrado ou acesso negado. Erro: ");
					defaultInterpolatedStringHandler.AppendFormatted(ex.Message);
					logger3.LogDebug(defaultInterpolatedStringHandler.ToStringAndClear());
				}
			}
			_suppressedProcesses.Clear();
			if (num > 0)
			{
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(47, 1);
				defaultInterpolatedStringHandler.AppendLiteral("[ABD] ");
				defaultInterpolatedStringHandler.AppendFormatted(num);
				defaultInterpolatedStringHandler.AppendLiteral(" processos restaurados ao estado original");
				LogAction(defaultInterpolatedStringHandler.ToStringAndClear());
				ILoggingService logger4 = _logger;
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(31, 1);
				defaultInterpolatedStringHandler.AppendLiteral("[ABD] IO restaurado: ");
				defaultInterpolatedStringHandler.AppendFormatted(num);
				defaultInterpolatedStringHandler.AppendLiteral(" processos");
				logger4.LogDebug(defaultInterpolatedStringHandler.ToStringAndClear());
			}
		}
		catch (Exception ex2)
		{
			_logger.LogError("[ABD] Erro em RestoreBackgroundProcesses: " + ex2.Message);
		}
	}

	private void SetGamingTimerResolution()
	{
		if (_isTimerSet)
		{
			return;
		}
		try
		{
			string s = (string)Registry.GetValue("HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion", "CurrentBuild", "");
			int.TryParse(s, out var result);
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler;
			if (result >= 22621)
			{
				ILoggingService logger = _logger;
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(87, 1);
				defaultInterpolatedStringHandler.AppendLiteral("[ABD] Windows 11 22H2+ (Build ");
				defaultInterpolatedStringHandler.AppendFormatted(result);
				defaultInterpolatedStringHandler.AppendLiteral("): timer resolution per-process | efeito local ao Voltris");
				logger.LogDebug(defaultInterpolatedStringHandler.ToStringAndClear());
			}
			else
			{
				ILoggingService logger2 = _logger;
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(85, 1);
				defaultInterpolatedStringHandler.AppendLiteral("[ABD] Windows 10/11 pre-22H2 (Build ");
				defaultInterpolatedStringHandler.AppendFormatted(result);
				defaultInterpolatedStringHandler.AppendLiteral("): timer resolution global | afeta todo o sistema");
				logger2.LogDebug(defaultInterpolatedStringHandler.ToStringAndClear());
			}
			uint CurrentResolution = 0u;
			NtSetTimerResolution(5000u, SetResolution: true, ref CurrentResolution);
			_isTimerSet = true;
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(66, 1);
			defaultInterpolatedStringHandler.AppendLiteral("[ABD] TimerResolution: 156000→5000 (15.6ms→0.5ms) | Windows Build=");
			defaultInterpolatedStringHandler.AppendFormatted(result);
			LogAction(defaultInterpolatedStringHandler.ToStringAndClear());
		}
		catch (Exception ex)
		{
			_logger.LogError("[ABD] Erro SetGamingTimerResolution: " + ex.Message);
		}
	}

	private void RestoreTimerResolution()
	{
		if (!_isTimerSet)
		{
			return;
		}
		try
		{
			uint CurrentResolution = 0u;
			NtSetTimerResolution(156001u, SetResolution: false, ref CurrentResolution);
			_isTimerSet = false;
			LogAction("[ABD] TimerResolution restaurado: 5000→156000");
		}
		catch (Exception ex)
		{
			_logger.LogError("[ABD] Erro RestoreTimerResolution: " + ex.Message);
		}
	}

	private static bool IsRunningAsAdmin()
	{
		try
		{
			using WindowsIdentity ntIdentity = WindowsIdentity.GetCurrent();
			WindowsPrincipal windowsPrincipal = new WindowsPrincipal(ntIdentity);
			return windowsPrincipal.IsInRole(WindowsBuiltInRole.Administrator);
		}
		catch
		{
			return false;
		}
	}

	private async Task MemoryMonitorLoop(CancellationToken ct)
	{
		double previousGameMemoryMb = 0.0;
		while (!ct.IsCancellationRequested)
		{
			try
			{
				if (_isGamingOptimizationsActive && IsRunningAsAdmin())
				{
					MEMORYSTATUSEX memStatus = default(MEMORYSTATUSEX);
					memStatus.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
					if (GlobalMemoryStatusEx(ref memStatus))
					{
						double freeRamPercent = (double)memStatus.ullAvailPhys / (double)memStatus.ullTotalPhys * 100.0;
						double freeRamMbBefore = (double)memStatus.ullAvailPhys / 1024.0 / 1024.0;
						double currentGameMemoryMb = 0.0;
						try
						{
							if (!string.IsNullOrEmpty(_confirmedForegroundProcess))
							{
								Process[] procs = Process.GetProcessesByName(_confirmedForegroundProcess);
								if (procs.Length != 0)
								{
									currentGameMemoryMb = (double)procs[0].WorkingSet64 / 1024.0 / 1024.0;
								}
							}
						}
						catch
						{
						}
						double growth = currentGameMemoryMb - previousGameMemoryMb;
						previousGameMemoryMb = currentGameMemoryMb;
						if (freeRamPercent < 5.0 && growth > 50.0)
						{
							double elapsedSinceFlush = (DateTime.UtcNow - _lastMemoryFlush).TotalSeconds;
							if (elapsedSinceFlush > 120.0)
							{
								ActiveBackgroundDirector activeBackgroundDirector = this;
								DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(61, 2);
								defaultInterpolatedStringHandler.AppendLiteral("[ABD] Memory flush bloqueado: Função de flush agressivo foi desativada para evitar Hard Page Faults e stutters.");
								activeBackgroundDirector.LogAction(defaultInterpolatedStringHandler.ToStringAndClear());
								// Stopwatch sw = Stopwatch.StartNew();
								// IntPtr ptr = Marshal.AllocHGlobal(4);
								// Marshal.WriteInt32(ptr, 4);
								// NtSetSystemInformation(80, ptr, 4);
								// Marshal.FreeHGlobal(ptr);
								// sw.Stop();
								_lastMemoryFlush = DateTime.UtcNow;
								if (GlobalMemoryStatusEx(ref memStatus))
								{
									double freeRamMbAfter = (double)memStatus.ullAvailPhys / 1024.0 / 1024.0;
									double freed = Math.Max(0.0, freeRamMbAfter - freeRamMbBefore);
									MemoryFreedMbSession += (long)freed;
									ActiveBackgroundDirector activeBackgroundDirector2 = this;
									defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(67, 3);
									defaultInterpolatedStringHandler.AppendLiteral("[ABD] Memory flush simulado concluído: FreeRAM: ");
									defaultInterpolatedStringHandler.AppendFormatted(freeRamMbBefore, "F0");
									defaultInterpolatedStringHandler.AppendLiteral("MB → ");
									defaultInterpolatedStringHandler.AppendFormatted(freeRamMbAfter, "F0");
									defaultInterpolatedStringHandler.AppendLiteral("MB (");
									defaultInterpolatedStringHandler.AppendFormatted(freed, "F0");
									defaultInterpolatedStringHandler.AppendLiteral("MB avaliados)");
									activeBackgroundDirector2.LogAction(defaultInterpolatedStringHandler.ToStringAndClear());
								}
							}
							else
							{
								ILoggingService logger = _logger;
								DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(58, 1);
								defaultInterpolatedStringHandler.AppendLiteral("[ABD] Memory flush bloqueado: cooldown ativo (");
								defaultInterpolatedStringHandler.AppendFormatted(120.0 - elapsedSinceFlush, "F0");
								defaultInterpolatedStringHandler.AppendLiteral("s restantes)");
								logger.LogDebug(defaultInterpolatedStringHandler.ToStringAndClear());
							}
						}
						else if (!(freeRamPercent >= 5.0))
						{
						}
					}
				}
			}
			catch (Exception ex)
			{
				_logger.LogError("[ABD] Erro no MemoryMonitorLoop: " + ex.Message);
			}
			await Task.Delay(5000, ct);
		}
	}

	public void Dispose()
	{
		StopAsync().Wait(2000);
		_cts?.Dispose();
	}
}
