using System;
using VoltrisOptimizer.Core.Configuration;

namespace VoltrisOptimizer.Core.Orchestration
{
    /// <summary>
    /// Motor de regras baseado em evidências e limites de tolerância.
    /// Define se uma ação DEVE ser requisitada com base nas telemetrias.
    /// </summary>
    public sealed class PolicyEngine
    {
        private static readonly Lazy<PolicyEngine> _instance = new(() => new PolicyEngine());
        public static PolicyEngine Instance => _instance.Value;

        private PolicyEngine() { }

        /// <summary>
        /// Exemplo de avaliação de política: Aumentar a prioridade de um processo de jogo.
        /// Retorna true se as condições justificarem a ação.
        /// </summary>
        public bool ShouldElevatePriority(double currentFps, int currentCpuUsage, int currentGpuUsage)
        {
            // Regra: "Se FPS < 80 E CPU > 90% E GPU < 60% Então Scheduler -> Aumentar prioridade"
            if (currentFps < 80.0 && currentCpuUsage > 90 && currentGpuUsage < 60)
            {
                return true;
            }
            return false;
        }

        // Outras políticas configuráveis serão injetadas aqui futuramente.
    }
}
