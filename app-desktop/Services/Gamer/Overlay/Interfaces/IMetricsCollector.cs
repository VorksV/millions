using System;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.Gamer.Overlay.Models;

namespace VoltrisOptimizer.Services.Gamer.Overlay.Interfaces
{
    /// <summary>
    /// Interface para coletor de métricas do sistema
    /// </summary>
    public interface IMetricsCollector : IDisposable
    {
        /// <summary>
        /// Evento disparado quando novas métricas são coletadas
        /// </summary>
        event EventHandler<MetricsData>? MetricsUpdated;

        /// <summary>
        /// Inicia a coleta de métricas para um processo de jogo
        /// </summary>
        Task StartAsync(int gameProcessId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Para a coleta de métricas
        /// </summary>
        Task StopAsync();

        /// <summary>
        /// Obtém as métricas atuais
        /// </summary>
        MetricsData GetCurrentMetrics();
    }
}


