using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Core.Brain.V2;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Power;

namespace VoltrisOptimizer.Core.Body;

public sealed class VoltrisSpine : IVoltrisSpine, IDisposable
{
	private readonly ILoggingService _logger;

	private readonly IBrainExecutor _executor;

	private readonly IGpuArm _gpuArm;

	private readonly INetworkArm _networkArm;

	private CancellationTokenSource? _cts;

	private Task? _watchdogTask;

	private readonly object _baselineLock = new object();

	private BaselineState? _capturedBaseline;

	private bool _baselineCaptured = false;

	private DateTime _startTimeUtc;

	private bool _isAdmin;

	private static readonly string BaselinePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Voltris", "Brain", "spine_baseline.json");

	private static readonly TimeSpan _startupGracePeriod = TimeSpan.FromSeconds(30.0);

	public TimeSpan WatchdogInterval { get; set; } = TimeSpan.FromSeconds(30.0);


	public bool IsWatchdogRunning => _watchdogTask != null && !_watchdogTask!.IsCompleted;

	public BaselineState CurrentBaseline
	{
		get
		{
			lock (_baselineLock)
			{
				return _capturedBaseline ?? new BaselineState();
			}
		}
	}

	public event EventHandler<HealthCheckEventArgs>? OnHealthCheck;

	public event EventHandler<AnomalyDetectedEventArgs>? OnAnomalyDetected;

	public event EventHandler<RollbackCompletedEventArgs>? OnRollbackCompleted;

	// [FIX:UNICA-FONTE] P/Invoke de powrprof REMOVIDOS do VoltrisSpine.
	//
	// `PowerGetActiveScheme` e `PowerSetActiveScheme` eram declarações mortas aqui
	// (nunca chamadas), mas a presença delas neste arquivo era um risco: qualquer
	// chamada futura passaria por DIRETO, sem o `PowerWriteGate`, e recriaria
	// exatamente a duplicação de autoria que acabamos de eliminar.
	//
	// A leitura do plano ativo e a ativação de plano passaram a usar
	// `PowerNativeMethods`, que é a nossa única superfície para `powrprof.dll`.
	// O `PowerSetActiveScheme` declarado aqui nunca foi chamado — o rollback
	// usava `SmartEnergyService.RunPowercfg` — mas ficar sem ele é o que garante
	// que a Spine não vire uma segunda porta de entrada.

	[DllImport("ntdll.dll")]
	private static extern int NtQueryTimerResolution(out uint minResolution, out uint maxResolution, out uint currentResolution);

	[DllImport("winmm.dll")]
	private static extern uint timeEndPeriod(uint uPeriod);

	public VoltrisSpine(ILoggingService logger, IBrainExecutor executor, IGpuArm gpuArm, INetworkArm networkArm)
	{
		_logger = logger ?? throw new ArgumentNullException("logger");
		_executor = executor ?? throw new ArgumentNullException("executor");
		_gpuArm = gpuArm ?? throw new ArgumentNullException("gpuArm");
		_networkArm = networkArm ?? throw new ArgumentNullException("networkArm");
		_isAdmin = IsRunningAsAdmin();
		string directoryName = Path.GetDirectoryName(BaselinePath);
		if (!string.IsNullOrEmpty(directoryName) && !Directory.Exists(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
	}

	public Task StartAsync(CancellationToken ct = default(CancellationToken))
	{
		_startTimeUtc = DateTime.UtcNow;
		_cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		_watchdogTask = Task.Run(() => WatchdogAsync(_cts!.Token), _cts!.Token);
		ILoggingService logger = _logger;
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(54, 1);
		defaultInterpolatedStringHandler.AppendLiteral("[SPINE] Watchdog iniciado | intervalo = 30s | admin = ");
		defaultInterpolatedStringHandler.AppendFormatted(_isAdmin);
		logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
		return Task.CompletedTask;
	}

	public async Task StopAsync()
	{
		_cts?.Cancel();
		try
		{
			if (_watchdogTask != null)
			{
				await _watchdogTask;
			}
		}
		catch
		{
		}
		_logger.LogInfo("[SPINE] Watchdog parado");
	}

	public void Dispose()
	{
		try
		{
			StopAsync().Wait(5000);
		}
		catch
		{
		}
		_cts?.Dispose();
	}

	public async Task CaptureBaselineAsync()
	{
		lock (_baselineLock)
		{
			if (_baselineCaptured)
			{
				_logger.LogInfo("[SPINE] Baseline já capturado nesta sessão, ignorando chamada duplicada.");
				return;
			}
			_baselineCaptured = true;
		}
		await Task.Run(delegate
		{
			BaselineState baselineState = new BaselineState
			{
				CapturedAtUtc = DateTime.UtcNow
			};
			try
			{
				baselineState.OriginalEpp = 50; // Padrão do Windows — sem leitura
			}
			catch (Exception ex)
			{
				_logger.LogWarning("[SPINE] Falha ler EPP: " + ex.Message);
				baselineState.OriginalEpp = 50;
			}
			try
			{
				baselineState.OriginalPowerPlanGuid = TryReadActivePowerPlanGuid();
			}
			catch
			{
				baselineState.OriginalPowerPlanGuid = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
			}
			try
			{
				baselineState.OriginalSystemResponsiveness = 20; // Padrão do Windows
			}
			catch
			{
				baselineState.OriginalSystemResponsiveness = 20;
			}
			try
			{
				baselineState.OriginalTimerResolution = TryReadCurrentTimerResolution();
			}
			catch
			{
				baselineState.OriginalTimerResolution = 156250u;
			}
			try
			{
				baselineState.OriginalGamingMode = TryReadCurrentGamingMode();
			}
			catch
			{
				baselineState.OriginalGamingMode = false;
			}
			try
			{
				baselineState.OriginalHagsMode = (Registry.GetValue("HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Control\\GraphicsDrivers", "HwSchMode", 1) as int?).GetValueOrDefault(1);
			}
			catch
			{
				baselineState.OriginalHagsMode = 1;
			}
			try
			{
				baselineState.OriginalNetworkThrottling = (Registry.GetValue("HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile", "NetworkThrottlingIndex", 10) as int?).GetValueOrDefault(10);
			}
			catch
			{
				baselineState.OriginalNetworkThrottling = 10;
			}
			lock (_baselineLock)
			{
				_capturedBaseline = baselineState;
			}
			ILoggingService logger = _logger;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(80, 3);
			defaultInterpolatedStringHandler.AppendLiteral("[SPINE] Baseline capturado: PowerPlan = ");
			defaultInterpolatedStringHandler.AppendFormatted(baselineState.OriginalPowerPlanGuid.ToString().Substring(0, 8));
			defaultInterpolatedStringHandler.AppendLiteral("... | Timer = ");
			defaultInterpolatedStringHandler.AppendFormatted(baselineState.OriginalTimerResolution);
			defaultInterpolatedStringHandler.AppendLiteral(" ticks | GamingMode = ");
			defaultInterpolatedStringHandler.AppendFormatted(baselineState.OriginalGamingMode);
			logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
		});
	}

	public async Task<RollbackResult> RollbackAllAsync()
	{
		Stopwatch sw = Stopwatch.StartNew();
		RollbackResult result = new RollbackResult
		{
			Success = true
		};
		await Task.Run(async delegate
		{
			BaselineState baseline;
			lock (_baselineLock)
			{
				baseline = _capturedBaseline;
			}
			if (baseline == null)
			{
				baseline = LoadBaselineFromDisk();
			}
			if (baseline == null)
			{
				result.Success = false;
				result.Details = "Nenhum baseline encontrado";
				_logger.LogWarning("[SPINE] Rollback solicitado sem baseline");
			}
			else
			{
				// Declarado aqui porque o resto do método (os blocos de timer e de
				// log abaixo) continua usando o mesmo handler.
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(0, 0);

				// [FIX:UNICO-DONO-DE-ENERGIA] O rollback da Spine não troca mais o
				// plano de energia.
				//
				// Este bloco montava, à mão, a string "/setactive {guid}" com
				// DefaultInterpolatedStringHandler e a executava por
				// `SmartEnergyService.RunPowercfg` — uma troca direta de plano.
				//
				// Vale notar que ela passava pelo `SmartEnergyService`, que é a
				// superfície com portão. A illusion de legitimidade vem daí: o
				// código tem a aparência de estar no caminho oficial, e está
				// apenas a uma camada do lugar onde a decisão é legítima.
				//
				// O problema do conteúdo, à parte do caminho: o rollback guardava
				// um baseline com o GUID do plano original e o reativava. Com o
				// Perfil como único dono, "plano original" não é um conceito
				// externo — o Perfil reasserta o que é dele, e é isso que precisa
				// acontecer. Reativar um GUID capturado antes pode devolver um
				// estado obsoleto.
				_logger.LogInfo("[SPINE] ROLLBACK: devolvendo o plano ao Perfil Inteligente " +
					$"(antes: /setactive {baseline.OriginalPowerPlanGuid}).");

				try
				{
					if (_isAdmin)
					{
						VoltrisOptimizer.Services.Power.ProfilePowerAuthority.RequestProfileApply(
							"VoltrisSpine.RollbackPowerPlan",
							"rollback da Spine",
							_logger);
						result.ItemsRestored++;
					}
					else
					{
						_logger.LogWarning("[SPINE] ROLLBACK: sem admin; plano nao alterado.");
						result.ItemsFailed++;
					}
				}
				catch (Exception ex8)
				{
					Exception ex4 = ex8;
					_logger.LogError("[SPINE] Erro ao devolver PowerPlan ao Perfil: " + ex4.Message);
					result.ItemsFailed++;
				}
				try
				{
					uint current3 = TryReadCurrentTimerResolution();
					timeEndPeriod(baseline.OriginalTimerResolution);
					ILoggingService logger4 = _logger;
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(52, 2);
					defaultInterpolatedStringHandler.AppendLiteral("[SPINE] Timer restaurado: ");
					defaultInterpolatedStringHandler.AppendFormatted(current3);
					defaultInterpolatedStringHandler.AppendLiteral(" ticks → ");
					defaultInterpolatedStringHandler.AppendFormatted(baseline.OriginalTimerResolution);
					defaultInterpolatedStringHandler.AppendLiteral(" ticks (original)");
					logger4.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
					result.ItemsRestored++;
				}
				catch (Exception ex6)
				{
					_logger.LogError("[SPINE] Erro restaurar Timer: " + ex6.Message);
					result.ItemsFailed++;
				}
				if (_isAdmin)
				{
					try
					{
						using (RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile\\Tasks\\Games", writable: true))
						{
							if (registryKey != null)
							{
								if (baseline.OriginalGamingMode)
								{
									registryKey.SetValue("GPU Priority", 8, RegistryValueKind.DWord);
									registryKey.SetValue("Priority", 6, RegistryValueKind.DWord);
									registryKey.SetValue("Scheduling Category", "High", RegistryValueKind.String);
								}
								else
								{
									registryKey.SetValue("GPU Priority", 2, RegistryValueKind.DWord);
									registryKey.SetValue("Priority", 2, RegistryValueKind.DWord);
									registryKey.SetValue("Scheduling Category", "Medium", RegistryValueKind.String);
								}
							}
						}
						ILoggingService logger5 = _logger;
						defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(42, 1);
						defaultInterpolatedStringHandler.AppendLiteral("[SPINE] GamingMode restaurado: ");
						defaultInterpolatedStringHandler.AppendFormatted(baseline.OriginalGamingMode);
						defaultInterpolatedStringHandler.AppendLiteral(" (original)");
						logger5.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
						result.ItemsRestored++;
					}
					catch (Exception ex8)
					{
						Exception ex5 = ex8;
						_logger.LogError("[SPINE] Erro restaurar GamingMode: " + ex5.Message);
						result.ItemsFailed++;
					}
				}
				try
				{
					await _executor.RestoreOriginalsAsync().ConfigureAwait(continueOnCapturedContext: false);
					_logger.LogInfo("[SPINE] Executor priorities restauradas");
				}
				catch
				{
				}
				try
				{
					await _gpuArm.RestoreGpuDefaultsAsync().ConfigureAwait(continueOnCapturedContext: false);
					_logger.LogInfo("[SPINE] GPU defaults restaurados");
					result.ItemsRestored++;
				}
				catch (Exception ex3)
				{
					_logger.LogError("[SPINE] Erro restaurar GPU defaults: " + ex3.Message);
					result.ItemsFailed++;
				}
				try
				{
					await _networkArm.RestoreNetworkDefaultsAsync().ConfigureAwait(continueOnCapturedContext: false);
					_logger.LogInfo("[SPINE] Network defaults restaurados");
					result.ItemsRestored++;
				}
				catch (Exception ex)
				{
					_logger.LogError("[SPINE] Erro restaurar Network defaults: " + ex.Message);
					result.ItemsFailed++;
				}
				sw.Stop();
				result.DurationMs = sw.Elapsed.TotalMilliseconds;
				ILoggingService logger6 = _logger;
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(49, 1);
				defaultInterpolatedStringHandler.AppendLiteral("[SPINE] ROLLBACK CONCLUÍDO em ");
				defaultInterpolatedStringHandler.AppendFormatted(result.DurationMs, "F0");
				defaultInterpolatedStringHandler.AppendLiteral(" ms - sistema limpo");
				logger6.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
			}
		}).ConfigureAwait(continueOnCapturedContext: false);
		return result;
	}

	private async Task WatchdogAsync(CancellationToken ct)
	{
		try
		{
			await Task.Delay(_startupGracePeriod, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (OperationCanceledException)
		{
			return;
		}
		while (!ct.IsCancellationRequested)
		{
			try
			{
				HealthCheckResult health = CheckHealth();
				TimeSpan uptime = DateTime.UtcNow - _startTimeUtc;
				if (health.IsHealthy)
				{
					ILoggingService logger = _logger;
					DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(45, 1);
					defaultInterpolatedStringHandler.AppendLiteral("[SPINE] Watchdog: tudo saudável | uptime = ");
					defaultInterpolatedStringHandler.AppendFormatted(uptime.TotalSeconds, "F0");
					defaultInterpolatedStringHandler.AppendLiteral(" s");
					logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
				}
				else
				{
					if (!health.EppMatchesExpected)
					{
						ILoggingService logger2 = _logger;
						DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(42, 2);
						defaultInterpolatedStringHandler.AppendLiteral("[SPINE] Watchdog: EPP esperado = ");
						defaultInterpolatedStringHandler.AppendFormatted(health.ExpectedEpp);
						defaultInterpolatedStringHandler.AppendLiteral(" atual = ");
						defaultInterpolatedStringHandler.AppendFormatted(health.ActualEpp);
						logger2.LogWarning(defaultInterpolatedStringHandler.ToStringAndClear());
					}
					this.OnAnomalyDetected?.Invoke(this, new AnomalyDetectedEventArgs
					{
						AnomalyType = "EPP_Mismatch",
						Expected = health.ExpectedEpp.ToString(),
						Actual = health.ActualEpp.ToString()
					});
				}
				this.OnHealthCheck?.Invoke(this, new HealthCheckEventArgs
				{
					Result = health,
					Uptime = uptime
				});
				await Task.Delay(WatchdogInterval, ct);
			}
			catch (OperationCanceledException)
			{
				break;
			}
			catch (Exception ex)
			{
				_logger.LogError("[SPINE] Watchdog erro: " + ex.Message);
				await Task.Delay(5000, ct);
			}
		}
	}

	public HealthCheckResult CheckHealth()
	{
		HealthCheckResult healthCheckResult = new HealthCheckResult
		{
			IsHealthy = true
		};
		healthCheckResult.ExpectedEpp = _capturedBaseline?.OriginalEpp ?? 50;
		try
		{
			healthCheckResult.ActualEpp = TryReadCurrentEpp().GetValueOrDefault(-1);
		}
		catch
		{
			healthCheckResult.ActualEpp = -1;
		}
		try
		{
			healthCheckResult.BrainLoopActive = App.BrainV2?.IsRunning ?? false;
		}
		catch
		{
			healthCheckResult.BrainLoopActive = false;
		}
		try
		{
			healthCheckResult.SensorLoopActive = App.BrainV2?.Executor != null;
		}
		catch
		{
			healthCheckResult.SensorLoopActive = false;
		}
		try
		{
			healthCheckResult.LegsPatrolActive = true;
		}
		catch
		{
			healthCheckResult.LegsPatrolActive = false;
		}
		if (healthCheckResult.ActualEpp < 0)
		{
			healthCheckResult.EppMatchesExpected = true;
		}
		else
		{
			healthCheckResult.EppMatchesExpected = Math.Abs(healthCheckResult.ExpectedEpp - healthCheckResult.ActualEpp) <= 10;
		}
		if (!healthCheckResult.EppMatchesExpected)
		{
			healthCheckResult.IsHealthy = false;
		}
		if (!healthCheckResult.BrainLoopActive || !healthCheckResult.SensorLoopActive || !healthCheckResult.LegsPatrolActive)
		{
			healthCheckResult.IsHealthy = false;
			ILoggingService logger = _logger;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(56, 3);
			defaultInterpolatedStringHandler.AppendLiteral("[SPINE] ⚠\ufe0f Watchdog: loops parados! Brain=");
			defaultInterpolatedStringHandler.AppendFormatted(healthCheckResult.BrainLoopActive);
			defaultInterpolatedStringHandler.AppendLiteral(" Sensor=");
			defaultInterpolatedStringHandler.AppendFormatted(healthCheckResult.SensorLoopActive);
			defaultInterpolatedStringHandler.AppendLiteral(" Legs=");
			defaultInterpolatedStringHandler.AppendFormatted(healthCheckResult.LegsPatrolActive);
			logger.LogWarning(defaultInterpolatedStringHandler.ToStringAndClear());
		}
		return healthCheckResult;
	}

	public async Task<SystemHealthReport> DiagnoseAsync()
	{
		return await Task.Run(delegate
		{
			HealthCheckResult healthCheckResult = CheckHealth();
			SystemHealthReport systemHealthReport = new SystemHealthReport
			{
				TimestampUtc = DateTime.UtcNow,
				BrainLoopActive = healthCheckResult.BrainLoopActive,
				SensorLoopActive = healthCheckResult.SensorLoopActive,
				LegsPatrolActive = healthCheckResult.LegsPatrolActive,
				EppCurrent = healthCheckResult.ActualEpp,
				EppExpected = healthCheckResult.ExpectedEpp,
				EppMatch = healthCheckResult.EppMatchesExpected
			};
			ILoggingService logger = _logger;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(46, 2);
			defaultInterpolatedStringHandler.AppendLiteral("[SPINE] Diagnostico concluido: EPP = ");
			defaultInterpolatedStringHandler.AppendFormatted(systemHealthReport.EppCurrent);
			defaultInterpolatedStringHandler.AppendLiteral(" match = ");
			defaultInterpolatedStringHandler.AppendFormatted(systemHealthReport.EppMatch);
			logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
			return systemHealthReport;
		});
	}

	private int? TryReadCurrentEpp()
	{
		try
		{
			ProcessStartInfo startInfo = new ProcessStartInfo
			{
				FileName = "powercfg.exe",
				Arguments = "/query SCHEME_CURRENT 54533251-82be-4824-96c1-47b60b740d00 36687f9e-e3a5-4dbf-b1dc-15eb381c6863",
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true
			};
			using Process process = Process.Start(startInfo);
			if (process == null)
			{
				return null;
			}
			string input = process.StandardOutput.ReadToEnd();
			process.WaitForExit(2000);
			Match match = Regex.Match(input, "Current AC Power Setting Index:\\s*0x([0-9A-Fa-f]+)");
			if (match.Success)
			{
				return Convert.ToInt32(match.Groups[1].Value, 16);
			}
		}
		catch
		{
			return null;
		}
		return null;
	}

	private Guid TryReadActivePowerPlanGuid()
	{
		try
		{
			// [FIX:UNICA-FONTE] Leitura do plano ativo pela superfície única.
			//
			// Usava o P/Invoke `PowerGetActiveScheme` declarado nesta classe, agora
			// removido. A leitura passa por `PowerNativeMethods.TryGetActiveSchemeGuid`,
			// que é a mesma função usada pelo `ProfilePowerCoordinator` — então a
			// Spine e o perfil não podem discordar sobre qual plano está ativo.
			if (VoltrisOptimizer.Utils.Win32.PowerNativeMethods.TryGetActiveSchemeGuid(out Guid activePolicyGuid))
			{
				return activePolicyGuid;
			}
		}
		catch
		{
		}
		return Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
	}

	private int? TryReadSystemResponsiveness()
	{
		try
		{
			using RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile");
			return registryKey?.GetValue("SystemResponsiveness") as int?;
		}
		catch
		{
			return null;
		}
	}

	private bool TryReadGamingMode()
	{
		try
		{
			using RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile");
			if (registryKey == null)
			{
				return false;
			}
			int? num = registryKey.GetValue("GPU Priority") as int?;
			int? num2 = registryKey.GetValue("Priority") as int?;
			return num.GetValueOrDefault() == 8 && num2.GetValueOrDefault() == 6;
		}
		catch
		{
			return false;
		}
	}

	private bool TryReadCurrentGamingMode()
	{
		return TryReadGamingMode();
	}

	private uint TryReadCurrentTimerResolution()
	{
		try
		{
			NtQueryTimerResolution(out var _, out var _, out var currentResolution);
			return currentResolution;
		}
		catch
		{
			return 156250u;
		}
	}

	private void PersistBaseline(BaselineState baseline)
	{
		try
		{
			string contents = JsonSerializer.Serialize(baseline, new JsonSerializerOptions { WriteIndented = true,
				ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase
			});
			File.WriteAllText(BaselinePath, contents);
		}
		catch (Exception ex)
		{
			_logger.LogWarning("[SPINE] Falha persistir baseline: " + ex.Message);
		}
	}

	private BaselineState? LoadBaselineFromDisk()
	{
		try
		{
			if (!File.Exists(BaselinePath))
			{
				return null;
			}
			string json = File.ReadAllText(BaselinePath);
			return JsonSerializer.Deserialize<BaselineState>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
		}
		catch
		{
			return null;
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
}
