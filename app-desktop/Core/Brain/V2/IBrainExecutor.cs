using System;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Core.Brain.V2;

public interface IBrainExecutor : IDisposable
{
	Task<BrainActionResultV2> ExecuteAsync(BrainActionV2 action, SensorSnapshot context);

	bool ExecutePowerPlan(string planGuid, string requestedBy);

	Task RestoreOriginalsAsync();
}
