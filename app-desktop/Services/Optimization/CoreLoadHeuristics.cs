using System;
using System.Collections.Generic;
using System.Linq;

namespace VoltrisOptimizer.Services.Optimization
{
    public enum CoreDecision
    {
        None,
        ModerateThrottle,
        StrongThrottle,
        Release
    }

    /// <summary>
    /// Heurísticas de carga de CPU usando API nativa GetSystemTimes (mesmo que Task Manager).
    /// REESCRITO: Removidos PerformanceCounters por core (extremamente pesados).
    /// Agora usa NativeSystemMetrics para leitura global ultra leve (microsegundos).
    /// </summary>
    public class CoreLoadHeuristics : IDisposable
    {
        private readonly Queue<double> _history = new();
        private const int MaxHistory = 5;
        private const int DebounceMs = 600;
        private DateTime _lastDecisionAt = DateTime.MinValue;
        private readonly VoltrisOptimizer.Services.Optimization.Providers.ICpuCoreLoadProvider? _provider;
        private readonly Helpers.NativeSystemMetrics _nativeMetrics = new();

        public CoreLoadHeuristics(VoltrisOptimizer.Services.Optimization.Providers.ICpuCoreLoadProvider? provider = null)
        {
            _provider = provider;
            // Warm-up da primeira leitura
            _nativeMetrics.GetCpuUsage();
        }

        public CoreDecision Sample()
        {
            if (_provider != null)
            {
                var loads = _provider.GetCoreLoads();
                if (loads.Length == 0) return CoreDecision.None;

                double maxVal = loads.Max();
                int countAbove55 = loads.Count(l => l > 55.0);
                bool allUnder20 = loads.All(l => l < 20.0);

                _history.Enqueue(maxVal);
                while (_history.Count > MaxHistory) _history.Dequeue();

                var now = DateTime.Now;
                if ((now - _lastDecisionAt).TotalMilliseconds < DebounceMs) return CoreDecision.None;

                if (maxVal > 70.0)
                {
                    int samplesAbove70 = _history.Count(x => x > 70.0);
                    if (samplesAbove70 >= 2)
                    {
                        _lastDecisionAt = now;
                        return CoreDecision.StrongThrottle;
                    }
                }
                
                if (countAbove55 >= 2)
                {
                    _lastDecisionAt = now;
                    return CoreDecision.ModerateThrottle;
                }

                if (allUnder20)
                {
                    _lastDecisionAt = now;
                    return CoreDecision.Release;
                }

                return CoreDecision.None;
            }
            else
            {
                // API nativa: microsegundos, zero alocação, mesmo valor do Task Manager
                double cpuLoad = _nativeMetrics.GetCpuUsage();
                _history.Enqueue(cpuLoad);
                while (_history.Count > MaxHistory) _history.Dequeue();

                var now = DateTime.Now;
                if ((now - _lastDecisionAt).TotalMilliseconds < DebounceMs) return CoreDecision.None;

                var avg = _history.Average();

                if (avg > 70.0 && _history.Count >= 2) { _lastDecisionAt = now; return CoreDecision.StrongThrottle; }
                if (avg > 55.0) { _lastDecisionAt = now; return CoreDecision.ModerateThrottle; }
                if (avg < 20.0) { _lastDecisionAt = now; return CoreDecision.Release; }
                return CoreDecision.None;
            }
        }

        public void Dispose() { }
    }
}
