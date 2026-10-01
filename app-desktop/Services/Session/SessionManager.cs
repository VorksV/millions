using System;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Session
{
    public class SessionManager
    {
        private readonly ILoggingService _logger;
        private readonly ActivityMonitor _activityMonitor;
        private readonly Telemetry.TelemetryService _telemetryService;
        private readonly Enterprise.MachineIdentityService _identityService;
        private string _machineId;
        private CancellationTokenSource _heartbeatCts;
        private Task _heartbeatTask;

        public SessionManager(ILoggingService logger, ActivityMonitor activityMonitor, Telemetry.TelemetryService telemetryService, Enterprise.MachineIdentityService identityService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _activityMonitor = activityMonitor;
            _telemetryService = telemetryService;
            _identityService = identityService;
        }

        public Task StartSessionAsync(string machineId)
        {
            _logger.LogInfo($"[SessionManager] Iniciando sessAo (MachineId: {machineId})");
            _machineId = machineId;
            _heartbeatCts = new CancellationTokenSource();
            _heartbeatTask = HeartbeatLoopAsync(_heartbeatCts.Token);
            return Task.CompletedTask;
        }

        public async Task EndSessionAsync()
        {
            if (_heartbeatCts == null) return;
            _logger.LogInfo("[SessionManager] Encerrando sessAo");
            try { _heartbeatCts?.Cancel(); } catch (ObjectDisposedException) { }
            if (_heartbeatTask != null)
            {
                try { await _heartbeatTask; } catch (OperationCanceledException) { } catch (ObjectDisposedException) { }
            }
        }

        private async Task HeartbeatLoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(60), token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        }
    }
}
