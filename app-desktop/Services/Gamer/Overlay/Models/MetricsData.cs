using System;

namespace VoltrisOptimizer.Services.Gamer.Overlay.Models
{
    /// <summary>
    /// Dados de métricas coletadas do sistema
    /// </summary>
    public class MetricsData
    {
        /// <summary>
        /// Timestamp da coleta
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>
        /// FPS atual
        /// </summary>
        public double Fps { get; set; } = 0;

        /// <summary>
        /// Frame time em milissegundos
        /// </summary>
        public double FrameTimeMs { get; set; } = 0;

        /// <summary>
        /// Uso de CPU (%)
        /// </summary>
        public double CpuUsagePercent { get; set; } = 0;

        /// <summary>
        /// Uso de GPU (%)
        /// </summary>
        public double GpuUsagePercent { get; set; } = 0;

        /// <summary>
        /// Uso de RAM (%)
        /// </summary>
        public double RamUsagePercent { get; set; } = 0;

        /// <summary>
        /// Uso de VRAM (%)
        /// </summary>
        public double VramUsagePercent { get; set; } = 0;

        /// <summary>
        /// Temperatura da CPU (°C)
        /// </summary>
        public double? CpuTemperature { get; set; } = null;

        /// <summary>
        /// Temperatura da GPU (°C)
        /// </summary>
        public double? GpuTemperature { get; set; } = null;

        /// <summary>
        /// Clock da CPU (MHz)
        /// </summary>
        public double? CpuClockMhz { get; set; } = null;

        /// <summary>
        /// Clock do core da GPU (MHz)
        /// </summary>
        public double? GpuCoreClockMhz { get; set; }

        /// <summary>
        /// Clock da memória da GPU (MHz)
        /// </summary>
        public double? GpuMemoryClockMhz { get; set; } = null;

        /// <summary>
        /// Latência de entrada (ms)
        /// </summary>
        public double? InputLatencyMs { get; set; } = null;

        /// <summary>
        /// Histórico de FPS para gráfico (últimos 60 valores)
        /// </summary>
        public double[] FpsHistory { get; set; } = new double[60];

        /// <summary>
        /// Histórico de FrameTime para gráfico (últimos 60 valores)
        /// </summary>
        public double[] FrameTimeHistory { get; set; } = new double[60];

        /// <summary>
        /// Adiciona um valor ao histórico de FPS
        /// </summary>
        public void AddFpsToHistory(double fps)
        {
            Array.Copy(FpsHistory, 1, FpsHistory, 0, FpsHistory.Length - 1);
            FpsHistory[FpsHistory.Length - 1] = fps;
        }

        /// <summary>
        /// Adiciona um valor ao histórico de FrameTime
        /// </summary>
        public void AddFrameTimeToHistory(double frameTime)
        {
            Array.Copy(FrameTimeHistory, 1, FrameTimeHistory, 0, FrameTimeHistory.Length - 1);
            FrameTimeHistory[FrameTimeHistory.Length - 1] = frameTime;
        }
    }
}

