namespace VoltrisOptimizer.Services.Gamer.StrategyFactory.Models
{
    public enum MachinePerformanceTier
    {
        Unknown,
        LowEnd,
        MidEnd,
        HighEnd,
        Enthusiast
    }

    public class PerformanceScore
    {
        public int CpuScore { get; set; }
        public int GpuScore { get; set; }
        public int MemoryScore { get; set; }
        public int StorageScore { get; set; }
        
        public int TotalScore => CpuScore + GpuScore + MemoryScore + StorageScore;

        public MachinePerformanceTier Tier
        {
            get
            {
                if (TotalScore >= 800) return MachinePerformanceTier.Enthusiast;
                if (TotalScore >= 600) return MachinePerformanceTier.HighEnd;
                if (TotalScore >= 300) return MachinePerformanceTier.MidEnd;
                return MachinePerformanceTier.LowEnd;
            }
        }
    }
}
