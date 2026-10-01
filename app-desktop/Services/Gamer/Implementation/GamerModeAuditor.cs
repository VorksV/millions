using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.Gamer.Audit;

namespace VoltrisOptimizer.Services.Gamer.Implementation
{
    public class GamerModeAuditor
    {
        private readonly ILoggingService _logger;
        private readonly IGamerAuditService _auditService;
        private readonly List<AuditEntry> _legacyEntries = new();
        private readonly string _auditFilePath;

        public GamerModeAuditor(ILoggingService logger, IGamerAuditService? auditService = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogEntry(nameof(GamerModeAuditor));
            _auditService = auditService ?? new GamerAuditService(logger);
            _auditFilePath = Path.Combine(LogDirectoryResolver.Resolve(), $"gamer_mode_audit_{DateTime.Now:yyyyMMdd_HHmmss}.json");

            OptimizationValidator.Initialize(logger);
            _logger.LogExit(nameof(GamerModeAuditor));
        }

        public void StartSession(string? gameName = null, int? gameProcessId = null)
        {
            _logger.LogEntry(nameof(StartSession));
            _auditService.StartSession(gameName, gameProcessId);
            _logger.LogExit(nameof(StartSession));
        }

        public void EndSession()
        {
            _logger.LogEntry(nameof(EndSession));
            _auditService.EndSession();
            _logger.LogExit(nameof(EndSession));
        }

        public IGamerAuditService AuditService => _auditService;

        public void LogOptimization(string service, string action, bool success, string? details = null)
        {
            _logger.LogEntry(nameof(LogOptimization));
            var entry = new AuditEntry
            {
                Timestamp = DateTime.Now,
                Service = service,
                Action = action,
                Result = success ? "SUCCESS" : "FAILED",
                Details = details ?? string.Empty,
                Type = "OPTIMIZATION"
            };
            _legacyEntries.Add(entry);

            var auditEntry = new OptimizationAuditEntry
            {
                Timestamp = DateTime.Now,
                Category = MapServiceToCategory(service),
                Service = service,
                Class = service,
                Method = action,
                Optimization = action,
                Result = success ? AuditResult.SUCCESS : AuditResult.FAILED,
                ExecutionTimeMs = 0
            };
            _auditService.RecordOptimization(auditEntry);

            if (success)
                _logger.LogSuccess($"[Audit] {service}: {action}");
            else
                _logger.LogWarning($"[Audit] {service}: {action} - {details}");

            _logger.LogExit(nameof(LogOptimization));
        }

        public void LogReversion(string service, string setting, string expectedValue, string actualValue)
        {
            _logger.LogEntry(nameof(LogReversion));
            var entry = new AuditEntry
            {
                Timestamp = DateTime.Now,
                Service = service,
                Action = $"REVERSION: {setting}",
                Result = "REVERTED_BY_WINDOWS",
                Details = $"Expected: {expectedValue}, Actual: {actualValue}",
                Type = "REVERSION"
            };
            _legacyEntries.Add(entry);

            var auditEntry = new OptimizationAuditEntry
            {
                Timestamp = DateTime.Now,
                Category = MapServiceToCategory(service),
                Service = service,
                Method = "Reversion",
                Optimization = $"REVERSION: {setting}",
                ExpectedValue = expectedValue,
                ActualValue = actualValue,
                Result = AuditResult.REVERTED_BY_WINDOWS,
                Validation = ValidationStatus.REVERTED,
                Before = new AuditSnapshot { Additional = { [setting] = expectedValue } },
                After = new AuditSnapshot { Additional = { [setting] = actualValue } }
            };
            _auditService.RecordOptimization(auditEntry);

            _logger.LogWarning($"[Audit] REVERTIDO: {service}.{setting}");
            _logger.LogExit(nameof(LogReversion));
        }

        public void LogDriverFailure(string vendor, string operation, string reason)
        {
            _logger.LogEntry(nameof(LogDriverFailure));
            var entry = new AuditEntry
            {
                Timestamp = DateTime.Now,
                Service = "GPU_DRIVER",
                Action = $"{vendor}: {operation}",
                Result = "DRIVER_NOT_SUPPORTED",
                Details = reason,
                Type = "DRIVER_FAILURE"
            };
            _legacyEntries.Add(entry);

            var auditEntry = new OptimizationAuditEntry
            {
                Timestamp = DateTime.Now,
                Category = AuditCategory.GPU,
                Service = "GPU_DRIVER",
                Method = operation,
                Optimization = $"{vendor}: {operation}",
                ErrorMessage = reason,
                Result = AuditResult.BLOCKED_BY_DRIVER,
                Validation = ValidationStatus.VALIDATION_FAILED
            };
            _auditService.RecordOptimization(auditEntry);

            _logger.LogWarning($"[Audit] Driver {vendor}: {operation} - {reason}");
            _logger.LogExit(nameof(LogDriverFailure));
        }

        public void LogMetric(string metric, double value, string unit)
        {
            _logger.LogEntry(nameof(LogMetric));
            var entry = new AuditEntry
            {
                Timestamp = DateTime.Now,
                Service = "METRICS",
                Action = metric,
                Result = $"{value:F2} {unit}",
                Details = string.Empty,
                Type = "METRIC"
            };
            _legacyEntries.Add(entry);
            _logger.LogExit(nameof(LogMetric));
        }

        public OptimizationAuditEntry CreateAuditEntry(AuditCategory category, string service, string method, string optimization)
        {
            _logger.LogEntry(nameof(CreateAuditEntry));
            _logger.LogExit(nameof(CreateAuditEntry));
            return new OptimizationAuditEntry
            {
                Timestamp = DateTime.Now,
                Category = category,
                Service = service,
                Class = service,
                Method = method,
                Optimization = optimization,
                ThreadId = Environment.CurrentManagedThreadId
            };
        }

        public void RecordAuditEntry(OptimizationAuditEntry entry)
        {
            _logger.LogEntry(nameof(RecordAuditEntry));
            _auditService.RecordOptimization(entry);

            var legacyEntry = new AuditEntry
            {
                Timestamp = entry.Timestamp,
                Service = entry.Service,
                Action = entry.Optimization,
                Result = entry.Result.ToString(),
                Details = entry.ErrorMessage ?? string.Empty,
                Type = entry.Category.ToString()
            };
            _legacyEntries.Add(legacyEntry);
            _logger.LogExit(nameof(RecordAuditEntry));
        }

        public void RecordServiceChange(ServiceAuditEntry entry)
        {
            _logger.LogEntry(nameof(RecordServiceChange));
            _auditService.RecordServiceChange(entry);
            _logger.LogExit(nameof(RecordServiceChange));
        }

        public void RecordProcessAction(ProcessAuditEntry entry)
        {
            _logger.LogEntry(nameof(RecordProcessAction));
            _auditService.RecordProcessAction(entry);
            _logger.LogExit(nameof(RecordProcessAction));
        }

        public void RecordRegistryChange(RegistryAuditEntry entry)
        {
            _logger.LogEntry(nameof(RecordRegistryChange));
            _auditService.RecordRegistryChange(entry);
            _logger.LogExit(nameof(RecordRegistryChange));
        }

        public void RecordGameDetection(GameAuditEntry entry)
        {
            _logger.LogEntry(nameof(RecordGameDetection));
            _auditService.RecordGameDetection(entry);
            _logger.LogExit(nameof(RecordGameDetection));
        }

        public void RecordHardware(HardwareAuditEntry entry)
        {
            _logger.LogEntry(nameof(RecordHardware));
            _auditService.RecordHardware(entry);
            _logger.LogExit(nameof(RecordHardware));
        }

        public void RecordPipelineStage(PipelineStageAuditEntry entry)
        {
            _logger.LogEntry(nameof(RecordPipelineStage));
            _auditService.RecordPipelineStage(entry);
            _logger.LogExit(nameof(RecordPipelineStage));
        }

        public async Task FlushAsync()
        {
            _logger.LogEntry(nameof(FlushAsync));
            await _auditService.FlushAsync();
            _logger.LogExit(nameof(FlushAsync));
        }

        public AuditReport GenerateReport()
        {
            _logger.LogEntry(nameof(GenerateReport));
            int totalOptimizations = 0;
            int successfulOptimizations = 0;
            int failedOptimizations = 0;
            int reversions = 0;
            int driverFailures = 0;

            foreach (var entry in _legacyEntries)
            {
                switch (entry.Type)
                {
                    case "OPTIMIZATION":
                        totalOptimizations++;
                        if (entry.Result == "SUCCESS") successfulOptimizations++;
                        else failedOptimizations++;
                        break;
                    case "REVERSION":
                        reversions++;
                        break;
                    case "DRIVER_FAILURE":
                        driverFailures++;
                        break;
                }
            }

            _logger.LogExit(nameof(GenerateReport));
            return new AuditReport
            {
                TotalOptimizations = totalOptimizations,
                SuccessfulOptimizations = successfulOptimizations,
                FailedOptimizations = failedOptimizations,
                Reversions = reversions,
                DriverFailures = driverFailures,
                EffectivenessRate = totalOptimizations > 0
                    ? (double)successfulOptimizations / totalOptimizations * 100
                    : 0,
                Entries = _legacyEntries
            };
        }

        public void SaveToFile()
        {
            _logger.LogEntry(nameof(SaveToFile));
            try
            {
                var report = GenerateReport();
                Directory.CreateDirectory(Path.GetDirectoryName(_auditFilePath)!);
                var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true,
                    ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
                });
                File.WriteAllText(_auditFilePath, json);
                _logger.LogSuccess($"[Audit] Relatório salvo: {_auditFilePath}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[Audit] Erro ao salvar relatório: {ex.Message}", ex);
            }
            _logger.LogExit(nameof(SaveToFile));
        }

        public async Task SaveConsolidatedReportAsync()
        {
            _logger.LogEntry(nameof(SaveConsolidatedReportAsync));
            await _auditService.SaveReportAsync();
            _logger.LogExit(nameof(SaveConsolidatedReportAsync));
        }

        public void PrintSummary()
        {
            _logger.LogEntry(nameof(PrintSummary));
            var report = GenerateReport();
            _logger.LogInfo("═══════════════════════════════════════════");
            _logger.LogInfo("RELATÓRIO DE AUDITORIA - MODO GAMER");
            _logger.LogInfo("═══════════════════════════════════════════");
            _logger.LogInfo($"Total de Otimizações: {report.TotalOptimizations}");
            _logger.LogInfo($"Sucesso: {report.SuccessfulOptimizations}");
            _logger.LogInfo($"Falhas: {report.FailedOptimizations}");
            _logger.LogInfo($"Reversões pelo Windows: {report.Reversions}");
            _logger.LogInfo($"Falhas de Driver: {report.DriverFailures}");
            _logger.LogInfo($"Taxa de Efetividade: {report.EffectivenessRate:F1}%");
            _logger.LogInfo("═══════════════════════════════════════════");

            if (report.Reversions > 0)
            {
                _logger.LogWarning($"ATENÇÃO: {report.Reversions} configurações foram revertidas pelo Windows");
            }
            if (report.DriverFailures > 0)
            {
                _logger.LogWarning($"ATENÇÃO: {report.DriverFailures} otimizações de driver falharam");
            }
            _logger.LogExit(nameof(PrintSummary));
        }

        public static AuditCategory MapServiceToCategory(string service)
        {
            var s = service.ToUpperInvariant();
            if (s.Contains("CPU")) return AuditCategory.CPU;
            if (s.Contains("GPU") || s.Contains("HAGS") || s.Contains("TDR") || s.Contains("GRAPHICS")) return AuditCategory.GPU;
            if (s.Contains("MEMORY") || s.Contains("RAM")) return AuditCategory.RAM;
            if (s.Contains("NETWORK") || s.Contains("TCP") || s.Contains("DNS")) return AuditCategory.Network;
            if (s.Contains("POWER") || s.Contains("ENERGY") || s.Contains("PLAN")) return AuditCategory.Energy;
            if (s.Contains("PROCESS") || s.Contains("TASK")) return AuditCategory.Process;
            if (s.Contains("SERVICE")) return AuditCategory.Service;
            if (s.Contains("REGISTRY")) return AuditCategory.Registry;
            if (s.Contains("GAME") || s.Contains("LAUNCHER")) return AuditCategory.Game;
            if (s.Contains("AI") || s.Contains("BRAIN") || s.Contains("INTELLIGENCE")) return AuditCategory.AI;
            if (s.Contains("DWM") || s.Contains("DESKTOP")) return AuditCategory.DWM;
            if (s.Contains("EXPLORER")) return AuditCategory.Explorer;
            if (s.Contains("GAMEDVR") || s.Contains("GAME DVR")) return AuditCategory.GameDVR;
            if (s.Contains("GAMEMODE") || s.Contains("GAME MODE")) return AuditCategory.GameMode;
            if (s.Contains("TELEMETRY")) return AuditCategory.Telemetry;
            if (s.Contains("PIPELINE") || s.Contains("STAGE")) return AuditCategory.Pipeline;
            return AuditCategory.System;
        }
    }

    public class AuditEntry
    {
        public DateTime Timestamp { get; set; }
        public string Service { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string Result { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
    }

    public class AuditReport
    {
        public int TotalOptimizations { get; set; }
        public int SuccessfulOptimizations { get; set; }
        public int FailedOptimizations { get; set; }
        public int Reversions { get; set; }
        public int DriverFailures { get; set; }
        public double EffectivenessRate { get; set; }
        public List<AuditEntry> Entries { get; set; } = new();
    }
}
