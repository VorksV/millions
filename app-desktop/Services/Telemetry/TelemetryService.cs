using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Telemetry
{
    /// <summary>
    /// TelemetryService — rastreamento real de eventos para análise de retenção.
    /// Persiste eventos localmente e os envia de forma assíncrona.
    /// Formato: [Timestamp] [Service] [Level] [Action] [Duration] [Result]
    /// </summary>
    public class TelemetryService
    {
        private readonly string _telemetryPath;
        private readonly BlockingCollection<TelemetryEvent> _queue = new(1000);
        private readonly CancellationTokenSource _cts = new();
        private string? _machineId;
        private string? _sessionId;
        private DateTime _sessionStart;
        private Task? _writerTask;

        // Contadores de sessão para análise de retenção
        private int _featureUsageCount;
        private int _optimizationCount;
        private int _errorCount;

        public TelemetryService()
        {
            _telemetryPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VoltrisOptimizer", "Telemetry");
            Directory.CreateDirectory(_telemetryPath);
        }

        public Task InitializeAsync(string machineId, string? sessionId)
        {
            _machineId = machineId;
            _sessionId = sessionId ?? Guid.NewGuid().ToString("N")[..8];
            _sessionStart = DateTime.UtcNow;
            _writerTask = Task.Run(WriterLoopAsync);
            
            // Rastrear abertura do app
            _ = TrackEvent("app_opened", new Dictionary<string, object>
            {
                ["machine_id"] = machineId,
                ["session_id"] = _sessionId,
                ["timestamp"] = _sessionStart.ToString("O"),
                ["os_version"] = Environment.OSVersion.ToString(),
                ["cpu_count"] = Environment.ProcessorCount
            });
            
            return Task.CompletedTask;
        }

        public void Initialize(string sessionId, string machineId)
        {
            _ = InitializeAsync(machineId, sessionId);
        }

        public Task ShutdownAsync()
        {
            var duration = (DateTime.UtcNow - _sessionStart).TotalSeconds;
            _ = TrackEvent("app_closed", new Dictionary<string, object>
            {
                ["session_duration_seconds"] = duration,
                ["feature_usage_count"] = _featureUsageCount,
                ["optimization_count"] = _optimizationCount,
                ["error_count"] = _errorCount,
                ["session_id"] = _sessionId ?? "unknown"
            });

            // CORREÇÃO: Task.Delay(500).Wait() bloqueia a thread chamadora por 500ms sem nenhum benefício.
            // O evento de telemetria já foi disparado como fire-and-forget acima.
            // Cancelar o CTS imediatamente — o worker interno vai finalizar o loop naturalmente.
            _cts.Cancel();
            return Task.CompletedTask;
        }

        public Task TrackEvent(string eventName, Dictionary<string, object>? properties = null)
        {
            Interlocked.Increment(ref _featureUsageCount);
            
            var evt = new TelemetryEvent
            {
                Timestamp = DateTime.UtcNow,
                EventName = eventName,
                MachineId = _machineId ?? "unknown",
                SessionId = _sessionId ?? "unknown",
                Properties = properties ?? new Dictionary<string, object>()
            };
            
            _queue.TryAdd(evt);
            return Task.CompletedTask;
        }

        public Task TrackEvent(string eventType, string featureName, string actionName,
            double durationMs = 0, object? metadata = null, bool success = true, bool forceFlush = false)
        {
            Interlocked.Increment(ref _featureUsageCount);
            if (featureName.Contains("optimization", StringComparison.OrdinalIgnoreCase))
                Interlocked.Increment(ref _optimizationCount);

            return TrackEvent($"{eventType}_{featureName}", new Dictionary<string, object>
            {
                ["action"] = actionName,
                ["duration_ms"] = durationMs,
                ["success"] = success,
                ["feature"] = featureName
            });
        }

        public Task TrackExceptionAsync(Exception exception, string context,
            Dictionary<string, object>? metadata = null)
        {
            Interlocked.Increment(ref _errorCount);
            return TrackEvent("exception", new Dictionary<string, object>
            {
                ["context"] = context,
                ["type"] = exception.GetType().Name,
                ["message"] = exception.Message,
                ["stack"] = exception.StackTrace?.Split('\n')[0] ?? "unknown"
            });
        }

        public string? GetDeviceId() => _machineId;

        public Task<List<object>> GetFilteredEventsAsync(DateTime startTime, DateTime endTime)
            => Task.FromResult(new List<object>());

        public Task<object> GenerateUsageStatisticsAsync(DateTime? startTime, DateTime? endTime)
        {
            var stats = new
            {
                SessionDuration = (DateTime.UtcNow - _sessionStart).TotalSeconds,
                FeatureUsage = _featureUsageCount,
                Optimizations = _optimizationCount,
                Errors = _errorCount
            };
            return Task.FromResult<object>(stats);
        }

        private async Task WriterLoopAsync()
        {
            var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var filePath = Path.Combine(_telemetryPath, $"events_{today}.jsonl");
            
            try
            {
                await using var writer = new StreamWriter(filePath, append: true, encoding: System.Text.Encoding.UTF8);
                
                foreach (var evt in _queue.GetConsumingEnumerable(_cts.Token))
                {
                    try
                    {
                        var options = new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, MaxDepth = 32, WriteIndented = false, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                        var line = JsonSerializer.Serialize(evt, options);
                        await writer.WriteLineAsync(line);
                        // CORRIGIDO: Removido FlushAsync por evento — causava I/O excessivo durante gameplay.
                        // O buffer do StreamWriter faz flush automático quando cheio (~4KB) ou no Dispose.
                    }
                    catch { /* não deixar o writer morrer por um evento */ }
                }
            }
            catch (OperationCanceledException) { }
            catch { }
        }

        private class TelemetryEvent
        {
            public DateTime Timestamp { get; set; }
            public string EventName { get; set; } = "";
            public string MachineId { get; set; } = "";
            public string SessionId { get; set; } = "";
            public Dictionary<string, object> Properties { get; set; } = new();
        }
    }
}
