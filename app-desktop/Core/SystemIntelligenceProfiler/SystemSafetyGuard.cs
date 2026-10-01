using VoltrisOptimizer.Core.SystemIntelligenceProfiler;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Core.SystemIntelligenceProfiler
{
    public class SystemSafetyGuard
    {
        private readonly ICompatibilityPolicy _policy;
        private readonly ILoggingService _logger;

        public SystemSafetyGuard(ICompatibilityPolicy policy, ILoggingService logger)
        {
            _policy = policy;
            _logger = logger;
        }

        public bool IsAllowed(ActionRecommendation recommendation, AuditData audit)
        {
            var ok = _policy.IsAllowed(recommendation, audit);
            if (!ok)
            {
                _logger.LogWarning("[SAFETY] Bloqueado por política de compatibilidade: " + recommendation.Name);
            }
            return ok;
        }
    }
}
