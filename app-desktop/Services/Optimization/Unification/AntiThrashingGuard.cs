using System;
using System.Collections.Concurrent;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Optimization.Unification
{
    /// <summary>
    /// Nível Extremo Passo 1: Piloto do Anti-Thrashing Global Guard.
    /// Impede que o Kernel receba enxurradas de comandos de Power Throttling e Affinity em loop.
    /// Define cooldowns baseados em tempo real (Stopwatch concept).
    /// </summary>
    public class AntiThrashingGuard
    {
        private readonly ILoggingService _logger;
        // PID -> Momento da última alteração de EcoQoS ou Afinidade
        private readonly ConcurrentDictionary<int, DateTime> _lastThrottled = new();
        private readonly ConcurrentDictionary<int, DateTime> _lastAffinityChanged = new();
        
        // Cooldown global (Impede que o mesmo processo sofra "ping-pong" mais rápido do que 10s)
        private const int GLOBAL_COOLDOWN_SECONDS = 10;

        public AntiThrashingGuard(ILoggingService logger)
        {
            _logger = logger;
        }

        public bool AllowThrottle(int pid)
        {
            if (_lastThrottled.TryGetValue(pid, out DateTime lastTime))
            {
                if ((DateTime.UtcNow - lastTime).TotalSeconds < GLOBAL_COOLDOWN_SECONDS)
                {
                    _logger.LogWarning($"[AntiThrashingGuard] PID {pid} tentou ser throttlado novamente em menos de {GLOBAL_COOLDOWN_SECONDS}s. BLOCK.");
                    return false;
                }
            }
            return true;
        }

        public void RegisterThrottle(int pid)
        {
            _lastThrottled[pid] = DateTime.UtcNow;
        }

        public bool AllowAffinityChange(int pid)
        {
            if (_lastAffinityChanged.TryGetValue(pid, out DateTime lastTime))
            {
                if ((DateTime.UtcNow - lastTime).TotalSeconds < GLOBAL_COOLDOWN_SECONDS)
                {
                    _logger.LogWarning($"[AntiThrashingGuard] Afinidade do PID {pid} tentou ser alterada novamente em menos de {GLOBAL_COOLDOWN_SECONDS}s. BLOCK.");
                    return false;
                }
            }
            return true;
        }

        public void RegisterAffinityChange(int pid)
        {
            _lastAffinityChanged[pid] = DateTime.UtcNow;
        }
    }
}
