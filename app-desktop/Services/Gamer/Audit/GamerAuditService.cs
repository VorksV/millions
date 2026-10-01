using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer.Audit
{
    public class GamerAuditService : IGamerAuditService
    {
        private readonly ILoggingService _logger;
        private readonly IRegistryService? _registry;

        private readonly ConcurrentQueue<OptimizationAuditEntry> _optimizationQueue = new();
        private readonly ConcurrentQueue<ServiceAuditEntry> _serviceQueue = new();
        private readonly ConcurrentQueue<ProcessAuditEntry> _processQueue = new();
        private readonly ConcurrentQueue<RegistryAuditEntry> _registryQueue = new();
        private readonly ConcurrentQueue<GameAuditEntry> _gameQueue = new();
        private readonly ConcurrentQueue<AIAuditEntry> _aiQueue = new();
        private readonly ConcurrentQueue<PipelineStageAuditEntry> _pipelineQueue = new();

        private readonly List<OptimizationAuditEntry> _committedOptimizations = new();
        private readonly List<ServiceAuditEntry> _committedServices = new();
        private readonly List<ProcessAuditEntry> _committedProcesses = new();
        private readonly List<RegistryAuditEntry> _committedRegistry = new();
        private readonly List<GameAuditEntry> _committedGames = new();
        private readonly List<AIAuditEntry> _committedAI = new();
        private readonly List<PipelineStageAuditEntry> _committedPipeline = new();
        private HardwareAuditEntry? _hardware;

        private readonly object _flushLock = new();
        private readonly SemaphoreSlim _writeSemaphore = new(1, 1);
        private readonly Timer _flushTimer;
        private readonly Stopwatch _sessionTimer = new();

        private const int FlushIntervalMs = 5000;
        private const int BatchSizeThreshold = 50;

        private string _sessionId = string.Empty;
        private string? _gameName;
        private int? _gameProcessId;
        private DateTime _sessionStart;
        private bool _disposed;

        private readonly string _auditDirectory;

        public GamerAuditService(ILoggingService logger, IRegistryService? registry = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _registry = registry;

            _auditDirectory = Path.Combine(LogDirectoryResolver.Resolve(), "GamerAudit");
            Directory.CreateDirectory(_auditDirectory);

            _flushTimer = new Timer(async _ => await FlushAsync(), null, Timeout.Infinite, Timeout.Infinite);

            _logger.LogEntry(nameof(GamerAuditService));
            _logger.LogExit(nameof(GamerAuditService));
        }

        public void StartSession(string? gameName = null, int? gameProcessId = null)
        {
            _logger.LogEntry(nameof(StartSession));
            _sessionId = Guid.NewGuid().ToString("N")[..12];
            _gameName = gameName;
            _gameProcessId = gameProcessId;
            _sessionStart = DateTime.Now;
            _sessionTimer.Restart();

            _logger.LogInfo($"[GamerAudit] Sessão {_sessionId} iniciada{(gameName != null ? $" para {gameName}" : "")}");

            _flushTimer.Change(FlushIntervalMs, FlushIntervalMs);
            _logger.LogExit(nameof(StartSession));
        }

        public void EndSession()
        {
            _logger.LogEntry(nameof(EndSession));
            Task.Run(async () => await EndSessionAsync()).GetAwaiter().GetResult();
            _logger.LogExit(nameof(EndSession));
        }

        public async Task EndSessionAsync()
        {
            _logger.LogEntry(nameof(EndSessionAsync));
            _flushTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _sessionTimer.Stop();

            await FlushAsync();
            await SaveReportAsync();

            _logger.LogInfo($"[GamerAudit] Sessão {_sessionId} finalizada ({_sessionTimer.Elapsed.TotalSeconds:F1}s)");
            _logger.LogExit(nameof(EndSessionAsync));
        }

        public void RecordOptimization(OptimizationAuditEntry entry)
        {
            _logger.LogEntry(nameof(RecordOptimization));
            entry.SessionId = _sessionId;
            entry.ThreadId = Environment.CurrentManagedThreadId;
            _optimizationQueue.Enqueue(entry);

            if (_optimizationQueue.Count >= BatchSizeThreshold)
                _ = FlushAsync();
            _logger.LogExit(nameof(RecordOptimization));
        }

        public async Task RecordOptimizationAsync(OptimizationAuditEntry entry)
        {
            _logger.LogEntry(nameof(RecordOptimizationAsync));
            entry.SessionId = _sessionId;
            entry.ThreadId = Environment.CurrentManagedThreadId;
            _optimizationQueue.Enqueue(entry);

            if (_optimizationQueue.Count >= BatchSizeThreshold)
                await FlushAsync();
            _logger.LogExit(nameof(RecordOptimizationAsync));
        }

        public void RecordServiceChange(ServiceAuditEntry entry)
        {
            _logger.LogEntry(nameof(RecordServiceChange));
            _serviceQueue.Enqueue(entry);
            _logger.LogExit(nameof(RecordServiceChange));
        }

        public void RecordProcessAction(ProcessAuditEntry entry)
        {
            _logger.LogEntry(nameof(RecordProcessAction));
            _processQueue.Enqueue(entry);
            _logger.LogExit(nameof(RecordProcessAction));
        }

        public void RecordRegistryChange(RegistryAuditEntry entry)
        {
            _logger.LogEntry(nameof(RecordRegistryChange));
            _registryQueue.Enqueue(entry);
            _logger.LogExit(nameof(RecordRegistryChange));
        }

        public void RecordGameDetection(GameAuditEntry entry)
        {
            _logger.LogEntry(nameof(RecordGameDetection));
            _gameQueue.Enqueue(entry);
            _logger.LogExit(nameof(RecordGameDetection));
        }

        public void RecordHardware(HardwareAuditEntry entry)
        {
            _logger.LogEntry(nameof(RecordHardware));
            _hardware = entry;
            _logger.LogExit(nameof(RecordHardware));
        }

        public void RecordAIDecision(AIAuditEntry entry)
        {
            _logger.LogEntry(nameof(RecordAIDecision));
            _aiQueue.Enqueue(entry);
            _logger.LogExit(nameof(RecordAIDecision));
        }

        public void RecordPipelineStage(PipelineStageAuditEntry entry)
        {
            _logger.LogEntry(nameof(RecordPipelineStage));
            _pipelineQueue.Enqueue(entry);
            _logger.LogExit(nameof(RecordPipelineStage));
        }

        public async Task FlushAsync()
        {
            _logger.LogEntry(nameof(FlushAsync));
            if (_disposed) { _logger.LogExit(nameof(FlushAsync)); return; }

            await _writeSemaphore.WaitAsync();
            try
            {
                lock (_flushLock)
                {
                    DrainQueue(_optimizationQueue, _committedOptimizations);
                    DrainQueue(_serviceQueue, _committedServices);
                    DrainQueue(_processQueue, _committedProcesses);
                    DrainQueue(_registryQueue, _committedRegistry);
                    DrainQueue(_gameQueue, _committedGames);
                    DrainQueue(_aiQueue, _committedAI);
                    DrainQueue(_pipelineQueue, _committedPipeline);
                }

                await WriteBufferedEntriesAsync();
            }
            finally
            {
                _writeSemaphore.Release();
            }
            _logger.LogExit(nameof(FlushAsync));
        }

        private static void DrainQueue<T>(ConcurrentQueue<T> source, List<T> target)
        {
            while (source.TryDequeue(out var item))
            {
                target.Add(item);
            }
        }

        private async Task WriteBufferedEntriesAsync()
        {
            _logger.LogEntry(nameof(WriteBufferedEntriesAsync));
            List<OptimizationAuditEntry> batch;
            lock (_flushLock)
            {
                if (_committedOptimizations.Count == 0) return;
                batch = new List<OptimizationAuditEntry>(_committedOptimizations);
                _committedOptimizations.Clear();
            }

            try
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var filePath = Path.Combine(_auditDirectory, $"audit_batch_{_sessionId}_{timestamp}.json");

                var wrapper = new AuditBatchWrapper
                {
                    SessionId = _sessionId,
                    Timestamp = DateTime.Now,
                    Entries = batch
                };

                var json = JsonSerializer.Serialize(wrapper, _jsonOptions);
                await File.WriteAllTextAsync(filePath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GamerAudit] Erro ao escrever batch: {ex.Message}", ex);
            }
            _logger.LogExit(nameof(WriteBufferedEntriesAsync));
        }

        public async Task<ConsolidatedAuditReport> GenerateReportAsync()
        {
            _logger.LogEntry(nameof(GenerateReportAsync));
            await FlushAsync();

            var report = new ConsolidatedAuditReport
            {
                ReportId = _sessionId,
                GeneratedAt = DateTime.Now,
                SessionStart = _sessionStart,
                SessionEnd = DateTime.Now,
                GameName = _gameName,
                GameProcessId = _gameProcessId,
                Hardware = _hardware
            };

            lock (_flushLock)
            {
                report.Optimizations = new List<OptimizationAuditEntry>(_committedOptimizations);
                report.Services = new List<ServiceAuditEntry>(_committedServices);
                report.Processes = new List<ProcessAuditEntry>(_committedProcesses);
                report.RegistryChanges = new List<RegistryAuditEntry>(_committedRegistry);
                report.Games = new List<GameAuditEntry>(_committedGames);
                report.AIEntries = new List<AIAuditEntry>(_committedAI);
                report.PipelineStages = new List<PipelineStageAuditEntry>(_committedPipeline);
            }

            var stats = new AuditStatistics();
            var byCategory = new Dictionary<AuditCategory, CategoryStats>();

            foreach (var opt in report.Optimizations)
            {
                stats.TotalOptimizations++;

                switch (opt.Result)
                {
                    case AuditResult.SUCCESS: stats.Successful++; break;
                    case AuditResult.FAILED: stats.Failed++; break;
                    case AuditResult.IGNORED:
                    case AuditResult.SKIPPED: stats.Ignored++; break;
                    case AuditResult.NOT_SUPPORTED: stats.NotSupported++; break;
                    case AuditResult.BLOCKED_BY_WINDOWS: stats.BlockedByWindows++; break;
                    case AuditResult.BLOCKED_BY_DRIVER: stats.BlockedByDriver++; break;
                    case AuditResult.REVERTED_BY_WINDOWS: stats.Reverted++; break;
                }

                switch (opt.Validation)
                {
                    case ValidationStatus.VALIDATED: stats.Validated++; break;
                    case ValidationStatus.VALIDATION_FAILED: stats.ValidationFailed++; break;
                }

                if (!byCategory.TryGetValue(opt.Category, out var catStats))
                {
                    catStats = new CategoryStats { Category = opt.Category };
                    byCategory[opt.Category] = catStats;
                }
                catStats.Total++;
                catStats.TotalExecutionTimeMs += opt.ExecutionTimeMs;
                if (opt.Result == AuditResult.SUCCESS) catStats.Success++;
                if (opt.Result == AuditResult.FAILED) catStats.Failed++;
                if (opt.Validation == ValidationStatus.VALIDATED) catStats.Validated++;
            }

            stats.ByCategory = byCategory;
            report.Stats = stats;

            _logger.LogExit(nameof(GenerateReportAsync));
            return report;
        }

        public async Task SaveReportAsync()
        {
            _logger.LogEntry(nameof(SaveReportAsync));
            try
            {
                var report = await GenerateReportAsync();
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var filePath = Path.Combine(_auditDirectory, $"audit_report_{_sessionId}_{timestamp}.json");

                var json = JsonSerializer.Serialize(report, _jsonOptions);
                await File.WriteAllTextAsync(filePath, json);

                _logger.LogSuccess($"[GamerAudit] Relatório consolidado salvo: {filePath}");
                _logger.LogInfo($"[GamerAudit] Total: {report.Stats.TotalOptimizations} | Sucesso: {report.Stats.Successful} | Falhas: {report.Stats.Failed} | Validações: {report.Stats.Validated}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[GamerAudit] Erro ao salvar relatório: {ex.Message}", ex);
            }
            _logger.LogExit(nameof(SaveReportAsync));
        }

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
            IncludeFields = true
        };

        public void Dispose()
        {
            _logger.LogEntry(nameof(Dispose));
            if (_disposed) { _logger.LogExit(nameof(Dispose)); return; }
            _disposed = true;

            _flushTimer?.Dispose();
            _writeSemaphore?.Dispose();

            try
            {
                if (_sessionTimer.IsRunning)
                {
                    Task.Run(async () => await EndSessionAsync()).GetAwaiter().GetResult();
                }
            }
            catch { }
            _logger.LogExit(nameof(Dispose));
        }
    }

    internal class AuditBatchWrapper
    {
        public string SessionId { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public List<OptimizationAuditEntry> Entries { get; set; } = new();
    }
}
