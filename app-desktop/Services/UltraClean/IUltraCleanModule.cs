using System;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.UltraClean
{
    public enum ModuleSafetyLevel
    {
        Safe,
        Moderate,
        Risky
    }

    public enum ModuleRiskLevel
    {
        Low,
        Medium,
        High,
        Extreme
    }

    public interface IUltraCleanModule
    {
        string Id { get; }
        string Name { get; }
        string Description { get; }
        string Category { get; }
        ModuleSafetyLevel Safety { get; }
        ModuleRiskLevel Risk { get; }
        bool RequiresAdmin { get; }
        bool IsRecommended { get; }

        Task<long> AnalyzeAsync(CancellationToken ct);
        Task<bool> CleanAsync(CancellationToken ct, IProgress<double>? progress = null);
    }
}
