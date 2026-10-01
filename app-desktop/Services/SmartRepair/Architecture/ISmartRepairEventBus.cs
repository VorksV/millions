using System;

namespace VoltrisOptimizer.Services.SmartRepair.Architecture
{
    public interface ISmartRepairEventBus
    {
        event EventHandler<SmartRepairEventArgs>? EventPublished;

        void Publish(SmartRepairEventArgs args);
        void PublishMessage(string correlationId, string moduleId, string message, SmartRepairEventType eventType = SmartRepairEventType.ProgressChanged);
        void PublishError(string correlationId, string moduleId, Exception exception, string message = "");
    }
}
