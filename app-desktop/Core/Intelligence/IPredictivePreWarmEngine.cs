using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Core.Intelligence;

public interface IPredictivePreWarmEngine
{
	bool IsPreWarming { get; }

	Task StartAsync(CancellationToken ct);

	Task StopAsync();

	Task CheckAndPreWarmAsync();
}
