using System;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.Performance.Decision;
using VoltrisOptimizer.Services.Performance.Decision.Models;
using VoltrisOptimizer.Services.Performance.Orchestration;

namespace VoltrisOptimizer.Services.Performance
{
    /// <summary>
    /// Intelligent Performance Coordinator - Non-invasive integration layer.
    /// Provides intelligent recommendations WITHOUT interfering with manual Gamer Mode.
    /// Follows the "Principal Software Architect" vision.
    /// </summary>
    public sealed class IntelligentPerformanceCoordinator
    {
        private readonly PerformanceContextBuilder _contextBuilder;
        private readonly IPerformanceDecisionEngine _decisionEngine;
        private readonly IPerformanceOrchestrator _orchestrator;
        private readonly ILoggingService _logger;
        private readonly SemaphoreSlim _lock = new(1, 1);

        private PerformanceContext? _lastContext;
        private PerformanceDecision? _lastDecision;
        private DateTime _lastAnalysisTime = DateTime.MinValue;
        private readonly TimeSpan _analysisInterval = TimeSpan.FromMinutes(2);

        public IntelligentPerformanceCoordinator(
            PerformanceContextBuilder contextBuilder,
            IPerformanceDecisionEngine decisionEngine,
            IPerformanceOrchestrator orchestrator,
            ILoggingService logger)
        {
            _contextBuilder = contextBuilder ?? throw new ArgumentNullException(nameof(contextBuilder));
            _decisionEngine = decisionEngine ?? throw new ArgumentNullException(nameof(decisionEngine));
            _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Analyzes current system state and returns intelligent recommendation.
        /// Does NOT execute anything - only provides advice.
        /// </summary>
        public async Task<PerformanceRecommendation> AnalyzeAsync(CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct);
            try
            {
                // Throttle analysis to avoid overhead
                if (DateTime.UtcNow - _lastAnalysisTime < _analysisInterval && _lastDecision != null)
                {
                    return new PerformanceRecommendation(_lastContext!, _lastDecision, false, "Recent analysis still valid");
                }

                _logger.LogInfo("[IntelligentCoordinator] 🧠 Analyzing system state...");

                // 1. Build Context
                var context = await _contextBuilder.BuildAsync(ct);
                _lastContext = context;

                // 2. Get Decision
                var decision = _decisionEngine.Decide(context);
                _lastDecision = decision;
                _lastAnalysisTime = DateTime.UtcNow;

                // 3. Determine if action is recommended
                bool shouldAct = decision.Type != DecisionType.NoAction && decision.Confidence >= DecisionConfidence.Medium;

                _logger.LogInfo($"[IntelligentCoordinator] 💡 Recommendation: {decision.Type} (Confidence: {decision.Confidence})");

                return new PerformanceRecommendation(context, decision, shouldAct, decision.PrimaryReason);
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Executes the last recommended decision (if user approves).
        /// This is the ONLY method that actually changes system state.
        /// </summary>
        public async Task<Orchestration.Models.OrchestrationResult> ExecuteRecommendationAsync(CancellationToken ct = default)
        {
            if (_lastDecision == null)
            {
                _logger.LogWarning("[IntelligentCoordinator] No recommendation to execute");
                return Orchestration.Models.OrchestrationResult.Skipped("No active recommendation");
            }

            _logger.LogInfo($"[IntelligentCoordinator] ⚡ Executing recommendation: {_lastDecision.Type}");
            return await _orchestrator.ExecuteDecisionAsync(_lastDecision, ct);
        }

        /// <summary>
        /// Gets the current orchestrator state (for UI display).
        /// </summary>
        public string GetCurrentState() => _orchestrator.GetCurrentState();

        /// <summary>
        /// Reverts all intelligent optimizations.
        /// </summary>
        public async Task<Orchestration.Models.OrchestrationResult> RevertAllAsync(CancellationToken ct = default)
        {
            _logger.LogInfo("[IntelligentCoordinator] 🔄 Reverting all intelligent optimizations");
            return await _orchestrator.RevertAllAsync(ct);
        }
    }

    /// <summary>
    /// Recommendation output from the intelligent coordinator.
    /// Read-only, descriptive only.
    /// </summary>
    public sealed class PerformanceRecommendation
    {
        public PerformanceContext Context { get; }
        public PerformanceDecision Decision { get; }
        public bool ShouldAct { get; }
        public string Reasoning { get; }
        public DateTime GeneratedAt { get; }

        public PerformanceRecommendation(
            PerformanceContext context,
            PerformanceDecision decision,
            bool shouldAct,
            string reasoning)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Decision = decision ?? throw new ArgumentNullException(nameof(decision));
            ShouldAct = shouldAct;
            Reasoning = reasoning ?? throw new ArgumentNullException(nameof(reasoning));
            GeneratedAt = DateTime.UtcNow;
        }

        public override string ToString() =>
            $"Recommendation: {Decision.Type} | Should Act: {ShouldAct} | {Reasoning}";
    }
}
