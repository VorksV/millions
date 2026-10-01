using System;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.SystemIntelligenceProfiler;
using VoltrisOptimizer.Core.SystemIntelligenceProfiler.Models;
using VoltrisOptimizer.Core.Models;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Adaptive;
using VoltrisOptimizer.Services.SystemChanges;
using VoltrisOptimizer.UI.ViewModels;

namespace VoltrisOptimizer.Core.Integration
{
    /// <summary>
    /// Main integration point for the Intelligent Profile system
    /// Coordinates all components and provides unified interface
    /// </summary>
    public class IntelligentProfileSystem
    {
        private readonly StateDetectionEngine _stateDetector;
        private readonly IntelligentOptimizationExecutor _executor;
        private readonly EnhancedRollbackManager _rollbackManager;
        private readonly StructuredOptimizationLogger _structuredLogger;
        private readonly AdaptiveOptimizationProfile _adaptiveProfile;
        private readonly ILoggingService _logger;
        private readonly ISystemInfoService _systemInfoService;

        public IntelligentProfileSystem(
            StateDetectionEngine stateDetector,
            IntelligentOptimizationExecutor executor,
            EnhancedRollbackManager rollbackManager,
            StructuredOptimizationLogger structuredLogger,
            AdaptiveOptimizationProfile adaptiveProfile,
            ILoggingService logger,
            ISystemInfoService systemInfoService)
        {
            _stateDetector = stateDetector ?? throw new ArgumentNullException(nameof(stateDetector));
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _rollbackManager = rollbackManager ?? throw new ArgumentNullException(nameof(rollbackManager));
            _structuredLogger = structuredLogger ?? throw new ArgumentNullException(nameof(structuredLogger));
            _adaptiveProfile = adaptiveProfile ?? throw new ArgumentNullException(nameof(adaptiveProfile));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _systemInfoService = systemInfoService ?? throw new ArgumentNullException(nameof(systemInfoService));
        }

        /// <summary>
        /// Executes intelligent optimization with full integration
        /// </summary>
        public async Task<IntegratedOptimizationResult> ExecuteIntelligentOptimizationAsync(
            OptimizationMode mode = OptimizationMode.Smart,
            CancellationToken ct = default)
        {
            try
            {
                _logger.Log(LogLevel.Info, LogCategory.General,
                    $"Starting integrated intelligent optimization in {mode} mode", 
                    source: "IntelligentProfileSystem");

                var sessionId = Guid.NewGuid().ToString();
                var startTime = DateTime.UtcNow;

                // Step 1: System analysis
                var hardwareProfile = await DetectHardwareProfileAsync(ct);
                var systemLoad = await GetCurrentSystemLoadAsync(ct);

                _logger.Log(LogLevel.Info, LogCategory.General,
                    $"Hardware: {hardwareProfile.Classification}, Load: {systemLoad.CpuUsagePercent:F1}%", 
                    source: "IntelligentProfileSystem");

                // Step 2: Adaptive profile calculation
                var userPreference = GetUserPreference();
                var optimalIntensity = _adaptiveProfile.CalculateOptimalIntensity(
                    hardwareProfile, systemLoad, userPreference);

                _logger.Log(LogLevel.Info, LogCategory.General,
                    $"Calculated optimal intensity: CPU={optimalIntensity.CpuOptimization}, Memory={optimalIntensity.MemoryOptimization}",
                    source: "IntelligentProfileSystem");

                // Step 3: Create optimization recommendations
                var recommendations = GenerateOptimizations(hardwareProfile, optimalIntensity);

                // Step 4: Execute optimizations
                var executionResult = await _executor.ApplyOptimizationsAsync(
                    recommendations, mode, ct);

                // Step 5: Log structured events
                await LogExecutionEventsAsync(executionResult, sessionId, hardwareProfile);

                var endTime = DateTime.UtcNow;
                
                var result = new IntegratedOptimizationResult
                {
                    SessionId = sessionId,
                    StartTime = startTime,
                    EndTime = endTime,
                    Duration = endTime - startTime,
                    HardwareProfile = hardwareProfile,
                    SystemLoad = systemLoad,
                    OptimalIntensity = optimalIntensity,
                    ExecutionResult = executionResult,
                    Mode = mode
                };

                _logger.Log(LogLevel.Success, LogCategory.General,
                    $"Intelligent optimization completed: {executionResult.TotalApplied} applied, {executionResult.TotalFailed} failed",
                    source: "IntelligentProfileSystem");

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Intelligent optimization failed: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// Validates system health and compatibility
        /// </summary>
        public async Task<SystemValidationReport> ValidateSystemAsync(CancellationToken ct = default)
        {
            try
            {
                _logger.Log(LogLevel.Info, LogCategory.General,
                    "Performing system validation", source: "IntelligentProfileSystem");

                var report = new SystemValidationReport();
                var hardwareProfile = await DetectHardwareProfileAsync(ct);

                // Hardware validation
                report.HardwareValidation = ValidateHardware(hardwareProfile);
                
                // Current state analysis
                var systemCategory = new OptimizationCategory { Name = "System" };
                var currentState = await _stateDetector.CaptureCurrentStateAsync(systemCategory, ct);
                report.StateAnalysis = currentState;

                // Compatibility checks
                report.CompatibilityIssues = CheckCompatibilityIssues(hardwareProfile, currentState);

                report.IsValid = report.HardwareValidation.IsValid && 
                               report.CompatibilityIssues.Count == 0;

                _logger.Log(LogLevel.Info, LogCategory.General,
                    $"System validation complete: Valid={report.IsValid}", source: "IntelligentProfileSystem");

                return report;
            }
            catch (Exception ex)
            {
                _logger.LogError($"System validation failed: {ex.Message}", ex);
                return new SystemValidationReport { IsValid = false, ErrorMessage = ex.Message };
            }
        }

        // Private helper methods
        private async Task<HardwareProfile> DetectHardwareProfileAsync(CancellationToken ct)
        {
            var dummyCategory = new OptimizationCategory { Name = "Hardware Detection" };
            var snapshot = await _stateDetector.CaptureCurrentStateAsync(dummyCategory, ct);
            return snapshot.HardwareProfile;
        }

        private async Task<VoltrisOptimizer.Services.Adaptive.SystemLoad> GetCurrentSystemLoadAsync(CancellationToken ct)
        {
            var cpuUsage = await _systemInfoService.GetCpuUsageAsync();
            var memoryUsage = await _systemInfoService.GetMemoryUsageAsync();
            
            return new VoltrisOptimizer.Services.Adaptive.SystemLoad
            {
                CpuUsagePercent = cpuUsage,
                MemoryUsagePercent = memoryUsage
            };
        }

        private UserPreference GetUserPreference()
        {
            var settings = SettingsService.Instance.Settings;
            var profileType = settings.IntelligentProfile;

            return new UserPreference
            {
                OptimizationPreference = MapProfileToPreference(profileType),
                RiskTolerance = MapProfileToRisk(profileType)
            };
        }

        private UserOptimizationPreference MapProfileToPreference(IntelligentProfileType type)
        {
            return type switch
            {
                IntelligentProfileType.GamerCompetitive => UserOptimizationPreference.Performance,
                IntelligentProfileType.GamerSinglePlayer => UserOptimizationPreference.Balanced,
                IntelligentProfileType.WorkOffice => UserOptimizationPreference.Balanced,
                IntelligentProfileType.CreativeVideoEditing => UserOptimizationPreference.Performance,
                IntelligentProfileType.DeveloperProgramming => UserOptimizationPreference.Balanced,
                IntelligentProfileType.GeneralBalanced => UserOptimizationPreference.Balanced,
                IntelligentProfileType.EnterpriseSecure => UserOptimizationPreference.Balanced,
                _ => UserOptimizationPreference.Balanced
            };
        }

        private RiskTolerance MapProfileToRisk(IntelligentProfileType type)
        {
            return type switch
            {
                IntelligentProfileType.GamerCompetitive => RiskTolerance.High,
                IntelligentProfileType.GamerSinglePlayer => RiskTolerance.Medium,
                IntelligentProfileType.WorkOffice => RiskTolerance.Low,
                IntelligentProfileType.CreativeVideoEditing => RiskTolerance.Medium,
                IntelligentProfileType.DeveloperProgramming => RiskTolerance.Medium,
                IntelligentProfileType.GeneralBalanced => RiskTolerance.Medium,
                IntelligentProfileType.EnterpriseSecure => RiskTolerance.Low,
                _ => RiskTolerance.Medium
            };
        }

        private VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization[] GenerateOptimizations(HardwareProfile hardware, OptimizationIntensity intensity)
        {
            // Generate appropriate optimizations based on hardware and intensity
            var optimizations = new System.Collections.Generic.List<VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization>();

            // Memory optimizations
            if (intensity.MemoryOptimization >= OptimizationLevel.Moderate)
            {
                optimizations.Add(new VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization
                {
                    Name = "Memory Management Optimization",
                    Description = "Optimize memory allocation and caching strategies",
                    Category = new OptimizationCategory { Name = "Memory" },
                    TargetRegistryValues = new System.Collections.Generic.Dictionary<string, object>
                    {
                        { @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\LargeSystemCache", 0 }
                    },
                    MinRamGb = 4
                });
            }

            // CPU optimizations
            if (intensity.CpuOptimization >= OptimizationLevel.Aggressive)
            {
                optimizations.Add(new VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization
                {
                    Name = "CPU Scheduling Optimization",
                    Description = "Optimize processor scheduling and affinity",
                    Category = new OptimizationCategory { Name = "CPU" },
                    TargetRegistryValues = new System.Collections.Generic.Dictionary<string, object>
                    {
                        { @"HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl\Win32PrioritySeparation", 38 }
                    },
                    MinCpuCores = 2
                });
            }

            // Storage optimizations
            if (intensity.StorageOptimization >= OptimizationLevel.Moderate && 
                hardware.Storage?.Type?.Contains("SSD") == true)
            {
                optimizations.Add(new VoltrisOptimizer.Core.SystemIntelligenceProfiler.Optimization
                {
                    Name = "SSD Optimization",
                    Description = "Optimize storage settings for solid state drives",
                    Category = new OptimizationCategory { Name = "Storage" },
                    TargetRegistryValues = new System.Collections.Generic.Dictionary<string, object>
                    {
                        { @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\DisableTaskOffload", 0 }
                    },
                    RequiresSSD = true
                });
            }

            return optimizations.ToArray();
        }

        private async Task LogExecutionEventsAsync(
            IntelligentOptimizationResult result, 
            string sessionId, 
            HardwareProfile hardwareProfile)
        {
            foreach (var executionResult in result.ExecutionResults)
            {
                var evt = OptimizationEventFactory.CreateApplicationEvent(
                    executionResult.OptimizationName,
                    executionResult.OptimizationName.Contains("Memory") ? 
                        new OptimizationCategory { Name = "Memory" } :
                        new OptimizationCategory { Name = "General" },
                    hardwareProfile,
                    null, // preState would be captured during execution
                    null, // postState would be captured during execution
                    new VoltrisOptimizer.Core.Models.ValidationResult 
                    { 
                        Success = executionResult.ValidationResult.Success, 
                        ErrorMessage = executionResult.ValidationResult.ErrorMessage,
                        Errors = executionResult.ValidationResult.Errors 
                    },
                    executionResult.Duration,
                    executionResult.Status == OptimizationExecutionStatus.Success,
                    Environment.UserName,
                    sessionId);

                _structuredLogger.LogOptimizationEvent(evt);
            }
        }

        private HardwareValidationResult ValidateHardware(HardwareProfile hardware)
        {
            var result = new HardwareValidationResult();
            
            result.Score = hardware.HardwareScore;
            result.Classification = hardware.Classification;
            result.IsValid = hardware.Classification >= HardwareClass.MidRange;
            
            if (!result.IsValid)
            {
                result.Issues.Add("Hardware classification is below minimum requirement (MidRange)");
            }

            if ((hardware.Ram?.TotalMb ?? 0) < 2048)
            {
                result.Issues.Add("Insufficient RAM (minimum 2GB recommended)");
                result.IsValid = false;
            }

            return result;
        }

        private System.Collections.Generic.List<string> CheckCompatibilityIssues(
            HardwareProfile hardware, 
            SystemStateSnapshot state)
        {
            var issues = new System.Collections.Generic.List<string>();

            // Check for conflicting services
            if (state.ServiceStates.ContainsKey("SysMain") && 
                state.ServiceStates["SysMain"].Status == System.ServiceProcess.ServiceControllerStatus.Running)
            {
                issues.Add("Superfetch service is running - may conflict with memory optimizations");
            }

            // Check for existing registry modifications
            var prefetchKey = @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters\EnablePrefetcher";
            if (state.RegistryStates.ContainsKey(prefetchKey) &&
                state.RegistryStates[prefetchKey].Exists &&
                Equals(state.RegistryStates[prefetchKey].ValueData, 0))
            {
                issues.Add("Prefetch is already disabled");
            }

            return issues;
        }
    }

    /// <summary>
    /// Result of integrated optimization execution
    /// </summary>
    public class IntegratedOptimizationResult
    {
        public string SessionId { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public HardwareProfile HardwareProfile { get; set; }
        public VoltrisOptimizer.Services.Adaptive.SystemLoad SystemLoad { get; set; }
        public OptimizationIntensity OptimalIntensity { get; set; }
        public IntelligentOptimizationResult ExecutionResult { get; set; }
        public OptimizationMode Mode { get; set; }
    }

    /// <summary>
    /// System validation report
    /// </summary>
    public class SystemValidationReport
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; }
        public HardwareValidationResult HardwareValidation { get; set; }
        public SystemStateSnapshot StateAnalysis { get; set; }
        public System.Collections.Generic.List<string> CompatibilityIssues { get; set; } = new();
    }

    /// <summary>
    /// Hardware validation result
    /// </summary>
    public class HardwareValidationResult
    {
        public bool IsValid { get; set; }
        public double Score { get; set; }
        public HardwareClass Classification { get; set; }
        public System.Collections.Generic.List<string> Issues { get; set; } = new();
    }

    // Mock implementations removed - were dead code (CreateApplyAllViewModelAsync was never called)


}
