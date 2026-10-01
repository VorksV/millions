using System;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Core.Body;

public interface IVoltrisLegs : IDisposable
{
	TimeSpan PatrolInterval { get; set; }

	int RequiredConfirmationCycles { get; }

	OperationalContext DetectedContext { get; }

	string LastDetectedProcess { get; }

	int CurrentConfirmationCount { get; }

	event EventHandler<ContextConfirmedEventArgs> OnContextConfirmed;

	Task StartAsync(CancellationToken ct = default(CancellationToken));

	Task StopAsync();

	Task<OperationalContext> ForceDetectionAsync();
}
