using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.VMRG.Models;

namespace VoltrisOptimizer.Core.VMRG.Interfaces
{
    public interface IVmDetectionProvider
    {
        string ProviderName { get; }
        VmHypervisor Hypervisor { get; }
        Task<List<VmInfo>> DetectAsync(CancellationToken ct);
    }
}
