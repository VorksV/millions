using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.SystemIntelligenceProfiler;
using VoltrisOptimizer.Core.SystemIntelligenceProfiler.Models;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// Intelligent optimization executor with pre-validation and deterministic behavior
    /// Ensures optimizations are only applied when needed and provides reliable rollback
    /// </summary>
    public class IntelligentOptimizationExecutor
    {
        private readonly StateDetectionEngine _stateDetector;
        private readonly ILoggingService _logger;
        private readonly VoltrisOptimizer.Services.SystemChanges.IRollbackManager _rollbackManager;

        public IntelligentOptimizationExecutor(
            StateDetectionEngine stateDetector,
            ILoggingService logger,
            VoltrisOptimizer.Services.SystemChanges.IRollbackManager rollbackManager)
        {
            _stateDetector = stateDetector ?? throw new ArgumentNullException(nameof(stateDetector));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _rollbackManager = rollbackManager ?? throw new ArgumentNullException(nameof(rollbackManager));
        }

        /// <summary>
        /// Applies optimizations intelligently with pre-validation and rollback capability
        /// </summary>
        /// <param name="optimizations">Collection of optimizations to apply</param>
        /// <param name="mode">Optimization mode (Smart, Force, Conservative)</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Detailed results of optimization execution</returns>
        public virtual async Task<IntelligentOptimizationResult> ApplyOptimizationsAsync(
            IEnumerable<VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization> optimizations,
            OptimizationMode mode = OptimizationMode.Smart,
            CancellationToken ct = default)
        {
            var result = new IntelligentOptimizationResult
            {
                StartTime = DateTime.UtcNow,
                Mode = mode
            };

            _logger.Log(LogLevel.Info, LogCategory.Optimization,
                $"Starting intelligent optimization execution with {optimizations.Count()} optimizations in {mode} mode",
                source: "IntelligentExecutor");

            var optimizationList = optimizations.ToList();

            try
            {
                // Phase 1: Pre-execution analysis
                var analysisResults = await PerformPreExecutionAnalysisAsync(optimizationList, ct);
                result.AnalysisResults = analysisResults;

                // Filter optimizations based on analysis and mode
                var filteredOptimizations = FilterOptimizations(optimizationList, analysisResults, mode);

                _logger.Log(LogLevel.Info, LogCategory.Optimization,
                    $"Filtered to {filteredOptimizations.Count} optimizations for execution",
                    source: "IntelligentExecutor");

                // Phase 2: Sequential execution with validation
                foreach (var optimization in filteredOptimizations)
                {
                    ct.ThrowIfCancellationRequested();

                    var executionResult = await ExecuteSingleOptimizationAsync(optimization, ct);
                    result.AddExecutionResult(executionResult);

                    // Update progress
                    result.Progress = (double)result.TotalProcessed / optimizationList.Count;
                }

                result.EndTime = DateTime.UtcNow;
                result.Duration = result.EndTime - result.StartTime;

                _logger.Log(LogLevel.Success, LogCategory.Optimization,
                    $"Optimization execution completed. Applied: {result.Applied.Count}, " +
                    $"Skipped: {result.Skipped.Count}, Failed: {result.Failed.Count}",
                    source: "IntelligentExecutor");

                return result;
            }
            catch (OperationCanceledException)
            {
                result.WasCancelled = true;
                _logger.Log(LogLevel.Warning, LogCategory.Optimization,
                    "Optimization execution was cancelled by user", source: "IntelligentExecutor");
                throw;
            }
            catch (Exception ex)
            {
                result.EndTime = DateTime.UtcNow;
                result.Duration = result.EndTime - result.StartTime;
                result.ErrorMessage = ex.Message;
                
                _logger.LogError($"Critical error during optimization execution: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// Performs comprehensive pre-execution analysis of all optimizations
        /// </summary>
        private async Task<List<OptimizationAnalysis>> PerformPreExecutionAnalysisAsync(
            List<VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization> optimizations, CancellationToken ct)
        {
            var results = new List<OptimizationAnalysis>();

            foreach (var optimization in optimizations)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var analysis = new OptimizationAnalysis
                    {
                        OptimizationName = optimization.Name,
                        Category = optimization.Category.Name
                    };

                    // Capture current state
                    var currentState = await _stateDetector.CaptureCurrentStateAsync(optimization.Category, ct);
                    analysis.CurrentState = currentState;

                    // Analyze optimization status
                    var status = _stateDetector.AnalyzeOptimizationStatus(optimization, currentState);
                    analysis.Status = status;

                    // Additional analysis
                    analysis.RequiresElevation = optimization.RequiresAdmin;
                    analysis.HardwareCompatibility = AnalyzeHardwareCompatibility(optimization, currentState.HardwareProfile);
                    analysis.EstimatedDuration = TimeSpan.FromMilliseconds(100 + (optimization.TargetRegistryValues?.Count ?? 0) * 50);

                    results.Add(analysis);

                    _logger.Log(LogLevel.Debug, LogCategory.Optimization,
                        $"Analysis complete for {optimization.Name}: {status}", source: "IntelligentExecutor");
                }
                catch (Exception ex)
                {
                    results.Add(new OptimizationAnalysis
                    {
                        OptimizationName = optimization.Name,
                        Category = optimization.Category.Name,
                        Status = OptimizationStatus.Unknown,
                        ErrorMessage = ex.Message
                    });

                    _logger.LogWarning($"Failed to analyze {optimization.Name}: {ex.Message}");
                }
            }

            return results;
        }

        /// <summary>
        /// Filters optimizations based on analysis results and execution mode
        /// </summary>
        private List<VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization> FilterOptimizations(
            List<VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization> optimizations,
            List<OptimizationAnalysis> analysisResults,
            OptimizationMode mode)
        {
            var filtered = new List<VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization>();

            for (int i = 0; i < optimizations.Count; i++)
            {
                var optimization = optimizations[i];
                var analysis = analysisResults[i];

                switch (mode)
                {
                    case OptimizationMode.Smart:
                        // Only apply optimizations that need application
                        if (analysis.Status == OptimizationStatus.NeedsApplication)
                        {
                            filtered.Add(optimization);
                        }
                        break;

                    case OptimizationMode.Force:
                        // Apply all optimizations except incompatible ones
                        if (analysis.Status != OptimizationStatus.Incompatible)
                        {
                            filtered.Add(optimization);
                        }
                        break;

                    case OptimizationMode.Conservative:
                        // Only apply optimizations that are definitely needed and compatible
                        if (analysis.Status == OptimizationStatus.NeedsApplication &&
                            analysis.HardwareCompatibility == CompatibilityLevel.Compatible)
                        {
                            filtered.Add(optimization);
                        }
                        break;
                }
            }

            return filtered;
        }

        /// <summary>
        /// Executes a single optimization with full validation and rollback capability
        /// </summary>
        private async Task<OptimizationExecutionResult> ExecuteSingleOptimizationAsync(
            VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization optimization, CancellationToken ct)
        {
            var result = new OptimizationExecutionResult
            {
                OptimizationName = optimization.Name,
                StartTime = DateTime.UtcNow
            };

            try
            {
                _logger.Log(LogLevel.Info, LogCategory.Optimization,
                    $"Executing optimization: {optimization.Name}", source: "IntelligentExecutor");

                // Step 1: Create rollback point
                var preState = await _stateDetector.CaptureCurrentStateAsync(optimization.Category, ct);
                var rollbackPoint = await _rollbackManager.CreatePreciseRollbackPointAsync(optimization, preState, ct);
                result.RollbackPointId = rollbackPoint.Id;

                // Step 2: Apply optimization
                await optimization.ApplyAction(ct);
                result.ApplyCompleted = true;

                // Step 3: Post-execution validation
                var postState = await _stateDetector.CaptureCurrentStateAsync(optimization.Category, ct);
                var validationResult = ValidateOptimizationApplied(optimization, preState, postState);

                result.ValidationResult = validationResult;
                result.EndTime = DateTime.UtcNow;
                result.Duration = result.EndTime - result.StartTime;

                if (validationResult.Success)
                {
                    result.Status = OptimizationExecutionStatus.Success;
                    _logger.Log(LogLevel.Success, LogCategory.Optimization,
                        $"Successfully applied {optimization.Name}", source: "IntelligentExecutor");
                }
                else
                {
                    // Validation failed - attempt rollback
                    _logger.LogWarning($"Validation failed for {optimization.Name}: {validationResult.ErrorMessage}");
                    
                    var rollbackSuccess = await _rollbackManager.ExecutePreciseRollbackAsync(rollbackPoint, ct);
                    result.RollbackPerformed = true;
                    result.RollbackSuccess = rollbackSuccess;

                    if (rollbackSuccess)
                    {
                        result.Status = OptimizationExecutionStatus.ValidationFailedWithRollback;
                        _logger.Log(LogLevel.Warning, LogCategory.Optimization,
                            $"Rolled back {optimization.Name} after validation failure", source: "IntelligentExecutor");
                    }
                    else
                    {
                        result.Status = OptimizationExecutionStatus.ValidationFailedNoRollback;
                        _logger.LogError($"Failed to rollback {optimization.Name} after validation failure", null);
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                result.EndTime = DateTime.UtcNow;
                result.Duration = result.EndTime - result.StartTime;
                result.ErrorMessage = ex.Message;
                result.Exception = ex;
                result.Status = OptimizationExecutionStatus.Failed;

                _logger.LogError($"Failed to execute {optimization.Name}: {ex.Message}", ex);

                // Attempt emergency rollback
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
                        _logger.LogError($"Emergency rollback also failed for {optimization.Name}: {rollbackEx.Message}", rollbackEx);
                    }
                }

                return result;
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

                // Validate file states
                if (optimization.TargetFileStates?.Any() == true)
                {
                    foreach (var targetFile in optimization.TargetFileStates)
                    {
                        if (postState.FileStates.TryGetValue(targetFile.Key, out var currentState))
                        {
                            if (!currentState.Exists || currentState.Hash != targetFile.Value)
                            {
                                result.Errors.Add($"File {targetFile.Key} not in expected state");
                            }
                        }
                        else
                        {
                            result.Errors.Add($"File {targetFile.Key} missing after application");
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

        /// <summary>
        /// Analyzes hardware compatibility for an optimization
        /// </summary>
        private CompatibilityLevel AnalyzeHardwareCompatibility(VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization optimization, HardwareProfile hardware)
        {
            // Check SSD requirement
            if (optimization.RequiresSSD && (hardware.Storage?.Type?.Contains("SSD", StringComparison.OrdinalIgnoreCase) != true))
                return CompatibilityLevel.Incompatible;

            // Check RAM requirement
            if (optimization.MinRamGb > 0 && (hardware.Ram?.TotalMb ?? 0) < optimization.MinRamGb * 1024)
                return CompatibilityLevel.Incompatible;

            // Check CPU cores requirement
            if (optimization.MinCpuCores > 0 && (hardware.Cpu?.LogicalCores ?? 0) < optimization.MinCpuCores)
                return CompatibilityLevel.Incompatible;

            // Check if hardware is borderline for this optimization
            if (hardware.Classification <= HardwareClass.MidRange && 
                (optimization.MinRamGb > 4 || optimization.MinCpuCores > 4))
                return CompatibilityLevel.Borderline;

            return CompatibilityLevel.Compatible;
        }
    }

    /// <summary>
    /// Result of optimization execution
    /// </summary>
    public class IntelligentOptimizationResult
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public OptimizationMode Mode { get; set; }
        public double Progress { get; set; }
        public bool WasCancelled { get; set; }
        public string ErrorMessage { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }

        public List<OptimizationAnalysis> AnalysisResults { get; set; } = new();
        public List<OptimizationExecutionResult> ExecutionResults { get; set; } = new();

        public List<OptimizationExecutionResult> Applied => 
            ExecutionResults.Where(r => r.Status == OptimizationExecutionStatus.Success).ToList();
        
        public List<OptimizationExecutionResult> Skipped => 
            ExecutionResults.Where(r => r.Status == OptimizationExecutionStatus.Skipped).ToList();
        
        public List<OptimizationExecutionResult> Failed => 
            ExecutionResults.Where(r => r.Status == OptimizationExecutionStatus.Failed ||
                                      r.Status == OptimizationExecutionStatus.ValidationFailedWithRollback ||
                                      r.Status == OptimizationExecutionStatus.ValidationFailedNoRollback).ToList();

        public int TotalProcessed => ExecutionResults.Count;
        public int TotalApplied => Applied.Count;
        public int TotalSkipped => Skipped.Count;
        public int TotalFailed => Failed.Count;

        public void AddExecutionResult(OptimizationExecutionResult result)
        {
            ExecutionResults.Add(result);
        }
    }

    /// <summary>
    /// Analysis result for a single optimization
    /// </summary>
    public class OptimizationAnalysis
    {
        public string OptimizationName { get; set; }
        public string Category { get; set; }
        public OptimizationStatus Status { get; set; }
        public bool RequiresElevation { get; set; }
        public CompatibilityLevel HardwareCompatibility { get; set; }
        public TimeSpan EstimatedDuration { get; set; }
        public SystemStateSnapshot CurrentState { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Result of executing a single optimization
    /// </summary>
    public class OptimizationExecutionResult
    {
        public string OptimizationName { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public OptimizationExecutionStatus Status { get; set; }
        public string RollbackPointId { get; set; }
        public bool ApplyCompleted { get; set; }
        public bool RollbackPerformed { get; set; }
        public bool RollbackSuccess { get; set; }
        public ValidationResult ValidationResult { get; set; }
        public string ErrorMessage { get; set; }
        public Exception Exception { get; set; }
    }

    /// <summary>
    /// Validation result for optimization application
    /// </summary>
    public class ValidationResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public List<string> Errors { get; set; } = new();
    }

    /// <summary>
    /// Hardware compatibility levels
    /// </summary>
    public enum CompatibilityLevel
    {
        Compatible,
        Borderline,
        Incompatible
    }

    /// <summary>
    /// Optimization execution modes
    /// </summary>
    public enum OptimizationMode
    {
        Smart,        // Only apply optimizations that need it
        Force,        // Apply all optimizations except incompatible
        Conservative  // Only apply safe, compatible optimizations
    }

    /// <summary>
    /// Status of optimization execution
    /// </summary>
    public enum OptimizationExecutionStatus
    {
        Success,
        Skipped,
        Failed,
        ValidationFailedWithRollback,
        ValidationFailedNoRollback
    }
}
