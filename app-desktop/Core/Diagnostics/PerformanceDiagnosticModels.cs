using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace VoltrisOptimizer.Core.Diagnostics
{
    public sealed class CpuSample
    {
        public string Source { get; init; } = "";
        public double CpuPercent { get; set; }
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
        public long ThreadId { get; init; }
    }

    public sealed class MemorySample
    {
        public long WorkingSetMb { get; set; }
        public long PrivateBytesMb { get; set; }
        public long ManagedHeapMb { get; set; }
        public long LargeObjectHeapMb { get; set; }
        public int Gen0Collections { get; set; }
        public int Gen1Collections { get; set; }
        public int Gen2Collections { get; set; }
        public long LiveObjects { get; set; }
        public double AllocationsPerSecondMb { get; set; }
        public Dictionary<string, long> TopObjectSizes { get; init; } = new();
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    }

    public sealed class ThreadSample
    {
        public int TotalThreads { get; set; }
        public int ThreadPoolThreads { get; set; }
        public int ThreadPoolPendingWorkItems { get; set; }
        public int ActiveThreads { get; set; }
        public int BlockedThreads { get; set; }
        public List<TaskInfo> ActiveTasks { get; init; } = new();
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    }

    public sealed class TaskInfo
    {
        public int Id { get; init; }
        public string Status { get; init; } = "";
        public string CreationStackTrace { get; init; } = "";
        public bool IsCompleted { get; init; }
        public bool IsFaulted { get; init; }
        public bool IsCanceled { get; init; }
    }

    public sealed class TimerInfo
    {
        public string Name { get; init; } = "";
        public string Type { get; init; } = "";
        public int IntervalMs { get; init; }
        public bool IsRunning { get; set; }
        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
        public long TotalTicks { get; set; }
        public string CreatedByStackTrace { get; init; } = "";
    }

    public sealed class EventSubscriptionInfo
    {
        public string EventSource { get; init; } = "";
        public string HandlerMethod { get; init; } = "";
        public string SubscriberClass { get; init; } = "";
        public DateTime SubscribedAt { get; init; } = DateTime.UtcNow;
        public bool IsLambda { get; init; }
        public bool CanBeRemoved { get; init; }
    }

    public sealed class DiagnosticSnapshot
    {
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
        public CpuSample? Cpu { get; init; }
        public MemorySample? Memory { get; init; }
        public ThreadSample? Threads { get; init; }
        public List<TimerInfo> Timers { get; init; } = new();
        public List<EventSubscriptionInfo> EventSubscriptions { get; init; } = new();
        public Dictionary<string, long> ServiceInitTimesMs { get; init; } = new();
        public List<string> ActiveMonitors { get; init; } = new();
        public List<string> PollingLoops { get; init; } = new();
        public long ManagedMemoryAllocatedMb { get; set; }
    }

    public sealed class DiagnosticReport
    {
        public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
        public string RootCause { get; set; } = "";
        public List<string> CulpritClasses { get; init; } = new();
        public List<string> CulpritMethods { get; init; } = new();
        public int? CulpritLineNumber { get; set; }
        public double EstimatedCpuHeadroomPercent { get; set; }
        public long EstimatedRamReductionMb { get; set; }
        public List<string> Evidence { get; init; } = new();
        public List<string> FixesApplied { get; init; } = new();
        public string IntermittentExplanation { get; set; } = "";
    }
}
