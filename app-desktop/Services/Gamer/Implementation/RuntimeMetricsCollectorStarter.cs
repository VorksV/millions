using System;
using VoltrisOptimizer.Services.Gamer.Interfaces;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer.Implementation
{
    /// <summary>
    /// Inicializa o RuntimeMetricsCollectorService no arranque da aplicação.
    /// O serviço será resolvido via DI e iniciado imediatamente.
    /// </summary>
    public class RuntimeMetricsCollectorStarter
    {
        private readonly IRuntimeMetricsCollector _collector;
        private readonly ILoggingService _logger;

        public RuntimeMetricsCollectorStarter(IRuntimeMetricsCollector collector, ILoggingService logger)
        {
            _collector = collector ?? throw new ArgumentNullException(nameof(collector));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogDebug("[MetricsStarter] >>> ENTER .ctor");
            Start();
            _logger.LogDebug("[MetricsStarter] <<< EXIT .ctor");
        }

        private void Start()
        {
            try
            {
                _collector.Start();
                _logger?.LogInfo("[MetricsStarter] Coleta de métricas iniciada.");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[MetricsStarter] Falha ao iniciar coleta: {ex.Message}");
                throw;
            }
        }
    }
}
