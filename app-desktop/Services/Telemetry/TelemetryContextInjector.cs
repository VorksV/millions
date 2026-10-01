using System;
using System.Collections.Generic;

namespace VoltrisOptimizer.Services.Telemetry
{
    public class TelemetryContextInjector
    {
        private static TelemetryContextInjector? _instance;
        public static TelemetryContextInjector Instance => _instance ??= new TelemetryContextInjector();
        private TelemetryContextInjector() { }

        public EnrichedEvent EnrichEvent(string eventType, string featureName, string actionName,
            double durationMs = 0, bool success = true, string? errorCode = null,
            object? metadata = null, Guid? sessionId = null, string? deviceId = null)
            => new EnrichedEvent { EventType = eventType, FeatureName = featureName, ActionName = actionName };

        public void SetFeatureFlag(string flagName, bool enabled) { }
        public Dictionary<string, bool> GetFeatureFlags() => new Dictionary<string, bool>();
        public VersionContext? GetVersionContext() => null;
    }

    public class EnrichedEvent
    {
        public string EventType { get; set; } = "";
        public string FeatureName { get; set; } = "";
        public string ActionName { get; set; } = "";
        public int DurationMs { get; set; }
        public bool Success { get; set; }
        public string? ErrorCode { get; set; }
        public object? Metadata { get; set; }
        public DateTime Timestamp { get; set; }
        public VersionContext? Version { get; set; }
        public SessionContext? Session { get; set; }
        public FeatureFlagContext? FeatureFlags { get; set; }
    }

    public class VersionContext
    {
        public string AppVersion { get; set; } = "";
        public string DeployVersion { get; set; } = "";
        public string GitCommitHash { get; set; } = "";
        public string BuildNumber { get; set; } = "";
        public string Environment { get; set; } = "";
    }

    public class SessionContext
    {
        public Guid? SessionId { get; set; }
        public string? DeviceId { get; set; }
    }

    public class FeatureFlagContext
    {
        public Dictionary<string, bool> Flags { get; set; } = new Dictionary<string, bool>();
        public string SnapshotHash { get; set; } = "";
    }
}
