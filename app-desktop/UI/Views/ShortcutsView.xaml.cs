using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.UI.Views
{
    /// <summary>
    /// Página de Atalhos.
    ///
    /// RESPONSABILIDADE DESTA CORREÇÃO:
    /// A página era um snapshot MANUAL e estático de <see cref="HotkeyService.DefaultShortcuts"/>,
    /// sem qualquer vínculo com o registro real. Já havia divergido: o atalho de
    /// Otimização Rápida aparecia como CTRL+ALT+Q enquanto o Windows registrava
    /// CTRL+SHIFT+Q — o usuário apertava uma combinação que nunca fez nada.
    ///
    /// Além de corrigir a exibição, a página agora informa quais atalhos
    /// REALMENTE foram registrados no Windows e quais falharam (tipicamente porque
    /// outro programa já possui aquela combinação). Antes, uma falha de
    /// RegisterHotKey era apenas um LogInfo e a tela continuava mostrando o atalho
    /// como se funcionasse.
    ///
    /// O conteúdo visual dos cards NÃO é gerado aqui: permaneceria uma duplicação
    /// frágil. O que esta classe faz é (a) corrigir a combinação divergente e
    /// (b) expor o estado real de registro.
    /// </summary>
    public partial class ShortcutsView : UserControl
    {
        public ShortcutsView()
        {
            InitializeComponent();
            Loaded += ShortcutsView_Loaded;
            Unloaded += ShortcutsView_Unloaded;
        }

        private void ShortcutsView_Loaded(object sender, RoutedEventArgs e)
        {
            App.TelemetryService?.TrackEvent("PAGE_VIEW", "Shortcuts", "Load", success: true);
            RefreshRegistrationStatus();
        }

        private void ShortcutsView_Unloaded(object sender, RoutedEventArgs e)
        {
            // Nada a desinscrever: a leitura do estado é pontual.
        }

        /// <summary>
        /// Lê o estado real do registro e informa o usuário quantos atalhos estão
        /// ativos e quais falharam.
        /// </summary>
        private void RefreshRegistrationStatus()
        {
            if (HotkeyStatusBanner == null || HotkeyStatusText == null) return;

            try
            {
                var hotkeys = App.Services?.GetService(typeof(HotkeyService)) as HotkeyService;
                if (hotkeys == null)
                {
                    HotkeyStatusBanner.Visibility = Visibility.Visible;
                    HotkeyStatusText.Text = LocalizationService.Instance.GetString("ShortcutServiceUnavailable");
                    App.LoggingService?.LogWarning("[Shortcuts] HotkeyService indisponível — estado real desconhecido.");
                    return;
                }

                var total = hotkeys.DefaultShortcuts.Count;
                var failed = hotkeys.GetFailedRegistrations();

                if (failed.Count == 0)
                {
                    HotkeyStatusBanner.Visibility = Visibility.Collapsed;
                    App.LoggingService?.LogInfo($"[Shortcuts] {total}/{total} atalhos registrados com sucesso no Windows.");
                    return;
                }

                HotkeyStatusBanner.Visibility = Visibility.Visible;
                HotkeyStatusText.Text = string.Format(
                    LocalizationService.Instance.GetString("ShortcutRegistrationFailed"),
                    total - failed.Count, total,
                    string.Join(", ", failed.Select(f => f.KeyCombo)));

                App.LoggingService?.LogWarning(
                    $"[Shortcuts] {failed.Count} de {total} atalhos NÃO foram registrados no Windows " +
                    $"(combinações já reservadas por outro programa): {string.Join(", ", failed.Select(f => f.KeyCombo))}");
            }
            catch (Exception ex)
            {
                HotkeyStatusBanner.Visibility = Visibility.Visible;
                HotkeyStatusText.Text = string.Format(
                    LocalizationService.Instance.GetString("ShortcutStatusUnavailable"), ex.Message);
                App.LoggingService?.LogWarning($"[Shortcuts] Falha ao ler o estado de registro: {ex.Message}");
            }
        }
    }
}
