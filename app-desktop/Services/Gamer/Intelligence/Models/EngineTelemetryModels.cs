using System;
using System.Collections.Generic;

namespace VoltrisOptimizer.Services.Gamer.Intelligence.Models
{
    public class EngineInsight
    {
        public SystemState State { get; set; } = new();
        public GameClassificationInsight Classification { get; set; } = new();
        public ActiveDecisionInsight Decision { get; set; } = new();
        public StabilityStatus Stability { get; set; } = StabilityStatus.Stable;
        public List<string> RecentDecisions { get; set; } = new();
    }

    public class SystemState
    {
        public double CpuClockMhz { get; set; }
        public int Pl1EffectiveW { get; set; }
        public int EppEffectivePercent { get; set; }
        public double CpuTemp { get; set; }
        public double ThermalHeadroom { get; set; }
        public double CpuUsage { get; set; }
        public double GpuUsage { get; set; }
        public double RamUsagePercent { get; set; }
        public double VramUsagePercent { get; set; }
    }

    public class GameClassificationInsight
    {
        public string Type { get; set; } = "Analyzing...";
        public double Confidence { get; set; }
        public List<string> Reasons { get; set; } = new();
    }

    public class ActiveDecisionInsight
    {
        public string Action { get; set; } = "Monitoring";
        public string Reason { get; set; } = "Routine";
        public double Headroom { get; set; }
        public double EmaJitter { get; set; }
        public double DeltaTemp { get; set; }
        public bool RequestedVsEffectiveMatch { get; set; }
    }

    public enum StabilityStatus
    {
        Stable,
        Adaptive,
        Adapting,
        Throttling,
        ThermalProtection,
        EmergencyThrottle
    }

    public class EngineSessionReport
    {
        public string GameName { get; set; } = "";
        public TimeSpan Duration { get; set; }
        public double AvgFps { get; set; }
        public double OnePercentLow { get; set; }
        public double FrametimeEma { get; set; }
        public double MaxTemp { get; set; }
        public double AvgTemp { get; set; }
        public int ThermalThrottleEvents { get; set; }
        public int Pl1Adjustments { get; set; }
        public int EppAdjustments { get; set; }
        public int AffinityChanges { get; set; }
        public string FinalClassification { get; set; } = "";
    }
}
