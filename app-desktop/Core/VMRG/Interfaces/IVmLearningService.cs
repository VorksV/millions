using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.VMRG.Models;

namespace VoltrisOptimizer.Core.VMRG.Interfaces
{
    public interface IVmLearningService
    {
        Task RecordOutcomeAsync(VmLearningEntry entry, CancellationToken ct);
        Task<bool> ShouldApplyActionAsync(VmHypervisor hypervisor, VmDecision.DecisionType action, VmContext context, CancellationToken ct);
        Task LoadAsync(CancellationToken ct);
        Task SaveAsync(CancellationToken ct);
    }
}
