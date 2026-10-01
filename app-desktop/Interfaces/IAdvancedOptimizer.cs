using System;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Interfaces
{
    /// <summary>
    /// Interface para serviço de otimizações avançadas
    /// </summary>
    public interface IAdvancedOptimizer
    {
        Task<bool> OptimizeStorageAsync(string driveLetter, bool isSSD, Action<int>? progressCallback = null);
        Task<bool> OptimizeMemoryAsync(Action<int>? progressCallback = null);
        Task<bool> OptimizeProcessesAsync(Action<int>? progressCallback = null);
        Task<bool> CleanRegistryAsync(Action<int>? progressCallback = null);
        bool IsSSD(string driveLetter);
    }
}
