using VoltrisOptimizer.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace VoltrisOptimizer.Services.Optimization.Providers
{
    /// <summary>
    /// GPU load provider com cache agressivo.
    /// 
    /// PERFORMANCE FIX: GPU Engine PerformanceCounters são extremamente caros (PDH).
    /// Em sistemas NVIDIA/AMD com muitas instâncias 3D, podem criar 15-20+ objetos.
    /// 
    /// ESTRATÉGIA:
    ///   - Cache de 30 segundos: GPU % não muda rápido o suficiente para justificar
    ///     reads por ciclo do DLS (que já roda a cada 2-3s).
    ///   - Inicialização lazy: contadores criados apenas no primeiro GetGpuUtilization().
    ///   - Se a criação falhar (ex: sistema sem GPU dedicada), retorna 0 silenciosamente.
    /// </summary>
    public class WindowsGpuLoadProvider : IGpuLoadProvider, IDisposable
    {
        private readonly List<SafePerformanceCounter> _gpuCounters = new();
        private DateTime _lastCheck = DateTime.MinValue;
        private double _lastValue = 0;
        private bool _disposed;
        private bool _initialized;

        // Cache agressivo de 30s — GPU % em idle raramente muda de forma relevante
        private const double CacheSeconds = 30.0;

        public WindowsGpuLoadProvider() { }

        private void EnsureInitialized()
        {
            if (_initialized || _disposed) return;
            _initialized = true;
            try
            {
                var category = new SafePerformanceCounterCategory("GPU Engine");
                var instanceNames = category.GetInstanceNames();

                // Limitar a no máximo 4 instâncias 3D para conter overhead
                var filtered = instanceNames
                    .Where(n => n.EndsWith("engtype_3D"))
                    .Take(4)
                    .ToArray();

                foreach (var name in filtered)
                    _gpuCounters.Add(new SafePerformanceCounter("GPU Engine", "Utilization Percentage", name, true));

                // Warmup — primeira leitura sempre retorna 0
                foreach (var c in _gpuCounters) { try { c.NextValue(); } catch { } }
            }
            catch { /* GPU counters não disponíveis — silenciosamente retorna 0 */ }
        }

        public double GetGpuUtilization()
        {
            if (_disposed) return 0;

            // Cache agressivo: 30 segundos
            if ((DateTime.Now - _lastCheck).TotalSeconds < CacheSeconds)
                return _lastValue;

            EnsureInitialized();

            if (_gpuCounters.Count == 0)
            {
                _lastCheck = DateTime.Now;
                return 0;
            }

            double max = 0;
            foreach (var counter in _gpuCounters)
            {
                try { float val = counter.NextValue(); if (val > max) max = val; }
                catch { }
            }

            _lastValue = max;
            _lastCheck = DateTime.Now;
            return max;
        }

        public void Dispose()
        {
            if (_disposed) return;
            foreach (var c in _gpuCounters) { try { c.Dispose(); } catch { } }
            _gpuCounters.Clear();
            _disposed = true;
        }
    }
}

