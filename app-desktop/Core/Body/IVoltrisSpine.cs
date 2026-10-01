using System;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Core.Body;

public interface IVoltrisSpine : IDisposable
{
	BaselineState CurrentBaseline { get; }

	TimeSpan WatchdogInterval { get; set; }

	bool IsWatchdogRunning { get; }

	event EventHandler<HealthCheckEventArgs> OnHealthCheck;

	event EventHandler<AnomalyDetectedEventArgs> OnAnomalyDetected;

	event EventHandler<RollbackCompletedEventArgs> OnRollbackCompleted;

	Task StartAsync(CancellationToken ct = default(CancellationToken));

	Task StopAsync();

	Task CaptureBaselineAsync();

	Task<RollbackResult> RollbackAllAsync();

	HealthCheckResult CheckHealth();

	Task<SystemHealthReport> DiagnoseAsync();
}
