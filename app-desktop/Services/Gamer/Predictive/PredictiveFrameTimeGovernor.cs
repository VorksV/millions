using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core;
using VoltrisOptimizer.Services.Monitoring.Interfaces;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer.Predictive
{
    public sealed class PredictiveFrameTimeGovernor : IDisposable
    {
        private readonly ILoggingService _logger;
        private readonly IEtwFrameTimeMonitor _fpsMonitor;
        private Timer? _analysisTimer;

        private const int MaxSamples = 60;
        private const double StutterDerivativeThreshold = 1.8;
        private const double CriticalDerivativeThreshold = 3.0;
        private const int MinSamplesForTrend = 8;
        private const int AnalysisIntervalMs = 100;

        private readonly object _sampleLock = new();
        private readonly LinkedList<double> _frameTimes = new();
        private readonly LinkedList<double> _fpsValues = new();

        private int _activePid;
        private string _activeGameName = string.Empty;
        private bool _isActive;

        public event EventHandler<PreStutterEventArgs>? PreStutterDetected;

        public PredictiveFrameTimeGovernor(ILoggingService logger, IEtwFrameTimeMonitor fpsMonitor)
        {
            _logger.LogEntry(nameof(PredictiveFrameTimeGovernor));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _fpsMonitor = fpsMonitor ?? throw new ArgumentNullException(nameof(fpsMonitor));
            _logger.LogExit(nameof(PredictiveFrameTimeGovernor));
        }

        public void Start()
        {
            _logger.LogEntry(nameof(Start));
            if (_isActive) { _logger.LogExit(nameof(Start)); return; }
            _isActive = true;
            _fpsMonitor.MetricsUpdated += OnMetricsUpdated;
            _analysisTimer = new Timer(OnAnalysisTick, null, AnalysisIntervalMs, AnalysisIntervalMs);
            _logger.LogInfo("[PredictiveGovernor] Iniciado. Analise preditiva de stutter a cada 100ms.");
            _logger.LogExit(nameof(Start));
        }

        public void Stop()
        {
            _logger.LogEntry(nameof(Stop));
            if (!_isActive) { _logger.LogExit(nameof(Stop)); return; }
            _isActive = false;
            _fpsMonitor.MetricsUpdated -= OnMetricsUpdated;
            _analysisTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _analysisTimer?.Dispose();
            _analysisTimer = null;
            ClearSamples();
            _logger.LogInfo("[PredictiveGovernor] Parado.");
            _logger.LogExit(nameof(Stop));
        }

        public void ClearSamples()
        {
            _logger.LogEntry(nameof(ClearSamples));
            lock (_sampleLock)
            {
                _frameTimes.Clear();
                _fpsValues.Clear();
                _activePid = 0;
                _activeGameName = string.Empty;
            }
            _logger.LogExit(nameof(ClearSamples));
        }

        private void OnMetricsUpdated(object? sender, FrameMetrics metrics)
        {
            _logger.LogEntry(nameof(OnMetricsUpdated));
            if (!_isActive || metrics.CurrentFps <= 0) { _logger.LogExit(nameof(OnMetricsUpdated)); return; }

            lock (_sampleLock)
            {
                _activePid = metrics.DetectedGameProcessId;
                _activeGameName = metrics.DetectedGameName;
                _fpsValues.AddLast(metrics.CurrentFps);
                _frameTimes.AddLast(metrics.AverageFrametimeMs);

                while (_frameTimes.Count > MaxSamples)
                {
                    _frameTimes.RemoveFirst();
                    _fpsValues.RemoveFirst();
                }
            }
            _logger.LogExit(nameof(OnMetricsUpdated));
        }

        private void OnAnalysisTick(object? state)
        {
            _logger.LogEntry(nameof(OnAnalysisTick));
            if (!_isActive || _activePid == 0) { _logger.LogExit(nameof(OnAnalysisTick)); return; }

            double[] ftSnapshot;
            double[] fpsSnapshot;
            lock (_sampleLock)
            {
                if (_frameTimes.Count < MinSamplesForTrend) return;
                ftSnapshot = _frameTimes.ToArray();
                fpsSnapshot = _fpsValues.ToArray();
            }

            double avgFt = ftSnapshot.Average();
            double currentFt = ftSnapshot[^1];
            double currentFps = fpsSnapshot[^1];

            double shortTermSlope = CalculateSlope(ftSnapshot, Math.Max(0, ftSnapshot.Length - 5), ftSnapshot.Length);
            double midTermSlope = CalculateSlope(ftSnapshot, Math.Max(0, ftSnapshot.Length - 15), ftSnapshot.Length);

            if (shortTermSlope >= CriticalDerivativeThreshold)
            {
                _logger.LogDebug($"[PredictiveGovernor] CRITICO: stutter iminente! shortTermSlope={shortTermSlope:F2} currentFt={currentFt:F2}ms avgFt={avgFt:F2}ms fps={currentFps:F0}");
                FirePreStutter(PreStutterSeverity.Critical, currentFt, avgFt, currentFps, shortTermSlope);
                _logger.LogExit(nameof(OnAnalysisTick));
                return;
            }

            if (shortTermSlope >= StutterDerivativeThreshold && midTermSlope >= 1.2)
            {
                _logger.LogDebug($"[PredictiveGovernor] ALERTA: tendencia de stutter. shortTermSlope={shortTermSlope:F2} midTermSlope={midTermSlope:F2} currentFt={currentFt:F2}ms");
                FirePreStutter(PreStutterSeverity.Warning, currentFt, avgFt, currentFps, shortTermSlope);
            }
            _logger.LogExit(nameof(OnAnalysisTick));
        }

        private static double CalculateSlope(double[] values, int start, int end)
        {
            int count = end - start;
            if (count < 3) return 0;

            double sumX = 0, sumY = 0, sumXy = 0, sumXx = 0;
            for (int i = start; i < end; i++)
            {
                double x = i - start;
                double y = values[i];
                sumX += x;
                sumY += y;
                sumXy += x * y;
                sumXx += x * x;
            }

            double n = count;
            double slope = (n * sumXy - sumX * sumY) / (n * sumXx - sumX * sumX);
            return slope;
        }

        private void FirePreStutter(PreStutterSeverity severity, double currentFrameTime, double averageFrameTime, double currentFps, double derivative)
        {
            _logger.LogEntry(nameof(FirePreStutter));
            PreStutterDetected?.Invoke(this, new PreStutterEventArgs
            {
                Severity = severity,
                CurrentFrameTimeMs = currentFrameTime,
                AverageFrameTimeMs = averageFrameTime,
                CurrentFps = currentFps,
                FrameTimeDerivative = derivative,
                ProcessId = _activePid,
                ProcessName = _activeGameName
            });
            _logger.LogExit(nameof(FirePreStutter));
        }

        public void Dispose()
        {
            _logger.LogEntry(nameof(Dispose));
            Stop();
            _logger.LogExit(nameof(Dispose));
        }
    }

    public enum PreStutterSeverity { Warning, Critical }

    public class PreStutterEventArgs : EventArgs
    {
        public PreStutterSeverity Severity { get; set; }
        public double CurrentFrameTimeMs { get; set; }
        public double AverageFrameTimeMs { get; set; }
        public double CurrentFps { get; set; }
        public double FrameTimeDerivative { get; set; }
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = string.Empty;
    }
}
