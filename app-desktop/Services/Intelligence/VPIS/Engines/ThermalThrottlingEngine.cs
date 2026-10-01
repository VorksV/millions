using System;
using System.Collections.Generic;
using VoltrisOptimizer.Core;
using VoltrisOptimizer.Services.Intelligence.VPIS.Models;

namespace VoltrisOptimizer.Services.Intelligence.VPIS.Engines
{
    public class ThermalThrottlingEngine : IVpisDiagnosticEngine
    {
        public string EngineName => "Thermal Throttling Forensics Engine";

        private string _targetProcess;
        private bool _isMonitoring;
        private int _consecutiveOverheatTicks;

        public void StartMonitoring(string processName)
        {
            _targetProcess = processName;
            _consecutiveOverheatTicks = 0;
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
            double cpuTemp = cache.CpuTemperature;
            double gpuTemp = cache.GpuTemperature;
            double maxTemp = Math.Max(cpuTemp, gpuTemp);

            if (double.IsNaN(maxTemp) || maxTemp <= 0)
                return null;

            bool isOverheating = maxTemp >= 90.0;

            if (isOverheating)
            {
                _consecutiveOverheatTicks++;
            }
            else
            {
                if (_consecutiveOverheatTicks > 0)
                    _consecutiveOverheatTicks--;
            }

            if (_consecutiveOverheatTicks >= 5)
            {
                _consecutiveOverheatTicks = 0;
                int confidence = CalculateConfidence(maxTemp);

                return new PerformanceInsightEvent
                {
                    ProcessName = _targetProcess,
                    Category = EventCategory.Thermal,
                    Severity = DiagnosticSeverity.Critical,
                    Confidence = confidence > 90 ? ConfidenceLevel.High : ConfidenceLevel.Medium,
                    ConfidenceScore = confidence,
                    Timestamp = DateTime.UtcNow,
                    Evidences = new List<string>
                    {
                        $"Temperatura máxima registrada: {Math.Round(maxTemp, 1)}°C",
                        $"Temperatura CPU: {Math.Round(cpuTemp, 1)}°C | GPU: {Math.Round(gpuTemp, 1)}°C"
                    },
                    Diagnosis = "Limitação Térmica (Thermal Throttling)",
                    Recommendations = new List<string>
                    {
                        "ative o controlador térmico preditivo do Voltris",
                        "limite o FPS máximo para reduzir a carga térmica",
                        "melhore a ventilação do gabinete ou notebook",
                        "verifique se as ventoinhas estão funcionando corretamente"
                    }
                };
            }

            return null;
        }

        private static int CalculateConfidence(double maxTemp)
        {
            int score = 0;
            if (maxTemp > 85) score += 30;
            if (maxTemp > 90) score += 30;
            if (maxTemp > 95) score += 30;
            return Math.Min(100, score + 10);
        }
    }
}
