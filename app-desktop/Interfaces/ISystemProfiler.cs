using System;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Interfaces
{
    public interface ISystemProfiler
    {
        bool RequireGate { get; }
        bool IsGateCompleted { get; }
        
        Task InitializeAsync();
        
        Task<SystemInfo?> GetSystemInfoAsync();
        
        Task SyncHardwareOptimizationsSilentlyAsync();
        
        void MarkGateCompleted();
        
        Task<object> AnalyzeAsync(CancellationToken ct = default);
    }
    
    public class SystemInfo
    {
        public string? ProcessorName { get; set; }
        public string? GraphicsCard { get; set; }
        public string? Memory { get; set; }
        public string? Storage { get; set; }
    }
}
