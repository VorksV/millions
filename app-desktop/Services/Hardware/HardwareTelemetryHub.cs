using System;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.HardwareTelemetry;

namespace VoltrisOptimizer.Services.Hardware
{
    /// <summary>
    /// GLOBAL HARDWARE TELEMETRY HUB (Phase 2 Adapter)
    /// Adaptador legado para manter a interface com os motores VPI e Overlay.
    /// Agora delega o fornecimento de dados de telemetria diretamente ao SensorCache de altíssima performance.
    /// </summary>
    public class HardwareTelemetryHub : IHardwareTelemetryHub
    {
        private readonly SensorCache _sensorCache;
        private readonly HardwareTelemetryService _telemetryService;

        public event EventHandler<HardwareTelemetryMetrics>? MetricsUpdated;

        public HardwareTelemetryHub(SensorCache sensorCache, HardwareTelemetryService telemetryService)
        {
            _sensorCache = sensorCache;
            _telemetryService = telemetryService;
        }

        /// <summary>
        /// Devolve as métricas do <see cref="SensorCache"/>.
        ///
        /// CORREÇÃO DE AUDITORIA: quando não havia sensor de temperatura, esta
        /// classe SUBSTITUÍA o NaN por <c>30 + (cpuLoad/100)*50</c> — uma fórmula
        /// inventada que produzia um número plausível e, portanto, indistinguível de
        /// uma medição real. Como o cartão SAÚDE e a página de Diagnóstico usam
        /// esta fonte, a ausência do sensor aparecia como temperatura válida, e
        /// falha de sensor produzia a MELHOR nota possível no score.
        ///
        /// Agora, sem sensor, a temperatura é <c>double.NaN</c> — o mesmo valor que
        /// o <see cref="SensorCache"/> já devolvia. Os consumidores devem tratar NaN
        /// como "não disponível" em vez de convertê-lo em um número.
        /// </summary>
        public HardwareTelemetryMetrics GetLatestMetrics()
        {
            var snap = _sensorCache.GetCurrent();

            // Temperaturas são repassadas como vieram do sensor. Sem sensor = NaN.
            // Nenhuma estimativa é feita aqui: um valor inventado é pior do que
            // ausência de informação, porque não pode ser distinguido do real.
            double cpuTemp = Sanitize(snap.CpuTemperature, min: 0, max: 150);
            double gpuTemp = Sanitize(snap.GpuTemperature, min: 0, max: 150);

            return new HardwareTelemetryMetrics
            {
                CpuTemperature = cpuTemp,
                CpuLoad = Finite(snap.CpuUsage),
                CpuClock = Finite(snap.CpuClock),
                CpuCores = Environment.ProcessorCount,
                GpuTemperature = gpuTemp,
                GpuLoad = Finite(snap.GpuUsage),
                GpuClock = Finite(snap.GpuCoreClock),
                GpuVramUsed = Finite(snap.GpuMemoryUsed),
                GpuVramTotal = Finite(snap.GpuMemoryTotal),
                SystemRamUsedGB = Finite(snap.RamUsage),
                Timestamp = snap.Timestamp
            };
        }

        /// <summary>
        /// Temperatura fora da faixa plausível vira NaN (indisponível) em vez de 0.
        /// O valor 0 °C é fisicamente impossível e era exibido como "0 °C".
        /// </summary>
        private static double Sanitize(double value, double min, double max)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return double.NaN;
            if (value <= min || value > max) return double.NaN;
            return value;
        }

        private static double Finite(double value) =>
            double.IsNaN(value) || double.IsInfinity(value) ? 0 : value;

        public Task<HardwareTelemetryMetrics> ForcePollAsync()
        {
            return Task.FromResult(GetLatestMetrics());
        }

        public Task StartMonitoringAsync()
        {
            // Executa em Task.Run para evitar bloqueio síncrono por chamadas WMI de sensores
            return Task.Run(() => _telemetryService.Start());
        }

        public Task StopMonitoringAsync()
        {
            // Ocultado intencionalmente para não derrubar o Singleton Global
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            // Serviço central lida com seu próprio ciclo de vida
        }
    }
}
