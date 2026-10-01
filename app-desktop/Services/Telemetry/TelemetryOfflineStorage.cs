using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Telemetry
{
    public class TelemetryOfflineStorage
    {
        public TelemetryOfflineStorage(ILoggingService? logger = null) { }
        public Task StoreEventsAsync(List<object> events) => Task.CompletedTask;
        public Task<List<EventBatch>> GetPendingBatchesAsync() => Task.FromResult(new List<EventBatch>());
        public Task DeleteBatchAsync(EventBatch batch) => Task.CompletedTask;
        public Task IncrementRetryCountAsync(EventBatch batch) => Task.CompletedTask;
        public Task<OfflineStorageStats> GetStatsAsync() => Task.FromResult(new OfflineStorageStats());

        public class EventBatch
        {
            public Guid BatchId { get; set; }
            public DateTime CreatedAt { get; set; }
            public int EventCount { get; set; }
            public List<object> Events { get; set; } = new List<object>();
            public int RetryCount { get; set; }
            public DateTime? LastRetryAt { get; set; }
            public string? FilePath { get; set; }
        }

        public class OfflineStorageStats
        {
            public int TotalBatches { get; set; }
            public int TotalEvents { get; set; }
            public DateTime? OldestBatch { get; set; }
            public int TotalRetries { get; set; }
        }
    }
}
