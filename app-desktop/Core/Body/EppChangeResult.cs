namespace VoltrisOptimizer.Core.Body;

public sealed class EppChangeResult
{
	public bool Executed { get; init; }

	public int OldValue { get; init; }

	public int NewValue { get; init; }

	public int Delta { get; init; }

	public bool CooldownBlocked { get; init; }

	public bool RequiresAdmin { get; init; }

	public string? Error { get; init; }

	public double SecondsSinceLastChange { get; init; }
}
