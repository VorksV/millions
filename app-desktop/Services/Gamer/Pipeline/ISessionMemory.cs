using System.Threading.Tasks;
using VoltrisOptimizer.Services.Gamer.Pipeline.Models;

namespace VoltrisOptimizer.Services.Gamer.Pipeline
{
    public interface ISessionMemory
    {
        Task SaveSessionAsync(GamerSessionContext context);
        Task<StoredSessionData?> GetLastSessionForGameAsync(string gameName);
        Task<OptimizationHistoryData> GetOptimizationHistoryAsync(string optimizationId);
    }

    public class StoredSessionData
    {
        public string GameName { get; set; } = "";
        public int TotalOptimizations { get; set; }
        public int Validated { get; set; }
        public int Placebos { get; set; }
        public double AvgFpsImprovement { get; set; }
        public System.DateTime PlayedAt { get; set; }
    }

    public class OptimizationHistoryData
    {
        public int TotalApplications { get; set; }
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public double AvgFpsDeltaPercent { get; set; }
        public double Confidence { get; set; }
    }
}
