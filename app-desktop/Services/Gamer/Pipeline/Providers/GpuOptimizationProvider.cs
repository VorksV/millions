using System;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Gamer.Interfaces;
using VoltrisOptimizer.Services.Gamer.Pipeline.Models;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer.Pipeline.Providers
{
    public class GpuOptimizationProvider : IOptimizationProvider
    {
        private readonly ILoggingService _logger;
        private readonly IGpuGamingOptimizer _gpuOptimizer;
        private bool _wasApplied;

        public string Id => "gpu-max-performance";
        public string DisplayName => "GPU Modo Máximo Desempenho";
        public string Category => "GPU";
        public double BaseBenefitScore => 6;
        public double BaseRiskScore => 2;
        public bool HasRollback => true;

        public GpuOptimizationProvider(ILoggingService logger, IGpuGamingOptimizer gpuOptimizer)
        {
            _logger.LogEntry(nameof(GpuOptimizationProvider));
            _logger = logger;
            _gpuOptimizer = gpuOptimizer;
            _logger.LogExit(nameof(GpuOptimizationProvider));
        }

        public bool CanHandle(GamerSessionContext context)
        {
            _logger.LogEntry(nameof(CanHandle));
            _logger.LogDebug("[GpuOptimizationProvider.CanHandle] Entry");
            var result = context.Policies.AllowGpuChanges
                && context.Hardware.IsDiscreteGpu;
            _logger.LogDebug($"[GpuOptimizationProvider.CanHandle] Exit: {result}");
            _logger.LogExit(nameof(CanHandle), result);
            return result;
        }

        public double CalculateBenefitScore(GamerSessionContext context)
        {
            _logger.LogEntry(nameof(CalculateBenefitScore));
            _logger.LogDebug("[GpuOptimizationProvider.CalculateBenefitScore] Entry");
            double score = BaseBenefitScore;
            if (context.Goals.PrioritizeFpsStability) score += 2;
            if (context.Hardware.GpuVideoMemoryMB >= 6000) score += 1;
            _logger.LogDebug($"[GpuOptimizationProvider.CalculateBenefitScore] Exit: {Math.Min(score, 10)}");
            _logger.LogExit(nameof(CalculateBenefitScore), Math.Min(score, 10));
            return Math.Min(score, 10);
        }

        public async Task<bool> ApplyAsync(CancellationToken ct)
        {
            _logger.LogEntry(nameof(ApplyAsync));
            _logger.LogInfo("[GpuOptimizationProvider.ApplyAsync] Entry");
            try
            {
                _logger.LogInfo("[GPUProvider] 🎮 Otimizando GPU para máximo desempenho...");
                var ok = await _gpuOptimizer.OptimizeAsync(ct);
                _wasApplied = ok;
                if (ok) _logger.LogInfo("[GPUProvider] ✅ GPU otimizada");
                else _logger.LogWarning("[GPUProvider] ⚠ Falha na otimização GPU");
                _logger.LogInfo("[GpuOptimizationProvider.ApplyAsync] Exit");
                _logger.LogExit(nameof(ApplyAsync), ok);
                return ok;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GPUProvider] ❌ Erro: {ex.Message}");
                _logger.LogInfo("[GpuOptimizationProvider.ApplyAsync] Exit (error)");
                _logger.LogExit(nameof(ApplyAsync), false);
                return false;
            }
        }

        public async Task<bool> RollbackAsync(CancellationToken ct)
        {
            _logger.LogEntry(nameof(RollbackAsync));
            _logger.LogInfo("[GpuOptimizationProvider.RollbackAsync] Entry");
            try
            {
                _logger.LogInfo("[GPUProvider] 🔄 Restaurando GPU...");
                var ok = await _gpuOptimizer.RestoreAsync(ct);
                _wasApplied = false;
                _logger.LogExit(nameof(RollbackAsync), ok);
                return ok;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GPUProvider] ❌ Erro no rollback: {ex.Message}");
                _logger.LogInfo("[GpuOptimizationProvider.RollbackAsync] Exit (error)");
                _logger.LogExit(nameof(RollbackAsync), false);
                return false;
            }
        }
    }
}
