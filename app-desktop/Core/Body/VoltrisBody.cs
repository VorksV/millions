using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.Brain.V2;
using VoltrisOptimizer.Core.Intelligence;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Core.Body;

public sealed class VoltrisBody : IVoltrisBody, IDisposable, IAutoStartService
{
	private readonly ILoggingService _logger;

	private readonly VoltrisBrainV2 _brain;

	private readonly IPowerArm _powerArm;

	private readonly IProcessArm _processArm;

	private readonly ISystemArm _systemArm;

	private readonly IGpuArm _gpuArm;

	private readonly INetworkArm _networkArm;

	private readonly IVoltrisLegs _legs;

	private readonly IVoltrisSpine _spine;

	private readonly IBrainSensor _sensor;

	private readonly IPredictivePreWarmEngine _preWarm;

	private readonly IActiveBackgroundDirector _abd;

	private readonly SemaphoreSlim _decisionLock = new SemaphoreSlim(1, 1);

	private readonly Channel<BrainDecision> _decisionChannel;

	private CancellationTokenSource? _decisionProcessorCts;

	private Task? _decisionProcessorTask;

	private long _totalDecisionsDispatched;

	private long _totalDecisionsSucceeded;

	public SystemHealthState CurrentHealth { get; private set; } = new SystemHealthState();


	public VoltrisBrainV2.ProfileType ActiveProfile => _brain?.ActiveProfile ?? VoltrisBrainV2.ProfileType.Balanced;

	public OperationalContext CurrentContext { get; private set; } = OperationalContext.Idle;


	public bool IsGamingModeActive => CurrentContext == OperationalContext.Gaming;

	public float CurrentEpp { get; private set; } = -1f;


	public float CpuTempCelsius { get; private set; } = -1f;


	public string ForegroundProcessName { get; private set; } = string.Empty;


	public event EventHandler<ProfileChangedEventArgs>? OnProfileChanged;

	public event EventHandler<ContextChangedEventArgs>? OnContextChanged;

	public event EventHandler<ThermalAlertEventArgs>? OnThermalAlert;

	public event EventHandler<DecisionExecutedEventArgs>? OnDecisionExecuted;

	public VoltrisBody(ILoggingService logger, VoltrisBrainV2 brain, IPowerArm powerArm, IProcessArm processArm, ISystemArm systemArm, IGpuArm gpuArm, INetworkArm networkArm, IVoltrisLegs legs, IVoltrisSpine spine, IBrainSensor sensor, IPredictivePreWarmEngine preWarm, IActiveBackgroundDirector abd)
	{
		_logger = logger ?? throw new ArgumentNullException("logger");
		_brain = brain ?? throw new ArgumentNullException("brain");
		_powerArm = powerArm ?? throw new ArgumentNullException("powerArm");
		_processArm = processArm ?? throw new ArgumentNullException("processArm");
		_systemArm = systemArm ?? throw new ArgumentNullException("systemArm");
		_gpuArm = gpuArm ?? throw new ArgumentNullException("gpuArm");
		_networkArm = networkArm ?? throw new ArgumentNullException("networkArm");
		_legs = legs ?? throw new ArgumentNullException("legs");
		_spine = spine ?? throw new ArgumentNullException("spine");
		_sensor = sensor ?? throw new ArgumentNullException("sensor");
		_preWarm = preWarm ?? throw new ArgumentNullException("preWarm");
		_abd = abd ?? throw new ArgumentNullException("abd");
		_legs.OnContextConfirmed += OnLegsContextConfirmed;
		_brain.OnDecisionReady += OnBrainDecisionReady;
		_sensor.SnapshotProduced += OnSensorSnapshotProduced;
		_decisionChannel = Channel.CreateUnbounded<BrainDecision>();
		if (_brain.Executor is BrainActionExecutorV2 brainActionExecutorV)
		{
			brainActionExecutorV.PowerArm = _powerArm;
			brainActionExecutorV.ProcessArm = _processArm;
			brainActionExecutorV.SystemArm = _systemArm;
			_logger.LogInfo("[BODY]Executor legado conectado aos Arms delegação ativada");
		}
		else
		{
			_logger.LogWarning("[BODY]Executor no V2 delegação para Arms desabilitada");
		}
	}

	private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
	{
		if (element.ValueKind == JsonValueKind.Object)
		{
			if (element.TryGetProperty(propertyName, out value))
			{
				return true;
			}
			foreach (JsonProperty property in element.EnumerateObject())
			{
				if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
				{
					value = property.Value;
					return true;
				}
			}
		}
		value = default;
		return false;
	}

	public async Task StartAsync(CancellationToken ct = default(CancellationToken))
	{
		_logger.LogInfo("[BODY]Iniciando sistema nervoso...");
		await _spine.CaptureBaselineAsync();
		_logger.LogInfo("[BODY]✓ Spine (baseline capturado)");
		await _sensor.StartAsync(ct);
		_logger.LogInfo("[BODY]✓ Sensors (coleta de métricas ativa)");
		await _legs.StartAsync(ct);
		_logger.LogInfo("[BODY]✓ Legs (patrol iniciado)");
		await _brain.StartAsync();
		ILoggingService logger = _logger;
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(40, 2);
		defaultInterpolatedStringHandler.AppendLiteral("[BODY]✓ Brain (Q-Table: ");
		defaultInterpolatedStringHandler.AppendFormatted(_brain.Decision.KnownStates);
		defaultInterpolatedStringHandler.AppendLiteral(" estados | ε = ");
		defaultInterpolatedStringHandler.AppendFormatted(_brain.Decision.Epsilon, "F3");
		defaultInterpolatedStringHandler.AppendLiteral(")");
		logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
		try
		{
			string profilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Voltris", "Brain", "active_profile.json");
			string legacyProfilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Voltris", "active_profile.json");
			string pathToRead = File.Exists(profilePath) ? profilePath : legacyProfilePath;
			if (File.Exists(pathToRead))
			{
				using JsonDocument doc = JsonDocument.Parse(await File.ReadAllTextAsync(pathToRead, ct));
				// Case-insensitive: o arquivo e gravado com PropertyNamingPolicy = CamelCase ("profile"),
				// mas a leitura anterior exigia "Profile" (PascalCase) e nunca encontrava o valor.
				if (TryGetPropertyIgnoreCase(doc.RootElement, "profile", out var prop))
				{
					string profileStr = prop.GetString();
					if (Enum.TryParse<VoltrisBrainV2.ProfileType>(profileStr, out var parsedProfile))
					{
						_brain.SetActiveProfile(parsedProfile);
						_logger.LogInfo($"[BODY] Perfil inicial recuperado do cache: {parsedProfile} (origem={pathToRead})");
					}
					else
					{
						_logger.LogWarning($"[BODY] Perfil inválido em active_profile.json: '{profileStr}' — mantendo padrão");
					}
				}
				else
				{
					_logger.LogWarning($"[BODY] active_profile.json sem propriedade 'profile' em {pathToRead} — mantendo padrão");
				}
			}
		}
		catch (Exception ex2)
		{
			Exception ex = ex2;
			_logger.LogWarning("[BODY] Falha ao carregar perfil inicial: " + ex.Message);
		}
		_decisionProcessorCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		_decisionProcessorTask = Task.Run(() => DecisionProcessorLoopAsync(_decisionProcessorCts!.Token), _decisionProcessorCts!.Token);
		_logger.LogInfo("[BODY]✓ Decision Processor Loop (consumidor do canal de decisões ativado)");
		if (_preWarm != null)
		{
			await _preWarm.StartAsync(ct);
			_logger.LogInfo("[BODY]✓ Predictive Pre-Warm Engine (motor ativado)");
		}
		await _spine.StartAsync(ct);
		_logger.LogInfo("[BODY]✓ Spine Watchdog (intervalo = 30s)");
		_logger.LogInfo("[BODY]Sistema nervoso operacional | Arms = OK | DecisionProcessor = ATIVO");
	}

	public async Task StopAsync()
	{
		_logger.LogInfo("[BODY]Parando sistema nervoso...");
		if (_decisionProcessorCts != null)
		{
			_logger.LogDebug("[BODY]Parando Decision Processor Loop...");
			try
			{
				_decisionProcessorCts!.Cancel();
			}
			catch
			{
			}
			try
			{
				if (_decisionProcessorTask != null)
				{
					await _decisionProcessorTask!.ConfigureAwait(continueOnCapturedContext: false);
				}
			}
			catch (OperationCanceledException)
			{
			}
			ILoggingService logger = _logger;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(69, 2);
			defaultInterpolatedStringHandler.AppendLiteral("[BODY]✓ Decision Processor parado | total despachadas = ");
			defaultInterpolatedStringHandler.AppendFormatted(_totalDecisionsDispatched);
			defaultInterpolatedStringHandler.AppendLiteral(" | sucesso = ");
			defaultInterpolatedStringHandler.AppendFormatted(_totalDecisionsSucceeded);
			logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
		}
		if (_preWarm != null)
		{
			await _preWarm.StopAsync();
		}
		await _brain.StopAsync();
		await _sensor.StopAsync();
		await _legs.StopAsync();
		await _spine.StopAsync();
		_logger.LogInfo("[BODY]Sistema nervoso parado completamente");
	}

	private async Task DecisionProcessorLoopAsync(CancellationToken ct)
	{
		_logger.LogInfo("[BODY]Decision Processor Loop: iniciado, aguardando decisões do Brain...");
		try
		{
			await foreach (BrainDecision decision in _decisionChannel.Reader.ReadAllAsync(ct))
			{
				try
				{
					ILoggingService logger = _logger;
					DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(66, 4);
					defaultInterpolatedStringHandler.AppendLiteral("[BODY]Decision Processor: processando #");
					defaultInterpolatedStringHandler.AppendFormatted(decision.Id);
					defaultInterpolatedStringHandler.AppendLiteral(" | type=");
					defaultInterpolatedStringHandler.AppendFormatted(decision.ActionType);
					defaultInterpolatedStringHandler.AppendLiteral(" | value=");
					defaultInterpolatedStringHandler.AppendFormatted(decision.ActionValue);
					defaultInterpolatedStringHandler.AppendLiteral(" | target=");
					defaultInterpolatedStringHandler.AppendFormatted(decision.Target ?? "none");
					logger.LogDebug(defaultInterpolatedStringHandler.ToStringAndClear());
					DecisionResult result = await DispatchDecisionAsync(decision);
					Interlocked.Increment(ref _totalDecisionsDispatched);
					if (result.Success)
					{
						Interlocked.Increment(ref _totalDecisionsSucceeded);
					}
					ILoggingService logger2 = _logger;
					defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(63, 6);
					defaultInterpolatedStringHandler.AppendLiteral("[BODY]Decision Processor: #");
					defaultInterpolatedStringHandler.AppendFormatted(decision.Id);
					defaultInterpolatedStringHandler.AppendLiteral(" → ");
					defaultInterpolatedStringHandler.AppendFormatted(decision.ActionType);
					defaultInterpolatedStringHandler.AppendLiteral(" | ");
					defaultInterpolatedStringHandler.AppendFormatted(result.Detail);
					defaultInterpolatedStringHandler.AppendLiteral(" | success=");
					defaultInterpolatedStringHandler.AppendFormatted(result.Success);
					defaultInterpolatedStringHandler.AppendLiteral(" | time=");
					defaultInterpolatedStringHandler.AppendFormatted(result.ExecutionTimeMs, "F1");
					defaultInterpolatedStringHandler.AppendLiteral("ms | total=");
					defaultInterpolatedStringHandler.AppendFormatted(_totalDecisionsDispatched);
					logger2.LogDebug(defaultInterpolatedStringHandler.ToStringAndClear());
				}
				catch (Exception ex2)
				{
					ILoggingService logger3 = _logger;
					DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(55, 2);
					defaultInterpolatedStringHandler.AppendLiteral("[BODY]Decision Processor: erro ao processar decisão #");
					defaultInterpolatedStringHandler.AppendFormatted(decision.Id);
					defaultInterpolatedStringHandler.AppendLiteral(": ");
					defaultInterpolatedStringHandler.AppendFormatted(ex2.Message);
					logger3.LogError(defaultInterpolatedStringHandler.ToStringAndClear());
				}
			}
		}
		catch (OperationCanceledException)
		{
			_logger.LogInfo("[BODY]Decision Processor Loop: cancelado (shutdown)");
		}
		catch (Exception ex4)
		{
			Exception ex = ex4;
			_logger.LogError("[BODY]Decision Processor Loop: erro fatal: " + ex.Message);
		}
	}

	public void Dispose()
	{
		try
		{
			StopAsync().Wait(10000);
		}
		catch
		{
		}
		_legs.OnContextConfirmed -= OnLegsContextConfirmed;
		_brain.OnDecisionReady -= OnBrainDecisionReady;
		_sensor.SnapshotProduced -= OnSensorSnapshotProduced;
		_decisionLock?.Dispose();
		_decisionProcessorCts?.Dispose();
	}

	public async Task<DecisionResult> DispatchDecisionAsync(BrainDecision decision)
	{
		ILoggingService logger = _logger;
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(89, 5);
		defaultInterpolatedStringHandler.AppendLiteral("[BODY]DispatchDecision: ENTRY | #");
		defaultInterpolatedStringHandler.AppendFormatted(decision.Id);
		defaultInterpolatedStringHandler.AppendLiteral(" | ActionType=");
		defaultInterpolatedStringHandler.AppendFormatted(decision.ActionType);
		defaultInterpolatedStringHandler.AppendLiteral(" | ActionValue=");
		defaultInterpolatedStringHandler.AppendFormatted(decision.ActionValue);
		defaultInterpolatedStringHandler.AppendLiteral(" | Target=");
		defaultInterpolatedStringHandler.AppendFormatted(decision.Target ?? "(null)");
		defaultInterpolatedStringHandler.AppendLiteral(" | Justification=");
		defaultInterpolatedStringHandler.AppendFormatted(decision.Justification);
		logger.LogDebug(defaultInterpolatedStringHandler.ToStringAndClear());
		await _decisionLock.WaitAsync();
		Stopwatch sw = Stopwatch.StartNew();
		bool success = false;
		string armUsed = "";
		try
		{
			ILoggingService logger2 = _logger;
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(41, 3);
			defaultInterpolatedStringHandler.AppendLiteral("[BODY]Decisão #");
			defaultInterpolatedStringHandler.AppendFormatted(decision.Id);
			defaultInterpolatedStringHandler.AppendLiteral(": ");
			defaultInterpolatedStringHandler.AppendFormatted(decision.ActionType);
			defaultInterpolatedStringHandler.AppendLiteral(" -> roteando (thread = ");
			defaultInterpolatedStringHandler.AppendFormatted(Thread.CurrentThread.ManagedThreadId);
			defaultInterpolatedStringHandler.AppendLiteral(")");
			logger2.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
			switch (decision.ActionType)
			{
			case DecisionActionType.None:
				armUsed = "";
				success = true;
				break;
			case DecisionActionType.SetEpp:
			{
				EppChangeResult eppResult = await _powerArm.SetEppAsync(decision.ActionValue);
				armUsed = "ARM - POWER";
				success = eppResult != null && (eppResult.Executed || eppResult.CooldownBlocked);
				break;
			}
			case DecisionActionType.SetPowerPlan:
			{
				Guid g;
				bool ppResult = await _powerArm.SetPowerPlanAsync(Guid.TryParse(decision.ActionValue.ToString(), out g) ? g : Guid.Empty, decision.Justification ?? "auto");
				armUsed = "ARM - POWER";
				success = ppResult;
				break;
			}
			case DecisionActionType.SetSystemResponsiveness:
				success = await _powerArm.SetSystemResponsivenessAsync(decision.ActionValue);
				armUsed = "ARM - POWER";
				break;
			case DecisionActionType.SetTurboBoostPolicy:
			{
				bool tbResult = await _powerArm.SetTurboBoostPolicyAsync(decision.ActionValue);
				armUsed = "ARM - POWER";
				success = tbResult;
				break;
			}
			case DecisionActionType.SetCoreParking:
			{
				bool cpResult = await _powerArm.SetCoreParkingAsync(Math.Max(0, Math.Min(100, decision.ActionValue)), Math.Max(0, Math.Min(100, decision.ActionValue)));
				armUsed = "ARM - POWER";
				success = cpResult;
				break;
			}
			case DecisionActionType.SetProcessPriority:
				if (decision.Target != null)
				{
					string[] parts = decision.Target!.Split(':');
					if (parts.Length == 2 && int.TryParse(parts[0], out var pid))
					{
						int actionValue = decision.ActionValue;
						if (1 == 0)
						{
						}
						ProcessPriorityClass processPriorityClass = actionValue switch
						{
							0 => ProcessPriorityClass.Normal, 
							1 => ProcessPriorityClass.AboveNormal, 
							2 => ProcessPriorityClass.High, 
							_ => ProcessPriorityClass.High};
						if (1 == 0)
						{
						}
						ProcessPriorityClass prioClass = processPriorityClass;
						success = (await _processArm.SetProcessPriorityAsync(pid, parts[1], prioClass))?.Success ?? false;
					}
				}
				armUsed = "ARM - PROCESS";
				break;
			case DecisionActionType.TrimWorkingSet:
			{
				TrimResult trimResult = await _processArm.TrimWorkingSetAsync(Array.Empty<int>());
				armUsed = "ARM - PROCESS";
				success = trimResult != null && !trimResult.RequiresAdmin;
				break;
			}
			case DecisionActionType.SetEcoQoS:
			{
				int ecoPidVal;
				int ecoPid = (int.TryParse(decision.Target ?? "0", out ecoPidVal) ? ecoPidVal : 0);
				bool ecoResult = await _processArm.SetEcoQoSAsync(ecoPid, decision.Target ?? "", decision.ActionValue == 1);
				armUsed = "ARM - PROCESS";
				success = ecoResult;
				break;
			}
			case DecisionActionType.SetCpuAffinity:
			{
				int affPidVal;
				int affPid = (int.TryParse(decision.Target ?? "0", out affPidVal) ? affPidVal : 0);
				bool affResult = await _processArm.SetCpuAffinityAsync(affPid, decision.Target ?? "", decision.ActionValue);
				armUsed = "ARM - PROCESS";
				success = affResult;
				break;
			}
			case DecisionActionType.EnableGamingMode:
			{
				SystemActionResult sysResult = await _systemArm.EnableGamingModeAsync(decision.ActionValue == 1);
				armUsed = "ARM - SYSTEM";
				success = sysResult != null && (sysResult.Success || sysResult.GuardBlocked);
				break;
			}
			case DecisionActionType.SetTimerResolution:
			{
				bool timerResult = await _systemArm.SetTimerResolutionAsync((uint)Math.Max(1, Math.Min(1000, decision.ActionValue)));
				armUsed = "ARM - SYSTEM";
				success = timerResult;
				break;
			}
			case DecisionActionType.EnableMmcssGaming:
			{
				bool mmcssResult = await _systemArm.EnableMmcssGamingAsync(decision.ActionValue == 1);
				armUsed = "ARM - SYSTEM";
				success = mmcssResult;
				break;
			}
			case DecisionActionType.FlushDnsCache:
			{
				bool dnsResult = await _systemArm.FlushDnsCacheAsync();
				armUsed = "ARM - SYSTEM";
				success = dnsResult;
				break;
			}
			case DecisionActionType.TrimStandbyList:
			{
				long standbyResult = await _systemArm.TrimStandbyListAsync();
				armUsed = "ARM - SYSTEM";
				success = standbyResult > 0;
				break;
			}
			case DecisionActionType.ApplyGpuGamingProfile:
			{
				ArmActionResult gpuResult1 = await _gpuArm.ApplyGamingProfileAsync(decision.Target ?? "");
				armUsed = "ARM - GPU";
				success = gpuResult1?.Success ?? false;
				break;
			}
			case DecisionActionType.SetGpuLowLatency:
			{
				ArmActionResult gpuResult2 = await _gpuArm.SetLowLatencyModeAsync(decision.ActionValue == 1);
				armUsed = "ARM - GPU";
				success = gpuResult2?.Success ?? false;
				break;
			}
			case DecisionActionType.EnableHags:
			{
				ArmActionResult gpuResult3 = await _gpuArm.EnableHagsAsync();
				armUsed = "ARM - GPU";
				success = gpuResult3?.Success ?? false;
				break;
			}
			case DecisionActionType.OptimizeNetworkForGaming:
			{
				ArmActionResult netResult1 = await _networkArm.OptimizeForGamingAsync(decision.Target ?? "");
				armUsed = "ARM - NETWORK";
				success = netResult1?.Success ?? false;
				break;
			}
			case DecisionActionType.SetNetworkInterruptAffinity:
			{
				ArmActionResult netResult2 = await _networkArm.SetInterruptAffinityAsync();
				armUsed = "ARM - NETWORK";
				success = netResult2?.Success ?? false;
				break;
			}
			case DecisionActionType.RestoreNetworkDefaults:
			{
				ArmActionResult netResult3 = await _networkArm.RestoreNetworkDefaultsAsync();
				armUsed = "ARM - NETWORK";
				success = netResult3?.Success ?? false;
				break;
			}
			default:
			{
				ILoggingService logger3 = _logger;
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(24, 1);
				defaultInterpolatedStringHandler.AppendLiteral("[BODY]Ação não mapeada: ");
				defaultInterpolatedStringHandler.AppendFormatted(decision.ActionType);
				logger3.LogWarning(defaultInterpolatedStringHandler.ToStringAndClear());
				break;
			}
			}
			this.OnDecisionExecuted?.Invoke(this, new DecisionExecutedEventArgs
			{
				Decision = decision,
				ArmUsed = armUsed,
				Success = success,
				ExecutionTimeMs = sw.Elapsed.TotalMilliseconds
			});
		}
		catch (Exception ex2)
		{
			Exception ex = ex2;
			ILoggingService logger4 = _logger;
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(16, 2);
			defaultInterpolatedStringHandler.AppendLiteral("[BODY]ERRO em ");
			defaultInterpolatedStringHandler.AppendFormatted(decision.ActionType);
			defaultInterpolatedStringHandler.AppendLiteral(": ");
			defaultInterpolatedStringHandler.AppendFormatted(ex.Message);
			logger4.LogError(defaultInterpolatedStringHandler.ToStringAndClear());
		}
		finally
		{
			_decisionLock.Release();
		}
		sw.Stop();
		return new DecisionResult
		{
			Success = success,
			Detail = armUsed,
			ExecutionTimeMs = sw.Elapsed.TotalMilliseconds
		};
	}

	public async Task SetActiveProfileAsync(VoltrisBrainV2.ProfileType profile)
	{
		VoltrisBrainV2.ProfileType old = ActiveProfile;
		_brain.SetActiveProfile(profile);
		this.OnProfileChanged?.Invoke(this, new ProfileChangedEventArgs
		{
			OldProfile = old,
			NewProfile = profile
		});
		try
		{
			string profilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Voltris", "Brain", "active_profile.json");
			string dir = Path.GetDirectoryName(profilePath);
			if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
			{
				Directory.CreateDirectory(dir);
			}
			await File.WriteAllTextAsync(profilePath, JsonSerializer.Serialize(new
			{
				Profile = profile.ToString(),
				UpdatedUtc = DateTime.UtcNow
			}, new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
			ILoggingService logger = _logger;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(32, 1);
			defaultInterpolatedStringHandler.AppendLiteral("[BODY] Perfil ativo persistido: ");
			defaultInterpolatedStringHandler.AppendFormatted(profile);
			logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
		}
		catch (Exception ex)
		{
			_logger.LogWarning("[BODY] Falha ao persistir perfil: " + ex.Message);
		}
	}

	private async void OnLegsContextConfirmed(object? sender, ContextConfirmedEventArgs e)
	{
		OperationalContext oldContext = CurrentContext;
		CurrentContext = e.Context;
		ILoggingService logger = _logger;
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(52, 2);
		defaultInterpolatedStringHandler.AppendLiteral("[BODY] Contexto: ");
		defaultInterpolatedStringHandler.AppendFormatted(oldContext);
		defaultInterpolatedStringHandler.AppendLiteral("→");
		defaultInterpolatedStringHandler.AppendFormatted(e.Context);
		defaultInterpolatedStringHandler.AppendLiteral(" | aplicando bundle de otimizações");
		logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
		try
		{
			switch (e.Context)
			{
			case OperationalContext.Gaming:
				// [FIX:POWER-VALUE-OWNER] O bundle NÃO escreve mais EPP.
				//
				// Este `SetEppAsync(0)` era o último caminho livre que restava para
				// o Brain sobrescrever o Perfil Inteligente. Ele não passava pelo
				// `PowerWriteGate` porque o `PowerArm` era considerado "dono" da
				// escrita — o que, na prática, significava que qualquer bundle
				// podia reescrever o plano gerenciado a qualquer momento.
				//
				// O valor 0 é especialmente destrutivo em máquina limitada por
				// potência: é o que o Brain gravava logo após o perfil aplicar 45,
				// e a medição a frio mostrou 108% -> 62% de pico no caso análogo
				// do estacionamento.
				//
				// O resto do bundle continua: timer de 1ms, Game Mode, GPU,
				// rede e SystemResponsiveness (que tem portão próprio) são
				// escolhas de latência, não de orçamento de energia, e não
				// competem com o EPP pelo mesmo orçamento do processador.
				_logger.LogInfo("[BODY] Bundle Gaming aplicado: SR=10 | Timer=1ms | GamingMode=ON | EPP sob controle do Perfil Inteligente");
				await _powerArm.SetSystemResponsivenessAsync(10);
				await _systemArm.SetTimerResolutionAsync(10u);
				await _systemArm.EnableGamingModeAsync(enable: true);
				await _gpuArm.ApplyGamingProfileAsync(e.TriggerProcess ?? "");
				await _networkArm.OptimizeForGamingAsync("");
				if (_abd != null)
				{
					await _abd.ApplyGamingOptimizationsAsync(e.TriggerProcess ?? "");
				}
				break;
			case OperationalContext.Rendering:
				// [FIX:POWER-VALUE-OWNER] O bundle deixa de escrever EPP em TODOS os
				// contextos, não só no Gaming. O EPP é decidido pelo Perfil
				// Inteligente, que já sabe o perfil, o tier de hardware e a fonte
				// de alimentação. O Brain tem carga instantânea; o perfil tem
				// contexto. Em máquina limitada por potência, quem tem contexto
				// é quem acerta.
				_logger.LogInfo("[BODY] Bundle Rendering aplicado: SR=10 | Timer=1ms | EPP sob controle do Perfil Inteligente");
				await _powerArm.SetSystemResponsivenessAsync(10);
				await _systemArm.SetTimerResolutionAsync(10u);
				if (_abd != null)
				{
					await _abd.RestoreAllAsync();
				}
				break;
			case OperationalContext.ThermalCrisis:
				// [FIX:POWER-VALUE-OWNER] ÚNICA exceção, e por segurança: em
				// emergência de temperatura o Perfil Inteligente pode ainda estar
				// esperando o ciclo do vigia. Segurar o pedido aqui é mais seguro
				// do que esperar pelo dono. O portão abaixo deixa passar.
				_logger.LogWarning("[BODY] CRISE TÉRMICA: EPP=100 solicitado | performance suspensa");
				using (VoltrisOptimizer.Services.Power.PowerWriteGate.BeginProfileApply())
				{
					await _powerArm.SetEppAsync(100);
				}
				break;
			case OperationalContext.UserAway:
				_logger.LogInfo("[BODY] Bundle UserAway aplicado: SR=50 | Timer=15.6ms | Liberando Memória");
				await _powerArm.SetSystemResponsivenessAsync(50);
				await _systemArm.SetTimerResolutionAsync(156u);
				await _processArm.TrimWorkingSetAsync();
				await _systemArm.TrimStandbyListAsync();
				if (_abd != null)
				{
					await _abd.RestoreAllAsync();
				}
				break;
			case OperationalContext.Idle:
				_logger.LogInfo("[BODY] Bundle Idle aplicado: SR=20 | Timer=15.6ms");
				await _powerArm.SetSystemResponsivenessAsync(20);
				await _systemArm.SetTimerResolutionAsync(156u);
				await _processArm.TrimWorkingSetAsync();
				if (_abd != null)
				{
					await _abd.RestoreAllAsync();
				}
				break;
			default:
				_logger.LogInfo("[BODY] Bundle padrão aplicado: SR=20 | Timer=15.6ms");
				await _powerArm.SetSystemResponsivenessAsync(20);
				await _systemArm.SetTimerResolutionAsync(156u);
				if (_abd != null)
				{
					await _abd.RestoreAllAsync();
				}
				break;
			}
			if (oldContext == OperationalContext.Gaming && e.Context != OperationalContext.Gaming)
			{
				_logger.LogInfo("[BODY] Saindo do contexto Gaming — restaurando configurações de rede (NetworkArm)");
				try
				{
					await _networkArm.RestoreNetworkDefaultsAsync();
				}
				catch (Exception ex3)
				{
					Exception ex = ex3;
					_logger.LogWarning("[BODY] Falha ao restaurar rede ao sair de Gaming: " + ex.Message);
				}
			}
		}
		catch (Exception ex2)
		{
			Exception ex = ex2;
			ILoggingService logger2 = _logger;
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(32, 2);
			defaultInterpolatedStringHandler.AppendLiteral("[BODY] Erro ao aplicar bundle ");
			defaultInterpolatedStringHandler.AppendFormatted(e.Context);
			defaultInterpolatedStringHandler.AppendLiteral(": ");
			defaultInterpolatedStringHandler.AppendFormatted(ex.Message);
			logger2.LogError(defaultInterpolatedStringHandler.ToStringAndClear());
		}
		this.OnContextChanged?.Invoke(this, new ContextChangedEventArgs
		{
			OldContext = oldContext,
			NewContext = e.Context
		});
	}

	private void OnBrainDecisionReady(object? sender, BrainDecisionReadyEventArgs e)
	{
		_decisionChannel.Writer.TryWrite(e.Decision);
	}

	private void OnSensorSnapshotProduced(object? sender, SensorSnapshot e)
	{
		if (e != null)
		{
			CpuTempCelsius = (float)e.CpuTemperatureC;
			ForegroundProcessName = e.ForegroundProcessName;
			CurrentEpp = ((float?)_powerArm?.CurrentEpp) ?? (-1f);
			CurrentHealth = new SystemHealthState
			{
				TimestampUtc = e.TimestampUtc,
				CpuPercent = (float)e.CpuUsagePercent,
				CpuTemperatureCelsius = (float)e.CpuTemperatureC,
				CpuClockMhz = e.CpuClockMhz,
				RamUsedPercent = (float)e.RamUsagePercent,
				RamAvailableMb = e.AvailableRam,
				RamTotalMb = e.RamTotalMB,
				GpuPercent = (float)e.GpuUsagePercent,
				GpuTemperatureCelsius = (float)e.GpuTemperatureC,
				IsOnBattery = e.IsOnBattery,
				ForegroundPid = e.ForegroundPid,
				ForegroundProcessName = e.ForegroundProcessName,
				CurrentEpp = (_powerArm?.CurrentEpp ?? 50),
				IsGamingMode = (CurrentContext == OperationalContext.Gaming),
				CurrentGame = ((CurrentContext == OperationalContext.Gaming) ? e.ForegroundProcessName : null)
			};
		}
	}
}
