using System;
using System.Linq;
using VoltrisOptimizer.Services.Performance.Benchmark.Models;

namespace VoltrisOptimizer.Services.Performance.Benchmark
{
    /// <summary>
    /// Statistical analyzer for benchmark results.
    /// Implements anti-placebo rules and confidence scoring.
    /// Pure mathematical analysis - zero side effects.
    /// </summary>
    public sealed class BenchmarkStatisticalAnalyzer
    {
        private readonly ILoggingService _logger;
        
        // Anti-placebo thresholds
        private const double MEANINGFUL_CHANGE_THRESHOLD = 2.0; // 2% minimum
        private const double SIGNIFICANT_CHANGE_THRESHOLD = 5.0; // 5% significant
        private const double NOISE_THRESHOLD = 1.5; // 1.5% considered noise
        
        public BenchmarkStatisticalAnalyzer(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        /// <summary>
        /// Analyzes before/after snapshots and generates statistical verdict.
        /// </summary>
        public (BenchmarkDelta delta, ConfidenceLevel confidence, BenchmarkVerdict verdict) Analyze(
            BenchmarkContext before,
            BenchmarkContext after)
        {
            _logger.LogInfo("[BenchmarkAnalyzer] 🔬 Performing statistical analysis...");
            
            // Calculate individual metric deltas
            var cpuIdleGain = CalculatePercentChange(before.Cpu.IdleStabilityPercent, after.Cpu.IdleStabilityPercent);
            var contextSwitchReduction = CalculatePercentChange(before.Cpu.ContextSwitchesPerSec, after.Cpu.ContextSwitchesPerSec, inverse: true);
            var interruptReduction = CalculatePercentChange(before.Cpu.InterruptRatePerSec, after.Cpu.InterruptRatePerSec, inverse: true);
            
            var memoryGain = CalculatePercentChange(before.Memory.AvailableMB, after.Memory.AvailableMB);
            var hardFaultReduction = CalculatePercentChange(before.Memory.HardFaultsPerSec, after.Memory.HardFaultsPerSec, inverse: true);
            
            var diskLatencyReduction = CalculatePercentChange(
                (before.Disk.AvgReadLatencyMs + before.Disk.AvgWriteLatencyMs) / 2,
                (after.Disk.AvgReadLatencyMs + after.Disk.AvgWriteLatencyMs) / 2,
                inverse: true);
            var ioGain = CalculatePercentChange(before.Disk.SmallIoPerformanceScore, after.Disk.SmallIoPerformanceScore);
            
            var appStartGain = CalculatePercentChange(before.UserExperience.AppColdStartMs, after.UserExperience.AppColdStartMs, inverse: true);
            var responsivenessGain = CalculatePercentChange(before.UserExperience.SystemResponsivenessScore, after.UserExperience.SystemResponsivenessScore);
            
            // Calculate weighted overall gain
            var overallGain = CalculateWeightedGain(
                cpuIdleGain, contextSwitchReduction, interruptReduction,
                memoryGain, hardFaultReduction,
                diskLatencyReduction, ioGain,
                appStartGain, responsivenessGain);
            
            // Calculate variance and noise
            var variance = CalculateVariance(new[]
            {
                cpuIdleGain, contextSwitchReduction, interruptReduction,
                memoryGain, hardFaultReduction,
                diskLatencyReduction, ioGain,
                appStartGain, responsivenessGain
            });
            
            var noise = CalculateNoiseLevel(before, after);
            
            // Create delta object
            var delta = new BenchmarkDelta(
                overallGain,
                cpuIdleGain,
                contextSwitchReduction,
                interruptReduction,
                memoryGain,
                hardFaultReduction,
                diskLatencyReduction,
                ioGain,
                appStartGain,
                responsivenessGain,
                variance,
                noise
            );
            
            // Determine confidence level
            var confidence = DetermineConfidence(variance, noise);
            
            // Determine verdict (anti-placebo rules)
            var verdict = DetermineVerdict(overallGain, variance, noise);
            
            _logger.LogInfo($"[BenchmarkAnalyzer] 📊 Analysis complete: Gain={overallGain:F2}%, Confidence={confidence}, Verdict={verdict}");
            
            return (delta, confidence, verdict);
        }
        
        private double CalculatePercentChange(double before, double after, bool inverse = false)
        {
            if (Math.Abs(before) < 0.001) return 0; // Avoid division by zero
            
            var change = ((after - before) / before) * 100;
            return inverse ? -change : change; // Inverse for metrics where lower is better
        }
        
        private double CalculateWeightedGain(
            double cpuIdle, double contextSwitch, double interrupt,
            double memory, double hardFault,
            double diskLatency, double io,
            double appStart, double responsiveness)
        {
            // Weighted average: UX metrics have higher weight
            var weights = new[]
            {
                (cpuIdle, 1.0),
                (contextSwitch, 0.8),
                (interrupt, 0.8),
                (memory, 1.2),
                (hardFault, 1.0),
                (diskLatency, 1.5),
                (io, 1.5),
                (appStart, 2.0),      // Highest weight - user-facing
                (responsiveness, 2.0)  // Highest weight - user-facing
            };
            
            var totalWeight = weights.Sum(w => w.Item2);
            var weightedSum = weights.Sum(w => w.Item1 * w.Item2);
            
            return weightedSum / totalWeight;
        }
        
        private double CalculateVariance(double[] values)
        {
            if (values.Length == 0) return 0;
            
            var mean = values.Average();
            var sumSquaredDiff = values.Sum(v => Math.Pow(v - mean, 2));
            return Math.Sqrt(sumSquaredDiff / values.Length);
        }
        
        private double CalculateNoiseLevel(BenchmarkContext before, BenchmarkContext after)
        {
            // Noise is estimated from CPU usage distribution variance
            var beforeVariance = CalculateVariance(before.Cpu.UsageDistribution);
            var afterVariance = CalculateVariance(after.Cpu.UsageDistribution);
            
            return (beforeVariance + afterVariance) / 2;
        }
        
        private ConfidenceLevel DetermineConfidence(double variance, double noise)
        {
            // Low variance + low noise = high confidence
            if (variance < 2.0 && noise < 5.0)
                return ConfidenceLevel.High;
            
            if (variance < 5.0 && noise < 10.0)
                return ConfidenceLevel.Medium;
            
            return ConfidenceLevel.Low;
        }
        
        private BenchmarkVerdict DetermineVerdict(double overallGain, double variance, double noise)
        {
            // ANTI-PLACEBO RULES
            
            // Rule 1: If change is within noise, it's inconclusive
            if (Math.Abs(overallGain) < noise || Math.Abs(overallGain) < NOISE_THRESHOLD)
            {
                _logger.LogInfo($"[BenchmarkAnalyzer] Verdict: Inconclusive (within noise: {noise:F2}%)");
                return BenchmarkVerdict.Inconclusive;
            }
            
            // Rule 2: If performance degraded
            if (overallGain < -MEANINGFUL_CHANGE_THRESHOLD)
            {
                _logger.LogWarning($"[BenchmarkAnalyzer] Verdict: Regression (degraded by {Math.Abs(overallGain):F2}%)");
                return BenchmarkVerdict.Regression;
            }
            
            // Rule 3: If improvement < 2%, no meaningful change
            if (overallGain < MEANINGFUL_CHANGE_THRESHOLD)
            {
                _logger.LogInfo($"[BenchmarkAnalyzer] Verdict: No meaningful change ({overallGain:F2}% < {MEANINGFUL_CHANGE_THRESHOLD}%)");
                return BenchmarkVerdict.NoMeaningfulChange;
            }
            
            // Rule 4: If improvement >= 5%, significant
            if (overallGain >= SIGNIFICANT_CHANGE_THRESHOLD)
            {
                _logger.LogSuccess($"[BenchmarkAnalyzer] Verdict: Significant improvement ({overallGain:F2}%)");
                return BenchmarkVerdict.SignificantImprovement;
            }
            
            // Rule 5: 2-5% improvement = moderate
            _logger.LogInfo($"[BenchmarkAnalyzer] Verdict: Moderate improvement ({overallGain:F2}%)");
            return BenchmarkVerdict.ModerateImprovement;
        }
    }
}
