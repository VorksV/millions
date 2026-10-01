using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Core.VMRG.Interfaces
{
    public interface IVmrgOrchestrator
    {
        bool IsRunning { get; }
        Task StartAsync(CancellationToken ct);
        Task StopAsync();
    }
}
