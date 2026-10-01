using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using VoltrisOptimizer.Core.Optimization;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Gamer.Models;
using VoltrisOptimizer.Services.SystemIntelligenceProfiler;

namespace VoltrisOptimizer.Core;

public class VoltrisOptimizationCore : IAutoStartService
{
	private readonly IServiceProvider _serviceProvider;

	private readonly ILoggingService _logger;

	private readonly Lazy<InstantOptimizationEngine> _instantEngine;

	private readonly Lazy<OptimizationDecisionEngine> _decisionEngine;

	private readonly Lazy<UnifiedOptimizationPipeline> _pipeline;

	private readonly Lazy<SystemIntelligenceProfilerService> _profiler;

	private volatile bool _initialized = false;

	private readonly SemaphoreSlim _initSemaphore = new SemaphoreSlim(1, 1);

	public VoltrisOptimizationCore(IServiceProvider serviceProvider, ILoggingService logger)
	{
		IServiceProvider serviceProvider2 = serviceProvider;
		VoltrisOptimizationCore voltrisOptimizationCore = this;
		_serviceProvider = serviceProvider2 ?? throw new ArgumentNullException("serviceProvider");
		_logger = logger ?? throw new ArgumentNullException("logger");
		_logger.LogInfo("[VoltrisCore] CONSTRUTOR INICIADO");
		_logger.LogInfo("[VoltrisCore] ServiceProvider: " + ((serviceProvider2 != null) ? "OK" : "NULL"));
		_logger.LogInfo("[VoltrisCore] Logger: " + ((logger != null) ? "OK" : "NULL"));
		_logger.LogInfo("[VoltrisCore] CONFIGURANDO LAZY INITIALIZATION...");
		try
		{
			_instantEngine = new Lazy<InstantOptimizationEngine>(delegate
			{
				voltrisOptimizationCore._logger.LogInfo("[VoltrisCore] Criando InstantOptimizationEngine...");
				return serviceProvider2.GetRequiredService<InstantOptimizationEngine>();
			});
			_decisionEngine = new Lazy<OptimizationDecisionEngine>(delegate
			{
				voltrisOptimizationCore._logger.LogInfo("[VoltrisCore] Criando OptimizationDecisionEngine...");
				return serviceProvider2.GetRequiredService<OptimizationDecisionEngine>();
			});
			_pipeline = new Lazy<UnifiedOptimizationPipeline>(delegate
			{
				voltrisOptimizationCore._logger.LogInfo("[VoltrisCore] Criando UnifiedOptimizationPipeline...");
				return serviceProvider2.GetRequiredService<UnifiedOptimizationPipeline>();
			});
			_profiler = new Lazy<SystemIntelligenceProfilerService>(delegate
			{
				voltrisOptimizationCore._logger.LogInfo("[VoltrisCore] Criando SystemIntelligenceProfilerService...");
				return serviceProvider2.GetRequiredService<SystemIntelligenceProfilerService>();
			});
			_logger.LogSuccess("[VoltrisCore] LAZY INITIALIZATION CONFIGURADA COM SUCESSO");
		}
		catch (Exception ex)
		{
			_logger.LogError("[VoltrisCore] ERRO AO CONFIGURAR LAZY INITIALIZATION: " + ex.Message, ex);
			throw;
		}
		_logger.LogSuccess("[VoltrisCore] CONSTRUTOR CONCLUÍDO COM SUCESSO");
	}

	public async Task InitializeAsync()
	{
		try
		{
			if (_initialized)
			{
				_logger.LogTrace("[VoltrisCore] Já inicializado - ignorando chamada duplicada");
				return;
			}
			await _initSemaphore.WaitAsync();
			try
			{
				if (_initialized)
				{
					_logger.LogTrace("[VoltrisCore] Já inicializado (dentro do semáforo) - ignorando");
					return;
				}
				Stopwatch stopwatch = Stopwatch.StartNew();
				_logger.LogInfo("[VoltrisCore] INICIALIZAÇÃO DO VOLTRIS CORE INICIADA");
				ILoggingService logger = _logger;
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(25, 1);
				defaultInterpolatedStringHandler.AppendLiteral("[VoltrisCore] Timestamp: ");
				defaultInterpolatedStringHandler.AppendFormatted(DateTime.Now, "HH:mm:ss.fff");
				logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
				ILoggingService logger2 = _logger;
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(25, 1);
				defaultInterpolatedStringHandler.AppendLiteral("[VoltrisCore] Thread ID: ");
				defaultInterpolatedStringHandler.AppendFormatted(Thread.CurrentThread.ManagedThreadId);
				logger2.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
				_logger.LogInfo("[VoltrisCore] INICIANDO INICIALIZAÇÃO PARALELA DOS MOTORES...");
				Task[] initTasks = new Task[4]
				{
					Task.Run(async delegate
					{
						_logger.LogInfo("[VoltrisCore] INICIALIZANDO SystemIntelligenceProfilerService...");
						try
						{
							SystemIntelligenceProfilerService profiler = _profiler.Value;
							ILoggingService logger9 = _logger;
							DefaultInterpolatedStringHandler defaultInterpolatedStringHandler5 = new DefaultInterpolatedStringHandler(62, 1);
							defaultInterpolatedStringHandler5.AppendLiteral("[VoltrisCore] SystemIntelligenceProfilerService inicializado: ");
							defaultInterpolatedStringHandler5.AppendFormatted(profiler != null);
							logger9.LogSuccess(defaultInterpolatedStringHandler5.ToStringAndClear());
							return profiler;
						}
						catch (Exception ex11)
						{
							Exception ex10 = ex11;
							_logger.LogError("[VoltrisCore] ERRO AO INICIALIZAR SystemIntelligenceProfilerService: " + ex10.Message, ex10);
							throw;
						}
					}),
					Task.Run(async delegate
					{
						_logger.LogInfo("[VoltrisCore] INICIALIZANDO OptimizationDecisionEngine...");
						try
						{
							OptimizationDecisionEngine decision = _decisionEngine.Value;
							ILoggingService logger8 = _logger;
							DefaultInterpolatedStringHandler defaultInterpolatedStringHandler4 = new DefaultInterpolatedStringHandler(55, 1);
							defaultInterpolatedStringHandler4.AppendLiteral("[VoltrisCore] OptimizationDecisionEngine inicializado: ");
							defaultInterpolatedStringHandler4.AppendFormatted(decision != null);
							logger8.LogSuccess(defaultInterpolatedStringHandler4.ToStringAndClear());
							return decision;
						}
						catch (Exception ex9)
						{
							Exception ex8 = ex9;
							_logger.LogError("[VoltrisCore] ERRO AO INICIALIZAR OptimizationDecisionEngine: " + ex8.Message, ex8);
							throw;
						}
					}),
					Task.Run(async delegate
					{
						_logger.LogInfo("[VoltrisCore] INICIALIZANDO UnifiedOptimizationPipeline...");
						try
						{
							UnifiedOptimizationPipeline pipeline = _pipeline.Value;
							ILoggingService logger7 = _logger;
							DefaultInterpolatedStringHandler defaultInterpolatedStringHandler3 = new DefaultInterpolatedStringHandler(56, 1);
							defaultInterpolatedStringHandler3.AppendLiteral("[VoltrisCore] UnifiedOptimizationPipeline inicializado: ");
							defaultInterpolatedStringHandler3.AppendFormatted(pipeline != null);
							logger7.LogSuccess(defaultInterpolatedStringHandler3.ToStringAndClear());
							return pipeline;
						}
						catch (Exception ex7)
						{
							Exception ex6 = ex7;
							_logger.LogError("[VoltrisCore] ERRO AO INICIALIZAR UnifiedOptimizationPipeline: " + ex6.Message, ex6);
							throw;
						}
					}),
					Task.Run(async delegate
					{
						_logger.LogInfo("[VoltrisCore] INICIALIZANDO InstantOptimizationEngine...");
						try
						{
							InstantOptimizationEngine instant = _instantEngine.Value;
							ILoggingService logger6 = _logger;
							DefaultInterpolatedStringHandler defaultInterpolatedStringHandler2 = new DefaultInterpolatedStringHandler(54, 1);
							defaultInterpolatedStringHandler2.AppendLiteral("[VoltrisCore] InstantOptimizationEngine inicializado: ");
							defaultInterpolatedStringHandler2.AppendFormatted(instant != null);
							logger6.LogSuccess(defaultInterpolatedStringHandler2.ToStringAndClear());
							return instant;
						}
						catch (Exception ex5)
						{
							Exception ex4 = ex5;
							_logger.LogError("[VoltrisCore] ERRO AO INICIALIZAR InstantOptimizationEngine: " + ex4.Message, ex4);
							throw;
						}
					})
				};
				ILoggingService logger3 = _logger;
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(63, 1);
				defaultInterpolatedStringHandler.AppendLiteral("[VoltrisCore] Aguardando conclusão de ");
				defaultInterpolatedStringHandler.AppendFormatted(initTasks.Length);
				defaultInterpolatedStringHandler.AppendLiteral(" tarefas (timeout 15s)...");
				logger3.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
				await Task.WhenAll(initTasks).WaitAsync(TimeSpan.FromSeconds(15.0));
				_logger.LogSuccess("[VoltrisCore] TODOS OS MOTORES INICIALIZADOS COM SUCESSO");
				_logger.LogInfo("[VoltrisCore] EXECUTANDO OTIMIZAÇÕES INSTANTÂNEAS...");
				try
				{
					_logger.LogInfo("[VoltrisCore] Otimizações instantâneas desabilitadas temporariamente");
				}
				catch (Exception ex3)
				{
					Exception ex2 = ex3;
					_logger.LogError("[VoltrisCore] ERRO NAS OTIMIZAÇÕES INSTANTÂNEAS: " + ex2.Message, ex2);
				}
				stopwatch.Stop();
				_initialized = true;
				ILoggingService logger4 = _logger;
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(47, 1);
				defaultInterpolatedStringHandler.AppendLiteral("[VoltrisCore] Tempo total de inicialização: ");
				defaultInterpolatedStringHandler.AppendFormatted(stopwatch.ElapsedMilliseconds);
				defaultInterpolatedStringHandler.AppendLiteral(" ms");
				logger4.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
				_logger.LogSuccess("[VoltrisCore] VOLTRISCORE INICIALIZADO COM SUCESSO");
			}
			finally
			{
				_initSemaphore.Release();
			}
		}
		catch (Exception ex3)
		{
			Exception ex = ex3;
			_logger.LogError("[VoltrisCore] ERRO FATAL NA INICIALIZAÇÃO", ex);
			_logger.LogError("[VoltrisCore] Tipo do erro: " + ex.GetType().Name);
			_logger.LogError("[VoltrisCore] Stack Trace: " + ex.StackTrace);
			ILoggingService logger5 = _logger;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(35, 1);
			defaultInterpolatedStringHandler.AppendLiteral("[VoltrisCore] Tempo até o erro: ");
			defaultInterpolatedStringHandler.AppendFormatted(Stopwatch.GetTimestamp());
			defaultInterpolatedStringHandler.AppendLiteral(" ms");
			logger5.LogError(defaultInterpolatedStringHandler.ToStringAndClear());
			if (ex.InnerException != null)
			{
				_logger.LogError("[VoltrisCore] Inner Exception: " + ex.InnerException!.Message);
			}
			throw;
		}
	}

	public async Task StartAsync(CancellationToken cancellationToken)
	{
		Stopwatch sw = Stopwatch.StartNew();
		_logger.LogInfo("[AUTO-START] VoltrisOptimizationCore iniciando automaticamente...");
		_logger.LogInfo("[AUTO-START] Executando otimizações rápidas em background...");
		try
		{
			CoreResult quickResult = await OptimizeQuickAsync().WaitAsync(TimeSpan.FromSeconds(10.0), cancellationToken);
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler;
			if (quickResult.Success)
			{
				ILoggingService logger = _logger;
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(61, 2);
				defaultInterpolatedStringHandler.AppendLiteral("[AUTO-START] Otimização rápida concluída: ");
				defaultInterpolatedStringHandler.AppendFormatted(quickResult.OptimizationsApplied);
				defaultInterpolatedStringHandler.AppendLiteral(" otimizações em ");
				defaultInterpolatedStringHandler.AppendFormatted(quickResult.ExecutionTimeMs);
				defaultInterpolatedStringHandler.AppendLiteral(" ms");
				logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
			}
			else
			{
				_logger.LogWarning("[AUTO-START] Otimização rápida falhou: " + quickResult.Message);
			}
			Task.Run(async delegate
			{
				try
				{
					_logger.LogInfo("[AUTO-START] Iniciando otimização completa em background...");
					_logger.LogInfo("[AUTO-START] Otimização completa desabilitada temporariamente");
				}
				catch (Exception ex5)
				{
					Exception ex4 = ex5;
					_logger.LogError("[AUTO-START] Erro na otimização completa em background: " + ex4.Message, ex4);
				}
			}, cancellationToken);
			ILoggingService logger2 = _logger;
			defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(62, 1);
			defaultInterpolatedStringHandler.AppendLiteral("[AUTO-START] Pipeline de auto-start iniciado com sucesso em ");
			defaultInterpolatedStringHandler.AppendFormatted(sw.ElapsedMilliseconds);
			defaultInterpolatedStringHandler.AppendLiteral("ms");
			logger2.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
		}
		catch (OperationCanceledException)
		{
			_logger.LogError("[AUTO-START] TIMEOUT: OptimizeQuickAsync excedeu 10s — possível deadlock ou inicialização travada.");
		}
		catch (Exception ex3)
		{
			Exception ex = ex3;
			_logger.LogError("[AUTO-START] Erro fatal no auto-start: " + ex.GetType().Name + ": " + ex.Message, ex);
			throw;
		}
		finally
		{
			sw.Stop();
		}
	}

	public async Task<CoreResult> OptimizeQuickAsync()
	{
		await EnsureInitializedAsync();
		Stopwatch sw = Stopwatch.StartNew();
		_logger.LogInfo("[VoltrisCore] Executando otimização rápida...");
		try
		{
			SystemMetricsCache metrics = SystemMetricsCache.Instance;
			double cpu = metrics.CpuPercent;
			double ram = metrics.MemoryUsedPercent;
			double temp = metrics.CpuTemperature;
			_logger.LogInfo($"[VoltrisCore] Estado atual do sistema - CPU: {cpu:F1}%, RAM: {ram:F1}%, Temp: {temp:F0}°C");
			if (cpu <= 50.0 && ram <= 70.0)
			{
				sw.Stop();
				_logger.LogInfo("[VoltrisCore] Sistema já está em estado ótimo - nenhuma otimização rápida necessária");
				return new CoreResult
				{
					Success = true,
					ExecutionTimeMs = sw.ElapsedMilliseconds,
					OptimizationsApplied = 0,
					Message = "Sistema já otimizado",
					Mode = "Quick"
				};
			}
			_logger.LogInfo($"[VoltrisCore] Gatilho de otimização: CPU={cpu:F1}% > 50% ou RAM={ram:F1}% > 70%. Aplicando correções rápidas...");
			int count = 0;
			VoltrisOptimizer.Core.Optimization.OptimizationContext context = new VoltrisOptimizer.Core.Optimization.OptimizationContext
			{
				UserIntent = "performance"
			};
			string[] quickWins = new string[4] { "powerplan_highperformance", "flush_dns", "optimize_memory", "enable_trim" };
			string[] array = quickWins;
			foreach (string opt in array)
			{
				try
				{
					ExecutionResult execResult = await _decisionEngine.Value.ExecuteOptimizationAsync(opt, context);
					if (execResult.Success)
					{
						Interlocked.Increment(ref count);
						ILoggingService logger2 = _logger;
						DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(46, 2);
						defaultInterpolatedStringHandler.AppendLiteral("[VoltrisCore] Otimização rápida aplicada: ");
						defaultInterpolatedStringHandler.AppendFormatted(opt);
						defaultInterpolatedStringHandler.AppendLiteral(" (");
						defaultInterpolatedStringHandler.AppendFormatted(execResult.ExecutionTimeMs);
						defaultInterpolatedStringHandler.AppendLiteral("ms)");
						logger2.LogSuccess(defaultInterpolatedStringHandler.ToStringAndClear());
					}
				}
				catch (Exception ex)
				{
					_logger.LogError($"[VoltrisCore] Erro na otimização rápida {opt}: {ex.Message}");
				}
			}
			sw.Stop();
			ILoggingService logger = _logger;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler2 = new DefaultInterpolatedStringHandler(54, 2);
			defaultInterpolatedStringHandler2.AppendLiteral("[VoltrisCore] Otimização rápida concluída: ");
			defaultInterpolatedStringHandler2.AppendFormatted(count);
			defaultInterpolatedStringHandler2.AppendLiteral(" otimizações em ");
			defaultInterpolatedStringHandler2.AppendFormatted(sw.ElapsedMilliseconds);
			defaultInterpolatedStringHandler2.AppendLiteral("ms");
			logger.LogSuccess(defaultInterpolatedStringHandler2.ToStringAndClear());
			return new CoreResult
			{
				Success = true,
				ExecutionTimeMs = sw.ElapsedMilliseconds,
				OptimizationsApplied = count,
				Message = $"Otimizações rápidas aplicadas: {count}",
				Mode = "Quick"
			};
		}
		catch (Exception ex3)
		{
			Exception ex = ex3;
			_logger.LogError("[VoltrisCore] Erro na otimização rápida", ex);
			sw.Stop();
			return new CoreResult
			{
				Success = false,
				ExecutionTimeMs = sw.ElapsedMilliseconds,
				Message = ex.Message,
				Mode = "Quick"
			};
		}
	}

	public async Task<DecisionResult> ShouldApplyOptimizationAsync(string optimizationName)
	{
		await EnsureInitializedAsync();
		VoltrisOptimizer.Core.Optimization.OptimizationContext context = new VoltrisOptimizer.Core.Optimization.OptimizationContext
		{
			UserIntent = "performance"
		};
		return await _decisionEngine.Value.ShouldApplyOptimizationAsync(optimizationName, context);
	}

	private async Task<OptimizationResult> ExecuteOptimizationAsync(VoltrisOptimizer.Core.Optimization.OptimizationContext context, OptimizationType type)
	{
		await EnsureInitializedAsync();
		string optimizationName = type.ToString();
		ILoggingService logger = _logger;
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(37, 1);
		defaultInterpolatedStringHandler.AppendLiteral("[VoltrisCore] Executando otimização: ");
		defaultInterpolatedStringHandler.AppendFormatted(type);
		logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
		try
		{
			ExecutionResult executionResult = await _decisionEngine.Value.ExecuteOptimizationAsync(optimizationName, context);
			return new OptimizationResult
			{
				Success = executionResult.Success,
				Category = type.ToString(),
				Impact = "Medium",
				Message = executionResult.Reason
			};
		}
		catch (Exception ex2)
		{
			Exception ex = ex2;
			_logger.LogError("[VoltrisCore] Erro ao executar " + optimizationName, ex);
			return new OptimizationResult
			{
				Success = false,
				Category = type.ToString(),
				Impact = "Error",
				Message = ex.Message
			};
		}
	}

	public async Task<HardwareProfileInfo> GetHardwareProfileAsync()
	{
		await EnsureInitializedAsync();
		HardwareProfile profile = await _profiler.Value.GenerateHardwareProfileAsync();
		SystemScores scores = _profiler.Value.CalculateSystemScores(profile);
		return new HardwareProfileInfo
		{
			CPUName = profile.CPUName,
			CPUCores = profile.CPUCores,
			TotalRAMGB = profile.TotalRAMGB,
			HasSSD = profile.HasSSD,
			HasNVMe = profile.HasNVMe,
			HasDedicatedGPU = profile.HasDedicatedGPU,
			GPUName = profile.GPUName,
			IsLaptop = profile.IsLaptop,
			Tier = profile.Tier.ToString(),
			PerformanceScore = scores.PerformanceScore,
			StabilityScore = scores.StabilityScore,
			RiskScore = scores.RiskScore,
			OverallScore = scores.OverallScore
		};
	}

	public async Task<OptimizationRecommendation[]> GetRecommendationsAsync()
	{
		await EnsureInitializedAsync();
		VoltrisOptimizer.Core.Optimization.OptimizationContext context = new VoltrisOptimizer.Core.Optimization.OptimizationContext
		{
			UserIntent = "performance"
		};
		return (await _decisionEngine.Value.GetRecommendedOptimizationsAsync(context)).Select((OptimizationRecommendation r) => new OptimizationRecommendation
		{
			Action = r.Action,
			Priority = r.Priority,
			Reason = r.Reason,
			Impact = r.Impact
		}).ToArray();
	}

	private async Task EnsureInitializedAsync()
	{
		if (!_initialized)
		{
			await InitializeAsync();
		}
	}

	private async Task ValidateServicesAsync()
	{
		string[] criticalServices = new string[4] { "PerformanceOptimizer", "NetworkOptimizer", "SystemCleaner", "AdvancedOptimizer" };
		string[] array = criticalServices;
		foreach (string serviceName in array)
		{
			object service = _serviceProvider.GetService(Type.GetType("VoltrisOptimizer.Services." + serviceName));
			if (service == null)
			{
				_logger.LogWarning("[VoltrisCore] Serviço crítico não encontrado: " + serviceName);
			}
		}
		await Task.CompletedTask;
	}

	private async Task WarmupCacheAsync()
	{
		await Task.Run(delegate
		{
			try
			{
				GC.KeepAlive(Environment.ProcessorCount);
				GC.KeepAlive(Environment.OSVersion);
			}
			catch
			{
			}
		});
	}

	private HardwareProfileInfo ConvertProfile(HardwareProfile profile)
	{
		return new HardwareProfileInfo
		{
			CPUName = "Unknown CPU",
			CPUCores = profile.CPUCores,
			TotalRAMGB = profile.TotalRAMGB,
			HasSSD = profile.HasSSD,
			HasNVMe = false,
			HasDedicatedGPU = profile.HasDedicatedGPU,
			GPUName = "Unknown GPU",
			IsLaptop = profile.IsLaptop,
			Tier = profile.Tier.ToString()
		};
	}
}
