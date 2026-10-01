using System;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.Execution;
using VoltrisOptimizer.Core.Telemetry;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.SystemSafety.Rollback;
using VoltrisOptimizer.Core.Configuration;

namespace VoltrisOptimizer.Core.Validation
{
    public class ValidationMetrics
    {
        public double AverageFps { get; set; }
        public double Fps1PercentLow { get; set; }
        public double FrameTimeVariance { get; set; }
        public double DpcLatencyMs { get; set; }
        public double InputLatencyMs { get; set; }

        public static ValidationMetrics CaptureCurrent(UnifiedTelemetryBus bus)
        {
            return new ValidationMetrics
            {
                AverageFps = bus.AverageFps,
                Fps1PercentLow = bus.Fps1PercentLow,
                FrameTimeVariance = bus.FrameTimeVariance,
                DpcLatencyMs = bus.DpcLatencyMs,
                InputLatencyMs = bus.InputLatencyMs
            };
        }
    }

    /// <summary>
    /// Validador Baseado em Evidências (Zero Placebo Policy).
    /// Executa as ações e avalia se elas trouxeram benefícios REAIS com base na 
    /// função de recompensa Multidimensional da IA.
    /// Caso não traga benefício, aciona o Rollback.
    /// </summary>
    public sealed class EvidenceValidator
    {
        private static readonly Lazy<EvidenceValidator> _instance = new(() => new EvidenceValidator());
        public static EvidenceValidator Instance => _instance.Value;

        private readonly UnifiedTelemetryBus _telemetry = UnifiedTelemetryBus.Instance;
        private ILoggingService _logger; // Injetado
        private IGranularRollbackEngine _rollbackEngine; // Injetado

        private EvidenceValidator() { }

        public void Initialize(ILoggingService logger, IGranularRollbackEngine rollbackEngine)
        {
            _logger = logger;
            _rollbackEngine = rollbackEngine;
        }

        public bool ValidateOptimization(ValidationMetrics preMetrics, ValidationMetrics postMetrics)
        {
            if (preMetrics == null || postMetrics == null) return true;
            if (postMetrics.AverageFps < preMetrics.AverageFps * 0.90) return false;
            if (postMetrics.FrameTimeVariance > preMetrics.FrameTimeVariance * 1.5) return false;
            return true;
        }

        public async Task<bool> ExecuteWithEvidenceValidationAsync(OptimizationAction action, Guid transactionId)
        {
            if (!VoltrisFeatureFlags.Instance.UseEvidenceValidator)
            {
                await action.ApplyAsync();
                return true;
            }

            // 1. Snapshot do estado antes (resolvido na fase de transação do Scheduler)
            await action.TakeSnapshotAsync();

            // 2. Coleta Pre-Metrics
            var preMetrics = await action.CollectPreMetricsAsync() ?? ValidationMetrics.CaptureCurrent(_telemetry);

            // 3. Aplica a Otimização
            await action.ApplyAsync();

            // 4. Período de Estabilização (Hardware settling time)
            await Task.Delay(2000); 

            // 5. Coleta Post-Metrics
            var postMetrics = await action.CollectPostMetricsAsync() ?? ValidationMetrics.CaptureCurrent(_telemetry);

            // 6. Comparação Estatística Multidimensional
            bool hasBenefit = EvaluateMultidimensionalBenefit(preMetrics, postMetrics);

            // 7. Aceita ou Reverte
            if (hasBenefit)
            {
                _logger?.LogInfo($"[EVIDENCE] Ação '{action.ActionName}' aprovada! Ganho mensurável detectado.");
                // Aprendizado de IA salvaria que esta ação funciona neste hardware
                return true;
            }
            else
            {
                _logger?.LogWarning($"[EVIDENCE] Ação '{action.ActionName}' reprovada (Zero Placebo). Revertendo.");
                // Inicia Rollback
                _rollbackEngine?.TryRollbackTransaction(transactionId);
                // Aprendizado de IA salvaria que esta ação é inútil ou prejudicial neste hardware
                return false;
            }
        }

        private bool EvaluateMultidimensionalBenefit(ValidationMetrics pre, ValidationMetrics post)
        {
            // Função de recompensa Multidimensional básica (A IA real usa pesos para cada contexto)
            int score = 0;

            // Melhoria em 1% Low tem peso fortíssimo
            if (post.Fps1PercentLow > pre.Fps1PercentLow * 1.02) score += 3;
            else if (post.Fps1PercentLow < pre.Fps1PercentLow * 0.98) score -= 3;

            // Variância de Frame Time menor significa mais fluidez
            if (post.FrameTimeVariance < pre.FrameTimeVariance * 0.95) score += 2;
            else if (post.FrameTimeVariance > pre.FrameTimeVariance * 1.05) score -= 2;

            // Redução de DPC
            if (post.DpcLatencyMs < pre.DpcLatencyMs * 0.90) score += 2;

            // FPS Médio (tem peso menor que estabilidade)
            if (post.AverageFps > pre.AverageFps * 1.01) score += 1;

            return score >= 1; // Se a pontuação for positiva, o benefício é real.
        }
    }
}
