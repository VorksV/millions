using System;
using System.Collections.Generic;
using VoltrisOptimizer.Core;
using VoltrisOptimizer.Services.Intelligence.VPIS.Models;

namespace VoltrisOptimizer.Services.Intelligence.VPIS.Engines
{
    public class VramSaturationEngine : IVpisDiagnosticEngine
    {
        public string EngineName => "VRAM Saturation Forensics Engine";

        private string _targetProcess;
        private bool _isMonitoring;
        private int _consecutiveSaturatedTicks;

        public void StartMonitoring(string processName)
        {
            _targetProcess = processName;
            _consecutiveSaturatedTicks = 0;
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
            double vramUsedGb = cache.GpuVramUsedGb;
            double vramTotalGb = cache.GpuVramTotalGb;

            if (vramTotalGb <= 0 || double.IsNaN(vramUsedGb) || double.IsNaN(vramTotalGb))
                return null;

            double vramPercent = (vramUsedGb / vramTotalGb) * 100.0;

            if (vramPercent >= 95.0)
            {
                _consecutiveSaturatedTicks++;
            }
            else
            {
                if (_consecutiveSaturatedTicks > 0)
                    _consecutiveSaturatedTicks--;
            }

            if (_consecutiveSaturatedTicks >= 8)
            {
                _consecutiveSaturatedTicks = 0;

                return new PerformanceInsightEvent
                {
                    ProcessName = _targetProcess,
                    Category = EventCategory.VRAM,
                    Severity = DiagnosticSeverity.Warning,
                    Confidence = ConfidenceLevel.High,
                    ConfidenceScore = 88,
                    Timestamp = DateTime.UtcNow,
                    Evidences = new List<string>
                    {
                        $"VRAM em {Math.Round(vramPercent)}% de uso ({Math.Round(vramUsedGb, 1)} GB / {Math.Round(vramTotalGb, 1)} GB)"
                    },
                    Diagnosis = "Saturação de VRAM",
                    Recommendations = new List<string>
                    {
                        "reduza a qualidade das texturas nas configurações do jogo",
                        "ative DLSS/FSR/XeSS para reduzir o consumo de VRAM",
                        "feche navegadores com aceleração de hardware ativada"
                    }
                };
            }

            return null;
        }
    }
}
