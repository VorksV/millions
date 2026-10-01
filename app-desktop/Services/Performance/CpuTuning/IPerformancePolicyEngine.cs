using VoltrisOptimizer.Services.Performance.CpuTuning.Models;

namespace VoltrisOptimizer.Services.Performance.CpuTuning
{
    /// <summary>
    /// Intelligent performance policy engine
    /// Makes tuning decisions based on workload, thermal state, and machine class
    /// </summary>
    public interface IPerformancePolicyEngine
    {
        /// <summary>
        /// Classify current workload
        /// </summary>
        WorkloadType ClassifyWorkload(CpuMetrics metrics);
        
        /// <summary>
        /// Determine optimal Speed Shift EPP value
        /// </summary>
        int DetermineOptimalEPP(WorkloadType workload, ThermalState thermal, MachineClass machineClass, PlatformType platform);
        
        /// <summary>
        /// Determine optimal Power Limits (PL1, PL2, Tau)
        /// </summary>
        (int pl1, int pl2, double tau) DetermineOptimalPowerLimits(WorkloadType workload, ThermalState thermal, CpuTuningCapabilities capabilities);
        
        /// <summary>
        /// Determine if C-States should be disabled
        /// </summary>
        bool ShouldDisableCStates(WorkloadType workload, double frameTimeJitter);
        
        /// <summary>
        /// Check if tuning adjustment is allowed (cooldown + hysteresis)
        /// </summary>
        bool CanAdjustTuning();
        
        /// <summary>
        /// Record that a tuning adjustment was made
        /// </summary>
        void RecordAdjustment();
    }
}
