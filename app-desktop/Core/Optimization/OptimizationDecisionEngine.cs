using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.ServiceProcess;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.SystemIntelligenceProfiler;

namespace VoltrisOptimizer.Core.Optimization;

public class OptimizationDecisionEngine
{
	private readonly ILoggingService _logger;

	private readonly SystemIntelligenceProfilerService _profiler;

	private readonly IServiceProvider _serviceProvider;

	private readonly Dictionary<string, DecisionRule> _rules = new Dictionary<string, DecisionRule>();

	public OptimizationDecisionEngine(ILoggingService logger, SystemIntelligenceProfilerService profiler, IServiceProvider serviceProvider)
	{
		_logger = logger ?? throw new ArgumentNullException("logger");
		_profiler = profiler ?? throw new ArgumentNullException("profiler");
		_serviceProvider = serviceProvider ?? throw new ArgumentNullException("serviceProvider");
		InitializeRules();
	}

	public async Task<DecisionResult> ShouldApplyOptimizationAsync(string optimizationName, OptimizationContext context)
	{
		try
		{
			await _profiler.EnsureProfileLoadedAsync();
			HardwareProfile systemProfile = await _profiler.GenerateHardwareProfileAsync();
			SystemScores scores = _profiler.CalculateSystemScores(systemProfile);
			HardwareProfile profile = systemProfile;
			if (_rules.TryGetValue(optimizationName.ToLowerInvariant(), out var rule))
			{
				return await EvaluateRuleAsync(rule, profile, scores, context);
			}
			ActionClassification classification = _profiler.ClassifyAction(optimizationName, systemProfile, scores);
			return new DecisionResult
			{
				ShouldApply = (classification != ActionClassification.Risky),
				Classification = classification,
				Reason = GetClassificationReason(classification, profile),
				Priority = GetPriorityFromClassification(classification),
				EstimatedImpact = GetEstimatedImpact(optimizationName, profile)
			};
		}
		catch (Exception ex2)
		{
			Exception ex = ex2;
			_logger.LogError("[DecisionEngine] Erro ao decidir sobre otimização " + optimizationName, ex);
			return new DecisionResult
			{
				ShouldApply = false,
				Classification = ActionClassification.Risky,
				Reason = "Erro no motor de decisão",
				Priority = "Low",
				EstimatedImpact = "Unknown"
			};
		}
	}

	public async Task<List<OptimizationRecommendation>> GetRecommendedOptimizationsAsync(OptimizationContext context)
	{
		await _profiler.EnsureProfileLoadedAsync();
		HardwareProfile profile = await _profiler.GenerateHardwareProfileAsync();
		SystemScores scores = _profiler.CalculateSystemScores(profile);
		List<OptimizationRecommendation> recommendations = new List<OptimizationRecommendation>();
		foreach (DecisionRule rule in _rules.Values)
		{
			if (rule.IsAutoRecommend)
			{
				HardwareProfile localProfile = profile;
				DecisionResult decision = await EvaluateRuleAsync(rule, localProfile, scores, context);
				if (decision.ShouldApply && decision.Classification != ActionClassification.Risky)
				{
					recommendations.Add(new OptimizationRecommendation
					{
						Action = rule.Name,
						Priority = decision.Priority,
						Reason = decision.Reason,
						Impact = decision.EstimatedImpact
					});
				}
			}
		}
		List<VoltrisOptimizer.Services.SystemIntelligenceProfiler.OptimizationRecommendation> profilerRecommendations = _profiler.GetRecommendations(profile, scores);
		recommendations.AddRange(profilerRecommendations.Select((VoltrisOptimizer.Services.SystemIntelligenceProfiler.OptimizationRecommendation r) => new OptimizationRecommendation
		{
			Action = r.Action,
			Priority = r.Priority,
			Reason = r.Reason,
			Impact = r.Impact
		}));
		return (from r in recommendations
			orderby GetPriorityWeight(r.Priority) descending, GetImpactWeight(r.Impact) descending
			select r).ToList();
	}

	public async Task<bool> IsOptimizationActiveAsync(string optimizationName)
	{
		try
		{
			return optimizationName.ToLowerInvariant() switch
			{
				"powerplan_highperformance" => false, 
				"trim_enabled" => await IsTrimEnabledAsync(), 
				"superfetch_disabled" => !(await IsServiceRunningAsync("SysMain")), 
				"hibernation_disabled" => !File.Exists("C:\\hiberfil.sys"), 
				"search_indexing_disabled" => !(await IsServiceRunningAsync("WSearch")), 
				"visual_optimized" => await IsVisualOptimizedAsync(), 
				_ => false};
		}
		catch (Exception ex2)
		{
			Exception ex = ex2;
			_logger.LogWarning("[DecisionEngine] Erro ao verificar estado da otimização " + optimizationName + ": " + ex.Message);
			return false;
		}
	}

	public async Task<ExecutionResult> ExecuteOptimizationAsync(string optimizationName, OptimizationContext context)
	{
		DecisionResult decision = await ShouldApplyOptimizationAsync(optimizationName, context);
		if (!decision.ShouldApply)
		{
			return new ExecutionResult
			{
				Success = false,
				Reason = "Otimização não recomendada: " + decision.Reason,
				Classification = decision.Classification
			};
		}
		try
		{
			_logger.LogInfo("[DecisionEngine] Executando otimização: " + optimizationName);
			OptimizationExecution result = await ExecuteSpecificOptimizationAsync(optimizationName, context);
			ILoggingService logger = _logger;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(42, 2);
			defaultInterpolatedStringHandler.AppendLiteral("[DecisionEngine] Otimização concluída: ");
			defaultInterpolatedStringHandler.AppendFormatted(optimizationName);
			defaultInterpolatedStringHandler.AppendLiteral(" - ");
			defaultInterpolatedStringHandler.AppendFormatted(result.Success);
			logger.LogSuccess(defaultInterpolatedStringHandler.ToStringAndClear());
			return new ExecutionResult
			{
				Success = result.Success,
				Reason = result.Message,
				Classification = decision.Classification,
				Impact = decision.EstimatedImpact,
				ExecutionTimeMs = result.ExecutionTimeMs
			};
		}
		catch (Exception ex)
		{
			_logger.LogError("[DecisionEngine] Erro ao executar otimização " + optimizationName, ex);
			return new ExecutionResult
			{
				Success = false,
				Reason = ex.Message,
				Classification = decision.Classification
			};
		}
	}

	private void InitializeRules()
	{
		_rules["powerplan_highperformance"] = new DecisionRule
		{
			Name = "powerplan_highperformance",
			IsAutoRecommend = true,
			Conditions = new List<Func<HardwareProfile, SystemScores, OptimizationContext, bool>>
			{
				(HardwareProfile profile, SystemScores scores, OptimizationContext ctx) => !profile.IsLaptop || profile.TotalRAMGB >= 16,
				(HardwareProfile profile, SystemScores scores, OptimizationContext ctx) => scores.RiskScore < 70
			},
			Reason = "Plano de alto desempenho recomendado para melhor performance",
			Priority = "High",
			Impact = "Alto"
		};
		_rules["optimize_memory"] = new DecisionRule
		{
			Name = "optimize_memory",
			IsAutoRecommend = true,
			Conditions = new List<Func<HardwareProfile, SystemScores, OptimizationContext, bool>>
			{
				(HardwareProfile profile, SystemScores scores, OptimizationContext ctx) => profile.TotalRAMGB < 16,
				(HardwareProfile profile, SystemScores scores, OptimizationContext ctx) => scores.PerformanceScore < 80
			},
			Reason = "Otimização de memória recomendada para sistemas com RAM limitada",
			Priority = "Medium",
			Impact = "Médio"
		};
		_rules["enable_trim"] = new DecisionRule
		{
			Name = "enable_trim",
			IsAutoRecommend = true,
			Conditions = new List<Func<HardwareProfile, SystemScores, OptimizationContext, bool>>
			{
				(HardwareProfile profile, SystemScores scores, OptimizationContext ctx) => profile.HasSSD
			},
			Reason = "TRIM deve ser habilitado em SSDs para manter performance",
			Priority = "High",
			Impact = "Médio"
		};
		_rules["disable_hibernation"] = new DecisionRule
		{
			Name = "disable_hibernation",
			IsAutoRecommend = true,
			Conditions = new List<Func<HardwareProfile, SystemScores, OptimizationContext, bool>>
			{
				(HardwareProfile profile, SystemScores scores, OptimizationContext ctx) => profile.TotalRAMGB >= 16
			},
			Reason = "Hibernação pode ser desabilitada em sistemas com muita RAM",
			Priority = "Medium",
			Impact = "Baixo"
		};
		_rules["flush_dns"] = new DecisionRule
		{
			Name = "flush_dns",
			IsAutoRecommend = true,
			Conditions = new List<Func<HardwareProfile, SystemScores, OptimizationContext, bool>>(),
			Reason = "Limpar cache DNS melhora resolução de nomes",
			Priority = "Low",
			Impact = "Baixo"
		};
		_rules["visual_optimize"] = new DecisionRule
		{
			Name = "visual_optimize",
			IsAutoRecommend = false,
			Conditions = new List<Func<HardwareProfile, SystemScores, OptimizationContext, bool>>
			{
				(HardwareProfile profile, SystemScores scores, OptimizationContext ctx) => !profile.IsGamingPC || scores.PerformanceScore < 70
			},
			Reason = "Otimizações visuais melhoram performance em sistemas não-gamer",
			Priority = "Medium",
			Impact = "Médio"
		};
		_rules["optimize_services"] = new DecisionRule
		{
			Name = "optimize_services",
			IsAutoRecommend = true,
			Conditions = new List<Func<HardwareProfile, SystemScores, OptimizationContext, bool>>
			{
				(HardwareProfile profile, SystemScores scores, OptimizationContext ctx) => profile.TotalRAMGB < 8 || scores.PerformanceScore < 60
			},
			Reason = "Otimização de serviços recomendada para sistemas de baixo desempenho",
			Priority = "Medium",
			Impact = "Médio"
		};
	}

	private async Task<DecisionResult> EvaluateRuleAsync(DecisionRule rule, HardwareProfile profile, SystemScores scores, OptimizationContext context)
	{
		HardwareProfile profile2 = profile;
		SystemScores scores2 = scores;
		OptimizationContext context2 = context;
		if (!rule.Conditions.All((Func<HardwareProfile, SystemScores, OptimizationContext, bool> condition) => condition(profile2, scores2, context2)))
		{
			return new DecisionResult
			{
				ShouldApply = false,
				Classification = ActionClassification.Conditional,
				Reason = "Condições do sistema não atendidas",
				Priority = rule.Priority,
				EstimatedImpact = rule.Impact
			};
		}
		HardwareProfile systemProfile = profile2;
		ActionClassification classification = _profiler.ClassifyAction(rule.Name, systemProfile, scores2);
		return new DecisionResult
		{
			ShouldApply = (classification != ActionClassification.Risky),
			Classification = classification,
			Reason = rule.Reason,
			Priority = rule.Priority,
			EstimatedImpact = rule.Impact
		};
	}

	private async Task<OptimizationExecution> ExecuteSpecificOptimizationAsync(string optimizationName, OptimizationContext context)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		string optName = optimizationName.ToLowerInvariant();
		try
		{
			switch (optName)
			{
			case "powerplan_highperformance":
			{
				// [FIX:UNICA-FONTE] ESTE CASO NÃO TROCA MAIS PLANO.
				//
				// O pedido "powerplan_highperformance" vinha do questionário e era
				// executado aqui trocando o plano de energia para o Alto Desempenho
				// do Windows. Medido nesta máquina, o Alto Desempenho é IDÊNTICO ao
				// Equilibrado em todos os settings de processador (EPP 20, mínimo 5,
				// máximo 100, boost 2, estacionamento 100) — ou seja, a troca não
				// dava ganho nenhum, apenas tirava do ar o plano que o Perfil
				// Inteligente tinha configurado.
				//
				// "Alto Desempenho" como objetivo do usuário é legítimo; a forma
				// certa de honrá-lo é escolher o perfil correspondente no
				// questionário e deixar o Perfil decidir os valores. Fazer a troca
				// aqui era uma segunda fonte de energia, por baixo da principal.
				_logger?.LogInfo(
					"[DecisionEngine] powerplan_highperformance: delegado ao Perfil Inteligente. " +
					"O plano de energia nao e trocado aqui; use o perfil correspondente no questionario.");

				return new OptimizationExecution
				{
					Success = true,
					Message = "Plano de energia sob controle do Perfil Inteligente (fonte unica)",
					ExecutionTimeMs = stopwatch.ElapsedMilliseconds
				};
			}
			case "optimize_memory":
			{
				AdvancedOptimizer advOptimizer = _serviceProvider.GetService<AdvancedOptimizer>();
				if (advOptimizer != null)
				{
					bool result = await advOptimizer.OptimizeMemoryAsync();
					return new OptimizationExecution
					{
						Success = result,
						Message = (result ? "Memória otimizada" : "Falha ao otimizar memória"),
						ExecutionTimeMs = stopwatch.ElapsedMilliseconds
					};
				}
				return new OptimizationExecution
				{
					Success = false,
					Message = "AdvancedOptimizer não registrado no contêiner - memória não otimizada nesta execução.",
					ExecutionTimeMs = stopwatch.ElapsedMilliseconds
				};
			}
			case "flush_dns":
			{
				NetworkOptimizer netOptimizer = _serviceProvider.GetService<NetworkOptimizer>();
				if (netOptimizer != null)
				{
					bool result2 = await netOptimizer.FlushDnsAsync();
					return new OptimizationExecution
					{
						Success = result2,
						Message = (result2 ? "Cache DNS limpo" : "Falha ao limpar DNS"),
						ExecutionTimeMs = stopwatch.ElapsedMilliseconds
					};
				}
				return new OptimizationExecution
				{
					Success = false,
					Message = "NetworkOptimizer não registrado no contêiner - DNS não limpo nesta execução.",
					ExecutionTimeMs = stopwatch.ElapsedMilliseconds
				};
			}
			case "enable_trim":
				try
				{
					ProcessStartInfo psi2 = new ProcessStartInfo("fsutil", "behavior set DisableDeleteNotify 0")
					{
						CreateNoWindow = true,
						UseShellExecute = false,
						RedirectStandardOutput = true,
						RedirectStandardError = true
					};
					using Process process = Process.Start(psi2);
					process?.WaitForExit(5000);
					bool success2 = process != null && process.ExitCode == 0;
					return new OptimizationExecution
					{
						Success = success2,
						Message = (success2 ? "TRIM habilitado com sucesso" : "Falha ao habilitar TRIM"),
						ExecutionTimeMs = stopwatch.ElapsedMilliseconds
					};
				}
				catch (Exception ex3)
				{
					Exception ex = ex3;
					return new OptimizationExecution
					{
						Success = false,
						Message = "Erro ao habilitar TRIM: " + ex.Message,
						ExecutionTimeMs = stopwatch.ElapsedMilliseconds
					};
				}
			case "disable_hibernation":
				try
				{
					ProcessStartInfo psi = new ProcessStartInfo("powercfg", "/hibernate off")
					{
						CreateNoWindow = true,
						UseShellExecute = false,
						RedirectStandardOutput = true,
						RedirectStandardError = true
					};
					using Process proc = Process.Start(psi);
					proc?.WaitForExit(5000);
					bool success = proc != null && proc.ExitCode == 0;
					return new OptimizationExecution
					{
						Success = success,
						Message = (success ? "Hibernação desativada com sucesso" : "Falha ao desativar hibernação"),
						ExecutionTimeMs = stopwatch.ElapsedMilliseconds
					};
				}
				catch (Exception ex3)
				{
					Exception ex2 = ex3;
					return new OptimizationExecution
					{
						Success = false,
						Message = "Erro ao desativar hibernação: " + ex2.Message,
						ExecutionTimeMs = stopwatch.ElapsedMilliseconds
					};
				}
			default:
				return new OptimizationExecution
				{
					Success = false,
					Message = "Otimização não implementada: " + optimizationName,
					ExecutionTimeMs = stopwatch.ElapsedMilliseconds
				};
			}
		}
		finally
		{
			stopwatch.Stop();
		}
	}

	private async Task<bool> IsTrimEnabledAsync()
	{
		try
		{
			Process process = Process.Start(new ProcessStartInfo
			{
				FileName = "fsutil",
				Arguments = "behavior query DisableDeleteNotify",
				UseShellExecute = false,
				RedirectStandardOutput = true,
				CreateNoWindow = true
			});
			if (process != null)
			{
				string output = await process.StandardOutput.ReadToEndAsync();
				process.WaitForExit();
				return output.Contains("DisableDeleteNotify = 0");
			}
		}
		catch
		{
		}
		return false;
	}

	private async Task<bool> IsServiceRunningAsync(string serviceName)
	{
		try
		{
			using ServiceController sc = new ServiceController(serviceName);
			return sc.Status == ServiceControllerStatus.Running;
		}
		catch
		{
			return false;
		}
	}

	private async Task<bool> IsVisualOptimizedAsync()
	{
		try
		{
			using RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\VisualEffects", writable: false);
			object val = key?.GetValue("VisualFXSetting");
			return val != null && Convert.ToInt32(val) == 2;
		}
		catch
		{
			return false;
		}
	}

	private string GetClassificationReason(ActionClassification classification, HardwareProfile profile)
	{
		if (1 == 0)
		{
		}
		string result = classification switch
		{
			ActionClassification.Safe => "Otimização segura para este sistema", 
			ActionClassification.Conditional => "Otimização condicional - verifique requisitos", 
			ActionClassification.Risky => "Otimização arriscada - não recomendada", 
			_ => "Classificação desconhecida"};
		if (1 == 0)
		{
		}
		return result;
	}

	private string GetPriorityFromClassification(ActionClassification classification)
	{
		if (1 == 0)
		{
		}
		string result = classification switch
		{
			ActionClassification.Safe => "Medium", 
			ActionClassification.Conditional => "Low", 
			ActionClassification.Risky => "Low", 
			_ => "Low"};
		if (1 == 0)
		{
		}
		return result;
	}

	private string GetEstimatedImpact(string optimizationName, HardwareProfile profile)
	{
		string text = optimizationName.ToLowerInvariant();
		if (text.Contains("powerplan"))
		{
			return "Alto";
		}
		if (text.Contains("memory"))
		{
			return (profile.TotalRAMGB < 8) ? "Alto" : "Médio";
		}
		if (text.Contains("trim") && profile.HasSSD)
		{
			return "Médio";
		}
		if (text.Contains("hibernation") && profile.TotalRAMGB >= 16)
		{
			return "Baixo";
		}
		if (text.Contains("dns"))
		{
			return "Baixo";
		}
		if (text.Contains("visual"))
		{
			return "Médio";
		}
		return "Desconhecido";
	}

	private int GetPriorityWeight(string priority)
	{
		string text = priority.ToLowerInvariant();
		if (1 == 0)
		{
		}
		int result = text switch
		{
			"high" => 3, 
			"medium" => 2, 
			"low" => 1, 
			_ => 0};
		if (1 == 0)
		{
		}
		return result;
	}

	private int GetImpactWeight(string impact)
	{
		string text = impact.ToLowerInvariant();
		if (1 == 0)
		{
		}
		int result = text switch
		{
			"alto" => 3, 
			"médio" => 2, 
			"baixo" => 1, 
			_ => 0};
		if (1 == 0)
		{
		}
		return result;
	}
}
