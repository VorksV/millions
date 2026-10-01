using System;
using System.Diagnostics;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.Gamer.Interfaces;
using VoltrisOptimizer.Services.Thermal.Models;
using VoltrisOptimizer.Services.Gamer.Intelligence.Implementation;

namespace VoltrisOptimizer.Services.Gamer.Implementation
{
    /// <summary>
    /// Wrapper que executa uma operação de otimização e registra métricas de desempenho antes e depois.
    /// Usa o <see cref="LatestPerformanceMetricsProvider"/> para capturar o estado atual.
    /// </summary>
    public class PerformanceTelemetryWrapper
    {
        private readonly ILoggingService _logger;
        private readonly PerformanceTelemetryService _telemetry;
        private readonly LatestPerformanceMetricsProvider _metricsProvider;

        public PerformanceTelemetryWrapper(ILoggingService logger, PerformanceTelemetryService telemetry, LatestPerformanceMetricsProvider metricsProvider)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            _metricsProvider = metricsProvider ?? throw new ArgumentNullException(nameof(metricsProvider));
        }

        /// <summary>
        /// Executa a operação e registra métricas de ganho.
        /// </summary>
        /// <param name="actionName">Nome descritivo da ação (ex.: "ApplyProfile_Competitive").</param>
        /// <param name="operation">Função assíncrona que realiza a otimização.</param>
        public async Task ExecuteAsync(string actionName, Func<Task> operation)
        {
            var before = _metricsProvider.GetLatest();
            var sw = Stopwatch.StartNew();
            try
            {
                await operation();
            }
            catch (Exception ex)
            {
                _logger.LogError($"[TelemetryWrapper] Erro ao executar {actionName}: {ex.Message}");
                throw;
            }
            finally
            {
                sw.Stop();
                var after = _metricsProvider.GetLatest();
                await _telemetry.LogEventAsync("OPTIMIZATION", actionName, new
                {
                    DurationMs = sw.ElapsedMilliseconds,
                    CpuUsageBefore = before?.CpuUsage,
                    CpuUsageAfter = after?.CpuUsage,
                    RamBefore = before?.RamUsagePercent,
                    RamAfter = after?.RamUsagePercent,
                    DiskReadLatencyBefore = before?.DiskReadLatencyMs,
                    DiskReadLatencyAfter = after?.DiskReadLatencyMs,
                    DpcLatencyBefore = before?.DpcLatencyMs,
                    DpcLatencyAfter = after?.DpcLatencyMs,
                    IsrLatencyBefore = before?.IsrLatencyMs,
                    IsrLatencyAfter = after?.IsrLatencyMs
                });
            }
        }
    }
}
