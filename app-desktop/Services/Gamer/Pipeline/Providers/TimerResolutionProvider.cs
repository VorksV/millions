using System;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Gamer.Interfaces;
using VoltrisOptimizer.Services.Gamer.Pipeline.Models;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer.Pipeline.Providers
{
    public class TimerResolutionProvider : IOptimizationProvider
    {
        private readonly ILoggingService _logger;
        private readonly ITimerResolutionService _timerResolution;
        private bool _wasApplied;

        public string Id => "timer-resolution-max";
        public string DisplayName => "Timer Resolution 0.5ms";
        public string Category => "Kernel";
        public double BaseBenefitScore => 7;
        public double BaseRiskScore => 1;
        public bool HasRollback => true;

        public TimerResolutionProvider(ILoggingService logger, ITimerResolutionService timerResolution)
        {
            _logger.LogEntry(nameof(TimerResolutionProvider));
            _logger = logger;
            _timerResolution = timerResolution;
            _logger.LogExit(nameof(TimerResolutionProvider));
        }

        public bool CanHandle(GamerSessionContext context)
        {
            _logger.LogEntry(nameof(CanHandle));
            _logger.LogDebug("[TimerResolutionProvider.CanHandle] Entry");
            var result = context.Policies.AllowTimerResolutionChanges
                && context.System.CurrentTimerResolutionMs > 1.0;
            _logger.LogDebug($"[TimerResolutionProvider.CanHandle] Exit: {result}");
            _logger.LogExit(nameof(CanHandle), result);
            return result;
        }

        public double CalculateBenefitScore(GamerSessionContext context)
        {
            _logger.LogEntry(nameof(CalculateBenefitScore));
            _logger.LogDebug("[TimerResolutionProvider.CalculateBenefitScore] Entry");
            double score = BaseBenefitScore;
            if (context.Goals.PrioritizeLatency) score += 3;
            if (context.Goals.PrioritizeFramePacing) score += 2;
            _logger.LogDebug($"[TimerResolutionProvider.CalculateBenefitScore] Exit: {Math.Min(score, 10)}");
            _logger.LogExit(nameof(CalculateBenefitScore), Math.Min(score, 10));
            return Math.Min(score, 10);
        }

        public Task<bool> ApplyAsync(CancellationToken ct)
        {
            _logger.LogEntry(nameof(ApplyAsync));
            _logger.LogInfo("[TimerResolutionProvider.ApplyAsync] Entry");
            try
            {
                _logger.LogInfo("[TimerProvider] ⏰ Definindo timer resolution para 0.5ms...");
                var ok = _timerResolution.SetMaximumResolution();
                _wasApplied = ok;
                if (ok) _logger.LogInfo("[TimerProvider] ✅ Timer resolution: 0.5ms ativo");
                else _logger.LogWarning("[TimerProvider] ⚠ Timer resolution não pôde ser alterada");
                _logger.LogInfo("[TimerResolutionProvider.ApplyAsync] Exit");
                _logger.LogExit(nameof(ApplyAsync), ok);
                return Task.FromResult(ok);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[TimerProvider] ❌ Erro: {ex.Message}");
                _logger.LogInfo("[TimerResolutionProvider.ApplyAsync] Exit (error)");
                _logger.LogExit(nameof(ApplyAsync), false);
                return Task.FromResult(false);
            }
        }

        public Task<bool> RollbackAsync(CancellationToken ct)
        {
            _logger.LogEntry(nameof(RollbackAsync));
            _logger.LogInfo("[TimerResolutionProvider.RollbackAsync] Entry");
            try
            {
                _logger.LogInfo("[TimerProvider] ⏰ Restaurando timer resolution...");
                var ok = _timerResolution.ReleaseResolution();
                _wasApplied = false;
                _logger.LogExit(nameof(RollbackAsync), ok);
                return Task.FromResult(ok);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[TimerProvider] ❌ Erro no rollback: {ex.Message}");
                _logger.LogInfo("[TimerResolutionProvider.RollbackAsync] Exit (error)");
                _logger.LogExit(nameof(RollbackAsync), false);
                return Task.FromResult(false);
            }
        }
    }
}
