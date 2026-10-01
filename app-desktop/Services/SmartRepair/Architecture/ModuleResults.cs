using System.Collections.Generic;

namespace VoltrisOptimizer.Services.SmartRepair.Architecture
{
    public class ModuleScanResult
    {
        public bool Success { get; set; } = true;
        public string Message { get; set; } = string.Empty;
        public ModuleStatistics Statistics { get; set; } = new();
        public List<object> FoundItems { get; set; } = new();
    }

    public class ModuleSimulationResult
    {
        public bool Success { get; set; } = true;
        public string Message { get; set; } = string.Empty;
        public ModuleStatistics EstimatedStatistics { get; set; } = new();
        public RiskLevel RiskLevel { get; set; } = RiskLevel.Safe;
        public List<object> ItemsToProcess { get; set; } = new();
    }

    public class ModuleExecutionResult
    {
        public bool Success { get; set; } = true;
        public string Message { get; set; } = string.Empty;
        public ModuleStatistics FinalStatistics { get; set; } = new();
        public List<object> ProcessedItems { get; set; } = new();
        public List<object> FailedItems { get; set; } = new();
    }

    public class ModuleRollbackResult
    {
        public bool Success { get; set; } = true;
        public string Message { get; set; } = string.Empty;
        public int ItemsRestored { get; set; }
        public int ItemsFailedToRestore { get; set; }
    }
}
