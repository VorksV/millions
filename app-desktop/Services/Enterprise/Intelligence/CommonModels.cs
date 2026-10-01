using System;
using System.Collections.Generic;

namespace VoltrisOptimizer.Services.Enterprise.Intelligence
{
    /// <summary>
    /// COMMON MODELS
    /// Modelos compartilhados para evitar duplicação
    /// </summary>

    public class ActionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }

    public class RealTimeMetric
    {
        public DateTime Timestamp { get; set; }
        public double FrameTimeMs { get; set; }
        public double FrameVarianceMs { get; set; }
        public double DpcLatencyMs { get; set; }
        public double CpuQueueLength { get; set; }
        public double IoWaitTimeMs { get; set; }
        public double GpuUsagePercent { get; set; }
        public int ThreadCount { get; set; }
        public int HandleCount { get; set; }
        public double MemoryMB { get; set; }
    }

    public class PerformanceMetric
    {
        public string Name { get; set; } = string.Empty;
        public double Value { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class AnomalyDetection
    {
        public string Id { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public AnomalySeverity Severity { get; set; }
        public string Description { get; set; } = string.Empty;
        public Dictionary<string, PerformanceMetric> Metrics { get; set; } = new();
    }

    public enum AnomalySeverity
    {
        Low,
        Medium,
        High,
        Critical
    }

    public class PerformanceDelta
    {
        public double OverallImprovement { get; set; }
        public Dictionary<string, double> MetricDeltas { get; set; } = new();
        public DateTime Timestamp { get; set; }
    }

    public class ActionImpact
    {
        public RealTimeMetric BeforeMetrics { get; set; } = new();
        public RealTimeMetric AfterMetrics { get; set; } = new();
        public bool IsPositive { get; set; }
        public double FrameTimeDelta { get; set; }
        public double CpuQueueDelta { get; set; }
        public double DpcLatencyDelta { get; set; }
        public double IoWaitDelta { get; set; }
        public double MemoryDelta { get; set; }
    }

    public class ProblemAnalysis
    {
        public PerformanceProblem Problem { get; set; } = new();
        public string RootCause { get; set; } = string.Empty;
        public double Confidence { get; set; }
        public string RecommendedAction { get; set; } = string.Empty;
    }

    public class PerformanceProblem
    {
        public ProblemType Type { get; set; }
        public string Description { get; set; } = string.Empty;
        public double Severity { get; set; }
        public Dictionary<string, double> Metrics { get; set; } = new();
    }

    public enum ProblemType
    {
        Stutter,
        CpuContention,
        MemoryPressure,
        IoBottleneck,
        GpuBottleneck,
        NetworkLatency,
        Unknown
    }
}
