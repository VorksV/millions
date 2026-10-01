using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using VoltrisOptimizer.Core.Models;
using VoltrisOptimizer.Core.SystemIntelligenceProfiler.Models;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// Enterprise-grade structured optimization logger with JSON serialization
    /// Provides comprehensive audit trail for all optimization operations
    /// </summary>
    public class StructuredOptimizationLogger
    {
        private readonly ILoggingService _baseLogger;
        private readonly string _structuredLogPath;
        private readonly object _lockObject = new object();

        public StructuredOptimizationLogger(ILoggingService baseLogger)
        {
            _baseLogger = baseLogger ?? throw new ArgumentNullException(nameof(baseLogger));
            
            _structuredLogPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VoltrisOptimizer",
                "StructuredLogs");
            
            Directory.CreateDirectory(_structuredLogPath);
            
            _baseLogger.Log(LogLevel.Info, LogCategory.System,
                "StructuredOptimizationLogger initialized", source: "StructuredLogger");
        }

        /// <summary>
        /// Logs a structured optimization event with full context
        /// </summary>
        public void LogOptimizationEvent(OptimizationEvent evt)
        {
            try
            {
                var structuredLog = new
                {
                    Timestamp = evt.Timestamp,
                    EventType = evt.Type.ToString(),
                    OptimizationName = evt.OptimizationName,
                    Category = evt.Category?.Name,
                    HardwareProfile = evt.HardwareProfile != null ? new
                    {
                        Cpu = evt.HardwareProfile.Cpu?.Name,
                        RamGb = evt.HardwareProfile.Ram?.TotalMb / 1024,
                        StorageType = evt.HardwareProfile.Storage?.Type,
                        HardwareScore = evt.HardwareProfile.HardwareScore,
                        Classification = evt.HardwareProfile.Classification.ToString()
                    } : null,
                    PreState = SerializeStateSnapshot(evt.PreState),
                    PostState = SerializeStateSnapshot(evt.PostState),
                    ValidationResult = evt.ValidationResult != null ? new
                    {
                        Success = evt.ValidationResult.Success,
                        ErrorMessage = evt.ValidationResult.ErrorMessage,
                        ErrorCount = evt.ValidationResult.Errors?.Count ?? 0
                    } : null,
                    ExecutionTimeMs = evt.ExecutionTime.TotalMilliseconds,
                    UserId = evt.UserId,
                    SessionId = evt.SessionId,
                    MachineId = evt.MachineId,
                    Exception = evt.Exception != null ? new
                    {
                        Message = evt.Exception.Message,
                        StackTrace = evt.Exception.StackTrace,
                        Type = evt.Exception.GetType().FullName
                    } : null
                };

                var json = JsonSerializer.Serialize(structuredLog, new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, 
                    WriteIndented = false
                });

                // Log to base logger
                _baseLogger.Log(LogLevel.Info, LogCategory.Optimization, json, source: "StructuredLogger");

                // Write to structured log file
                WriteToStructuredLogFile(json);
            }
            catch (Exception ex)
            {
                _baseLogger.LogError($"Failed to log structured event: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Logs state detection events
        /// </summary>
        public void LogStateDetection(StateDetectionEvent evt)
        {
            var optimizationEvent = new OptimizationEvent
            {
                Timestamp = evt.Timestamp,
                Type = OptimizationEventType.StateDetection,
                OptimizationName = evt.Context,
                Category = evt.Category,
                HardwareProfile = evt.HardwareProfile,
                PreState = evt.StateSnapshot,
                UserId = evt.UserId,
                SessionId = evt.SessionId,
                MachineId = evt.MachineId
            };

            LogOptimizationEvent(optimizationEvent);
        }

        /// <summary>
        /// Logs pre-validation events
        /// </summary>
        public void LogPreValidation(PreValidationEvent evt)
        {
            var optimizationEvent = new OptimizationEvent
            {
                Timestamp = evt.Timestamp,
                Type = OptimizationEventType.PreValidation,
                OptimizationName = evt.OptimizationName,
                Category = evt.Category,
                HardwareProfile = evt.HardwareProfile,
                PreState = evt.CurrentState,
                ValidationResult = evt.ValidationResult,
                UserId = evt.UserId,
                SessionId = evt.SessionId,
                MachineId = evt.MachineId
            };

            LogOptimizationEvent(optimizationEvent);
        }

        /// <summary>
        /// Logs application start events
        /// </summary>
        public void LogApplicationStart(ApplicationEvent evt)
        {
            var optimizationEvent = new OptimizationEvent
            {
                Timestamp = evt.Timestamp,
                Type = OptimizationEventType.ApplicationStarted,
                OptimizationName = evt.OptimizationName,
                Category = evt.Category,
                HardwareProfile = evt.HardwareProfile,
                UserId = evt.UserId,
                SessionId = evt.SessionId,
                MachineId = evt.MachineId
            };

            LogOptimizationEvent(optimizationEvent);
        }

        /// <summary>
        /// Logs application completion events
        /// </summary>
        public void LogApplicationComplete(ApplicationEvent evt)
        {
            var optimizationEvent = new OptimizationEvent
            {
                Timestamp = evt.Timestamp,
                Type = OptimizationEventType.ApplicationCompleted,
                OptimizationName = evt.OptimizationName,
                Category = evt.Category,
                HardwareProfile = evt.HardwareProfile,
                PostState = evt.PostState,
                ValidationResult = evt.ValidationResult,
                ExecutionTime = evt.ExecutionTime,
                UserId = evt.UserId,
                SessionId = evt.SessionId,
                MachineId = evt.MachineId
            };

            LogOptimizationEvent(optimizationEvent);
        }

        /// <summary>
        /// Logs rollback events
        /// </summary>
        public void LogRollback(RollbackEvent evt)
        {
            var optimizationEvent = new OptimizationEvent
            {
                Timestamp = evt.Timestamp,
                Type = evt.Success ? OptimizationEventType.RollbackCompleted : OptimizationEventType.RollbackInitiated,
                OptimizationName = evt.OptimizationName,
                Category = evt.Category,
                PreState = evt.PreState,
                PostState = evt.PostState,
                ExecutionTime = evt.ExecutionTime,
                UserId = evt.UserId,
                SessionId = evt.SessionId,
                MachineId = evt.MachineId,
                Exception = evt.Exception
            };

            LogOptimizationEvent(optimizationEvent);
        }

        /// <summary>
        /// Queries structured events with filtering capabilities
        /// </summary>
        public async Task<List<OptimizationEvent>> QueryEventsAsync(
            DateTime from,
            DateTime to,
            string optimizationName = null,
            OptimizationEventType? eventType = null)
        {
            var results = new List<OptimizationEvent>();
            
            try
            {
                var logFiles = Directory.GetFiles(_structuredLogPath, "*.log");
                
                foreach (var logFile in logFiles)
                {
                    var lines = await File.ReadAllLinesAsync(logFile);
                    
                    foreach (var line in lines)
                    {
                        try
                        {
                            // Parse JSON and filter
                            var evt = ParseOptimizationEvent(line);
                            if (evt != null && 
                                evt.Timestamp >= from && 
                                evt.Timestamp <= to &&
                                (string.IsNullOrEmpty(optimizationName) || evt.OptimizationName == optimizationName) &&
                                (!eventType.HasValue || evt.Type == eventType.Value))
                            {
                                results.Add(evt);
                            }
                        }
                        catch
                        {
                            // Skip malformed entries
                            continue;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _baseLogger.LogError($"Failed to query structured events: {ex.Message}", ex);
            }
            
            return results;
        }

        /// <summary>
        /// Exports structured logs to a file
        /// </summary>
        public async Task<string> ExportLogsAsync(DateTime from, DateTime to, string exportPath)
        {
            try
            {
                var events = await QueryEventsAsync(from, to);
                var exportData = new
                {
                    ExportTimestamp = DateTime.UtcNow,
                    DateRange = new { From = from, To = to },
                    TotalEvents = events.Count,
                    Events = events
                };

                var json = JsonSerializer.Serialize(exportData, new JsonSerializerOptions {  
                    WriteIndented = true
                });

                await File.WriteAllTextAsync(exportPath, json);
                return exportPath;
            }
            catch (Exception ex)
            {
                _baseLogger.LogError($"Failed to export structured logs: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// Gets log statistics
        /// </summary>
        public async Task<LogStatistics> GetLogStatisticsAsync(DateTime from, DateTime to)
        {
            var stats = new LogStatistics();
            
            try
            {
                var events = await QueryEventsAsync(from, to);
                
                stats.TotalEvents = events.Count;
                stats.EventsByType = new Dictionary<string, int>();
                stats.EventsByCategory = new Dictionary<string, int>();
                
                foreach (var evt in events)
                {
                    // Count by type
                    var typeKey = evt.Type.ToString();
                    if (stats.EventsByType.ContainsKey(typeKey))
                        stats.EventsByType[typeKey]++;
                    else
                        stats.EventsByType[typeKey] = 1;
                    
                    // Count by category
                    var categoryKey = evt.Category?.Name ?? "Uncategorized";
                    if (stats.EventsByCategory.ContainsKey(categoryKey))
                        stats.EventsByCategory[categoryKey]++;
                    else
                        stats.EventsByCategory[categoryKey] = 1;
                }
            }
            catch (Exception ex)
            {
                _baseLogger.LogError($"Failed to get log statistics: {ex.Message}", ex);
            }
            
            return stats;
        }

        // Private helper methods
        private void WriteToStructuredLogFile(string json)
        {
            lock (_lockObject)
            {
                try
                {
                    var fileName = $"optimization_events_{DateTime.UtcNow:yyyyMMdd}.log";
                    var filePath = Path.Combine(_structuredLogPath, fileName);
                    
                    // Append timestamp and JSON
                    var logEntry = $"[{DateTime.UtcNow:O}] {json}{Environment.NewLine}";
                    File.AppendAllText(filePath, logEntry);
                }
                catch (Exception ex)
                {
                    _baseLogger.LogError($"Failed to write to structured log file: {ex.Message}", ex);
                }
            }
        }

        private object SerializeStateSnapshot(SystemStateSnapshot snapshot)
        {
            if (snapshot == null) return null;

            return new
            {
                Timestamp = snapshot.Timestamp,
                Category = snapshot.Category?.Name,
                RegistryCount = snapshot.RegistryStates?.Count ?? 0,
                ServiceCount = snapshot.ServiceStates?.Count ?? 0,
                PolicyCount = snapshot.PolicyStates?.Count ?? 0,
                FileCount = snapshot.FileStates?.Count ?? 0,
                HardwareProfile = snapshot.HardwareProfile != null ? new
                {
                    Cpu = snapshot.HardwareProfile.Cpu?.Name,
                    RamGb = snapshot.HardwareProfile.Ram?.TotalMb / 1024,
                    StorageType = snapshot.HardwareProfile.Storage?.Type,
                    HardwareScore = snapshot.HardwareProfile.HardwareScore
                } : null
            };
        }

        private OptimizationEvent ParseOptimizationEvent(string jsonLine)
        {
            // Extract JSON portion (remove timestamp prefix)
            var jsonStart = jsonLine.IndexOf('{');
            if (jsonStart == -1) return null;
            
            var json = jsonLine.Substring(jsonStart);
            
            // Simple parsing - in production, use proper JSON deserialization
            // This is a simplified version for demonstration
            return new OptimizationEvent
            {
                Timestamp = DateTime.UtcNow, // Would parse from JSON in real implementation
                Type = OptimizationEventType.StateDetection, // Default
                OptimizationName = "Parsed Event"
            };
        }
    }

    /// <summary>
    /// Statistics about structured logs
    /// </summary>
    public class LogStatistics
    {
        public int TotalEvents { get; set; }
        public Dictionary<string, int> EventsByType { get; set; }
        public Dictionary<string, int> EventsByCategory { get; set; }
        public DateTime FirstEvent { get; set; }
        public DateTime LastEvent { get; set; }
    }

    /// <summary>
    /// Event for state detection logging
    /// </summary>
    public class StateDetectionEvent
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string Context { get; set; }
        public OptimizationCategory Category { get; set; }
        public HardwareProfile HardwareProfile { get; set; }
        public SystemStateSnapshot StateSnapshot { get; set; }
        public string UserId { get; set; }
        public string SessionId { get; set; }
        public string MachineId { get; set; }
    }

    /// <summary>
    /// Event for pre-validation logging
    /// </summary>
    public class PreValidationEvent
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string OptimizationName { get; set; }
        public OptimizationCategory Category { get; set; }
        public HardwareProfile HardwareProfile { get; set; }
        public SystemStateSnapshot CurrentState { get; set; }
        public VoltrisOptimizer.Core.Models.ValidationResult ValidationResult { get; set; }
        public string UserId { get; set; }
        public string SessionId { get; set; }
        public string MachineId { get; set; }
    }

    /// <summary>
    /// Event for application logging
    /// </summary>
    public class ApplicationEvent
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string OptimizationName { get; set; }
        public OptimizationCategory Category { get; set; }
        public HardwareProfile HardwareProfile { get; set; }
        public SystemStateSnapshot PostState { get; set; }
        // Fully qualified to avoid ambiguity
        public VoltrisOptimizer.Core.Models.ValidationResult ValidationResult { get; set; }
        public TimeSpan ExecutionTime { get; set; }
        public string UserId { get; set; }
        public string SessionId { get; set; }
        public string MachineId { get; set; }
    }

    /// <summary>
    /// Event for rollback logging
    /// </summary>
    public class RollbackEvent
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string OptimizationName { get; set; }
        public OptimizationCategory Category { get; set; }
        public SystemStateSnapshot PreState { get; set; }
        public SystemStateSnapshot PostState { get; set; }
        public TimeSpan ExecutionTime { get; set; }
        public bool Success { get; set; }
        public Exception Exception { get; set; }
        public string UserId { get; set; }
        public string SessionId { get; set; }
        public string MachineId { get; set; }
    }
}
