using System;
using System.Collections.Generic;

namespace VoltrisOptimizer.Services.Intelligence.VPIS.Models
{
    public class PerformanceHealthReport
    {
        public string SessionName { get; set; } // E.g., "Cyberpunk 2077"
        public TimeSpan Duration { get; set; }
        public double OverallQualityScore { get; set; } // 0.0 to 10.0
        
        public EventCategory MainProblemCategory { get; set; }
        public string MainProblemTitle { get; set; } // E.g., "Thermal Throttling"
        public int MainProblemConfidence { get; set; } // 0 to 100
        public string EstimatedImpact { get; set; } // E.g., "-18% FPS"
        
        /// <summary>
        /// Narrative explaining the events. E.g., "Detectamos 11 episódios de degradação..."
        /// </summary>
        public string ForensicsNarrative { get; set; }
        
        public List<string> KeyEvidences { get; set; } = new List<string>();
        public List<string> Recommendations { get; set; } = new List<string>();
        
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }
}
