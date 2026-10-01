using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace VoltrisOptimizer.Services
{
    // NotificationType já existe em NotificationManager.cs
    
    /// <summary>
    /// Serviço centralizado de notificações
    /// </summary>
    public class NotificationService
    {
        private static NotificationService? _instance;
        private static readonly object _lock = new();
        
        private readonly ILoggingService? _logger;
        private Border? _toastContainer;
        private TextBlock? _toastText;
        private CancellationTokenSource? _activeToastCts;
        private readonly object _ctsLock = new();
        
        public static NotificationService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new NotificationService(App.LoggingService);
                    }
                }
                return _instance;
            }
        }
        
        private NotificationService(ILoggingService? logger)
        {
            _logger = logger;
        }
        
        /// <summary>
        /// Inicializa o serviço com o container de toast
        /// </summary>
        public void Initialize(Border? toastContainer, TextBlock? toastText)
        {
            _toastContainer = toastContainer;
            _toastText = toastText;
        }
        
        /// <summary>
        /// Mostra uma notificação toast
        /// </summary>
        public async Task ShowToastAsync(string message, NotificationType type = NotificationType.Info, int durationMs = 3000)
        {
            try
            {
                // Substituído sistema paralelo pelo Custom Toast original do VOLTRIS
                GlobalNotificationService.Show(string.Empty, message, type);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[Notification] Erro ao redirecionar toast: {ex.Message}");
            }
            
            await Task.CompletedTask;
        }
        
        /// <summary>
        /// Mostra um toast de sucesso
        /// </summary>
        public Task ShowSuccessAsync(string message, int durationMs = 3000)
            => ShowToastAsync(message, NotificationType.Success, durationMs);
        
        /// <summary>
        /// Mostra um toast de erro
        /// </summary>
        public Task ShowErrorAsync(string message, int durationMs = 4000)
            => ShowToastAsync(message, NotificationType.Error, durationMs);

        /// <summary>
        /// Mostra um toast de aviso
        /// </summary>
        public Task ShowWarningAsync(string message, int durationMs = 3500)
            => ShowToastAsync(message, NotificationType.Warning, durationMs);
        
        /// <summary>
        /// Mostra um toast de informação
        /// </summary>
        public Task ShowInfoAsync(string message, int durationMs = 3000)
            => ShowToastAsync(message, NotificationType.Info, durationMs);
            
        public Task NotifyAsync(NotificationRequest request)
            => ShowToastAsync(request.Message, request.Type, request.DurationMs);
        
        /// <summary>
        /// Mostra um diálogo de confirmação
        /// </summary>
        public async Task<bool> ShowConfirmationAsync(string message, string? title = null)
        {
            var result = await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                return MessageBox.Show(
                    Application.Current.MainWindow,
                    message,
                    title ?? LocalizationService.Instance.GetString("Confirmation"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question
                ) == MessageBoxResult.Yes;
            });
            
            return result;
        }
        
        /// <summary>
        /// Mostra um diálogo de confirmação com opções personalizadas
        /// </summary>
        public async Task<bool> ShowConfirmationWithWarningAsync(string message, string title, string[] warnings)
        {
            var warningText = string.Join("\n• ", warnings);
            var fullMessage = $"{message}\n\n⚠️ Avisos:\n• {warningText}";
            
            return await ShowConfirmationAsync(fullMessage, title ?? LocalizationService.Instance.GetString("Confirmation"));
        }
        
        /// <summary>
        /// Mostra diálogo que requer reinício
        /// </summary>
        public async Task<bool> ShowRestartRequiredAsync(string message)
        {
            var result = await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                return MessageBox.Show(
                    Application.Current.MainWindow,
                    string.Format(LocalizationService.Instance.GetString("RestartPrompt"), message),
                    LocalizationService.Instance.GetString("RestartRequiredTitle"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning
                ) == MessageBoxResult.Yes;
            });
            
            return result;
        }
        
        private void LogMessage(string message, NotificationType type)
        {
            switch (type)
            {
                case NotificationType.Success:
                    _logger?.LogSuccess(message);
                    break;
                case NotificationType.Warning:
                    _logger?.LogWarning(message);
                    break;
                case NotificationType.Error:
                    _logger?.LogError(message);
                    break;
                default:
                    _logger?.LogInfo(message);
                    break;
            }
        }
    }
}

