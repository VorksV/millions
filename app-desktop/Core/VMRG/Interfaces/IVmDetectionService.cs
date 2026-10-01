using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.VMRG.Models;

namespace VoltrisOptimizer.Core.VMRG.Interfaces
{
    public interface IVmDetectionService
    {
        Task<VmDetectionResult> DetectAllAsync(CancellationToken ct);
        void RegisterProvider(IVmDetectionProvider provider);
    }
}
