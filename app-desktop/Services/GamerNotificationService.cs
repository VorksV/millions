using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using VoltrisOptimizer.UI.ViewModels;

namespace VoltrisOptimizer.Services
{
    /// <summary>
    /// Serviço de notificação global para modo gamer - evita problemas com DashboardViewMode
    /// </summary>
    public class GamerNotificationService
    {
        private readonly ILoggingService? _logger;
        private static GamerNotificationService? _instance;
        private static readonly object _lock = new object();

        public static GamerNotificationService Instance
        {
            get
            {
                lock (_lock)
                {
                    return _instance ??= new GamerNotificationService();
                }
            }
        }

        private GamerNotificationService()
        {
            _logger = App.LoggingService;
        }

        /// <summary>
        /// Notifica o Dashboard sãobre mudança do modo gamer de forma segura
        /// </summary>
        public void NotifyGamerModeStatusChanged(bool isActive, string? gameName = null)
        {
            try
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        var dashboardViewModel = App.Services?.GetService<DashboardViewModel>();
                        if (dashboardViewModel != null)
                        {
                            // Usar reflexão para acessar propriedade privada de forma segura
                            var property = typeof(DashboardViewModel).GetProperty("GamerServiceStatus");
                            if (property != null)
                            {
                                var status = isActive ? LocalizationService.Instance.GetString("GamerModeActivated") : LocalizationService.Instance.GetString("StatusReady");
                                property.SetValue(dashboardViewModel, status);
                                _logger?.LogInfo($"[GamerNotification] Dashboard notificado: {status}{(gameName != null ? $" | Jogo: {gameName}" : "")}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError($"[GamerNotification] Erro ao notificar Dashboard: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[GamerNotification] Erro ao notificar Dashboard: {ex.Message}");
            }
        }
    }
}
