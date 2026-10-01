using System;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Monitoring.Interfaces
{
    public interface IEtwFrameTimeMonitor : IDisposable
    {
        /// <summary>
        /// Ocorre a cada ciclo de atualização UI (ex: 500ms) enviando métricas atualizadas.
        /// </summary>
        event EventHandler<FrameMetrics>? MetricsUpdated;

        /// <summary>
        /// True se a sessão ETW está rodando com sucesso.
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        /// Inicia a captura de eventos de frametime (requer privilégio administrativo).
        /// </summary>
        Task StartAsync();

        /// <summary>
        /// Para a captura passiva de eventos e libera a sessão ETW.
        /// </summary>
        void Stop();
    }

    public class FrameMetrics
    {
        public double CurrentFps { get; set; }
        public double AverageFrametimeMs { get; set; }
        public double OnePercentLowFps { get; set; }
        public bool IsStuttering { get; set; }
        public int DetectedGameProcessId { get; set; }
        public string DetectedGameName { get; set; } = string.Empty;
    }
}
