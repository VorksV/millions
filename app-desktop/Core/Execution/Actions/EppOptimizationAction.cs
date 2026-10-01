using System;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Core.Execution.Actions
{
    /// <summary>
    /// Exemplo de uma Ação Transacional.
    /// Encapsula a alteração de Energy Performance Preference (EPP).
    /// </summary>
    public sealed class EppOptimizationAction : OptimizationAction
    {
        private readonly int _targetEppValue;
        private int _previousEppValue;

        // O recurso alvo é "EPP", a prioridade é alta e o cooldown é de 100ms para evitar spam
        public EppOptimizationAction(int coreId, int eppValue, string actionName = "Set_Energy_Performance_Preference") 
            : base("CPU_EPP", actionName, ActionPriority.Normal, OptimizationContext.Global, TimeSpan.FromSeconds(30))
        {
            _targetEppValue = eppValue;
        }

        public override Task<bool> CheckPreconditionsAsync()
        {
            // Exemplo: Verificar se o hardware suporta EPP (Intel Speed Shift / AMD CPPC)
            return Task.FromResult(true);
        }

        public override Task TakeSnapshotAsync()
        {
            // Tirar snapshot do EPP atual via WMI ou Powrprof.dll
            _previousEppValue = 50; // valor fictício de estado anterior
            return Task.CompletedTask;
        }

        public override Task ApplyAsync()
        {
            // Chamada nativa para alterar o EPP para _targetEppValue
            return Task.CompletedTask;
        }

        public override Task RollbackAsync()
        {
            // Reverte o EPP para _previousEppValue
            return Task.CompletedTask;
        }

        public override string GetStateBefore() => $"EPP: {_previousEppValue}";
        public override string GetStateAfter() => $"EPP: {_targetEppValue}";
    }
}
