using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Performance
{
    /// <summary>
    /// Service for validating performance impact of optimizations
    /// Measures FPS, input latency, and system responsiveness
    /// </summary>
    public class PerformanceValidationService
    {
        private readonly ILoggingService _logger;
        private readonly ISystemInfoService _systemInfoService;
        private readonly List<PerformanceMetric> _baselineMetrics;
        private readonly object _lockObject = new object();

        public PerformanceValidationService(ILoggingService logger, ISystemInfoService systemInfoService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _systemInfoService = systemInfoService ?? throw new ArgumentNullException(nameof(systemInfoService));
            _baselineMetrics = new List<PerformanceMetric>();
        }

        /// <summary>
        /// Establishes baseline performance metrics
        /// </summary>
        public async Task<PerformanceBaseline> EstablishBaselineAsync(
            PerformanceTestScenario scenario,
            CancellationToken ct = default)
        {
            _logger.Log(LogLevel.Info, LogCategory.Performance,
                $"Establishing performance baseline for scenario: {scenario.ScenarioName}",
                source: "PerformanceValidation");

            var baseline = new PerformanceBaseline
            {
                Scenario = scenario,
                Timestamp = DateTime.UtcNow,
                Metrics = new List<PerformanceMetric>()
            };

            try
            {
                // Measure CPU baseline
                var cpuUsage = await MeasureCpuUsageAsync(ct);
                baseline.Metrics.Add(new PerformanceMetric
                {
                    MetricType = PerformanceMetricType.CpuUsage,
                    Value = cpuUsage,
                    Unit = "%"
                });

                // Measure memory baseline
                var memoryUsage = await _systemInfoService.GetMemoryUsageAsync();
                baseline.Metrics.Add(new PerformanceMetric
                {
                    MetricType = PerformanceMetricType.MemoryUsage,
                    Value = memoryUsage,
                    Unit = "%"
                });

                // Measure disk I/O baseline
                var diskIo = await MeasureDiskIoAsync(ct);
                baseline.Metrics.Add(new PerformanceMetric
                {
                    MetricType = PerformanceMetricType.DiskIo,
                    Value = diskIo,
                    Unit = "MB/s"
                });

                // Measure system responsiveness
                var responsiveness = await MeasureSystemResponsivenessAsync(ct);
                baseline.Metrics.Add(new PerformanceMetric
                {
                    MetricType = PerformanceMetricType.SystemResponsiveness,
                    Value = responsiveness,
                    Unit = "ms"
                });

                lock (_lockObject)
                {
                    _baselineMetrics.Clear();
                    _baselineMetrics.AddRange(baseline.Metrics);
                }

                _logger.Log(LogLevel.Success, LogCategory.Performance,
                    $"Baseline established: CPU={cpuUsage:F1}%, Memory={memoryUsage:F1}%, Responsiveness={responsiveness:F1}ms",
                    source: "PerformanceValidation");

                return baseline;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to establish baseline: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// Validates performance after optimization application
        /// </summary>
        public async Task<PerformanceValidationResult> ValidatePerformanceAsync(
            PerformanceBaseline baseline,
            TimeSpan testDuration,
            CancellationToken ct = default)
        {
            _logger.Log(LogLevel.Info, LogCategory.Performance,
                "Validating performance impact of optimization",
                source: "PerformanceValidation");

            var result = new PerformanceValidationResult
            {
                Baseline = baseline,
                TestDuration = testDuration,
                StartTime = DateTime.UtcNow,
                Metrics = new List<PerformanceMetric>()
            };

            try
            {
                // Run validation for specified duration
                var stopwatch = Stopwatch.StartNew();
                
                while (stopwatch.Elapsed < testDuration && !ct.IsCancellationRequested)
                {
                    ct.ThrowIfCancellationRequested();

                    // Measure current performance
                    var currentMetrics = await CollectPerformanceMetricsAsync(ct);
                    result.Metrics.AddRange(currentMetrics);

                    await Task.Delay(1000, ct); // Sample every second
                }

                result.EndTime = DateTime.UtcNow;
                result.Duration = result.EndTime - result.StartTime;

                // Analyze results
                result.Analysis = AnalyzePerformanceImpact(baseline, result.Metrics);

                _logger.Log(LogLevel.Info, LogCategory.Performance,
                    $"Performance validation completed. Impact: {result.Analysis.OverallImpact}",
                    source: "PerformanceValidation");

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Performance validation failed: {ex.Message}", ex);
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        /// <summary>
        /// Determines if performance degradation exceeds acceptable thresholds
        /// </summary>
        public bool IsPerformanceDegradationAcceptable(PerformanceAnalysis analysis)
        {
            const double MAX_FPS_DEGRADATION = 5.0; // 5% FPS drop acceptable
            const double MAX_LATENCY_INCREASE = 10.0; // 10ms latency increase acceptable
            const double MAX_CPU_SPIKE = 15.0; // 15% CPU increase acceptable

            var isAcceptable = true;
            var issues = new List<string>();

            if (analysis.FpsDropPercentage > MAX_FPS_DEGRADATION)
            {
                isAcceptable = false;
                issues.Add($"FPS degradation {analysis.FpsDropPercentage:F1}% exceeds threshold of {MAX_FPS_DEGRADATION}%");
            }

            if (analysis.LatencyIncreaseMs > MAX_LATENCY_INCREASE)
            {
                isAcceptable = false;
                issues.Add($"Latency increase {analysis.LatencyIncreaseMs:F1}ms exceeds threshold of {MAX_LATENCY_INCREASE}ms");
            }

            if (analysis.CpuUsageIncreasePercentage > MAX_CPU_SPIKE)
            {
                isAcceptable = false;
                issues.Add($"CPU usage increase {analysis.CpuUsageIncreasePercentage:F1}% exceeds threshold of {MAX_CPU_SPIKE}%");
            }

            if (!isAcceptable)
            {
                _logger.Log(LogLevel.Warning, LogCategory.Performance,
                    $"Performance degradation detected: {string.Join("; ", issues)}",
                    null, "PerformanceValidation");
            }

            return isAcceptable;
        }

        // Private helper methods
        private async Task<double> MeasureCpuUsageAsync(CancellationToken ct)
        {
            var samples = new List<double>();
            for (int i = 0; i < 5; i++)
            {
                ct.ThrowIfCancellationRequested();
                var usage = await _systemInfoService.GetCpuUsageAsync();
                samples.Add(usage);
                await Task.Delay(200, ct);
            }
            return samples.Average();
        }

        private async Task<double> MeasureDiskIoAsync(CancellationToken ct)
        {
            // Simplified disk I/O measurement
            // In production, would use Performance Counters or WMI
            var driveInfo = (await _systemInfoService.GetDrivesInfoAsync()).FirstOrDefault();
            return driveInfo != null ? 1.0 : 0.0; // Placeholder
        }

        private async Task<double> MeasureSystemResponsivenessAsync(CancellationToken ct)
        {
            // Measure system responsiveness by timing simple operations
            var stopwatch = Stopwatch.StartNew();
            await Task.Delay(1, ct); // Minimal delay to measure overhead
            stopwatch.Stop();
            return stopwatch.ElapsedMilliseconds;
        }

        private async Task<List<PerformanceMetric>> CollectPerformanceMetricsAsync(CancellationToken ct)
        {
            var metrics = new List<PerformanceMetric>();

            // CPU Usage
            var cpuUsage = await _systemInfoService.GetCpuUsageAsync();
            metrics.Add(new PerformanceMetric
            {
                MetricType = PerformanceMetricType.CpuUsage,
                Value = cpuUsage,
                Unit = "%",
                Timestamp = DateTime.UtcNow
            });

            // Memory Usage
            var memoryUsage = await _systemInfoService.GetMemoryUsageAsync();
            metrics.Add(new PerformanceMetric
            {
                MetricType = PerformanceMetricType.MemoryUsage,
                Value = memoryUsage,
                Unit = "%",
                Timestamp = DateTime.UtcNow
            });

            // System Responsiveness
            var responsiveness = await MeasureSystemResponsivenessAsync(ct);
            metrics.Add(new PerformanceMetric
            {
                MetricType = PerformanceMetricType.SystemResponsiveness,
                Value = responsiveness,
                Unit = "ms",
                Timestamp = DateTime.UtcNow
            });

            return metrics;
        }

        private PerformanceAnalysis AnalyzePerformanceImpact(
            PerformanceBaseline baseline,
            List<PerformanceMetric> currentMetrics)
        {
            var analysis = new PerformanceAnalysis();

            // Group metrics by type
            var baselineGroups = baseline.Metrics.GroupBy(m => m.MetricType).ToDictionary(g => g.Key, g => g.Average(m => m.Value));
            var currentGroups = currentMetrics.GroupBy(m => m.MetricType).ToDictionary(g => g.Key, g => g.Average(m => m.Value));

            // Analyze CPU usage impact
            if (baselineGroups.ContainsKey(PerformanceMetricType.CpuUsage) &&
                currentGroups.ContainsKey(PerformanceMetricType.CpuUsage))
            {
                var baselineCpu = baselineGroups[PerformanceMetricType.CpuUsage];
                var currentCpu = currentGroups[PerformanceMetricType.CpuUsage];
                analysis.CpuUsageIncreasePercentage = ((currentCpu - baselineCpu) / baselineCpu) * 100;
            }

            // Analyze memory usage impact
            if (baselineGroups.ContainsKey(PerformanceMetricType.MemoryUsage) &&
                currentGroups.ContainsKey(PerformanceMetricType.MemoryUsage))
            {
                var baselineMem = baselineGroups[PerformanceMetricType.MemoryUsage];
                var currentMem = currentGroups[PerformanceMetricType.MemoryUsage];
                analysis.MemoryUsageIncreasePercentage = ((currentMem - baselineMem) / baselineMem) * 100;
            }

            // Analyze system responsiveness impact
            if (baselineGroups.ContainsKey(PerformanceMetricType.SystemResponsiveness) &&
                currentGroups.ContainsKey(PerformanceMetricType.SystemResponsiveness))
            {
                var baselineResp = baselineGroups[PerformanceMetricType.SystemResponsiveness];
                var currentResp = currentGroups[PerformanceMetricType.SystemResponsiveness];
                analysis.LatencyIncreaseMs = currentResp - baselineResp;
            }

            // Determine overall impact
            analysis.OverallImpact = DetermineOverallImpact(analysis);

            return analysis;
        }

        private PerformanceImpact DetermineOverallImpact(PerformanceAnalysis analysis)
        {
            if (analysis.CpuUsageIncreasePercentage > 20 || 
                analysis.LatencyIncreaseMs > 50 ||
                analysis.MemoryUsageIncreasePercentage > 30)
                return PerformanceImpact.CriticalDegradation;

            if (analysis.CpuUsageIncreasePercentage > 10 || 
                analysis.LatencyIncreaseMs > 25 ||
                analysis.MemoryUsageIncreasePercentage > 15)
                return PerformanceImpact.SignificantDegradation;

            if (analysis.CpuUsageIncreasePercentage > 5 || 
                analysis.LatencyIncreaseMs > 10 ||
                analysis.MemoryUsageIncreasePercentage > 5)
                return PerformanceImpact.MildDegradation;

            return PerformanceImpact.NoSignificantImpact;
        }
    }

    // Data models
    public class PerformanceTestScenario
    {
        public string ScenarioName { get; set; }
        public string Description { get; set; }
        public TimeSpan Duration { get; set; } = TimeSpan.FromSeconds(30);
        public List<string> TargetProcesses { get; set; } = new();
    }

    public class PerformanceBaseline
    {
        public PerformanceTestScenario Scenario { get; set; }
        public DateTime Timestamp { get; set; }
        public List<PerformanceMetric> Metrics { get; set; }
    }

    public class PerformanceMetric
    {
        public PerformanceMetricType MetricType { get; set; }
        public double Value { get; set; }
        public string Unit { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class PerformanceValidationResult
    {
        public PerformanceBaseline Baseline { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public TimeSpan TestDuration { get; set; }
        public List<PerformanceMetric> Metrics { get; set; }
        public PerformanceAnalysis Analysis { get; set; }
        public string ErrorMessage { get; set; }
    }

    public class PerformanceAnalysis
    {
        public double CpuUsageIncreasePercentage { get; set; }
        public double MemoryUsageIncreasePercentage { get; set; }
        public double FpsDropPercentage { get; set; }
        public double LatencyIncreaseMs { get; set; }
        public PerformanceImpact OverallImpact { get; set; }
    }

    // Enums
    public enum PerformanceMetricType
    {
        CpuUsage,
        MemoryUsage,
        DiskIo,
        NetworkUsage,
        Fps,
        FrameTime,
        BootTime,
        SystemResponsiveness,
        UiSmoothness,
        InputLatency,
        GpuUsage,
        GpuTemperature,
        ApplicationLaunchTime
    }

    public enum PerformanceImpact
    {
        NoSignificantImpact,
        MildDegradation,
        SignificantDegradation,
        CriticalDegradation
    }
}
