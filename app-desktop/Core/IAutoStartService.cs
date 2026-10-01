using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Core;

public interface IAutoStartService
{
	Task StartAsync(CancellationToken cancellationToken);
}
