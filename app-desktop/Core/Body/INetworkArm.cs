using System;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Core.Body;

public interface INetworkArm : IDisposable
{
	string ActiveNicName { get; }

	string ActiveNicGuid { get; }

	Task<ArmActionResult> OptimizeForGamingAsync(string gameProcessName);

	Task<ArmActionResult> SetInterruptAffinityAsync();

	Task<ArmActionResult> RestoreNetworkDefaultsAsync();
}
