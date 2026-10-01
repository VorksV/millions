using System;

namespace VoltrisOptimizer.Services.Performance.Orchestration.Models
{
    /// <summary>
    /// Result of an orchestration execution.
    /// Read-only, descriptive only.
    /// </summary>
    public sealed class OrchestrationResult
    {
        public bool Success { get; }
        public string Message { get; }
        public OrchestrationStatus Status { get; }
        public DateTime CompletedAt { get; }
        public TimeSpan Duration { get; }
        public string? ErrorDetails { get; }

        private OrchestrationResult(
            bool success,
            string message,
            OrchestrationStatus status,
            TimeSpan duration,
            string? errorDetails = null)
        {
            Success = success;
            Message = message ?? throw new ArgumentNullException(nameof(message));
            Status = status;
            CompletedAt = DateTime.UtcNow;
            Duration = duration;
            ErrorDetails = errorDetails;
        }

        public static OrchestrationResult Succeeded(string message, TimeSpan duration) =>
            new OrchestrationResult(true, message, OrchestrationStatus.Executed, duration);

        public static OrchestrationResult Skipped(string reason) =>
            new OrchestrationResult(true, reason, OrchestrationStatus.Skipped, TimeSpan.Zero);

        public static OrchestrationResult Deduplicated(string reason) =>
            new OrchestrationResult(true, reason, OrchestrationStatus.Deduplicated, TimeSpan.Zero);

        public static OrchestrationResult Throttled(string reason) =>
            new OrchestrationResult(true, reason, OrchestrationStatus.Throttled, TimeSpan.Zero);

        public static OrchestrationResult Failed(string message, TimeSpan duration, string? errorDetails = null) =>
            new OrchestrationResult(false, message, OrchestrationStatus.Failed, duration, errorDetails);

        public override string ToString() =>
            $"OrchestrationResult[{Status}] {Message} (Duration: {Duration.TotalMilliseconds:F0}ms)";
    }

    /// <summary>
    /// Status of orchestration execution
    /// </summary>
    public enum OrchestrationStatus
    {
        Executed,       // Decision was executed successfully
        Skipped,        // Decision was skipped (e.g., NoAction)
        Deduplicated,   // Same decision already applied
        Throttled,      // Stability window not elapsed
        Failed          // Execution failed
    }
}
