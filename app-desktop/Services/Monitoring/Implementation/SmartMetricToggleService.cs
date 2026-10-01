using System;
using System.Collections.Generic;
using VoltrisOptimizer.Services.Monitoring.Interfaces;

namespace VoltrisOptimizer.Services.Monitoring.Implementation
{
    public sealed class SmartMetricToggleService : IMetricToggleService
    {
        private readonly ILoggingService? _logger;
        private readonly Dictionary<MetricType, bool> _states = new()
        {
            [MetricType.CpuUsage] = true,
            [MetricType.CpuTemperature] = true,
            [MetricType.CpuClock] = false,
            [MetricType.GpuUsage] = true,
            [MetricType.GpuTemperature] = true,
            [MetricType.GpuClock] = false,
            [MetricType.GpuMemoryClock] = false,
            [MetricType.RamUsage] = true,
            [MetricType.VramUsage] = true,
            [MetricType.DiskUsage] = false,
            [MetricType.Fps] = true,
            [MetricType.FrameTime] = true,
            [MetricType.InputLatency] = false};

        private readonly object _lock = new();

        public event EventHandler<MetricType>? MetricToggled;

        public SmartMetricToggleService(ILoggingService? logger = null)
        {
            _logger = logger;
            _logger?.LogInfo("[SMART-METRIC] ✔️ SmartMetricToggleService inicializado com defaults profissionais");
        }

        public bool IsEnabled(MetricType metric)
        {
            lock (_lock)
            {
                return _states.TryGetValue(metric, out var enabled) && enabled;
            }
        }

        public void SetEnabled(MetricType metric, bool enabled)
        {
            lock (_lock)
            {
                if (_states.TryGetValue(metric, out var current) && current == enabled)
                    return;
                _states[metric] = enabled;
            }

            _logger?.LogInfo($"[SMART-METRIC] {(enabled ? "✔️" : "❌")} {metric}: {(enabled ? "ativado" : "desativado")} — coleta de dados {(enabled ? "iniciada" : "interrompida")}");
            
            // Log adicional: dump de todos os estados
            lock (_lock)
            {
                _logger?.LogInfo($"[SMART-METRIC] State dump: Fps={_states[MetricType.Fps]}, FrameTime={_states[MetricType.FrameTime]}, CpuClock={_states[MetricType.CpuClock]}, GpuClock={_states[MetricType.GpuClock]}, InputLatency={_states[MetricType.InputLatency]}");
            }
            
            MetricToggled?.Invoke(this, metric);
        }

        public IReadOnlyDictionary<MetricType, bool> GetAllStates()
        {
            lock (_lock)
            {
                return new Dictionary<MetricType, bool>(_states);
            }
        }

        public bool IsAnyGpuMetricEnabled
        {
            get
            {
                lock (_lock)
                {
                    return _states[MetricType.GpuUsage] ||
                           _states[MetricType.GpuTemperature] ||
                           _states[MetricType.GpuClock] ||
                           _states[MetricType.GpuMemoryClock] ||
                           _states[MetricType.VramUsage];
                }
            }
        }

        public bool IsAnyCpuMetricEnabled
        {
            get
            {
                lock (_lock)
                {
                    return _states[MetricType.CpuUsage] ||
                           _states[MetricType.CpuTemperature] ||
                           _states[MetricType.CpuClock];
                }
            }
        }

        public bool IsAnyMemoryMetricEnabled
        {
            get
            {
                lock (_lock)
                {
                    return _states[MetricType.RamUsage] ||
                           _states[MetricType.VramUsage];
                }
            }
        }

        public bool IsFpsMetricEnabled
        {
            get
            {
                lock (_lock)
                {
                    return _states[MetricType.Fps] ||
                           _states[MetricType.FrameTime];
                }
            }
        }
    }
}
