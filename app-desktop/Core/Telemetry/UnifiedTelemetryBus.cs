using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.Configuration;

namespace VoltrisOptimizer.Core.Telemetry
{
    /// <summary>
    /// Barramento unificado de telemetria orientado a eventos (Event-Driven).
    /// Coleta dados multidimensionais (FPS Min, 1% Low, ISR, DPC, Context Switches)
    /// para alimentar o Motor de Aprendizado (AI) e o Observability Dashboard.
    /// </summary>
    public sealed class UnifiedTelemetryBus : IDisposable
    {
        private static readonly Lazy<UnifiedTelemetryBus> _instance = new(() => new UnifiedTelemetryBus());
        public static UnifiedTelemetryBus Instance => _instance.Value;

        // Estruturas lock-free para métricas do Sistema (Hardware/OS)
        private long _totalContextSwitches;
        private double _averageDpcLatencyMs;
        private double _averageIsrLatencyMs;
        private int _currentCpuUsagePercentage;
        private int _currentGpuUsagePercentage;
        private long _availableRamBytes;
        
        // Estruturas lock-free para métricas de Gaming/Performance
        private double _averageFps;
        private double _fps1PercentLow;
        private double _fps01PercentLow;
        private double _averageFrameTimeMs;
        private double _frameTimeVariance;
        private double _inputLatencyMs;

        // CancellationToken para os listeners
        private readonly CancellationTokenSource _cts = new();

        // Observabilidade Interna do VOLTRIS
        private readonly ConcurrentDictionary<string, ComponentMetrics> _internalMetrics = new(StringComparer.OrdinalIgnoreCase);

        private UnifiedTelemetryBus()
        {
        }

        public void Start()
        {
            if (!VoltrisFeatureFlags.Instance.UseUnifiedTelemetry)
                return;

            _ = Task.Run(() => StartEtwListenerAsync(_cts.Token), _cts.Token);
        }

        // --- Propriedades de Telemetria Multidimensional expostas para a IA ---

        public long ContextSwitches => Interlocked.Read(ref _totalContextSwitches);
        public double DpcLatencyMs => Interlocked.CompareExchange(ref _averageDpcLatencyMs, 0, 0); // safe read
        public double IsrLatencyMs => Interlocked.CompareExchange(ref _averageIsrLatencyMs, 0, 0);
        public int CpuUsage => Volatile.Read(ref _currentCpuUsagePercentage);
        public int GpuUsage => Volatile.Read(ref _currentGpuUsagePercentage);
        public long AvailableRamBytes => Interlocked.Read(ref _availableRamBytes);

        public double AverageFps => Interlocked.CompareExchange(ref _averageFps, 0, 0);
        public double Fps1PercentLow => Interlocked.CompareExchange(ref _fps1PercentLow, 0, 0);
        public double Fps01PercentLow => Interlocked.CompareExchange(ref _fps01PercentLow, 0, 0);
        public double AverageFrameTimeMs => Interlocked.CompareExchange(ref _averageFrameTimeMs, 0, 0);
        public double FrameTimeVariance => Interlocked.CompareExchange(ref _frameTimeVariance, 0, 0);
        public double InputLatencyMs => Interlocked.CompareExchange(ref _inputLatencyMs, 0, 0);

        private async Task StartEtwListenerAsync(CancellationToken token)
        {
            // O Event Tracing for Windows nos dará DPC, ISR, e Context Switches sem polling
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(1000, token);
            }
        }

        /// <summary>
        /// Atualiza métricas de GPU/Frametime recebidas por hooks de apresentação (DXGI/Vulkan)
        /// ou via providers parceiros como RTSS.
        /// </summary>
        public void UpdateGamingMetrics(double avgFps, double p1Low, double p01Low, double frameTime, double variance, double inputLag)
        {
            Interlocked.Exchange(ref _averageFps, avgFps);
            Interlocked.Exchange(ref _fps1PercentLow, p1Low);
            Interlocked.Exchange(ref _fps01PercentLow, p01Low);
            Interlocked.Exchange(ref _averageFrameTimeMs, frameTime);
            Interlocked.Exchange(ref _frameTimeVariance, variance);
            Interlocked.Exchange(ref _inputLatencyMs, inputLag);
        }

        /// <summary>
        /// Registra métricas de execução interna dos componentes (Observabilidade).
        /// </summary>
        public void RecordInternalMetric(string componentName, double executionTimeMs, bool success, bool rollback)
        {
            var metrics = _internalMetrics.GetOrAdd(componentName, _ => new ComponentMetrics());
            metrics.RecordExecution(executionTimeMs, success, rollback);
        }

        public ComponentMetrics? GetInternalMetrics(string componentName)
        {
            return _internalMetrics.TryGetValue(componentName, out var metrics) ? metrics : null;
        }

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
        }
    }

    /// <summary>
    /// Mantém as métricas internas operacionais de cada módulo para detectar gargalos do próprio Voltris.
    /// Exposto para o EnterpriseObservabilityDash.
    /// </summary>
    public class ComponentMetrics
    {
        private long _executionCount;
        private long _successCount;
        private long _rollbackCount;
        private double _totalExecutionTimeMs;

        public long ExecutionCount => Interlocked.Read(ref _executionCount);
        public long SuccessCount => Interlocked.Read(ref _successCount);
        public long RollbackCount => Interlocked.Read(ref _rollbackCount);
        public double AverageExecutionTimeMs => ExecutionCount == 0 ? 0 : _totalExecutionTimeMs / ExecutionCount;

        public void RecordExecution(double timeMs, bool success, bool rollback)
        {
            Interlocked.Increment(ref _executionCount);
            if (success) Interlocked.Increment(ref _successCount);
            if (rollback) Interlocked.Increment(ref _rollbackCount);
            
            double initialValue, computedValue;
            do
            {
                initialValue = _totalExecutionTimeMs;
                computedValue = initialValue + timeMs;
            }
            while (Interlocked.CompareExchange(ref _totalExecutionTimeMs, computedValue, initialValue) != initialValue);
        }
    }
}
