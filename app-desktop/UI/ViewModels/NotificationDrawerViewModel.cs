using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Notifications;

namespace VoltrisOptimizer.UI.ViewModels 
{
    public class NotificationDrawerViewModel : INotifyPropertyChanged 
    {
        private readonly INotificationService _notificationService;
        private bool _isOpen;

        public ObservableCollection<NotificationItemViewModel> Notifications { get; } = new();

        public bool IsOpen 
        {
            get => _isOpen;
            set 
            {
                if (_isOpen == value) return;
                _isOpen = value;

                // Marca todas as notificações como lidas ao abrir o Drawer, interrompendo instantaneamente a animação do sino
                if (_isOpen && HasUnread && MarkAllAsReadCommand?.CanExecute(null) == true)
                {
                    MarkAllAsReadCommand.Execute(null);
                }

                OnPropertyChanged();
            }
        }

        public bool HasUnread => UnreadCount > 0;
        public bool HasNotifications => Notifications.Count > 0;
        public int UnreadCount 
        {
            get 
            {
                int count = 0;
                foreach (var n in Notifications) if (!n.IsRead) count++;
                return count;
            }
        }

        public string Subtitle
        {
            get
            {
                if (!HasUnread)
                {
                    return LocalizationService.Instance.GetString("DrawerSubtitleAllCaughtUp");
                }
                string format = UnreadCount == 1 
                    ? LocalizationService.Instance.GetString("DrawerSubtitleUnreadSingular")
                    : LocalizationService.Instance.GetString("DrawerSubtitleUnreadPlural");
                return string.Format(format, UnreadCount);
            }
        }

        public ICommand CloseCommand { get; }
        public ICommand ClearAllCommand { get; }
        public ICommand MarkAllAsReadCommand { get; }

        public NotificationDrawerViewModel(INotificationService notificationService) 
        {
            _notificationService = notificationService;

            // Substituir timer por eventos para sincronização em tempo real e performance
            _notificationService.NotificationAdded += (s, msg) => 
            {
                Application.Current.Dispatcher.BeginInvoke(() => 
                {
                    // Adicionar no topo (mais recente primeiro)
                    Notifications.Insert(0, new NotificationItemViewModel(msg, _notificationService, this));

                    // O servico limita o historico a 100; a colecao do drawer precisa
                    // acompanhar o mesmo teto, senao cresce sem limite durante a sessao.
                    while (Notifications.Count > 100)
                    {
                        Notifications.RemoveAt(Notifications.Count - 1);
                    }

                    NotifyStateChanged();
                });
            };

            _notificationService.HistoryCleared += (s, e) => 
            {
                Application.Current.Dispatcher.BeginInvoke(() => 
                {
                    Notifications.Clear();
                    NotifyStateChanged();
                });
            };

            // Quando o idioma mudar, re-localizar todos os itens do drawer
            LocalizationService.Instance.LanguageChanged += (s, e) =>
            {
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    foreach (var n in Notifications)
                    {
                        n.RefreshLocalization();
                    }
                    NotifyStateChanged();
                });
            };

            CloseCommand = new RelayCommand(() => IsOpen = false);
            ClearAllCommand = new RelayCommand(() => 
            {
                _notificationService.ClearAll();
                // O evento HistoryCleared cuidará do resto
            });

            MarkAllAsReadCommand = new RelayCommand(() => 
            {
                foreach (var n in Notifications) 
                {
                    if (!n.IsRead) 
                    {
                        n.IsRead = true;
                        _notificationService.MarkAsRead(n.SourceMessage.Id);
                    }
                }
                NotifyStateChanged();
            });

            SyncNotifications();
        }

        private void NotifyStateChanged()
        {
            OnPropertyChanged(nameof(HasNotifications));
            OnPropertyChanged(nameof(UnreadCount));
            OnPropertyChanged(nameof(HasUnread));
            OnPropertyChanged(nameof(Subtitle));
        }

        public void SyncNotifications() 
        {
            try 
            {
                Notifications.Clear();
                // Ordenar por data descrescente (mais recentes primeiro)
                var sortedHistory = _notificationService.NotificationHistory
                                    .OrderByDescending(n => n.Timestamp);

                foreach (var msg in sortedHistory) 
                {
                    Notifications.Add(new NotificationItemViewModel(msg, _notificationService, this));
                }
                NotifyStateChanged();
            }
            catch (Exception ex) 
            {
                App.LoggingService?.LogError($"[DRAWER] Erro em SyncNotifications: {ex.Message}", ex);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public void OnPropertyChanged([CallerMemberName] string propertyName = "") 
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class NotificationItemViewModel : INotifyPropertyChanged 
    {
        private readonly NotificationDrawerViewModel _parent;
        private bool _isRead;

        public NotificationMessage SourceMessage { get; }

        /// <summary>
        /// Título dinamicamente localizado — re-avaliado a cada acesso com base no idioma atual.
        /// SourceMessage.Title armazena a string original (raw, PT-BR).
        /// Emojis são removidos para exibir apenas o ícone moderno do status.
        /// </summary>
        public string Title => GlobalNotificationService.StripEmojis(GlobalNotificationService.LocalizeTitle(SourceMessage.Title));

        /// <summary>
        /// Mensagem dinamicamente localizada — re-avaliada a cada acesso com base no idioma atual.
        /// SourceMessage.Message armazena a string original (raw, PT-BR).
        /// Emojis são removidos para exibir apenas o ícone moderno do status.
        /// </summary>
        public string Message => GlobalNotificationService.StripEmojis(GlobalNotificationService.LocalizeMessage(SourceMessage.Message));

        public string TimeGroup => SourceMessage.Timestamp.ToString("HH:mm");

        /// <summary>
        /// Chamado pelo ViewModel pai quando o idioma muda, forçando a UI a re-ler Title e Message.
        /// </summary>
        public void RefreshLocalization()
        {
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Message));
        }

        public Brush StatusColor { get; }
        public Brush StatusBackground { get; }
        public Geometry IconGeometry { get; }

        public bool IsRead 
        {
            get => _isRead;
            set 
            {
                _isRead = value;
                OnPropertyChanged();
            }
        }

        public ICommand MarkAsReadCommand { get; }

        public NotificationItemViewModel(NotificationMessage message, INotificationService service, NotificationDrawerViewModel parent) 
        {
            SourceMessage = message;
            _parent = parent;
            _isRead = message.IsRead;

            // Mapeamento Dinâmico de Identidade Visual
            switch (message.Type)
            {
                case NotificationType.Success:
                    StatusColor = Application.Current.TryFindResource("SuccessBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(16, 185, 129));
                    IconGeometry = Application.Current.TryFindResource("IconSuccess") as Geometry ?? Geometry.Empty;
                    break;
                case NotificationType.Warning:
                    StatusColor = Application.Current.TryFindResource("WarningBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(245, 158, 11));
                    IconGeometry = Application.Current.TryFindResource("IconWarning") as Geometry ?? Geometry.Empty;
                    break;
                case NotificationType.Error:
                    StatusColor = Application.Current.TryFindResource("ErrorBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(239, 68, 68));
                    IconGeometry = Application.Current.TryFindResource("IconError") as Geometry ?? Geometry.Empty;
                    break;
                case NotificationType.Processing:
                    StatusColor = Application.Current.TryFindResource("ProcessingBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(192, 132, 252));
                    IconGeometry = Application.Current.TryFindResource("IconProcessing") as Geometry ?? Geometry.Empty;
                    break;
                default:
                    StatusColor = Application.Current.TryFindResource("InfoBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(49, 168, 255));
                    IconGeometry = Application.Current.TryFindResource("IconInfo") as Geometry ?? Geometry.Empty;
                    break;
            }

            StatusBackground = StatusColor.Clone();
            StatusBackground.Opacity = 0.15;

            MarkAsReadCommand = new RelayCommand(() => 
            {
                if (!IsRead) 
                {
                    IsRead = true;
                    service.MarkAsRead(SourceMessage.Id);
                    _parent.SyncNotifications(); 
                }
            });
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public void OnPropertyChanged([CallerMemberName] string propertyName = "") 
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
