using System;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.Brain.V2;

namespace VoltrisOptimizer.Core.Body;

public interface IVoltrisBody : IDisposable
{
	SystemHealthState CurrentHealth { get; }

	VoltrisBrainV2.ProfileType ActiveProfile { get; }

	OperationalContext CurrentContext { get; }

	bool IsGamingModeActive { get; }

	float CurrentEpp { get; }

	float CpuTempCelsius { get; }

	string ForegroundProcessName { get; }

	event EventHandler<ProfileChangedEventArgs> OnProfileChanged;

	event EventHandler<ContextChangedEventArgs> OnContextChanged;

	event EventHandler<ThermalAlertEventArgs> OnThermalAlert;

	event EventHandler<DecisionExecutedEventArgs> OnDecisionExecuted;

	Task StartAsync(CancellationToken ct = default(CancellationToken));

	Task StopAsync();

	Task<DecisionResult> DispatchDecisionAsync(BrainDecision decision);

	Task SetActiveProfileAsync(VoltrisBrainV2.ProfileType profile);
}
