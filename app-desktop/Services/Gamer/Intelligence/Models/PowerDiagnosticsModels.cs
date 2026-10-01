using System;
using System.Collections.Generic;
using System.Linq;

namespace VoltrisOptimizer.Services.Gamer.Intelligence.Models
{
    public enum PowerDiagnosticState
    {
        Idle,
        Stabilizing, // Waiting for gameplay to stabilize (loading screens, etc)
        Monitoring,  // Collecting data on High Performance
        Testing,     // Testing Balanced
        Deciding,    // Comparing results
        Applied      // Final decision made
    }

    public class HardwareFingerprint
    {
        public string CpuModel { get; set; } = "Unknown";
        public string GpuModel { get; set; } = "Unknown";
        public string WindowsVersion { get; set; } = "Unknown";
        public bool IsOnBattery { get; set; }
        
        public bool Equals(HardwareFingerprint other)
        {
            if (other == null) return false;
            return CpuModel == other.CpuModel && 
                   GpuModel == other.GpuModel && 
                   WindowsVersion == other.WindowsVersion && 
                   IsOnBattery == other.IsOnBattery;
        }
    }

    public class PowerDiagnosticResult
    {
        public bool HighPerformanceDegrading { get; set; }
        public double HighPerfAvgFps { get; set; }
        public double BalancedAvgFps { get; set; }
        public double HighPerfAvgClock { get; set; }
        public double BalancedAvgClock { get; set; }
        public double HighPerfMaxTemp { get; set; }
        public double BalancedMaxTemp { get; set; }
        public double ConfidenceScore { get; set; }
        public DateTime TestedAt { get; set; } = DateTime.Now;
        public string GameName { get; set; } = "";
        public string Reason { get; set; } = "";
        public HardwareFingerprint Fingerprint { get; set; } = new();
    }

    public class SessionMetrics
    {
        public List<double> FpsValues { get; set; } = new();
        public List<double> Clocks { get; set; } = new();
        public List<double> Temps { get; set; } = new();
        public List<double> CpuUsage { get; set; } = new();
        public List<double> GpuUsage { get; set; } = new();

        public double AvgFps => FpsValues.Count > 0 ? FpsValues.Average() : 0;
        public double AvgClock => Clocks.Count > 0 ? Clocks.Average() : 0;
        public double MaxTemp => Temps.Count > 0 ? Temps.Max() : 0;
        public double AvgCpuUsage => CpuUsage.Count > 0 ? CpuUsage.Average() : 0;
        public double AvgGpuUsage => GpuUsage.Count > 0 ? GpuUsage.Average() : 0;

        public void Clear()
        {
            FpsValues.Clear();
            Clocks.Clear();
            Temps.Clear();
            CpuUsage.Clear();
            GpuUsage.Clear();
        }
    }
}
