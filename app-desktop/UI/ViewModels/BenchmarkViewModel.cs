using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Collections.ObjectModel;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Performance;
using VoltrisOptimizer.Services.Performance.Benchmark;
using VoltrisOptimizer.Services.Performance.Benchmark.Models;
using VoltrisOptimizer.Services.Performance.Orchestration;

namespace VoltrisOptimizer.UI.ViewModels
{
    public class BenchmarkViewModel : ViewModelBase
    {
        private readonly BenchmarkEngine _benchmarkEngine;
        private readonly BenchmarkMetricCollector _metricCollector;
        private readonly BenchmarkStatisticalAnalyzer _analyzer;
        private readonly BenchmarkPersistenceService _persistence;
        private readonly IPerformanceOrchestrator _orchestrator;
        private readonly IPerformanceOptimizationService _performanceService;
        private readonly ILoggingService _logger;

        private bool _isRunning;
        private string _statusMessage;
        private double _progress;
        private BenchmarkResult? _lastResult;
        private bool _hasPendingPostReboot;

        // Commands
        public ICommand RunStandardBenchmarkCommand { get; }
        public ICommand ResumeBenchmarkCommand { get; }
        
        // Results Display
        private string _resultVerdict = "";
        private string _resultGain = "";
        private string _resultConfidence = "";
        private string _resultCpuIdleDelta = "";
        private string _resultRamDelta = "";
        private string _resultDiskDelta = "";
        private string _resultUxDelta = "";

        public string ResultVerdict
        {
            get => _resultVerdict;
            private set { SetProperty(ref _resultVerdict, value); }
        }
        public string ResultGain
        {
            get => _resultGain;
            private set { SetProperty(ref _resultGain, value); }
        }
        public string ResultConfidence
        {
            get => _resultConfidence;
            private set { SetProperty(ref _resultConfidence, value); }
        }
        public string ResultCpuIdleDelta
        {
            get => _resultCpuIdleDelta;
            private set { SetProperty(ref _resultCpuIdleDelta, value); }
        }
        public string ResultRamDelta
        {
            get => _resultRamDelta;
            private set { SetProperty(ref _resultRamDelta, value); }
        }
        public string ResultDiskDelta
        {
            get => _resultDiskDelta;
            private set { SetProperty(ref _resultDiskDelta, value); }
        }
        public string ResultUxDelta
        {
            get => _resultUxDelta;
            private set { SetProperty(ref _resultUxDelta, value); }
        }

        public bool IsRunning
        {
            get => _isRunning;
            set { SetProperty(ref _isRunning, value); }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { SetProperty(ref _statusMessage, value); }
        }

        public double Progress
        {
            get => _progress;
            set { SetProperty(ref _progress, value); }
        }
        
        private bool _hasResult;
        public bool HasResult
        {
            get => _hasResult;
            private set { SetProperty(ref _hasResult, value); }
        }

        public bool HasPendingPostReboot
        {
            get => _hasPendingPostReboot;
            set { SetProperty(ref _hasPendingPostReboot, value); }
        }

        public BenchmarkViewModel(
            BenchmarkEngine benchmarkEngine,
            BenchmarkMetricCollector metricCollector,
            BenchmarkStatisticalAnalyzer analyzer,
            BenchmarkPersistenceService persistence,
            IPerformanceOrchestrator orchestrator,
            IPerformanceOptimizationService performanceService,
            ILoggingService logger)
        {
            _benchmarkEngine = benchmarkEngine ?? throw new ArgumentNullException(nameof(benchmarkEngine));
            _metricCollector = metricCollector ?? throw new ArgumentNullException(nameof(metricCollector));
            _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
            _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
            _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
            _performanceService = performanceService ?? throw new ArgumentNullException(nameof(performanceService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _statusMessage = LocalizationService.Instance.GetString("ReadyToStartBenchmark");
            
            RunStandardBenchmarkCommand = new RelayCommand(RunStandardBenchmarkAsync, () => !IsRunning);
            ResumeBenchmarkCommand = new RelayCommand(ResumeBenchmarkAsync, () => !IsRunning && HasPendingPostReboot);

            CheckForPendingBenchmark();
        }

        private async void CheckForPendingBenchmark()
        {
            try
            {
            var pending = await _persistence.LoadPendingBenchmarkAsync();
            HasPendingPostReboot = pending != null;
            if (HasPendingPostReboot)
            {
                StatusMessage = LocalizationService.Instance.GetString("PendingPostRebootValidation");
            }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[BenchmarkViewModel] Erro em CheckForPendingBenchmark: {ex.Message}", ex);
            }
        }

        private async Task RunStandardBenchmarkAsync()
        {
            if (IsRunning) return;

            IsRunning = true;
            Progress = 0;
            _lastResult = null;
            HasResult = false;

            GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("RunningScientificBenchmark"), true);
            
            try
            {
                StatusMessage = LocalizationService.Instance.GetString("StartingScientificValidation");
                GlobalProgressService.Instance.UpdateProgress(5, LocalizationService.Instance.GetString("StartingBenchmark"));
                
                Func<Task> optimizationAction = async () =>
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        StatusMessage = LocalizationService.Instance.GetString("ApplyingRecommendedOptimizations");
                    });
                    await _performanceService.ApplyRecommendedOptimizationsAsync();
                };

                var result = await _benchmarkEngine.RunBenchmarkAsync(
                    LocalizationService.Instance.GetString("CompleteSystemOptimization"),
                    optimizationAction,
                    TimeSpan.FromSeconds(30),
                    (stage, pct) => 
                    {
                        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                        {
                            StatusMessage = stage;
                            Progress = pct;
                        });
                    }
                );

                GlobalProgressService.Instance.UpdateProgress(80, LocalizationService.Instance.GetString("ProcessingResults"));

                if (result.Verdict == BenchmarkVerdict.AwaitingReboot)
                {
                    await _persistence.SavePendingBenchmarkAsync(result.OptimizationApplied, result.Before);
                    HasPendingPostReboot = true;
                    StatusMessage = LocalizationService.Instance.GetString("OptimizationsAppliedReboot");
                    
                    _ = RestartManagerService.Instance.CheckAndPromptRestartAsync(LocalizationService.Instance.GetString("PerformanceOptimizations"));
                    GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("OptimizationsAppliedRebootShort"));
                    GlobalNotificationService.ShowInfo(LocalizationService.Instance.GetString("Benchmark"), LocalizationService.Instance.GetString("OptimizationsAppliedReboot"));
                }
                else
                {
                    _lastResult = result;
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        DisplayResults(result);
                        HasResult = true;
                    });
                    StatusMessage = LocalizationService.Instance.GetString("ValidationCompleted");
                    GlobalProgressService.Instance.UpdateProgress(100, LocalizationService.Instance.GetString("BenchmarkCompleted"));
                    GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("BenchmarkCompletedGain"), result.Delta.OverallGainPercent));
                    GlobalNotificationService.ShowSuccess(LocalizationService.Instance.GetString("Benchmark"), string.Format(LocalizationService.Instance.GetString("ScientificValidationGain"), result.Delta.OverallGainPercent));
                }
            }
            catch (Exception ex)
            {
                StatusMessage = string.Format(LocalizationService.Instance.GetString("BenchmarkError"), ex.Message);
                _logger.LogError("Benchmark Error", ex);
                HasResult = false;
                GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("BenchmarkError"), ex.Message));
                GlobalNotificationService.ShowError(LocalizationService.Instance.GetString("Benchmark"), string.Format(LocalizationService.Instance.GetString("BenchmarkErrorMessage"), ex.Message));
            }
            finally
            {
                IsRunning = false;
            }
        }

        private async Task ResumeBenchmarkAsync()
        {
            if (IsRunning) return;

            var pending = await _persistence.LoadPendingBenchmarkAsync();
            if (pending == null) 
            {
                HasPendingPostReboot = false;
                return;
            }

            IsRunning = true;
            Progress = 0;

            GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("ResumingPostRebootBenchmark"), true);
            
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                StatusMessage = LocalizationService.Instance.GetString("CapturingPostRebootMetrics");
                Progress = 20;
                GlobalProgressService.Instance.UpdateProgress(20, LocalizationService.Instance.GetString("CapturingPostRebootMetrics"));

                await Task.Delay(5000); 
                
                var afterContext = await _metricCollector.CaptureSnapshotAsync();
                GlobalProgressService.Instance.UpdateProgress(60, LocalizationService.Instance.GetString("MetricsCapturedAnalyzing"));
                Progress = 80;

                StatusMessage = LocalizationService.Instance.GetString("AnalyzingStatisticalResults");
                var (delta, confidence, verdict) = _analyzer.Analyze(pending.BeforeContext, afterContext);
                
                sw.Stop();
                _lastResult = new BenchmarkResult(
                    pending.OptimizationName,
                    pending.BeforeContext,
                    afterContext,
                    delta,
                    confidence,
                    verdict,
                    TimeSpan.Zero,
                    sw.Elapsed
                );

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    DisplayResults(_lastResult);
                    HasResult = true;
                });
                
                _persistence.ClearPendingBenchmark();
                HasPendingPostReboot = false;
                StatusMessage = LocalizationService.Instance.GetString("PostRebootValidationCompleted");
                GlobalProgressService.Instance.UpdateProgress(100, LocalizationService.Instance.GetString("ValidationCompleted"));
                GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("PostRebootBenchmarkCompleted"));
                GlobalNotificationService.ShowSuccess(LocalizationService.Instance.GetString("Benchmark"), LocalizationService.Instance.GetString("PostRebootValidationSuccess"));
            }
            catch (Exception ex)
            {
                StatusMessage = string.Format(LocalizationService.Instance.GetString("ResumeBenchmarkError"), ex.Message);
                _logger.LogError("Resume Benchmark Error", ex);
                GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("ResumeBenchmarkError"), ex.Message));
            }
            finally
            {
                IsRunning = false;
            }
        }

        private void DisplayResults(BenchmarkResult result)
        {
            ResultVerdict = result.Verdict.ToString();
            ResultGain = $"{result.Delta.OverallGainPercent:F2}%";
            ResultConfidence = result.Confidence.ToString();
            
            ResultCpuIdleDelta = FormatDelta(result.Delta.CpuIdleImprovement);
            ResultRamDelta = $"{result.After.Memory.AvailableMB - result.Before.Memory.AvailableMB:+#;-#;0} MB";
            ResultDiskDelta = FormatDelta(result.Delta.DiskLatencyReduction, inverse: true);
            ResultUxDelta = FormatDelta(result.Delta.ResponsivenessGain);
        }

        private string FormatDelta(double value, bool inverse = false)
        {
            if (inverse) value = -value;
            return $"{value:+#.00;-#.00;0.00}%";
        }
    }
}
