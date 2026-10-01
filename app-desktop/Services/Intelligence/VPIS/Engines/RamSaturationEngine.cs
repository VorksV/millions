using System;
using System.Collections.Generic;
using VoltrisOptimizer.Core;
using VoltrisOptimizer.Services.Intelligence.VPIS.Models;

namespace VoltrisOptimizer.Services.Intelligence.VPIS.Engines
{
    public class RamSaturationEngine : IVpisDiagnosticEngine
    {
        public string EngineName => "RAM Saturation Forensics Engine";

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
            double ramPercent = cache.MemoryUsedPercent;
            double availableMb = cache.AvailableRamMb;

            if (double.IsNaN(ramPercent) || availableMb <= 0)
                return null;

            double totalMb = availableMb / (1.0 - (ramPercent / 100.0));
            double usedGb = (totalMb - availableMb) / 1024.0;
            double thresholdPercent = 90.0;

            if (ramPercent >= thresholdPercent)
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
                    Category = EventCategory.RAM,
                    Severity = DiagnosticSeverity.Critical,
                    Confidence = ConfidenceLevel.High,
                    ConfidenceScore = 95,
                    Timestamp = DateTime.UtcNow,
                    Evidences = new List<string>
                    {
                        $"Uso de RAM atingiu {Math.Round(ramPercent)}% ({Math.Round(usedGb, 1)} GB de ~{Math.Round(totalMb / 1024.0)} GB)",
                        $"Apenas {Math.Round(availableMb)} MB disponíveis — pressão extrema de memória"
                    },
                    Diagnosis = "Saturação de Memória RAM",
                    Recommendations = new List<string>
                    {
                        "fechar navegadores e aplicativos pesados antes de jogar",
                        "verificar se há vazamento de memória em aplicativos em segundo plano",
                        "considere aumentar a capacidade de RAM física do sistema"
                    }
                };
            }

            return null;
        }
    }
}
