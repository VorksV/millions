using System;
using System.Collections.Generic;
using VoltrisOptimizer.Core;
using VoltrisOptimizer.Services.Intelligence.VPIS.Models;

namespace VoltrisOptimizer.Services.Intelligence.VPIS.Engines
{
    public class CpuBottleneckEngine : IVpisDiagnosticEngine
    {
        public string EngineName => "CPU Bottleneck Forensics Engine";

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
            double cpuLoad = cache.CpuPercent;
            double gpuLoad = cache.GpuUsagePercent;

            if (double.IsNaN(cpuLoad) || double.IsNaN(gpuLoad))
                return null;

            if (cpuLoad >= 85.0 && gpuLoad <= 70.0)
            {
                _consecutiveBottleneckTicks++;
            }
            else
            {
                if (_consecutiveBottleneckTicks > 0)
                    _consecutiveBottleneckTicks--;
            }

            if (_consecutiveBottleneckTicks >= 10)
            {
                _consecutiveBottleneckTicks = 0;

                return new PerformanceInsightEvent
                {
                    ProcessName = _targetProcess,
                    Category = EventCategory.CPU,
                    Severity = DiagnosticSeverity.Warning,
                    Confidence = ConfidenceLevel.High,
                    ConfidenceScore = 90,
                    Timestamp = DateTime.UtcNow,
                    Evidences = new List<string>
                    {
                        $"CPU permaneceu acima de {Math.Round(cpuLoad)}% de uso",
                        $"GPU com ociosidade relativa de apenas {Math.Round(gpuLoad)}% de uso"
                    },
                    Diagnosis = "Gargalo de Processador (CPU Bound)",
                    Recommendations = new List<string>
                    {
                        "feche aplicativos em segundo plano pesados (navegadores, Discord, etc.)",
                        "aumente a resolução ou qualidade gráfica para transferir carga para a GPU",
                        "verifique se há processos consumindo CPU desnecessariamente"
                    }
                };
            }

            return null;
        }
    }
}
