using System;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.Gamer.Models;
using VoltrisOptimizer.Services.Gamer.Pipeline.Models;

namespace VoltrisOptimizer.Services.Gamer.Orchestrator
{
    public interface IGamerOrchestrator
    {
        bool IsActive { get; }
        GamerSessionContext? CurrentSession { get; }

        event EventHandler<GamerSessionContext>? SessionStarted;
        event EventHandler<GamerSessionContext>? SessionCompleted;

        Task<bool> StartSessionAsync(
            string? gameExecutable = null,
            GamerOptimizationOptions? options = null,
            CancellationToken ct = default);

        Task EndSessionAsync(CancellationToken ct = default);
        Task EmergencyRollbackAsync();
    }
}
