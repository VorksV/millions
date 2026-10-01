using System;
using System.Collections.Generic;
using VoltrisOptimizer.Core;
using VoltrisOptimizer.Services.Intelligence.VPIS.Models;

namespace VoltrisOptimizer.Services.Intelligence.VPIS.Engines
{
    public class FrametimeSpikeEngine : IVpisDiagnosticEngine
    {
        public string EngineName => "Frametime Spike Forensics Engine";

        private string _targetProcess;
        private bool _isMonitoring;
        private double _lastFrametimeMs = 16.6;
        private int _spikeCount;
        private double _worstSpike;

        public void StartMonitoring(string processName)
        {
            _targetProcess = processName;
            _spikeCount = 0;
            _worstSpike = 0;
            _isMonitoring = true;
        }

        public void StopMonitoring()
        {
            _isMonitoring = false;
        }

        public PerformanceInsightEvent AnalyzeTick()
        {
            if (!_isMonitoring) return null;

            var cache = SystemMetricsCache.Instance;
            if (!cache.FpsAvailable) return null;

            double currentMs = cache.FpsAverageFrametimeMs;
            if (currentMs <= 0)
            {
                _lastFrametimeMs = 16.6;
                return null;
            }

            if (_lastFrametimeMs > 0 && currentMs > _lastFrametimeMs * 2.5 && currentMs > 50.0)
            {
                _spikeCount++;
                if (currentMs > _worstSpike)
                    _worstSpike = currentMs;
            }

            _lastFrametimeMs = currentMs;

            if (_spikeCount >= 15)
            {
                _spikeCount = 0;
                double recSpike = _worstSpike;
                _worstSpike = 0;

                return new PerformanceInsightEvent
                {
                    ProcessName = _targetProcess,
                    Category = EventCategory.Unknown,
                    Severity = DiagnosticSeverity.Warning,
                    Confidence = ConfidenceLevel.Medium,
                    ConfidenceScore = 85,
                    Timestamp = DateTime.UtcNow,
                    Evidences = new List<string>
                    {
                        $"Múltiplos picos de frametime (stuttering) detectados",
                        $"Pior pico: {Math.Round(recSpike)} ms de travamento"
                    },
                    Diagnosis = "Instabilidade de Frametime (Micro Stuttering)",
                    Recommendations = new List<string>
                    {
                        "ative V-Sync ou limite o FPS máximo",
                        "desative overlays (Discord, GeForce Experience, Steam)",
                        "verifique se há processos em segundo plano causando picos"
                    }
                };
            }

            return null;
        }
    }
}
