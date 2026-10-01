using System;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Core.Body;

public interface IPowerArm : IDisposable
{
	int CurrentEpp { get; }

	Guid CurrentPowerPlanGuid { get; }

	int CurrentSystemResponsiveness { get; }

	int CurrentTurboBoostPolicy { get; }

	Task<EppChangeResult> SetEppAsync(int value);

	Task<bool> SetPowerPlanAsync(Guid guid, string name);

	Task<bool> SetSystemResponsivenessAsync(int value);

	Task<bool> SetTurboBoostPolicyAsync(int mode);

	Task<bool> SetCoreParkingAsync(int minPercent, int maxPercent);

	// P1: Ultimate Performance Plan
	Task<bool> SetUltimatePerformancePlanAsync(bool enable);
}
