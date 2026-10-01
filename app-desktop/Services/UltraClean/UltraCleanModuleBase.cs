using System;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.UltraClean
{
    public abstract class UltraCleanModuleBase : IUltraCleanModule
    {
        protected readonly ILoggingService _logger;

        public abstract string Id { get; }
        public abstract string Name { get; }
        public abstract string Description { get; }
        public abstract string Category { get; }
        public virtual ModuleSafetyLevel Safety => ModuleSafetyLevel.Safe;
        public virtual ModuleRiskLevel Risk => ModuleRiskLevel.Low;
        public virtual bool RequiresAdmin => false;
        public virtual bool IsRecommended => true;

        protected UltraCleanModuleBase(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogTrace($"[UltraClean] Módulo carregado: {GetType().Name}");
        }

        public abstract Task<long> AnalyzeAsync(CancellationToken ct);
        public abstract Task<bool> CleanAsync(CancellationToken ct, IProgress<double>? progress = null);
        
        protected void ReportProgress(IProgress<double>? progress, double value)
        {
            progress?.Report(value);
        }
    }
}
