using System;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.SmartRepair
{
    public enum StepStatus
    {
        Pending,
        Running,
        Completed,
        Warning,
        Failed,
        Skipped,
        Cancelled
    }

    public enum LogMessageType
    {
        Info,
        Success,
        Warning,
        Error,
        Focus
    }

    public class RepairStepResult
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public StepStatus Status { get; set; } = StepStatus.Pending;
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public TimeSpan? Duration => CompletedAt.HasValue && StartedAt.HasValue
            ? CompletedAt.Value - StartedAt.Value
            : null;
        public bool Success => Status == StepStatus.Completed;
        public string? ErrorMessage { get; set; }
        public string? WarningMessage { get; set; }
        public string DetailSummary { get; set; } = string.Empty;
        public string DetailLog { get; set; } = string.Empty;
        public long FilesChecked { get; set; }
        public long FilesFixed { get; set; }
        public long SpaceRecoveredBytes { get; set; }
        public bool Expandable { get; set; }
        public string CorrelationId { get; set; } = Guid.NewGuid().ToString("N");
    }

    public abstract class RepairStepBase
    {
        public int Id { get; }
        public virtual string Name => VoltrisOptimizer.Services.LocalizationService.Instance.GetString($"SmartRepairStep{Id:D2}Name");
        public virtual string Description => VoltrisOptimizer.Services.LocalizationService.Instance.GetString($"SmartRepairStep{Id:D2}Desc");

        protected RepairStepBase(int id)
        {
            Id = id;
        }

        public abstract Task<RepairStepResult> ExecuteAsync(
            IProgress<RepairProgress> progress,
            CancellationToken ct);
    }

    public class RepairProgress
    {
        public int CurrentStepId { get; set; }
        public string StepName { get; set; } = string.Empty;
        public int OverallPercent { get; set; }
        public int StepPercent { get; set; }
        public string StatusMessage { get; set; } = string.Empty;
        public string LogMessage { get; set; } = string.Empty;
        public bool IsLogMessage { get; set; }
        public LogMessageType LogType { get; set; } = LogMessageType.Info;
        public StepStatus? CurrentStepStatus { get; set; }
        public TimeSpan EstimatedTimeRemaining { get; set; }
        
        // Metrics for real-time UI updates
        public long FilesChecked { get; set; }
        public long FilesFixed { get; set; }
        public long SpaceRecoveredBytes { get; set; }
        public int ProblemsFound { get; set; }
    }
}
