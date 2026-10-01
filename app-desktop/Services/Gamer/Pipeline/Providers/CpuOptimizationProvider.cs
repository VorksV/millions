using System;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Gamer.Interfaces;
using VoltrisOptimizer.Services.Gamer.Pipeline.Models;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer.Pipeline.Providers
{
    public class CpuOptimizationProvider : IOptimizationProvider
    {
        private readonly ILoggingService _logger;
        private readonly ICpuGamingOptimizer _cpuOptimizer;
        private bool _wasApplied;

        public string Id => "cpu-gaming-optimization";
        public string DisplayName => "Otimização de CPU para Jogos";
        public string Category => "CPU";
        public double BaseBenefitScore => 6;
        public double BaseRiskScore => 2;
        public bool HasRollback => true;

        public CpuOptimizationProvider(ILoggingService logger, ICpuGamingOptimizer cpuOptimizer)
        {
            _logger.LogEntry(nameof(CpuOptimizationProvider));
            _logger = logger;
            _cpuOptimizer = cpuOptimizer;
            _logger.LogExit(nameof(CpuOptimizationProvider));
        }

        public bool CanHandle(GamerSessionContext context)
        {
            _logger.LogEntry(nameof(CanHandle));
            _logger.LogDebug("[CpuOptimizationProvider.CanHandle] Entry");
            var result = context.Hardware.LogicalCores >= 4
                && context.Goals.PrioritizeCpuThroughput;
            _logger.LogDebug($"[CpuOptimizationProvider.CanHandle] Exit: {result}");
            _logger.LogExit(nameof(CanHandle), result);
            return result;
        }

        public double CalculateBenefitScore(GamerSessionContext context)
        {
            _logger.LogEntry(nameof(CalculateBenefitScore));
            _logger.LogDebug("[CpuOptimizationProvider.CalculateBenefitScore] Entry");
            double score = BaseBenefitScore;
            if (context.Game.IsCpuBound) score += 3;
            if (context.Hardware.IsHybrid) score += 2;
            _logger.LogDebug($"[CpuOptimizationProvider.CalculateBenefitScore] Exit: {Math.Min(score, 10)}");
            _logger.LogExit(nameof(CalculateBenefitScore), Math.Min(score, 10));
            return Math.Min(score, 10);
        }

        public async Task<bool> ApplyAsync(CancellationToken ct)
        {
            _logger.LogEntry(nameof(ApplyAsync));
            _logger.LogInfo("[CpuOptimizationProvider.ApplyAsync] Entry");
            try
            {
                _logger.LogInfo("[CPUProvider] ⚡ Otimizando CPU...");
                var ok = await _cpuOptimizer.OptimizeAsync(ct);
                _wasApplied = ok;
                if (ok) _logger.LogInfo("[CPUProvider] ✅ CPU otimizada");
                _logger.LogInfo("[CpuOptimizationProvider.ApplyAsync] Exit");
                _logger.LogExit(nameof(ApplyAsync), ok);
                return ok;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[CPUProvider] ❌ Erro: {ex.Message}");
                _logger.LogInfo("[CpuOptimizationProvider.ApplyAsync] Exit (error)");
                _logger.LogExit(nameof(ApplyAsync), false);
                return false;
            }
        }

        public async Task<bool> RollbackAsync(CancellationToken ct)
        {
            _logger.LogEntry(nameof(RollbackAsync));
            _logger.LogInfo("[CpuOptimizationProvider.RollbackAsync] Entry");
            try
            {
                _logger.LogInfo("[CPUProvider] 🔄 Restaurando CPU...");
                var ok = await _cpuOptimizer.RestoreAsync(ct);
                _wasApplied = false;
                _logger.LogExit(nameof(RollbackAsync), ok);
                return ok;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[CPUProvider] ❌ Erro no rollback: {ex.Message}");
                _logger.LogInfo("[CpuOptimizationProvider.RollbackAsync] Exit (error)");
                _logger.LogExit(nameof(RollbackAsync), false);
                return false;
            }
        }
    }
}
