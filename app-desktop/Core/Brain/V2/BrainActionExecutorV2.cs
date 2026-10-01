using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Principal;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Core.Body;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Optimization.Engines;
using VoltrisOptimizer.Services.Optimization.Features;
using VoltrisOptimizer.Services.Power;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Core.Brain.V2;

public sealed class BrainActionExecutorV2 : IBrainExecutor, IDisposable
{
	private readonly ILoggingService _logger;

	private IPowerArm? _powerArm;

	private IProcessArm? _processArm;

	private ISystemArm? _systemArm;

	private readonly object _serialDisruptiveOps = new object();

	private static readonly string[] ProtectedProcesses = new string[32]
	{
		"system", "registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsm", "fontdrvhost",
		"dwm", "explorer", "taskhostw", "svchost", "chrome", "msedge", "firefox", "brave", "opera", "vivaldi",
		"msmpeng", "nissrv", "securityhealthservice", "securityhealthsystray", "avp", "avgnt", "avastsvc", "avgsvc", "bdservicehost", "ekrn",
		"kavfsservice", "voltrisoptimizer"
	};

	private int? _originalEpp;

	private int? _originalSystemResponsiveness;

	private readonly ConcurrentDictionary<int, System.Diagnostics.ProcessPriorityClass> _originalPriorities = new ConcurrentDictionary<int, System.Diagnostics.ProcessPriorityClass>();

	private bool _gamingModeKeysOriginalCaptured;

	private (int gpuPriority, int priority, string scheduling)? _originalGamingModeKeys;

	private int? _lastAppliedEpp;

	private DateTime _lastEppAppliedUtc = DateTime.UtcNow;

	private int? _lastAppliedSysResp;

	private DateTime _lastSysRespChangeUtc = DateTime.UtcNow;

	private readonly TimeSpan _sysRespCooldown = TimeSpan.FromSeconds(60.0);

	private int? _lastAppliedGamingMode;

	private DateTime _lastTrimUtc = DateTime.UtcNow;

	private DateTime _lastPowerPlanUtc = DateTime.UtcNow;

	private string? _lastPowerPlanGuid;

	private readonly ConcurrentDictionary<int, DateTime> _lastPriorityChangeByPid = new ConcurrentDictionary<int, DateTime>();

	private readonly TimeSpan _priorityCooldown = TimeSpan.FromSeconds(30.0);

	private readonly bool _isAdmin;

	private readonly TopologyAwareAffinityEngine _affinityEngine;

	private readonly ZeroStutterMemoryEngine _memoryEngine;

	private readonly ZeroJitterProcessSuspender _jitterSuspender;

	private DateTime _lastGamingModeChangeUtc = DateTime.UtcNow;

	private readonly TimeSpan _gamingModeCooldown = TimeSpan.FromMinutes(3.0);

	public IPowerArm? PowerArm
	{
		get
		{
			return _powerArm;
		}
		set
		{
			_powerArm = value;
		}
	}

	public IProcessArm? ProcessArm
	{
		get
		{
			return _processArm;
		}
		set
		{
			_processArm = value;
		}
	}

	public ISystemArm? SystemArm
	{
		get
		{
			return _systemArm;
		}
		set
		{
			_systemArm = value;
		}
	}

	public BrainActionExecutorV2(ILoggingService logger)
	{
		_logger = logger ?? throw new ArgumentNullException("logger");
		_isAdmin = IsRunningAsAdmin();
		_originalEpp = TryReadCurrentEpp();
		_originalSystemResponsiveness = TryReadSystemResponsiveness();
		_affinityEngine = new TopologyAwareAffinityEngine(logger);
		_memoryEngine = new ZeroStutterMemoryEngine(logger);
		_jitterSuspender = new ZeroJitterProcessSuspender(logger);
		ILoggingService logger2 = _logger;
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(90, 3);
		defaultInterpolatedStringHandler.AppendLiteral("[ACTION] BrainActionExecutorV2 iniciado | admin = ");
		defaultInterpolatedStringHandler.AppendFormatted(_isAdmin);
		defaultInterpolatedStringHandler.AppendLiteral(" | EPP original = ");
		defaultInterpolatedStringHandler.AppendFormatted(_originalEpp?.ToString() ?? "n/a");
		defaultInterpolatedStringHandler.AppendLiteral(" | SysResp original = ");
		defaultInterpolatedStringHandler.AppendFormatted(_originalSystemResponsiveness?.ToString() ?? "n/a");
		logger2.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
	}

	public async Task<BrainActionResultV2> ExecuteAsync(BrainActionV2 action, SensorSnapshot context)
	{
		_logger.LogEntry(nameof(ExecuteAsync), ("action", action.ActionId), ("reason", action.Reason));
		BrainActionV2 action2 = action;
		Stopwatch sw = Stopwatch.StartNew();
		BrainActionResultV2 result;
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler;
		try
		{
			_logger.LogDecision("ExecuteAction", action.Reason, action.ActionId);
			ILoggingService logger = _logger;
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(40, 4);
			defaultInterpolatedStringHandler.AppendLiteral("[ACTION] PRÉ ");
			defaultInterpolatedStringHandler.AppendFormatted(action2.ActionId);
			defaultInterpolatedStringHandler.AppendLiteral(" | reason = ");
			defaultInterpolatedStringHandler.AppendFormatted(action2.Reason);
			defaultInterpolatedStringHandler.AppendLiteral(" | fg = ");
			defaultInterpolatedStringHandler.AppendFormatted(action2.TargetProcessName ?? "n/a");
			defaultInterpolatedStringHandler.AppendLiteral(" pid = ");
			defaultInterpolatedStringHandler.AppendFormatted(action2.TargetPid);
			logger.LogDebug(defaultInterpolatedStringHandler.ToStringAndClear(), "BrainActionExecutorV2");
			BrainActionKind kind = action2.Kind;

			BrainActionResultV2 brainActionResultV;
			switch (kind)
			{
			case BrainActionKind.NoAction:
				brainActionResultV = Skip(action2, "NoAction (neutro)");
				break;
			case BrainActionKind.SetEpp:
			{
				BrainActionResultV2 brainActionResultV4 = ((_powerArm == null) ? DoSetEpp(action2, context) : (await DelegateToPowerArmAsync((IPowerArm a) => a.SetEppAsync(action2.Param), action2)));
				brainActionResultV = brainActionResultV4;
				break;
			}
			case BrainActionKind.SetForegroundPriority:
			{
				BrainActionResultV2 brainActionResultV5 = ((_processArm == null) ? DoSetPriority(action2, context) : (await DelegateToProcessArmAsync((IProcessArm a) => a.SetProcessPriorityAsync(action2.TargetPid.GetValueOrDefault(), action2.TargetProcessName ?? "", VoltrisOptimizer.Core.Body.ProcessPriorityClassMap.FromChoiceOrdinal(action2.Param)), action2)));
				brainActionResultV = brainActionResultV5;
				break;
			}
			case BrainActionKind.SetSystemResponsiveness:
			{
				BrainActionResultV2 brainActionResultV3 = ((_powerArm == null) ? DoSetSystemResponsiveness(action2, context) : (await DelegateToPowerArmAsync((IPowerArm a) => a.SetSystemResponsivenessAsync(action2.Param), action2)));
				brainActionResultV = brainActionResultV3;
				break;
			}
			case BrainActionKind.EnableGamingMode:
			{
				BrainActionResultV2 brainActionResultV6 = ((_systemArm == null) ? DoEnableGamingMode(action2, context) : (await DelegateToSystemArmAsync((ISystemArm a) => a.EnableGamingModeAsync(action2.Param == 1), action2)));
				brainActionResultV = brainActionResultV6;
				break;
			}
			case BrainActionKind.TrimWorkingSet:
			{
				BrainActionResultV2 brainActionResultV2 = ((_processArm == null) ? DoTrimWorkingSet(action2, context) : (await DelegateToProcessArmAsync((IProcessArm a) => a.TrimWorkingSetAsync(Array.Empty<int>()), action2)));
				brainActionResultV = brainActionResultV2;
				break;
			}
			case BrainActionKind.LockMemoryHard:
				brainActionResultV = DoLockMemoryHard(action2, context);
				break;
			case BrainActionKind.ForceTopologyPcores:
				brainActionResultV = DoForceTopologyPcores(action2, context);
				break;
			case BrainActionKind.SuspendJitterProcesses:
				brainActionResultV = DoSuspendJitterProcesses(action2, context);
				break;
			default:
				brainActionResultV = Skip(action2, "kind desconhecido");
				break;
			}

			result = brainActionResultV;
		}
		catch (Exception ex2)
		{
			Exception ex = ex2;
			_logger.LogError("[ACTION] ERRO em " + action2.ActionId + ": " + ex.Message, ex);
			result = new BrainActionResultV2
			{
				Action = action2,
				Skipped = true,
				SkipReason = "exception: " + ex.Message
			};
		}
		sw.Stop();
		BrainActionResultV2 withTime = new BrainActionResultV2
		{
			Action = result.Action,
			Executed = result.Executed,
			Skipped = result.Skipped,
			SkipReason = result.SkipReason,
			ImpactVerified = result.ImpactVerified,
			ImpactDetail = result.ImpactDetail,
			ExecutionMs = sw.Elapsed.TotalMilliseconds
		};
		ILoggingService logger2 = _logger;
		defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(52, 5);
		defaultInterpolatedStringHandler.AppendLiteral("[ACTION] PÓS ");
		defaultInterpolatedStringHandler.AppendFormatted(action2.ActionId);
		defaultInterpolatedStringHandler.AppendLiteral(" | exec = ");
		defaultInterpolatedStringHandler.AppendFormatted(withTime.Executed);
		defaultInterpolatedStringHandler.AppendLiteral(" skip = ");
		defaultInterpolatedStringHandler.AppendFormatted(withTime.Skipped);
		defaultInterpolatedStringHandler.AppendLiteral(" reason = ");
		defaultInterpolatedStringHandler.AppendFormatted(withTime.Skipped ? withTime.SkipReason : withTime.ImpactDetail);
		defaultInterpolatedStringHandler.AppendLiteral(" time = ");
		defaultInterpolatedStringHandler.AppendFormatted(withTime.ExecutionMs, "F1");
		defaultInterpolatedStringHandler.AppendLiteral(" ms");
		logger2.LogDebug(defaultInterpolatedStringHandler.ToStringAndClear(), "BrainActionExecutorV2");
		BrainObservabilityHub.Publish((!withTime.Executed) ? BrainEventSeverity.Warning : BrainEventSeverity.Success, "BrainActionExecutorV2", "ActionExecution", withTime.Executed ? ("Ação aplicada: " + action2.ActionId) : ("Ação não aplicada: " + action2.ActionId), withTime.Executed ? withTime.ImpactDetail : withTime.SkipReason);
		lock (_serialDisruptiveOps)
		{
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(11, 5);
			defaultInterpolatedStringHandler.AppendFormatted(DateTime.UtcNow, "O");
			defaultInterpolatedStringHandler.AppendLiteral("\t");
			defaultInterpolatedStringHandler.AppendFormatted(action2.Kind);
			defaultInterpolatedStringHandler.AppendLiteral("\t");
			defaultInterpolatedStringHandler.AppendFormatted(action2.ActionId);
			defaultInterpolatedStringHandler.AppendLiteral("\texec = ");
			defaultInterpolatedStringHandler.AppendFormatted(withTime.Executed);
			defaultInterpolatedStringHandler.AppendLiteral("\t");
			defaultInterpolatedStringHandler.AppendFormatted(withTime.Skipped ? withTime.SkipReason : withTime.ImpactDetail);
			string audit = defaultInterpolatedStringHandler.ToStringAndClear();
			BrainActionAuditLog.Append(audit);
		}
		return withTime;
	}

	private BrainActionResultV2 DoSetEpp(BrainActionV2 a, SensorSnapshot ctx)
	{
		if (!BrainSafetyPolicy.ShouldAllowEpp)
		{
			return Skip(a, "policy: EPP desativado (flag ou fallback por erros)");
		}
		int param = a.Param;
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler;
		if (param < 0 || param > 100)
		{
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(14, 1);
			defaultInterpolatedStringHandler.AppendLiteral("epp inválido: ");
			defaultInterpolatedStringHandler.AppendFormatted(param);
			return Skip(a, defaultInterpolatedStringHandler.ToStringAndClear());
		}
		if (ctx.IsOnBattery && param < 70)
		{
			return Skip(a, "bateria: ajuste EPP agressivo (performance) bloqueado");
		}
		if (!_isAdmin)
		{
			_logger.LogWarning("[WARN] Ação SetEpp requer admin - pulada.");
			return Skip(a, "requer admin");
		}
		if (_lastAppliedEpp == param)
		{
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(18, 1);
			defaultInterpolatedStringHandler.AppendLiteral("EPP já em ");
			defaultInterpolatedStringHandler.AppendFormatted(param);
			defaultInterpolatedStringHandler.AppendLiteral(" (cache)");
			return Skip(a, defaultInterpolatedStringHandler.ToStringAndClear());
		}
		TimeSpan timeSpan = TimeSpan.FromSeconds(15.0);
		int num = _lastAppliedEpp ?? _originalEpp ?? TryReadCurrentEpp().GetValueOrDefault(param);
		int num2 = Math.Abs(param - num);
		TimeSpan timeSpan2 = DateTime.UtcNow - _lastEppAppliedUtc;
		if (num2 < 25 || (_lastEppAppliedUtc != DateTime.MinValue && timeSpan2 < timeSpan))
		{
			ILoggingService logger = _logger;
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(75, 2);
			defaultInterpolatedStringHandler.AppendLiteral("[BRAIN] EPP debounced: ");
			defaultInterpolatedStringHandler.AppendFormatted(num);
			defaultInterpolatedStringHandler.AppendLiteral(" → ");
			defaultInterpolatedStringHandler.AppendFormatted(param);
			defaultInterpolatedStringHandler.AppendLiteral(" bloqueado (delta insuficiente ou cooldown ativo)");
			logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(32, 2);
			defaultInterpolatedStringHandler.AppendLiteral("debounced delta = ");
			defaultInterpolatedStringHandler.AppendFormatted(num2);
			defaultInterpolatedStringHandler.AppendLiteral(" cooldown = ");
			defaultInterpolatedStringHandler.AppendFormatted(timeSpan2.TotalSeconds, "F1");
			defaultInterpolatedStringHandler.AppendLiteral(" s");
			return Skip(a, defaultInterpolatedStringHandler.ToStringAndClear());
		}
		// [FIX:UNICA-FONTE] ESTE CÓDIGO NÃO DEVE EXECUTAR.
		//
		// `DoSetEpp` é o caminho de FALLBACK do Brain: ele só roda quando não há
		// `IPowerArm` injetado, e por isso gravava EPP pelo `o servico legado`,
		// um serviço legado removido do projeto.
		//
		// Manter o método compilando, mas inapto a escrever, é deliberado. Ele
		// existe na hierarquia de fallback e apagar a assinatura mudaria o
		// contrato de `Execute` — mas o Brain não tem mais autoridade sobre EPP.
		//
		// O que o Brain FAZ com energia: pede AJUSTE dentro da faixa que o perfil
		// definiu. A autoridade é do Perfil Inteligente, e ela é exercida pelo
		// `PowerArm`, que é o único caminho que chega ao portão.
		_logger.LogWarning(
			"[BRAIN] SetEpp recusado: o Brain nao tem autoridade sobre EPP. " +
			"O valor e definido pelo Perfil Inteligente (ProfilePowerMatrix) para o " +
			"perfil e o tier da maquina. Use o PowerArm, que respeita o portão.");
		return Skip(a, "EPP pertence ao Perfil Inteligente (fonte unica de energia)");
		int? num3 = _lastAppliedEpp ?? _originalEpp;
		_lastAppliedEpp = param;
		_lastEppAppliedUtc = DateTime.UtcNow;
		ILoggingService logger2 = _logger;
		defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(31, 2);
		defaultInterpolatedStringHandler.AppendLiteral("[ACTION] EPP alterado de ");
		defaultInterpolatedStringHandler.AppendFormatted(num3?.ToString() ?? "N/A");
		defaultInterpolatedStringHandler.AppendLiteral(" para ");
		defaultInterpolatedStringHandler.AppendFormatted(param);
		logger2.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(7, 2);
			defaultInterpolatedStringHandler.AppendLiteral("EPP ");
			defaultInterpolatedStringHandler.AppendFormatted(num3?.ToString() ?? "N/A");
			defaultInterpolatedStringHandler.AppendLiteral(" → ");
			defaultInterpolatedStringHandler.AppendFormatted(param);
			BrainActionResultV2 obj = new BrainActionResultV2
			{
				Action = a,
				Executed = true,
				ImpactVerified = true,
				ImpactDetail = defaultInterpolatedStringHandler.ToStringAndClear()
			};
		return obj;
	}

	private BrainActionResultV2 DoSetPriority(BrainActionV2 a, SensorSnapshot ctx)
	{
		if (!BrainSafetyPolicy.ShouldAllowPriority)
		{
			return Skip(a, "policy: prioridade desativada (flag ou fallback)");
		}
		if (!a.TargetPid.HasValue || a.TargetPid <= 4)
		{
			return Skip(a, "pid inválido");
		}
		int value = a.TargetPid.Value;
		string text = (a.TargetProcessName ?? "")!.ToLowerInvariant();
		if (IsProtected(text))
		{
			return Skip(a, "processo protegido: " + text);
		}
		if (_lastPriorityChangeByPid.TryGetValue(value, out var value2))
		{
			TimeSpan timeSpan = DateTime.UtcNow - value2;
			if (timeSpan < _priorityCooldown)
			{
				TimeSpan timeSpan2 = _priorityCooldown - timeSpan;
				ILoggingService logger = _logger;
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(67, 2);
				defaultInterpolatedStringHandler.AppendLiteral("[ARM - PROCESS] Prioridade PID = ");
				defaultInterpolatedStringHandler.AppendFormatted(value);
				defaultInterpolatedStringHandler.AppendLiteral(" bloqueada: cooldown restante = ");
				defaultInterpolatedStringHandler.AppendFormatted(timeSpan2.TotalSeconds, "F0");
				defaultInterpolatedStringHandler.AppendLiteral(" s");
				logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(33, 1);
				defaultInterpolatedStringHandler.AppendLiteral("prioridade cooldown: ");
				defaultInterpolatedStringHandler.AppendFormatted(timeSpan2.TotalSeconds, "F0");
				defaultInterpolatedStringHandler.AppendLiteral(" s restantes");
				return Skip(a, defaultInterpolatedStringHandler.ToStringAndClear());
			}
		}
		bool flag = IsKnownGame(text);
		bool flag2 = ctx.Workload == WorkloadCategory.Game;
		ProcessPriorityChoice param = (ProcessPriorityChoice)a.Param;

		System.Diagnostics.ProcessPriorityClass processPriorityClass = param switch
		{
			ProcessPriorityChoice.High => System.Diagnostics.ProcessPriorityClass.High, 
			ProcessPriorityChoice.AboveNormal => System.Diagnostics.ProcessPriorityClass.AboveNormal, 
			_ => System.Diagnostics.ProcessPriorityClass.Normal};

		System.Diagnostics.ProcessPriorityClass processPriorityClass2 = processPriorityClass;
		if (flag && flag2 && processPriorityClass2 == System.Diagnostics.ProcessPriorityClass.AboveNormal)
		{
			_logger.LogInfo("[ARM - PROCESS] Prioridade mantida em High: " + text + " processo de jogo em foreground");
			processPriorityClass2 = System.Diagnostics.ProcessPriorityClass.High;
		}
		try
		{
			using Process process = Process.GetProcessById(value);
			System.Diagnostics.ProcessPriorityClass priorityClass = process.PriorityClass;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler;
			if (priorityClass == processPriorityClass2)
			{
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(6, 1);
				defaultInterpolatedStringHandler.AppendLiteral("já em ");
				defaultInterpolatedStringHandler.AppendFormatted(processPriorityClass2);
				return Skip(a, defaultInterpolatedStringHandler.ToStringAndClear());
			}
			if (flag && flag2 && priorityClass == System.Diagnostics.ProcessPriorityClass.High && processPriorityClass2 == System.Diagnostics.ProcessPriorityClass.AboveNormal)
			{
				_logger.LogInfo("[ARM - PROCESS] Prioridade mantida em High: " + text + " jogo ativo (bloqueado downgrade)");
				return Skip(a, "jogo ativo: prioridade High mantida");
			}
			_originalPriorities.TryAdd(value, priorityClass);
			process.PriorityClass = processPriorityClass2;
			_lastPriorityChangeByPid[value] = DateTime.UtcNow;
			ILoggingService logger2 = _logger;
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(57, 4);
			defaultInterpolatedStringHandler.AppendLiteral("[ACTION] Prioridade do processo ");
			defaultInterpolatedStringHandler.AppendFormatted(process.ProcessName);
			defaultInterpolatedStringHandler.AppendLiteral(" (");
			defaultInterpolatedStringHandler.AppendFormatted(value);
			defaultInterpolatedStringHandler.AppendLiteral(") alterada para ");
			defaultInterpolatedStringHandler.AppendFormatted(processPriorityClass2);
			defaultInterpolatedStringHandler.AppendLiteral(" (era ");
			defaultInterpolatedStringHandler.AppendFormatted(priorityClass);
			defaultInterpolatedStringHandler.AppendLiteral(")");
			logger2.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(5, 3);
			defaultInterpolatedStringHandler.AppendFormatted(process.ProcessName);
			defaultInterpolatedStringHandler.AppendLiteral(": ");
			defaultInterpolatedStringHandler.AppendFormatted(priorityClass);
			defaultInterpolatedStringHandler.AppendLiteral(" → ");
			defaultInterpolatedStringHandler.AppendFormatted(processPriorityClass2);
			BrainActionResultV2 obj = new BrainActionResultV2
			{
				Action = a,
				Executed = true,
				ImpactVerified = true,
				ImpactDetail = defaultInterpolatedStringHandler.ToStringAndClear()
			};
			return obj;
		}
		catch (Exception ex)
		{
			return Skip(a, "falhou: " + ex.Message);
		}
	}

	public static bool IsKnownGame(string name)
	{
		string name2 = name;
		string[] source = new string[32]
		{
			"cs2", "csgo", "valorant", "fortnite", "apex", "overwatch", "lol", "dota", "gta5", "minecraft",
			"rust", "pubg", "rocketleague", "rainbowsix", "warzone", "fortniteclient", "eldenring",
			"cyberpunk2077", "witcher3", "battlefield", "starfield", "diablo", "worldofwarcraft", "wow",
			"halo", "destiny2", "callofduty", "fc24", "fifa", "eafc", "tarkov", "escapefromtarkov"
		};
		return source.Any((string g) => name2.Contains(g, StringComparison.OrdinalIgnoreCase));
	}

	private BrainActionResultV2 DoSetSystemResponsiveness(BrainActionV2 a, SensorSnapshot ctx)
	{
		if (!_isAdmin)
		{
			_logger.LogWarning("[WARN] Ação SetSystemResponsiveness requer admin - pulada.");
			return Skip(a, "requer admin");
		}
		int param = a.Param;
		if (_lastAppliedSysResp == param)
		{
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(35, 1);
			defaultInterpolatedStringHandler.AppendLiteral("SystemResponsiveness já em ");
			defaultInterpolatedStringHandler.AppendFormatted(param);
			defaultInterpolatedStringHandler.AppendLiteral(" (cache)");
			return Skip(a, defaultInterpolatedStringHandler.ToStringAndClear());
		}
		TimeSpan timeSpan = DateTime.UtcNow - _lastSysRespChangeUtc;
		if (timeSpan < _sysRespCooldown)
		{
			TimeSpan timeSpan2 = _sysRespCooldown - timeSpan;
			ILoggingService logger = _logger;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(73, 1);
			defaultInterpolatedStringHandler.AppendLiteral("[ARM - SYSTEM] SystemResponsiveness bloqueado: cooldown = ");
			defaultInterpolatedStringHandler.AppendFormatted(timeSpan2.TotalSeconds, "F0");
			defaultInterpolatedStringHandler.AppendLiteral(" s (mínimo 60s)");
			logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(20, 1);
			defaultInterpolatedStringHandler.AppendLiteral("sysresp-cooldown: ");
			defaultInterpolatedStringHandler.AppendFormatted(timeSpan2.TotalSeconds, "F0");
			defaultInterpolatedStringHandler.AppendLiteral(" s");
			return Skip(a, defaultInterpolatedStringHandler.ToStringAndClear());
		}
		if (param == 20 && IsGameContext(ctx) && _lastAppliedSysResp.GetValueOrDefault() == 10)
		{
			_logger.LogInfo("[ARM - SYSTEM] SystemResponsiveness mantido em 10: jogo ativo em background (anti-AltTab)");
			return Skip(a, "jogo-bg: sysresp-mantido-em-10");
		}
		try
		{
			using RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile", writable: true);
			if (registryKey == null)
			{
				return Skip(a, "chave SystemProfile não acessível");
			}
			int? num = registryKey.GetValue("SystemResponsiveness") as int?;
			registryKey.SetValue("SystemResponsiveness", param, RegistryValueKind.DWord);
			_lastAppliedSysResp = param;
			_lastSysRespChangeUtc = DateTime.UtcNow;
			ILoggingService logger2 = _logger;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(37, 2);
			defaultInterpolatedStringHandler.AppendLiteral("[ACTION] SystemResponsiveness ");
			defaultInterpolatedStringHandler.AppendFormatted(param);
			defaultInterpolatedStringHandler.AppendLiteral(" (era ");
			defaultInterpolatedStringHandler.AppendFormatted(num?.ToString() ?? "N/A");
			defaultInterpolatedStringHandler.AppendLiteral(")");
			logger2.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(11, 2);
			defaultInterpolatedStringHandler.AppendLiteral("SysResp ");
			defaultInterpolatedStringHandler.AppendFormatted(num?.ToString() ?? "N/A");
			defaultInterpolatedStringHandler.AppendLiteral(" → ");
			defaultInterpolatedStringHandler.AppendFormatted(param);
			BrainActionResultV2 obj = new BrainActionResultV2
			{
				Action = a,
				Executed = true,
				ImpactVerified = true,
				ImpactDetail = defaultInterpolatedStringHandler.ToStringAndClear()
			};
			return obj;
		}
		catch (Exception ex)
		{
			return Skip(a, "registry: " + ex.Message);
		}
	}

	private BrainActionResultV2 DoEnableGamingMode(BrainActionV2 a, SensorSnapshot context)
	{
		if (!_isAdmin)
		{
			_logger.LogWarning("[WARN] Ação EnableGamingMode requer admin - pulada.");
			return Skip(a, "requer admin");
		}
		bool flag = a.Param == 1;
		if (_lastAppliedGamingMode == a.Param)
		{
			return Skip(a, "gaming mode já " + (flag ? "on" : "off") + " (cache)");
		}
		if (!flag && IsGameContext(context))
		{
			ILoggingService logger = _logger;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(66, 1);
			defaultInterpolatedStringHandler.AppendLiteral("[ARM - SYSTEM] GamingMode OFF bloqueado: jogo ativo (");
			defaultInterpolatedStringHandler.AppendFormatted(context.Workload);
			defaultInterpolatedStringHandler.AppendLiteral(") mantendo ON");
			logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
			return Skip(a, "jogo-ativo: gamingmode-mantido");
		}
		TimeSpan timeSpan = DateTime.UtcNow - _lastGamingModeChangeUtc;
		if (timeSpan < _gamingModeCooldown)
		{
			TimeSpan timeSpan2 = _gamingModeCooldown - timeSpan;
			ILoggingService logger2 = _logger;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(64, 1);
			defaultInterpolatedStringHandler.AppendLiteral("[ARM - SYSTEM] GamingMode bloqueado: cooldown = ");
			defaultInterpolatedStringHandler.AppendFormatted(timeSpan2.TotalSeconds, "F0");
			defaultInterpolatedStringHandler.AppendLiteral(" s (mínimo 3min)");
			logger2.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(23, 1);
			defaultInterpolatedStringHandler.AppendLiteral("gamingmode-cooldown: ");
			defaultInterpolatedStringHandler.AppendFormatted(timeSpan2.TotalSeconds, "F0");
			defaultInterpolatedStringHandler.AppendLiteral(" s");
			return Skip(a, defaultInterpolatedStringHandler.ToStringAndClear());
		}
		if (flag && !IsGameContext(context))
		{
			ILoggingService logger3 = _logger;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(65, 1);
			defaultInterpolatedStringHandler.AppendLiteral("[ARM - SYSTEM] GamingMode ON bloqueado: não é contexto de jogo (");
			defaultInterpolatedStringHandler.AppendFormatted(context.Workload);
			defaultInterpolatedStringHandler.AppendLiteral(")");
			logger3.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
			return Skip(a, "não-jogo: gamingmode-bloqueado");
		}
		try
		{
			using RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile\\Tasks\\Games", writable: true) ?? Registry.LocalMachine.CreateSubKey("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile\\Tasks\\Games", writable: true);
			if (registryKey == null)
			{
				return Skip(a, "chave Tasks\\Games não acessível");
			}
			if (!_gamingModeKeysOriginalCaptured)
			{
				int valueOrDefault = (registryKey.GetValue("GPU Priority") as int?).GetValueOrDefault(2);
				int valueOrDefault2 = (registryKey.GetValue("Priority") as int?).GetValueOrDefault(2);
				string item = (registryKey.GetValue("Scheduling Category") as string) ?? "Medium";
				_originalGamingModeKeys = (valueOrDefault, valueOrDefault2, item);
				_gamingModeKeysOriginalCaptured = true;
			}
			if (flag)
			{
				registryKey.SetValue("GPU Priority", 8, RegistryValueKind.DWord);
				registryKey.SetValue("Priority", 6, RegistryValueKind.DWord);
				registryKey.SetValue("Scheduling Category", "High", RegistryValueKind.String);
			}
			else
			{
				(int, int, string)? originalGamingModeKeys = _originalGamingModeKeys;
				if (originalGamingModeKeys.HasValue)
				{
					(int, int, string) valueOrDefault3 = originalGamingModeKeys.GetValueOrDefault();
					if (true)
					{
						registryKey.SetValue("GPU Priority", valueOrDefault3.Item1, RegistryValueKind.DWord);
						registryKey.SetValue("Priority", valueOrDefault3.Item2, RegistryValueKind.DWord);
						registryKey.SetValue("Scheduling Category", valueOrDefault3.Item3, RegistryValueKind.String);
					}
				}
			}
			_lastAppliedGamingMode = a.Param;
			_lastGamingModeChangeUtc = DateTime.UtcNow;
			_logger.LogInfo("[ACTION] GamingMode " + (flag ? "ON" : "OFF"));
			return new BrainActionResultV2
			{
				Action = a,
				Executed = true,
				ImpactVerified = true,
				ImpactDetail = (flag ? "MMCSS Games tuned" : "MMCSS Games restored")
			};
		}
		catch (Exception ex)
		{
			return Skip(a, "registry: " + ex.Message);
		}
	}

	[Obsolete("TrimWorkingSet causa Hard Page Faults e prejudica o frametime. Mantido apenas para compatibilidade.")]
	private BrainActionResultV2 DoTrimWorkingSet(BrainActionV2 a, SensorSnapshot ctx)
	{
		return Skip(a, "REMOVIDO: TrimWorkingSet causa Hard Page Faults e prejudica o frametime");
	}

	private static bool IsProtected(string name)
	{
		string name2 = name;
		return ProtectedProcesses.Any((string p) => name2.Contains(p, StringComparison.OrdinalIgnoreCase));
	}

	private static bool IsGameContext(SensorSnapshot ctx)
	{
		return ctx.Workload == WorkloadCategory.Game || (ctx.CpuUsagePercent > 60.0 && ctx.GpuUsagePercent > 60.0);
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

	private static BrainActionResultV2 Skip(BrainActionV2 action, string reason)
	{
		return new BrainActionResultV2
		{
			Action = action,
			Skipped = true,
			SkipReason = reason
		};
	}

	/// <summary>
	/// [FIX:UNICA-FONTE] Leitura do EPP pela API do Windows.
	///
	/// Substitui `o servico legado.GetCurrentEpp()`, removido com o serviço
	/// legado. É leitura pura, usada para contexto do Brain e para o log.
	/// O Brain continua sem autoridade de escrita sobre esse valor.
	/// </summary>
	private static int? TryReadCurrentEpp()
	{
		try
		{
			if (!VoltrisOptimizer.Utils.Win32.PowerNativeMethods.TryGetActiveSchemeGuid(out Guid scheme))
			{
				return null;
			}

			uint? value = VoltrisOptimizer.Utils.Win32.PowerNativeMethods.TryReadSchemeAcValueIndex(
				scheme,
				new Guid("36687f9e-e3a5-4dbf-b1dc-15eb381c6863"),
				new Guid("54533251-82be-4824-96c1-47b60b740d00"));

			return value.HasValue ? (int)value.Value : null;
		}
		catch
		{
			return null;
		}
	}

	private static int? TryReadSystemResponsiveness()
	{
		try
		{
			using RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile");
			if (registryKey != null)
			{
				return registryKey.GetValue("SystemResponsiveness") as int?;
			}
		}
		catch (Exception ex)
		{
			App.LoggingService?.LogError($"[{nameof(BrainActionExecutorV2)}] {ex.Message}", ex);
		}
		return null;
	}

	private async Task<BrainActionResultV2> DelegateToPowerArmAsync(Func<IPowerArm, Task> action, BrainActionV2 brainAction)
	{
		if (_powerArm == null)
		{
			return Skip(brainAction, "PowerArm não disponível");
		}
		try
		{
			await action(_powerArm);
			return new BrainActionResultV2
			{
				Action = brainAction,
				Executed = true,
				ImpactVerified = true,
				ImpactDetail = "delegado ao PowerArm"
			};
		}
		catch (Exception ex)
		{
			return Skip(brainAction, "PowerArm falhou: " + ex.Message);
		}
	}

	private async Task<BrainActionResultV2> DelegateToProcessArmAsync(Func<IProcessArm, Task> action, BrainActionV2 brainAction)
	{
		if (_processArm == null)
		{
			return Skip(brainAction, "ProcessArm não disponível");
		}
		try
		{
			await action(_processArm);
			return new BrainActionResultV2
			{
				Action = brainAction,
				Executed = true,
				ImpactVerified = true,
				ImpactDetail = "delegado ao ProcessArm"
			};
		}
		catch (Exception ex)
		{
			return Skip(brainAction, "ProcessArm falhou: " + ex.Message);
		}
	}

	private async Task<BrainActionResultV2> DelegateToSystemArmAsync(Func<ISystemArm, Task> action, BrainActionV2 brainAction)
	{
		if (_systemArm == null)
		{
			return Skip(brainAction, "SystemArm não disponível");
		}
		try
		{
			await action(_systemArm);
			return new BrainActionResultV2
			{
				Action = brainAction,
				Executed = true,
				ImpactVerified = true,
				ImpactDetail = "delegado ao SystemArm"
			};
		}
		catch (Exception ex)
		{
			return Skip(brainAction, "SystemArm falhou: " + ex.Message);
		}
	}

	public async Task RestoreOriginalsAsync()
	{
		try
		{
			// [FIX:POWER-VALUE-OWNER] O Brain NÃO restaura EPP.
			//
			// Este era o último caminho de escrita de EPP ainda aberto, e ele é
			// especialmente traiçoeiro porque roda na SAÍDA: o Brain gravava o EPP
			// que tinha lido no início da sessão, por `powercfg` direto, e o log
			// mostrava "EPP restaurado para 20" — sobrescrevendo o 45 que o
			// Perfil Inteligente tinha aplicado corretamente.
			//
			// A ideia original era simétrica ("quem mexeu, restaura"), e ela
			// funciona para SystemResponsiveness e para a prioridade do processo.
			// Para EPP não funciona, porque o EPP não é um ajuste temporário: ele
			// é o VALOR do perfil. O dono do EPP é a tabela, e a tabela não tem
			// memória do que foi antes dela.
			//
			// Quem restaura o EPP agora é o vigia de valores do coordenador, que
			// compara contra a linha aplicada e reescreve o que divergir.
			//
			// SystemResponsiveness, logo abaixo, continua sendo restaurado: esse
			// SIM é um ajuste por sessão, e o portão permite o Brain devolvê-lo.
			if (_originalSystemResponsiveness.HasValue)
			{
				using RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile", writable: true);
				if (registryKey != null)
				{
					registryKey.SetValue("SystemResponsiveness", _originalSystemResponsiveness.Value, RegistryValueKind.DWord);
					ILoggingService logger2 = _logger;
					DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(46, 1);
					defaultInterpolatedStringHandler.AppendLiteral("[ACTION] SystemResponsiveness restaurado para ");
					defaultInterpolatedStringHandler.AppendFormatted(_originalSystemResponsiveness.Value);
					logger2.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
				}
			}
			foreach (KeyValuePair<int, System.Diagnostics.ProcessPriorityClass> originalPriority in _originalPriorities)
			{
				originalPriority.Deconstruct(out var key2, out var value);
				int pid = key2;
				System.Diagnostics.ProcessPriorityClass priority = value;
				try
				{
					using Process p = Process.GetProcessById(pid);
					p.PriorityClass = priority;
				}
				catch (Exception ex)
				{
					_logger?.LogError($"[{nameof(BrainActionExecutorV2)}] RestoreOriginalsAsync: falha ao restaurar prioridade do PID {pid}", ex);
				}
			}
			if (_originalGamingModeKeys.HasValue && _isAdmin)
			{
				using RegistryKey key = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile\\Tasks\\Games", writable: true);
				if (key != null)
				{
					key.SetValue("GPU Priority", _originalGamingModeKeys.Value.gpuPriority, RegistryValueKind.DWord);
					key.SetValue("Priority", _originalGamingModeKeys.Value.priority, RegistryValueKind.DWord);
					key.SetValue("Scheduling Category", _originalGamingModeKeys.Value.scheduling, RegistryValueKind.String);
					_logger.LogInfo("[ACTION] GamingMode restaurado");
				}
			}
		}
		catch (Exception ex2)
		{
			Exception ex = ex2;
			_logger.LogError("[ACTION] Erro ao restaurar originais: " + ex.Message, ex);
		}
		await Task.CompletedTask;
	}

	/// <summary>
	/// Executa uma ação de plano de energia vinda do Brain.
	///
	/// [FIX:UNICO-DONO-DE-ENERGIA] Este método não troca mais o plano.
	///
	/// Ele rodava `SmartEnergyService.RunPowercfg("/setactive " + planGuid)` e
	/// registrava "PowerPlan alterado para {guid}". Passava pelo
	/// `SmartEnergyService` — a superfície com portão — o que lhe dava aparência
	/// de legitimidade, mas trocava o plano por fora de quem é o dono.
	///
	/// O Brain é uma das duas portas legítimas do Perfil Inteligente, e é
	/// justamente este o lugar onde isso deve aparecer. O Brain sabe QUE o jogo
	/// começou; ele não sabe qual é o perfil adequado nem a capacidade da
	/// máquina. Por isso o parâmetro `planGuid` é ignorado, e o log diz isso
	/// explicitamente em vez de mentir informando um GUID que não foi usado.
	/// </summary>
	public bool ExecutePowerPlan(string planGuid, string requestedBy)
	{
		if (!_isAdmin)
		{
			_logger.LogWarning("[ACTION] ExecutePowerPlan requer admin");
			return false;
		}
		try
		{
			_logger.LogInfo(
				"[ACTION] PowerPlan: delegando ao Perfil Inteligente (pedido de " + requestedBy + "). " +
				"O GUID " + planGuid + " foi IGNORADO: quem escolhe o plano e' o Perfil, " +
				"conforme o perfil e a capacidade real da maquina.");

			return VoltrisOptimizer.Services.Power.ProfilePowerAuthority.RequestProfileApply(
				"BrainActionExecutorV2.ExecutePowerPlan",
				"acao de plano solicitada por " + requestedBy,
				_logger);
		}
		catch (Exception ex)
		{
			_logger.LogError("[ACTION] Erro ao pedir reaplicacao do perfil: " + ex.Message, ex);
			return false;
		}
	}

	private BrainActionResultV2 DoLockMemoryHard(BrainActionV2 a, SensorSnapshot ctx)
	{
		if (!a.TargetPid.HasValue || a.TargetPid <= 4)
		{
			return Skip(a, "pid inválido para memory lock");
		}
		try
		{
			_memoryEngine.LockGameWorkingSet(a.TargetPid.Value);
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(24, 1);
			defaultInterpolatedStringHandler.AppendLiteral("Hard Memory Lock no PID ");
			defaultInterpolatedStringHandler.AppendFormatted(a.TargetPid.Value);
			BrainActionResultV2 obj = new BrainActionResultV2
			{
				Action = a,
				Executed = true,
				ImpactVerified = true,
				ImpactDetail = defaultInterpolatedStringHandler.ToStringAndClear()
			};
			return obj;
		}
		catch (Exception ex)
		{
			return Skip(a, "memory engine falhou: " + ex.Message);
		}
	}

	private BrainActionResultV2 DoForceTopologyPcores(BrainActionV2 a, SensorSnapshot ctx)
	{
		if (!a.TargetPid.HasValue || a.TargetPid <= 4)
		{
			return Skip(a, "pid inválido para affinity");
		}
		try
		{
			_affinityEngine.ApplyGamingAffinity(a.TargetPid.Value);
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(33, 1);
			defaultInterpolatedStringHandler.AppendLiteral("P-Cores / V-Cache forçado no PID ");
			defaultInterpolatedStringHandler.AppendFormatted(a.TargetPid.Value);
			BrainActionResultV2 obj = new BrainActionResultV2
			{
				Action = a,
				Executed = true,
				ImpactVerified = true,
				ImpactDetail = defaultInterpolatedStringHandler.ToStringAndClear()
			};
			return obj;
		}
		catch (Exception ex)
		{
			return Skip(a, "affinity engine falhou: " + ex.Message);
		}
	}

	private BrainActionResultV2 DoSuspendJitterProcesses(BrainActionV2 a, SensorSnapshot ctx)
	{
		if (a.Param == 1)
		{
			try
			{
				_jitterSuspender.ActivateZeroJitterMode();
				return new BrainActionResultV2
				{
					Action = a,
					Executed = true,
					ImpactVerified = true,
					ImpactDetail = "Zero Jitter Mode Ativado (Suspendidos processos secundários)"
				};
			}
			catch (Exception ex)
			{
				return Skip(a, "jitter suspender falhou: " + ex.Message);
			}
		}
		try
		{
			_jitterSuspender.DeactivateZeroJitterMode();
			return new BrainActionResultV2
			{
				Action = a,
				Executed = true,
				ImpactVerified = true,
				ImpactDetail = "Zero Jitter Mode Desativado"
			};
		}
		catch (Exception ex2)
		{
			return Skip(a, "jitter suspender falhou: " + ex2.Message);
		}
	}

	public void Dispose()
	{
		try
		{
			Task.Run(async () => await RestoreOriginalsAsync().ConfigureAwait(false)).GetAwaiter().GetResult();
		}
		catch (Exception ex)
		{
			_logger?.LogError($"[{nameof(BrainActionExecutorV2)}] {ex.Message}", ex);
		}
	}
}
