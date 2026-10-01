using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.UI.Views
{
    /// <summary>
    /// Modal de compra de licença com 3 planos (Standard, Pro, Enterprise)
    /// Suporta efeito Acrylic do Windows 11+
    /// </summary>
    public partial class LicensePurchaseModal : UserControl
    {
        public event EventHandler? PlanSelected;
        public event EventHandler? HaveLicenseClicked;
        public event EventHandler? Closed;

        private Border? _mainModalBorder;

        public LicensePurchaseModal()
        {
            InitializeComponent();
            Loaded += LicensePurchaseModal_Loaded;
        }

        private void LicensePurchaseModal_Loaded(object sender, RoutedEventArgs e)
        {
            // Registrar a Border principal para controle de Acrylic
            if (this.FindName("MainModalBorder") is Border mainBorder)
            {
                SetMainBorder(mainBorder);
            }

            // Aplicar efeito Acrylic se habilitado
            ApplyAcrylicEffect();
            
            // Monitorar mudanças de transparência (quando usuário clica no ícone do header)
            SettingsService.Instance.SettingsChanged += OnSettingsChanged;
        }

        private void OnSettingsChanged(object? sender, EventArgs e)
        {
            // CORREÇÃO DE CAUSA RAIZ: SettingsService.SettingsChanged é levantado pela thread
            // que alterou a configuração — frequentemente um worker em background. A implementação
            // anterior aplicava o efeito direto em _mainModalBorder, um DependencyObject pertencente
            // à UI thread, produzindo "InvalidOperationException: O thread de chamada não pode
            // acessar este objeto porque ele pertence a um thread diferente" — registrado 25x
            // no log e como first-chance exception no log de diagnóstico.
            ApplyAcrylicEffect();
        }

        private void ApplyAcrylicEffect()
        {
            void Apply()
            {
                try
                {
                    bool transparencyEnabled = SettingsService.Instance?.Settings?.EnableTransparency ?? false;
                    App.LoggingService?.LogInfo($"[LicensePurchaseModal] ApplyAcrylicEffect: EnableTransparency={transparencyEnabled}");

                    if (_mainModalBorder != null)
                    {
                        // Background SEMI-TRANSPARENTE para deixar wallpaper/backdrop aparecer
                        // Opacity 0.65-0.75 cria efeito glass morphism
                        _mainModalBorder.Background = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromArgb(180, 31, 24, 37));
                    }
                }
                catch (Exception ex)
                {
                    App.LoggingService?.LogWarning($"[LicensePurchaseModal] Erro ao aplicar o efeito Acrylic: {ex.Describe()}");
                }
            }

            var dispatcher = Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                Apply();
                return;
            }

            // Toda mutação de WPF deve ocorrer na UI thread.
            dispatcher.BeginInvoke(Apply, System.Windows.Threading.DispatcherPriority.Background);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // Desativar partículas para economizar CPU/RAM
            if (this.FindName("ParticleCanvas") is Canvas particleCanvas)
            {
                particleCanvas.Visibility = Visibility.Collapsed;
            }
            
            Closed?.Invoke(this, EventArgs.Empty);
        }

        private void StandardPlan_Click(object sender, RoutedEventArgs e)
        {
            OpenBuyUrl("standard");
        }

        private void ProPlan_Click(object sender, RoutedEventArgs e)
        {
            OpenBuyUrl("pro");
        }

        private void EnterprisePlan_Click(object sender, RoutedEventArgs e)
        {
            OpenBuyUrl("enterprise");
        }

        private void HaveLicenseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Obter a MainWindow e navegar para License (usa o método NavigateToPageSafe)
                var mainWindow = Application.Current.MainWindow as UI.MainWindow;
                if (mainWindow != null)
                {
                    App.LoggingService?.LogInfo("[LicensePurchaseModal] Navegando para License");
                    mainWindow.NavigateToPageSafe("License");
                    
                    // Fechar o modal
                    Closed?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    App.LoggingService?.LogWarning("[LicensePurchaseModal] MainWindow não encontrada");
                    HaveLicenseClicked?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[LicensePurchaseModal] Erro ao navegar para License: {ex.Message}", ex);
                HaveLicenseClicked?.Invoke(this, EventArgs.Empty);
            }
        }

        private void OpenBuyUrl(string plan)
        {
            try
            {
                App.LoggingService?.LogInfo($"[LicensePurchaseModal] Abrindo página de compra para plano: {plan}");
                var baseUrl = VoltrisOptimizer.Services.SiteConfig.PurchaseLicenseUrl;

                var url = $"{baseUrl}?plan={plan}";
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[LicensePurchaseModal] Erro ao abrir página de compra: {ex.Message}", ex);
            }
        }

        public void SetMainBorder(Border border)
        {
            _mainModalBorder = border;
        }
    }
}
