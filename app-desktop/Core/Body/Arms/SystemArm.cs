using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Core.Brain.V2;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Utils;

namespace VoltrisOptimizer.Core.Body.Arms;

public sealed class SystemArm : ISystemArm, IDisposable
{
	private readonly ILoggingService _logger;

	private readonly IBrainExecutor _executor;

	private DateTime _lastGamingModeChange = DateTime.UtcNow.AddMinutes(-5.0);

	private readonly TimeSpan _gamingModeCooldown = TimeSpan.FromMinutes(3.0);

	private uint _previousTimerResolution = 156u;

	private bool _hasActiveTimerResolution = false;

	private const int SystemMemoryListInformation = 80;

	private const int MemoryPurgeStandbyList = 4;

	[DllImport("winmm.dll")]
	private static extern uint timeBeginPeriod(uint uMilliseconds);

	[DllImport("winmm.dll")]
	private static extern uint timeEndPeriod(uint uMilliseconds);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

	private const uint SPI_SETUIEFFECTS = 0x0021;
	private const uint SPIF_UPDATEINIFILE = 0x01;
	private const uint SPIF_SENDCHANGE = 0x02;

	public SystemArm(ILoggingService logger, IBrainExecutor executor)
	{
		_logger = logger ?? throw new ArgumentNullException("logger");
		_executor = executor ?? throw new ArgumentNullException("executor");
	}

	public Task<SystemActionResult> EnableGamingModeAsync(bool enable)
	{
		return Task.Run(delegate
		{
			try
			{
				WorkloadCategory workloadCategory = DetectContext();
				TimeSpan timeSpan = DateTime.UtcNow - _lastGamingModeChange;
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler;
				if (enable && workloadCategory != WorkloadCategory.Game)
				{
					ILoggingService logger = _logger;
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(67, 1);
					defaultInterpolatedStringHandler.AppendLiteral("[ARM - SYSTEM] GamingMode ON BLOQUEADO: contexto = ");
					defaultInterpolatedStringHandler.AppendFormatted(workloadCategory);
					defaultInterpolatedStringHandler.AppendLiteral(" (requer Gaming)");
					logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(27, 1);
					defaultInterpolatedStringHandler.AppendLiteral("contexto = ");
					defaultInterpolatedStringHandler.AppendFormatted(workloadCategory);
					defaultInterpolatedStringHandler.AppendLiteral(" (requer Gaming)");
					SystemActionResult obj = new SystemActionResult
					{
						Success = false,
						GuardBlocked = true,
						GuardReason = defaultInterpolatedStringHandler.ToStringAndClear()
					};
					return obj;
				}
				if (!enable && workloadCategory == WorkloadCategory.Game)
				{
					_logger.LogInfo("[ARM - SYSTEM] GamingMode OFF BLOQUEADO: jogo ativo");
					return new SystemActionResult
					{
						Success = false,
						GuardBlocked = true,
						GuardReason = "jogo ativo"
					};
				}
				if (timeSpan < _gamingModeCooldown)
				{
					if (enable && workloadCategory == WorkloadCategory.Game)
					{
						_lastGamingModeChange = DateTime.UtcNow.AddMinutes(-5.0);
						_logger.LogInfo("[ARM - SYSTEM] GamingMode COOLDOWN RESETADO: jogo ainda ativo, permitindo reativação");
					}
					else
					{
						double totalSeconds2 = (_gamingModeCooldown - timeSpan).TotalSeconds;
						ILoggingService logger2 = _logger;
						defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(48, 1);
						defaultInterpolatedStringHandler.AppendLiteral("[ARM - SYSTEM] GamingMode COOLDOWN: ");
						defaultInterpolatedStringHandler.AppendFormatted(totalSeconds2, "F0");
						defaultInterpolatedStringHandler.AppendLiteral(" s restantes");
						logger2.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
						defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(22, 1);
						defaultInterpolatedStringHandler.AppendLiteral("cooldown: ");
						defaultInterpolatedStringHandler.AppendFormatted(totalSeconds2, "F0");
						defaultInterpolatedStringHandler.AppendLiteral(" s restantes");
						SystemActionResult obj2 = new SystemActionResult
						{
							Success = false,
							GuardBlocked = true,
							GuardReason = defaultInterpolatedStringHandler.ToStringAndClear()
						};
						return obj2;
					}
				}
				_lastGamingModeChange = DateTime.UtcNow;
				ILoggingService logger3 = _logger;
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(41, 2);
				defaultInterpolatedStringHandler.AppendLiteral("[ARM - SYSTEM] GamingMode: ");
				defaultInterpolatedStringHandler.AppendFormatted(enable ? "ON" : "OFF");
				defaultInterpolatedStringHandler.AppendLiteral(" | contexto = ");
				defaultInterpolatedStringHandler.AppendFormatted(workloadCategory);
				logger3.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
				return new SystemActionResult
				{
					Success = true
				};
			}
			catch (Exception ex)
			{
				_logger.LogError("[ARM - SYSTEM] ERRO em EnableGamingModeAsync: " + ex.Message);
				return new SystemActionResult
				{
					Success = false,
					GuardReason = ex.Message
				};
			}
		});
	}

	public Task<bool> SetTimerResolutionAsync(uint ticks)
	{
		return Task.Run(delegate
		{
			try
			{
				if (!IsAdmin())
				{
					_logger.LogWarning("[ARM - SYSTEM] TimerResolution requer admin");
					return false;
				}
				if (_hasActiveTimerResolution)
				{
					timeEndPeriod(_previousTimerResolution);
				}
				uint previousTimerResolution = _previousTimerResolution;
				_previousTimerResolution = ticks;
				timeBeginPeriod(ticks);
				_hasActiveTimerResolution = true;
				double value = (double)ticks * 0.1;
				double value2 = (double)previousTimerResolution * 0.1;
				ILoggingService logger = _logger;
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(61, 3);
				defaultInterpolatedStringHandler.AppendLiteral("[ARM - SYSTEM] TimerResolution: ");
				defaultInterpolatedStringHandler.AppendFormatted(ticks);
				defaultInterpolatedStringHandler.AppendLiteral(" ticks (");
				defaultInterpolatedStringHandler.AppendFormatted(value, "F1");
				defaultInterpolatedStringHandler.AppendLiteral(" ms) | anterior = ");
				defaultInterpolatedStringHandler.AppendFormatted(value2, "F1");
				defaultInterpolatedStringHandler.AppendLiteral(" ms");
				logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
				return true;
			}
			catch (Exception ex)
			{
				_logger.LogError("[ARM - SYSTEM] ERRO em SetTimerResolutionAsync: " + ex.Message);
				return false;
			}
		});
	}

	public Task<bool> RestoreTimerResolutionAsync()
	{
		return Task.Run(delegate
		{
			try
			{
				if (_hasActiveTimerResolution)
				{
					timeEndPeriod(_previousTimerResolution);
					_hasActiveTimerResolution = false;
					_logger.LogInfo("[ARM - SYSTEM] TimerResolution restaurado para o padrão do Windows");
				}
				return true;
			}
			catch (Exception ex)
			{
				_logger.LogError("[ARM - SYSTEM] ERRO em RestoreTimerResolutionAsync: " + ex.Message);
				return false;
			}
		});
	}

	public Task<bool> EnableMmcssGamingAsync(bool enable)
	{
		return Task.Run(delegate
		{
			try
			{
				_logger.LogInfo("[ARM - SYSTEM] MMCSS Gaming: " + (enable ? "ON" : "OFF"));
				return true;
			}
			catch (Exception ex)
			{
				_logger.LogError("[ARM - SYSTEM] ERRO em EnableMmcssGamingAsync: " + ex.Message);
				return false;
			}
		});
	}

	public Task<bool> FlushDnsCacheAsync()
	{
		return Task.Run(delegate
		{
			try
			{
				Stopwatch stopwatch = Stopwatch.StartNew();
				ProcessStartInfo startInfo = new ProcessStartInfo
				{
					FileName = "ipconfig.exe",
					Arguments = "/flushdns",
					UseShellExecute = false,
					CreateNoWindow = true,
					RedirectStandardOutput = true,
					RedirectStandardError = true
				};
				using Process process = Process.Start(startInfo);
				if (process == null)
				{
					_logger.LogError("[ARM - SYSTEM] FlushDns: falha ao iniciar ipconfig");
					return false;
				}
				process.WaitForExit(5000);
				bool flag = process.ExitCode == 0;
				ILoggingService logger = _logger;
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(70, 2);
				defaultInterpolatedStringHandler.AppendLiteral("[ARM - SYSTEM] DNS cache limpo via ipconfig /flushdns | sucesso=");
				defaultInterpolatedStringHandler.AppendFormatted(flag);
				defaultInterpolatedStringHandler.AppendLiteral(" | ");
				defaultInterpolatedStringHandler.AppendFormatted(stopwatch.ElapsedMilliseconds);
				defaultInterpolatedStringHandler.AppendLiteral(" ms");
				logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
				return flag;
			}
			catch (Exception ex)
			{
				_logger.LogError("[ARM - SYSTEM] ERRO em FlushDnsCacheAsync: " + ex.Message);
				return false;
			}
		});
	}

	[DllImport("ntdll.dll", SetLastError = true)]
	private static extern int NtSetSystemInformation(int infoClass, ref int info, int length);

	public Task<long> TrimStandbyListAsync()
	{
		return Task.Run(delegate
		{
			try
			{
				Stopwatch stopwatch = Stopwatch.StartNew();
				if (!IsAdmin())
				{
					_logger.LogInfo("[ARM - SYSTEM] Standby List PULADO: requer admin");
					return 0L;
				}

				// ── HABILITAÇÃO DE PRIVILÉGIO ──────────────────────────────────
				// NtSetSystemInformation(MemoryPurgeStandbyList) exige
				// SeProfileSingleProcessPrivilege ATIVO no token. Ser admin não
				// basta: o UAC entrega o token com os privilégios desligados, e
				// sem reabilitar a chamada volta NTSTATUS PRIVILEGE_NOT_HELD
				// (0xC0000061) e a standby list não é purgada.
				//
				// Mesmo padrão do WinMemoryCleaner, que chama SetIncreasePrivilege
				// antes de cada técnica de memória
				// (ComputerService.OptimizeStandbyList, linha ~269).
				if (!Utils.PrivilegeHelper.EnablePrivilege(Utils.PrivilegeHelper.SE_PROFILE_SINGLE_PROCESS_NAME))
				{
					_logger.LogWarning("[ARM - SYSTEM] Não foi possível ativar SeProfileSingleProcessPrivilege; a purga da Standby List pode falhar.");
				}
				else
				{
					_logger.LogInfo("[ARM - SYSTEM] SeProfileSingleProcessPrivilege ativado.");
				}
				long num = 0L;
				try
				{
					SafePerformanceCounter safePerformanceCounter = new SafePerformanceCounter("Memory", "Standby Cache Normal Priority Bytes");
					num = (long)(safePerformanceCounter.NextValue() / 1024f / 1024f);
					safePerformanceCounter.Dispose();
				}
				catch
				{
				}
				int info = 4;
				int num2 = NtSetSystemInformation(80, ref info, 4);
				long num3 = 0L;
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler;
				if (num2 == 0)
				{
					try
					{
						SafePerformanceCounter safePerformanceCounter2 = new SafePerformanceCounter("Memory", "Standby Cache Normal Priority Bytes");
						long num4 = (long)(safePerformanceCounter2.NextValue() / 1024f / 1024f);
						safePerformanceCounter2.Dispose();
						num3 = Math.Max(0L, num - num4);
					}
					catch
					{
						num3 = num;
					}
				}
				else
				{
					ILoggingService logger = _logger;
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(49, 1);
					defaultInterpolatedStringHandler.AppendLiteral("[ARM - SYSTEM] NtSetSystemInformation retornou 0x");
					defaultInterpolatedStringHandler.AppendFormatted(num2, "X8");
					logger.LogWarning(defaultInterpolatedStringHandler.ToStringAndClear());
				}
				ILoggingService logger2 = _logger;
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(49, 2);
				defaultInterpolatedStringHandler.AppendLiteral("[ARM - SYSTEM] Standby List: ");
				defaultInterpolatedStringHandler.AppendFormatted(num3);
				defaultInterpolatedStringHandler.AppendLiteral(" MB liberados em ");
				defaultInterpolatedStringHandler.AppendFormatted(stopwatch.ElapsedMilliseconds);
				defaultInterpolatedStringHandler.AppendLiteral(" ms");
				logger2.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
				return num3;
			}
			catch (Exception ex)
			{
				_logger.LogError("[ARM - SYSTEM] ERRO em TrimStandbyListAsync: " + ex.Message);
				return 0L;
			}
		});
	}

	private static WorkloadCategory DetectContext()
	{
		// [FIX P0] Detecção de contexto REAL (antes retornava sempre Game, o que impedia
		// o GamingMode OFF e tornava os guards de contexto inúteis).
		// Lógica: janela em foreground -> jogo conhecido? -> Game.
		// Caso contrário, se o sistema está renderizando frames com GPU/CPU alta -> Game.
		// Senão -> Work se CPU estiver exigida, senão Idle.
		try
		{
			int pid = ForegroundWindowTracker.Instance.CurrentPid;
			if (pid <= 4)
			{
				return WorkloadCategory.Idle;
			}

			string processName;
			try
			{
				using Process p = Process.GetProcessById(pid);
				processName = p.ProcessName.ToLowerInvariant();
			}
			catch (Exception)
			{
				return WorkloadCategory.Idle;
			}

			if (BrainActionExecutorV2.IsKnownGame(processName))
			{
				return WorkloadCategory.Game;
			}

			SystemMetricsCache metrics = SystemMetricsCache.Instance;
			if (metrics.FpsAvailable && (metrics.GpuUsagePercent >= 50.0 || metrics.CpuPercent >= 60.0))
			{
				return WorkloadCategory.Game;
			}

			return metrics.CpuPercent >= 40.0 ? WorkloadCategory.Work : WorkloadCategory.Idle;
		}
		catch (Exception)
		{
			return WorkloadCategory.Idle;
		}
	}

	private static bool IsAdmin()
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

	// P1: Registro genérico com rollback
	public Task<SystemActionResult> SetRegistryValueAsync(string keyPath, string valueName, object value, RegistryValueKind kind, bool requireAdmin = true, string? rollbackDescription = null)
	{
		return Task.Run(() =>
		{
			try
			{
				if (requireAdmin && !IsAdmin())
				{
					_logger.LogWarning($"[ARM-SYSTEM] Registro BLOQUEADO (requer admin): {keyPath}\\{valueName}");
					return new SystemActionResult { Success = false, GuardBlocked = true, GuardReason = "Requer privilégio de administrador" };
				}

using var key = Registry.LocalMachine.OpenSubKey(keyPath, true) ?? Registry.CurrentUser.OpenSubKey(keyPath, true);
			if (key == null)
			{
				_logger.LogError($"[ARM-SYSTEM] Chave não encontrada: {keyPath}");
				return new SystemActionResult { Success = false, GuardReason = "Chave de registro não encontrada" };
			}

			object? oldValue = key.GetValue(valueName);
			key.SetValue(valueName, value, kind);

			_logger.LogSuccess($"[ARM-SYSTEM] ✅ Registro aplicado: {keyPath}\\{valueName} = {value} ({kind}) {rollbackDescription ?? ""} | OldValue={oldValue}");
			return new SystemActionResult { Success = true };
			}
			catch (Exception ex)
			{
				_logger.LogError($"[ARM-SYSTEM] ERRO em SetRegistryValueAsync: {ex.Message}", ex);
				return new SystemActionResult { Success = false, GuardReason = ex.Message };
			}
		});
	}

	// P1: Visual Effects "Best Performance"
	public Task<SystemActionResult> SetVisualEffectsAsync(bool bestPerformance)
	{
		return Task.Run(() =>
		{
			try
			{
				if (!IsAdmin())
				{
					_logger.LogWarning("[ARM-SYSTEM] VisualEffects BLOQUEADO (requer admin)");
					return new SystemActionResult { Success = false, GuardBlocked = true, GuardReason = "Requer admin" };
				}

				_logger.LogInfo($"[ARM-SYSTEM] VisualEffects: {(bestPerformance ? "Best Performance" : "Default")}");

				// 1. SystemParametersInfo para efeito imediato
				uint action = SPI_SETUIEFFECTS;
				uint param = bestPerformance ? 0u : 1u;
				uint flags = SPIF_UPDATEINIFILE | SPIF_SENDCHANGE;
				SystemParametersInfo(SPI_SETUIEFFECTS, param, IntPtr.Zero, flags);

				// 2. Registry para persistência
				using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", true);
				if (key != null)
				{
					key.SetValue("VisualFXSetting", bestPerformance ? 2 : 0, RegistryValueKind.DWord); // 2=Custom/BestPerf, 0=Let Windows choose
				}

				using var keyAdv = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", true);
				if (keyAdv != null)
				{
					keyAdv.SetValue("TaskbarAnimations", bestPerformance ? 0 : 1, RegistryValueKind.DWord);
				}

				using var keyDesk = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true);
				if (keyDesk != null)
				{
					// UserPreferencesMask para "Adjust for best performance"
					byte[] mask = bestPerformance
						? new byte[] { 0x90, 0x12, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }
						: new byte[] { 0x9E, 0x3E, 0x05, 0x80, 0x12, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
					keyDesk.SetValue("UserPreferencesMask", mask, RegistryValueKind.Binary);
				}

				_logger.LogSuccess($"[ARM-SYSTEM] ✅ VisualEffects aplicado: {(bestPerformance ? "Best Performance" : "Default")} | VisualFXSetting={(bestPerformance ? 2 : 0)} TaskbarAnimations={(bestPerformance ? 0 : 1)}");
				return new SystemActionResult { Success = true };
			}
			catch (Exception ex)
			{
				_logger.LogError($"[ARM-SYSTEM] ERRO em SetVisualEffectsAsync: {ex.Message}", ex);
				return new SystemActionResult { Success = false, GuardReason = ex.Message };
			}
		});
	}

	// P1: Game DVR / Background Recording
	public Task<SystemActionResult> SetGameDvrAsync(bool enable)
	{
		return Task.Run(() =>
		{
			try
			{
				_logger.LogInfo($"[ARM-SYSTEM] GameDVR: {(enable ? "ON" : "OFF")}");

				// HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR
				using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\GameDVR"))
				{
					key.SetValue("AppCaptureEnabled", enable ? 1 : 0, RegistryValueKind.DWord);
					key.SetValue("GameDVR_Enabled", enable ? 1 : 0, RegistryValueKind.DWord);
				}

				// HKCU\System\GameConfigStore
				using (var key = Registry.CurrentUser.CreateSubKey(@"System\GameConfigStore"))
				{
					key.SetValue("GameDVR_Enabled", enable ? 1 : 0, RegistryValueKind.DWord);
				}

				// HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR (policy level, requer admin)
				if (IsAdmin())
				{
					using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\GameDVR"))
					{
						key.SetValue("AllowGameDVR", enable ? 1 : 0, RegistryValueKind.DWord);
					}
				}

				_logger.LogSuccess($"[ARM-SYSTEM] ✅ GameDVR {(enable ? "ativado" : "desativado")} | AppCaptureEnabled={enable} GameDVR_Enabled={enable} PolicyAllowGameDVR={IsAdmin()}");
				return new SystemActionResult { Success = true };
			}
			catch (Exception ex)
			{
				_logger.LogError($"[ARM-SYSTEM] ERRO em SetGameDvrAsync: {ex.Message}", ex);
				return new SystemActionResult { Success = false, GuardReason = ex.Message };
			}
		});
	}

	// P1: HAGS (Hardware-Accelerated GPU Scheduling) - stub seguro
	public Task<SystemActionResult> SetHagsAsync(bool enable)
	{
		return Task.Run(() =>
		{
			try
			{
				if (!IsAdmin())
				{
					_logger.LogWarning("[ARM-SYSTEM] HAGS BLOQUEADO (requer admin + reboot)");
					return new SystemActionResult { Success = false, GuardBlocked = true, GuardReason = "Requer admin + reboot" };
				}

				_logger.LogInfo($"[ARM-SYSTEM] HAGS: {(enable ? "ON" : "OFF")} (requer reboot)");

				using (var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers"))
				{
					key.SetValue("HwSchMode", enable ? 2 : 1, RegistryValueKind.DWord); // 2=On, 1=Off
				}

				_logger.LogSuccess($"[ARM-SYSTEM] ✅ HAGS {(enable ? "ativado" : "desativado")} (HwSchMode={(enable ? 2 : 1)}) — REQUER REBOOT");
				return new SystemActionResult { Success = true, GuardReason = "Requer reinicialização para efeito" };
			}
			catch (Exception ex)
			{
				_logger.LogError($"[ARM-SYSTEM] ERRO em SetHagsAsync: {ex.Message}", ex);
				return new SystemActionResult { Success = false, GuardReason = ex.Message };
			}
		});
	}

	public void Dispose()
	{
		try
		{
			if (_hasActiveTimerResolution)
			{
				timeEndPeriod(_previousTimerResolution);
				_hasActiveTimerResolution = false;
			}
		}
		catch
		{
		}
	}
}
