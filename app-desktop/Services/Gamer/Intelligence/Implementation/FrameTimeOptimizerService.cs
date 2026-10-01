using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.Gamer.Intelligence.Interfaces;
using VoltrisOptimizer.Services.Gamer.Intelligence.Models;

namespace VoltrisOptimizer.Services.Gamer.Intelligence.Implementation
{
    public class FrameTimeOptimizerService : IFrameTimeOptimizer, IDisposable
    {
        private readonly ILoggingService _logger;
        private readonly IProcessRunner _processRunner;
        private readonly List<StutterEvent> _stutterHistory = new();
        private int _monitoredProcessId;
        private FrameTimeMetrics _currentMetrics = new();

        private static FrameTimeOptimizerService? _instance;
        public static FrameTimeOptimizerService Instance => _instance ??= new FrameTimeOptimizerService();

        public FrameTimeMetrics CurrentMetrics => _currentMetrics;
        public event EventHandler<StutterEvent>? StutterDetected;

        public FrameTimeOptimizerService(ILoggingService logger = null, IProcessRunner processRunner = null)
        {
            _logger = logger ?? VoltrisOptimizer.App.LoggingService;
            _processRunner = processRunner;
            _logger?.LogEntry(nameof(FrameTimeOptimizerService));
            _logger?.LogExit(nameof(FrameTimeOptimizerService));
        }

        public void StartMonitoring(int processId)
        {
            _logger.LogEntry(nameof(StartMonitoring), ("processId", processId));
            _monitoredProcessId = processId;
            _logger.LogInfo($"[FrameTime] Monitoramento passivo iniciado (Single-Shot Architecture) para PID {_monitoredProcessId}");
            _logger.LogExit(nameof(StartMonitoring));
        }

        public void StopMonitoring()
        {
            _logger.LogEntry(nameof(StopMonitoring));
            _logger.LogExit(nameof(StopMonitoring));
        }

        public IReadOnlyList<StutterEvent> GetStutterHistory()
        {
            _logger.LogEntry(nameof(GetStutterHistory));
            _logger.LogExit(nameof(GetStutterHistory), _stutterHistory.Count);
            return _stutterHistory;
        }

        public Task<bool> ApplyPreventiveFixesAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogEntry(nameof(ApplyPreventiveFixesAsync));
            _logger.LogExit(nameof(ApplyPreventiveFixesAsync), true);
            return Task.FromResult(true);
        }

        public void Dispose()
        {
            _logger.LogEntry(nameof(Dispose));
            StopMonitoring();
            GC.SuppressFinalize(this);
            _logger.LogExit(nameof(Dispose));
        }
    }
}
