using System;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Core.Brain.V2;

public interface IBrainSensor : IDisposable
{
	SensorSnapshot? CurrentSnapshot { get; }

	event EventHandler<SensorSnapshot>? SnapshotProduced;

	Task StartAsync(CancellationToken ct);

	Task StopAsync();
}
