using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Principal;

namespace VoltrisOptimizer.Core.Brain.V2.AntiStutter;

public sealed class AntiStutterActionRegistry
{
	private readonly List<OptimizerAction> _actions = new List<OptimizerAction>();

	private readonly Dictionary<string, DateTime> _lastExecution = new Dictionary<string, DateTime>();

	public AntiStutterActionRegistry()
	{
		Register();
	}

	public IReadOnlyList<OptimizerAction> GetAll()
	{
		return _actions;
	}

	public IReadOnlyList<OptimizerAction> GetFor(BottleneckAnalysis analysis)
	{
		BottleneckAnalysis analysis2 = analysis;
		return _actions.Where((OptimizerAction a) => a.TargetBottlenecks.Contains(analysis2.PrimaryBottleneck) || a.TargetBottlenecks.Contains(analysis2.SecondaryBottleneck)).ToList();
	}

	public ActionScore Score(OptimizerAction action, AntiStutterSnapshot snap, BottleneckAnalysis analysis)
	{
		double num = Compatibility(action, snap);
		double num2 = Gain(action, analysis);
		double num3 = Risk(action, snap, analysis);
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(9, 4);
		defaultInterpolatedStringHandler.AppendFormatted(action.Name);
		defaultInterpolatedStringHandler.AppendLiteral(" C:");
		defaultInterpolatedStringHandler.AppendFormatted(num, "F2");
		defaultInterpolatedStringHandler.AppendLiteral(" G:");
		defaultInterpolatedStringHandler.AppendFormatted(num2, "F2");
		defaultInterpolatedStringHandler.AppendLiteral(" R:");
		defaultInterpolatedStringHandler.AppendFormatted(num3, "F2");
		string reasoning = defaultInterpolatedStringHandler.ToStringAndClear();
		ActionScore obj = new ActionScore
		{
			CompatibilityScore = num,
			ExpectedGainScore = num2,
			RiskScore = num3,
			Reasoning = reasoning
		};
		return obj;
	}

	public void RecordExecution(string actionId)
	{
		_lastExecution[actionId] = DateTime.UtcNow;
	}

	private double Compatibility(OptimizerAction a, AntiStutterSnapshot s)
	{
		double num = 1.0;
		if (a.RequiresAdmin && !IsAdmin())
		{
			num -= 0.5;
		}
		if (s.IsOnBattery && a.Category == ActionCategory.PowerPlan)
		{
			num -= 0.3;
		}
		if (s.IsGame && a.Category == ActionCategory.DiskOptimization)
		{
			num -= 0.2;
		}
		return Math.Clamp(num, 0.0, 1.0);
	}

	private double CalculateScore(OptimizerAction a, AntiStutterSnapshot s, BottleneckAnalysis analysis)
	{
		double num = Gain(a, analysis);
		double num2 = Risk(a, s, analysis);
		double num3 = num - num2;
		if (a.RequiresAdmin && !IsAdmin())
		{
			num3 -= 0.4;
		}
		if (s.IsOnBattery && a.Category == ActionCategory.PowerPlan)
		{
			num3 -= 0.3;
		}
		if (s.IsGame && a.Category == ActionCategory.DiskOptimization)
		{
			num3 -= 0.2;
		}
		return Math.Clamp(num3, 0.0, 1.0);
	}

	private double Gain(OptimizerAction a, BottleneckAnalysis analysis)
	{
		if (!analysis.HasBottleneck)
		{
			return 0.05;
		}
		if (a.TargetBottlenecks.Contains(analysis.PrimaryBottleneck))
		{
			return 0.6 + analysis.SeverityScore * 0.4;
		}
		if (a.TargetBottlenecks.Contains(analysis.SecondaryBottleneck))
		{
			return 0.3 + analysis.SeverityScore * 0.2;
		}
		return 0.1;
	}

	private double Risk(OptimizerAction a, AntiStutterSnapshot s, BottleneckAnalysis analysis)
	{
		ActionSafetyLevel safetyLevel = a.SafetyLevel;

		double num = safetyLevel switch
		{
			ActionSafetyLevel.Safe => 0.05, 
			ActionSafetyLevel.Conditional => 0.25, 
			ActionSafetyLevel.Risky => 0.6, 
			_ => 0.5};

		double num2 = num;
		if (s.IsGame && a.Category == ActionCategory.ServiceManagement)
		{
			num2 += 0.2;
		}
		if (analysis.SeverityScore < 0.5)
		{
			num2 += 0.1;
		}
		return Math.Clamp(num2, 0.0, 1.0);
	}

	private void Register()
	{
		_actions.Add(new OptimizerAction
		{
			Name = "ElevateForegroundPriority",
			Description = "Define prioridade AboveNormal para processão ativo",
			Category = ActionCategory.ProcessPriority,
			SafetyLevel = ActionSafetyLevel.Safe,
			TargetBottlenecks = new BottleneckType[2]
			{
				BottleneckType.CpuSingleCore,
				BottleneckType.CpuMultiCore
			},
			RequiresAdmin = false,
			CooldownMs = 5000
		});
		_actions.Add(new OptimizerAction
		{
			Name = "EnsureHighPerformancePlan",
			Description = "Ativa plano de energia de alto desempenho",
			Category = ActionCategory.PowerPlan,
			SafetyLevel = ActionSafetyLevel.Safe,
			TargetBottlenecks = new BottleneckType[2]
			{
				BottleneckType.PowerPlan,
				BottleneckType.CpuSingleCore
			},
			RequiresAdmin = true,
			CooldownMs = 30000,
			CanExecute = (AntiStutterSnapshot s, BottleneckAnalysis _) => !s.IsHighPerformance && !s.IsOnBattery
		});
		_actions.Add(new OptimizerAction
		{
			Name = "ReduceBackgroundLoad",
			Description = "Sugere redução de processãos pesados",
			Category = ActionCategory.CpuManagement,
			SafetyLevel = ActionSafetyLevel.Conditional,
			TargetBottlenecks = new BottleneckType[2]
			{
				BottleneckType.CpuMultiCore,
				BottleneckType.RamPressure
			},
			RequiresAdmin = false,
			CooldownMs = 15000
		});
		_actions.Add(new OptimizerAction
		{
			Name = "PauseIndexing",
			Description = "Pausa indexação durante uso intenso",
			Category = ActionCategory.ServiceManagement,
			SafetyLevel = ActionSafetyLevel.Conditional,
			TargetBottlenecks = new BottleneckType[2]
			{
				BottleneckType.DiskHdd,
				BottleneckType.CpuSingleCore
			},
			RequiresAdmin = true,
			CooldownMs = 60000,
			CanExecute = (AntiStutterSnapshot s, BottleneckAnalysis _) => s.IsGame
		});
		_actions.Add(new OptimizerAction
		{
			Name = "ForceHighPriority",
			Description = "Define prioridade High (no realtime)",
			Category = ActionCategory.ProcessPriority,
			SafetyLevel = ActionSafetyLevel.Risky,
			TargetBottlenecks = new BottleneckType[1] { BottleneckType.CpuSingleCore },
			RequiresAdmin = false,
			CooldownMs = 60000,
			CanExecute = (AntiStutterSnapshot s, BottleneckAnalysis a) => s.IsGame && a.SeverityScore > 0.7
		});
		_actions.Add(new OptimizerAction
		{
			Name = "DisableTelemetryTemporary",
			Description = "Desativa telemetria temporariamente",
			Category = ActionCategory.ServiceManagement,
			SafetyLevel = ActionSafetyLevel.Risky,
			TargetBottlenecks = new BottleneckType[1] { BottleneckType.CpuMultiCore },
			RequiresAdmin = true,
			CooldownMs = 300000
		});
	}

	private static bool IsAdmin()
	{
		try
		{
			WindowsIdentity current = WindowsIdentity.GetCurrent();
			WindowsPrincipal windowsPrincipal = new WindowsPrincipal(current);
			return windowsPrincipal.IsInRole(WindowsBuiltInRole.Administrator);
		}
		catch
		{
			return false;
		}
	}
}
