using System;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Gamer.Audit
{
    public interface IGamerAuditService : IDisposable
    {
        void StartSession(string? gameName = null, int? gameProcessId = null);
        void EndSession();

        void RecordOptimization(OptimizationAuditEntry entry);
        Task RecordOptimizationAsync(OptimizationAuditEntry entry);

        void RecordServiceChange(ServiceAuditEntry entry);
        void RecordProcessAction(ProcessAuditEntry entry);
        void RecordRegistryChange(RegistryAuditEntry entry);
        void RecordGameDetection(GameAuditEntry entry);
        void RecordHardware(HardwareAuditEntry entry);
        void RecordAIDecision(AIAuditEntry entry);
        void RecordPipelineStage(PipelineStageAuditEntry entry);

        Task FlushAsync();
        Task<ConsolidatedAuditReport> GenerateReportAsync();
        Task SaveReportAsync();
    }
}
