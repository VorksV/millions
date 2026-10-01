using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services
{
    // STUBS PARA COMPILAÇÃO - Tipos de Notification removidos durante refatoração
    
    public interface INotificationService
    {
        Task ShowNotificationAsync(string title, string message, NotificationType type = NotificationType.Info, CancellationToken cancellationToken = default);
        Task<List<NotificationMessage>> GetNotificationsAsync(CancellationToken cancellationToken = default);
        Task ClearNotificationsAsync(CancellationToken cancellationToken = default);
        void MarkAsRead(string notificationId);
        void ClearAll();
        List<NotificationMessage> NotificationHistory { get; }
        event EventHandler<NotificationMessage>? NotificationAdded;
        event EventHandler? HistoryCleared;
    }

    public class NotificationMessage
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public bool IsRead { get; set; }
        public NotificationType Type { get; set; } = NotificationType.Info;
    }
}
