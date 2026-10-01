using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.Gamer.Interfaces;
using VoltrisOptimizer.Services.Performance.Decision.Models;
using VoltrisOptimizer.Services.Performance.Orchestration.Models;
using VoltrisOptimizer.Services.Optimization.Unification;

namespace VoltrisOptimizer.Services.Performance.Orchestration
{
    /// <summary>
    /// Lightweight Orchestrator (Execution Layer).
    /// Statelessly executes decisions authorized by the Intelligence Layer.
    /// Follows the "Principal Software Architect" vision for 100% intelligent performance.
    /// </summary>
    public sealed class PerformanceOrchestrator : IPerformanceOrchestrator
    {
        private readonly ILoggingService _logger;
        private readonly IGamerModeOrchestrator _gamerOrchestrator;
        private readonly ICpuGamingOptimizer _cpuOptimizer;
        private readonly INetworkGamingOptimizer _networkOptimizer;
        private readonly TimerResolutionManager _timerManager;
        private readonly OrchestrationState _state;
        private readonly SemaphoreSlim _executionLock = new(1, 1);
        private bool _timerRequested = false;

        public PerformanceOrchestrator(
            ILoggingService logger,
            IGamerModeOrchestrator gamerOrchestrator,
            ICpuGamingOptimizer cpuOptimizer,
            INetworkGamingOptimizer networkOptimizer,
            ITimerResolutionService timerService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _gamerOrchestrator = gamerOrchestrator ?? throw new ArgumentNullException(nameof(gamerOrchestrator));
            _cpuOptimizer = cpuOptimizer ?? throw new ArgumentNullException(nameof(cpuOptimizer));
            _networkOptimizer = networkOptimizer ?? throw new ArgumentNullException(nameof(networkOptimizer));
            // Usar TimerResolutionManager para participar do reference counting centralizado
            _timerManager = new TimerResolutionManager(logger, timerService ?? throw new ArgumentNullException(nameof(timerService)));
            
            // 5 minute stability window by default to prevent thrashing
            _state = new OrchestrationState(TimeSpan.FromMinutes(5));
        }

        public async Task<OrchestrationResult> ExecuteDecisionAsync(
            PerformanceDecision decision,
            CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();

            try
            {
                // 1. Thread safety and deduplication check
                if (!_state.ShouldExecute(decision, out string skipReason))
                {
                    _logger.LogInfo($"[Orchestrator] Execution skipped: {skipReason}");
                    return OrchestrationResult.Skipped(skipReason);
                }

                await _executionLock.WaitAsync(cancellationToken);
                try
                {
                    _logger.LogInfo($"[Orchestrator] 🚀 Executing Decision: {decision.Type} (Intensity: {decision.Intensity})");
                    _logger.LogInfo($"[Orchestrator] Reason: {decision.PrimaryReason}");

                    // 2. Execution Logic based on allowed targets
                    bool success = await ProcessAllowedTargetsAsync(decision, cancellationToken);

                    if (success)
                    {
                        _state.RecordExecution(decision, OrchestrationStatus.Executed);
                        sw.Stop();
                        return OrchestrationResult.Succeeded(
                            $"Successfully applied {decision.Type} optimizations.", 
                            sw.Elapsed);
                    }
                    else
                    {
                        return OrchestrationResult.Failed(
                            "Failed to apply one or more optimizations.", 
                            sw.Elapsed);
                    }
                }
                finally
                {
                    _executionLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[Orchestrator] Critical error during execution of {decision.Type}", ex);
                return OrchestrationResult.Failed(
                    $"Fatal error: {ex.Message}", 
                    sw.Elapsed, 
                    ex.StackTrace);
            }
        }

        private async Task<bool> ProcessAllowedTargetsAsync(PerformanceDecision decision, CancellationToken ct)
        {
            var targets = decision.AllowedTargets;
            bool anySuccess = false;

            // CPU Optimizations
            if (targets.CpuPriority || targets.CpuAffinity)
            {
                _logger.LogInfo("[Orchestrator] Configuring CPU targets...");
                // Note: Logic for finding processes would be here or in specialized service
                // For now, we interact with the existing optimizers
                anySuccess |= await _cpuOptimizer.OptimizeAsync(ct);
            }

            // Gaming Mode (High-level target)
            if (decision.Type == DecisionType.EnableGamerMode || targets.LaunchBoost)
            {
                _logger.LogInfo("[Orchestrator] Activating Gamer Mode Path...");
                // Map Intensity to GamerOptions
                var options = MapToGamerOptions(decision.Intensity);
                anySuccess |= await _gamerOrchestrator.ActivateAsync(options, decision.ContextId, null, ct, false);
            }

            // Network / QoS
            if (targets.ServiceQoS)
            {
                _logger.LogInfo("[Orchestrator] Optimizing Network QoS...");
                anySuccess |= await _networkOptimizer.OptimizeAsync(ct);
            }

            // Timer Resolution (Latency)
            if (targets.TimerResolution)
            {
                _logger.LogInfo("[Orchestrator] Setting high-precision Timer Resolution...");
                if (!_timerRequested)
                {
                    _timerManager.RequestHighPrecision("PerformanceOrchestrator");
                    _timerRequested = true;
                }
                anySuccess = true;
            }

            // Revert Case
            if (decision.Type == DecisionType.Revert)
            {
                _logger.LogInfo("[Orchestrator] 🔄 Performing Global Rollback...");
                await RevertAllAsync(ct);
                anySuccess = true;
            }

            return anySuccess;
        }

        public async Task<OrchestrationResult> RevertAllAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInfo("[Orchestrator] 🏁 Reverting all optimizations to baseline...");
            var sw = Stopwatch.StartNew();

            try
            {
                await _gamerOrchestrator.DeactivateAsync(null, cancellationToken);
                await _cpuOptimizer.RestoreAsync(cancellationToken);
                await _networkOptimizer.RestoreAsync(cancellationToken);
                if (_timerRequested)
                {
                    _timerManager.ReleaseHighPrecision("PerformanceOrchestrator.RevertAll");
                    _timerRequested = false;
                }

                _state.Reset();

                sw.Stop();
                return OrchestrationResult.Succeeded("All optimizations reverted successfully.", sw.Elapsed);
            }
            catch (Exception ex)
            {
                _logger.LogError("[Orchestrator] Error during rollback", ex);
                return OrchestrationResult.Failed("Failed to revert optimizations safely.", sw.Elapsed, ex.Message);
            }
        }

        public string GetCurrentState() => _state.ToString();

        private VoltrisOptimizer.Services.Gamer.Models.GamerOptimizationOptions MapToGamerOptions(DecisionIntensity intensity)
        {
            // Intelligent Mapping: High-end hardware gets extreme tweaks, low-end gets stability.
            var options = new VoltrisOptimizer.Services.Gamer.Models.GamerOptimizationOptions();
            
            switch (intensity)
            {
                case DecisionIntensity.Aggressive:
                    options.EnableExtremeMode = true;
                    options.OptimizeMemory = true;
                    options.OptimizeCpu = true;
                    options.OptimizeNetwork = true;
                    break;
                case DecisionIntensity.Moderate:
                    options.OptimizeMemory = true;
                    options.OptimizeCpu = true;
                    options.OptimizeNetwork = false;
                    break;
                default:
                    options.OptimizeMemory = false;
                    options.OptimizeCpu = true;
                    break;
            }
            return options;
        }
    }
}
