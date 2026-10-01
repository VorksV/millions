using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.Performance.Benchmark.Models;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Performance.Benchmark
{
    /// <summary>
    /// Handles persistence of benchmark states across system restarts.
    /// Crucial for "100% Professional" SaaS validation flows.
    /// </summary>
    public sealed class BenchmarkPersistenceService
    {
        private static readonly string StatePath = AppDataPaths.BenchmarkPendingValidation;

        private readonly ILoggingService _logger;

        public BenchmarkPersistenceService(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Ensure directory exists
            var dir = Path.GetDirectoryName(StatePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        public async Task SavePendingBenchmarkAsync(string optimizationName, BenchmarkContext beforeContext)
        {
            try
            {
                var state = new PendingBenchmarkState
                {
                    OptimizationName = optimizationName,
                    BeforeContext = beforeContext,
                    SavedAt = DateTime.UtcNow
                };

                var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,  WriteIndented = true });
                await File.WriteAllTextAsync(StatePath, json);
                
                _logger.LogInfo($"[BenchmarkPersistence] Pending benchmark saved to disk: {optimizationName}");
            }
            catch (Exception ex)
            {
                _logger.LogError("[BenchmarkPersistence] Failed to save pending benchmark", ex);
            }
        }

        public async Task<PendingBenchmarkState?> LoadPendingBenchmarkAsync()
        {
            if (!File.Exists(StatePath)) return null;

            try
            {
                var json = await File.ReadAllTextAsync(StatePath);
                var state = JsonSerializer.Deserialize<PendingBenchmarkState>(json);
                
                // Validate expiry (if older than 48h, ignore)
                if (state != null && (DateTime.UtcNow - state.SavedAt).TotalHours > 48)
                {
                    _logger.LogWarning("[BenchmarkPersistence] Found pending benchmark but it's too old (expired).");
                    ClearPendingBenchmark();
                    return null;
                }

                return state;
            }
            catch (Exception ex)
            {
                _logger.LogError("[BenchmarkPersistence] Failed to load pending benchmark", ex);
                return null;
            }
        }

        public void ClearPendingBenchmark()
        {
            if (File.Exists(StatePath))
            {
                try { File.Delete(StatePath); } catch { }
            }
        }
    }

    public class PendingBenchmarkState
    {
        public string OptimizationName { get; set; } = "";
        public BenchmarkContext BeforeContext { get; set; } = null!;
        public DateTime SavedAt { get; set; }
    }
}
