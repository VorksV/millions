using System;
using System.Collections.Generic;
using System.Linq;
using VoltrisOptimizer.Services.Intelligence.VPIS.Core;
using VoltrisOptimizer.Services.Intelligence.VPIS.Engines;
using VoltrisOptimizer.Services.Intelligence.VPIS.Models;

namespace VoltrisOptimizer.Services.Intelligence.VPIS.Forensics
{
    public class SessionInvestigator
    {
        private readonly RootCauseRankingEngine _rankingEngine;
        private readonly CausalTimelineBuilder _timelineBuilder;

        public SessionInvestigator(RootCauseRankingEngine rankingEngine, CausalTimelineBuilder timelineBuilder)
        {
            _rankingEngine = rankingEngine;
            _timelineBuilder = timelineBuilder;
        }

        public PerformanceHealthReport AnalyzeSession(string sessionName, TimeSpan duration, IReadOnlyList<PerformanceInsightEvent> sessionEvents)
        {
            if (sessionEvents == null || !sessionEvents.Any())
            {
                return new PerformanceHealthReport
                {
                    SessionName = sessionName,
                    Duration = duration,
                    OverallQualityScore = 10.0,
                    MainProblemTitle = "Nenhum gargalo detectado",
                    MainProblemConfidence = 100,
                    EstimatedImpact = "0%",
                    ForensicsNarrative = "Sua sessão foi perfeitamente estável. Nenhum problema físico ou de software afetou sua performance."
                };
            }

            var rootCause = _rankingEngine.DetermineMainProblem(sessionEvents);
            var narrative = _timelineBuilder.BuildTimelineNarrative(sessionEvents, rootCause);

            // Basic quality score logic based on severity and count
            double penalty = sessionEvents.Count * 0.2 + (rootCause.Severity == DiagnosticSeverity.Critical ? 2.0 : 1.0);
            double qualityScore = Math.Max(0.0, 10.0 - penalty);

            return new PerformanceHealthReport
            {
                SessionName = sessionName,
                Duration = duration,
                OverallQualityScore = Math.Round(qualityScore, 1),
                MainProblemCategory = rootCause.Category,
                MainProblemTitle = rootCause.Diagnosis,
                MainProblemConfidence = rootCause.ConfidenceScore,
                EstimatedImpact = CalcularImpacto(rootCause, sessionEvents.Count),
                ForensicsNarrative = narrative,
                KeyEvidences = rootCause.Evidences,
                Recommendations = rootCause.Recommendations
            };
        }
        /// <summary>
        /// Calcula o impacto estimado com base em eventos reais da sessão.
        /// Fórmula: penalidade por severidade * número de eventos * fator de confiança
        /// </summary>
        private static string CalcularImpacto(PerformanceInsightEvent rootCause, int totalEventos)
        {
            double severidadePeso = rootCause.Severity switch
            {
                DiagnosticSeverity.Critical => 3.0,
                DiagnosticSeverity.Warning => 1.5,
                _ => 0.5
            };

            double confiancaFator = rootCause.ConfidenceScore / 100.0;
            double impactoBruto = severidadePeso * totalEventos * confiancaFator * 2.5;
            double impactoPercent = Math.Min(100.0, impactoBruto);

            return $"{Math.Round(impactoPercent, 0)}% de impacto estimado em frametime e estabilidade ({totalEventos} eventos de {rootCause.Category})";
        }
    }
}
