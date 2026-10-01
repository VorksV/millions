using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.License;
using VoltrisOptimizer.Services.License.Models;

namespace VoltrisOptimizer.UI.Behaviors
{
    /// <summary>
    /// LICENSE GUARD BEHAVIOR - SaaS Level UI Protection
    /// Comportamento WPF para bloqueio automático de UI baseado em licença
    /// Implementa feature gating profissional em nível de interface
    /// </summary>
    public class LicenseGuardBehavior : Behavior<FrameworkElement>
    {
        #region Properties

        private string _feature = "Optimization";
        public string Feature
        {
            get => _feature;
            set => _feature = value;
        }

        private string _blockMessage = "This feature requires a Pro license";
        public string BlockMessage
        {
            get => _blockMessage;
            set => _blockMessage = value;
        }

        private bool _showBlockDialog = true;
        public bool ShowBlockDialog
        {
            get => _showBlockDialog;
            set => _showBlockDialog = value;
        }

        private bool _disableVisually = true;
        public bool DisableVisually
        {
            get => _disableVisually;
            set => _disableVisually = value;
        }

        private double _blockedOpacity = 0.5;
        public double BlockedOpacity
        {
            get => _blockedOpacity;
            set => _blockedOpacity = value;
        }

        #endregion

        #region Private Fields

        private bool _isBlocked = false;
        private Storyboard? _fadeStoryboard;
        private readonly object _lockObject = new object();

        #endregion

        #region Behavior Lifecycle

        protected override void OnAttached()
        {
            // Subscribe to license state changes
            VoltrisOptimizer.Services.License.LicenseOrchestrationService.Instance.StateChanged += OnLicenseStateChanged;
            
            // Initial state check - executado de forma não-bloqueante na UI thread
            if (AssociatedObject != null)
            {
                AssociatedObject.Dispatcher.BeginInvoke(new Action(() =>
                {
                    CheckLicenseState();
                }));
            }
            
            // Handle click events for buttons
            if (AssociatedObject is Button button)
            {
                button.Click += OnButtonClick;
            }
            else if (AssociatedObject is MenuItem menuItem)
            {
                menuItem.Click += OnMenuClick;
            }
        }

        protected override void OnDetaching()
        {
            // Unsubscribe from events
            VoltrisOptimizer.Services.License.LicenseOrchestrationService.Instance.StateChanged -= OnLicenseStateChanged;
            
            if (AssociatedObject is Button button)
            {
                button.Click -= OnButtonClick;
            }
            else if (AssociatedObject is MenuItem menuItem)
            {
                menuItem.Click -= OnMenuClick;
            }
            
            base.OnDetaching();
        }

        #endregion

        #region Event Handlers

        private void OnFeatureChanged()
        {
            if (AssociatedObject != null)
            {
                AssociatedObject.Dispatcher.BeginInvoke(new Action(() =>
                {
                    CheckLicenseState();
                }));
            }
        }

        private void OnLicenseStateChanged(object? sender, LicenseStateChangedEventArgs e)
        {
            // Decoplar completamente a thread de monitoramento/background da UI thread
            // evitando que o Semaphore do orquestrador sofra nested deadlock
            if (AssociatedObject != null)
            {
                AssociatedObject.Dispatcher.BeginInvoke(new Action(() =>
                {
                    CheckLicenseState();
                }));
            }
        }

        private async void OnButtonClick(object? sender, RoutedEventArgs e)
        {
            await HandleInteractionAsync(sender);
        }

        private async void OnMenuClick(object? sender, RoutedEventArgs e)
        {
            await HandleInteractionAsync(sender);
        }

        #endregion

        #region Core Logic

        /// <summary>
        /// Verifica o estado da licença e aplica proteção de forma síncrona
        /// </summary>
        private void CheckLicenseState()
        {
            try
            {
                if (string.IsNullOrEmpty(Feature))
                {
                    SetUnblockedState();
                    return;
                }

                var orchestrator = VoltrisOptimizer.Services.License.LicenseOrchestrationService.Instance;
                var licenseState = orchestrator.CachedState ?? new LicenseState { LicenseType = "None", FormattedStatus = "Não Licenciado" };
                var isFeatureEnabled = IsFeatureEnabled(Feature);

                lock (_lockObject)
                {
                    if (isFeatureEnabled)
                    {
                        SetUnblockedState();
                    }
                    else
                    {
                        SetBlockedState(licenseState);
                    }
                }
            }
            catch (Exception ex)
            {
                // Log error but don't crash UI
                System.Diagnostics.Debug.WriteLine($"[LicenseGuard] Error checking license: {ex.Message}");
                SetUnblockedState(); // Fail open
            }
        }

        /// <summary>
        /// Verifica se uma feature específica está habilitada de forma síncrona
        /// </summary>
        private bool IsFeatureEnabled(string feature)
        {
            return feature.ToUpperInvariant() switch
            {
                "OPTIMIZATION" => VoltrisOptimizer.Services.License.LicenseOrchestrationService.Features.IsOptimizationEnabled,
                "GAMERMODE" => VoltrisOptimizer.Services.License.LicenseOrchestrationService.Features.IsGamerModeEnabled,
                "ADVANCEDTOOLS" => VoltrisOptimizer.Services.License.LicenseOrchestrationService.Features.IsAdvancedToolsEnabled,
                "REALTIMEMONITORING" => VoltrisOptimizer.Services.License.LicenseOrchestrationService.Features.IsRealTimeMonitoringEnabled,
                "CLOUDSYNC" => VoltrisOptimizer.Services.License.LicenseOrchestrationService.Features.IsCloudSyncEnabled,
                "PREMIUMSUPPORT" => VoltrisOptimizer.Services.License.LicenseOrchestrationService.Features.IsPremiumSupportEnabled,
                _ => VoltrisOptimizer.Services.License.LicenseOrchestrationService.Features.IsOptimizationEnabled // Default
            };
        }

        /// <summary>
        /// Aplica estado desbloqueado ao elemento
        /// </summary>
        private void SetUnblockedState()
        {
            if (_isBlocked)
            {
                _isBlocked = false;

                if (AssociatedObject is UIElement element)
                {
                    element.IsEnabled = true;
                    element.Opacity = 1.0;
                    element.IsHitTestVisible = true;
                }

                // Remove tooltip de bloqueio
                if (AssociatedObject is FrameworkElement frameworkElement)
                {
                    FrameworkElementServices.SetToolTip(frameworkElement, null);
                }
            }
        }

        /// <summary>
        /// Aplica estado bloqueado ao elemento
        /// </summary>
        private void SetBlockedState(LicenseState licenseState)
        {
            if (!_isBlocked)
            {
                _isBlocked = true;

                if (AssociatedObject is UIElement element)
                {
                    if (DisableVisually)
                    {
                        element.IsEnabled = false;
                        element.Opacity = BlockedOpacity;
                        element.IsHitTestVisible = false;
                    }
                }

                // Adiciona tooltip informativo
                var tooltipText = $"{BlockMessage}\n\nStatus: {licenseState.FormattedStatus}";
                if (AssociatedObject is FrameworkElement frameworkElement)
                {
                    FrameworkElementServices.SetToolTip(frameworkElement, tooltipText);
                }
            }
        }

        /// <summary>
        /// Manipula interações do usuário com elementos bloqueados
        /// </summary>
        private async Task HandleInteractionAsync(object? sender)
        {
            if (_isBlocked && ShowBlockDialog)
            {
                try
                {
                    var orchestrator = VoltrisOptimizer.Services.License.LicenseOrchestrationService.Instance;
                    var licenseState = orchestrator.CachedState ?? new LicenseState { LicenseType = "None", FormattedStatus = "Não Licenciado" };
                    var message = FormateBlockMessage(licenseState);
                    
                    await ShowLicenseRequiredDialogAsync(message);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[LicenseGuard] Error showing dialog: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Formata mensagem de bloqueio baseada no estado da licença
        /// </summary>
        private string FormateBlockMessage(LicenseState licenseState)
        {
            return licenseState.LicenseType switch
            {
                "none" => "Nenhuma licença encontrada. Por favor, ative uma licença para utilizar esta funcionalidade.",
                "trial" when !licenseState.IsTrialActive => $"Você está na versão gratuita. Adquira o PRO para desbloquear esta funcionalidade.\n\n{BlockMessage}",
                "trial" => $"Esta funcionalidade é exclusiva do PRO. Adquira uma licença para desbloquear.\n\n{BlockMessage}",
                "pro" => "Funcionalidade indisponível para sua licença atual. Considere fazer upgrade para Enterprise.",
                "enterprise" => "Funcionalidade temporariamente indisponível. Contate o suporte.",
                _ => BlockMessage
            };
        }

        /// <summary>
        /// Mostra diálogo de licença necessária
        /// </summary>
        private async Task ShowLicenseRequiredDialogAsync(string message)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var result = VoltrisOptimizer.UI.Controls.ModernMessageBox.Show(
                    message,
                    LocalizationService.Instance.GetString("LicenseRequiredMsgTitle"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                {
                    // Abrir página de ativação de licença
                    OpenLicenseActivation();
                }
            });
        }

        /// <summary>
        /// Abre página de ativação de licença
        /// </summary>
        private void OpenLicenseActivation()
        {
            try
            {
                var mainWindow = Application.Current.MainWindow as UI.MainWindow;
                if (mainWindow != null)
                {
                    mainWindow.Dispatcher.BeginInvoke(() => {
                        var method = mainWindow.GetType().GetMethod("ActivateLicenseHeaderButton_Click", 
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                        method?.Invoke(mainWindow, new object[] { null!, null! });
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LicenseGuard] Error opening license activation: {ex.Message}");
            }
        }

        #endregion
    }

    #region Helper Classes

    /// <summary>
    /// Helper class for FrameworkElement services
    /// </summary>
    internal static class FrameworkElementServices
    {
        public static void SetToolTip(FrameworkElement element, string? tooltip)
        {
            if (element != null)
            {
                element.ToolTip = string.IsNullOrEmpty(tooltip) ? null : tooltip;
            }
        }
    }

    #endregion
}
