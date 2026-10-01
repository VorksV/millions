using System;

namespace VoltrisOptimizer.Services.SmartRepair.Architecture
{
    public class SmartRepairEventBus : ISmartRepairEventBus
    {
        public event EventHandler<SmartRepairEventArgs>? EventPublished;

        public void Publish(SmartRepairEventArgs args)
        {
            EventPublished?.Invoke(this, args);
        }

        public void PublishMessage(string correlationId, string moduleId, string message, SmartRepairEventType eventType = SmartRepairEventType.ProgressChanged)
        {
            Publish(new SmartRepairEventArgs
            {
                CorrelationId = correlationId,
                ModuleId = moduleId,
                Message = message,
                EventType = eventType
            });
        }

        public void PublishError(string correlationId, string moduleId, Exception exception, string message = "")
        {
            Publish(new SmartRepairEventArgs
            {
                CorrelationId = correlationId,
                ModuleId = moduleId,
                Exception = exception,
                Message = string.IsNullOrEmpty(message) ? exception.Message : message,
                EventType = SmartRepairEventType.ErrorRaised
            });
        }
    }
}
