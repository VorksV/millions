using System.Collections.Generic;

namespace VoltrisOptimizer.Services.GameRepairEnterprise.Models
{
    public class GameDependencyDiagnosis
    {
        public string ComponentId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsHealthy { get; set; }
        public bool CanAutoFix { get; set; } = true;
        public bool IsSupportedByHardware { get; set; } = true;
        
        public string FoundVersion { get; set; } = string.Empty;
        public string ExpectedVersion { get; set; } = string.Empty;
        
        public List<string> Evidences { get; set; } = new List<string>();
        public List<string> MissingEvidences { get; set; } = new List<string>();
        
        public bool NeedsRepair => !IsHealthy && IsSupportedByHardware;
    }
}
