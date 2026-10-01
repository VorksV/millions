using System;
using System.Diagnostics;
using System.Windows;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.UI.Windows
{
    public partial class RestartConfirmationModal : Window
    {
        public enum RestartChoice
        {
            Cancel,          // Usuário cancelou - NÃO aplicar nada
            RestartLater,    // Aplicar otimizações SEM reiniciar
            RestartNow       // Aplicar otimizações E reiniciar
        }

        public RestartChoice UserChoice { get; private set; }
        public bool ShouldRestart => UserChoice == RestartChoice.RestartNow;
        public bool ShouldApplyOptimizations => UserChoice != RestartChoice.Cancel;

        public RestartConfirmationModal()
        {
            InitializeComponent();
            SourceInitialized += RestartConfirmationModal_SourceInitialized;
            App.LoggingService?.LogInfo("[RESTART] Exibindo modal de confirmação de reinicialização");
            RoundedWindowHelper.Apply(this, 16);
            UserChoice = RestartChoice.Cancel; // Padrão: cancelar

            // Aplicar transparência se habilitada nas configurações do programa
            Loaded += (s, e) => ApplyTransparencyEffect();
        }

        /// <summary>
        /// Aplica efeito de transparência/Mica ao modal se a configuração EnableTransparency estiver ativa.
        /// Respeita o toggle de transparência do header e das configurações do programa.
        /// </summary>
        private void ApplyTransparencyEffect()
        {
            try
            {
                bool transparencyEnabled = SettingsService.Instance?.Settings?.EnableTransparency ?? false;
                App.LoggingService?.LogInfo($"[RESTART] ApplyTransparencyEffect: EnableTransparency={transparencyEnabled}");

                if (!transparencyEnabled) return;

                // Aplicar backdrop Mica/Acrylic via Win32 (Windows 11+)
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero && Environment.OSVersion.Version.Build >= 22000)
                {
                    // DWMWA_SYSTEMBACKDROP_TYPE = 38, DWMSBT_MAINWINDOW = 2 (Mica)
                    int value = 2;
                    DwmSetWindowAttribute(hwnd, 38, ref value, sizeof(int));
                    App.LoggingService?.LogInfo("[RESTART] Mica backdrop aplicado ao modal de reinicialização.");
                }

                // Tornar o fundo do Border principal semi-transparente
                if (this.Content is System.Windows.Controls.Border mainBorder)
                {
                    mainBorder.Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromArgb(200, 13, 13, 20)); // Semi-transparente
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning($"[RESTART] Falha ao aplicar transparência: {ex.Message}");
            }
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        /// <summary>
        /// Define uma mensagem customizada exibida no modal (ex: "REPARO COMPLETO FINALIZADO COM SUCESSO").
        /// Se null, exibe o texto padrão.
        /// </summary>
        public void SetCustomMessage(string? title, string? description)
        {
            if (title != null && ModalTitleText != null)
                ModalTitleText.Text = title;
            if (description != null && ModalDescriptionText != null)
                ModalDescriptionText.Text = description;
        }

        private void Window_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                this.DragMove();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            App.LoggingService?.LogInfo("[RESTART] Usuário CANCELOU a aplicação e o reinício");
            UserChoice = RestartChoice.Cancel;
            DialogResult = false; // Cancelado
            Close();
        }

        private void RestartLaterButton_Click(object sender, RoutedEventArgs e)
        {
            App.LoggingService?.LogInfo("[RESTART] Usuário escolheu: APLICAR AGORA, REINICIAR DEPOIS");
            UserChoice = RestartChoice.RestartLater;
            DialogResult = true; // Confirmado (aplicar sem reiniciar)
            Close();
        }

        private void RestartNowButton_Click(object sender, RoutedEventArgs e)
        {
            App.LoggingService?.LogWarning("[RESTART] Usuário escolheu: REINICIAR IMEDIATAMENTE");
            UserChoice = RestartChoice.RestartNow;
            DialogResult = true; // Confirmado (aplicar e reiniciar)
            Close();
        }
    
        private void RestartConfirmationModal_SourceInitialized(object? sender, System.EventArgs e)
        {
            try
            {
                var settings = VoltrisOptimizer.Services.SettingsService.Instance.Settings;
                if (settings.EnableTransparency)
                {
                    bool isLight = settings.Theme?.Equals("Light", System.StringComparison.OrdinalIgnoreCase) == true;
                    VoltrisOptimizer.UI.Helpers.BackdropHelper.ApplyModernBackdrop(this, VoltrisOptimizer.UI.Helpers.BackdropHelper.SystemBackdropType.Acrylic, isLight);
                }
            }
            catch (System.Exception ex)
            {
                VoltrisOptimizer.App.LoggingService?.LogError($"[RestartConfirmationModal] Erro ao aplicar backdrop de transparencia", ex);
            }
        }}
}
