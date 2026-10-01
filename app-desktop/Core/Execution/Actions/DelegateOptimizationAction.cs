using System;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.Validation;

namespace VoltrisOptimizer.Core.Execution.Actions
{
    /// <summary>
    /// Um wrapper universal para converter qualquer função legada em uma Ação Transacional.
    /// Permite migrar milhares de linhas de código de serviços antigos para o novo UnifiedDecisionEngine
    /// sem reescrever a lógica interna de otimização de forma imediata.
    /// </summary>
    public sealed class DelegateOptimizationAction : OptimizationAction
    {
        private readonly Func<Task> _applyFunc;
        private readonly Func<Task> _rollbackFunc;
        private readonly string _stateBeforeDesc;
        private readonly string _stateAfterDesc;

        public DelegateOptimizationAction(
            string targetResource,
            string actionName,
            ActionPriority priority,
            TimeSpan cooldown,
            Func<Task> applyFunc,
            Func<Task> rollbackFunc,
            string stateBeforeDesc = "Legacy State",
            string stateAfterDesc = "Optimized State")
            : base(targetResource, actionName, priority, OptimizationContext.Global, cooldown)
        {
            _applyFunc = applyFunc ?? throw new ArgumentNullException(nameof(applyFunc));
            _rollbackFunc = rollbackFunc ?? throw new ArgumentNullException(nameof(rollbackFunc));
            _stateBeforeDesc = stateBeforeDesc;
            _stateAfterDesc = stateAfterDesc;
        }

        public override Task<bool> CheckPreconditionsAsync()
        {
            return Task.FromResult(true); // O legado já faz seus checks
        }

        public override Task TakeSnapshotAsync()
        {
            return Task.CompletedTask; // O legado normalmente já tira snapshots internamente
        }

        public override async Task ApplyAsync()
        {
            await _applyFunc();
        }

        public override async Task RollbackAsync()
        {
            await _rollbackFunc();
        }

        public override string GetStateBefore() => _stateBeforeDesc;
        public override string GetStateAfter() => _stateAfterDesc;

        public override Task<ValidationMetrics?> CollectPreMetricsAsync()
        {
            return Task.FromResult<ValidationMetrics?>(null);
        }

        public override Task<ValidationMetrics?> CollectPostMetricsAsync()
        {
            return Task.FromResult<ValidationMetrics?>(null);
        }
    }
}
