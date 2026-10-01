using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.Gamer.Pipeline.Models;

namespace VoltrisOptimizer.Services.Gamer.OBrain
{
    public interface IOBrain
    {
        Task<HardwareProfile> ProfileHardwareAsync(CancellationToken ct = default);
        Task<SystemProfile> ProfileSystemAsync(CancellationToken ct = default);
        Task<GameAnalysis> ProfileGameAsync(string executablePath, CancellationToken ct = default);
        Task<GamerSessionContext> BuildFullProfileAsync(string? gameExecutable, CancellationToken ct = default);
    }
}
