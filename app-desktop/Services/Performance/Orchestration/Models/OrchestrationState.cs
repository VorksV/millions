using System;
using VoltrisOptimizer.Services.Performance.Decision.Models;

namespace VoltrisOptimizer.Services.Performance.Orchestration.Models
{
    /// <summary>
    /// Internal state snapshot of orchestrator.
    /// Tracks what was last executed to prevent thrashing and duplication.
    /// Lightweight, in-memory only.
    /// </summary>
    internal sealed class OrchestrationState
    {
        // Last executed decision
        public PerformanceDecision? LastDecision { get; private set; }
        public DateTime? LastExecutionTime { get; private set; }
        public OrchestrationStatus? LastStatus { get; private set; }

        // Active services tracking
        public bool IsGamerModeActive { get; private set; }
        public bool IsDynamicBalancerActive { get; private set; }
        public bool AreSystemTweaksApplied { get; private set; }

        // Stability window configuration
        public TimeSpan StabilityWindow { get; }

        public OrchestrationState(TimeSpan stabilityWindow)
        {
            StabilityWindow = stabilityWindow;
        }

        /// <summary>
        /// Checks if a new decision should be executed based on stability window and deduplication.
        /// </summary>
        public bool ShouldExecute(PerformanceDecision newDecision, out string reason)
        {
            // Rule 1: NoAction always skips
            if (newDecision.Type == DecisionType.NoAction)
            {
                reason = "Decision type is NoAction";
                return false;
            }

            // Rule 2: First execution always allowed
            if (LastDecision == null || LastExecutionTime == null)
            {
                reason = "First execution";
                return true;
            }

            // Rule 3: Deduplication - same decision type and intensity
            if (IsDuplicate(newDecision))
            {
                reason = $"Duplicate decision: {newDecision.Type} ({newDecision.Intensity})";
                return false;
            }

            // Rule 4: Stability window - check if enough time has passed
            var timeSinceLastExecution = DateTime.UtcNow - LastExecutionTime.Value;
            if (timeSinceLastExecution < StabilityWindow)
            {
                // Exception: Allow if confidence is significantly higher
                if (newDecision.Confidence > LastDecision.Confidence)
                {
                    reason = $"Higher confidence ({newDecision.Confidence} > {LastDecision.Confidence})";
                    return true;
                }

                reason = $"Stability window not elapsed ({timeSinceLastExecution.TotalSeconds:F0}s < {StabilityWindow.TotalSeconds:F0}s)";
                return false;
            }

            // Rule 5: Different decision type or intensity - allow
            reason = $"Different decision: {newDecision.Type} ({newDecision.Intensity})";
            return true;
        }

        /// <summary>
        /// Checks if the new decision is a duplicate of the last one.
        /// </summary>
        private bool IsDuplicate(PerformanceDecision newDecision)
        {
            if (LastDecision == null)
                return false;

            // Same type and intensity = duplicate
            return newDecision.Type == LastDecision.Type &&
                   newDecision.Intensity == LastDecision.Intensity;
        }

        /// <summary>
        /// Updates state after successful execution.
        /// </summary>
        public void RecordExecution(PerformanceDecision decision, OrchestrationStatus status)
        {
            LastDecision = decision;
            LastExecutionTime = DateTime.UtcNow;
            LastStatus = status;

            // Update active services based on decision type
            switch (decision.Type)
            {
                case DecisionType.EnableGamerMode:
                    IsGamerModeActive = true;
                    break;

                case DecisionType.EnableDynamicBalancing:
                    IsDynamicBalancerActive = true;
                    break;

                case DecisionType.ApplySystemTweaks:
                    AreSystemTweaksApplied = true;
                    break;

                case DecisionType.Hybrid:
                    IsGamerModeActive = true;
                    IsDynamicBalancerActive = true;
                    break;

                case DecisionType.Revert:
                    IsGamerModeActive = false;
                    IsDynamicBalancerActive = false;
                    AreSystemTweaksApplied = false;
                    break;
            }
        }

        /// <summary>
        /// Resets all state (for testing or manual override).
        /// </summary>
        public void Reset()
        {
            LastDecision = null;
            LastExecutionTime = null;
            LastStatus = null;
            IsGamerModeActive = false;
            IsDynamicBalancerActive = false;
            AreSystemTweaksApplied = false;
        }

        public override string ToString() =>
            $"State[Last: {LastDecision?.Type}, GamerMode: {IsGamerModeActive}, DLS: {IsDynamicBalancerActive}, Tweaks: {AreSystemTweaksApplied}]";
    }
}
