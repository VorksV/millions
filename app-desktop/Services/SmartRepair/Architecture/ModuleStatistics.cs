using System;

namespace VoltrisOptimizer.Services.SmartRepair.Architecture
{
    public class ModuleStatistics
    {
        public long ItemsScanned { get; set; }
        public long ItemsFound { get; set; }
        public long ItemsIgnored { get; set; }
        public long ItemsProtected { get; set; }
        public long ItemsRepaired { get; set; }
        public long SpaceRecoveredBytes { get; set; }
        
        public TimeSpan ExecutionTime { get; set; }
        public TimeSpan EstimatedTime { get; set; }
        
        public int ErrorCount { get; set; }
        public int WarningCount { get; set; }
        
        public void Add(ModuleStatistics other)
        {
            ItemsScanned += other.ItemsScanned;
            ItemsFound += other.ItemsFound;
            ItemsIgnored += other.ItemsIgnored;
            ItemsProtected += other.ItemsProtected;
            ItemsRepaired += other.ItemsRepaired;
            SpaceRecoveredBytes += other.SpaceRecoveredBytes;
            ExecutionTime += other.ExecutionTime;
            EstimatedTime += other.EstimatedTime;
            ErrorCount += other.ErrorCount;
            WarningCount += other.WarningCount;
        }
    }
}
