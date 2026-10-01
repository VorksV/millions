using System;
using System.Runtime.InteropServices;
using System.Diagnostics;
using VoltrisOptimizer.Services.Optimization.Unification;

namespace VoltrisOptimizer.Services.Optimization.Engines
{
    /// <summary>
    /// DSL 5.0 - Input Latency Engine
    /// Redução de jitter e atraso perceptível entre periférico e resposta do sistema.
    /// Usa TimerResolutionManager para participar do reference counting centralizado,
    /// evitando conflito com outros componentes que também gerenciam o timer.
    /// </summary>
    public class InputLatencyEngine : IDisposable
    {
        private readonly ILoggingService _logger;
        private readonly TimerResolutionManager _timerManager;
        private bool _timerRequested = false;

        public InputLatencyEngine(ILoggingService logger, ITimerResolutionService timerService)
        {
            _logger = logger;
            // Criar um TimerResolutionManager local que delega para o mesmo ITimerResolutionService singleton
            _timerManager = new TimerResolutionManager(logger, timerService);
        }

        public void Update(int foregroundPid)
        {
            try
            {
                // Timer de alta resolução deve estar SEMPRE ativo enquanto o DSL estiver rodando.
                // A condição anterior (foregroundPid > 0) causava o timer ficar em 15.6ms
                // quando nenhuma janela estava em foco — exatamente quando o sistema precisa
                // de scheduling preciso para responder ao próximo input.
                if (!_timerRequested)
                {
                    _timerManager.RequestHighPrecision("InputLatencyEngine");
                    _timerRequested = true;
                }
            }
            catch { }
        }

        public void Dispose()
        {
            if (_timerRequested)
            {
                _timerManager.ReleaseHighPrecision("InputLatencyEngine.Dispose");
                _timerRequested = false;
            }
            _timerManager.Dispose();
        }
    }
}
