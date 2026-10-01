using System;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Gamer
{
    /// <summary>
    /// STUB - IntelligentBackgroundSuspender desativado (V2 Architecture - Process Lasso approach)
    /// Loop de monitoramento removido para eliminar CPU wakeups
    /// </summary>
    public class IntelligentBackgroundSuspender : IDisposable
    {
        private readonly ILoggingService _logger;

        public IntelligentBackgroundSuspender(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogInfo("[IntelligentBackgroundSuspender] STUB - V2 Architecture (zero loops)");
        }

        public void Start(int gameProcessId) { }
        public void Stop() { }
        public void SuspendMoreAggressively() { }
        public void RestoreNormalPriorities() { }

        public void Dispose()
        {
            // Stub - nada para disposicionar
        }
    }
}