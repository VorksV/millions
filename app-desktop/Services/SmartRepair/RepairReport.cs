using System;
using System.Collections.Generic;
using System.Text;

namespace VoltrisOptimizer.Services.SmartRepair
{
    public class RepairReport
    {
        public DateTime StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public TimeSpan? TotalDuration => CompletedAt.HasValue
            ? CompletedAt.Value - StartedAt
            : null;
        public List<RepairStepResult> Steps { get; set; } = new();
        public RepairStatistics Statistics { get; set; } = new();
        public bool RequiresRestart { get; set; }
        public int HealthScoreBefore { get; set; }
        public int HealthScoreAfter { get; set; }

        public string GenerateHtmlReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><head>");
            sb.AppendLine("<meta charset='UTF-8'>");
            sb.AppendLine("<title>Relatório de Reparo Inteligente - VOLTRIS</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("body{font-family:'Segoe UI',sans-serif;background:#0d1117;color:#e6edf3;margin:0;padding:20px}");
            sb.AppendLine("h1{color:#58a6ff;border-bottom:1px solid #30363d;padding-bottom:10px}");
            sb.AppendLine("h2{color:#8b949e;font-size:16px;margin-top:20px}");
            sb.AppendLine(".card{background:#161b22;border:1px solid #30363d;border-radius:8px;padding:16px;margin:8px 0}");
            sb.AppendLine(".ok{color:#3fb950}.warn{color:#d29922}.err{color:#f85149}");
            sb.AppendLine(".stat{display:inline-block;margin:8px 16px 8px 0;padding:12px;background:#0d1117;border-radius:6px;min-width:120px}");
            sb.AppendLine(".stat-value{font-size:24px;font-weight:700;display:block}");
            sb.AppendLine(".stat-label{font-size:12px;color:#8b949e}");
            sb.AppendLine("</style></head><body>");
            sb.AppendLine("<h1>🔧 Relatório de Reparo Inteligente</h1>");
            sb.AppendLine($"<p>Início: {StartedAt:dd/MM/yyyy HH:mm:ss} | Duração: {TotalDuration?.TotalMinutes:F1} min</p>");

            sb.AppendLine("<div style='margin:20px 0'>");
            sb.AppendLine($"<div class='stat'><span class='stat-value'>{Statistics.CompletedSteps}/{Statistics.TotalSteps}</span><span class='stat-label'>Etapas</span></div>");
            sb.AppendLine($"<div class='stat'><span class='stat-value'>{Statistics.TotalFilesChecked:N0}</span><span class='stat-label'>Arquivos verificados</span></div>");
            sb.AppendLine($"<div class='stat'><span class='stat-value'>{Statistics.TotalFilesFixed:N0}</span><span class='stat-label'>Correções</span></div>");
            sb.AppendLine($"<div class='stat'><span class='stat-value'>{Statistics.TotalSpaceRecoveredFormatted}</span><span class='stat-label'>Espaço recuperado</span></div>");
            sb.AppendLine("</div>");

            if (RequiresRestart)
                sb.AppendLine("<div class='card warn'>⚠️ Reinicialização necessária para aplicar todas as correções.</div>");

            sb.AppendLine("<h2>Etapas Executadas</h2>");
            foreach (var step in Steps)
            {
                var icon = step.Status == StepStatus.Completed ? "✅" :
                           step.Status == StepStatus.Failed ? "❌" :
                           step.Status == StepStatus.Warning ? "⚠️" :
                           step.Status == StepStatus.Skipped ? "⏭️" : "⏳";
                var cls = step.Success ? "ok" : step.Status == StepStatus.Warning ? "warn" : "err";
                sb.AppendLine($"<div class='card'><span class='{cls}'>{icon} <strong>{step.Name}</strong></span>");
                sb.AppendLine($"<p style='margin:4px 0 0;font-size:13px;color:#8b949e'>{step.DetailSummary}</p>");
                if (step.Duration.HasValue)
                    sb.AppendLine($"<p style='margin:2px 0 0;font-size:12px;color:#484f58'>⏱ {step.Duration.Value.TotalSeconds:F1}s</p>");
                sb.AppendLine("</div>");
            }

            sb.AppendLine("</body></html>");
            return sb.ToString();
        }
    }
}
