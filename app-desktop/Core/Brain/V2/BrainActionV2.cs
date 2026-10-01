using System.Runtime.CompilerServices;

namespace VoltrisOptimizer.Core.Brain.V2;

public sealed class BrainActionV2
{
	public BrainActionKind Kind { get; init; }

	public int Param { get; init; }

	public int? TargetPid { get; init; }

	public string? TargetProcessName { get; init; }

	public string Reason { get; init; } = string.Empty;


	public string ActionId
	{
		get
		{
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(2, 2);
			defaultInterpolatedStringHandler.AppendFormatted(Kind);
			defaultInterpolatedStringHandler.AppendLiteral("(");
			defaultInterpolatedStringHandler.AppendFormatted(Param);
			defaultInterpolatedStringHandler.AppendLiteral(")");
			return defaultInterpolatedStringHandler.ToStringAndClear();
		}
	}

	public static BrainActionV2 NoOp()
	{
		return new BrainActionV2
		{
			Kind = BrainActionKind.NoAction,
			Reason = "no-op"
		};
	}

	public override string ToString()
	{
		return ActionId + " " + Reason;
	}
}
