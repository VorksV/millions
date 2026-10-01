using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Core.Brain.V2.AntiStutter;

public sealed class AntiStutterDecisionEngine
{
	private readonly ILoggingService _logger;

	private readonly AntiStutterActionRegistry _registry;

	private readonly AntiStutterAnalyzer _analyzer;

	private const double MIN_SEVERITY = 0.4;

	private const double MIN_SCORE = 0.5;

	private const double EMERGENCY_THRESHOLD = 0.8;

	private const int MAX_ACTIONS = 3;

	private UserProfile _profile = UserProfile.Balanced;

	private AntiStutterDecision? _last;

	public UserProfile CurrentProfile
	{
		get
		{
			return _profile;
		}
		set
		{
			if (_profile != value)
			{
				ILoggingService logger = _logger;
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(24, 2);
				defaultInterpolatedStringHandler.AppendLiteral("[Decision] Profile: ");
				defaultInterpolatedStringHandler.AppendFormatted(_profile);
				defaultInterpolatedStringHandler.AppendLiteral(" -> ");
				defaultInterpolatedStringHandler.AppendFormatted(value);
				logger.LogInfo(defaultInterpolatedStringHandler.ToStringAndClear());
				_profile = value;
			}
		}
	}

	public event EventHandler<AntiStutterDecision>? DecisionMade;

	public AntiStutterDecisionEngine(ILoggingService logger, AntiStutterActionRegistry registry, AntiStutterAnalyzer analyzer)
	{
		_logger = logger;
		_registry = registry;
		_analyzer = analyzer;
	}

	public AntiStutterDecision Decide(AntiStutterSnapshot snapshot, bool gamerMode = false)
	{
		if (snapshot == null)
		{
			return new AntiStutterDecision
			{
				ShouldAct = false,
				Reasoning = "Snapshot nulo"
			};
		}
		BottleneckAnalysis bottleneckAnalysis = _analyzer.Analyze(snapshot);
		if (!bottleneckAnalysis.HasBottleneck || bottleneckAnalysis.SeverityScore < 0.4)
		{
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(19, 1);
			defaultInterpolatedStringHandler.AppendLiteral("Severidade baixa (");
			defaultInterpolatedStringHandler.AppendFormatted(bottleneckAnalysis.SeverityScore, "F2");
			defaultInterpolatedStringHandler.AppendLiteral(")");
			AntiStutterDecision obj = new AntiStutterDecision
			{
				ShouldAct = false,
				Analysis = bottleneckAnalysis,
				Reasoning = defaultInterpolatedStringHandler.ToStringAndClear()
			};
			return obj;
		}
		bool flag = snapshot.StutterDetected && bottleneckAnalysis.SeverityScore >= 0.8;
		List<(OptimizerAction, ActionScore)> list = BuildCandidates(snapshot, bottleneckAnalysis, gamerMode, flag);
		if (!list.Any())
		{
			return new AntiStutterDecision
			{
				ShouldAct = false,
				Analysis = bottleneckAnalysis,
				Reasoning = "Sem ações válidas"
			};
		}
		List<OptimizerAction> list2 = SelectTop(list, flag);
		AntiStutterDecision antiStutterDecision = (_last = new AntiStutterDecision
		{
			ShouldAct = list2.Any(),
			SelectedActions = list2,
			Analysis = bottleneckAnalysis,
			ConfidenceScore = ComputeConfidence(list2, bottleneckAnalysis),
			ProfileUsed = _profile,
			IsEmergencyProtocol = flag,
			Reasoning = BuildReasoning(bottleneckAnalysis, list2, flag)
		});
		this.DecisionMade?.Invoke(this, antiStutterDecision);
		_logger.LogInfo("[Decision] " + antiStutterDecision.Reasoning);
		return antiStutterDecision;
	}

	private List<(OptimizerAction action, ActionScore score)> BuildCandidates(AntiStutterSnapshot snap, BottleneckAnalysis analysis, bool gamerMode, bool emergency)
	{
		IReadOnlyList<OptimizerAction> @for = _registry.GetFor(analysis);
		List<(OptimizerAction, ActionScore)> list = new List<(OptimizerAction, ActionScore)>();
		foreach (OptimizerAction item in @for)
		{
			if (item.CanExecute(snap, analysis))
			{
				ActionScore s = _registry.Score(item, snap, analysis);
				s = ApplyProfile(s, item);
				if ((item.SafetyLevel != ActionSafetyLevel.Risky || (emergency && !snap.IsOnBattery)) && (!gamerMode || item.Category != ActionCategory.ServiceManagement || emergency) && s.FinalScore >= 0.5)
				{
					list.Add((item, s));
				}
			}
		}
		return list.OrderByDescending(((OptimizerAction, ActionScore) x) => x.Item2.FinalScore).ToList();
	}

	private List<OptimizerAction> SelectTop(List<(OptimizerAction action, ActionScore score)> candidates, bool emergency)
	{
		List<OptimizerAction> list = new List<OptimizerAction>();
		foreach (var (optimizerAction, actionScore) in candidates)
		{
			if (list.Count >= 3)
			{
				break;
			}
			if (optimizerAction.SafetyLevel == ActionSafetyLevel.Safe)
			{
				list.Add(optimizerAction);
			}
			else if (optimizerAction.SafetyLevel == ActionSafetyLevel.Conditional && actionScore.FinalScore > 0.6)
			{
				list.Add(optimizerAction);
			}
			else if (emergency && optimizerAction.SafetyLevel == ActionSafetyLevel.Risky && actionScore.FinalScore > 0.8)
			{
				list.Add(optimizerAction);
			}
		}
		return list;
	}

	private ActionScore ApplyProfile(ActionScore s, OptimizerAction a)
	{
		double compatibilityScore = s.CompatibilityScore;
		double num = s.ExpectedGainScore;
		double num2 = s.RiskScore;
		switch (_profile)
		{
		case UserProfile.GamerCompetitive:
			num *= 1.2;
			num2 *= 0.85;
			break;
		case UserProfile.QualityVisual:
			num2 *= 1.3;
			break;
		case UserProfile.NotebookThermal:
			num2 = ((a.Category != ActionCategory.PowerPlan) ? (num2 * 1.2) : (num2 * 0.7));
			break;
		}
		return new ActionScore
		{
			CompatibilityScore = Math.Clamp(compatibilityScore, 0.0, 1.0),
			ExpectedGainScore = Math.Clamp(num, 0.0, 1.0),
			RiskScore = Math.Clamp(num2, 0.0, 1.0),
			Reasoning = s.Reasoning
		};
	}

	private double ComputeConfidence(List<OptimizerAction> actions, BottleneckAnalysis analysis)
	{
		if (!actions.Any())
		{
			return 0.0;
		}
		double severityScore = analysis.SeverityScore;
		double num = actions.Average((OptimizerAction a) => (a.SafetyLevel == ActionSafetyLevel.Safe) ? 0.2 : ((a.SafetyLevel == ActionSafetyLevel.Conditional) ? 0.1 : 0.0));
		return Math.Clamp(severityScore + num, 0.0, 1.0);
	}

	private string BuildReasoning(BottleneckAnalysis a, List<OptimizerAction> actions, bool emergency)
	{
		string value = string.Join(",", actions.Select((OptimizerAction x) => x.Name));
		string value2 = (emergency ? "[EMERGÊNCIA]" : "");
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(5, 4);
		defaultInterpolatedStringHandler.AppendFormatted(value2);
		defaultInterpolatedStringHandler.AppendLiteral(" ");
		defaultInterpolatedStringHandler.AppendFormatted(a.PrimaryBottleneck);
		defaultInterpolatedStringHandler.AppendLiteral(" (");
		defaultInterpolatedStringHandler.AppendFormatted(a.SeverityScore, "F2");
		defaultInterpolatedStringHandler.AppendLiteral(") ");
		defaultInterpolatedStringHandler.AppendFormatted(value);
		return defaultInterpolatedStringHandler.ToStringAndClear();
	}
}
