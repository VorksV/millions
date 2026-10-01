using System;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.Validation;

namespace VoltrisOptimizer.Core.Execution
{
    public enum ActionPriority
    {
        Idle = 0,
        Low = 1,
        Normal = 2,
        High = 3,
        Critical = 4
    }

    public enum OptimizationContext
    {
        Global = 0,
        Gaming = 1,
        VirtualMachine = 2,
        Streaming = 3,
        Rendering = 4,
        Benchmark = 5,
        BatterySaver = 6
    }

    /// <summary>
    /// Representa uma transação completa de otimização (Snapshot -> Apply -> Validate -> Commit/Rollback).
    /// </summary>
    public abstract class OptimizationAction
    {
        public string TargetResource { get; }
        public string ActionName { get; }
        public ActionPriority Priority { get; }
        public OptimizationContext Context { get; }
        public TimeSpan Cooldown { get; }
        
        /// <summary>
        /// O estado desejado que essa ação aplicará. Usado pelo Scheduler para consolidação/conflito.
        /// Ex: "0" para EPP, ou "True" para GameMode.
        /// </summary>
        public string DesiredState { get; }

        protected OptimizationAction(
            string targetResource, 
            string actionName, 
            ActionPriority priority, 
            OptimizationContext context, 
            TimeSpan cooldown, 
            string desiredState = null)
        {
            TargetResource = targetResource;
            ActionName = actionName;
            Priority = priority;
            Context = context;
            Cooldown = cooldown;
            DesiredState = desiredState ?? string.Empty;
        }

        public abstract Task<bool> CheckPreconditionsAsync();
        public abstract Task TakeSnapshotAsync();
        public abstract Task ApplyAsync();
        public abstract Task RollbackAsync();

        public abstract string GetStateBefore();
        public abstract string GetStateAfter();

        public virtual Task<ValidationMetrics?> CollectPreMetricsAsync() => Task.FromResult<ValidationMetrics?>(null);
        public virtual Task<ValidationMetrics?> CollectPostMetricsAsync() => Task.FromResult<ValidationMetrics?>(null);
    }
}
