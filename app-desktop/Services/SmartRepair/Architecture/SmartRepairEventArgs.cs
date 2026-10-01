using System;

namespace VoltrisOptimizer.Services.SmartRepair.Architecture
{
    public enum SmartRepairEventType
    {
        ModuleStarted,
        ModuleFinished,
        CategoryStarted,
        CategoryFinished,
        OperationStarted,
        OperationFinished,
        ProgressChanged,
        WarningRaised,
        ErrorRaised,
        RollbackStarted,
        RollbackFinished
    }

    public class SmartRepairEventArgs : EventArgs
    {
        public SmartRepairEventType EventType { get; set; }
        public string CorrelationId { get; set; } = string.Empty;
        public string ModuleId { get; set; } = string.Empty;
        public string CategoryId { get; set; } = string.Empty;
        public string OperationId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        
        public int ProgressPercentage { get; set; }
        
        public int StepIndex { get; set; } = -1;
        
        public long ItemsProcessed { get; set; }
        public long BytesProcessed { get; set; }
        public Exception? Exception { get; set; }
        
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public int ThreadId { get; set; } = Environment.CurrentManagedThreadId;
    }
}
