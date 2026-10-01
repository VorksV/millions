using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.VMRG.Models;

namespace VoltrisOptimizer.Core.VMRG.Interfaces
{
    public interface IVmDecisionEngine
    {
        Task<VmDecision> DecideAsync(VmInfo vm, VmContext context, CancellationToken ct);
    }

    public class VmContext
    {
        public double SystemCpuPercent { get; set; }
        public double SystemRamPercent { get; set; }
        public double SystemDiskPercent { get; set; }
        public bool GamerModeActive { get; set; }
        public bool GameRunning { get; set; }
        public long AvailableRamMb { get; set; }
        public int LastInputMs { get; set; }
        public double ResponsivenessScore { get; set; }
    }
}
