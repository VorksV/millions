using VoltrisOptimizer.Services.Intelligence.VPIS.Models;

namespace VoltrisOptimizer.Services.Intelligence.VPIS.Engines
{
    public interface IVpisDiagnosticEngine
    {
        string EngineName { get; }
        void StartMonitoring(string processName);
        void StopMonitoring();
        PerformanceInsightEvent AnalyzeTick();
    }
}
