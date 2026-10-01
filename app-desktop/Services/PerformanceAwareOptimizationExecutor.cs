using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.SystemIntelligenceProfiler;
using VoltrisOptimizer.Core.SystemIntelligenceProfiler.Models;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Performance;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// Enhanced Intelligent Optimization Executor with performance validation
    /// Ensures optimizations don't degrade system performance before application
    /// </summary>
    public class PerformanceAwareOptimizationExecutor : IntelligentOptimizationExecutor
    {
        private readonly StateDetectionEngine _stateDetector;
        private readonly ILoggingService _logger;
        private readonly VoltrisOptimizer.Services.SystemChanges.IRollbackManager _rollbackManager;
        private readonly PerformanceValidationService _performanceValidator;
        private readonly StructuredOptimizationLogger _structuredLogger;

        public PerformanceAwareOptimizationExecutor(
            StateDetectionEngine stateDetector,
            ILoggingService logger,
            VoltrisOptimizer.Services.SystemChanges.IRollbackManager rollbackManager,
            PerformanceValidationService performanceValidator,
            StructuredOptimizationLogger structuredLogger)
            : base(stateDetector, logger, rollbackManager)
        {
            _stateDetector = stateDetector ?? throw new ArgumentNullException(nameof(stateDetector));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _rollbackManager = rollbackManager ?? throw new ArgumentNullException(nameof(rollbackManager));
            _performanceValidator = performanceValidator ?? throw new ArgumentNullException(nameof(performanceValidator));
            _structuredLogger = structuredLogger ?? throw new ArgumentNullException(nameof(structuredLogger));
        }

        /// <summary>
        /// Overrides base optimization application with performance-aware validation
        /// </summary>
        public override async Task<IntelligentOptimizationResult> ApplyOptimizationsAsync(
            IEnumerable<VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization> optimizations,
            OptimizationMode mode = OptimizationMode.Smart,
            CancellationToken ct = default)
        {
            // Delegate to the specialized method with default validation settings
            var perfResult = await ApplyOptimizationsWithValidationAsync(optimizations, mode, true, default, ct);
            return ConvertToCompatResult(perfResult);
        }

        private IntelligentOptimizationResult ConvertToCompatResult(PerformanceAwareOptimizationResult perfResult)
        {
            var result = new IntelligentOptimizationResult
            {
                StartTime = perfResult.StartTime,
                EndTime = perfResult.EndTime,
                Duration = perfResult.Duration,
                Mode = perfResult.Mode,
                Progress = 1.0, // Completed
                ErrorMessage = perfResult.ErrorMessage
            };

            foreach (var r in perfResult.OptimizationResults)
            {
                var status = r.Status switch
                {
                    OptimizationApplicationStatus.Applied => OptimizationExecutionStatus.Success,
                    OptimizationApplicationStatus.Skipped => OptimizationExecutionStatus.Skipped,
                    OptimizationApplicationStatus.Failed => OptimizationExecutionStatus.Failed,
                    OptimizationApplicationStatus.ValidationFailed => OptimizationExecutionStatus.ValidationFailedNoRollback,
                    OptimizationApplicationStatus.RolledbackDueToPerformance => OptimizationExecutionStatus.ValidationFailedWithRollback,
                    _ => OptimizationExecutionStatus.Failed
                };

                result.AddExecutionResult(new OptimizationExecutionResult
                {
                    OptimizationName = r.OptimizationName,
                    StartTime = r.StartTime,
                    EndTime = r.EndTime,
                    Duration = r.Duration,
                    Status = status,
                    ErrorMessage = r.ErrorMessage,
                    Exception = r.Exception,
                    RollbackPointId = r.RollbackPointId,
                    ApplyCompleted = r.ApplyCompleted,
                    RollbackPerformed = r.RollbackPerformed,
                    RollbackSuccess = r.RollbackSuccess,
                    ValidationResult = r.ValidationResult
                });
            }

            return result;
        }

        /// <summary>
        /// Applies optimizations with comprehensive performance validation
        /// </summary>
        public async Task<PerformanceAwareOptimizationResult> ApplyOptimizationsWithValidationAsync(
            IEnumerable<VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization> optimizations,
            OptimizationMode mode = OptimizationMode.Smart,
            bool validatePerformance = true,
            TimeSpan performanceTestDuration = default,
            CancellationToken ct = default)
        {
            if (performanceTestDuration == default)
                performanceTestDuration = TimeSpan.FromSeconds(30);

            var result = new PerformanceAwareOptimizationResult
            {
                StartTime = DateTime.UtcNow,
                Mode = mode,
                PerformanceValidationEnabled = validatePerformance
            };

            _logger.Log(LogLevel.Info, LogCategory.Optimization,
                $"Starting performance-aware optimization execution with {optimizations.Count()} optimizations",
                source: "PerformanceAwareExecutor");

            try
            {
                // Phase 1: Establish performance baseline
                if (validatePerformance)
                {
                    _logger.Log(LogLevel.Info, LogCategory.Performance,
                        "Establishing performance baseline before optimization",
                        source: "PerformanceAwareExecutor");

                    var baselineScenario = new PerformanceTestScenario
                    {
                        ScenarioName = "Pre-Optimization Baseline",
                        Description = "Baseline performance metrics before any optimizations",
                        Duration = performanceTestDuration
                    };

                    result.Baseline = await _performanceValidator.EstablishBaselineAsync(baselineScenario, ct);
                }

                // Phase 2: Apply optimizations with validation
                var optimizationList = optimizations.ToList();
                result.OptimizationResults = await ApplyOptimizationsWithIndividualValidationAsync(
                    optimizationList, mode, validatePerformance, ct);

                // Phase 3: Post-optimization performance validation
                if (validatePerformance)
                {
                    _logger.Log(LogLevel.Info, LogCategory.Performance,
                        "Validating performance after optimization application",
                        source: "PerformanceAwareExecutor");

                    result.PostValidation = await _performanceValidator.ValidatePerformanceAsync(
                        result.Baseline, performanceTestDuration, ct);

                    // Check for unacceptable performance degradation
                    if (!_performanceValidator.IsPerformanceDegradationAcceptable(result.PostValidation.Analysis))
                    {
                        _logger.Log(LogLevel.Warning, LogCategory.Optimization,
                            "Performance degradation detected - initiating auto-rollback",
                            null, "PerformanceAwareExecutor");

                        await AutoRollbackAllOptimizationsAsync(result, ct);
                        result.PerformanceDegradationDetected = true;
                    }
                }

                result.EndTime = DateTime.UtcNow;
                result.Duration = result.EndTime - result.StartTime;

                _logger.Log(LogLevel.Success, LogCategory.Optimization,
                    $"Performance-aware optimization completed. Applied: {result.AppliedCount}, " +
                    $"Failed: {result.FailedCount}, Performance Issues: {result.PerformanceDegradationDetected}",
                    source: "PerformanceAwareExecutor");

                return result;
            }
            catch (Exception ex)
            {
                result.EndTime = DateTime.UtcNow;
                result.Duration = result.EndTime - result.StartTime;
                result.ErrorMessage = ex.Message;
                
                _logger.LogError($"Performance-aware optimization failed: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// Applies optimizations individually with per-optimization validation
        /// </summary>
        private async Task<List<IndividualOptimizationResult>> ApplyOptimizationsWithIndividualValidationAsync(
            List<VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization> optimizations,
            OptimizationMode mode,
            bool validatePerformance,
            CancellationToken ct)
        {
            var results = new List<IndividualOptimizationResult>();

            foreach (var optimization in optimizations)
            {
                ct.ThrowIfCancellationRequested();

                var individualResult = new IndividualOptimizationResult
                {
                    OptimizationName = optimization.Name,
                    StartTime = DateTime.UtcNow
                };

                try
                {
                    _logger.Log(LogLevel.Info, LogCategory.Optimization,
                        $"Processing optimization: {optimization.Name}",
                        source: "PerformanceAwareExecutor");

                    // Pre-application validation
                    var preValidationResult = await ValidateOptimizationEligibilityAsync(optimization, mode, ct);
                    individualResult.PreValidationResult = preValidationResult;

                    if (!preValidationResult.IsEligible)
                    {
                        individualResult.Status = OptimizationApplicationStatus.Skipped;
                        individualResult.SkipReason = preValidationResult.Reason;
                        _logger.Log(LogLevel.Info, LogCategory.Optimization,
                            $"Skipping optimization {optimization.Name}: {preValidationResult.Reason}",
                            source: "PerformanceAwareExecutor");
                    }
                    else
                    {
                        // Apply with performance monitoring
                        await ApplySingleOptimizationWithMonitoringAsync(
                            optimization, individualResult, validatePerformance, ct);
                    }
                }
                catch (Exception ex)
                {
                    individualResult.Status = OptimizationApplicationStatus.Failed;
                    individualResult.ErrorMessage = ex.Message;
                    individualResult.Exception = ex;
                    _logger.LogError($"Failed to process optimization {optimization.Name}: {ex.Message}", ex);
                }

                individualResult.EndTime = DateTime.UtcNow;
                individualResult.Duration = individualResult.EndTime - individualResult.StartTime;
                results.Add(individualResult);
            }

            return results;
        }

        /// <summary>
        /// Validates if an optimization is eligible for application
        /// </summary>
        private async Task<PreValidationResult> ValidateOptimizationEligibilityAsync(
            VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization optimization,
            OptimizationMode mode,
            CancellationToken ct)
        {
            var result = new PreValidationResult();

            try
            {
                // Check if already applied
                var currentState = await _stateDetector.CaptureCurrentStateAsync(optimization.Category, ct);
                var status = _stateDetector.AnalyzeOptimizationStatus(optimization, currentState);

                if (status == OptimizationStatus.AlreadyApplied)
                {
                    result.IsEligible = false;
                    result.Reason = "Optimization already applied";
                    return result;
                }

                if (status == OptimizationStatus.Incompatible)
                {
                    result.IsEligible = false;
                    result.Reason = "Hardware/software incompatible";
                    return result;
                }

                // Check mode-specific eligibility
                switch (mode)
                {
                    case OptimizationMode.Conservative:
                        if (status != OptimizationStatus.NeedsApplication)
                        {
                            result.IsEligible = false;
                            result.Reason = "Conservative mode - only applying clearly needed optimizations";
                            return result;
                        }
                        break;

                    case OptimizationMode.Smart:
                        if (status != OptimizationStatus.NeedsApplication)
                        {
                            result.IsEligible = false;
                            result.Reason = "Smart mode - optimization not needed";
                            return result;
                        }
                        break;

                    case OptimizationMode.Force:
                        // Force mode applies everything except incompatible
                        if (status == OptimizationStatus.Incompatible)
                        {
                            result.IsEligible = false;
                            result.Reason = "Incompatible with system";
                            return result;
                        }
                        break;
                }

                result.IsEligible = true;
                return result;
            }
            catch (Exception ex)
            {
                result.IsEligible = false;
                result.Reason = $"Validation error: {ex.Message}";
                return result;
            }
        }

        /// <summary>
        /// Applies a single optimization with performance monitoring
        /// </summary>
        private async Task ApplySingleOptimizationWithMonitoringAsync(
            VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization optimization,
            IndividualOptimizationResult result,
            bool validatePerformance,
            CancellationToken ct)
        {
            try
            {
                // Create rollback point
                var preState = await _stateDetector.CaptureCurrentStateAsync(optimization.Category, ct);
                var rollbackPoint = await _rollbackManager.CreatePreciseRollbackPointAsync(optimization, preState, ct);
                result.RollbackPointId = rollbackPoint.Id;

                // Optional: Establish mini-baseline for this specific optimization
                PerformanceBaseline miniBaseline = null;
                if (validatePerformance)
                {
                    var miniScenario = new PerformanceTestScenario
                    {
                        ScenarioName = $"Pre-{optimization.Name}",
                        Duration = TimeSpan.FromSeconds(10)
                    };
                    miniBaseline = await _performanceValidator.EstablishBaselineAsync(miniScenario, ct);
                }

                // Apply optimization
                await optimization.ApplyAction(ct);
                result.ApplyCompleted = true;

                // Post-application validation
                var postState = await _stateDetector.CaptureCurrentStateAsync(optimization.Category, ct);
                var validationResult = ValidateOptimizationApplied(optimization, preState, postState);
                result.ValidationResult = validationResult;

                // Performance validation for this optimization
                if (validatePerformance && miniBaseline != null)
                {
                    var miniValidation = await _performanceValidator.ValidatePerformanceAsync(
                        miniBaseline, TimeSpan.FromSeconds(10), ct);

                    result.PerformanceImpact = miniValidation.Analysis;

                    if (!_performanceValidator.IsPerformanceDegradationAcceptable(miniValidation.Analysis))
                    {
                        _logger.Log(LogLevel.Warning, LogCategory.Optimization,
                            $"Performance degradation detected for {optimization.Name} - rolling back",
                            null, "PerformanceAwareExecutor");

                        await _rollbackManager.ExecutePreciseRollbackAsync(rollbackPoint, ct);
                        result.RollbackPerformed = true;
                        result.RollbackSuccess = true;
                        result.Status = OptimizationApplicationStatus.RolledbackDueToPerformance;
                        return;
                    }
                }

                result.Status = validationResult.Success ? 
                    OptimizationApplicationStatus.Applied : 
                    OptimizationApplicationStatus.ValidationFailed;
            }
            catch (Exception ex)
            {
                result.Status = OptimizationApplicationStatus.Failed;
                result.ErrorMessage = ex.Message;
                result.Exception = ex;

                // Attempt rollback on failure
                if (!string.IsNullOrEmpty(result.RollbackPointId))
                {
                    try
                    {
                        var rollbackSuccess = await _rollbackManager.ExecutePreciseRollbackAsync(
                            new VoltrisOptimizer.Services.SystemChanges.RollbackPoint { Id = result.RollbackPointId }, ct);
                        result.RollbackPerformed = true;
                        result.RollbackSuccess = rollbackSuccess;
                    }
                    catch (Exception rollbackEx)
                    {
                        _logger.LogError($"Emergency rollback failed for {optimization.Name}: {rollbackEx.Message}", rollbackEx);
                    }
                }
            }
        }

        /// <summary>
        /// Automatically rolls back all optimizations due to performance degradation
        /// </summary>
        private async Task AutoRollbackAllOptimizationsAsync(
            PerformanceAwareOptimizationResult result,
            CancellationToken ct)
        {
            _logger.Log(LogLevel.Warning, LogCategory.Optimization,
                "Auto-rollback initiated due to performance degradation",
                source: "PerformanceAwareExecutor");

            foreach (var optResult in result.OptimizationResults.Where(r => r.RollbackPointId != null))
            {
                try
                {
                    var rollbackSuccess = await _rollbackManager.ExecutePreciseRollbackAsync(
                        new VoltrisOptimizer.Services.SystemChanges.RollbackPoint { Id = optResult.RollbackPointId }, ct);
                    
                    optResult.AutoRollbackPerformed = true;
                    optResult.AutoRollbackSuccess = rollbackSuccess;

                    _logger.Log(LogLevel.Info, LogCategory.Optimization,
                        $"Auto-rollback for {optResult.OptimizationName}: {(rollbackSuccess ? "SUCCESS" : "FAILED")}",
                        source: "PerformanceAwareExecutor");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Auto-rollback failed for {optResult.OptimizationName}: {ex.Message}", ex);
                    optResult.AutoRollbackSuccess = false;
                }
            }
        }

        /// <summary>
        /// Validates that an optimization was properly applied
        /// </summary>
        private ValidationResult ValidateOptimizationApplied(
            VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization optimization,
            SystemStateSnapshot preState,
            SystemStateSnapshot postState)
        {
            var result = new ValidationResult();

            try
            {
                // Validate registry changes
                if (optimization.TargetRegistryValues?.Any() == true)
                {
                    foreach (var targetReg in optimization.TargetRegistryValues)
                    {
                        if (postState.RegistryStates.TryGetValue(targetReg.Key, out var currentState))
                        {
                            if (!currentState.Exists || !Equals(currentState.ValueData, targetReg.Value))
                            {
                                result.Errors.Add($"Registry value {targetReg.Key} not set correctly");
                            }
                        }
                        else
                        {
                            result.Errors.Add($"Registry value {targetReg.Key} missing after application");
                        }
                    }
                }

                // Validate service states
                if (optimization.TargetServiceStates?.Any() == true)
                {
                    foreach (var targetSvc in optimization.TargetServiceStates)
                    {
                        if (postState.ServiceStates.TryGetValue(targetSvc.Key, out var currentState))
                        {
                            if (!currentState.Exists || currentState.Status != targetSvc.Value)
                            {
                                result.Errors.Add($"Service {targetSvc.Key} not in expected state");
                            }
                        }
                        else
                        {
                            result.Errors.Add($"Service {targetSvc.Key} missing after application");
                        }
                    }
                }

                result.Success = result.Errors.Count == 0;
                result.ErrorMessage = result.Success ? null : string.Join("; ", result.Errors);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = $"Validation error: {ex.Message}";
                result.Errors.Add(result.ErrorMessage);
            }

            return result;
        }
    }

    // Result models
    public class PerformanceAwareOptimizationResult
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public OptimizationMode Mode { get; set; }
        public bool PerformanceValidationEnabled { get; set; }
        public bool PerformanceDegradationDetected { get; set; }
        public string ErrorMessage { get; set; }

        public PerformanceBaseline Baseline { get; set; }
        public PerformanceValidationResult PostValidation { get; set; }
        public List<IndividualOptimizationResult> OptimizationResults { get; set; } = new();

        public int AppliedCount => OptimizationResults.Count(r => r.Status == OptimizationApplicationStatus.Applied);
        public int FailedCount => OptimizationResults.Count(r => r.Status == OptimizationApplicationStatus.Failed);
        public int SkippedCount => OptimizationResults.Count(r => r.Status == OptimizationApplicationStatus.Skipped);
        public int RollbackCount => OptimizationResults.Count(r => r.AutoRollbackSuccess);
    }

    public class IndividualOptimizationResult
    {
        public string OptimizationName { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public OptimizationApplicationStatus Status { get; set; }
        public string SkipReason { get; set; }
        public string ErrorMessage { get; set; }
        public Exception Exception { get; set; }

        public string RollbackPointId { get; set; }
        public bool ApplyCompleted { get; set; }
        public bool RollbackPerformed { get; set; }
        public bool RollbackSuccess { get; set; }
        public bool AutoRollbackPerformed { get; set; }
        public bool AutoRollbackSuccess { get; set; }

        public PreValidationResult PreValidationResult { get; set; }
        public ValidationResult ValidationResult { get; set; }
        public PerformanceAnalysis PerformanceImpact { get; set; }
    }

    public class PreValidationResult
    {
        public bool IsEligible { get; set; }
        public string Reason { get; set; }
    }

    public enum OptimizationApplicationStatus
    {
        Applied,
        Skipped,
        Failed,
        ValidationFailed,
        RolledbackDueToPerformance
    }
}
