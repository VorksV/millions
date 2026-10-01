using System;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Gamer.Interfaces;
using VoltrisOptimizer.Services.Gamer.Pipeline.Models;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer.Pipeline.Providers
{
    public class MemoryOptimizationProvider : IOptimizationProvider
    {
        private readonly ILoggingService _logger;
        private readonly IMemoryGamingOptimizer _memoryOptimizer;
        private bool _wasApplied;

        public string Id => "memory-cleanup";
        public string DisplayName => "Limpeza de Memória (Standby List)";
        public string Category => "Memory";
        public double BaseBenefitScore => 4;
        public double BaseRiskScore => 1;
        public bool HasRollback => false;

        public MemoryOptimizationProvider(ILoggingService logger, IMemoryGamingOptimizer memoryOptimizer)
        {
            _logger.LogEntry(nameof(MemoryOptimizationProvider));
            _logger = logger;
            _memoryOptimizer = memoryOptimizer;
            _logger.LogExit(nameof(MemoryOptimizationProvider));
        }

        public bool CanHandle(GamerSessionContext context)
        {
            _logger.LogEntry(nameof(CanHandle));
            _logger.LogDebug("[MemoryOptimizationProvider.CanHandle] Entry");
            var result = context.System.RamUsagePercent > 75
                || context.Goals.PrioritizeMemory;
            _logger.LogDebug($"[MemoryOptimizationProvider.CanHandle] Exit: {result}");
            _logger.LogExit(nameof(CanHandle), result);
            return result;
        }

        public double CalculateBenefitScore(GamerSessionContext context)
        {
            _logger.LogEntry(nameof(CalculateBenefitScore));
            _logger.LogDebug("[MemoryOptimizationProvider.CalculateBenefitScore] Entry");
            double score = BaseBenefitScore;
            if (context.System.RamUsagePercent > 85) score += 3;
            if (context.System.RamUsagePercent > 90) score += 3;
            _logger.LogDebug($"[MemoryOptimizationProvider.CalculateBenefitScore] Exit: {Math.Min(score, 10)}");
            _logger.LogExit(nameof(CalculateBenefitScore), Math.Min(score, 10));
            return Math.Min(score, 10);
        }

        public Task<bool> ApplyAsync(CancellationToken ct)
        {
            _logger.LogEntry(nameof(ApplyAsync));
            _logger.LogInfo("[MemoryOptimizationProvider.ApplyAsync] Entry");
            try
            {
                _logger.LogInfo($"[MemoryProvider] 🧠 Limpando standby list (RAM: {Environment.WorkingSet / 1024 / 1024}MB)...");
                var ok = _memoryOptimizer.CleanStandbyList();
                _wasApplied = ok;
                if (ok) _logger.LogInfo("[MemoryProvider] ✅ Standby list limpa");
                _logger.LogInfo("[MemoryOptimizationProvider.ApplyAsync] Exit");
                _logger.LogExit(nameof(ApplyAsync), ok);
                return Task.FromResult(ok);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[MemoryProvider] ❌ Erro: {ex.Message}");
                _logger.LogInfo("[MemoryOptimizationProvider.ApplyAsync] Exit (error)");
                _logger.LogExit(nameof(ApplyAsync), false);
                return Task.FromResult(false);
            }
        }

        public Task<bool> RollbackAsync(CancellationToken ct)
        {
            _logger.LogEntry(nameof(RollbackAsync));
            _logger.LogDebug("[MemoryOptimizationProvider.RollbackAsync] Entry/Exit");
            _wasApplied = false;
            _logger.LogExit(nameof(RollbackAsync), true);
            return Task.FromResult(true);
        }
    }
}
