using VoltrisOptimizer.Services.Performance.CpuTuning.Models;

namespace VoltrisOptimizer.Services.Performance.CpuTuning
{
    /// <summary>
    /// Detects hardware capabilities, limits, and classifies machine tier
    /// </summary>
    public interface IHardwareCapabilityDetector
    {
        /// <summary>
        /// Get all hardware capabilities and limits
        /// </summary>
        CpuTuningCapabilities Capabilities { get; }

        /// <summary>
        /// Detect all hardware capabilities and limits
        /// </summary>
        CpuTuningCapabilities DetectCapabilities();
        
        /// <summary>
        /// Get cached capabilities (avoid re-detection)
        /// </summary>
        CpuTuningCapabilities GetCapabilities();
        
        /// <summary>
        /// Check if a specific setting is locked by firmware
        /// </summary>
        bool IsSettingLocked(string settingName);
    }
}
