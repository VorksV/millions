using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace VoltrisOptimizer.Core.Optimization;

public sealed class VoltriScoreEvaluator
{
	public VoltriScoreReport Evaluate(VoltriScoreSnapshot before, VoltriScoreSnapshot after, IReadOnlyList<StartupAccelerationStepResult> steps)
	{
		int num = steps.Where((StartupAccelerationStepResult s) => s.Success).Sum((StartupAccelerationStepResult s) => s.EstimatedImpactPoints);
		double num2 = Math.Clamp((double)num * 4.25, 0.0, 100.0);
		double num3 = Math.Clamp(before.CpuPercent - after.CpuPercent, -25.0, 25.0);
		double num4 = Math.Clamp((after.AvailableRamMb - before.AvailableRamMb) / 128.0, -10.0, 20.0);
		double num5 = Math.Clamp((double)(before.LastInputMs - after.LastInputMs) / 1000.0, -10.0, 20.0);
		double num6 = Math.Clamp(num3 * 0.55 + num4 * 0.3 + num5 * 0.15, 0.0, 100.0);
		VoltriImpactLevel impactLevel = ((num6 >= 22.0) ? VoltriImpactLevel.Wow : ((num6 >= 12.0) ? VoltriImpactLevel.High : ((num6 >= 5.0) ? VoltriImpactLevel.Medium : VoltriImpactLevel.Low)));
		long num7 = Math.Max(0L, before.LastInputMs - after.LastInputMs);
		double num8 = after.AvailableRamMb - before.AvailableRamMb;
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(65, 6);
		defaultInterpolatedStringHandler.AppendLiteral("VoltriScore estimado ");
		defaultInterpolatedStringHandler.AppendFormatted(num2, "F1");
		defaultInterpolatedStringHandler.AppendLiteral("% e real ");
		defaultInterpolatedStringHandler.AppendFormatted(num6, "F1");
		defaultInterpolatedStringHandler.AppendLiteral("% (");
		defaultInterpolatedStringHandler.AppendFormatted(impactLevel.ToString().ToUpperInvariant());
		defaultInterpolatedStringHandler.AppendLiteral("). CPU = ");
		defaultInterpolatedStringHandler.AppendFormatted(num3, "F1");
		defaultInterpolatedStringHandler.AppendLiteral("%|RAM + ");
		defaultInterpolatedStringHandler.AppendFormatted(num8, "F0");
		defaultInterpolatedStringHandler.AppendLiteral(" MB|Input - ");
		defaultInterpolatedStringHandler.AppendFormatted(num7, "F0");
		defaultInterpolatedStringHandler.AppendLiteral(" ms");
		string summary = defaultInterpolatedStringHandler.ToStringAndClear();
		VoltriScoreReport obj = new VoltriScoreReport
		{
			TimestampUtc = DateTime.UtcNow,
			EstimatedGain = num2,
			RealGain = num6,
			ImpactLevel = impactLevel,
			CpuReductionPercent = num3,
			AvailableRamGainMb = num8,
			InputLatencyImprovementMs = num7,
			Summary = summary
		};
		return obj;
	}
}
