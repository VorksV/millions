using System;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Gamer
{
    /// <summary>
    /// STUB - InputLatencyMonitorService desativado (V2 Architecture - Process Lasso approach)
    /// Loop de polling removido para eliminar DPC latency spikes
    /// </summary>
    public class InputLatencyMonitorService : IDisposable
    {
        private readonly ILoggingService _logger;

        public InputLatencyMonitorService(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogInfo("[InputLatencyMonitorService] STUB - V2 Architecture (zero loops)");
        }

        public void Start(int gameProcessId) { }
        public void Stop() { }

        public void Dispose()
        {
            // Stub - nada para disposicionar
        }
    }
}