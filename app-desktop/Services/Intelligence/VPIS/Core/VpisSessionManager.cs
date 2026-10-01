using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core;
using VoltrisOptimizer.Services.Intelligence.VPIS.Engines;
using VoltrisOptimizer.Services.Intelligence.VPIS.Forensics;
using VoltrisOptimizer.Services.Intelligence.VPIS.Models;
using VoltrisOptimizer.Services.Intelligence.VPIS.Delivery;

namespace VoltrisOptimizer.Services.Intelligence.VPIS.Core
{
    public class VpisSessionManager
    {
        private const int BASE_INTERVAL_MS = 1000;
        private const int MAX_INTERVAL_MS = 3000;
        private const double CPU_IDLE_THRESHOLD = 30.0;

        private readonly VpisEventPipeline _eventPipeline;
        private readonly SessionInvestigator _investigator;
        private readonly VpisNotificationHandler _notificationHandler;
        private readonly List<IVpisDiagnosticEngine> _engines;

        private CancellationTokenSource _sessionCancellationTokenSource;
        private string _currentSessionName;
        private DateTime _sessionStartTime;
        private bool _isSessionActive;

        public VpisSessionManager(
            VpisEventPipeline eventPipeline,
            SessionInvestigator investigator,
            VpisNotificationHandler notificationHandler,
            IEnumerable<IVpisDiagnosticEngine> engines)
        {
            _eventPipeline = eventPipeline;
            _investigator = investigator;
            _notificationHandler = notificationHandler;
            _engines = engines.ToList();
        }

        public void StartSession(string processName)
        {
            if (_isSessionActive) return;

            _currentSessionName = processName;
            _sessionStartTime = DateTime.UtcNow;
            _isSessionActive = true;
            _eventPipeline.ClearSession();

            _sessionCancellationTokenSource = new CancellationTokenSource();

            foreach (var engine in _engines)
                engine.StartMonitoring(processName);

            Task.Run(() => MonitoringLoop(_sessionCancellationTokenSource.Token));
        }

        public void EndSession()
        {
            if (!_isSessionActive) return;

            _isSessionActive = false;
            _sessionCancellationTokenSource?.Cancel();

            foreach (var engine in _engines)
                engine.StopMonitoring();

            var duration = DateTime.UtcNow - _sessionStartTime;
            var events = _eventPipeline.GetSessionEvents();

            var report = _investigator.AnalyzeSession(_currentSessionName, duration, events);
            _notificationHandler.DeliverReport(report);
        }

        private async Task MonitoringLoop(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    foreach (var engine in _engines)
                    {
                        var insightEvent = engine.AnalyzeTick();
                        if (insightEvent != null)
                            _eventPipeline.PublishEvent(insightEvent);
                    }

                    int interval = GetAdaptiveInterval();
                    await Task.Delay(interval, cancellationToken);
                }
            }
            catch (TaskCanceledException)
            {
            }
        }

        private static int GetAdaptiveInterval()
        {
            try
            {
                var cache = SystemMetricsCache.Instance;
                double cpu = cache.CpuPercent;

                if (cpu > 80)
                    return BASE_INTERVAL_MS;

                if (cpu < CPU_IDLE_THRESHOLD)
                    return MAX_INTERVAL_MS;
            }
            catch
            {
            }

            return BASE_INTERVAL_MS + 500;
        }
    }
}
