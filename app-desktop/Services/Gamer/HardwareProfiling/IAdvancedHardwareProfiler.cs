using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.Gamer.HardwareProfiling.Models;

namespace VoltrisOptimizer.Services.Gamer.HardwareProfiling
{
    public interface IAdvancedHardwareProfiler
    {
        Task<AdvancedHardwareProfile> GetCurrentProfileAsync(CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Recalcula o perfil em tempo real se houver mudança de hardware detectada (ex: eGPU conectada).
        /// </summary>
        Task RefreshProfileAsync(CancellationToken cancellationToken = default);
    }
}
