using System;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.Performance.Benchmark.Models;

namespace VoltrisOptimizer.Services.Performance.Benchmark
{
    /// <summary>
    /// Orchestrator for the scientific benchmark pipeline.
    /// Coordinates: Baseline -> Optimization -> Stabilization -> Post-Measure -> Analysis.
    /// Ensures isolation and validity of the test.
    /// </summary>
    public sealed class BenchmarkEngine
    {
        private readonly BenchmarkMetricCollector _collector;
        private readonly BenchmarkStatisticalAnalyzer _analyzer;
        private readonly ILoggingService _logger;

        public BenchmarkEngine(
            BenchmarkMetricCollector collector,
            BenchmarkStatisticalAnalyzer analyzer,
            ILoggingService logger)
        {
            _collector = collector ?? throw new ArgumentNullException(nameof(collector));
            _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Executes a scientifically valid benchmark run for a specific optimization action.
        /// </summary>
        /// <param name="optimizationName">Name of the optimization being tested (for report).</param>
        /// <param name="optimizationAction">The action to execute (e.g., orchestrator call).</param>
        /// <param name="stabilizationWindow">Time to wait after optimization before measuring.</param>
        /// <param name="progressCallback">Optional callback for UI updates (0-100%).</param>
        /// <param name="cancellationToken">Token to cancel the benchmark.</param>
        /// <returns>A comprehensive statistical result.</returns>
        public async Task<BenchmarkResult> RunBenchmarkAsync(
            string optimizationName,
            Func<Task> optimizationAction,
            TimeSpan stabilizationWindow,
            Action<string, int>? progressCallback = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(optimizationName))
                throw new ArgumentException("Optimization name required", nameof(optimizationName));
            
            if (optimizationAction == null)
                throw new ArgumentNullException(nameof(optimizationAction));

            _logger.LogInfo($"[BenchmarkEngine] 🧪 Starting scientific benchmark for: {optimizationName}");
            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                // PHASE 1: Baseline Capture
                // ---------------------------------------------------------
                progressCallback?.Invoke("Capturing baseline metrics...", 10);
                var beforeContext = await _collector.CaptureSnapshotAsync(cancellationToken);
                
                // PHASE 2: Execution
                // ---------------------------------------------------------
                progressCallback?.Invoke("Executing optimization...", 30);
                await optimizationAction();

                // PHASE 3: Stabilization (or Reboot Detection)
                // ---------------------------------------------------------
                // Professional Check: Is a reboot required for these optimizations to take effect?
                if (RestartManagerService.Instance.HasPendingChanges)
                {
                    _logger.LogWarning("[BenchmarkEngine] ⚠️ Optimizations applied require a system reboot. Pausing benchmark for post-reboot validation.");
                    progressCallback?.Invoke("Reboot required to continue validation.", 100);
                    
                    sw.Stop();
                    return new BenchmarkResult(
                        optimizationName,
                        beforeContext,
                        beforeContext, // After is same as before for now
                        new BenchmarkDelta(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
                        ConfidenceLevel.Certain,
                        BenchmarkVerdict.AwaitingReboot,
                        stabilizationWindow,
                        sw.Elapsed,
                        rebootRequired: true
                    );
                }

                progressCallback?.Invoke($"Stabilizing system ({stabilizationWindow.TotalSeconds}s)...", 50);
                _logger.LogInfo($"[BenchmarkEngine] ⏳ Waiting {stabilizationWindow.TotalSeconds}s for system stabilization...");
                
                // We use Task.Delay but respect cancellation
                await Task.Delay(stabilizationWindow, cancellationToken);

                // PHASE 4: Post-Measurement Capture
                // ---------------------------------------------------------
                progressCallback?.Invoke("Capturing post-optimization metrics...", 80);
                var afterContext = await _collector.CaptureSnapshotAsync(cancellationToken);

                // PHASE 5: Statistical Analysis
                // ---------------------------------------------------------
                progressCallback?.Invoke("Analyzing results...", 90);
                var (delta, confidence, verdict) = _analyzer.Analyze(beforeContext, afterContext);

                sw.Stop();
                progressCallback?.Invoke("Benchmark complete.", 100);

                var result = new BenchmarkResult(
                    optimizationName,
                    beforeContext,
                    afterContext,
                    delta,
                    confidence,
                    verdict,
                    stabilizationWindow,
                    sw.Elapsed
                );

                LogResultSummary(result);
                return result;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("[BenchmarkEngine] Benchmark cancelled by user.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[BenchmarkEngine] Benchmark failed: {ex.Message}", ex);
                throw;
            }
        }

        private void LogResultSummary(BenchmarkResult result)
        {
            var icon = result.Verdict switch
            {
                BenchmarkVerdict.SignificantImprovement => "🚀",
                BenchmarkVerdict.ModerateImprovement => "✅",
                BenchmarkVerdict.Regression => "⚠️",
                _ => "ℹ️"
            };

            _logger.LogInfo($"[BenchmarkEngine] {icon} Benchmark Result: {result.Verdict}");
            _logger.LogInfo($"   • Overall Gain: {result.Delta.OverallGainPercent:F2}% (Confidence: {result.Confidence})");
            _logger.LogInfo($"   • CPU Idle: {result.Before.Cpu.IdleStabilityPercent:F1}% -> {result.After.Cpu.IdleStabilityPercent:F1}%");
            _logger.LogInfo($"   • RAM Avail: {result.Before.Memory.AvailableMB}MB -> {result.After.Memory.AvailableMB}MB");
            _logger.LogInfo($"   • UX Latency: {result.Before.UserExperience.SystemResponsivenessScore:F0} -> {result.After.UserExperience.SystemResponsivenessScore:F0}");
        }
    }
}
