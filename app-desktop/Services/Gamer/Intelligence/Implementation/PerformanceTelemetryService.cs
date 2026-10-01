using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer.Intelligence.Implementation
{
    public class PerformanceTelemetryService
    {
        private static readonly Lazy<PerformanceTelemetryService> _instance = 
            new Lazy<PerformanceTelemetryService>(() => new PerformanceTelemetryService(App.LoggingService!));
            
        public static PerformanceTelemetryService Instance => _instance.Value;

        private readonly ILoggingService _logger;
        private readonly string _logDir;
        private readonly string _logFile;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        private PerformanceTelemetryService(ILoggingService logger)
        {
            _logger = logger;
            _logger.LogEntry("[PerformanceTelemetryService] .ctor");
            _logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Voltris", "Logs", "GamerMode");
            if (!Directory.Exists(_logDir))
            {
                Directory.CreateDirectory(_logDir);
            }
            _logFile = Path.Combine(_logDir, $"GamerTelemetry_{DateTime.Now:yyyyMMdd}.log");
            _logger.LogExit("[PerformanceTelemetryService] .ctor");
        }

        public async Task LogEventAsync(string category, string eventName, object data)
        {
            _logger.LogEntry(nameof(LogEventAsync), ("category", category), ("eventName", eventName));
            await _lock.WaitAsync();
            try
            {
                var correlationId = Guid.NewGuid().ToString("N").Substring(0, 8);
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var threadId = Thread.CurrentThread.ManagedThreadId;
                
                var dataJson = JsonSerializer.Serialize(data, new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,  WriteIndented = false });
                
                var logLine = $"[{timestamp}] [Thread:{threadId:D2}] [{correlationId}] [{category}] {eventName} => {dataJson}\n";
                
                await File.AppendAllTextAsync(_logFile, logLine);
                
                // Mirror to standard Voltris log
                _logger.LogInfo($"[Telemetry] [{category}] {eventName} => {dataJson}");
                _logger.LogExit(nameof(LogEventAsync), "Success");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to write telemetry: {ex.Message}");
                _logger.LogExit(nameof(LogEventAsync), $"Error: {ex.Message}");
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task LogOptimizationAppliedAsync(string processName, string optimizationName, string evidenceLevel, string result, long durationMs)
        {
            _logger.LogEntry(nameof(LogOptimizationAppliedAsync), ("processName", processName), ("optimizationName", optimizationName));
            await LogEventAsync("OPTIMIZATION", optimizationName, new
            {
                Process = processName,
                EvidenceLevel = evidenceLevel,
                Result = result,
                DurationMs = durationMs
            });
            _logger.LogExit(nameof(LogOptimizationAppliedAsync));
        }

        public async Task LogProfileSwitchAsync(string previousProfile, string currentProfile, string triggerReason)
        {
            _logger.LogEntry(nameof(LogProfileSwitchAsync), ("previousProfile", previousProfile), ("currentProfile", currentProfile));
            await LogEventAsync("PROFILE_SWITCH", "AutoSwitch", new
            {
                Previous = previousProfile,
                Current = currentProfile,
                Reason = triggerReason
            });
            _logger.LogExit(nameof(LogProfileSwitchAsync));
        }
        
        public async Task LogBenchmarkResultAsync(string processName, string metricName, double valueBefore, double valueAfter)
        {
            _logger.LogEntry(nameof(LogBenchmarkResultAsync), ("processName", processName), ("metricName", metricName));
            double gain = valueBefore > 0 ? ((valueAfter - valueBefore) / valueBefore) * 100 : 0;
            
            await LogEventAsync("BENCHMARK", metricName, new
            {
                Process = processName,
                Before = valueBefore,
                After = valueAfter,
                GainPercent = Math.Round(gain, 2)
            });
            _logger.LogExit(nameof(LogBenchmarkResultAsync));
        }
    }

    public enum OptimizationEvidenceLevel
    {
        Unknown,
        Theoretical,
        Measured,
        Verified
    }
}
