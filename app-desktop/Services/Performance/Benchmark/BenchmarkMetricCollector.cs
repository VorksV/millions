using VoltrisOptimizer.Utils;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using VoltrisOptimizer.Services.Performance.Benchmark.Models;

namespace VoltrisOptimizer.Services.Performance.Benchmark
{
    /// <summary>
    /// Passive metric collector for benchmark validation.
    /// ZERO side effects - purely observational.
    /// Uses Windows Performance Counters for scientific accuracy.
    /// </summary>
    public sealed class BenchmarkMetricCollector : IDisposable
    {
        private readonly ILoggingService _logger;
        
        // Performance Counters (lazy initialized)
        private SafePerformanceCounter? _cpuCounter;
        private SafePerformanceCounter? _contextSwitchCounter;
        private SafePerformanceCounter? _interruptCounter;
        private SafePerformanceCounter? _availableMemoryCounter;
        private SafePerformanceCounter? _pageFaultsCounter;
        private SafePerformanceCounter? _diskReadCounter;
        private SafePerformanceCounter? _diskWriteCounter;
        private SafePerformanceCounter? _diskQueueCounter;
        
        private bool _disposed;
        
        public BenchmarkMetricCollector(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        /// <summary>
        /// Captures a complete benchmark snapshot.
        /// Samples metrics over a short period for accuracy.
        /// </summary>
        public async Task<BenchmarkContext> CaptureSnapshotAsync(CancellationToken ct = default)
        {
            _logger.LogInfo("[BenchmarkCollector] 📊 Capturing performance snapshot...");
            
            try
            {
                // Initialize counters if needed
                EnsureCountersInitialized();
                
                // Sample metrics over 10 seconds for high statistical accuracy (SaaS Elite Level)
                var samples = 10;
                var cpuSamples = new double[samples];
                var contextSwitchSamples = new double[samples];
                var interruptSamples = new double[samples];
                
                for (int i = 0; i < samples; i++)
                {
                    cpuSamples[i] = GetCpuUsage();
                    contextSwitchSamples[i] = GetContextSwitches();
                    interruptSamples[i] = GetInterrupts();
                    
                    if (i < samples - 1)
                        await Task.Delay(1000, ct); // 1s interval between samples
                }
                
                // Capture CPU metrics
                var cpuMetrics = CaptureCpuMetrics(cpuSamples, contextSwitchSamples, interruptSamples);
                
                // Capture Memory metrics
                var memoryMetrics = CaptureMemoryMetrics();
                
                // Capture Disk metrics
                var diskMetrics = CaptureDiskMetrics();
                
                // Capture User Experience metrics
                var uxMetrics = await CaptureUserExperienceMetricsAsync(ct);
                
                // Get process info
                var (processCount, weightedScore) = GetProcessInfo();
                
                // Generate environment hash
                var envHash = GenerateEnvironmentHash();
                
                var context = new BenchmarkContext(
                    envHash,
                    cpuMetrics,
                    memoryMetrics,
                    diskMetrics,
                    uxMetrics,
                    processCount,
                    weightedScore
                );
                
                _logger.LogInfo($"[BenchmarkCollector] ✅ Snapshot captured: CPU={cpuMetrics.AverageCpuUsagePercent:F1}%, RAM={memoryMetrics.AvailableMB}MB");
                
                return context;
            }
            catch (Exception ex)
            {
                _logger.LogError("[BenchmarkCollector] Failed to capture snapshot", ex);
                throw;
            }
        }
        
        private void EnsureCountersInitialized()
        {
            if (_cpuCounter != null) return;
            
            try
            {
                _cpuCounter = new SafePerformanceCounter("Processor", "% Processor Time", "_Total");
                _contextSwitchCounter = new SafePerformanceCounter("System", "Context Switches/sec");
                _interruptCounter = new SafePerformanceCounter("Processor", "Interrupts/sec", "_Total");
                _availableMemoryCounter = new SafePerformanceCounter("Memory", "Available MBytes");
                _pageFaultsCounter = new SafePerformanceCounter("Memory", "Page Faults/sec");
                _diskReadCounter = new SafePerformanceCounter("PhysicalDisk", "Avg. Disk sec/Read", "_Total");
                _diskWriteCounter = new SafePerformanceCounter("PhysicalDisk", "Avg. Disk sec/Write", "_Total");
                _diskQueueCounter = new SafePerformanceCounter("PhysicalDisk", "Avg. Disk Queue Length", "_Total");
                
                // Prime the counters
                _cpuCounter.NextValue();
                _contextSwitchCounter.NextValue();
                _interruptCounter.NextValue();
                
                _logger.LogInfo("[BenchmarkCollector] Performance counters initialized");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[BenchmarkCollector] Failed to initialize some counters: {ex.Message}");
            }
        }
        
        private double GetCpuUsage()
        {
            try
            {
                return _cpuCounter?.NextValue() ?? 0;
            }
            catch
            {
                return 0;
            }
        }
        
        private double GetContextSwitches()
        {
            try
            {
                return _contextSwitchCounter?.NextValue() ?? 0;
            }
            catch
            {
                return 0;
            }
        }
        
        private double GetInterrupts()
        {
            try
            {
                return _interruptCounter?.NextValue() ?? 0;
            }
            catch
            {
                return 0;
            }
        }
        
        private CpuMetrics CaptureCpuMetrics(double[] cpuSamples, double[] contextSwitches, double[] interrupts)
        {
            var avgCpu = cpuSamples.Average();
            var idleStability = 100 - avgCpu; // Simplified: true idle would need more analysis
            var avgContextSwitches = contextSwitches.Average();
            var avgInterrupts = interrupts.Average();
            
            return new CpuMetrics(
                idleStability,
                avgContextSwitches,
                avgInterrupts,
                avgCpu,
                cpuSamples
            );
        }
        
        private MemoryMetrics CaptureMemoryMetrics()
        {
            try
            {
                var availableMB = (long)(_availableMemoryCounter?.NextValue() ?? 0);
                var hardFaults = _pageFaultsCounter?.NextValue() ?? 0;
                
                // Get total memory for pressure calculation
                var totalMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024);
                var commitPressure = totalMemory > 0 ? ((totalMemory - availableMB) / (double)totalMemory) * 100 : 0;
                
                // Get working set
                var currentProcess = Process.GetCurrentProcess();
                var workingSetMB = currentProcess.WorkingSet64 / (1024 * 1024);
                
                return new MemoryMetrics(
                    availableMB,
                    hardFaults,
                    commitPressure,
                    workingSetMB,
                    0 // Page file usage - would need additional counter
                );
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[BenchmarkCollector] Memory metrics error: {ex.Message}");
                return new MemoryMetrics(0, 0, 0, 0, 0);
            }
        }
        
        private DiskMetrics CaptureDiskMetrics()
        {
            try
            {
                var readLatency = (_diskReadCounter?.NextValue() ?? 0) * 1000; // Convert to ms
                var writeLatency = (_diskWriteCounter?.NextValue() ?? 0) * 1000;
                var queueLength = _diskQueueCounter?.NextValue() ?? 0;
                
                // Small IO score: inverse of latency (lower latency = higher score)
                var avgLatency = (readLatency + writeLatency) / 2;
                var ioScore = avgLatency > 0 ? Math.Min(100, 100 / avgLatency) : 100;
                
                return new DiskMetrics(
                    readLatency,
                    writeLatency,
                    queueLength,
                    ioScore,
                    0 // Disk time % - would need additional counter
                );
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[BenchmarkCollector] Disk metrics error: {ex.Message}");
                return new DiskMetrics(0, 0, 0, 100, 0);
            }
        }
        
        private async Task<UserExperienceMetrics> CaptureUserExperienceMetricsAsync(CancellationToken ct)
        {
            try
            {
                // SAE (System Activity Efficiency) Measure:
                // Measure time to perform a series of standard OS queries.
                // Professional grade: run 5 iterations and use the trimmed mean (remove outliers).
                var iterations = 5;
                var latencies = new List<double>();
                
                for (int i = 0; i < iterations; i++)
                {
                    var sw = Stopwatch.StartNew();
                    _ = Process.GetProcesses(); // System-level query
                    sw.Stop();
                    latencies.Add(sw.Elapsed.TotalMilliseconds);
                    await Task.Delay(50, ct); 
                }

                var systemQueryLatency = latencies.OrderBy(x => x).Skip(1).Take(3).Average();
                
                // Responsiveness score: Logarithmic scale for better human perception of speed
                // 1ms = 100 score, 100ms = ~20 score
                var responsivenessScore = Math.Max(0, Math.Min(100, 100 - (20 * Math.Log10(Math.Max(1, systemQueryLatency)))));
                
                // Proxy for cold/warm start impact based on current storage I/O and latency
                var coldStart = systemQueryLatency * 8.5; // Estimated from system query overhead
                var warmStart = systemQueryLatency * 2.1; 
                
                return new UserExperienceMetrics(
                    coldStart,
                    warmStart,
                    responsivenessScore,
                    systemQueryLatency
                );
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[BenchmarkCollector] UX metrics error: {ex.Message}");
                return new UserExperienceMetrics(0, 0, 100, 0);
            }
        }
        
        private (int count, int weightedScore) GetProcessInfo()
        {
            try
            {
                var processes = Process.GetProcesses();
                var count = processes.Length;
                
                // Weighted score: system processes = 1, user processes = 2, games = 3
                var weightedScore = processes.Sum(p =>
                {
                    try
                    {
                        var name = p.ProcessName.ToLowerInvariant();
                        if (name.Contains("system") || name.Contains("svchost"))
                            return 1;
                        if (name.Contains("game") || name.Contains("steam") || name.Contains("epic"))
                            return 3;
                        return 2;
                    }
                    catch
                    {
                        return 1;
                    }
                });
                
                return (count, weightedScore);
            }
            catch
            {
                return (0, 0);
            }
        }
        
        private string GenerateEnvironmentHash()
        {
            var cpuName = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "Unknown";
            var osVersion = Environment.OSVersion.VersionString;
            var ramSize = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            
            var combined = $"{cpuName}|{osVersion}|{ramSize}";
            return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(combined)).Substring(0, 16);
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            
            _cpuCounter?.Dispose();
            _contextSwitchCounter?.Dispose();
            _interruptCounter?.Dispose();
            _availableMemoryCounter?.Dispose();
            _pageFaultsCounter?.Dispose();
            _diskReadCounter?.Dispose();
            _diskWriteCounter?.Dispose();
            _diskQueueCounter?.Dispose();
            
            _disposed = true;
        }
    }
}

