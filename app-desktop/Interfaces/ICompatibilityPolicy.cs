using VoltrisOptimizer.Core.SystemIntelligenceProfiler;

namespace VoltrisOptimizer.Interfaces
{
    public interface ICompatibilityPolicy
    {
        bool IsAllowed(ActionRecommendation recommendation, AuditData audit);
    }
}
