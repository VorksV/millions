using System;

namespace VoltrisOptimizer.Services
{
    public class ToastService
    {
        public void Show(string title, string message)
        {
            try
            {
                // Substituído Windows Toast nativo pelo Custom Toast original do VOLTRIS
                GlobalNotificationService.ShowInfo(title, message);
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[ToastService] Falha ao exibir notificação customizada: {ex.Message}");
            }
        }
    }
}
