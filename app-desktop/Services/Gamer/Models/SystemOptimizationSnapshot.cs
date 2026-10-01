using System;
using VoltrisOptimizer.Services.Gamer.Implementation;

namespace VoltrisOptimizer.Services.Gamer.Models
{
    public class SystemOptimizationSnapshot
    {
        public object? Win32PrioritySeparation { get; set; }
        public int? PowerThrottlingOff { get; set; }
        public TaskbarState? TaskbarState { get; set; } 
        public string? MinAnimate { get; set; }
        public byte[]? UserPreferencesMask { get; set; }
        public string? FontSmoothing { get; set; }
        public int? FontSmoothingType { get; set; }
        public int? EnableTransparency { get; set; }
        public int? BackgroundAppsGlobalDisabled { get; set; }
        public Guid? OriginalPowerPlanGuid { get; set; }
        public int? HwSchMode { get; set; }
        public int? TdrDelay { get; set; }
        public int? TdrLevel { get; set; }
        public int? TdrDdiDelay { get; set; }
        public DateTime CapturedAt { get; set; }
    }
}
