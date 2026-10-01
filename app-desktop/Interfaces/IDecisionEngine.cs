using System.Collections.Generic;

namespace VoltrisOptimizer.Core.SystemIntelligenceProfiler
{
    public interface IDecisionEngine
    {
        ProfilerReport Evaluate(AuditData audit, UserAnswers answers);
    }
}

