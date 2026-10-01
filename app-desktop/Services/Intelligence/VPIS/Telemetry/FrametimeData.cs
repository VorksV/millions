using System;

namespace VoltrisOptimizer.Services.Intelligence.VPIS.Telemetry
{
    public class FrametimeData
    {
        public DateTime Timestamp { get; set; }
        public double FrametimeMs { get; set; }
        public double Fps { get; set; }
        public string ProcessName { get; set; }
    }
}
