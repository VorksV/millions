using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.VMRG.Models;

namespace VoltrisOptimizer.Core.VMRG.Interfaces
{
    public interface IVmValidationService
    {
        Task<VmValidationResult> ValidateAsync(VmInfo before, VmInfo after, VmDecision decision, CancellationToken ct);
    }
}
