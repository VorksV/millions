using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Gamer.Fluidity
{
    // STUBS - VFE Interfaces (V2 Architecture - desativadas)
    public interface IVfePerformanceEngine { }
    public interface IVfeDecisionEngine { }
    public interface IVfeIntelligentRollbackEngine { }
    public interface IVfePerformanceLearningEngine { }
    public interface IVfeSessionAnalyticsEngine { }
    public interface IEngineDetectionService { }
    public interface ICpuSchedulerEngine { }
    public interface IBackgroundGovernor { }
    public interface IValidationEngine { }
    public interface IDriverLatencyEngine { }
    public interface IVramEngine { }
    public interface IStorageEngine { }
    public interface IInputLatencyEngine { }
    public interface IThermalPredictionEngine { }

    // STUB - BackgroundGovernor class
    public class BackgroundGovernor
    {
        public void SetGamePids(List<int> pidList)
        {
        }
    }
}
