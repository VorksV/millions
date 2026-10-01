using System;
using System.Collections.Generic;

namespace VoltrisOptimizer.Services.Gamer.Intelligence
{
    // STUB - Machine Profile Result (V2 Architecture não usa profiling em tempo real)
    public class MachineProfileResult
    {
        public Intelligence.MachineProfile Profile { get; set; }
        public HardwareTier CpuTier { get; set; }
        public HardwareTier GpuTier { get; set; }
        public HardwareTier RamTier { get; set; }
        public bool IsNotebook { get; set; }
        public List<string> Recommendations { get; set; } = new();
        public List<string> Restrictions { get; set; } = new();
    }
    
    // STUB - HardwareTier enum
    public enum HardwareTier { Entry, Mid, Medium, High, Enthusiast }
    
    // STUB - AnomalySeverity enum
    public enum AnomalySeverity { Low, Medium, High, Critical }
}