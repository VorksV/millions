using System;
using VoltrisOptimizer.Services.Performance.CpuTuning.Models;

namespace VoltrisOptimizer.Services.Performance.CpuTuning
{
    /// <summary>
    /// Main CPU Performance Tuning Module
    /// Operates EXCLUSIVELY when Gamer Mode is active
    /// </summary>
    public interface ICpuTuningModule
    {
        /// <summary>
        /// Activate CPU tuning (called when Gamer Mode activates)
        /// </summary>
        void Activate();
        
        /// <summary>
        /// Deactivate CPU tuning and restore original settings (called when Gamer Mode deactivates)
        /// </summary>
        void Deactivate();
        
        /// <summary>
        /// Check if module is currently active
        /// </summary>
        bool IsActive { get; }
        
        /// <summary>
        /// Get current CPU metrics
        /// </summary>
        CpuMetrics GetCurrentMetrics();
        
        /// <summary>
        /// Get current thermal state
        /// </summary>
        ThermalState GetThermalState();
        
        /// <summary>
        /// Get hardware capabilities
        /// </summary>
        CpuTuningCapabilities GetCpuTuningCapabilities();
        
        /// <summary>
        /// Get current workload classification
        /// </summary>
        WorkloadType GetCurrentWorkload();
        
        /// <summary>
        /// Event fired when tuning is activated
        /// </summary>
        event EventHandler? TuningActivated;
        
        /// <summary>
        /// Event fired when tuning is deactivated
        /// </summary>
        event EventHandler? TuningDeactivated;
        
        /// <summary>
        /// Event fired when performance metrics are updated
        /// </summary>
        event EventHandler<PerformanceMetricsEventArgs>? MetricsUpdated;
    }
    
    public class PerformanceMetricsEventArgs : EventArgs
    {
        public CpuMetrics Metrics { get; set; } = new();
        public WorkloadType Workload { get; set; }
        public ThermalState ThermalState { get; set; } = new();

        public PerformanceMetricsEventArgs() { }
        public PerformanceMetricsEventArgs(CpuMetrics metrics)
        {
            Metrics = metrics;
        }
    }
}
