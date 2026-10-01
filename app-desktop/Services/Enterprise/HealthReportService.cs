using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Enterprise
{
    public class HealthReportService : IHealthReportService
    {
        private readonly ILoggingService _logger;
        private readonly string _reportDir;
        private readonly List<HealthEvent> _recentEvents = new();

        public HealthReportService(ILoggingService logger)
        {
            _logger = logger;
            _reportDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Reports");
            if (!Directory.Exists(_reportDir)) Directory.CreateDirectory(_reportDir);
        }

        public async Task LogHealthEventAsync(string component, string status, string details, Dictionary<string, object>? metrics = null)
        {
            var evt = new HealthEvent
            {
                Timestamp = DateTime.Now,
                Component = component,
                Status = status,
                Details = details,
                Metrics = metrics ?? new Dictionary<string, object>()
            };

            lock (_recentEvents)
            {
                _recentEvents.Add(evt);
                if (_recentEvents.Count > 100) _recentEvents.RemoveAt(0);
            }

            _logger.LogInfo($"[HealthReport] {component}: {status} - {details}");
            
            // Em modelo SaaS, aqui enviaríamos o evento para a API via SignalR ou Rest
        }

        public async Task<string> GenerateSystemReportAsync()
        {
            var sb = new StringBuilder();
            sb.AppendLine("╔═══════════════════════════════════════════════════════════╗");
            sb.AppendLine("║             RELATÓRIO DE SAÚDE VOLTRIS                    ║");
            sb.AppendLine($"║             Gerado em: {DateTime.Now:dd/MM/yyyy HH:mm}           ║");
            sb.AppendLine("╚═══════════════════════════════════════════════════════════╝");
            sb.AppendLine();

            lock (_recentEvents)
            {
                foreach (var evt in _recentEvents)
                {
                    sb.AppendLine($"[{evt.Timestamp:HH:mm:ss}] {evt.Component.ToUpper()}: {evt.Status}");
                    sb.AppendLine($"   Detalhes: {evt.Details}");
                    if (evt.Metrics.Count > 0)
                    {
                        sb.AppendLine($"   Métricas: {JsonSerializer.Serialize(evt.Metrics, new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, PropertyNamingPolicy = JsonNamingPolicy.CamelCase })}");
                    }
                    sb.AppendLine("─────────────────────────────────────────────────────────────");
                }
            }

            var reportPath = Path.Combine(_reportDir, $"HealthReport_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            await File.WriteAllTextAsync(reportPath, sb.ToString());
            return reportPath;
        }

        private class HealthEvent
        {
            public DateTime Timestamp { get; set; }
            public string Component { get; set; } = "";
            public string Status { get; set; } = "";
            public string Details { get; set; } = "";
            public Dictionary<string, object> Metrics { get; set; } = new();
        }
    }
}
