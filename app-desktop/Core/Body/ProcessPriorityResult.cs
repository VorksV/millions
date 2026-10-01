namespace VoltrisOptimizer.Core.Body;

/// <summary>
/// Desfecho real de uma alteração de prioridade de processo.
/// <see cref="Success"/> só é verdadeiro quando a prioridade foi efetivamente
/// alterada e confirmada por releitura.
/// </summary>
public sealed class ProcessPriorityResult
{
	/// <summary>Verdadeiro somente se a prioridade foi aplicada E confirmada por releitura.</summary>
	public bool Success { get; init; }

	/// <summary>Verdadeiro quando a alteração foi recusada por proteção de processo.</summary>
	public bool BlockedByBlacklist { get; init; }

	public int Pid { get; init; }

	public string ProcessName { get; init; } = string.Empty;

	/// <summary>Prioridade efetivamente aplicada (confirmada por releitura).</summary>
	public ProcessPriorityClass AppliedPriority { get; init; }

	/// <summary>Prioridade que o processo tinha antes da alteração.</summary>
	public ProcessPriorityClass? PreviousPriority { get; init; }

	/// <summary>Motivo da falha quando <see cref="Success"/> é falso.</summary>
	public string Error { get; init; } = string.Empty;
}
