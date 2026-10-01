using System;

namespace VoltrisOptimizer.Services.Performance.Benchmark.Models
{
    /// <summary>
    /// Read-only snapshot of system performance metrics.
    /// Used for scientific before/after comparison.
    /// ZERO side effects - purely observational.
    /// </summary>
    public sealed class BenchmarkContext
    {
        // Metadata
        public string ContextId { get; }
        public DateTime CapturedAt { get; }
        public string EnvironmentHash { get; } // Hardware + OS fingerprint
        
        // CPU Metrics
        public CpuMetrics Cpu { get; }
        
        // Memory Metrics
        public MemoryMetrics Memory { get; }
        
        // Disk Metrics
        public DiskMetrics Disk { get; }
        
        // User Experience Metrics
        public UserExperienceMetrics UserExperience { get; }
        
        // System State
        public int ActiveProcessCount { get; }
        public int WeightedProcessScore { get; } // Weighted by process type (system, user, game)
        
        public BenchmarkContext(
            string environmentHash,
            CpuMetrics cpu,
            MemoryMetrics memory,
            DiskMetrics disk,
            UserExperienceMetrics userExperience,
            int activeProcessCount,
            int weightedProcessScore)
        {
            ContextId = Guid.NewGuid().ToString("N");
            CapturedAt = DateTime.UtcNow;
            EnvironmentHash = environmentHash ?? throw new ArgumentNullException(nameof(environmentHash));
            Cpu = cpu ?? throw new ArgumentNullException(nameof(cpu));
            Memory = memory ?? throw new ArgumentNullException(nameof(memory));
            Disk = disk ?? throw new ArgumentNullException(nameof(disk));
            UserExperience = userExperience ?? throw new ArgumentNullException(nameof(userExperience));
            ActiveProcessCount = activeProcessCount;
            WeightedProcessScore = weightedProcessScore;
        }
    }

    /// <summary>
    /// CPU performance metrics
    /// </summary>
    public sealed class CpuMetrics
    {
        public double IdleStabilityPercent { get; }      // % time in true idle
        public double ContextSwitchesPerSec { get; }     // Context switches/sec
        public double InterruptRatePerSec { get; }       // Interrupts/sec
        public double AverageCpuUsagePercent { get; }    // Average CPU usage
        public double[] UsageDistribution { get; }       // Per-core usage distribution
        
        public CpuMetrics(
            double idleStability,
            double contextSwitches,
            double interruptRate,
            double avgUsage,
            double[] usageDistribution)
        {
            IdleStabilityPercent = Math.Clamp(idleStability, 0, 100);
            ContextSwitchesPerSec = Math.Max(0, contextSwitches);
            InterruptRatePerSec = Math.Max(0, interruptRate);
            AverageCpuUsagePercent = Math.Clamp(avgUsage, 0, 100);
            UsageDistribution = usageDistribution ?? Array.Empty<double>();
        }
    }

    /// <summary>
    /// Memory performance metrics
    /// </summary>
    public sealed class MemoryMetrics
    {
        public long AvailableMB { get; }                 // Available physical memory
        public double HardFaultsPerSec { get; }          // Hard page faults/sec
        public double CommitPressurePercent { get; }     // Commit charge pressure
        public long WorkingSetMB { get; }                // Total working set
        public double PageFileUsagePercent { get; }      // Page file usage
        
        public MemoryMetrics(
            long availableMB,
            double hardFaults,
            double commitPressure,
            long workingSetMB,
            double pageFileUsage)
        {
            AvailableMB = Math.Max(0, availableMB);
            HardFaultsPerSec = Math.Max(0, hardFaults);
            CommitPressurePercent = Math.Clamp(commitPressure, 0, 100);
            WorkingSetMB = Math.Max(0, workingSetMB);
            PageFileUsagePercent = Math.Clamp(pageFileUsage, 0, 100);
        }
    }

    /// <summary>
    /// Disk performance metrics
    /// </summary>
    public sealed class DiskMetrics
    {
        public double AvgReadLatencyMs { get; }          // Average read latency
        public double AvgWriteLatencyMs { get; }         // Average write latency
        public double QueueLength { get; }               // Disk queue length
        public double SmallIoPerformanceScore { get; }   // Small random IO score (0-100)
        public double DiskTimePercent { get; }           // % Disk Time
        
        public DiskMetrics(
            double avgReadLatency,
            double avgWriteLatency,
            double queueLength,
            double smallIoScore,
            double diskTime)
        {
            AvgReadLatencyMs = Math.Max(0, avgReadLatency);
            AvgWriteLatencyMs = Math.Max(0, avgWriteLatency);
            QueueLength = Math.Max(0, queueLength);
            SmallIoPerformanceScore = Math.Clamp(smallIoScore, 0, 100);
            DiskTimePercent = Math.Clamp(diskTime, 0, 100);
        }
    }

    /// <summary>
    /// User experience metrics (real-world impact)
    /// </summary>
    public sealed class UserExperienceMetrics
    {
        public double AppColdStartMs { get; }            // Cold start time sample
        public double AppWarmStartMs { get; }            // Warm start time sample
        public double SystemResponsivenessScore { get; } // Overall responsiveness (0-100)
        public double UiThreadLatencyMs { get; }         // UI thread response time
        
        public UserExperienceMetrics(
            double coldStart,
            double warmStart,
            double responsiveness,
            double uiLatency)
        {
            AppColdStartMs = Math.Max(0, coldStart);
            AppWarmStartMs = Math.Max(0, warmStart);
            SystemResponsivenessScore = Math.Clamp(responsiveness, 0, 100);
            UiThreadLatencyMs = Math.Max(0, uiLatency);
        }
    }

    /// <summary>
    /// Statistical analysis of benchmark comparison
    /// </summary>
    public sealed class BenchmarkResult
    {
        // Metadata
        public string ResultId { get; }
        public DateTime ExecutedAt { get; }
        public string OptimizationApplied { get; }
        
        // Snapshots
        public BenchmarkContext Before { get; }
        public BenchmarkContext After { get; }
        
        // Statistical Analysis
        public BenchmarkDelta Delta { get; }
        public ConfidenceLevel Confidence { get; }
        public BenchmarkVerdict Verdict { get; }
        
        // Execution Info
        public TimeSpan StabilizationDuration { get; }
        public TimeSpan TotalDuration { get; }
        public bool RebootRequired { get; }
        
        public BenchmarkResult(
            string optimizationApplied,
            BenchmarkContext before,
            BenchmarkContext after,
            BenchmarkDelta delta,
            ConfidenceLevel confidence,
            BenchmarkVerdict verdict,
            TimeSpan stabilizationDuration,
            TimeSpan totalDuration,
            bool rebootRequired = false)
        {
            ResultId = Guid.NewGuid().ToString("N");
            ExecutedAt = DateTime.UtcNow;
            OptimizationApplied = optimizationApplied ?? throw new ArgumentNullException(nameof(optimizationApplied));
            Before = before ?? throw new ArgumentNullException(nameof(before));
            After = after ?? throw new ArgumentNullException(nameof(after));
            Delta = delta ?? throw new ArgumentNullException(nameof(delta));
            Confidence = confidence;
            Verdict = verdict;
            StabilizationDuration = stabilizationDuration;
            TotalDuration = totalDuration;
            RebootRequired = rebootRequired;
        }
        
        public override string ToString() =>
            $"Benchmark[{ResultId}] {OptimizationApplied} | Verdict: {Verdict} | Confidence: {Confidence} | Gain: {Delta.OverallGainPercent:F2}%";
    }

    /// <summary>
    /// Delta between before and after metrics
    /// </summary>
    public sealed class BenchmarkDelta
    {
        // Overall Score
        public double OverallGainPercent { get; }        // Composite improvement score
        
        // CPU Deltas
        public double CpuIdleImprovement { get; }
        public double ContextSwitchReduction { get; }
        public double InterruptReduction { get; }
        
        // Memory Deltas
        public double MemoryAvailableIncrease { get; }
        public double HardFaultReduction { get; }
        
        // Disk Deltas
        public double DiskLatencyReduction { get; }
        public double IoPerformanceGain { get; }
        
        // User Experience Deltas
        public double AppStartSpeedup { get; }
        public double ResponsivenessGain { get; }
        
        // Statistical Metrics
        public double Variance { get; }
        public double NoiseLevel { get; }
        
        public BenchmarkDelta(
            double overallGain,
            double cpuIdle,
            double contextSwitch,
            double interrupt,
            double memoryAvailable,
            double hardFault,
            double diskLatency,
            double ioPerformance,
            double appStart,
            double responsiveness,
            double variance,
            double noise)
        {
            OverallGainPercent = overallGain;
            CpuIdleImprovement = cpuIdle;
            ContextSwitchReduction = contextSwitch;
            InterruptReduction = interrupt;
            MemoryAvailableIncrease = memoryAvailable;
            HardFaultReduction = hardFault;
            DiskLatencyReduction = diskLatency;
            IoPerformanceGain = ioPerformance;
            AppStartSpeedup = appStart;
            ResponsivenessGain = responsiveness;
            Variance = variance;
            NoiseLevel = noise;
        }
    }

    /// <summary>
    /// Confidence level in benchmark results
    /// </summary>
    public enum ConfidenceLevel
    {
        Low,        // High variance, unreliable
        Medium,     // Moderate confidence
        High,       // Strong statistical significance
        Certain     // Deterministic, repeatable
    }

    /// <summary>
    /// Final verdict on optimization effectiveness
    /// </summary>
    public enum BenchmarkVerdict
    {
        SignificantImprovement,     // > 5% gain, high confidence
        ModerateImprovement,        // 2-5% gain
        NoMeaningfulChange,         // < 2% change
        Inconclusive,               // High variance, within noise
        Regression,                 // Performance degraded
        AwaitingReboot              // Optimization applied but requires restart for full measurement
    }
}
