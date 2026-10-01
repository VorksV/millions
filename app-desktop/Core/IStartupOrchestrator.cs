using System.Threading.Tasks;

namespace VoltrisOptimizer.Core;

public interface IStartupOrchestrator
{
	Task RunAsync();
}
