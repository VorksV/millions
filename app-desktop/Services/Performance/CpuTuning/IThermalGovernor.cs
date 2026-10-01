using System;
using VoltrisOptimizer.Services.Performance.CpuTuning.Models;

namespace VoltrisOptimizer.Services.Performance.CpuTuning
{
    /// <summary>
    /// Thermal safety governor - HIGHEST PRIORITY component
    /// Monitors temperature and enforces thermal protection
    /// </summary>
    public interface IThermalGovernor
    {
        /// <summary>
        /// Start thermal monitoring
        /// </summary>
        void Start();
        
        /// <summary>
        /// Stop thermal monitoring
        /// </summary>
        void Stop();
        
        /// <summary>
        /// Inject a temperature reading from external hardware source (e.g. nvidia-smi, shared sensor)
        /// This is the preferred path when an external, accurate sensor is available.
        /// </summary>
        void InjectTemperature(double celsius);
        
        /// <summary>
        /// Get current thermal state
        /// </summary>
        ThermalState GetThermalState();
        
        /// <summary>
        /// Check if performance increase is thermally safe
        /// </summary>
        bool CanIncreasePerformance();
        
        /// <summary>
        /// Check if thermal protection is active
        /// </summary>
        bool IsThermalProtectionActive { get; }
        
        /// <summary>
        /// Event fired when thermal protection activates
        /// </summary>
        event EventHandler<ThermalProtectionEventArgs>? ThermalProtectionTriggered;
    }
    
    public enum ThermalCondition
    {
        Safe,      // Normal operation ( < 75°C )
        Warm,      // Caution ( 75°C - 85°C )
        Hot,       // Warning ( 85°C - 92°C )
        Critical   // Emergency ( > 92°C or Silicon Throttling bit active )
    }
    
    public class ThermalState
    {
        public double CurrentTemperature { get; set; }
        public double TjSafe { get; set; }
        public double TjMax { get; set; }
        public double DistanceToTjMax => TjMax - CurrentTemperature;
        public double ThermalHeadroom => TjSafe - CurrentTemperature;
        public double TemperatureDelta { get; set; } 
        public double PredictedTemperature { get; set; }
        
        public ThermalCondition Condition { get; set; }
        public bool IsSiliconThrottling { get; set; } // Read from MSR 0x19C
        
        public bool IsInDangerZone => Condition >= ThermalCondition.Hot;
        public bool IsCritical => Condition == ThermalCondition.Critical || IsSiliconThrottling;
    }
    
    public class ThermalProtectionEventArgs : EventArgs
    {
        public double Temperature { get; set; }
        public double TjSafe { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}
