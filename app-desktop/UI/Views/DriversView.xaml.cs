using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Drivers;
using VoltrisOptimizer.UI.ViewModels;

namespace VoltrisOptimizer.UI.Views
{
    /// <summary>
    /// Página de gerenciamento de drivers.
    ///
    /// Pipeline: DETECÇÃO → VERIFICAÇÃO DE VERSÃO → DOWNLOAD → VALIDAÇÃO →
    /// PREPARAÇÃO → PONTO DE RESTAURAÇÃO → INSTALAÇÃO → VALIDAÇÃO → ATUALIZAÇÃO DA UI.
    ///
    /// Correções estruturais aplicadas (ver relatório de auditoria):
    ///  - Ícones: a causa raiz era um P/Invoke "DestaroyIcon" inexistente que lançava
    ///    EntryPointNotFoundException dentro de um "catch { }", returning null sempre.
    ///    Agora usa DriverIconResolver (ícone nativo do shell + fallback vetorial por categoria).
    ///  - Botão de instalação: o estado passou a ser um enumerador (DriverInstallState).
    ///    O texto no code-behind ("Atualizao Disponvel") nunca casava com o gatilho do XAML
    ///    ("Atualização Disponível"), então o botão jamais aparecia.
    ///  - Download: não existe mais "MOCK_SUCCESS". O download é real, com progresso,
    ///    timeout, cancelamento, validação de hash, assinatura e publicador.
    ///  - Threading: enumeração, classificação e verificação online rodam fora da UI thread.
    ///    Antes, 150+ dispositivos eram processados na UI thread com P/Invoke e criação de
    ///    BitmapSource por item, o que explains os freezes de ~1s registrados no log.
    ///  - Logs: as dezenas de blocos "Task.Run que montava StringBuilder e não fazia nada"
    ///    e o log por dispositivo foram removidos; restam logs de operação com duração.
    /// </summary>
    public partial class DriversView : UserControl, IDisposable
    {
        private static readonly TimeSpan OnlineCheckTimeout = TimeSpan.FromSeconds(90);

        private readonly ObservableCollection<DriverItemViewModel> _devices = new();
        private readonly Dictionary<string, DriverItemViewModel> _devicesByInstanceId = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<DriverUpdate> _detectedUpdates = new();
        private readonly HashSet<string> _activeInstalls = new(StringComparer.OrdinalIgnoreCase);
        private readonly UniversalDriverDetectionService _updateService = new();
        private readonly SafeDriverInstaller _safeInstaller = new();
        private readonly SecureDriverDownloader _downloader = new();
        private readonly DriverSecurityService _securityService;

        private CancellationTokenSource? _scanCts;
        private CancellationTokenSource? _installCts;
        private bool _isScanning;
        private bool _disposed;

        public DriversView()
        {
            InitializeComponent();
            DriverIconResolver.EnsureInitialized();

            var systemToolsService = new SystemToolsService(App.LoggingService);
            _securityService = new DriverSecurityService(systemToolsService, App.LoggingService);

            App.LoggingService?.LogInfo("[DriversView] Página de drivers inicializada.");
        }

        // ==================================================================================
        // VARREDURA
        // ==================================================================================

        private async void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isScanning) return;

            _isScanning = true;
            _scanCts?.Cancel();
            _scanCts = new CancellationTokenSource();
            var token = _scanCts.Token;

            using var op = new DriverOperationScope("DRIVER_SCAN", "página de drivers");

            SetScanUiState(true);
            GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("DriverOpScanning"));
            UpdateGlobalStatus(5, "Verificando dispositivos", "Consultando a árvore de hardware PnP…");

            try
            {
                // Invalida o cache de fontes de rede (Windows Update). Sem isto, uma varredura
                // reaproveitaria o resultado da anterior — que é o comportamento desejado para
                // varreduras repetidas em sequência, mas não para uma varredura explícita.
                WindowsUpdateFallback.ResetCache();

                // ---------- ETAPA 1: enumeração real do SetupAPI (FORA da UI thread) ----------
                UpdateGlobalStatus(12, "Verificando dispositivos", "Consultando a árvore de hardware PnP…");
                op.Stage("ENUMERACAO", "SetupDiGetClassDevs + SetupDiEnumDeviceInfo");

                List<DeviceInfo>? devices = await Task.Run(() =>
                {
                    try
                    {
                        using var enumerator = new SetupApiEnumerator();
                        return enumerator.EnumerateDevices();
                    }
                    catch (Exception ex)
                    {
                        App.LoggingService?.LogError($"[DriversView] Falha ao enumerar dispositivos. {ex.Describe()}", ex);
                        return null;
                    }
                }, token).ConfigureAwait(true);

                if (devices == null || devices.Count == 0)
                {
                    op.Fail("nenhum dispositivo retornado pelo SetupAPI");
                    ShowEmptyState(LocalizationService.Instance.GetString("DriversNoPnpDevice"));
                    return;
                }

                token.ThrowIfCancellationRequested();
                op.Stage("ENUMERACAO", $"{devices.Count} dispositivos");

                UpdateGlobalStatus(32, "Processando dispositivos", $"Analisando {devices.Count} dispositivos…");

                // ---------- ETAPA 2: construção das linhas ----------
                // As linhas são criadas em lote na UI thread: os ícones vêm de cache
                // (≈25 ClassGuids e ≈15 categorias), não um P/Invoke por dispositivo.
                var rows = new List<DriverItemViewModel>(devices.Count);
                foreach (var device in devices)
                {
                    token.ThrowIfCancellationRequested();
                    rows.Add(DriverItemViewModel.FromDevice(device));
                }

                _devices.Clear();
                _devicesByInstanceId.Clear();
                foreach (var row in rows)
                {
                    _devices.Add(row);
                    if (!string.IsNullOrEmpty(row.HardwareId)) _devicesByInstanceId[row.HardwareId] = row;
                }

                foreach (var row in _devices) ApplyAuditNote(row);
                BindDeviceList();
                UpdateSummaryTiles();
                ShowEmptyState(null);
                op.Stage("UI", $"{_devices.Count} linhas exibidas | ícones: {DriverIconResolver.DescribeStats()}");

                // ---------- ETAPA 3: verificação de versões online ----------
                UpdateGlobalStatus(55, "Verificando atualizações", "Consultando fontes do fabricante…");
                op.Stage("VERIFICACAO", $"timeout={OnlineCheckTimeout.TotalSeconds:0}s");

                await CheckForUpdatesAsync(devices, token).ConfigureAwait(true);

                token.ThrowIfCancellationRequested();

                // ---------- ETAPA 4: resumo ----------
                UpdateGlobalStatus(100, "Concluído", $"{_devices.Count} dispositivos verificados");
                GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("DriverOpScanComplete"));

                int attention = _devices.Count(d => d.State == DriverInstallState.AttentionRequired);
                int updates = _devices.Count(d => d.State == DriverInstallState.UpdateAvailable);
                op.Succeed($"dispositivos={_devices.Count} | updates={updates} | atencao={attention} | icones={DriverIconResolver.DescribeStats()}");

                if (updates > 0) BtnUpdateAll.IsEnabled = true;
            }
            catch (OperationCanceledException)
            {
                op.Fail("cancelado pelo usuário");
                GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("ScanCancelledProgress"));
                ShowEmptyState(LocalizationService.Instance.GetString("ScanCancelledEmptyState"));
            }
            catch (Exception ex)
            {
                op.Fail($"erro inesperado: {ex.GetType().Name}", ex);
                GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("DriverOpScanFail"));
                UpdateGlobalStatus(100, "Erro na varredura", ex.Message);
                ShowEmptyState(string.Format(LocalizationService.Instance.GetString("DriversScanFailedFmt"), ex.Message));
            }
            finally
            {
                _isScanning = false;
                SetScanUiState(false);
                if (_devices.Count > 0) SpnlEmptyState.Visibility = Visibility.Collapsed;
            }
        }

        private async Task CheckForUpdatesAsync(List<DeviceInfo> devices, CancellationToken token)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(OnlineCheckTimeout);

            try
            {
                _detectedUpdates.Clear();
                _detectedUpdates.AddRange(await _updateService.CheckAllDriversAsync(devices).ConfigureAwait(true));
            }
            catch (OperationCanceledException)
            {
                App.LoggingService?.LogWarning("[DriversView] Verificação online cancelada por timeout. Exibindo apenas o estado local.");
                return;
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[DriversView] Falha na verificação online de drivers. {ex.Describe()}", ex);
                return;
            }

            int applied = 0, rejected = 0;

            foreach (var update in _detectedUpdates)
            {
                if (cts.IsCancellationRequested) break;
                if (update?.NewDriver == null) continue;

                if (!TryResolveRow(update, out var row))
                {
                    rejected++;
                    App.LoggingService?.LogWarning(
                        $"[DriversView] Update '{update.NewDriver.Title}' descartado: nenhum dispositivo correspondente na lista. " +
                        $"HWID='{update.Device?.HardwareIds}'");
                    continue;
                }

                // Bloqueios de segurança e de coerência: jamais presenting atualização
                // para um dispositivo ao qual ela não pertence.
                if (!IsUpdateCompatible(update, row, out string reason))
                {
                    rejected++;
                    App.LoggingService?.LogWarning(
                        $"[DriversView] Update '{update.NewDriver.Title}' REJEITADO para '{row.DeviceName}': {reason}");
                    continue;
                }

                row.DriverUpdate = update;
                row.State = DriverInstallState.UpdateAvailable;
                row.StatusText = string.IsNullOrWhiteSpace(update.NewDriver.Changelog)
                    ? $"Nova versão {update.NewDriver.Version} disponível."
                    : update.NewDriver.Changelog;
                applied++;
            }

            App.LoggingService?.LogInfo(
                $"[DriversView] Verificação online concluída: {applied} atualização(ões) aplicada(s) à UI, " +
                $"{rejected} descartada(s) por incompatibilidade ou ausência de correspondência.");

            // Cada linha passa a explicar por que tem ou não tem atualização.
            foreach (var row in _devices) ApplyAuditNote(row);

            UpdateSummaryTiles();
        }

        /// <summary>
        /// Associa um update ao dispositivo correto. A correspondência é feita pelo
        /// DeviceInstanceId (identificador canônico) e, apenas como segundo critério,
        /// por Hardware ID. Sem correspondência, o update é descartado — nunca adivinhado.
        /// </summary>
        private bool TryResolveRow(DriverUpdate update, out DriverItemViewModel row)
        {
            row = null!;

            string? instanceId = update.Device?.DeviceInstanceId;
            if (!string.IsNullOrWhiteSpace(instanceId) &&
                _devicesByInstanceId.TryGetValue(instanceId.Trim(), out var exact))
            {
                row = exact;
                return true;
            }

            var updateHwIds = update.Device?.HardwareIdList ?? Array.Empty<string>();
            foreach (var hwId in updateHwIds.Take(4))
            {
                if (string.IsNullOrWhiteSpace(hwId)) continue;
                var match = _devices.FirstOrDefault(d =>
                    !string.IsNullOrEmpty(d.HardwareIds) &&
                    d.HardwareIds!.Contains(hwId, StringComparison.OrdinalIgnoreCase));

                if (match != null)
                {
                    row = match;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Verificação de compatibilidade antes de oferecer a atualização. Impede o clássico
        /// erro de um gerenciador "atualizar tudo" que instala um driver de áudio em uma GPU.
        /// </summary>
        private static bool IsUpdateCompatible(DriverUpdate update, DriverItemViewModel row, out string reason)
        {
            reason = string.Empty;

            // A categoria do pacote (inferida do título) precisa ser compatível com o dispositivo.
            DriverCategory packageCategory = InferCategoryFromTitle(update.NewDriver.Title);
            if (packageCategory != DriverCategory.Unknown && packageCategory != row.CategoryKind)
            {
                // Wi-Fi e Rede compartilham o mesmo controller; tolerar essa troca.
                bool networkPair =
                    (packageCategory is DriverCategory.Wifi or DriverCategory.Network) &&
                    row.CategoryKind is DriverCategory.Wifi or DriverCategory.Network;
                if (!networkPair)
                {
                    reason = $"categoria do pacote ({DriverCategoryClassifier.GetDisplayName(packageCategory)}) " +
                             $"difere do dispositivo ({DriverCategoryClassifier.GetDisplayName(row.CategoryKind)})";
                    return false;
                }
            }

            // Versão precisa ser realmente superior à instalada.
            if (!string.IsNullOrWhiteSpace(update.NewDriver.Version) && row.Device?.ParsedDriverVersion != null)
            {
                if (Version.TryParse(update.NewDriver.Version, out var candidate) &&
                    candidate <= row.Device.ParsedDriverVersion)
                {
                    reason = $"a versão disponível ({candidate}) não é superior à instalada ({row.Device.ParsedDriverVersion})";
                    return false;
                }
            }

            // Hardware IDs do pacote precisam intersectar com os do dispositivo.
            if (update.NewDriver.HardwareId is { Length: > 2 } pkgHwId &&
                row.Device is { HardwareIdList.Count: > 0 } device)
            {
                bool anyMatch = device.HardwareIdList.Any(h =>
                    h.Contains(pkgHwId, StringComparison.OrdinalIgnoreCase) ||
                    pkgHwId.Contains(h, StringComparison.OrdinalIgnoreCase));
                if (!anyMatch)
                {
                    reason = $"o Hardware ID do pacote ('{pkgHwId}') não consta no dispositivo";
                    return false;
                }
            }

            // Hardware IDs ausentes no pacote: sem evidência de compatibilidade, não oferecer.
            if (string.IsNullOrWhiteSpace(update.NewDriver.HardwareId))
            {
                reason = "o pacote não declara Hardware ID — sem evidência de compatibilidade";
                return false;
            }

            return true;
        }

        private static DriverCategory InferCategoryFromTitle(string? title)
        {
            if (string.IsNullOrWhiteSpace(title)) return DriverCategory.Unknown;
            string t = title.ToLowerInvariant();

            if (t.Contains("bluetooth")) return DriverCategory.Bluetooth;
            if (t.Contains("wi-fi") || t.Contains("wifi") || t.Contains("wireless")) return DriverCategory.Wifi;
            if (t.Contains("ethernet") || t.Contains("network") || t.Contains("lan")) return DriverCategory.Network;
            if (t.Contains("audio") || t.Contains("sound") || t.Contains("media")) return DriverCategory.Audio;
            if (t.Contains("graphics") || t.Contains("display") || t.Contains("vga")) return DriverCategory.Gpu;
            if (t.Contains("chipset") || t.Contains("inf") && t.Contains("chipset")) return DriverCategory.Chipset;
            if (t.Contains("storage") || t.Contains("nvm") || t.Contains("sata") || t.Contains("raid")) return DriverCategory.Storage;
            if (t.Contains("usb")) return DriverCategory.Usb;
            if (t.Contains("touchpad") || t.Contains("keyboard") || t.Contains("mouse") || t.Contains("touchpad")) return DriverCategory.Input;
            if (t.Contains("monitor")) return DriverCategory.Monitor;
            if (t.Contains("printer")) return DriverCategory.Printer;
            if (t.Contains("webcam") || t.Contains("camera")) return DriverCategory.Camera;
            if (t.Contains("battery") || t.Contains("power")) return DriverCategory.Battery;
            return DriverCategory.Unknown;
        }

        // ==================================================================================
        // INSTALAÇÃO
        // ==================================================================================

        private async void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not DriverItemViewModel item) return;
            await InstallSingleAsync(item, skipRestorePoint: false, showRestartPrompt: true).ConfigureAwait(true);
        }

        private async void UpdateAllButton_Click(object sender, RoutedEventArgs e)
        {
            var queue = _devices
                .Where(d => d.State is DriverInstallState.UpdateAvailable or DriverInstallState.Failed)
                .ToList();

            if (queue.Count == 0)
            {
                App.LoggingService?.LogWarning("[DriversView] 'Atualizar tudo' acionado sem nenhum driver elegível.");
                return;
            }

            using var op = new DriverOperationScope("DRIVER_UPDATE_ALL", $"{queue.Count} driver(s)");

            BtnUpdateAll.IsEnabled = false;
            BtnScan.IsEnabled = false;
            SetOverlayVisible(true);
            GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("DriverOpUpdating"));

            int success = 0, failed = 0;

            // Um único ponto de restauração para o lote: criar um por driver é inviável
            // (o Windows aplica um throttle de frequência) e não agrega segurança.
            UpdateGlobalStatus(2, "Preparando proteção", "Criando ponto de restauração do sistema…");
            op.Stage("RESTORE_POINT", "ponto único para o lote");

            bool restorePointOk = await _securityService
                .CreatePreUpdateRestorePointAsync("Voltris - Atualização em lote de drivers", isBatchUpdate: true)
                .ConfigureAwait(true);

            if (!restorePointOk)
            {
                op.StageFailed("RESTORE_POINT", "não foi possível criar o ponto de restauração — lote cancelado por segurança", null);
                await _securityService.ShowRestorePointFailureMessageAsync("Voltris - Atualização em lote de drivers").ConfigureAwait(true);
                GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("DriversCancelledBySafety"));
                SetOverlayVisible(false);
                BtnScan.IsEnabled = true;
                BtnUpdateAll.IsEnabled = true;
                return;
            }

            for (int i = 0; i < queue.Count; i++)
            {
                var item = queue[i];
                UpdateGlobalStatus(10 + (int)(80.0 * i / queue.Count), "Instalando drivers", $"({i + 1}/{queue.Count}) {item.DeviceName}");

                bool ok = await InstallSingleAsync(item, skipRestorePoint: true, showRestartPrompt: false).ConfigureAwait(true);
                if (ok && item.State == DriverInstallState.Installed) success++;
                else failed++;
            }

            op.Succeed($"instalados={success} | falhas={failed}");

            GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("DriverOpUpdateComplete"));
            SetOverlayVisible(false);
            BtnScan.IsEnabled = true;
            UpdateSummaryTiles();

            if (success > 0)
            {
                ShowRestartModal(
                    "ATUALIZAÇÃO DE DRIVERS CONCLUÍDA",
                    $"{success} driver(s) atualizado(s) com sucesso" +
                    (failed > 0 ? $". {failed} falha(s)." : ".") +
                    " Para garantir a estabilidade total do sistema, recomendamos reiniciar o computador.");
            }
            else if (failed > 0)
            {
                ShowRestartModal(
                    "ATUALIZAÇÃO DE DRIVERS CONCLUÍDA",
                    "Nenhum driver foi instalado. Consulte o histórico de operações para o motivo de cada falha.");
            }
        }

        /// <summary>
        /// Executa o pipeline completo de um driver. Retorna true somente se o driver foi
        /// realmente instalado e validado.
        /// </summary>
        private async Task<bool> InstallSingleAsync(DriverItemViewModel item, bool skipRestorePoint, bool showRestartPrompt)
        {
            string key = item.HardwareId ?? item.DeviceName;

            // Bloqueia instalações simultâneas do MESMO driver (o requisito é explícito).
            if (!_activeInstalls.Add(key))
            {
                App.LoggingService?.LogWarning($"[DriversView] Operação já em andamento para '{item.DeviceName}'. Ação ignorada.");
                return false;
            }

            _installCts?.Cancel();
            _installCts = new CancellationTokenSource();
            var token = _installCts.Token;

            using var op = new DriverOperationScope("DRIVER_INSTALL", item.DeviceName);

            try
            {
                item.IsBusy = true;

                // ---------------- Manual: o usuário fornece o próprio INF ----------------
                if (item.State == DriverInstallState.ManualOnly || item.DriverUpdate == null)
                {
                    return await PerformManualInstallAsync(item, op, token).ConfigureAwait(true);
                }

                var update = item.DriverUpdate;
                var package = update.NewDriver;

                // ---------------- 1. PONTO DE RESTAURAÇÃO (infraestrutura existente) ----------------
                if (!skipRestorePoint)
                {
                    UpdateGlobalStatus(10, "Protegendo o sistema", "Criando ponto de restauração…");
                    op.Stage("RESTORE_POINT", $"descrição='Voltris - {item.DeviceName}'");

                    bool rpOk = await _securityService
                        .CreatePreUpdateRestorePointAsync(item.DeviceName, isBatchUpdate: false)
                        .ConfigureAwait(true);

                    if (!rpOk)
                    {
                        op.StageFailed("RESTORE_POINT", "ponto de restauração não criado — instalação cancelada por segurança", null);
                        item.State = DriverInstallState.Failed;
                        item.StatusText = LocalizationService.Instance.GetString("DriversCancelledNoRestorePoint");
                        await _securityService.ShowRestorePointFailureMessageAsync(item.DeviceName).ConfigureAwait(true);
                        return false;
                    }
                }

                // ---------------- 3. VALIDAÇÃO: a informação é VERIFICÁVEL? ----------------
                // Um pacote whose URL is a search page (not a driver artifact) has no
                // provenance strong enough to be installed automatically. O usuário é
                // direcionado ao canal oficial em vez de receber um download que falharia.
                if (!package.IsVerified)
                {
                    string why = package.Provenance == DriverUpdateProvenance.None
                        ? "não há fonte verificável para esta versão"
                        : $"a URL informada ({package.SourceUrl ?? package.DownloadUrl}) é uma página de consulta, não o pacote do driver";

                    op.StageFailed("PROVENIENCIA", why);
                    item.State = DriverInstallState.ManualOnly;
                    item.OperationText =
                        $"Versão {package.Version} citada por: {package.ProvenanceLabel}. " +
                        "A instalação automática exige o link direto do pacote — abra a página oficial do fabricante.";
                    item.SourceUrl = package.SourceUrl ?? package.DownloadUrl;
                    return false;
                }

                // ---------------- 4. DOWNLOAD com progresso e cancelamento ----------------
                item.State = DriverInstallState.Downloading;
                item.DownloadProgress = 0;
                item.OperationText = $"Baixando {package.Title}…";

                var progress = new Progress<(double Fraction, long Received, long Total)>(p =>
                {
                    item.DownloadProgress = p.Fraction;
                    long total = p.Total > 0 ? p.Total : 0;
                    item.OperationText = total > 0
                        ? $"Baixando… {FormatBytes(p.Received)} de {FormatBytes(total)}"
                        : $"Baixando… {FormatBytes(p.Received)}";
                });

                op.Stage("DOWNLOAD", $"origem={package.SourceUrl ?? package.DownloadUrl}");

                var download = await _downloader.DownloadDriverAsync(
                    url: package.DownloadUrl,
                    expectedSha256: package.Sha256,
                    version: package.Version,
                    vendor: package.Vendor,
                    expectedSizeBytes: package.FileSize,
                    progress: progress,
                    token: token).ConfigureAwait(true);

                if (!download.Success)
                {
                    op.StageFailed("DOWNLOAD", download.ErrorMessage, null);
                    item.State = DriverInstallState.Failed;
                    item.OperationText = download.ErrorMessage;
                    item.DownloadProgress = 0;
                    return false;
                }

                item.DownloadProgress = 1;
                op.Stage("DOWNLOAD_OK",
                    $"bytes={download.SizeBytes} | sha256={download.Sha256?[..Math.Min(16, download.Sha256.Length)]} | " +
                    $"origem={download.SourceHost} | tipo={download.Kind} | assinante={download.Publisher}");

                // ---------------- 3. VALIDAÇÃO DE COMPATIBILIDADE DO PACOTE REAL ----------------
                if (download.Kind == DriverPackageKind.VendorInstaller)
                {
                    // Instaladores .exe/.msi de fabricante não são executados automaticamente.
                    item.State = DriverInstallState.ManualOnly;
                    item.OperationText =
                        $"Pacote validado ({download.Publisher}). Execute o instalador oficial do fabricante para concluir.";
                    op.Stage("VALIDACAO", "instalador de fabricante — execução automática bloqueada por segurança");
                    return true;
                }

                if (download.InfFiles.Count == 0)
                {
                    op.StageFailed("VALIDACAO", "nenhum .INF no pacote baixado", null);
                    item.State = DriverInstallState.Failed;
                    item.OperationText = "O pacote baixado não contém descritores de driver (.INF).";
                    return false;
                }

                // ---------------- 4. INSTALAÇÃO (reutiliza SafeDriverInstaller) ----------------
                item.State = DriverInstallState.Installing;
                item.OperationText = "Instalando e validando o driver…";
                UpdateGlobalStatus(75, "Instalando driver", item.DeviceName);

                bool installed = await _safeInstaller
                    .InstallDriverSafelyAsync(update, download.ExtractedFolder ?? download.PackagePath!, download.InfFiles)
                    .ConfigureAwait(true);

                if (!installed)
                {
                    op.StageFailed("INSTALACAO", "o instalador seguro recusou ou falhou na instalação", null);
                    item.State = DriverInstallState.Failed;
                    item.OperationText = "A instalação falhou ou foi recusada por segurança. Consulte o log de operações.";
                    return false;
                }

                // ---------------- 5. ATUALIZAÇÃO DA INTERFACE ----------------
                item.State = DriverInstallState.Installed;
                item.OperationText = $"Driver {package.Version} instalado e validado.";
                item.DriverVersion = package.Version;
                item.DownloadProgress = 0;

                // Reavaliar o dispositivo real para refletir a versão agora instalada.
                await RefreshRowFromSystemAsync(item, token).ConfigureAwait(true);

                op.Succeed($"driver={package.Version} | reinicialização_recomendada=true");

                if (showRestartPrompt)
                {
                    ShowRestartModal(
                        "DRIVER ATUALIZADO COM SUCESSO",
                        $"O driver de '{item.DeviceName}' foi atualizado para a versão {package.Version}. " +
                        "Para que a mudança passe a valer, recomendamos reiniciar o computador.");
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                op.Fail("cancelado pelo usuário");
                item.State = DriverInstallState.Failed;
                item.OperationText = "Operação cancelada.";
                item.DownloadProgress = 0;
                return false;
            }
            catch (Exception ex)
            {
                op.Fail($"erro inesperado: {ex.GetType().Name}", ex);
                item.State = DriverInstallState.Failed;
                item.OperationText = $"Falha inesperada: {ex.Message}";
                item.DownloadProgress = 0;
                return false;
            }
            finally
            {
                item.IsBusy = false;
                _activeInstalls.Remove(key);
                if (token.IsCancellationRequested) _installCts?.Dispose();
                UpdateSummaryTiles();
            }
        }

        /// <summary>
        /// Reenumera o dispositivo para confirmar a versão realmente instalada após a operação.
        /// Sem isso a UI continuaria exibindo a versão antiga.
        /// </summary>
        private async Task RefreshRowFromSystemAsync(DriverItemViewModel item, CancellationToken token)
        {
            if (string.IsNullOrEmpty(item.HardwareId)) return;

            try
            {
                var refreshed = await Task.Run(() =>
                {
                    using var enumerator = new SetupApiEnumerator();
                    return enumerator.EnumerateDevices();
                }, token).ConfigureAwait(true);

                var match = refreshed.FirstOrDefault(d =>
                    string.Equals(d.DeviceInstanceId, item.HardwareId, StringComparison.OrdinalIgnoreCase));

                if (match != null && !string.IsNullOrWhiteSpace(match.DriverVersion))
                {
                    item.DriverVersion = match.DriverVersion;
                    App.LoggingService?.LogInfo(
                        $"[DriversView] Versão confirmada pelo sistema para '{item.DeviceName}': {match.DriverVersion}");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning(
                    $"[DriversView] Não foi possível reconfirmar a versão de '{item.DeviceName}': {ex.Message}");
            }
        }

        /// <summary>
        /// Instalação manual a partir de um INF escolhido pelo usuário.
        /// O arquivo passa pelas MESMAS validações do fluxo automático: assinatura,
        /// publicador e Hardware ID. Um INF desconhecido ou não assinado é recusado.
        /// </summary>
        private async Task<bool> PerformManualInstallAsync(DriverItemViewModel item, DriverOperationScope op, CancellationToken token)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Driver INF (*.inf)|*.inf",
                Title = "Selecione o pacote de driver (.inf)"
            };

            if (dialog.ShowDialog() != true)
            {
                op.Stage("CANCELADO", "o usuário não selecionou nenhum arquivo");
                item.OperationText = "Seleção de arquivo cancelada.";
                return false;
            }

            string infPath = dialog.FileName;
            op.Stage("SELECAO", $"arquivo={Path.GetFileName(infPath)}");

            // --- Validação de assinatura do INF (o .inf em si normalmente não é assinado;
            //     quem é assinado é o .cat do pacote. Validamos ambos quando presentes.)
            string? catPath = Path.ChangeExtension(infPath, ".cat");
            string target = File.Exists(catPath) ? catPath : infPath;

            var signature = new DriverSignatureVerifier().VerifyWithPublisher(target);
            if (!signature.IsSigned)
            {
                op.StageFailed("ASSINATURA", $"arquivo manual rejeitado: {signature.StatusDescription}", null);
                item.State = DriverInstallState.Failed;
                item.OperationText = $"Arquivo rejeitado: {signature.StatusDescription}";
                return false;
            }

            if (!signature.IsTrustedPublisher)
            {
                op.StageFailed("PUBLICADOR", $"publicador não reconhecido: {signature.Publisher}", null);
                item.State = DriverInstallState.Failed;
                item.OperationText = $"Publicador não reconhecido: {signature.Publisher ?? "(desconhecido)"}";
                return false;
            }

            // --- Validação de Hardware ID contra o dispositivo alvo ---
            if (!InfSupportsHardware(infPath, item))
            {
                op.StageFailed("HARDWARE_ID", $"o INF não declara suporte a '{item.HardwareId}'", null);
                item.State = DriverInstallState.Failed;
                item.OperationText = "O INF selecionado não declara suporte a este dispositivo.";
                return false;
            }

            // --- Ponto de restauração antes de alterar o sistema ---
            UpdateGlobalStatus(10, "Protegendo o sistema", "Criando ponto de restauração…");
            bool rpOk = await _securityService
                .CreatePreUpdateRestorePointAsync(item.DeviceName, isBatchUpdate: false)
                .ConfigureAwait(true);

            if (!rpOk)
            {
                op.StageFailed("RESTORE_POINT", "ponto de restauração não criado — cancelado por segurança", null);
                item.State = DriverInstallState.Failed;
                item.OperationText = "Cancelado por segurança: não foi possível criar o ponto de restauração.";
                await _securityService.ShowRestorePointFailureMessageAsync(item.DeviceName).ConfigureAwait(true);
                return false;
            }

            item.State = DriverInstallState.Installing;
            item.OperationText = "Instalando o INF selecionado…";

            int exitCode = await RunPnPUtilAsync($"/add-driver \"{infPath}\" /install", token).ConfigureAwait(true);
            op.Stage("PNPUTIL", $"exitCode={exitCode}");

            if (exitCode != 0)
            {
                op.StageFailed("PNPUTIL", $"pnputil retornou {exitCode}", null);
                item.State = DriverInstallState.Failed;
                item.OperationText = $"O Windows recusou a instalação (código {exitCode}).";
                return false;
            }

            item.State = DriverInstallState.Installed;
            item.OperationText = "Driver instalado manualmente com sucesso.";
            op.Succeed($"inf={Path.GetFileName(infPath)} | exitCode={exitCode}");

            await RefreshRowFromSystemAsync(item, token).ConfigureAwait(true);
            return true;
        }

        /// <summary>
        /// Executa o pnputil com elevação correta e sem risco de deadlock de pipe.
        /// Retorna o ExitCode, ou um valor negativo se a execução não foi possível.
        /// </summary>
        private static async Task<int> RunPnPUtilAsync(string arguments, CancellationToken token)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "pnputil.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            try
            {
                using var proc = new Process { StartInfo = psi };
                if (!proc.Start()) return -1;

                // Ler os dois fluxos em paralelo: ler um await por vez pode travar
                // indefinidamente se o outro pipe encher.
                var stdout = proc.StandardOutput.ReadToEndAsync();
                var stderr = proc.StandardError.ReadToEndAsync();

                using (token.Register(() => { try { if (!proc.HasExited) proc.Kill(true); } catch { } }))
                {
                    await proc.WaitForExitAsync().ConfigureAwait(false);
                }

                string outText = await stdout.ConfigureAwait(false);
                string errText = await stderr.ConfigureAwait(false);

                App.LoggingService?.LogInfo(
                    $"[DriversView] pnputil {arguments} -> exitCode={proc.ExitCode} | out={outText.Trim()} | err={errText.Trim()}");

                return proc.ExitCode;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[DriversView] Falha ao executar pnputil. {ex.Describe()}", ex);
                return -1;
            }
        }

        /// <summary>
        /// Confere se o INF declara o Hardware ID do dispositivo. Reutiliza a mesma rotina
        /// (e a mesma correção de BOM UTF-16) do instalador automático.
        /// </summary>
        private static bool InfSupportsHardware(string infPath, DriverItemViewModel item)
        {
            try
            {
                string content = SafeDriverInstaller.ReadInfText(infPath);
                if (string.IsNullOrEmpty(content)) return false;

                var deviceIds = item.Device?.HardwareIdList ?? Array.Empty<string>();
                if (deviceIds.Count == 0) return true; // sem HWID conhecido: não é possível recusar

                foreach (var id in deviceIds)
                {
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    string clean = NormalizeHardwareIdForInf(id);
                    if (clean.Length > 3 && content.Contains(clean, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[DriversView] Falha ao ler o INF '{infPath}'. {ex.Describe()}", ex);
                return false;
            }
        }

        private static string NormalizeHardwareIdForInf(string hardwareId)
        {
            var parts = hardwareId.Split('&');
            string joined = string.Join("&", parts.Take(2));
            int mi = joined.IndexOf("&MI_", StringComparison.OrdinalIgnoreCase);
            return mi > 0 ? joined[..mi] : joined;
        }

        // ==================================================================================
        // UI
        // ==================================================================================

        private void BindDeviceList()
        {
            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_devices);
            if (!view.GroupDescriptions.Any())
                view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(DriverItemViewModel.Category)));
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new System.ComponentModel.SortDescription(
                nameof(DriverItemViewModel.Category), System.ComponentModel.ListSortDirection.Ascending));
            IcDevices.ItemsSource = view;
        }

        private void UpdateSummaryTiles()
        {
            int total = _devices.Count;
            int updates = _devices.Count(d => d.State is DriverInstallState.UpdateAvailable or DriverInstallState.Failed);
            int attention = _devices.Count(d => d.State == DriverInstallState.AttentionRequired);
            int problems = _devices.Count(d => d.Device?.IsProblem == true);

            TxtTotalDevices.Text = total.ToString();
            TxtUpdatesFound.Text = updates.ToString();

            if (attention > 0)
            {
                TxtHealthStatus.Text = attention.ToString();
                TxtHealthStatus.Foreground = BrushFromHex("#FF3D3D");
            }
            else if (problems > 0)
            {
                TxtHealthStatus.Text = problems.ToString();
                TxtHealthStatus.Foreground = BrushFromHex("#FFB020");
            }
            else
            {
                TxtHealthStatus.Text = "OK";
                TxtHealthStatus.Foreground = BrushFromHex("#00E58F");
            }

            BtnUpdateAll.IsEnabled = updates > 0 && !_isScanning;
        }

        private void SetScanUiState(bool scanning)
        {
            BtnScan.IsEnabled = !scanning;
            BtnScan.Content = scanning
                ? LocalizationService.Instance.GetString("Loc_Processing")
                : (_devices.Count > 0 ? LocalizationService.Instance.GetString("Loc_StartScan") : LocalizationService.Instance.GetString("Loc_StartScan"));

            if (scanning) SetOverlayVisible(true);
            else SetOverlayVisible(false);
        }

        private void SetOverlayVisible(bool visible)
        {
            SpnlLoading.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowEmptyState(string? message)
        {
            if (string.IsNullOrEmpty(message))
            {
                SpnlEmptyState.Visibility = _devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                return;
            }
            SpnlEmptyState.Visibility = Visibility.Visible;
            TxtEmptyMessage.Text = message;
        }

        private void UpdateGlobalStatus(double progress, string title, string status)
        {
            void Apply()
            {
                if (TxtLoadingTitle != null) TxtLoadingTitle.Text = title;
                GlobalProgressService.Instance.UpdateProgress((int)Math.Clamp(progress, 0, 100), status);
            }

            if (Dispatcher.CheckAccess()) Apply();
            else Dispatcher.InvokeAsync(Apply, DispatcherPriority.Background);
        }

        private async void DetailsButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not DriverItemViewModel item) return;

            button.IsEnabled = false;
            try
            {
                // O DriverDetailsModal é um UserControl que se anexa sozinho ao MainWindow.
                var modal = new DriverDetailsModal();
                var update = item.DriverUpdate ?? BuildInformationalUpdate(item);
                modal.ShowModal(update);
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[DriversView] Falha ao abrir os detalhes de '{item.DeviceName}'. {ex.Describe()}", ex);
                System.Windows.MessageBox.Show(
                    $"Não foi possível abrir os detalhes do dispositivo.\n\n{ex.Message}",
                    LocalizationService.Instance.GetString("Error"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                button.IsEnabled = true;
            }
        }

        private static DriverUpdate BuildInformationalUpdate(DriverItemViewModel item) => new()
        {
            Device = item.Device ?? new DeviceInfo { FriendlyName = item.DeviceName },
            CurrentVersion = item.DriverVersion,
            NewDriver = new DriverPackage
            {
                Title = item.DeviceName,
                Version = item.DriverVersion,
                ProductName = item.DeviceName,
                Vendor = item.Vendor,
                SourceUrl = item.SourceUrl
            }
        };

        private void ShowRestartModal(string title, string description)
        {
            try
            {
                var owner = Window.GetWindow(this);
                var modal = new VoltrisOptimizer.UI.Windows.RestartConfirmationModal
                {
                    Owner = owner ?? Application.Current.MainWindow
                };
                modal.SetCustomMessage(title, description);
                modal.ShowDialog();

                if (modal.UserChoice == VoltrisOptimizer.UI.Windows.RestartConfirmationModal.RestartChoice.RestartNow)
                {
                    App.LoggingService?.LogWarning("[DriversView] Usuário escolheu reiniciar após a atualização de drivers.");
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "shutdown",
                        Arguments = "/r /t 5 /c \"Voltris Optimizer: drivers atualizados, reiniciando em 5 segundos...\"",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[DriversView] Falha ao exibir o modal de reinicialização. {ex.Describe()}", ex);
            }
        }

        /// <summary>
        /// Por que este dispositivo tem ou não tem atualização.
        ///
        /// Transparentemente explica o resultado, distinguindo três situações que antes eram
        /// todas apresentadas como "atualização disponível":
        ///   1) atualização verificada, com artefato para baixar;
        ///   2) informação citada por fonte secundária (site do fabricante), sem artefato direto;
        ///   3) nenhuma fonte respondeu — e o motivo (catálogo vazio, HTTP 403, sem API pública).
        /// </summary>
        private static void ApplyAuditNote(DriverItemViewModel row)
        {
            var catalog = DriverCatalog.Instance;

            row.AuditNote = row.DriverUpdate switch
            {
                null when catalog.Count == 0 =>
                    "Nenhuma fonte respondeu. O catálogo local está vazio e o site do fabricante " +
                    "bloqueia consulta automática (HTTP 403). Use o Windows Update ou o site oficial.",

                null =>
                    "Nenhuma fonte respondeu para este dispositivo. Este é o estado real — " +
                    "nenhuma versão é inventada para preencher a lista.",

                { NewDriver.IsVerified: true } pkg =>
                    $"Atualização verificada: {pkg.NewDriver.Version} via {pkg.NewDriver.ProvenanceLabel}. " +
                    "O pacote será validado por hash e assinatura antes de instalar.",

                var pkg =>
                    $"{pkg.NewDriver.Version} citado por {pkg.NewDriver.ProvenanceLabel}, mas sem link direto " +
                    "para o pacote. Apenas informativo; a instalação automática fica desabilitada."
            };
        }

        private static Brush BrushFromHex(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] units = { "B", "KB", "MB", "GB" };
            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            return $"{value:0.#} {units[unit]}";
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _scanCts?.Cancel(); } catch { }
            try { _installCts?.Cancel(); } catch { }
            try { _downloader.Dispose(); } catch { }
        }
    }
}
