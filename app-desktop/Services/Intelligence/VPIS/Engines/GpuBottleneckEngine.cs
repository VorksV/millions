using System;
using System.Collections.Generic;
using VoltrisOptimizer.Core;
using VoltrisOptimizer.Services.Intelligence.VPIS.Models;

namespace VoltrisOptimizer.Services.Intelligence.VPIS.Engines
{
    public class GpuBottleneckEngine : IVpisDiagnosticEngine
    {
        public string EngineName => "GPU Bottleneck Forensics Engine";

        private string _targetProcess;
        private bool _isMonitoring;
        private int _consecutiveBottleneckTicks;

        public void StartMonitoring(string processName)
        {
            _targetProcess = processName;
            _consecutiveBottleneckTicks = 0;
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
            double gpuLoad = cache.GpuUsagePercent;
            double cpuLoad = cache.CpuPercent;

            if (double.IsNaN(gpuLoad) || double.IsNaN(cpuLoad))
                return null;

            if (gpuLoad >= 95.0 && cpuLoad <= 65.0)
            {
                _consecutiveBottleneckTicks++;
            }
            else
            {
                if (_consecutiveBottleneckTicks > 0)
                    _consecutiveBottleneckTicks--;
            }

            if (_consecutiveBottleneckTicks >= 12)
            {
                _consecutiveBottleneckTicks = 0;

                return new PerformanceInsightEvent
                {
                    ProcessName = _targetProcess,
                    Category = EventCategory.GPU,
                    Severity = DiagnosticSeverity.Warning,
                    Confidence = ConfidenceLevel.Medium,
                    ConfidenceScore = 80,
                    Timestamp = DateTime.UtcNow,
                    Evidences = new List<string>
                    {
                        $"GPU operou no limite em {Math.Round(gpuLoad)}% de uso",
                        $"CPU com folga relativa de apenas {Math.Round(cpuLoad)}% de uso"
                    },
                    Diagnosis = "Sobrecarga Gráfica (GPU Bound)",
                    Recommendations = new List<string>
                    {
                        "reduza a qualidade de texturas, sombras ou reflexos",
                        "ative tecnologias de upscaling (DLSS, FSR, XeSS)",
                        "reduza a resolução do jogo se necessário"
                    }
                };
            }

            return null;
        }
    }
}
