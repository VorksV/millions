using System;
using System.Collections.Generic;

namespace VoltrisOptimizer.Services.Intelligence.VPIS.Models
{
    public class PerformanceInsightEvent
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string ProcessName { get; set; }
        public EventCategory Category { get; set; }
        public DiagnosticSeverity Severity { get; set; }
        public ConfidenceLevel Confidence { get; set; }
        public int ConfidenceScore { get; set; } // 0 to 100
        
        /// <summary>
        /// What happened precisely. E.g., "GPU reached 88°C"
        /// </summary>
        public List<string> Evidences { get; set; } = new List<string>();
        
        /// <summary>
        /// Forensics conclusion. E.g., "Thermal limit reached causing frequency drop."
        /// </summary>
        public string Diagnosis { get; set; }
        
        /// <summary>
        /// Actionable fix. E.g., "Enable VDPS or improve cooling."
        /// </summary>
        public List<string> Recommendations { get; set; } = new List<string>();

        public override string ToString()
        {
            return $"[{Timestamp:HH:mm:ss}] {Category} ({Severity}) - Confidence: {ConfidenceScore}% -> {Diagnosis}";
        }
    }
}
