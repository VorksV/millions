using System;

namespace VoltrisOptimizer.Services.Performance.CpuTuning
{
    /// <summary>
    /// Stability watchdog - monitors system stability and triggers rollback.
    /// Monitors backend health, MSR/MMIO operations, thermal protection, and rollback events.
    /// </summary>
    public interface IStabilityWatchdog
    {
        /// <summary>
        /// Initialize watchdog
        /// </summary>
        void Initialize();
        
        /// <summary>
        /// Record successful tuning session
        /// </summary>
        void RecordSuccessfulSession();
        
        /// <summary>
        /// Check if tuning should be disabled due to instability
        /// </summary>
        bool ShouldDisableTuning();
        
        /// <summary>
        /// Record crash or instability event
        /// </summary>
        void RecordCrash(string reason);
        
        /// <summary>
        /// Reset failure counter (manual user action)
        /// </summary>
        void ResetFailureCounter();
        
        /// <summary>
        /// Get current failure count
        /// </summary>
        int GetFailureCount();
        
        /// <summary>
        /// Record backend failure
        /// </summary>
        void RecordBackendFailure(string reason, string? backendName = null);
        
        /// <summary>
        /// Record MSR access failure
        /// </summary>
        void RecordMsrFailure(string msrAddress, string reason);
        
        /// <summary>
        /// Record MMIO access failure
        /// </summary>
        void RecordMmioFailure(ulong address, string reason);
        
        /// <summary>
        /// Record thermal protection trigger
        /// </summary>
        void RecordThermalTrigger(string reason, double temperature);
        
        /// <summary>
        /// Record rollback event
        /// </summary>
        void RecordRollback(string reason);
        
        /// <summary>
        /// Get detailed status of all failure counters
        /// </summary>
        (int failureCount, int backendFailures, int msrFailures, int mmioFailures, int thermalTriggers, int rollbacks) GetDetailedStatus();
        
        /// <summary>
        /// Check if backend is unstable
        /// </summary>
        bool IsBackendUnstable();
        
        /// <summary>
        /// Check if MSR access is unstable
        /// </summary>
        bool IsMsrAccessUnstable();
        
        /// <summary>
        /// Check if MMIO access is unstable
        /// </summary>
        bool IsMmioAccessUnstable();
        
        /// <summary>
        /// Event fired when rollback is required
        /// </summary>
        event EventHandler<RollbackEventArgs>? RollbackRequired;
    }
    
    public class RollbackEventArgs : EventArgs
    {
        public string Reason { get; set; } = string.Empty;
        public int FailureCount { get; set; }
    }
}
