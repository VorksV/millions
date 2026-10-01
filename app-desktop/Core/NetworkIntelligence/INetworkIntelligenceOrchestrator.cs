using System;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Core.NetworkIntelligence;

public enum DecisionProfile
{
    Idle,
    ModoGamer,
    Dsl,
    WatchDogs,
    PerfilInteligente
}

public sealed class DecisionResultEventArgs : EventArgs
{
    public NetworkDecision Decision { get; init; } = null!;

    public SystemContextSnapshot Context { get; init; } = null!;
}

public interface INetworkIntelligenceOrchestrator : IDisposable
{
    NetworkDecision? LastDecision { get; }

    DecisionProfile ActiveProfile { get; }

    bool IsRunning { get; }

    event EventHandler<DecisionResultEventArgs>? OnDecisionProduced;

    Task StartAsync(CancellationToken ct = default);

    Task StopAsync();

    Task<NetworkDecision> ForceEvaluateNowAsync();

    Task<NetworkDecision> RequestProfileAsync(DecisionProfile profile);
}
