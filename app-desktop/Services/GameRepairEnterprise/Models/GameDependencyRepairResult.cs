namespace VoltrisOptimizer.Services.GameRepairEnterprise.Models
{
    public class GameDependencyRepairResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public long DiagnosisTimeMs { get; set; }
        public long DownloadTimeMs { get; set; }
        public long InstallTimeMs { get; set; }
        public long ValidationTimeMs { get; set; }
        
        public bool RequiredRollback { get; set; }
        public string RollbackReason { get; set; } = string.Empty;
    }
}
