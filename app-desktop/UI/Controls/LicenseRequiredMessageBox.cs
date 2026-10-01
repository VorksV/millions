using System;
using System.Diagnostics;
using System.Windows;

namespace VoltrisOptimizer.UI.Controls
{
    /// <summary>
    /// MessageBox personalizada para bloqueio de licença com botão "Comprar Licença"
    /// </summary>
    public static class LicenseRequiredMessageBox
    {
        public static MessageBoxResult Show(string message, string? title = null)
        {
            try
            {
                title = title ?? VoltrisOptimizer.Services.LocalizationService.Instance.GetString("LicenseRequiredTitle");
                App.LoggingService?.LogInfo($"[LicenseRequiredMessageBox] Exibindo mensagem de bloqueio: {title}");
                
                var result = ModernMessageBox.Show(
                    message + "\n\n\n" + VoltrisOptimizer.Services.LocalizationService.Instance.GetString("LicenseRequiredPrompt"),
                    title,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                {
                    App.LoggingService?.LogInfo("[LicenseRequiredMessageBox] Usuário clicou em Comprar Licença");
                    
                    // Abrir site de compra de licença (escolhido pelo idioma do app)
                    var purchaseUrl = VoltrisOptimizer.Services.SiteConfig.PurchaseLicenseUrl;

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = purchaseUrl,
                        UseShellExecute = true
                    });
                    
                    App.LoggingService?.LogInfo($"[LicenseRequiredMessageBox] Site de licença aberto: {purchaseUrl}");
                }
                else
                {
                    App.LoggingService?.LogInfo("[LicenseRequiredMessageBox] Usuário recusou comprar licença");
                }

                return result;
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[LicenseRequiredMessageBox] Erro ao exibir mensagem", ex);
                
                // Fallback para MessageBox padrão
                return MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
