using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.Gamer.Audit;

namespace VoltrisOptimizer.Services.Gamer.Implementation
{
    public class GamerExecutionReportGenerator
    {
        private readonly ILoggingService _logger;
        private readonly string _reportsDirectory;

        public GamerExecutionReportGenerator(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogEntry(nameof(GamerExecutionReportGenerator));
            _reportsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Reports", "GamerMode");
            Directory.CreateDirectory(_reportsDirectory);
            _logger.LogExit(nameof(GamerExecutionReportGenerator));
        }

        public void GenerateCompleteReport(ExecutionReport executionReport, FullValidationReport validationReport)
        {
            _logger.LogEntry(nameof(GenerateCompleteReport));
            try
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var reportPath = Path.Combine(_reportsDirectory, $"gamer_mode_report_{timestamp}.json");
                var logPath = Path.Combine(_reportsDirectory, $"gamer_mode_log_{timestamp}.txt");

                var completeReport = new CompleteGamerReport
                {
                    Timestamp = DateTime.Now,
                    ExecutionReport = executionReport,
                    ValidationReport = validationReport,
                    Summary = GenerateSummary(executionReport, validationReport)
                };

                var json = JsonSerializer.Serialize(completeReport, new JsonSerializerOptions { WriteIndented = true, ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles });
                File.WriteAllText(reportPath, json);

                GenerateReadableLog(completeReport, logPath);

                _logger.LogSuccess($"[ReportGenerator] Relatório completo gerado: {reportPath}");
                _logger.LogInfo($"[ReportGenerator] Log legível gerado: {logPath}");

                LogSummaryToConsole(completeReport.Summary);
            }
            catch (Exception ex)
            {
                _logger.LogError("[ReportGenerator] Erro ao gerar relatório completo", ex);
            }
            _logger.LogExit(nameof(GenerateCompleteReport));
        }

        public async Task GenerateConsolidatedReportAsync(ConsolidatedAuditReport auditReport)
        {
            _logger.LogEntry(nameof(GenerateConsolidatedReportAsync));
            try
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var reportPath = Path.Combine(_reportsDirectory, $"gamer_audit_report_{timestamp}.json");
                var logPath = Path.Combine(_reportsDirectory, $"gamer_audit_log_{timestamp}.txt");

                var json = JsonSerializer.Serialize(auditReport, new JsonSerializerOptions { WriteIndented = true, ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles });
                await File.WriteAllTextAsync(reportPath, json);

                await GenerateReadableAuditLogAsync(auditReport, logPath);

                _logger.LogSuccess($"[ReportGenerator] Relatório de auditoria gerado: {reportPath}");
                LogAuditSummaryToConsole(auditReport);
            }
            catch (Exception ex)
            {
                _logger.LogError("[ReportGenerator] Erro ao gerar relatório de auditoria", ex);
            }
            _logger.LogExit(nameof(GenerateConsolidatedReportAsync));
        }

        private ExecutionSummary GenerateSummary(ExecutionReport executionReport, FullValidationReport validationReport)
        {
            _logger.LogEntry(nameof(GenerateSummary));
            var summary = new ExecutionSummary
            {
                SessionId = executionReport.SessionId,
                StartTime = executionReport.StartTime,
                EndTime = executionReport.EndTime,
                Duration = executionReport.Duration,
                ExecutionSuccess = executionReport.Validation.IsComplete,
                TotalOperations = executionReport.TotalOperations,
                SuccessfulOperations = executionReport.Validation.TotalSuccess,
                FailedOperations = executionReport.Validation.TotalFailed,
                MissingOperations = executionReport.Validation.MissingModules.Count,
                ValidationSuccess = validationReport.IsOverallSuccess,
                ValidationChecks = validationReport.TotalChecks,
                ValidationPassed = validationReport.PassedChecks,
                ValidationFailed = validationReport.FailedChecks,
                ValidationPercentage = validationReport.SuccessPercentage,
                ModuleStatus = new Dictionary<string, ModuleStatus>(),
                Issues = new List<string>(),
                Recommendations = new List<string>()
            };
            _logger.LogExit(nameof(GenerateSummary));
            return summary;
        }

        private void GenerateReadableLog(CompleteGamerReport report, string logPath)
        {
            _logger.LogEntry(nameof(GenerateReadableLog));
            var log = new List<string>
            {
                "",
                "RELATÓRIO COMPLETO DO MODO GAMER",
                "",
                $"Data/Hora: {report.Timestamp:yyyy-MM-dd HH:mm:ss}",
                $"Sessão: {report.Summary.SessionId}",
                $"Duração: {report.Summary.Duration.TotalMilliseconds:F0} ms",
                "",
                "FIM DO RELATÓRIO",
                ""
            };
            File.WriteAllLines(logPath, log);
            _logger.LogExit(nameof(GenerateReadableLog));
        }

        private async Task GenerateReadableAuditLogAsync(ConsolidatedAuditReport report, string logPath)
        {
            _logger.LogEntry(nameof(GenerateReadableAuditLogAsync));
            var log = new List<string>
            {
                "═══════════════════════════════════════════",
                "  RELATÓRIO DE AUDITORIA - MODO GAMER",
                "═══════════════════════════════════════════",
                $"Relatório ID: {report.ReportId}",
                $"Gerado em: {report.GeneratedAt:yyyy-MM-dd HH:mm:ss}",
                $"Sessão iniciada: {report.SessionStart:yyyy-MM-dd HH:mm:ss}",
                $"Sessão finalizada: {report.SessionEnd:yyyy-MM-dd HH:mm:ss}",
                $"Duração: {report.SessionDuration.TotalSeconds:F1}s",
                $"Jogo: {report.GameName ?? "N/A"}",
                $"PID: {report.GameProcessId?.ToString() ?? "N/A"}",
                "",
                "--- ESTATÍSTICAS ---",
                $"Total de otimizações: {report.Stats.TotalOptimizations}",
                $"Sucesso: {report.Stats.Successful}",
                $"Falhas: {report.Stats.Failed}",
                $"Ignoradas: {report.Stats.Ignored}",
                $"Não suportadas: {report.Stats.NotSupported}",
                $"Bloqueadas pelo Windows: {report.Stats.BlockedByWindows}",
                $"Bloqueadas pelo driver: {report.Stats.BlockedByDriver}",
                $"Revertidas: {report.Stats.Reverted}",
                $"Validadas: {report.Stats.Validated}",
                $"Falha na validação: {report.Stats.ValidationFailed}",
                $"Taxa de sucesso: {report.Stats.SuccessRate:F1}%",
                $"Taxa de validação: {report.Stats.ValidationRate:F1}%",
                "",
                "--- POR CATEGORIA ---"
            };

            foreach (var kvp in report.Stats.ByCategory)
            {
                log.Add($"  {kvp.Key}: {kvp.Value.Success}/{kvp.Value.Total} sucesso, {kvp.Value.Validated} validadas, media {kvp.Value.AverageExecutionTimeMs:F1}ms");
            }

            log.Add("");
            log.Add("--- OTIMIZAÇÕES ---");
            foreach (var opt in report.Optimizations)
            {
                log.Add($"  [{opt.Timestamp:HH:mm:ss.fff}] [{opt.Category}] {opt.Service}.{opt.Method}: {opt.Optimization}");
                log.Add($"    Resultado: {opt.Result} | Validação: {opt.Validation} | {opt.ExecutionTimeMs}ms");
                if (!string.IsNullOrEmpty(opt.ErrorMessage))
                    log.Add($"    Erro: {opt.ErrorMessage}");
            }

            log.Add("");
            log.Add("--- PROCESSOS ---");
            foreach (var proc in report.Processes)
            {
                log.Add($"  {proc.ProcessName} (PID:{proc.ProcessId}) CPU:{proc.CpuUsage:F1}% RAM:{proc.RamUsageMb:F0}MB {(proc.WasTerminated ? "[TERMINATED]" : proc.WasSuspended ? "[SUSPENDED]" : proc.WasIgnored ? "[IGNORED]" : "")}");
            }

            log.Add("");
            log.Add("--- SERVIÇOS ---");
            foreach (var svc in report.Services)
            {
                log.Add($"  {svc.ServiceName}: {svc.PreviousState} -> {svc.NewState} ({svc.Result})");
            }

            log.Add("");
            log.Add("--- REGISTRY ---");
            foreach (var reg in report.RegistryChanges)
            {
                log.Add($"  {reg.RegistryPath}\\{reg.ValueName}: {reg.PreviousValue} -> {reg.NewValue} ({reg.Result})");
            }

            log.Add("");
            log.Add("FIM DO RELATÓRIO DE AUDITORIA");

            await File.WriteAllLinesAsync(logPath, log);
            _logger.LogExit(nameof(GenerateReadableAuditLogAsync));
        }

        private void LogSummaryToConsole(ExecutionSummary summary)
        {
            _logger.LogEntry(nameof(LogSummaryToConsole));
            _logger.LogInfo("RESUMO DO MODO GAMER:");
            _logger.LogInfo($"Sessão: {summary.SessionId}");
            _logger.LogInfo($"Duração: {summary.Duration.TotalMilliseconds:F0} ms");
            _logger.LogInfo($"Execução: {(summary.ExecutionSuccess ? "SUCESSO" : "FALHA")} ({summary.SuccessfulOperations}/{summary.TotalOperations})");
            _logger.LogInfo($"Validação: {(summary.ValidationSuccess ? "SUCESSO" : "FALHA")} ({summary.ValidationPassed}/{summary.ValidationChecks})");

            if (summary.Issues.Any())
                _logger.LogWarning($"{summary.Issues.Count} problemas detectados");

            if (summary.Recommendations.Any())
                _logger.LogInfo($"{summary.Recommendations.Count} recomendações geradas");
            _logger.LogExit(nameof(LogSummaryToConsole));
        }

        private void LogAuditSummaryToConsole(ConsolidatedAuditReport report)
        {
            _logger.LogEntry(nameof(LogAuditSummaryToConsole));
            _logger.LogInfo("═══════════════════════════════════════════");
            _logger.LogInfo("RESUMO DE AUDITORIA - MODO GAMER");
            _logger.LogInfo($"Jogo: {report.GameName ?? "N/A"}");
            _logger.LogInfo($"Total: {report.Stats.TotalOptimizations}");
            _logger.LogInfo($"Sucesso: {report.Stats.Successful}");
            _logger.LogInfo($"Falhas: {report.Stats.Failed}");
            _logger.LogInfo($"Validadas: {report.Stats.Validated}");
            _logger.LogInfo($"Taxa de Sucesso: {report.Stats.SuccessRate:F1}%");
            _logger.LogInfo($"Taxa de Efetividade: {report.Stats.EffectivenessRate:F1}%");
            _logger.LogInfo("═══════════════════════════════════════════");
            _logger.LogExit(nameof(LogAuditSummaryToConsole));
        }
    }

    public class CompleteGamerReport
    {
        public DateTime Timestamp { get; set; }
        public ExecutionReport ExecutionReport { get; set; } = new();
        public FullValidationReport ValidationReport { get; set; } = new();
        public ExecutionSummary Summary { get; set; } = new();
    }

    public class ExecutionSummary
    {
        public string SessionId { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public bool ExecutionSuccess { get; set; }
        public int TotalOperations { get; set; }
        public int SuccessfulOperations { get; set; }
        public int FailedOperations { get; set; }
        public int MissingOperations { get; set; }
        public bool ValidationSuccess { get; set; }
        public int ValidationChecks { get; set; }
        public int ValidationPassed { get; set; }
        public int ValidationFailed { get; set; }
        public double ValidationPercentage { get; set; }
        public Dictionary<string, ModuleStatus> ModuleStatus { get; set; } = new();
        public List<string> Issues { get; set; } = new();
        public List<string> Recommendations { get; set; } = new();
    }

    public class ModuleStatus
    {
        public string ModuleName { get; set; } = string.Empty;
        public bool Executed { get; set; }
        public bool ExecutionSuccess { get; set; }
        public long ExecutionDuration { get; set; }
        public bool Validated { get; set; }
        public bool ValidationSuccess { get; set; }
        public int ValidationChecks { get; set; }
        public int ValidationPassed { get; set; }
        public List<string> Issues { get; set; } = new();
    }
}
