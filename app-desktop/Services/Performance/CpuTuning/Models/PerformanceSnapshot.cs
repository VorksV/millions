using System;

namespace VoltrisOptimizer.Services.Performance.CpuTuning.Models
{
    /// <summary>
    /// Performance snapshot for anti-placebo validation
    /// </summary>
    public class PerformanceSnapshot
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        
        // Frequency metrics
        public double SustainedFrequency { get; set; } // Average over measurement period
        public double TimeInTurboPercent { get; set; }
        
        // Thermal metrics
        public int ThermalThrottlingEvents { get; set; }
        public double AverageTemperature { get; set; }
        
        // Frame time metrics (if available)
        public double AverageFrameTime { get; set; } // milliseconds
        public double FrameTimeJitter { get; set; } // standard deviation
        
        // Power behavior
        public double AveragePower { get; set; }
        public double PeakPower { get; set; }
        
        /// <summary>
        /// Calculate performance improvement percentage
        /// </summary>
        public double CalculateImprovement(PerformanceSnapshot baseline)
        {
            if (baseline == null) return 0;
            
            double freqImprovement = (SustainedFrequency - baseline.SustainedFrequency) / baseline.SustainedFrequency * 100;
            double turboImprovement = TimeInTurboPercent - baseline.TimeInTurboPercent;
            double throttleReduction = baseline.ThermalThrottlingEvents - ThermalThrottlingEvents;
            
            // Weighted average: frequency is most important
            return (freqImprovement * 0.6) + (turboImprovement * 0.3) + (throttleReduction * 0.1);
        }
        
        /// <summary>
        /// Check if performance degraded
        /// </summary>
        public bool IsDegradedFrom(PerformanceSnapshot baseline)
        {
            if (baseline == null) return false;
            
            // Check for negative indicators
            bool moreThrottling = ThermalThrottlingEvents > baseline.ThermalThrottlingEvents;
            bool worseFrameTime = FrameTimeJitter > baseline.FrameTimeJitter * 1.1; // 10% tolerance
            bool lowerFrequency = SustainedFrequency < baseline.SustainedFrequency * 0.98; // 2% tolerance
            
            return moreThrottling || worseFrameTime || lowerFrequency;
        }
    }
}
