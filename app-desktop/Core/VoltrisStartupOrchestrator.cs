using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Power;

namespace VoltrisOptimizer.Core;

public sealed class VoltrisStartupOrchestrator
{
	private readonly IServiceProvider _services;

	private readonly ILoggingService _logger;

	private static readonly SemaphoreSlim _initLock = new SemaphoreSlim(1, 1);

	private bool _initialized;

	public VoltrisStartupOrchestrator(IServiceProvider services, ILoggingService logger)
	{
		_services = services ?? throw new ArgumentNullException("services");
		_logger = logger ?? throw new ArgumentNullException("logger");
	}

	public async Task InitializeOptimizationsAsync(CancellationToken ct = default(CancellationToken))
	{
		if (!(await _initLock.WaitAsync(0, ct)))
		{
			_logger.LogWarning("[StartupOrchestrator] Inicialização já em andamento, ignorando chamada duplicada.");
			return;
		}
		try
		{
			if (_initialized)
			{
				_logger.LogInfo("[StartupOrchestrator] Já inicializado, ignorando.");
				return;
			}
			_logger.LogInfo("[StartupOrchestrator] INICIANDO OTIMIZACOES (paralelizadas)");
			Stopwatch sw = Stopwatch.StartNew();
			using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
			timeoutCts.CancelAfter(TimeSpan.FromSeconds(20.0));
			try
			{
				await Task.WhenAll(InitializeCpuProfileAsync(timeoutCts.Token), InitializePowerPlanAsync(timeoutCts.Token), InitializeOptimizationManagerAsync(timeoutCts.Token), InitializeBootEnhancerAsync(timeoutCts.Token), InitializeVMRGAsync(timeoutCts.Token));
			}
			catch (OperationCanceledException)
			{
				_logger.LogWarning("[StartupOrchestrator] ⚠\ufe0f Timeout de 20s atingido! Inicializacao abortada para prevenir Zombie Tasks.");
			}
			sw.Stop();
			_initialized = true;
			ILoggingService logger = _logger;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(65, 1);
			defaultInterpolatedStringHandler.AppendLiteral("[StartupOrchestrator] OTIMIZACOES INICIALIZADAS em ");
			defaultInterpolatedStringHandler.AppendFormatted(sw.ElapsedMilliseconds);
			defaultInterpolatedStringHandler.AppendLiteral(" ms (paralelo)");
			logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
		}
		catch (Exception ex3)
		{
			Exception ex = ex3;
			_logger.LogError("[StartupOrchestrator] Erro na inicialização: " + ex.Message, ex);
		}
		finally
		{
			_initLock.Release();
		}
	}

	/// <summary>
	/// [FIX:UNICA-FONTE] INICIALIZAÇÃO DA ENERGIA — PERFIL INTELIGENTE.
	///
	/// Este passo substitui as DUAS inicializações de energia que existiam
	/// antes (`InitializeCpuProfileAsync` e `InitializePowerPlanAsync`), que
	/// subiam em paralelo com o nosso sistema e escreviam EPP e plano de energia
	/// sem lock e sem ordem. Com os dois serviços removidos, sobra uma chamada,
	/// e ela pertence ao `ProfilePowerCoordinator` — o mesmo dono que o
	/// `SettingsService.ProfileChanged` aciona quando o usuário troca de perfil.
	///
	/// Importante: o mesmo `ProfileChanged` alimenta a aplicação por evento. Esta
	/// chamada de inicialização existe para o caso em que o perfil ainda não
	/// gravou nada nesta sessão (primeira execução, ou estado carregado do disco),
	/// e por isso ela é idempotente.
	/// </summary>
	private async Task InitializeCpuProfileAsync(CancellationToken ct)
	{
		try
		{
			_logger.LogInfo("[StartupOrchestrator] [1/3] Aplicando energia do Perfil Inteligente...");
			VoltrisOptimizer.Services.Power.ProfilePowerCoordinator.Initialize(_logger);
			VoltrisOptimizer.Services.Power.ProfilePowerCoordinator.ApplyCurrentProfile(_logger);
			_logger.LogInfo("[StartupOrchestrator] [1/3] Energia do Perfil Inteligente aplicada.");
		}
		catch (Exception ex2)
		{
			Exception ex = ex2;
			_logger.LogError("[StartupOrchestrator] Erro ao aplicar energia do perfil: " + ex.Message, ex);
		}
	}

	/// <summary>
	/// [FIX:UNICA-FONTE] Não há segunda inicialização de energia.
	///
	/// A antiga `InitializePowerPlanAsync` subia o `o servico legado`,
	/// que criava e renomeava planos próprios. Esse serviço foi removido, e a
	/// criação do plano agora acontece dentro de `ProfilePowerApplier`, apenas
	/// quando o plano não existe — sempre sob o mesmo portão de escrita.
	/// </summary>
	private Task InitializePowerPlanAsync(CancellationToken ct)
	{
		_logger.LogInfo(
			"[StartupOrchestrator] [2/3] Plano de energia: criado sob demanda pelo Perfil Inteligente.");
		return Task.CompletedTask;
	}

	private async Task InitializeOptimizationManagerAsync(CancellationToken ct)
	{
		try
		{
			OptimizationManager optManager = _services.GetService<OptimizationManager>();
			if (optManager == null)
			{
				_logger.LogWarning("[StartupOrchestrator] OptimizationManager não disponível.");
				return;
			}
			_logger.LogInfo("[StartupOrchestrator] [3/3] Aplicando otimizações GPU/Latency/PCIe...");
			OptimizationManager.ApplyResult result = await optManager.ApplyIntelligentOptimizationsAsync();
			ILoggingService logger = _logger;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(59, 3);
			defaultInterpolatedStringHandler.AppendLiteral("[StartupOrchestrator] [3/3] Otimizações aplicadas: ");
			defaultInterpolatedStringHandler.AppendFormatted(result.Applied);
			defaultInterpolatedStringHandler.AppendLiteral("/");
			defaultInterpolatedStringHandler.AppendFormatted(result.Total);
			defaultInterpolatedStringHandler.AppendLiteral(" (");
			defaultInterpolatedStringHandler.AppendFormatted(result.ElapsedMs);
			defaultInterpolatedStringHandler.AppendLiteral(" ms).");
			logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
		}
		catch (Exception ex2)
		{
			Exception ex = ex2;
			_logger.LogError("[StartupOrchestrator] Erro no OptimizationManager: " + ex.Message, ex);
		}
	}

	private async Task InitializeBootEnhancerAsync(CancellationToken ct)
	{
		try
		{
			ITimerResolutionService timerService = _services.GetService<ITimerResolutionService>();
			if (timerService == null)
			{
				_logger.LogWarning("[StartupOrchestrator] TimerResolutionService não disponível - BootEnhancer será ignorado.");
				return;
			}
			using BootPerformanceEnhancer enhancer = new BootPerformanceEnhancer(_logger, timerService);
			await enhancer.InitializeAsync(ct);
		}
		catch (Exception ex2)
		{
			Exception ex = ex2;
			_logger.LogError("[StartupOrchestrator] Erro no BootPerformanceEnhancer: " + ex.Message, ex);
		}
	}

	private async Task InitializeVMRGAsync(CancellationToken ct)
	{
		try
		{
			VoltrisOptimizer.Core.VMRG.Interfaces.IVmrgOrchestrator vmrg = _services.GetService<VoltrisOptimizer.Core.VMRG.Interfaces.IVmrgOrchestrator>();
			if (vmrg == null)
			{
				_logger.LogWarning("[StartupOrchestrator] VMRGOrchestrator não disponível.");
				return;
			}
			_logger.LogInfo("[StartupOrchestrator] [VMRG] Iniciando governança de máquinas virtuais...");
			await vmrg.StartAsync(ct);
			_logger.LogInfo("[StartupOrchestrator] [VMRG] VMRG iniciado com sucesso.");
		}
		catch (Exception ex)
		{
			_logger.LogError("[StartupOrchestrator] Erro ao iniciar VMRG: " + ex.Message, ex);
		}
	}
}
