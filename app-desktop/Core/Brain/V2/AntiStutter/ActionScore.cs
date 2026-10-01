namespace VoltrisOptimizer.Core.Brain.V2.AntiStutter;

public sealed class ActionScore
{
	public double CompatibilityScore { get; init; }

	public double ExpectedGainScore { get; init; }

	public double RiskScore { get; init; }

	public double FinalScore => CompatibilityScore * 0.3 + ExpectedGainScore * 0.5 + (1.0 - RiskScore) * 0.2;

	public string Reasoning { get; init; } = string.Empty;

}
