using VoltrisOptimizer.Services;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using VoltrisOptimizer.Services.Drivers;

namespace VoltrisOptimizer.UI.Views
{
    public partial class DriverDetailsModal : UserControl
    {
        private DriverUpdate _currentDriver;
        private string _sourceUrl = "";

        public DriverDetailsModal()
        {
            var sw = Stopwatch.StartNew();
            Debug.WriteLine("[DriverDetailsModal] Constructor - Enter");
            try {
                App.LoggingService?.LogInfo("[DriverDetailsModal] Iniciando construtor simplificado...");
                InitializeComponent();
                App.LoggingService?.LogInfo("[DriverDetailsModal] InitializeComponent() concluído");

                ConfigureTransparency();

                sw.Stop();
                Debug.WriteLine($"[DriverDetailsModal] Constructor - Exit duration={sw.ElapsedMilliseconds}ms");
                App.LoggingService?.LogInfo($"[DriverDetailsModal] Construtor simplificado concluído com SUCESSO duration={sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex) {
                sw.Stop();
                Debug.WriteLine($"[DriverDetailsModal] Constructor - ERRO CRÍTICO: {ex.Message}");
                App.LoggingService?.LogError($"[DriverDetailsModal] ERRO CRÍTICO no construtor: {ex.Message}", ex);
                throw;
            }
        }

        private void ConfigureTransparency()
        {
            Debug.WriteLine("[DriverDetailsModal] ConfigureTransparency - Enter");
            try {
                App.LoggingService?.LogInfo("[DriverDetailsModal] Usando design system - transparência automática");
            }
            catch (Exception ex) {
                Debug.WriteLine($"[DriverDetailsModal] ConfigureTransparency - Error: {ex.Message}");
                App.LoggingService?.LogError($"[DriverDetailsModal] Erro em ConfigureTransparency: {ex.Message}", ex);
            }
            Debug.WriteLine("[DriverDetailsModal] ConfigureTransparency - Exit");
        }

        /// <summary>Exibe o modal de detalhes para o driver especificado.</summary>
        /// <param name="driver">Informações do driver a serem exibidas.</param>
        public void ShowModal(DriverUpdate driver)
        {
            var sw = Stopwatch.StartNew();
            string driverName = driver?.Device?.FriendlyName ?? "null";
            Debug.WriteLine($"[DriverDetailsModal] ShowModal - Enter driver='{driverName}'");
            try {
                App.LoggingService?.LogInfo($"[DriverDetailsModal] ShowModal() iniciado para {driver?.Device?.FriendlyName}");

                _currentDriver = driver;

                if (driver != null) {
                    DriverNameText.Text = driver.Device?.FriendlyName ?? LocalizationService.Instance.GetString("DriverUnknownName");
                    CurrentVersionText.Text = driver.CurrentVersion ?? "--";
                    NewVersionText.Text = driver.NewDriver?.Version ?? "--";
                    VendorNameText.Text = driver.Device?.Vendor ?? "Desconhecido";
                    if (driver.NewDriver?.FileSize > 0) {
                        long fileSize = driver.NewDriver.FileSize;
                        string sizeText;
                        if (fileSize >= 1024 * 1024 * 1024) {
                            sizeText = $"{fileSize / (1024.0 * 1024.0 * 1024.0):F1} GB";
                        } else if (fileSize >= 1024 * 1024) {
                            sizeText = $"{fileSize / (1024.0 * 1024.0):F1} MB";
                        } else if (fileSize >= 1024) {
                            sizeText = $"{fileSize / 1024.0:F1} KB";
                        } else {
                            sizeText = $"{fileSize} bytes";
                        }
                        FileSizeText.Text = sizeText;
                    } else {
                        FileSizeText.Text = "--";
                    }
                    ReleaseNotesText.Text = driver.NewDriver?.ReleaseNotes ?? LocalizationService.Instance.GetString("DriverNoChangelogInfo");

                    _sourceUrl = driver.NewDriver?.DownloadUrl ?? "";

                    App.LoggingService?.LogInfo($"[DriverDetailsModal] Informações preenchidas: {driver.Device?.FriendlyName}");
                }

                if (Parent == null) {
                    App.LoggingService?.LogInfo("[DriverDetailsModal] Parent é null, procurando janela principal...");

                    Window mainWindow = Application.Current.MainWindow;
                    if (mainWindow != null) {
                        App.LoggingService?.LogInfo($"[DriverDetailsModal] MainWindow encontrada: {mainWindow.Title}");

                        if (mainWindow.Content is Grid mainGrid) {
                            App.LoggingService?.LogInfo("[DriverDetailsModal] MainWindow.Content é Grid, adicionando modal...");

                            this.HorizontalAlignment = HorizontalAlignment.Stretch;
                            this.VerticalAlignment = VerticalAlignment.Stretch;
                            this.Width = double.NaN;
                            this.Height = double.NaN;

                            mainGrid.Children.Add(this);

                            Panel.SetZIndex(this, 9999);

                            App.LoggingService?.LogInfo("[DriverDetailsModal] Modal adicionado com sucesso ao grid principal");
                        } else {
                            App.LoggingService?.LogWarning($"[DriverDetailsModal] MainWindow.Content não é Grid, é {mainWindow.Content?.GetType().Name}");
                        }
                    } else {
                        App.LoggingService?.LogError("[DriverDetailsModal] MainWindow é null!");
                    }
                } else {
                    App.LoggingService?.LogInfo("[DriverDetailsModal] Parent não é null, modal já está na árvore visual");
                }

                sw.Stop();
                Debug.WriteLine($"[DriverDetailsModal] ShowModal - Exit duration={sw.ElapsedMilliseconds}ms");
                App.LoggingService?.LogInfo($"[DriverDetailsModal] ShowModal concluído para '{driverName}' duration={sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex) {
                sw.Stop();
                Debug.WriteLine($"[DriverDetailsModal] ShowModal - Error: {ex.Message}");
                App.LoggingService?.LogError($"[DriverDetailsModal] Erro em ShowModal(): {ex.Message}", ex);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            var sw = Stopwatch.StartNew();
            Debug.WriteLine($"[DriverDetailsModal] CloseButton_Click - Enter sender={sender?.GetType().Name}");
            try {
                App.LoggingService?.LogInfo("[DriverDetailsModal] CloseButton clicado");
                CloseModal();
            }
            catch (Exception ex) {
                Debug.WriteLine($"[DriverDetailsModal] CloseButton_Click - Error: {ex.Message}");
                App.LoggingService?.LogError($"[DriverDetailsModal] Erro em CloseButton_Click: {ex.Message}", ex);
            }
            sw.Stop();
            Debug.WriteLine($"[DriverDetailsModal] CloseButton_Click - Exit duration={sw.ElapsedMilliseconds}ms");
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            var sw = Stopwatch.StartNew();
            Debug.WriteLine($"[DriverDetailsModal] CancelButton_Click - Enter sender={sender?.GetType().Name}");
            try {
                App.LoggingService?.LogInfo("[DriverDetailsModal] CancelButton clicado");
                CloseModal();
            }
            catch (Exception ex) {
                Debug.WriteLine($"[DriverDetailsModal] CancelButton_Click - Error: {ex.Message}");
                App.LoggingService?.LogError($"[DriverDetailsModal] Erro em CancelButton_Click: {ex.Message}", ex);
            }
            sw.Stop();
            Debug.WriteLine($"[DriverDetailsModal] CancelButton_Click - Exit duration={sw.ElapsedMilliseconds}ms");
        }

        private async void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            var sw = Stopwatch.StartNew();
            Debug.WriteLine($"[DriverDetailsModal] InstallButton_Click - Enter sender={sender?.GetType().Name}");
            try 
            {
                App.LoggingService?.LogInfo($"[DriverDetailsModal] InstallButton clicado para {_currentDriver?.Device?.FriendlyName}");

                if (_currentDriver?.NewDriver == null) {
                    App.LoggingService?.LogWarning("[DriverDetailsModal] Nenhum driver disponível para instalação");
                    MessageBox.Show(LocalizationService.Instance["Loc_NenhumDriverDisponivelParaInstalacao"], LocalizationService.Instance["Loc_Informacao"], MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (_currentDriver.NewDriver.Vendor.Equals("Intel", StringComparison.OrdinalIgnoreCase)) {
                    await InstallIntelDriverAutomatically();
                    return;
                }

                if (string.IsNullOrEmpty(_currentDriver.NewDriver.DownloadUrl)) {
                    App.LoggingService?.LogWarning("[DriverDetailsModal] URL de download não disponível");
                    MessageBox.Show(LocalizationService.Instance.GetString("DownloadUrlNotAvailable"), LocalizationService.Instance.GetString("Warning"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (_currentDriver.NewDriver.DownloadUrl == "N/A" || _currentDriver.NewDriver.DownloadUrl.Contains("download-center")) {
                    App.LoggingService?.LogInfo("[DriverDetailsModal] Abrindo página de download do fabricante");

                    var vendorUrl = GetVendorDownloadUrl(_currentDriver.NewDriver.Vendor);
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                        FileName = vendorUrl,
                        UseShellExecute = true
                    });

                    MessageBox.Show(string.Format(LocalizationService.Instance.GetString("ManualDownloadMessage"), _currentDriver.NewDriver.Vendor, _currentDriver.NewDriver.Title), LocalizationService.Instance.GetString("ManualDownloadTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                App.LoggingService?.LogInfo($"[DriverDetailsModal] Iniciando instalação de: {_currentDriver.NewDriver.Title}");

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                    FileName = _currentDriver.NewDriver.DownloadUrl,
                    UseShellExecute = true
                });

                MessageBox.Show(string.Format(LocalizationService.Instance.GetString("DownloadStartedMessage"), _currentDriver.NewDriver.Title), LocalizationService.Instance.GetString("DownloadStartedTitle"), MessageBoxButton.OK, MessageBoxImage.Information);

                CloseModal();
            }
            catch (Exception ex) {
                sw.Stop();
                Debug.WriteLine($"[DriverDetailsModal] InstallButton_Click - Error: {ex.Message}");
                App.LoggingService?.LogError($"[DriverDetailsModal] Erro em InstallButton_Click: {ex.Message}", ex);
                MessageBox.Show(string.Format(LocalizationService.Instance.GetString("Error") + ": {0}", ex.Message), LocalizationService.Instance.GetString("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            sw.Stop();
            Debug.WriteLine($"[DriverDetailsModal] InstallButton_Click - Exit duration={sw.ElapsedMilliseconds}ms");
            App.LoggingService?.LogInfo($"[DriverDetailsModal] InstallButton_Click - concluído duration={sw.ElapsedMilliseconds}ms");
        }

        private async Task InstallIntelDriverAutomatically()
        {
            var sw = Stopwatch.StartNew();
            Debug.WriteLine($"[DriverDetailsModal] InstallIntelDriverAutomatically - Enter title='{_currentDriver?.NewDriver?.Title}'");
            try 
            {
                App.LoggingService?.LogInfo($"[DriverDetailsModal] Iniciando instalação automática Intel: {_currentDriver.NewDriver.Title}");

                var directDownloadUrl = await FindIntelDirectDownloadUrl();

                if (string.IsNullOrEmpty(directDownloadUrl)) {
                    App.LoggingService?.LogWarning("[DriverDetailsModal] URL direta não encontrada através de scraping, usando fallback para navegador");

                    if (!string.IsNullOrEmpty(_currentDriver.NewDriver.DownloadUrl)) {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                            FileName = _currentDriver.NewDriver.DownloadUrl,
                            UseShellExecute = true
                        });

                        MessageBox.Show(LocalizationService.Instance["Loc_NaoFoiPossivelAutomatizarODownloadDesteDriverEspecifico"] + _currentDriver.NewDriver.Title + LocalizationService.Instance["Loc_CliqueEmDownloadParaBaixarEInstaleManualmente"], LocalizationService.Instance["Loc_DownloadManualNecessario"], MessageBoxButton.OK, MessageBoxImage.Information);
                    } else {
                        MessageBox.Show(LocalizationService.Instance["Loc_NaoFoiPossivelLocalizarOLinkDeDownloadParaEsteDriverIntel"], LocalizationService.Instance["Loc_ErroDeLocalizacao"], MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    return;
                }

                App.LoggingService?.LogInfo($"[DriverDetailsModal] Iniciando download automatizado de: {directDownloadUrl}");

                var downloadPath = await DownloadIntelDriverFile(directDownloadUrl);

                if (string.IsNullOrEmpty(downloadPath)) {
                    App.LoggingService?.LogError("[DriverDetailsModal] Falha no download do driver");
                    MessageBox.Show(LocalizationService.Instance.GetString("IntelDownloadFailed"), LocalizationService.Instance.GetString("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                App.LoggingService?.LogInfo($"[DriverDetailsModal] Instalando driver: {downloadPath}");

                var installSuccess = await InstallIntelDriverFromFile(downloadPath);

                App.LoggingService?.LogInfo("[DriverDetailsModal] Verificando resultado da instalação no hardware...");
                await Task.Delay(5000);

                string detectedVersion = _currentDriver.CurrentVersion ?? "";
                bool isActuallyUpdated = VersionCompare(detectedVersion, _currentDriver.NewDriver.Version);

                if (!isActuallyUpdated && installSuccess) {
                    App.LoggingService?.LogWarning("[DriverDetailsModal] Versão não mudou. Tentando forçar o Windows a carregar o novo driver (Hot-Swap)...");
                    try {
                        using (var enumerator = new SetupApiEnumerator()) {
                            bool powerCycleSucceeded = await Task.Run(() => SetupApiHelper.RestartDevice(_currentDriver.Device.DeviceInstanceId));

                            if (powerCycleSucceeded) {
                                App.LoggingService?.LogInfo("[DriverDetailsModal] Ciclo de hardware concluído. Aguardando 4s para re-leitura...");
                                await Task.Delay(4000);
                                var devices = enumerator.EnumerateDevices();
                                var hwTargetAfter = devices.FirstOrDefault(d => d.DeviceInstanceId.Equals(_currentDriver.Device.DeviceInstanceId, StringComparison.OrdinalIgnoreCase));
                                if (hwTargetAfter != null) {
                                    detectedVersion = hwTargetAfter.DriverVersion;
                                    App.LoggingService?.LogInfo($"[DriverDetailsModal] Nova leitura pós-reparo: {detectedVersion}");
                                    isActuallyUpdated = VersionCompare(detectedVersion, _currentDriver.NewDriver.Version);
                                }
                            }
                        }
                    } catch (Exception ex) {
                        Debug.WriteLine($"[DriverDetailsModal] InstallIntelDriverAutomatically - Hot-Swap error: {ex.Message}");
                        App.LoggingService?.LogError($"[DriverDetailsModal] Falha no Hot-Swap: {ex.Message}");
                    }
                }

                if (installSuccess) {
                    isActuallyUpdated = VersionCompare(detectedVersion, _currentDriver.NewDriver.Version);

                    if (isActuallyUpdated) {
                        App.LoggingService?.LogSuccess($"[DriverDetailsModal] Driver Intel atualizado com SUCESSO e VALIDADO");
                        MessageBox.Show(LocalizationService.Instance["Loc_Driver"] + _currentDriver.NewDriver.Title + LocalizationService.Instance["Loc_InstaladoEValidadoComSucesso"], LocalizationService.Instance["Loc_InstalacaoConcluida"], MessageBoxButton.OK, MessageBoxImage.Information);
                    } else {
                        App.LoggingService?.LogWarning($"[DriverDetailsModal] Driver instalado (Código 0), mas versão no sistema ({detectedVersion}) difere da nova ({_currentDriver.NewDriver.Version}).");

                        var res = MessageBox.Show(
                            string.Format(LocalizationService.Instance.GetString("RestartRequiredMessage2"), detectedVersion), 
                            LocalizationService.Instance.GetString("RestartRequiredTitle2"), 
                            MessageBoxButton.YesNo, 
                            MessageBoxImage.Warning);

                        if (res == MessageBoxResult.Yes) {
                            App.LoggingService?.LogInfo("[DriverDetailsModal] Usuário solicitou reinicialização imediata.");
                            Process.Start("shutdown.exe", "/r /t 0");
                        }
                    }
                } else {
                    App.LoggingService?.LogError("[DriverDetailsModal] Falha na instalação do driver (Código de erro)");
                    MessageBox.Show(LocalizationService.Instance["Loc_OInstaladorDoDriverIntelRetornouUmErroOuFoiCancelado"], LocalizationService.Instance.GetString("AutoInstallErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                }

                CloseModal();
            }
            catch (Exception ex) {
                sw.Stop();
                Debug.WriteLine($"[DriverDetailsModal] InstallIntelDriverAutomatically - Error: {ex.Message}");
                App.LoggingService?.LogError($"[DriverDetailsModal] Erro na instalação automática Intel: {ex.Message}", ex);
                MessageBox.Show(string.Format(LocalizationService.Instance.GetString("Error") + ": {0}", ex.Message), LocalizationService.Instance.GetString("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            sw.Stop();
            Debug.WriteLine($"[DriverDetailsModal] InstallIntelDriverAutomatically - Exit duration={sw.ElapsedMilliseconds}ms");
            App.LoggingService?.LogInfo($"[DriverDetailsModal] InstallIntelDriverAutomatically - concluído duration={sw.ElapsedMilliseconds}ms");
        }

        private async Task<string> FindIntelDirectDownloadUrl()
        {
            var sw = Stopwatch.StartNew();
            Debug.WriteLine($"[DriverDetailsModal] FindIntelDirectDownloadUrl - Enter url='{_currentDriver?.NewDriver?.DownloadUrl}'");
            try {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                var response = await client.GetAsync(_currentDriver.NewDriver.DownloadUrl);
                var html = await response.Content.ReadAsStringAsync();

                var downloadRegex = new Regex(@"(?:href|data-href|url)=['""]([^'""\s]+\.(?:exe|msi|zip)[^'""\s]*)", RegexOptions.IgnoreCase);
                var rawLinksRegex = new Regex(@"['""](https?://[^'""\s]+\.(?:exe|msi|zip))['""]", RegexOptions.IgnoreCase);

                var allMatches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (Match m in downloadRegex.Matches(html)) allMatches.Add(NormalizeIntelUrl(m.Groups[1].Value));
                foreach (Match m in rawLinksRegex.Matches(html)) allMatches.Add(m.Groups[1].Value);

                string versionFilter = _currentDriver.NewDriver.Version.Replace(".", "_");
                var priorityLinks = allMatches
                    .OrderByDescending(u => u.Contains("downloadmirror.intel.com"))
                    .ThenByDescending(u => u.Contains(_currentDriver.NewDriver.Version))
                    .ThenByDescending(u => u.Contains(versionFilter))
                    .ThenByDescending(u => u.Contains("64bit", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (priorityLinks.Any()) {
                    sw.Stop();
                    Debug.WriteLine($"[DriverDetailsModal] FindIntelDirectDownloadUrl - Link encontrado: {priorityLinks[0]} duration={sw.ElapsedMilliseconds}ms");
                    App.LoggingService?.LogSuccess($"[DriverDetailsModal] Link direto encontrado via Scraper: {priorityLinks[0]}");
                    return priorityLinks[0];
                }
            }
            catch (Exception ex) {
                sw.Stop();
                Debug.WriteLine($"[DriverDetailsModal] FindIntelDirectDownloadUrl - Error: {ex.Message}");
                App.LoggingService?.LogError($"[DriverDetailsModal] Erro Crítico no Scraper Intel: {ex.Message}", ex);
                return null;
            }
            sw.Stop();
            Debug.WriteLine($"[DriverDetailsModal] FindIntelDirectDownloadUrl - Exit (not found) duration={sw.ElapsedMilliseconds}ms");
            return null;
        }

        private string NormalizeIntelUrl(string url)
        {
            Debug.WriteLine($"[DriverDetailsModal] NormalizeIntelUrl - Enter url='{url}'");
            string result;
            if (url.StartsWith("//")) result = "https:" + url;
            else if (url.StartsWith("/")) result = "https://www.intel.com" + url;
            else if (!url.StartsWith("http")) result = "https://www.intel.com/content/www/us/en/" + url;
            else result = url;
            Debug.WriteLine($"[DriverDetailsModal] NormalizeIntelUrl - result='{result}'");
            return result;
        }

        private async Task<string> DownloadIntelDriverFile(string downloadUrl)
        {
            var sw = Stopwatch.StartNew();
            Debug.WriteLine($"[DriverDetailsModal] DownloadIntelDriverFile - Enter url='{downloadUrl}'");
            try {
                var tempPath = Path.Combine(Path.GetTempPath(), "VoltrisDrivers", Guid.NewGuid().ToString());
                Directory.CreateDirectory(tempPath);

                string safeTitle = Regex.Replace(_currentDriver.NewDriver.Title, @"[^a-zA-Z0-9_\-]", "_");
                var extension = downloadUrl.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) ? ".msi" : 
                                downloadUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? ".zip" : ".exe";

                var fileName = $"Intel_{safeTitle}_{_currentDriver.NewDriver.Version}{extension}";
                var filePath = Path.Combine(tempPath, fileName);

                using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

                App.LoggingService?.LogInfo($"[DriverDetailsModal] Baixando para: {filePath}");

                var response = await client.GetAsync(downloadUrl);
                response.EnsureSuccessStatusCode();

                using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None)) {
                    await response.Content.CopyToAsync(fs);
                }

                sw.Stop();
                Debug.WriteLine($"[DriverDetailsModal] DownloadIntelDriverFile - Concluído: {filePath} duration={sw.ElapsedMilliseconds}ms");
                App.LoggingService?.LogSuccess($"[DriverDetailsModal] Download concluído: {filePath}");
                return filePath;
            }
            catch (Exception ex) {
                sw.Stop();
                Debug.WriteLine($"[DriverDetailsModal] DownloadIntelDriverFile - Error: {ex.Message}");
                App.LoggingService?.LogError($"[DriverDetailsModal] Erro no download: {ex.Message}", ex);
                return null;
            }
        }

        private async Task<bool> InstallIntelDriverFromFile(string filePath)
        {
            var sw = Stopwatch.StartNew();
            Debug.WriteLine($"[DriverDetailsModal] InstallIntelDriverFromFile - Enter file='{filePath}'");
            try {
                App.LoggingService?.LogInfo($"[DriverDetailsModal] Executando instalação: {filePath}");

                var startInfo = new System.Diagnostics.ProcessStartInfo {
                    FileName = filePath,
                    Arguments = $"-s -silent /S /v/qn /quiet /norestart", 
                    UseShellExecute = true,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                };

                using var process = System.Diagnostics.Process.Start(startInfo);

                await Task.Delay(2000);

                if (!process.HasExited) {
                    App.LoggingService?.LogInfo($"[DriverDetailsModal] Instalador Intel iniciado, aguardando conclusão...");

                    await Task.Run(() => {
                        process.WaitForExit(300000);
                    });

                    App.LoggingService?.LogInfo($"[DriverDetailsModal] Instalador concluído com código: {process.ExitCode}");
                    sw.Stop();
                    Debug.WriteLine($"[DriverDetailsModal] InstallIntelDriverFromFile - Exit code={process.ExitCode} duration={sw.ElapsedMilliseconds}ms");
                    return process.ExitCode == 0 || process.ExitCode == 1641 || process.ExitCode == 3010;
                }

                sw.Stop();
                Debug.WriteLine($"[DriverDetailsModal] InstallIntelDriverFromFile - Exit (process exited early) duration={sw.ElapsedMilliseconds}ms");
                return false;
            }
            catch (Exception ex) {
                sw.Stop();
                Debug.WriteLine($"[DriverDetailsModal] InstallIntelDriverFromFile - Error: {ex.Message}");
                App.LoggingService?.LogError($"[DriverDetailsModal] Erro na instalação: {ex.Message}", ex);
                return false;
            }
        }

        private string GetVendorDownloadUrl(string vendor)
        {
            Debug.WriteLine($"[DriverDetailsModal] GetVendorDownloadUrl - Enter vendor='{vendor}'");
            App.LoggingService?.LogInfo($"[DriverDetailsModal] GetVendorDownloadUrl - vendor='{vendor}'");
            string result = vendor?.ToLowerInvariant() switch {
                "intel" => "https://www.intel.com/content/www/us/en/download-center/home.html",
                "nvidia" => "https://www.nvidia.com/Download/index.aspx",
                "amd" => "https://www.amd.com/en/support",
                "realtek" => "https://www.realtek.com/en/component-download",
                "samsung" => "https://www.samsung.com/support/",
                "microsoft" => "https://catalog.update.microsoft.com/",
                _ => "https://www.google.com/search?q=" + Uri.EscapeDataString($"{vendor} driver download")
            };
            Debug.WriteLine($"[DriverDetailsModal] GetVendorDownloadUrl - result='{result}'");
            App.LoggingService?.LogInfo($"[DriverDetailsModal] GetVendorDownloadUrl - result='{result}'");
            return result;
        }

        private void OpenSourceButton_Click(object sender, RoutedEventArgs e)
        {
            var sw = Stopwatch.StartNew();
            Debug.WriteLine($"[DriverDetailsModal] OpenSourceButton_Click - Enter url='{_sourceUrl}'");
            try {
                App.LoggingService?.LogInfo($"[DriverDetailsModal] OpenSourceButton clicado - URL: {_sourceUrl}");

                if (!string.IsNullOrEmpty(_sourceUrl)) {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                        FileName = _sourceUrl,
                        UseShellExecute = true
                    });
                    App.LoggingService?.LogInfo("[DriverDetailsModal] URL aberta com sucesso");
                }
            }
            catch (Exception ex) {
                Debug.WriteLine($"[DriverDetailsModal] OpenSourceButton_Click - Error: {ex.Message}");
                App.LoggingService?.LogError($"[DriverDetailsModal] Erro em OpenSourceButton_Click: {ex.Message}", ex);
            }
            sw.Stop();
            Debug.WriteLine($"[DriverDetailsModal] OpenSourceButton_Click - Exit duration={sw.ElapsedMilliseconds}ms");
        }

        private bool VersionCompare(string v1, string v2)
        {
            var sw = Stopwatch.StartNew();
            Debug.WriteLine($"[DriverDetailsModal] VersionCompare - Enter v1='{v1}' v2='{v2}'");
            if (string.IsNullOrEmpty(v1) || string.IsNullOrEmpty(v2))
            {
                sw.Stop();
                Debug.WriteLine($"[DriverDetailsModal] VersionCompare - Exit false (null/empty) duration={sw.ElapsedMilliseconds}ms");
                return false;
            }
            if (v1.Equals(v2, StringComparison.OrdinalIgnoreCase))
            {
                sw.Stop();
                Debug.WriteLine($"[DriverDetailsModal] VersionCompare - Exit true (exact match) duration={sw.ElapsedMilliseconds}ms");
                return true;
            }

            try {
                var s1 = string.Join(".", v1.Split('.').Select(p => p.TrimStart('0').PadLeft(1, '0')));
                var s2 = string.Join(".", v2.Split('.').Select(p => p.TrimStart('0').PadLeft(1, '0')));
                bool result = s1.Equals(s2, StringComparison.OrdinalIgnoreCase);
                sw.Stop();
                Debug.WriteLine($"[DriverDetailsModal] VersionCompare - Exit {result} (normalized: '{s1}' vs '{s2}') duration={sw.ElapsedMilliseconds}ms");
                return result;
            } catch {
                sw.Stop();
                Debug.WriteLine("[DriverDetailsModal] VersionCompare - Exit false (exception during normalization)");
                return false;
            }
        }

        private void CloseModal()
        {
            var sw = Stopwatch.StartNew();
            Debug.WriteLine("[DriverDetailsModal] CloseModal - Enter");
            try {
                App.LoggingService?.LogInfo("[DriverDetailsModal] Iniciando fechamento do modal...");

                if (Parent is Grid parentGrid) {
                    parentGrid.Children.Remove(this);
                    App.LoggingService?.LogInfo("[DriverDetailsModal] Modal removido da árvore visual");
                }
            }
            catch (Exception ex) {
                Debug.WriteLine($"[DriverDetailsModal] CloseModal - Error: {ex.Message}");
                App.LoggingService?.LogError($"[DriverDetailsModal] Erro ao fechar modal: {ex.Message}");
            }
            sw.Stop();
            Debug.WriteLine($"[DriverDetailsModal] CloseModal - Exit duration={sw.ElapsedMilliseconds}ms");
        }
    }
}
