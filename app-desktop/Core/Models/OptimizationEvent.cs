using System;
using VoltrisOptimizer.Core.SystemIntelligenceProfiler.Models;

namespace VoltrisOptimizer.Core.Models
{
    /// <summary>
    /// Comprehensive optimization event model for enterprise-grade audit trails
    /// Captures all relevant context for optimization operations
    /// </summary>
    public class OptimizationEvent
    {
        /// <summary>
        /// UTC timestamp when the event occurred
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Type of optimization event
        /// </summary>
        public OptimizationEventType Type { get; set; }

        /// <summary>
        /// Name of the optimization being processed
        /// </summary>
        public string OptimizationName { get; set; }

        /// <summary>
        /// Category of the optimization
        /// </summary>
        public OptimizationCategory Category { get; set; }

        /// <summary>
        /// Hardware profile at time of event
        /// </summary>
        public HardwareProfile HardwareProfile { get; set; }

        /// <summary>
        /// System state before the operation
        /// </summary>
        public SystemStateSnapshot PreState { get; set; }

        /// <summary>
        /// System state after the operation
        /// </summary>
        public SystemStateSnapshot PostState { get; set; }

        /// <summary>
        /// Validation result of the operation
        /// </summary>
        public ValidationResult ValidationResult { get; set; }

        /// <summary>
        /// Execution time of the operation
        /// </summary>
        public TimeSpan ExecutionTime { get; set; }

        /// <summary>
        /// User identifier (if applicable)
        /// </summary>
        public string UserId { get; set; }

        /// <summary>
        /// Session identifier for grouping related operations
        /// </summary>
        public string SessionId { get; set; }

        /// <summary>
        /// Machine identifier for multi-machine environments
        /// </summary>
        public string MachineId { get; set; }

        /// <summary>
        /// Exception details if operation failed
        /// </summary>
        public Exception Exception { get; set; }

        /// <summary>
        /// Additional metadata/context for the event
        /// </summary>
        public object Metadata { get; set; }

        /// <summary>
        /// Severity level of the event
        /// </summary>
        public EventSeverity Severity { get; set; }

        /// <summary>
        /// Indicates if this event represents a critical operation
        /// </summary>
        public bool IsCritical { get; set; }

        /// <summary>
        /// Version of the optimization engine that generated this event
        /// </summary>
        public string EngineVersion { get; set; }

        /// <summary>
        /// Operating system information
        /// </summary>
        public OsInfo OperatingSystem { get; set; }

        /// <summary>
        /// Creates a copy of this event with updated timestamp
        /// </summary>
        public OptimizationEvent CloneWithNewTimestamp()
        {
            return new OptimizationEvent
            {
                Timestamp = DateTime.UtcNow,
                Type = this.Type,
                OptimizationName = this.OptimizationName,
                Category = this.Category,
                HardwareProfile = this.HardwareProfile,
                PreState = this.PreState,
                PostState = this.PostState,
                ValidationResult = this.ValidationResult,
                ExecutionTime = this.ExecutionTime,
                UserId = this.UserId,
                SessionId = this.SessionId,
                MachineId = this.MachineId,
                Exception = this.Exception,
                Metadata = this.Metadata,
                Severity = this.Severity,
                IsCritical = this.IsCritical,
                EngineVersion = this.EngineVersion,
                OperatingSystem = this.OperatingSystem
            };
        }
    }

    /// <summary>
    /// Types of optimization events that can be logged
    /// </summary>
    public enum OptimizationEventType
    {
        /// <summary>
        /// System state detection and analysis
        /// </summary>
        StateDetection,

        /// <summary>
        /// Pre-execution validation of optimization requirements
        /// </summary>
        PreValidation,

        /// <summary>
        /// Optimization application has started
        /// </summary>
        ApplicationStarted,

        /// <summary>
        /// Optimization application has completed successfully
        /// </summary>
        ApplicationCompleted,

        /// <summary>
        /// Post-execution validation passed
        /// </summary>
        ValidationPassed,

        /// <summary>
        /// Post-execution validation failed
        /// </summary>
        ValidationFailed,

        /// <summary>
        /// Rollback operation initiated
        /// </summary>
        RollbackInitiated,

        /// <summary>
        /// Rollback operation completed
        /// </summary>
        RollbackCompleted,

        /// <summary>
        /// Configuration conflicts detected
        /// </summary>
        ConflictDetected,

        /// <summary>
        /// Hardware/software incompatibility found
        /// </summary>
        IncompatibilityFound,

        /// <summary>
        /// User cancelled the operation
        /// </summary>
        UserCancelled,

        /// <summary>
        /// System error occurred during operation
        /// </summary>
        SystemError,

        /// <summary>
        /// Administrative privileges required
        /// </summary>
        ElevationRequired,

        /// <summary>
        /// Optimization skipped due to existing configuration
        /// </summary>
        OptimizationSkipped,

        /// <summary>
        /// Custom event for extensibility
        /// </summary>
        Custom
    }

    /// <summary>
    /// Severity levels for optimization events
    /// </summary>
    public enum EventSeverity
    {
        /// <summary>
        /// Informational event
        /// </summary>
        Info,

        /// <summary>
        /// Warning event
        /// </summary>
        Warning,

        /// <summary>
        /// Error event
        /// </summary>
        Error,

        /// <summary>
        /// Critical error event
        /// </summary>
        Critical
    }

    /// <summary>
    /// Operating system information for audit trails
    /// </summary>
    public class OsInfo
    {
        /// <summary>
        /// Operating system name and version
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// OS build number
        /// </summary>
        public string Build { get; set; }

        /// <summary>
        /// OS architecture (x86, x64, ARM)
        /// </summary>
        public string Architecture { get; set; }

        /// <summary>
        /// Whether running as administrator
        /// </summary>
        public bool IsAdministrator { get; set; }

        /// <summary>
        /// System uptime
        /// </summary>
        public TimeSpan Uptime { get; set; }
    }

    /// <summary>
    /// Result of optimization validation
    /// </summary>
    public class ValidationResult
    {
        /// <summary>
        /// Whether validation was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Error message if validation failed
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// Collection of validation errors
        /// </summary>
        public System.Collections.Generic.List<string> Errors { get; set; } = new();

        /// <summary>
        /// Detailed validation results for different components
        /// </summary>
        public System.Collections.Generic.Dictionary<string, ComponentValidationResult> ComponentResults { get; set; } = new();

        /// <summary>
        /// Timestamp when validation was performed
        /// </summary>
        public DateTime ValidationTimestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Validation result for a specific component
    /// </summary>
    public class ComponentValidationResult
    {
        /// <summary>
        /// Component name (Registry, Service, File, etc.)
        /// </summary>
        public string ComponentName { get; set; }

        /// <summary>
        /// Whether this component passed validation
        /// </summary>
        public bool Passed { get; set; }

        /// <summary>
        /// Expected value
        /// </summary>
        public object ExpectedValue { get; set; }

        /// <summary>
        /// Actual value found
        /// </summary>
        public object ActualValue { get; set; }

        /// <summary>
        /// Error message if validation failed
        /// </summary>
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Factory methods for creating optimization events
    /// </summary>
    public static class OptimizationEventFactory
    {
        /// <summary>
        /// Creates a state detection event
        /// </summary>
        public static OptimizationEvent CreateStateDetectionEvent(
            string context,
            OptimizationCategory category,
            HardwareProfile hardwareProfile,
            SystemStateSnapshot stateSnapshot,
            string userId = null,
            string sessionId = null,
            string machineId = null)
        {
            return new OptimizationEvent
            {
                Timestamp = DateTime.UtcNow,
                Type = OptimizationEventType.StateDetection,
                OptimizationName = context,
                Category = category,
                HardwareProfile = hardwareProfile,
                PreState = stateSnapshot,
                UserId = userId,
                SessionId = sessionId ?? Guid.NewGuid().ToString(),
                MachineId = machineId ?? Environment.MachineName,
                Severity = EventSeverity.Info,
                EngineVersion = "1.0.0"
            };
        }

        /// <summary>
        /// Creates a validation event
        /// </summary>
        public static OptimizationEvent CreateValidationEvent(
            string optimizationName,
            OptimizationCategory category,
            HardwareProfile hardwareProfile,
            SystemStateSnapshot currentState,
            ValidationResult validationResult,
            bool passed,
            string userId = null,
            string sessionId = null,
            string machineId = null)
        {
            return new OptimizationEvent
            {
                Timestamp = DateTime.UtcNow,
                Type = passed ? OptimizationEventType.ValidationPassed : OptimizationEventType.ValidationFailed,
                OptimizationName = optimizationName,
                Category = category,
                HardwareProfile = hardwareProfile,
                PreState = currentState,
                ValidationResult = validationResult,
                UserId = userId,
                SessionId = sessionId ?? Guid.NewGuid().ToString(),
                MachineId = machineId ?? Environment.MachineName,
                Severity = passed ? EventSeverity.Info : EventSeverity.Warning,
                EngineVersion = "1.0.0"
            };
        }

        /// <summary>
        /// Creates an application event
        /// </summary>
        public static OptimizationEvent CreateApplicationEvent(
            string optimizationName,
            OptimizationCategory category,
            HardwareProfile hardwareProfile,
            SystemStateSnapshot preState,
            SystemStateSnapshot postState,
            ValidationResult validationResult,
            TimeSpan executionTime,
            bool completed,
            string userId = null,
            string sessionId = null,
            string machineId = null)
        {
            return new OptimizationEvent
            {
                Timestamp = DateTime.UtcNow,
                Type = completed ? OptimizationEventType.ApplicationCompleted : OptimizationEventType.ApplicationStarted,
                OptimizationName = optimizationName,
                Category = category,
                HardwareProfile = hardwareProfile,
                PreState = preState,
                PostState = postState,
                ValidationResult = validationResult,
                ExecutionTime = executionTime,
                UserId = userId,
                SessionId = sessionId ?? Guid.NewGuid().ToString(),
                MachineId = machineId ?? Environment.MachineName,
                Severity = completed ? EventSeverity.Info : EventSeverity.Warning,
                EngineVersion = "1.0.0"
            };
        }

        /// <summary>
        /// Creates an error event
        /// </summary>
        public static OptimizationEvent CreateErrorEvent(
            string optimizationName,
            OptimizationCategory category,
            Exception exception,
            string userId = null,
            string sessionId = null,
            string machineId = null)
        {
            return new OptimizationEvent
            {
                Timestamp = DateTime.UtcNow,
                Type = OptimizationEventType.SystemError,
                OptimizationName = optimizationName,
                Category = category,
                Exception = exception,
                UserId = userId,
                SessionId = sessionId ?? Guid.NewGuid().ToString(),
                MachineId = machineId ?? Environment.MachineName,
                Severity = EventSeverity.Error,
                EngineVersion = "1.0.0"
            };
        }
    }
}
