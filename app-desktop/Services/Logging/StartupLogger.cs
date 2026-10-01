using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace VoltrisOptimizer.Services.Logging
{
    /// <summary>
    /// StartupLogger — rastreia o tempo de cada etapa do startup.
    /// Formato: [Timestamp] [Service] [Level] [Action] [Duration] [Result]
    /// Substitui os centenas de LogInfo de debug por métricas estruturadas.
    /// </summary>
    public class StartupLogger
    {
        private readonly ILoggingService _logger;
        private readonly Stopwatch _totalSw = Stopwatch.StartNew();
        private readonly Dictionary<string, long> _stepDurations = new();
        private Stopwatch? _stepSw;
        private string? _currentStep;

        public StartupLogger(ILoggingService logger)
        {
            _logger = logger;
        }

        public void BeginStep(string stepName)
        {
            _currentStep = stepName;
            _stepSw = Stopwatch.StartNew();
        }

        public void EndStep(bool success = true, string? detail = null)
        {
            if (_currentStep == null || _stepSw == null) return;
            
            _stepSw.Stop();
            _stepDurations[_currentStep] = _stepSw.ElapsedMilliseconds;
            
            var status = success ? "[OK]" : "[FAIL]";
            var detailStr = detail != null ? $" | {detail}" : "";
            _logger.LogInfo($"[Startup] {status} {_currentStep} — {_stepSw.ElapsedMilliseconds}ms{detailStr}");
            
            _currentStep = null;
            _stepSw = null;
        }

        public void LogSummary()
        {
            _totalSw.Stop();
            _logger.LogSuccess($"[Startup] ═══ INICIALIZAÇÃO COMPLETA em {_totalSw.ElapsedMilliseconds}ms ═══");
            foreach (var (step, ms) in _stepDurations)
            {
                var indicator = ms > 1000 ? "[WARN]" : ms > 500 ? "[SLOW]" : "[OK]";
                _logger.LogInfo($"[Startup]   {indicator} {step}: {ms}ms");
            }
        }
    }
}
