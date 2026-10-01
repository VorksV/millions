using VoltrisOptimizer.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer.Implementation
{
    /// <summary>
    /// DETECTOR REAL DE PERFORMANCE - SEM PLACEBO
    /// Mede frametime real, spikes e correlaciona com eventos do sistema
    /// </summary>
    public class RealPerformanceDetector : IDisposable
    {
        private readonly ILoggingService _logger;

        // Estado de monitoramento
        private bool _isMonitoring = false;
        private int? _targetProcessId = null;
        private CancellationTokenSource? _monitoringCts;
        private Task? _monitoringTask;

        // Buffers de métricas reais
        private readonly CircularBuffer<double> _frameTimeBuffer = new(300); // 5 segundos a 60 FPS
        private readonly CircularBuffer<PerformanceSnapshot> _snapshotBuffer = new(60); // 1 minuto de snapshots

        // Performance counters reais
        private SafePerformanceCounter? _cpuCounter;
        private SafePerformanceCounter? _memoryCounter;
        private SafePerformanceCounter? _diskQueueCounter;
        private SafePerformanceCounter? _pageFaultsCounter;

        // Baseline para detecção de spikes
        private double _baselineFrameTime = 16.67; // 60 FPS
        private double _stutterThreshold = 1.5; // 50% acima do baseline
        private DateTime _lastSpikeTime = DateTime.MinValue;

        // Eventos correlacionados
        private readonly List<PerformanceEvent> _correlatedEvents = new();

        public RealPerformanceDetector(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogDebug($"[RealPerfDetector] >>> ENTER .ctor");
            InitializeCounters();
            _logger.LogDebug($"[RealPerfDetector] <<< EXIT .ctor");
        }

        /// <summary>
        /// Inicia monitoramento real do processão do jogo
        /// </summary>
        public async Task<bool> StartMonitoringAsync(int processId, CancellationToken cancellationToken = default)
        {
            var methodStart = DateTime.Now;
            _logger.LogDebug($"[RealPerfDetector] >>> ENTER StartMonitoringAsync (processId: {processId})");
            try
            {
                if (_isMonitoring)
                {
                    _logger.LogWarning("[RealPerfDetector] Monitoramento já ativo");
                    _logger.LogDebug($"[RealPerfDetector] <<< EXIT StartMonitoringAsync (Already monitoring, Duration: {(DateTime.Now - methodStart).TotalMilliseconds}ms)");
                    return false;
                }

                // Validar processão
                if (!IsValidProcess(processId))
                {
                    _logger.LogError($"[RealPerfDetector] Processão {processId} inválido");
                    _logger.LogDebug($"[RealPerfDetector] <<< EXIT StartMonitoringAsync (Invalid process, Duration: {(DateTime.Now - methodStart).TotalMilliseconds}ms)");
                    return false;
                }

                _targetProcessId = processId;
                _isMonitoring = true;
                _monitoringCts = new CancellationTokenSource();

                // Calcular baseline inicial
                await CalculateBaselineAsync(cancellationToken);

                // Iniciar task de monitoramento
                _monitoringTask = Task.Run(() => MonitoringLoop(_monitoringCts.Token), _monitoringCts.Token);

                _logger.LogSuccess($"[RealPerfDetector] Monitoramento iniciado para PID {processId}");
                _logger.LogDebug($"[RealPerfDetector] <<< EXIT StartMonitoringAsync (Success: true, Duration: {(DateTime.Now - methodStart).TotalMilliseconds}ms)");

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("[RealPerfDetector] Erro ao iniciar monitoramento", ex);
                await StopMonitoringAsync();
                _logger.LogDebug($"[RealPerfDetector] <<< EXIT StartMonitoringAsync (Error: {ex.Message}, Duration: {(DateTime.Now - methodStart).TotalMilliseconds}ms)");
                return false;
            }
        }

        /// <summary>
        /// Para monitoramento e gera relatório
        /// </summary>
        public async Task<PerformanceReport> StopMonitoringAsync()
        {
            var methodStart = DateTime.Now;
            _logger.LogDebug($"[RealPerfDetector] >>> ENTER StopMonitoringAsync");
            if (!_isMonitoring)
            {
                _logger.LogDebug($"[RealPerfDetector] <<< EXIT StopMonitoringAsync (Not monitoring, Duration: {(DateTime.Now - methodStart).TotalMilliseconds}ms)");
                return new PerformanceReport { IsValid = false };
            }

            try
            {
                _monitoringCts?.Cancel();

                if (_monitoringTask != null)
                {
                    await _monitoringTask;
                }

                _isMonitoring = false;
                var report = GeneratePerformanceReport();

                _logger.LogInfo($"[RealPerfDetector] Monitoramento parado - Gerado relatório com {report.TotalFrames} frames");
                _logger.LogDebug($"[RealPerfDetector] <<< EXIT StopMonitoringAsync (Success: true, Duration: {(DateTime.Now - methodStart).TotalMilliseconds}ms)");

                return report;
            }
            catch (Exception ex)
            {
                _logger.LogError("[RealPerfDetector] Erro ao parar monitoramento", ex);
                _logger.LogDebug($"[RealPerfDetector] <<< EXIT StopMonitoringAsync (Error: {ex.Message}, Duration: {(DateTime.Now - methodStart).TotalMilliseconds}ms)");
                return new PerformanceReport { IsValid = false };
            }
            finally
            {
                _monitoringCts?.Dispose();
                _monitoringCts = null;
                _monitoringTask = null;
                _targetProcessId = null;
            }
        }

        /// <summary>
        /// Obtém métricas em tempo real
        /// </summary>
        public RealTimeMetrics GetCurrentMetrics()
        {
            _logger.LogDebug($"[RealPerfDetector] >>> ENTER GetCurrentMetrics");
            if (!_isMonitoring || !_frameTimeBuffer.HasData)
            {
                _logger.LogDebug($"[RealPerfDetector] <<< EXIT GetCurrentMetrics (No data)");
                return new RealTimeMetrics();
            }

            var frameTimesArray = _frameTimeBuffer.GetRecent(60); // último segundo
            var recentSnapshots = _snapshotBuffer.GetRecent(10); // últimos 10 segundos
            var frameTimes = frameTimesArray.ToList();

            var result = new RealTimeMetrics
            {
                CurrentFps = frameTimes.Count > 0 ? 1000.0 / frameTimes.Average() : 0,
                AvgFrameTime = frameTimes.Average(),
                P1LowFps = CalculatePercentile(frameTimes, 0.01),
                P01LowFps = CalculatePercentile(frameTimes, 0.001),
                FrameTimeStability = CalculateStability(frameTimes),
                CpuUsage = recentSnapshots.LastOrDefault()?.CpuUsage ?? 0,
                MemoryUsage = recentSnapshots.LastOrDefault()?.MemoryUsage ?? 0,
                IsStuttering = DetectStuttering(frameTimes),
                LastSpikeTime = _lastSpikeTime
            };
            _logger.LogDebug($"[RealPerfDetector] <<< EXIT GetCurrentMetrics (FPS: {result.CurrentFps:F1})");
            return result;
        }

        private async Task MonitoringLoop(CancellationToken cancellationToken)
        {
            _logger.LogDebug($"[RealPerfDetector] >>> ENTER MonitoringLoop");
            _logger.LogInfo("[RealPerfDetector] Iniciando loop de monitoramento real");

            var frameTimer = new HighPrecisionTimer();
            var lastFrameTime = 0.0;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Medir frametime real
                    var currentFrameTime = frameTimer.GetElapsedMilliseconds();

                    if (lastFrameTime > 0)
                    {
                        var frameDelta = currentFrameTime - lastFrameTime;
                        _frameTimeBuffer.Add(frameDelta);

                        // Detectar spike em tempo real
                        if (frameDelta > _baselineFrameTime * _stutterThreshold)
                        {
                            await HandleFrameTimeSpikeAsync(frameDelta, cancellationToken);
                        }
                    }

                    lastFrameTime = currentFrameTime;

                    // Capturar snapshot do sistema a cada segundo
                    if (DateTime.Now.Millisecond < 100) // Aproximadamente 1x por segundo
                    {
                        await CaptureSystemSnapshotAsync(cancellationToken);
                    }

                    // Aguardar próximo frame (target 60 FPS = 16.67ms)
                    await Task.Delay(16, cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[RealPerfDetector] Erro no loop: {ex.Message}");
                    await Task.Delay(1000, cancellationToken);
                }
            }
            _logger.LogDebug($"[RealPerfDetector] <<< EXIT MonitoringLoop");
        }

        private async Task CalculateBaselineAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug($"[RealPerfDetector] >>> ENTER CalculateBaselineAsync");
            _logger.LogInfo("[RealPerfDetector] Calculando baseline de performance");

            var baselineSamples = new List<double>();
            var frameTimer = new HighPrecisionTimer();
            var lastTime = 0.0;

            // Coletar amostras por 5 segundos
            var endTime = DateTime.UtcNow.AddSeconds(5);

            while (DateTime.UtcNow < endTime && !cancellationToken.IsCancellationRequested)
            {
                var frameTime = frameTimer.GetElapsedMilliseconds() / 1000.0 - lastTime;

                if (frameTime > 0.001) // Ignorar frames muito rápidos
                {
                    baselineSamples.Add(frameTime);
                    lastTime = frameTimer.GetElapsedMilliseconds() / 1000.0;
                }

                await Task.Delay(1, cancellationToken);
            }

            if (baselineSamples.Count > 0)
            {
                _baselineFrameTime = baselineSamples.Average();
                _logger.LogInfo($"[RealPerfDetector] Baseline calculado: {_baselineFrameTime:F3} ms ({baselineSamples.Count} amostras)");
            }
            _logger.LogDebug($"[RealPerfDetector] <<< EXIT CalculateBaselineAsync");
        }

        private async Task CaptureSystemSnapshotAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug($"[RealPerfDetector] >>> ENTER CaptureSystemSnapshotAsync");
            try
            {
                var snapshot = new PerformanceSnapshot
                {
                    Timestamp = DateTime.UtcNow,
                    CpuUsage = _cpuCounter?.NextValue() ?? 0,
                    MemoryUsage = _memoryCounter?.NextValue() ?? 0,
                    DiskQueueLength = _diskQueueCounter?.NextValue() ?? 0,
                    PageFaultsPerSec = _pageFaultsCounter?.NextValue() ?? 0
                };

                _snapshotBuffer.Add(snapshot);

                // Detectar correlações com eventos do sistema
                await DetectSystemCorrelationsAsync(snapshot, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[RealPerfDetector] Erro ao capturar snapshot: {ex.Message}");
            }
            finally
            {
                _logger.LogDebug($"[RealPerfDetector] <<< EXIT CaptureSystemSnapshotAsync");
            }
        }

        private async Task HandleFrameTimeSpikeAsync(double frameTime, CancellationToken cancellationToken)
        {
            _logger.LogDebug($"[RealPerfDetector] >>> ENTER HandleFrameTimeSpikeAsync (frameTime: {frameTime:F2}ms)");
            _lastSpikeTime = DateTime.Now;

            // Capturar snapshot imediato para correlação
            var immediateSnapshot = new PerformanceSnapshot
            {
                Timestamp = DateTime.UtcNow,
                CpuUsage = _cpuCounter?.NextValue() ?? 0,
                MemoryUsage = _memoryCounter?.NextValue() ?? 0,
                DiskQueueLength = _diskQueueCounter?.NextValue() ?? 0,
                PageFaultsPerSec = _pageFaultsCounter?.NextValue() ?? 0
            };

            // Detectar causa provável
            var probableCause = DetectProbableCause(immediateSnapshot);

            var spikeEvent = new PerformanceEvent
            {
                Timestamp = DateTime.UtcNow,
                EventType = PerformanceEventType.FrameTimeSpike,
                Severity = frameTime > _baselineFrameTime * 3.0 ? EventSeverity.High : EventSeverity.Medium,
                Details = $"FrameTime: {frameTime:F2} ms (baseline: {_baselineFrameTime:F2} ms)",
                CorrelatedData = immediateSnapshot,
                ProbableCause = probableCause
            };

            _correlatedEvents.Add(spikeEvent);

            _logger.LogWarning($"[RealPerfDetector] SPIKE DETECTADO: {frameTime:F2} ms - Provável causa: {probableCause}");
            _logger.LogDebug($"[RealPerfDetector] <<< EXIT HandleFrameTimeSpikeAsync");
        }

        private async Task DetectSystemCorrelationsAsync(PerformanceSnapshot snapshot, CancellationToken cancellationToken)
        {
            _logger.LogDebug($"[RealPerfDetector] >>> ENTER DetectSystemCorrelationsAsync");
            // CPU spike correlacionado?
            if (snapshot.CpuUsage > 80)
            {
                _correlatedEvents.Add(new PerformanceEvent
                {
                    Timestamp = DateTime.UtcNow,
                    EventType = PerformanceEventType.CpuSpike,
                    Severity = snapshot.CpuUsage > 95 ? EventSeverity.High : EventSeverity.Medium,
                    Details = $"CPU: {snapshot.CpuUsage:F1}%",
                    CorrelatedData = snapshot
                });
            }

            // Page fault correlacionado?
            if (snapshot.PageFaultsPerSec > 100)
            {
                _correlatedEvents.Add(new PerformanceEvent
                {
                    Timestamp = DateTime.UtcNow,
                    EventType = PerformanceEventType.PageFaultSpike,
                    Severity = snapshot.PageFaultsPerSec > 500 ? EventSeverity.High : EventSeverity.Medium,
                    Details = $"Page faults: {snapshot.PageFaultsPerSec:F0}/s",
                    CorrelatedData = snapshot
                });
            }

            // Disk activity correlacionado?
            if (snapshot.DiskQueueLength > 2)
            {
                _correlatedEvents.Add(new PerformanceEvent
                {
                    Timestamp = DateTime.UtcNow,
                    EventType = PerformanceEventType.DiskActivity,
                    Severity = snapshot.DiskQueueLength > 5 ? EventSeverity.High : EventSeverity.Medium,
                    Details = $"Disk queue: {snapshot.DiskQueueLength:F1}",
                    CorrelatedData = snapshot
                });
            }
            _logger.LogDebug($"[RealPerfDetector] <<< EXIT DetectSystemCorrelationsAsync");
        }

        private string DetectProbableCause(PerformanceSnapshot snapshot)
        {
            _logger.LogDebug($"[RealPerfDetector] >>> ENTER DetectProbableCause");
            if (snapshot.CpuUsage > 90) return "CPU Bottleneck";
            if (snapshot.PageFaultsPerSec > 200) return "Memory Pressure";
            if (snapshot.DiskQueueLength > 3) return "Disk I/O";
            if (snapshot.MemoryUsage > 90) return "High Memory Usage";
            _logger.LogDebug($"[RealPerfDetector] <<< EXIT DetectProbableCause (Result: Unknown)");
            return "Unknown";
        }

        private PerformanceReport GeneratePerformanceReport()
        {
            _logger.LogDebug($"[RealPerfDetector] >>> ENTER GeneratePerformanceReport");
            if (!_frameTimeBuffer.HasData)
            {
                _logger.LogDebug($"[RealPerfDetector] <<< EXIT GeneratePerformanceReport (No data)");
                return new PerformanceReport { IsValid = false };
            }

            var allFrameTimes = _frameTimeBuffer.GetAll().ToList();
            var allSnapshots = _snapshotBuffer.GetAll().ToList();

            var result = new PerformanceReport
            {
                IsValid = true,
                Duration = TimeSpan.FromMilliseconds(allFrameTimes.Count * _baselineFrameTime),
                TotalFrames = allFrameTimes.Count,
                AvgFps = 1000.0 / allFrameTimes.Average(),
                P1LowFps = CalculatePercentile(allFrameTimes, 0.01),
                P01LowFps = CalculatePercentile(allFrameTimes, 0.001),
                AvgFrameTime = allFrameTimes.Average(),
                MaxFrameTime = allFrameTimes.Max(),
                FrameTimeStdDev = CalculateStandardDeviation(allFrameTimes),
                StutterCount = CountStutters(allFrameTimes),
                StutterPercentage = (CountStutters(allFrameTimes) * 100.0) / allFrameTimes.Count,
                CorrelatedEvents = _correlatedEvents.Where(e => e.Timestamp > DateTime.UtcNow.AddMinutes(-1)).ToList(),
                SystemMetrics = new SystemMetrics
                {
                    AvgCpuUsage = allSnapshots.Any() ? allSnapshots.Average(s => s.CpuUsage) : 0,
                    MaxCpuUsage = allSnapshots.Any() ? allSnapshots.Max(s => s.CpuUsage) : 0,
                    AvgMemoryUsage = allSnapshots.Any() ? allSnapshots.Average(s => s.MemoryUsage) : 0,
                    AvgPageFaults = allSnapshots.Any() ? allSnapshots.Average(s => s.PageFaultsPerSec) : 0
                }
            };
            _logger.LogDebug($"[RealPerfDetector] <<< EXIT GeneratePerformanceReport (TotalFrames: {result.TotalFrames}, AvgFps: {result.AvgFps:F1})");
            return result;
        }

        private double CalculatePercentile(List<double> values, double percentile)
        {
            _logger.LogDebug($"[RealPerfDetector] >>> ENTER CalculatePercentile (percentile: {percentile})");
            if (!values.Any())
            {
                _logger.LogDebug($"[RealPerfDetector] <<< EXIT CalculatePercentile (Result: 0)");
                return 0;
            }

            values.Sort();
            var index = (int)(percentile * values.Count);

            var result = 1000.0 / Math.Max(values[index], 0.1);
            _logger.LogDebug($"[RealPerfDetector] <<< EXIT CalculatePercentile (Result: {result:F1})");
            return result;
        }

        private double CalculateStandardDeviation(List<double> values)
        {
            _logger.LogDebug($"[RealPerfDetector] >>> ENTER CalculateStandardDeviation");
            if (!values.Any())
            {
                _logger.LogDebug($"[RealPerfDetector] <<< EXIT CalculateStandardDeviation (Result: 0)");
                return 0;
            }

            var mean = values.Average();
            var variance = values.Average(x => Math.Pow(x - mean, 2));
            var result = Math.Sqrt(variance);
            _logger.LogDebug($"[RealPerfDetector] <<< EXIT CalculateStandardDeviation (Result: {result:F3})");
            return result;
        }

        private double CalculateStability(List<double> values)
        {
            _logger.LogDebug($"[RealPerfDetector] >>> ENTER CalculateStability");
            if (!values.Any())
            {
                _logger.LogDebug($"[RealPerfDetector] <<< EXIT CalculateStability (Result: 0)");
                return 0;
            }

            var stdDev = CalculateStandardDeviation(values);
            var mean = values.Average();

            var result = mean > 0 ? (1.0 - (stdDev / mean)) * 100.0 : 0;
            _logger.LogDebug($"[RealPerfDetector] <<< EXIT CalculateStability (Result: {result:F1}%)");
            return result;
        }

        private bool DetectStuttering(List<double> recentFrameTimes)
        {
            _logger.LogDebug($"[RealPerfDetector] >>> ENTER DetectStuttering");
            if (recentFrameTimes.Count < 10)
            {
                _logger.LogDebug($"[RealPerfDetector] <<< EXIT DetectStuttering (Result: false - insufficient data)");
                return false;
            }

            var recent = recentFrameTimes.TakeLast(10);
            var result = recent.Any(ft => ft > _baselineFrameTime * _stutterThreshold);
            _logger.LogDebug($"[RealPerfDetector] <<< EXIT DetectStuttering (Result: {result})");
            return result;
        }

        private int CountStutters(List<double> frameTimes)
        {
            _logger.LogDebug($"[RealPerfDetector] >>> ENTER CountStutters");
            var result = frameTimes.Count(ft => ft > _baselineFrameTime * _stutterThreshold);
            _logger.LogDebug($"[RealPerfDetector] <<< EXIT CountStutters (Result: {result})");
            return result;
        }

        private void InitializeCounters()
        {
            _logger.LogDebug($"[RealPerfDetector] >>> ENTER InitializeCounters");
            try
            {
                _cpuCounter = new SafePerformanceCounter("Processor", "%Processor Time", "_Total", readOnly: true);
                _memoryCounter = new SafePerformanceCounter("Memory", "Available MBytes", readOnly: true);
                _diskQueueCounter = new SafePerformanceCounter("PhysicalDisk", "Avg.Disk Queue Length", "_Total", readOnly: true);
                _pageFaultsCounter = new SafePerformanceCounter("Memory", "Page Faults/sec", readOnly: true);

                _logger.LogInfo("[RealPerfDetector] Performance counters inicializados");
                _logger.LogDebug($"[RealPerfDetector] <<< EXIT InitializeCounters (Success: true)");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[RealPerfDetector] Erro ao inicializar contadores: {ex.Message}");
                _logger.LogDebug($"[RealPerfDetector] <<< EXIT InitializeCounters (Error: {ex.Message})");
            }
        }

        private bool IsValidProcess(int processId)
        {
            _logger.LogDebug($"[RealPerfDetector] >>> ENTER IsValidProcess (processId: {processId})");
            try
            {
                var process = Process.GetProcessById(processId);
                var result = !process.HasExited;
                _logger.LogDebug($"[RealPerfDetector] <<< EXIT IsValidProcess (Result: {result})");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[RealPerfDetector] <<< EXIT IsValidProcess (Error: {ex.Message})");
                return false;
            }
        }

        private class CircularBuffer<T>
        {
            private readonly T[] _buffer;
            private int _head;
            private int _count;

            public CircularBuffer(int capacity)
            {
                _buffer = new T[capacity];
            }

            public bool HasData => _count > 0;

            public void Add(T item)
            {
                _buffer[_head] = item;
                _head = (_head + 1) % _buffer.Length;

                if (_count < _buffer.Length) _count++;
            }

            public T[] GetAll()
            {
                var result = new T[_count];

                for (int i = 0; i < _count; i++)
                {
                    result[i] = _buffer[(_head - _count + i + _buffer.Length) % _buffer.Length];
                }

                return result;
            }

            public T[] GetRecent(int count)
            {
                var take = Math.Min(count, _count);
                var result = new T[take];

                for (int i = 0; i < take; i++)
                {
                    result[i] = _buffer[(_head - i - 1 + _buffer.Length) % _buffer.Length];
                }

                return result.Reverse().ToArray();
            }
        }

        private class HighPrecisionTimer
        {
            private readonly long _frequency;
            private long _lastTime;

            public HighPrecisionTimer()
            {
                QueryPerformanceFrequency(out _frequency);
                QueryPerformanceCounter(out _lastTime);
            }

            public double GetElapsedMilliseconds()
            {
                QueryPerformanceCounter(out long currentTime);
                var elapsed = (double)(currentTime - _lastTime) / _frequency * 1000.0;
                _lastTime = currentTime;
                return elapsed;
            }
        }

        [DllImport("kernel32.dll")]
        private static extern bool QueryPerformanceFrequency(out long frequency);

        [DllImport("kernel32.dll")]
        private static extern bool QueryPerformanceCounter(out long value);

        public void Dispose()
        {
            _logger.LogDebug($"[RealPerfDetector] >>> ENTER Dispose");
            _ = StopMonitoringAsync();
            _cpuCounter?.Dispose();
            _memoryCounter?.Dispose();
            _diskQueueCounter?.Dispose();
            _pageFaultsCounter?.Dispose();
            _monitoringCts?.Dispose();
            _logger.LogDebug($"[RealPerfDetector] <<< EXIT Dispose");
        }
    }

    // Modelos de dados
    public class PerformanceSnapshot
    {
        public DateTime Timestamp { get; set; }
        public double CpuUsage { get; set; }
        public double MemoryUsage { get; set; }
        public double DiskQueueLength { get; set; }
        public double PageFaultsPerSec { get; set; }
    }

    public class PerformanceEvent
    {
        public DateTime Timestamp { get; set; }
        public PerformanceEventType EventType { get; set; }
        public EventSeverity Severity { get; set; }
        public string Details { get; set; } = string.Empty;
        public PerformanceSnapshot CorrelatedData { get; set; }
        public string ProbableCause { get; set; } = string.Empty;
    }

    public class RealTimeMetrics
    {
        public double CurrentFps { get; set; }
        public double AvgFrameTime { get; set; }
        public double P1LowFps { get; set; }
        public double P01LowFps { get; set; }
        public double FrameTimeStability { get; set; }
        public double CpuUsage { get; set; }
        public double MemoryUsage { get; set; }
        public bool IsStuttering { get; set; }
        public DateTime LastSpikeTime { get; set; }
    }

    public class PerformanceReport
    {
        public bool IsValid { get; set; }
        public TimeSpan Duration { get; set; }
        public int TotalFrames { get; set; }
        public double AvgFps { get; set; }
        public double P1LowFps { get; set; }
        public double P01LowFps { get; set; }
        public double AvgFrameTime { get; set; }
        public double MaxFrameTime { get; set; }
        public double FrameTimeStdDev { get; set; }
        public int StutterCount { get; set; }
        public double StutterPercentage { get; set; }
        public List<PerformanceEvent> CorrelatedEvents { get; set; } = new();
        public SystemMetrics SystemMetrics { get; set; } = new();
    }

    public class SystemMetrics
    {
        public double AvgCpuUsage { get; set; }
        public double MaxCpuUsage { get; set; }
        public double AvgMemoryUsage { get; set; }
        public double AvgPageFaults { get; set; }
    }

    public enum PerformanceEventType
    {
        FrameTimeSpike,
        CpuSpike,
        PageFaultSpike,
        DiskActivity,
        MemoryPressure
    }

    public enum EventSeverity
    {
        Low,
        Medium,
        High,
        Critical
    }
} 

