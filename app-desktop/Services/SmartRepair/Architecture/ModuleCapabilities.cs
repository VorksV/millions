namespace VoltrisOptimizer.Services.SmartRepair.Architecture
{
    public enum RiskLevel
    {
        Safe,
        Moderate,
        High
    }

    public class ModuleCapabilities
    {
        public bool SupportsScan { get; set; } = true;
        public bool SupportsSimulation { get; set; } = true;
        public bool SupportsExecution { get; set; } = true;
        public bool SupportsRollback { get; set; } = false;
        public bool SupportsParallelism { get; set; } = true;
        
        public bool RequiresAdmin { get; set; } = false;
        public bool RequiresReboot { get; set; } = false;
        public bool RequiresLogoff { get; set; } = false;
        public bool RequiresAdditionalConfirmation { get; set; } = false;
        
        public bool HasDestructiveOperations { get; set; } = false;
        public RiskLevel MaxRiskLevel { get; set; } = RiskLevel.Safe;
    }
}
