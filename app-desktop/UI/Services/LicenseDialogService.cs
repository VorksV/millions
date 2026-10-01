using VoltrisOptimizer.Services;

using System;
using System.Diagnostics;
using System.Windows;
using VoltrisOptimizer.Services.License.Interfaces;
using VoltrisOptimizer.Services.License.Models;

namespace VoltrisOptimizer.UI.Services
{
    /// <summary>
    /// Serviço de diálogo de licença - UI Layer
    /// Responsável apenas pela UI, sem lógica de negócio
    /// </summary>
    public class LicenseDialogService : ILicenseDialogService
    {
        private string PURCHASE_URL => SiteConfig.PurchaseLicenseUrl;

        /// <summary>
        /// Traduz o slug interno de uma feature PRO para o nome amigável exibido no modal,
        /// respeitando o idioma atual (PT/ES/EN).
        /// </summary>
        private string GetFriendlyFeatureName(string feature)
        {
            string key = feature switch
            {
                "smart_repair" => "ProFeatureSmartRepair",
                "shield" => "ProFeatureShield",
                "gamer_mode" => "ProFeatureGamerMode",
                "stream_mode" => "ProFeatureStreamMode",
                "intelligent_optimization" => "ProFeatureIntelligentOptimization",
                "prepare_pc" => "ProFeaturePreparePc",
                "cleanup" => "ProFeatureCleanup",
                "debloat" => "ProFeatureDebloat",
                "optimization" => "ProFeatureOptimization",
                "network" => "ProFeatureNetwork",
                "intelligent_profile" => "IntelligentProfile",
                _ => null
            };
            return key != null ? LocalizationService.Instance.GetString(key) : feature;
        }

        public void ShowLicenseBlocked(LicenseState state, string feature)
        {
            try
            {
                App.LoggingService?.LogInfo($"[LicenseDialogService] Exibindo modal de bloqueio para: {feature}");
                
                // Traduzir slugs de features para nomes amigáveis na UI (3 idiomas)
                string friendlyFeature = GetFriendlyFeatureName(feature);

                // Determinar o tier mínimo exigido para a funcionalidade
                string minimumTier = GetMinimumRequiredTier(feature);

                // Executar na UI Thread de forma assíncrona
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    // Chamar o MainWindow para mostrar o modal de compra (overlay)
                    if (Application.Current.MainWindow is VoltrisOptimizer.UI.MainWindow mainWindow)
                    {
                        mainWindow.ShowLicensePurchaseModal($"Feature bloqueada: {friendlyFeature} (requer {minimumTier})");
                    }
                    else
                    {
                        // Fallback se não encontrar MainWindow
                        OpenPurchaseWebsite();
                    }
                });
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[LicenseDialogService] Erro ao exibir diálogo de bloqueio", ex);
                
                // Fallback seguro
                MessageBox.Show(
                    string.Format(LocalizationService.Instance.GetString("FeatureUnavailableFormat"), state?.FormattedStatus ?? "None", feature),
                    LocalizationService.Instance.GetString("LicenseRequired"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        /// <summary>
        /// Retorna o tier mínimo de licença necessário para uma funcionalidade.
        /// </summary>
        private static string GetMinimumRequiredTier(string feature)
        {
            return feature?.ToLowerInvariant() switch
            {
                "smart_repair" => "standard",
                "shield"       => "standard",
                "gamer_mode"   => "standard",
                "stream_mode"  => "standard",
                "prepare_pc"   => "standard",
                "cleanup"      => "standard",
                "debloat"      => "standard",
                "optimization" => "standard",
                "network"      => "standard",
                _              => "standard"
            };
        }

        public void ShowLicenseExpired(LicenseState state)
        {
            try
            {
                App.LoggingService?.LogInfo("[LicenseDialogService] Exibindo modal de bloqueio (trial expirado)");
                
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    if (Application.Current.MainWindow is VoltrisOptimizer.UI.MainWindow mainWindow)
                    {
                        mainWindow.ShowLicensePurchaseModal("Trial expirado - assine uma licença para continuar");
                    }
                    else
                    {
                        OpenPurchaseWebsite();
                    }
                });
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[LicenseDialogService] Erro ao exibir diálogo de trial expirado", ex);
                
                MessageBox.Show(
                    LocalizationService.Instance.GetString("TrialExpired"),
                    LocalizationService.Instance.GetString("TrialExpiredTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        public void ShowPurchaseLicense(LicenseState state, string feature)
        {
            ShowLicenseBlocked(state, feature);
        }

        public void NavigateToLicensePage()
        {
            try
            {
                App.LoggingService?.LogInfo("[LicenseDialogService] Navegando para página de licença");
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    if (Application.Current.MainWindow is VoltrisOptimizer.UI.MainWindow mainWindow)
                    {
                        mainWindow.NavigateToPageSafe("LicenseActivation");
                    }
                });
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[LicenseDialogService] Erro ao navegar para página de licença", ex);
            }
        }

        private void OpenPurchaseWebsite()
        {
            try
            {
                App.LoggingService?.LogInfo($"[LicenseDialogService] Abrindo site de compra: {PURCHASE_URL}");
                Process.Start(new ProcessStartInfo
                {
                    FileName = PURCHASE_URL,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[LicenseDialogService] Erro ao abrir site de compra: {ex.Message}", ex);
            }
        }
    }
}