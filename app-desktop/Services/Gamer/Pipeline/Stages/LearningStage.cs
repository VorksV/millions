using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Gamer.Pipeline.Models;

namespace VoltrisOptimizer.Services.Gamer.Pipeline.Stages
{
    public class LearningStage : IPipelineStage
    {
        private readonly ILoggingService _logger;
        private readonly ISessionMemory _sessionMemory;

        public string Name => "Learning";
        public int Order => 10;

        public LearningStage(ILoggingService logger, ISessionMemory sessionMemory)
        {
            _logger = logger;
            _sessionMemory = sessionMemory;
        }

        public async Task ExecuteAsync(GamerSessionContext context, CancellationToken ct)
        {
            _logger.LogEntry(nameof(ExecuteAsync));
            _logger.LogInfo($"[Pipeline] [GO] Estágio 10/11: Learning — analisando resultados da sessão...");

            int total = context.Decisions.Count;
            int applied = context.Decisions.Count(d => d.Applied);
            int validated = context.Results.Count(r => r.PassedValidation);
            int placebos = context.Results.Count(r => !r.PassedValidation);

            double avgImprovement = context.Results
                .Where(r => r.PassedValidation)
                .Select(r => r.MeasuredFpsDelta)
                .DefaultIfEmpty(0)
                .Average();

            _logger.LogInfo($"[Learning] Sessão: {context.SessionId[..8]} | Jogo: {context.Game.Name}");
            _logger.LogInfo($"[Learning] Decisões: {total} | Aplicadas: {applied} | Validadas: {validated} | Placebos: {placebos} | ΔFPS médio: {avgImprovement:F1}");

            // Histórico do jogo atual
            var lastSession = await _sessionMemory.GetLastSessionForGameAsync(context.Game.Name);
            if (lastSession != null)
            {
                _logger.LogInfo($"[Learning] 📜 Última sessão deste jogo: {lastSession.PlayedAt:g} | {lastSession.Validated}/{lastSession.TotalOptimizations} OK");
            }

            // Efetividade geral
            if (total > 0)
            {
                double effectiveness = total > 0 ? (double)validated / total * 100 : 0;
                _logger.LogInfo($"[Learning] 📊 Efetividade da sessão: {effectiveness:F0}%");

                if (effectiveness < 30 && total >= 3)
                {
                    _logger.LogWarning("[Learning] ⚠ Baixa efetividade (<30%) — sessão sub-ótima");
                }
                else if (effectiveness >= 70)
                {
                    _logger.LogSuccess("[Learning] 🎯 Alta efetividade (≥70%) — estratégias bem-sucedidas");
                }
            }

            // Relatório por otimização
            var placebosDetected = context.Results.Where(r => !r.PassedValidation).ToList();
            if (placebosDetected.Any())
            {
                _logger.LogInfo("[Learning] 📋 Placebos detectados nesta sessão:");
                foreach (var p in placebosDetected)
                {
                    var history = await _sessionMemory.GetOptimizationHistoryAsync(p.OptimizationId);
                    _logger.LogInfo($"[Learning]   ❌ {p.OptimizationId}: {p.FailureReason ?? "Sem ganho"} (histórico: {history.PassCount}/{history.TotalApplications} OK, confiança: {history.Confidence:F0}%)");
                }
            }

            // Recomendações
            if (avgImprovement <= 0 && total >= 3)
            {
                _logger.LogWarning("[Learning] 💡 Nenhuma melhoria detectada nesta sessão. Considere desativar otimizações para este jogo.");
            }
            _logger.LogExit(nameof(ExecuteAsync));
        }
    }
}
