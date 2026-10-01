using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Gamer.Interfaces
{
    /// <summary>
    /// Interface para otimização de rede para gaming
    /// </summary>
    public interface INetworkOptimizerService
    {
        Task<bool> OptimizeNetworkAsync();
        Task<bool> RestoreNetworkAsync();
        (double PingMs, double PacketLoss, double Jitter) GetNetworkMetrics();
    }
    
    /// <summary>
    /// Interface para otimização de memória avançada
    /// </summary>
    public interface IMemoryOptimizerService
    {
        Task<bool> OptimizeMemoryAsync();
        Task<bool> RestoreMemoryAsync();
        (double UsedGb, double TotalGb, double UsagePercent, double CacheSize) GetMemoryMetrics();
    }
    
    /// <summary>
    /// Interface para otimização em kernel mode
    /// </summary>
    public interface IKernelOptimizerService
    {
        Task<bool> OptimizeKernelAsync();
        Task<bool> RestoreKernelAsync();
        (int ActiveThreads, double CpuUsage, int ContextSwitches, int Interrupts) GetKernelMetrics();
    }
    
    /// <summary>
    /// Interface para otimização de display
    /// </summary>
    public interface IDisplayOptimizerService
    {
        Task<bool> OptimizeDisplayAsync();
        Task<bool> RestoreDisplayAsync();
        (int RefreshRate, bool VrrEnabled, string ColorProfile) GetDisplayMetrics();
    }
    
    /// <summary>
    /// Interface para otimização de áudio
    /// </summary>
    public interface IAudioOptimizerService
    {
        Task<bool> OptimizeAudioAsync();
        Task<bool> RestoreAudioAsync();
        (int BufferSize, int SampleRate, int VolumeLevel, bool EffectsDisabled) GetAudioMetrics();
    }
    
    /// <summary>
    /// Interface para otimização avançada de CPU scheduler
    /// </summary>
    public interface IAdvancedCpuSchedulerService
    {
        Task<bool> OptimizeCpuSchedulerAsync();
        Task<bool> RestoreCpuSchedulerAsync();
        (int ActiveCores, int QuantumLength, bool NumaEnabled, bool HybridCpu) GetSchedulerMetrics();
    }
}
