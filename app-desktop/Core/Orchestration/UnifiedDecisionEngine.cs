using System;
using VoltrisOptimizer.Core.Configuration;
using VoltrisOptimizer.Core.Execution;

namespace VoltrisOptimizer.Core.Orchestration
{
    /// <summary>
    /// O Cérebro Único. Substitui o Brain V3, AntiStutter e as engines do Gamer Mode.
    /// Toma decisões através do PolicyEngine e encaminha para o OptimizationScheduler.
    /// </summary>
    public sealed class UnifiedDecisionEngine
    {
        private static readonly Lazy<UnifiedDecisionEngine> _instance = new(() => new UnifiedDecisionEngine());
        public static UnifiedDecisionEngine Instance => _instance.Value;

        private UnifiedDecisionEngine() { }

        /// <summary>
        /// Chamado ciclicamente pelo TelemetryBus ou quando eventos específicos chegam.
        /// </summary>
        public void EvaluateSystemState(double currentFps, int currentCpuUsage, int currentGpuUsage)
        {
            if (!VoltrisFeatureFlags.Instance.UseUnifiedDecisionEngine)
            {
                // Deixa os módulos legados tomarem as decisões
                return;
            }

            // Exemplo: Validação de política
            if (PolicyEngine.Instance.ShouldElevatePriority(currentFps, currentCpuUsage, currentGpuUsage))
            {
                // No futuro, isso criará uma "PriorityOptimizationAction"
                // OptimizationScheduler.Instance.RequestAction(new PriorityOptimizationAction(...));
            }
        }
    }
}
