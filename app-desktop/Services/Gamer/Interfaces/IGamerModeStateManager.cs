using System.Threading.Tasks;
using VoltrisOptimizer.Services.Gamer.Models;

namespace VoltrisOptimizer.Services.Gamer.Interfaces
{
    public interface IGamerModeStateManager
    {
        bool IsGamerModeActive { get; }
        Task<SystemOptimizationSnapshot> CaptureSnapshotAsync();
        Task RestoreOriginalSystemState();
    }
}
