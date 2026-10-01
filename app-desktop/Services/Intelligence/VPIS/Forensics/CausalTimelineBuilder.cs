using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VoltrisOptimizer.Services.Intelligence.VPIS.Models;

namespace VoltrisOptimizer.Services.Intelligence.VPIS.Forensics
{
    public class CausalTimelineBuilder
    {
        public string BuildTimelineNarrative(IReadOnlyList<PerformanceInsightEvent> sessionEvents, PerformanceInsightEvent rootCause)
        {
            if (sessionEvents == null || !sessionEvents.Any())
                return "Nenhum evento anômalo registrado.";

            var sb = new StringBuilder();
            sb.AppendLine($"Detectamos {sessionEvents.Count} episódios de degradação.");
            
            var firstEvent = sessionEvents.OrderBy(e => e.Timestamp).First();
            sb.AppendLine($"A primeira degradação ocorreu às {firstEvent.Timestamp:HH:mm}.");
            sb.AppendLine();
            
            sb.AppendLine("Durante a sessão, observamos a seguinte cascata de eventos:");
            sb.AppendLine();

            // Build chronological cascade for the root cause
            var rootCauseTimeline = sessionEvents
                .Where(e => e.Category == rootCause.Category)
                .OrderBy(e => e.Timestamp)
                .Take(4) // Show up to 4 key events to avoid wall of text
                .ToList();

            foreach (var evt in rootCauseTimeline)
            {
                sb.AppendLine($"{evt.Timestamp:HH:mm:ss}");
                foreach (var evidence in evt.Evidences)
                {
                    sb.AppendLine($"• {evidence}");
                }
                sb.AppendLine("↓");
            }
            
            // Remove the last arrow
            if (sb.Length >= Environment.NewLine.Length + 1)
            {
                sb.Length -= (Environment.NewLine.Length + 2);
            }

            sb.AppendLine();
            sb.AppendLine("Conclusão:");
            sb.AppendLine($"Há fortes evidências de {rootCause.Diagnosis.ToLower()}.");

            return sb.ToString();
        }
    }
}
