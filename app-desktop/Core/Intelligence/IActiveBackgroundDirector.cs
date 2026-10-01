using System;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Core.Intelligence;

public interface IActiveBackgroundDirector
{
	bool IsActive { get; }

	int SuppressedProcessCount { get; }

	long MemoryFreedMbSession { get; }

	CpuTopologyInfo DetectedTopology { get; }

	event EventHandler<BackgroundDirectorEventArgs>? OnActionTaken;

	event EventHandler<string>? OnForegroundContextChanged;

	Task StartAsync(CancellationToken ct);

	Task StopAsync();

	Task ApplyGamingOptimizationsAsync(string process);

	Task RestoreAllAsync();
}
