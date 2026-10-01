using System;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Gamer.Interfaces;
using VoltrisOptimizer.Services.Gamer.Pipeline.Models;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer.Pipeline.Providers
{
    public class NetworkLatencyProvider : IOptimizationProvider
    {
        private readonly ILoggingService _logger;
        private readonly INetworkGamingOptimizer _networkOptimizer;
        private bool _wasApplied;

        public string Id => "network-latency-optimization";
        public string DisplayName => "Otimização de Latência de Rede";
        public string Category => "Network";
        public double BaseBenefitScore => 5;
        public double BaseRiskScore => 1;
        public bool HasRollback => true;

        public NetworkLatencyProvider(ILoggingService logger, INetworkGamingOptimizer networkOptimizer)
        {
            _logger.LogEntry(nameof(NetworkLatencyProvider));
            _logger = logger;
            _networkOptimizer = networkOptimizer;
            _logger.LogExit(nameof(NetworkLatencyProvider));
        }

        public bool CanHandle(GamerSessionContext context)
        {
            _logger.LogEntry(nameof(CanHandle));
            _logger.LogDebug("[NetworkLatencyProvider.CanHandle] Entry");
            var result = context.Goals.PrioritizeLatency;
            _logger.LogDebug($"[NetworkLatencyProvider.CanHandle] Exit: {result}");
            _logger.LogExit(nameof(CanHandle), result);
            return result;
        }

        public double CalculateBenefitScore(GamerSessionContext context)
        {
            _logger.LogEntry(nameof(CalculateBenefitScore));
            _logger.LogDebug("[NetworkLatencyProvider.CalculateBenefitScore] Entry");
            double score = BaseBenefitScore;
            if (context.Game.Category == GameCategorization.Models.GameCategory.Competitive) score += 3;
            _logger.LogDebug($"[NetworkLatencyProvider.CalculateBenefitScore] Exit: {Math.Min(score, 10)}");
            _logger.LogExit(nameof(CalculateBenefitScore), Math.Min(score, 10));
            return Math.Min(score, 10);
        }

        public async Task<bool> ApplyAsync(CancellationToken ct)
        {
            _logger.LogEntry(nameof(ApplyAsync));
            _logger.LogInfo("[NetworkLatencyProvider.ApplyAsync] Entry");
            try
            {
                _logger.LogInfo("[NetworkProvider] 🌐 Otimizando rede para baixa latência...");
                var ok = await _networkOptimizer.OptimizeAsync(ct);
                _wasApplied = ok;
                if (ok) _logger.LogInfo("[NetworkProvider] ✅ Rede otimizada");
                _logger.LogInfo("[NetworkLatencyProvider.ApplyAsync] Exit");
                _logger.LogExit(nameof(ApplyAsync), ok);
                return ok;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[NetworkProvider] ❌ Erro: {ex.Message}");
                _logger.LogInfo("[NetworkLatencyProvider.ApplyAsync] Exit (error)");
                _logger.LogExit(nameof(ApplyAsync), false);
                return false;
            }
        }

        public async Task<bool> RollbackAsync(CancellationToken ct)
        {
            _logger.LogEntry(nameof(RollbackAsync));
            _logger.LogInfo("[NetworkLatencyProvider.RollbackAsync] Entry");
            try
            {
                _logger.LogInfo("[NetworkProvider] 🔄 Restaurando rede...");
                var ok = await _networkOptimizer.RestoreAsync(ct);
                _wasApplied = false;
                _logger.LogExit(nameof(RollbackAsync), ok);
                return ok;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[NetworkProvider] ❌ Erro no rollback: {ex.Message}");
                _logger.LogInfo("[NetworkLatencyProvider.RollbackAsync] Exit (error)");
                _logger.LogExit(nameof(RollbackAsync), false);
                return false;
            }
        }
    }
}
