using System;

namespace VoltrisOptimizer.Services.SmartRepair
{
    public class RepairStatistics
    {
        public int TotalSteps { get; set; }
        public int CompletedSteps { get; set; }
        public int FailedSteps { get; set; }
        public int WarningSteps { get; set; }
        public int SkippedSteps { get; set; }
        public long TotalFilesChecked { get; set; }
        public long TotalFilesFixed { get; set; }
        public long TotalSpaceRecoveredBytes { get; set; }
        public TimeSpan TotalElapsed { get; set; }
        public TimeSpan EstimatedRemaining { get; set; }
        public int OverallPercent { get; set; }
        public string TotalSpaceRecoveredFormatted
        {
            get
            {
                double bytes = TotalSpaceRecoveredBytes;
                if (bytes >= 1_073_741_824)
                    return $"{bytes / 1_073_741_824:F1} GB";
                if (bytes >= 1_048_576)
                    return $"{bytes / 1_048_576:F1} MB";
                if (bytes >= 1_024)
                    return $"{bytes / 1_024:F0} KB";
                return "0 B";
            }
        }
    }
}
