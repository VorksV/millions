using System;
using System.Threading;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Optimization.Unification
{
    public enum SystemState
    {
        Idle,
        Interactive,
        HeavyApp,
        Gaming,
        IOHeavy,
        BatteryMode,
        ThermalProtection
    }

    public class SystemBehaviorStateMachine
    {
        private SystemState _currentState = SystemState.Idle;
        private readonly object _lock = new object();
        private readonly ILoggingService _logger;

        public event EventHandler<SystemState>? StateChanged;

        public SystemBehaviorStateMachine(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public SystemState CurrentState
        {
            get { lock (_lock) { return _currentState; } }
        }

        public void TransitionTo(SystemState newState, string reason)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            lock (_lock)
            {
                if (_currentState == newState) return;
                
                // Regras de precedência (Thermal e Battery > Gaming)
                if ((_currentState == SystemState.ThermalProtection || _currentState == SystemState.BatteryMode) && 
                    newState == SystemState.Gaming)
                {
                    _logger.LogWarning($"[StateMachine] Transição para {newState} bloqueada pelo estado atual {_currentState}. Motivo: {reason}");
                    return;
                }

                _logger.LogInfo($"[StateMachine] Transition: {_currentState} -> {newState}. Motivo: {reason}");
                _currentState = newState;
            }
            StateChanged?.Invoke(this, newState);
            sw.Stop();
            
            if (sw.ElapsedMilliseconds > 100)
            {
                _logger.LogWarning($"[StateMachine] Aviso de Performance: Mudança para {newState} levou {sw.ElapsedMilliseconds}ms (> 100ms target).");
            }
        }
    }
}
