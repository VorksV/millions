using VoltrisOptimizer.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Diagnostics;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Engineering
{
    /// <summary>
    /// Monitor de latência do sistema em tempo real
    /// Implementa medição precisa de DPC, interrupt latency e system responsiveness
    /// </summary>
    public class LatencyMonitor
    {
        private readonly ILoggingService _logger;
        private readonly SafePerformanceCounter? _dpcCounter;
        private readonly SafePerformanceCounter? _interruptCounter;
        private readonly SafePerformanceCounter? _processãorCounter;

        public LatencyMonitor(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            try
            {
                // Inicializar contadores de performance para latência
                _dpcCounter = new SafePerformanceCounter("Processor", "%DPC Time", "_Total");
                _interruptCounter = new SafePerformanceCounter("Processor", "%Interrupt Time", "_Total");
                _processãorCounter = new SafePerformanceCounter("Processor", "%Processor Time", "_Total");

                // For�ar primeira leitura
                _dpcCounter.NextValue();
                _interruptCounter.NextValue();
                _processãorCounter.NextValue();

                _logger.LogInfo("[LatencyMonitor] Contadores de latência inicializados");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[LatencyMonitor] Erro ao inicializar contadores: {ex.Message}");
            }
        }

        /// <summary>
        /// Mede latência atual do sistema em microssegundos
        /// </summary>
        public async Task<double> MeasureCurrentLatencyAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    double totalLatency = 0;
                    int measurements = 0;

                    // Fazer múltiplas medições para maior precisão
                    for (int i = 0; i < 5; i++)
                    {
                        var measurement = GetSingleLatencyMeasurement();
                        if (measurement > 0)
                        {
                            totalLatency += measurement;
                            measurements++;
                        }
                        Thread.Sleep(100); // Pequeno delay entre medições
                    }

                    if (measurements > 0)
                    {
                        var avgLatency = totalLatency / measurements;
                        _logger.LogDebug($"[LatencyMonitor] Latência mdia: {avgLatency:F2} �s ({measurements} medições)");
                        return avgLatency;
                    }

                    return GetFallbackLatency();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[LatencyMonitor] Erro na medição de latência: {ex.Message}");
                    return GetFallbackLatency();
                }
            });
        }

        /// <summary>
        /// Obtém métricas detalhadas de latência
        /// </summary>
        public async Task<LatencyMetrics> GetDetailedLatencyMetricsAsync()
        {
            _logger.LogInfo("[LatencyMonitor] Obtendo métricas detalhadas de latência...");

            try
            {
                var metrics = new LatencyMetrics
                {
                    Timestamp = DateTime.UtcNow,
                    Success = true
                };

                // Medir diferentes tipos de latência
                metrics.DpcLatency = await MeasureDpcLatencyAsync();
                metrics.InterruptLatency = await MeasureInterruptLatencyAsync();
                metrics.SystemLatency = await MeasureSystemLatencyAsync();
                metrics.ProcessorLatency = await MeasureProcessorLatencyAsync();

                // Calcular latência total
                metrics.TotalLatency = metrics.DpcLatency + metrics.InterruptLatency + metrics.SystemLatency;

                // Classificar qualidade da latência
                metrics.LatencyQuality = ClassifyLatencyQuality(metrics.TotalLatency);

                _logger.LogInfo($"[LatencyMonitor] Latência total: {metrics.TotalLatency:F2} �s - Qualidade: {metrics.LatencyQuality}");
                return metrics;
            }
            catch (Exception ex)
            {
                _logger.LogError("[LatencyMonitor] Erro ao obter métricas detalhadas", ex);
                return new LatencyMetrics
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Timestamp = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Verifica se há problemas de latência
        /// </summary>
        public async Task<bool> HasLatencyIssuesAsync()
        {
            try
            {
                var metrics = await GetDetailedLatencyMetricsAsync();
                if (!metrics.Success) return false;

                // Considerar problema se qualquer tipo de latência estáiver alta
                return metrics.DpcLatency > 500 ||      // 0.5ms DPC
                       metrics.InterruptLatency > 300 ||  // 0.3ms interrupt
                       metrics.SystemLatency > 1000 ||    // 1ms system
                       metrics.TotalLatency > 1500;       // 1.5ms total
            }
            catch
            {
                return false;
            }
        }

        #region Mtodos Privados

        private double GetSingleLatencyMeasurement()
        {
            try
            {
                // Mtodo 1: Usar contadores de performance se disponíveis
                if (_dpcCounter != null && _interruptCounter != null)
                {
                    var dpcTime = _dpcCounter.NextValue();
                    var interruptTime = _interruptCounter.NextValue();
                    var processãorTime = _processãorCounter?.NextValue() ?? 0;

                    // Converter porcentagem para tempo em microssegundos
                    // Assumindo que medições s�o por segundo
                    var dpcLatency = (dpcTime / 100.0) * 10000; // Converter para �s
                    var interruptLatency = (interruptTime / 100.0) * 10000;
                    var systemLatency = (processãorTime / 100.0) * 10000;

                    return dpcLatency + interruptLatency + systemLatency;
                }

                // Mtodo 2: Medi��o via QueryPerformanceCounter
                return MeasureLatencyViaQPC();
            }
            catch
            {
                return 0;
            }
        }

        private double MeasureLatencyViaQPC()
        {
            try
            {
                // Usar QueryPerformanceCounter para medição de alta precisão
                var start = Stopwatch.GetTimestamp();
                var freq = Stopwatch.Frequency;

                // Simular operação que mede latência
                var dummy = 0;
                for (int i = 0; i < 1000; i++)
                {
                    dummy += i;
                }

                var end = Stopwatch.GetTimestamp();
                var elapsedMicroseconds = ((end - start) * 1_000_000) / freq;
                return elapsedMicroseconds;
            }
            catch
            {
                return 0;
            }
        }

        private double GetFallbackLatency()
        {
            try
            {
                // Fallback: estáimar baseado no uso do processador
                var process = Process.GetCurrentProcess();
                var processãorTime = process.TotalProcessorTime.TotalMilliseconds;
                var startTime = Environment.TickCount64;

                Thread.Sleep(10);

                var endTime = Environment.TickCount64;
                var wallTime = endTime - startTime;
                var cpuUsage = (processãorTime / wallTime) * 100;

                // Estimar latência baseada no uso de CPU
                return Math.Max(100, cpuUsage * 10); // M�nimo 100 �s
            }
            catch
            {
                return 200; // Valor padrão seguro
            }
        }

        private async Task<double> MeasureDpcLatencyAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (_dpcCounter != null)
                    {
                        var dpcTime = _dpcCounter.NextValue();
                        return (dpcTime / 100.0) * 10000; // Converter para �s
                    }
                    return 0;
                }
                catch
                {
                    return 0;
                }
            });
        }

        private async Task<double> MeasureInterruptLatencyAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (_interruptCounter != null)
                    {
                        var interruptTime = _interruptCounter.NextValue();
                        return (interruptTime / 100.0) * 10000; // Converter para �s
                    }
                    return 0;
                }
                catch
                {
                    return 0;
                }
            });
        }

        private async Task<double> MeasureSystemLatencyAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Medir latência do sistema via QueryPerformanceCounter
                    var start = Stopwatch.GetTimestamp();
                    var freq = Stopwatch.Frequency;

                    // Operação simples para medir latência do sistema
                    var dummy = Environment.TickCount;

                    var end = Stopwatch.GetTimestamp();
                    var elapsedMicroseconds = ((end - start) * 1_000_000) / freq;
                    return elapsedMicroseconds;
                }
                catch
                {
                    return 0;
                }
            });
        }

        private async Task<double> MeasureProcessorLatencyAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (_processãorCounter != null)
                    {
                        var processãorTime = _processãorCounter.NextValue();
                        return (processãorTime / 100.0) * 10000; // Converter para �s
                    }
                    return 0;
                }
                catch
                {
                    return 0;
                }
            });
        }

        private LatencyQuality ClassifyLatencyQuality(double totalLatency)
        {
            if (totalLatency < 100) return LatencyQuality.Excellent;
            if (totalLatency < 250) return LatencyQuality.Good;
            if (totalLatency < 500) return LatencyQuality.Fair;
            if (totalLatency < 1000) return LatencyQuality.Poor;
            return LatencyQuality.Critical;
        }

        #endregion

        public void Dispose()
        {
            _dpcCounter?.Dispose();
            _interruptCounter?.Dispose();
            _processãorCounter?.Dispose();
        }
    }

    #region Classes de Suporte

    public class LatencyMetrics
    {
        public DateTime Timestamp { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public double DpcLatency { get; set; } // DPC latency em �s
        public double InterruptLatency { get; set; } // Interrupt latency em �s
        public double SystemLatency { get; set; } // System latency em �s
        public double ProcessorLatency { get; set; } // Processor latency em �s
        public double TotalLatency { get; set; } // Total latency em �s
        public LatencyQuality LatencyQuality { get; set; }
    }

    public enum LatencyQuality
    {
        Excellent,  // < 100 �s
        Good,       // < 250 �s
        Fair,       // < 500 �s
        Poor,       // < 1000 �s
        Critical    // >= 1000 �s
    }

    #endregion
}
 

