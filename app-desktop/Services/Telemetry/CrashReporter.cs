using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Telemetry
{
    public class CrashReporter : IDisposable
    {
        public CrashReporter(ILoggingService logger) { }
        public Task ReportCrashAsync(Exception exception, string context = "Unknown", Dictionary<string, string>? additionalData = null) => Task.CompletedTask;
        public void Dispose() { }
    }

    public class CrashReport
    {
        public DateTime Timestamp { get; set; }
        public string Context { get; set; } = string.Empty;
        public string ExceptionType { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string StackTrace { get; set; } = string.Empty;
        public string? InnerException { get; set; }
        public string OsVersion { get; set; } = string.Empty;
        public string ClrVersion { get; set; } = string.Empty;
        public bool Is64BitProcess { get; set; }
        public bool Is64BitOperatingSystem { get; set; }
        public int ProcessorCount { get; set; }
        public string MachineName { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string AppVersion { get; set; } = string.Empty;
        public long WorkingSet { get; set; }
        public Dictionary<string, string> AdditionalData { get; set; } = new Dictionary<string, string>();
    }
}
