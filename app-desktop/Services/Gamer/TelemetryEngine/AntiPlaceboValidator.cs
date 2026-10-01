using System;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Gamer.StrategyFactory.Models;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer.TelemetryEngine
{
    public interface IAntiPlaceboValidator
    {
        Task<bool> ValidateOptimizationAsync(string optimizationId, double baselineFps, double optimizedFps, double baselineFrametime, double optimizedFrametime);
    }

    public class AntiPlaceboValidator : IAntiPlaceboValidator
    {
        private readonly ILoggingService _logger;
        private readonly ITelemetryDatabaseService _db;

        public AntiPlaceboValidator(ILoggingService logger, ITelemetryDatabaseService db)
        {
            _logger.LogEntry(nameof(AntiPlaceboValidator));
            _logger = logger;
            _db = db;
            _logger.LogExit(nameof(AntiPlaceboValidator));
        }

        public async Task<bool> ValidateOptimizationAsync(string optimizationId, double baselineFps, double optimizedFps, double baselineFrametime, double optimizedFrametime)
        {
            _logger.LogEntry(nameof(ValidateOptimizationAsync));
            _logger.LogInfo($"[AntiPlacebo] Validando {optimizationId}... Base FPS: {baselineFps:F1} -> Novo FPS: {optimizedFps:F1}");
            
            // Buscar histórico de confiança ou criar um novo
            var conf = await _db.GetOptimizationConfidenceAsync(optimizationId) ?? new OptimizationConfidence { OptimizationId = optimizationId };

            // Evitar divisões por zero
            if (baselineFps <= 0) return true;

            double fpsDelta = ((optimizedFps - baselineFps) / baselineFps) * 100.0;
            double frametimeDelta = ((baselineFrametime - optimizedFrametime) / baselineFrametime) * 100.0; // Invertido: Frametime menor é melhor (+)

            _logger.LogInfo($"[AntiPlacebo] {optimizationId} Delta FPS: {fpsDelta:F2}% | Delta Frametime: {frametimeDelta:F2}%");

            bool isStatisticallyBetter = (fpsDelta >= 1.0) || (frametimeDelta >= 2.0); // Margem de erro e flutuação normal

            if (isStatisticallyBetter)
            {
                _logger.LogSuccess($"[AntiPlacebo] {optimizationId} APROVADO! Ganho real detectado.");
                conf.SuccessCount++;
                conf.ConfidenceScore = Math.Min(100, conf.ConfidenceScore + 5);
                
                // Média Móvel Exponencial simples
                conf.AverageFpsDeltaPercent = (conf.AverageFpsDeltaPercent * 0.8) + (fpsDelta * 0.2);
                conf.AverageFrametimeDeltaPercent = (conf.AverageFrametimeDeltaPercent * 0.8) + (frametimeDelta * 0.2);
            }
            else
            {
                _logger.LogWarning($"[AntiPlacebo] {optimizationId} REJEITADO! Nenhum ganho real ou placebo detectado. Revertendo efeito.");
                conf.FailureCount++;
                conf.ConfidenceScore = Math.Max(0, conf.ConfidenceScore - 10); // Punição é maior que recompensa para evitar falsos positivos mantidos
                
                conf.AverageFpsDeltaPercent = (conf.AverageFpsDeltaPercent * 0.8) + (fpsDelta * 0.2);
                conf.AverageFrametimeDeltaPercent = (conf.AverageFrametimeDeltaPercent * 0.8) + (frametimeDelta * 0.2);
            }

            await _db.SaveOptimizationResultAsync(conf);
            _logger.LogExit(nameof(ValidateOptimizationAsync), isStatisticallyBetter);
            return isStatisticallyBetter;
        }
    }
}
