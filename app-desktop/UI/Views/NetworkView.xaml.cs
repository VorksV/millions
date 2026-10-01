using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using VoltrisOptimizer;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.UI.Controls;

namespace VoltrisOptimizer.UI.Views
{
    public partial class NetworkView : UserControl
    {
        private bool _isRunning = false;
        private Dictionary<string, (string[] v4, string[] v6)> _dnsProviders;

        public NetworkView()
        {
            InitializeComponent();
            InitializeDNSProviders();
            Loaded += NetworkView_Loaded;
        }

        private void InitializeDNSProviders()
        {
            _dnsProviders = new Dictionary<string, (string[] v4, string[] v6)>
            {
                { "Google DNS", (new[] { "8.8.8.8", "8.8.4.4" }, new[] { "2001:4860:4860::8888", "2001:4860:4860::8844" }) },
                { "Cloudflare DNS", (new[] { "1.1.1.1", "1.0.0.1" }, new[] { "2606:4700:4700::1111", "2606:4700:4700::1001" }) },
                { "Quad9 DNS", (new[] { "9.9.9.9", "149.112.112.112" }, new[] { "2620:fe::fe", "2620:fe::9" }) },
                { "OpenDNS", (new[] { "208.67.222.222", "208.67.220.220" }, new[] { "2620:0:ccc::2", "2620:0:ccd::2" }) },
                { "AdGuard DNS", (new[] { "94.140.14.14", "94.140.15.15" }, new[] { "2a10:50c0::ad1:ff", "2a10:50c0::ad2:ff" }) }
            };

            cmbDNSOptions.ItemsSource = _dnsProviders.Keys;
            cmbDNSOptions.SelectedIndex = 1; // Default to Cloudflare
        }

        private void NetworkView_Loaded(object sender, RoutedEventArgs e)
        {
            PopulateNetworkInterfaces();
        }

        private void PopulateNetworkInterfaces()
        {
            try
            {
                App.LoggingService?.LogInfo("[NetworkView] Populando interfaces de rede...");
                cmbNetworkInterfaces.Items.Clear();

                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(nic => (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ||
                                   nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet) &&
                                   nic.OperationalStatus == OperationalStatus.Up)
                    .ToList();

                foreach (var nic in interfaces)
                {
                    cmbNetworkInterfaces.Items.Add(nic.Name);
                }

                if (cmbNetworkInterfaces.Items.Count > 0)
                {
                    cmbNetworkInterfaces.SelectedIndex = 0;
                    App.LoggingService?.LogDebug($"[NetworkView] {cmbNetworkInterfaces.Items.Count} interfaces encontradas.");
                }
                else
                {
                    App.LoggingService?.LogWarning("[NetworkView] Nenhuma interface de rede ativa encontrada.");
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[NetworkView] Erro ao popular interfaces", ex);
            }
        }

        private void cmbNetworkInterfaces_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbNetworkInterfaces.SelectedItem != null)
            {
                DisplayNetworkInfo(cmbNetworkInterfaces.SelectedItem.ToString());
            }
        }

        private void DisplayNetworkInfo(string interfaceName)
        {
            try
            {
                var nic = NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(n => n.Name == interfaceName);

                if (nic == null) return;

                var ipProps = nic.GetIPProperties();
                var dnsAddresses = ipProps.DnsAddresses;

                // Reset fields
                string notDefined = LocalizationService.Instance.GetString("CommonNotDefined");
                txtIPv4DNSPrimary.Text = notDefined;
                txtIPv4DNSSecondary.Text = notDefined;
                txtIPv6DNSPrimary.Text = notDefined;
                txtIPv6DNSSecondary.Text = notDefined;

                var ipv4Dns = dnsAddresses.Where(d => d.AddressFamily == AddressFamily.InterNetwork).ToList();
                if (ipv4Dns.Count > 0) txtIPv4DNSPrimary.Text = ipv4Dns[0].ToString();
                if (ipv4Dns.Count > 1) txtIPv4DNSSecondary.Text = ipv4Dns[1].ToString();

                var ipv6Dns = dnsAddresses.Where(d => d.AddressFamily == AddressFamily.InterNetworkV6).ToList();
                if (ipv6Dns.Count > 0) txtIPv6DNSPrimary.Text = ipv6Dns[0].ToString();
                if (ipv6Dns.Count > 1) txtIPv6DNSSecondary.Text = ipv6Dns[1].ToString();

                App.LoggingService?.LogDebug($"[NetworkView] Info de rede atualizada para: {interfaceName}");
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[NetworkView] Erro ao exibir info de rede", ex);
            }
        }

        private async void ApplyDNS_Click(object sender, RoutedEventArgs e)
        {
            // SaaS-LEVEL: Feature Gate centralizado para bloqueio profissional
            App.LoggingService?.LogInfo("[NetworkView] INICIANDO VERIFICAÇÃO DE FEATURE GATE PARA APLICAÇÃO DE DNS...");

            var isNetworkOptimizationEnabled = VoltrisOptimizer.Services.License.LicenseOrchestrationService.Features.IsOptimizationEnabled;
            App.LoggingService?.LogInfo($"[NetworkView] Feature Gate Result: IsOptimizationEnabled={isNetworkOptimizationEnabled}");

            if (!isNetworkOptimizationEnabled)
            {
                App.LoggingService?.LogWarning("[NetworkView] FEATURE GATE BLOQUEADO - Otimização de rede não está habilitada");

                var licenseState = await VoltrisOptimizer.Services.License.LicenseOrchestrationService.Instance.GetCurrentStateAsync();
                App.LoggingService?.LogInfo($"[NetworkView] Estado: {licenseState.FormattedStatus} ({licenseState.LicenseType})");

                var message = string.Format(
                    LocalizationService.Instance.GetString("NetworkLicenseGateMessage"),
                    licenseState.FormattedStatus);

                VoltrisOptimizer.UI.Controls.LicenseRequiredMessageBox.Show(
                    message,
                    LocalizationService.Instance.GetString("LicenseRequiredTitle"));

                App.LoggingService?.LogInfo("[NetworkView] Aplicação de DNS BLOQUEADA e finalizada");
                return;
            }

            App.LoggingService?.LogInfo("[NetworkView] FEATURE GATE APROVADO - Otimização de rede habilitada, continuando...");

            if (cmbNetworkInterfaces.SelectedItem == null || cmbDNSOptions.SelectedItem == null)
            {
                ModernMessageBox.Show(
                    LocalizationService.Instance.GetString("NetworkSelectInterface"),
                    LocalizationService.Instance.GetString("CommonWarning"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning,
                    Window.GetWindow(this));
                return;
            }

            string nicName = cmbNetworkInterfaces.SelectedItem.ToString();
            string dnsProvider = cmbDNSOptions.SelectedItem.ToString();

            if (!_dnsProviders.ContainsKey(dnsProvider)) return;

            var (v4, v6) = _dnsProviders[dnsProvider];

            bool started = GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("NetworkOpApplyDns"), false);

            try
            {
                ApplyDNSButton.IsEnabled = false;
                ApplyDNSButton.Content = LocalizationService.Instance.GetString("NetworkApplying");

                App.LoggingService?.LogInfo($"[NetworkView] Iniciando aplicação de DNS: {dnsProvider} em {nicName}");

                bool success = await App.NetworkOptimizer.SetDnsAsync(nicName, v4, v6);

                if (success)
                {
                    App.LoggingService?.LogSuccess($"[NetworkView] DNS {dnsProvider} aplicado com sucesso.");
                    HistoryService.RecordActivity("System Optimization", $"DNS alterado para {dnsProvider}.");

                    GlobalNotificationService.ShowSuccess("Network", string.Format(LocalizationService.Instance.GetString("NetworkSuccessDns"), dnsProvider));

                    if (started) GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("NetworkOpDnsApplied"), dnsProvider));

                    DisplayNetworkInfo(nicName);
                }
                else
                {
                    ModernMessageBox.Show(
                        LocalizationService.Instance.GetString("NetworkFailDns"),
                        LocalizationService.Instance.GetString("CommonError"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error,
                        Window.GetWindow(this));

                    GlobalNotificationService.ShowError("Network", LocalizationService.Instance.GetString("NetworkFailDns"));
                    if (started) GlobalProgressService.Instance.FailOperation(string.Format(LocalizationService.Instance.GetString("NetworkOpFailApplyDns"), dnsProvider));
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[NetworkView] Erro ao aplicar DNS", ex);
                GlobalNotificationService.ShowError("Network", string.Format(LocalizationService.Instance.GetString("NetworkErrorApplyDnsMsg"), ex.Message));
                if (started) GlobalProgressService.Instance.FailOperation(string.Format(LocalizationService.Instance.GetString("NetworkOpErrorApplyDns"), ex.Message));
                ModernMessageBox.Show(
                    $"{LocalizationService.Instance.GetString("CommonError")}: {ex.Message}",
                    LocalizationService.Instance.GetString("CommonError"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error,
                    Window.GetWindow(this));
            }
            finally
            {
                ApplyDNSButton.IsEnabled = true;
                ApplyDNSButton.Content = LocalizationService.Instance.GetString("NetworkApplyDns");
                if (started) GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("NetworkOpApplyDnsComplete"));
            }
        }

        private async void ResetDefaultDNS_Click(object sender, RoutedEventArgs e)
        {
            if (cmbNetworkInterfaces.SelectedItem == null)
            {
                ModernMessageBox.Show(
                    LocalizationService.Instance.GetString("NetworkSelectInterfaceRestore"),
                    LocalizationService.Instance.GetString("CommonWarning"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning,
                    Window.GetWindow(this));
                return;
            }

            string nicName = cmbNetworkInterfaces.SelectedItem.ToString();

            bool started = GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("NetworkOpRestoreDns"), false);

            try
            {
                ResetDNSButton.IsEnabled = false;
                ResetDNSButton.Content = LocalizationService.Instance.GetString("NetworkRestoring");

                App.LoggingService?.LogInfo($"[NetworkView] Restaurando DNS para DHCP em {nicName}");

                bool success = await App.NetworkOptimizer.ResetDnsAsync(nicName);

                if (success)
                {
                    App.LoggingService?.LogSuccess($"[NetworkView] DNS de {nicName} restaurado para DHCP.");
                    HistoryService.RecordActivity("System Optimization", "DNS restaurado para o padrão (DHCP).");

                    GlobalNotificationService.ShowSuccess("Network", LocalizationService.Instance.GetString("NetworkSuccessDnsRestore"));

                    if (started) GlobalProgressService.Instance.CompleteOperation(string.Format(LocalizationService.Instance.GetString("NetworkOpDnsRestored"), nicName));

                    DisplayNetworkInfo(nicName);
                }
                else
                {
                    ModernMessageBox.Show(
                        LocalizationService.Instance.GetString("NetworkFailDnsRestore"),
                        LocalizationService.Instance.GetString("CommonError"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error,
                        Window.GetWindow(this));

                    GlobalNotificationService.ShowError("Network", LocalizationService.Instance.GetString("NetworkFailDnsRestore"));
                    if (started) GlobalProgressService.Instance.FailOperation(string.Format(LocalizationService.Instance.GetString("NetworkOpFailRestoreDns"), nicName));
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[NetworkView] Erro ao restaurar DNS", ex);
                GlobalNotificationService.ShowError("Network", string.Format(LocalizationService.Instance.GetString("NetworkErrorRestoreDnsMsg"), ex.Message));
                if (started) GlobalProgressService.Instance.FailOperation(string.Format(LocalizationService.Instance.GetString("NetworkOpErrorRestoreDns"), ex.Message));
            }
            finally
            {
                ResetDNSButton.IsEnabled = true;
                ResetDNSButton.Content = LocalizationService.Instance.GetString("NetworkRestoreDefault");
                if (started) GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("NetworkOpRestoreDnsComplete"));
            }
        }

        private async void RunNetworkButton_Click(object sender, RoutedEventArgs e)
        {
            // SaaS-LEVEL: Feature Gate centralizado para bloqueio profissional
            App.LoggingService?.LogInfo("[NetworkView] INICIANDO VERIFICAÇÃO DE FEATURE GATE PARA OTIMIZAÇÃO DE REDE...");

            var isNetworkOptimizationEnabled = VoltrisOptimizer.Services.License.LicenseOrchestrationService.Features.IsOptimizationEnabled;
            App.LoggingService?.LogInfo($"[NetworkView] Feature Gate Result: IsOptimizationEnabled={isNetworkOptimizationEnabled}");

            if (!isNetworkOptimizationEnabled)
            {
                App.LoggingService?.LogWarning("[NetworkView] FEATURE GATE BLOQUEADO - Otimização de rede não está habilitada");

                var licenseState = await VoltrisOptimizer.Services.License.LicenseOrchestrationService.Instance.GetCurrentStateAsync();
                App.LoggingService?.LogInfo($"[NetworkView] Estado: {licenseState.FormattedStatus} ({licenseState.LicenseType})");

                var message = string.Format(
                    LocalizationService.Instance.GetString("NetworkLicenseGateMessage"),
                    licenseState.FormattedStatus);

                VoltrisOptimizer.UI.Controls.LicenseRequiredMessageBox.Show(
                    message,
                    LocalizationService.Instance.GetString("LicenseRequiredTitle"));

                App.LoggingService?.LogInfo("[NetworkView] Otimização de rede BLOQUEADA e finalizada");
                return;
            }

            App.LoggingService?.LogInfo("[NetworkView] FEATURE GATE APROVADO - Otimização de rede habilitada, continuando...");

            bool started = GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("NetworkOpOptimization"), false);

            try
            {
                RunNetworkButton.IsEnabled = false;

                if (App.NetworkOptimizer == null)
                {
                    App.LoggingService?.LogWarning("[NetworkView] NetworkOptimizer indisponível");
                    GlobalNotificationService.ShowWarning("Network", LocalizationService.Instance.GetString("NetworkOptimizerUnavailable"));
                    return;
                }

                await App.NetworkOptimizer.FlushDnsAsync();
                await App.NetworkOptimizer.RenewDhcpAsync();
                await App.NetworkOptimizer.ResetWinsockAsync();
                await App.NetworkOptimizer.ResetIPStackAsync();

                GlobalNotificationService.ShowSuccess("Network", LocalizationService.Instance.GetString("NetworkSuccessOptimization"));
                if (started) GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("NetworkOpOptimizationComplete"));
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[NetworkView] Erro ao otimizar rede", ex);
                GlobalNotificationService.ShowError("Network", string.Format(LocalizationService.Instance.GetString("NetworkErrorOptimizationMsg"), ex.Message));
                if (started) GlobalProgressService.Instance.FailOperation(string.Format(LocalizationService.Instance.GetString("NetworkOpErrorOptimization"), ex.Message));
                ModernMessageBox.Show(
                    $"{LocalizationService.Instance.GetString("CommonError")}: {ex.Message}",
                    LocalizationService.Instance.GetString("CommonError"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error,
                    Window.GetWindow(this));
            }
            finally
            {
                RunNetworkButton.IsEnabled = true;
                if (started) GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("NetworkOpOptimizationFinished"));
            }
        }

        // New method to handle OptimizeStackButton_Click – performs selected network stack actions
        private async void OptimizeStackButton_Click(object sender, RoutedEventArgs e)
        {
            // Feature gate check (same as other network actions)
            App.LoggingService?.LogInfo("[NetworkView] INICIANDO VERIFICAÇÃO DE FEATURE GATE PARA OTIMIZAÇÃO DE PILHA DE REDE...");

            var isNetworkOptimizationEnabled = VoltrisOptimizer.Services.License.LicenseOrchestrationService.Features.IsOptimizationEnabled;
            App.LoggingService?.LogInfo($"[NetworkView] Feature Gate Result: IsOptimizationEnabled={isNetworkOptimizationEnabled}");

            if (!isNetworkOptimizationEnabled)
            {
                App.LoggingService?.LogWarning("[NetworkView] FEATURE GATE BLOQUEADO - Otimização de rede não está habilitada");

                var licenseState = await VoltrisOptimizer.Services.License.LicenseOrchestrationService.Instance.GetCurrentStateAsync();
                App.LoggingService?.LogInfo($"[NetworkView] Estado: {licenseState.FormattedStatus} ({licenseState.LicenseType})");

                var message = string.Format(
                    LocalizationService.Instance.GetString("NetworkLicenseGateMessage"),
                    licenseState.FormattedStatus);

                VoltrisOptimizer.UI.Controls.LicenseRequiredMessageBox.Show(
                    message,
                    LocalizationService.Instance.GetString("LicenseRequiredTitle"));

                App.LoggingService?.LogInfo("[NetworkView] Otimização de pilha BLOQUEADA e finalizada");
                return;
            }

            bool started = GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("NetworkOpStackOptimization"), false);
            try
            {
                OptimizeStackButton.IsEnabled = false;

                if (App.NetworkOptimizer == null)
                {
                    App.LoggingService?.LogWarning("[NetworkView] NetworkOptimizer indisponível");
                    GlobalNotificationService.ShowWarning("Network", LocalizationService.Instance.GetString("NetworkOptimizerUnavailable"));
                    return;
                }

                // Execute selected actions based on checkboxes
                bool anySuccess = true;
                if (NetworkFlushDNS?.IsChecked == true)
                {
                    App.LoggingService?.LogInfo("[NetworkView] Flush DNS selecionado");
                    var success = await App.NetworkOptimizer.FlushDnsAsync();
                    anySuccess &= success;
                }
                if (NetworkResetWinsock?.IsChecked == true)
                {
                    App.LoggingService?.LogInfo("[NetworkView] Reset Winsock selecionado");
                    var success = await App.NetworkOptimizer.ResetWinsockAsync();
                    anySuccess &= success;
                }
                if (NetworkResetTCP?.IsChecked == true)
                {
                    App.LoggingService?.LogInfo("[NetworkView] Reset IP Stack selecionado");
                    var success = await App.NetworkOptimizer.ResetIPStackAsync();
                    anySuccess &= success;
                }
                if (NetworkRenewDHCP?.IsChecked == true)
                {
                    App.LoggingService?.LogInfo("[NetworkView] Renovar DHCP selecionado");
                    var success = await App.NetworkOptimizer.RenewDhcpAsync();
                    anySuccess &= success;
                }

                if (anySuccess)
                {
                    GlobalNotificationService.ShowSuccess("Network", LocalizationService.Instance.GetString("NetworkOptimizationsSuccess"));
                    if (started) GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("NetworkOpStackOptimizationComplete"));
                }
                else
                {
                    GlobalNotificationService.ShowWarning("Network", LocalizationService.Instance.GetString("NetworkOptimizationsPartial"));
                    if (started) GlobalProgressService.Instance.FailOperation(LocalizationService.Instance.GetString("NetworkOpStackOptimizationPartial"));
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[NetworkView] Erro ao otimizar pilha de rede", ex);
                GlobalNotificationService.ShowError("Network", string.Format(LocalizationService.Instance.GetString("NetworkErrorStackOptimizationMsg"), ex.Message));
                if (started) GlobalProgressService.Instance.FailOperation(string.Format(LocalizationService.Instance.GetString("NetworkOpErrorStackOptimization"), ex.Message));
                ModernMessageBox.Show(
                    $"{LocalizationService.Instance.GetString("CommonError")}: {ex.Message}",
                    LocalizationService.Instance.GetString("CommonError"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error,
                    Window.GetWindow(this));
            }
            finally
            {
                OptimizeStackButton.IsEnabled = true;
                if (started) GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("NetworkOpStackOptimizationFinished"));
            }
        }
    }
}
#if false
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using VoltrisOptimizer;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.UI.Controls;

namespace VoltrisOptimizer.UI.Views
{
    public partial class NetworkView : UserControl
    {
        // Duplicate content suppressed
    }
}
#endif
