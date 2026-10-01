using System;

namespace VoltrisOptimizer.Services
{
    public class NotificationRequest
    {
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public NotificationType Type { get; set; } = NotificationType.Info;
        public bool ShowToast { get; set; } = true;
        public bool PersistInCenter { get; set; }
        public int DurationMs { get; set; } = 3000;
    }
}
