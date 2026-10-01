using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.UI.Windows;

namespace VoltrisOptimizer.Services.Notifications
{
    public class VoltrisNotificationService : INotificationService
    {
        private readonly ILoggingService _logger;
        private readonly List<NotificationMessage> _history = new();
        private readonly object _lock = new();
        private int _nextId = 1;

        public event EventHandler<NotificationMessage>? NotificationAdded;
        public event EventHandler? HistoryCleared;

        public List<NotificationMessage> NotificationHistory
        {
            get { lock (_lock) return _history.ToList(); }
        }

        public VoltrisNotificationService(ILoggingService logger)
        {
            logger.LogEntry($"[Notification] VoltrisNotificationService.ctor");
            _logger = logger;
            logger.LogExit($"[Notification] VoltrisNotificationService.ctor");
        }

        public Task ShowNotificationAsync(string title, string message, NotificationType type = NotificationType.Info, CancellationToken cancellationToken = default)
        {
            _logger.LogEntry($"[Notification] ShowNotificationAsync(title={title}, type={type})");
            try
            {
            var notification = new NotificationMessage
            {
                Id = Guid.NewGuid().ToString(),
                Title = title,
                Message = message,
                Timestamp = DateTime.Now,
                IsRead = false,
                Type = type
            };

            lock (_lock)
            {
                _history.Add(notification);
                int removedCount = 0;
                if (_history.Count > 100)
                {
                    _history.RemoveAt(0);
                    removedCount = 1;
                }
                _logger.LogInfo($"[Notification] ShowNotificationAsync: histórico agora tem {_history.Count} itens (removidos={removedCount})");
            }

            _logger.LogInfo($"[Notification] Nova notificação [{type}]: {title}");

            NotificationAdded?.Invoke(this, notification);
            OnPropertyChanged(nameof(NotificationHistory));
            }
            catch (Exception ex)
            {
                _logger.LogError($"[Notification] ShowNotificationAsync: {ex.Message}");
            }
            _logger.LogExit($"[Notification] ShowNotificationAsync({title})");
            return Task.CompletedTask;
        }

        public Task<List<NotificationMessage>> GetNotificationsAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogEntry($"[Notification] GetNotificationsAsync");
            List<NotificationMessage> result;
            lock (_lock)
                result = _history.ToList();
            _logger.LogExit($"[Notification] GetNotificationsAsync = {result.Count} notificações");
            return Task.FromResult(result);
        }

        public Task ClearNotificationsAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogEntry($"[Notification] ClearNotificationsAsync");
            ClearAll();
            _logger.LogExit($"[Notification] ClearNotificationsAsync");
            return Task.CompletedTask;
        }

        public void ClearAll()
        {
            _logger.LogEntry($"[Notification] ClearAll");
            lock (_lock)
            {
                _history.Clear();
                _logger.LogInfo($"[Notification] ClearAll: histórico limpo");
            }
            HistoryCleared?.Invoke(this, EventArgs.Empty);
            OnPropertyChanged(nameof(NotificationHistory));
            _logger.LogExit($"[Notification] ClearAll");
        }

        public void MarkAsRead(string notificationId)
        {
            _logger.LogEntry($"[Notification] MarkAsRead(notificationId={notificationId})");
            lock (_lock)
            {
                var item = _history.FirstOrDefault(n => n.Id == notificationId);
                if (item != null)
                {
                    item.IsRead = true;
                    _logger.LogDebug($"[Notification] MarkAsRead: notificação {notificationId} marcada como lida");
                }
                else
                {
                    _logger.LogDebug($"[Notification] MarkAsRead: notificação {notificationId} não encontrada");
                }
            }
            OnPropertyChanged(nameof(NotificationHistory));
            _logger.LogExit($"[Notification] MarkAsRead({notificationId})");
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }
}
