using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Core.SystemIntelligenceProfiler
{
    public interface IAuditCollector
    {
        Task<AuditData> CollectAsync(CancellationToken ct);
    }
}

