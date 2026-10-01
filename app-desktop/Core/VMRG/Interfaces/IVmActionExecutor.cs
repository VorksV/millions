using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.VMRG.Models;

namespace VoltrisOptimizer.Core.VMRG.Interfaces
{
    public interface IVmActionExecutor
    {
        Task<bool> ExecuteAsync(VmDecision decision, CancellationToken ct);
        Task<bool> RollbackAsync(VmInfo vm, CancellationToken ct);
        Task<bool> RestoreAllAsync(CancellationToken ct);
    }
}
