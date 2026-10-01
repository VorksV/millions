using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Utils;

namespace VoltrisOptimizer.UI.ViewModels
{
    public class SecurityViewModel : INotifyPropertyChanged
    {
        private readonly ISecurityTuningService _securityService;
        private readonly ILoggingService _logger;
        private readonly LocalizationService _loc;
        private readonly WindowsUpdateService _updateService = new();
           private CancellationTokenSource _pollingCts = new();
           private bool _initialized;


        private SecurityStatus? _status;
        public SecurityStatus? Status
        {
            get => _status;
            set
            {
                if (_status == value) return;
                _status = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasStatus));
                OnPropertyChanged(nameof(IsStatusLoading));
            }
        }

        public bool HasStatus => _status != null;

        private bool _isSmartScreenEnabled;
        public bool IsSmartScreenEnabled
        {
            get => _isSmartScreenEnabled;
            set { if (_isSmartScreenEnabled != value) { _isSmartScreenEnabled = value; OnPropertyChanged(); _ = ApplyTweak("SmartScreen", value); } }
        }

        private bool _isVbsEnabled;
        public bool IsVbsEnabled
        {
            get => _isVbsEnabled;
            set { if (_isVbsEnabled != value) { _isVbsEnabled = value; OnPropertyChanged(); _ = ApplyTweak("VBS", value); } }
        }

        // ── Windows Update ────────────────────────────────────────────────────
        private ObservableCollection<WindowsUpdateInfo> _pendingUpdates = new();
        public ObservableCollection<WindowsUpdateInfo> PendingUpdates
        {
            get => _pendingUpdates;
            set { _pendingUpdates = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasPendingUpdates)); OnPropertyChanged(nameof(UpdateCountText)); }
        }

        private bool _isCheckingUpdates;
        public bool IsCheckingUpdates
        {
            get => _isCheckingUpdates;
            set { _isCheckingUpdates = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanCheckUpdates)); }
        }

        private bool _isInstallingUpdates;
        public bool IsInstallingUpdates
        {
            get => _isInstallingUpdates;
            set { _isInstallingUpdates = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanCheckUpdates)); }
        }

        private string _updateStatusMessage = string.Empty;
        public string UpdateStatusMessage
        {
            get => _updateStatusMessage;
            set { _updateStatusMessage = value; OnPropertyChanged(); }
        }

        private bool _updateCheckDone;
        public bool UpdateCheckDone
        {
            get => _updateCheckDone;
            set { _updateCheckDone = value; OnPropertyChanged(); }
        }

        private bool _rebootRequired;
        public bool RebootRequired
        {
            get => _rebootRequired;
            set { _rebootRequired = value; OnPropertyChanged(); }
        }

        public bool HasPendingUpdates => PendingUpdates.Count > 0;
        public bool CanCheckUpdates => !IsCheckingUpdates && !IsInstallingUpdates;

        public string UpdateCountText => PendingUpdates.Count switch
        {
            0 => _loc["WUSystemUpToDate"],
            1 => _loc["WUOnePending"],
            _ => string.Format(_loc["WUManyPending"], PendingUpdates.Count)
        };

        public ICommand RefreshCommand { get; }
        public ICommand ScanCommand { get; }
        public ICommand UpdateCommand { get; }
        public ICommand CheckWindowsUpdatesCommand { get; }
        public ICommand InstallWindowsUpdatesCommand { get; }
        public ICommand FixTamperProtectionCommand { get; }
        public ICommand FixUacCommand { get; }
        public ICommand OpenAntivirusCommand { get; }
        public ICommand OpenRealtimeProtectionCommand { get; }
        public ICommand OpenFirewallCommand { get; }
        public ICommand OpenWindowsUpdateCommand { get; }
        public ICommand OpenSmartScreenCommand { get; }
        public ICommand OpenTamperProtectionCommand { get; }
        public ICommand OpenUacCommand { get; }

        public SecurityViewModel(ISecurityTuningService securityService, ILoggingService logger)
        {
            _securityService = securityService;
            _logger = logger;
            _loc = LocalizationService.Instance;
            var hash = GetHashCode();
            _logger.Log(LogLevel.Debug, LogCategory.Security, $"[SecurityVM:{hash}] Construtor chamado (Singleton check)");

            RefreshCommand = new AsyncRelayCommand(async _ =>
            {
                _securityService.InvalidateCache();
                await LoadStatusAsync(forceRefresh: true);
            });
            ScanCommand = new AsyncRelayCommand(async _ => await RunScanAsync());
            UpdateCommand = new AsyncRelayCommand(async _ => await RunUpdateAsync());
            CheckWindowsUpdatesCommand = new AsyncRelayCommand(async _ => await CheckWindowsUpdatesAsync(), _ => CanCheckUpdates);
            InstallWindowsUpdatesCommand = new AsyncRelayCommand(async _ => await InstallWindowsUpdatesAsync(), _ => CanCheckUpdates && HasPendingUpdates);
            FixTamperProtectionCommand = new RelayCommand(_ => OpenWindowsSecurityPage("windowsdefender://threatsettings/"));
            FixUacCommand = new RelayCommand(_ => OpenWindowsSecurityPage("ms-settings:useraccounts"));
            OpenAntivirusCommand = new RelayCommand(_ => OpenWindowsSecurityPage("windowsdefender://threat/"));
            OpenRealtimeProtectionCommand = new RelayCommand(_ => OpenWindowsSecurityPage("windowsdefender://threatsettings/"));
            OpenFirewallCommand = new RelayCommand(_ => OpenWindowsSecurityPage("windowsdefender://firewall"));
            OpenWindowsUpdateCommand = new RelayCommand(_ => OpenWindowsSecurityPage("ms-settings:windowsupdate"));
            OpenSmartScreenCommand = new RelayCommand(_ => OpenWindowsSecurityPage("windowsdefender://appbrowser/"));
            OpenTamperProtectionCommand = new RelayCommand(_ => OpenWindowsSecurityPage("windowsdefender://threatsettings/"));
            OpenUacCommand = new RelayCommand(_ => LaunchUacSettings());
        }

        /// <summary>
        /// Chamado pela View no evento Loaded. Garante que o status seja carregado
        /// sem bloquear a UI e sem mostrar "Inativo" padrão durante o carregamento.
        /// </summary>
        public async Task EnsureInitializedAsync()
        {
            if (_initialized) return;
            _initialized = true;

            _logger.Log(LogLevel.Info, LogCategory.Security, "EnsureInitializedAsync: iniciando carga de status");
            await LoadStatusAsync();
        }

        private async Task RunScanAsync()
        {
            var loc = LocalizationService.Instance;
            bool started = VoltrisOptimizer.Services.GlobalProgressService.Instance.StartOperation(loc.GetString("SecurityVerification"), true);
            if (started) VoltrisOptimizer.Services.GlobalProgressService.Instance.UpdateProgress(30, loc.GetString("SecurityQuickScanStarting"));
            await _securityService.RunQuickScanAsync();
            if (started) VoltrisOptimizer.Services.GlobalProgressService.Instance.CompleteOperation(loc.GetString("SecurityQuickScanComplete"));
            GlobalNotificationService.ShowSuccess(loc.GetString("SecurityCategory"), loc.GetString("SecurityQuickScanCompleted"));
            await LoadStatusAsync(forceRefresh: true);
        }

        private async Task RunUpdateAsync()
        {
            var loc = LocalizationService.Instance;
            bool started = VoltrisOptimizer.Services.GlobalProgressService.Instance.StartOperation(loc.GetString("SecurityUpdateTitle"), true);
            if (started) VoltrisOptimizer.Services.GlobalProgressService.Instance.UpdateProgress(20, loc.GetString("SecurityConnectingMs"));
            if (started) VoltrisOptimizer.Services.GlobalProgressService.Instance.UpdateProgress(50, loc.GetString("SecurityDownloadingDefs"));
            await _securityService.UpdateSignaturesAsync();
            if (started) VoltrisOptimizer.Services.GlobalProgressService.Instance.CompleteOperation(loc.GetString("SecurityUpdateComplete"));
            GlobalNotificationService.ShowSuccess(loc.GetString("SecurityCategory"), loc.GetString("SecurityVirusDefsUpdated"));
            await LoadStatusAsync(forceRefresh: true);
        }

        public async Task CheckWindowsUpdatesAsync()
        {
            if (!CanCheckUpdates) return;
            IsCheckingUpdates = true;
            UpdateCheckDone = false;
            UpdateStatusMessage = _loc["WUChecking"];
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                PendingUpdates.Clear();
                OnPropertyChanged(nameof(HasPendingUpdates));
                OnPropertyChanged(nameof(UpdateCountText));
            });

            bool started = VoltrisOptimizer.Services.GlobalProgressService.Instance.StartOperation(_loc.GetString("CheckWindowsUpdate"), true);
            if (started) VoltrisOptimizer.Services.GlobalProgressService.Instance.UpdateProgress(20, _loc.GetString("ConnectingWU"));

            try
            {
                if (started) VoltrisOptimizer.Services.GlobalProgressService.Instance.UpdateProgress(50, _loc.GetString("SearchingPendingUpdates"));
                var result = await _updateService.CheckForUpdatesAsync();
                if (started) VoltrisOptimizer.Services.GlobalProgressService.Instance.UpdateProgress(70, _loc.GetString("WUDownloading"));
                if (started) VoltrisOptimizer.Services.GlobalProgressService.Instance.UpdateProgress(80, _loc.GetString("WUPreparing"));
                if (started) VoltrisOptimizer.Services.GlobalProgressService.Instance.UpdateProgress(90, _loc.GetString("ProcessingResults"));

                if (result.Success)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        foreach (var u in result.PendingUpdates)
                            PendingUpdates.Add(u);

                        UpdateStatusMessage = result.IsUpToDate
                            ? _loc["WUCheckCompleteUpToDate"]
                            : string.Format(_loc["WUCheckPendingFound"], PendingUpdates.Count);

                        UpdateCheckDone = true;
                        OnPropertyChanged(nameof(HasPendingUpdates));
                        OnPropertyChanged(nameof(UpdateCountText));
                        OnPropertyChanged(nameof(CanCheckUpdates));
                    });
                }
                else
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        UpdateStatusMessage = string.Format(_loc["WUErrorCheck"], result.ErrorMessage);
                        UpdateCheckDone = true;
                        OnPropertyChanged(nameof(CanCheckUpdates));
                    });
                }
                
                if (started) VoltrisOptimizer.Services.GlobalProgressService.Instance.CompleteOperation(result.IsUpToDate
                    ? _loc["WUSystemUpToDate"]
                    : string.Format(_loc["WUManyPending"], PendingUpdates.Count));
                
                if (result.IsUpToDate)
                    GlobalNotificationService.ShowSuccess(LocalizationService.Instance.GetString("NotifyWindowsUpdate"), _loc["WUCheckCompleteUpToDate"]);
                else
                    GlobalNotificationService.ShowInfo(LocalizationService.Instance.GetString("NotifyWindowsUpdate"), string.Format(_loc["WUCheckPendingFound"], PendingUpdates.Count));
            }
            catch (Exception ex)
            {
                _logger.LogError($"[SecurityVM] Erro ao verificar Windows Update: {ex.Message}");
                UpdateStatusMessage = string.Format(_loc["WUUnknownError"], ex.Message);
                UpdateCheckDone = true;
                if (started) VoltrisOptimizer.Services.GlobalProgressService.Instance.CompleteOperation(string.Format(_loc["WUErrorCheck"], ""));
                GlobalNotificationService.ShowError(LocalizationService.Instance.GetString("NotifyWindowsUpdate"), string.Format(_loc["WUErrorCheck"], ex.Message));
            }
            finally
            {
                IsCheckingUpdates = false;
                OnPropertyChanged(nameof(CanCheckUpdates));
            }
        }

        public async Task InstallWindowsUpdatesAsync()
        {
            if (!CanCheckUpdates || !HasPendingUpdates) return;
            IsInstallingUpdates = true;
            UpdateStatusMessage = _loc["WUInstalling"];
            OnPropertyChanged(nameof(CanCheckUpdates));

            bool startedInstall = VoltrisOptimizer.Services.GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("InstallWindowsUpdateOpName"), true);
            if (startedInstall) VoltrisOptimizer.Services.GlobalProgressService.Instance.UpdateProgress(3, _loc.GetString("WUPreparing"));

            try
            {
                var progress = new Progress<(int percent, string message)>(p =>
                {
                    if (startedInstall) VoltrisOptimizer.Services.GlobalProgressService.Instance.UpdateProgress(p.percent, p.message);
                    System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                        UpdateStatusMessage = p.message);
                });

                var (success, reboot, message) = await _updateService.InstallUpdatesAsync(progress);

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    UpdateStatusMessage = message;
                    RebootRequired = reboot;
                    if (success) PendingUpdates.Clear();
                    OnPropertyChanged(nameof(HasPendingUpdates));
                    OnPropertyChanged(nameof(UpdateCountText));
                });

                if (startedInstall) VoltrisOptimizer.Services.GlobalProgressService.Instance.CompleteOperation(message);
                GlobalNotificationService.ShowSuccess(LocalizationService.Instance.GetString("NotifyWindowsUpdate"), message);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[SecurityVM] Erro ao instalar Windows Update: {ex.Message}");
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    UpdateStatusMessage = string.Format(_loc["WUUnknownError"], ex.Message));
                if (startedInstall) VoltrisOptimizer.Services.GlobalProgressService.Instance.CompleteOperation(_loc["WUErrorInstall"]);
                GlobalNotificationService.ShowError(LocalizationService.Instance.GetString("NotifyWindowsUpdate"), string.Format(_loc["WUUnknownError"], ex.Message));
            }
            finally
            {
                IsInstallingUpdates = false;
                OnPropertyChanged(nameof(CanCheckUpdates));
            }
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (_isLoading == value) return;
                _isLoading = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsStatusLoading));
            }
        }

        public bool IsStatusLoading => _isLoading || _status == null;

        private async Task LoadStatusAsync(bool forceRefresh = false)
        {
            try
            {
                var instanceTag = $"[SecurityVM:{GetHashCode()}]";
                _logger.Log(LogLevel.Info, LogCategory.Security, $"▶ {instanceTag} LoadStatusAsync iniciando... forceRefresh={forceRefresh}");
                
                if (forceRefresh)
                    _securityService.InvalidateCache();

                IsLoading = true;
                var swTotal = Stopwatch.StartNew();

                // Fallback de segurança: se após 12s o status ainda não carregou, força exibição
                var forceDisplayCts = new CancellationTokenSource();
                var forceDisplayTask = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(12_000, forceDisplayCts.Token);
                        if (_status == null && _isLoading)
                        {
                            _logger.LogWarning($"{instanceTag} ⏱ Fallback de 12s acionado — forçando exibição dos cards com status padrão");
                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                if (_status == null)
                                    Status = new SecurityStatus
                                    {
                                        AntivirusEnabled = true,
                                        AntivirusProduct = "Windows Defender",
                                        FirewallEnabled = true,
                                        WindowsUpdateEnabled = true,
                                        SmartScreenEnabled = true,
                                        RealTimeProtectionEnabled = true,
                                        UacEnabled = true,
                                        TamperProtectionEnabled = true,
                                        DefenderServiceEnabled = true
                                    };
                                if (_isLoading) IsLoading = false;
                            });
                        }
                    }
                    catch (TaskCanceledException) { }
                });

                // Progress callback: recebe status parcial (Fase 1) antes da Fase 2 terminar
                var progress = new Progress<SecurityStatus>(partial =>
                {
                    var sw = Stopwatch.StartNew();
                    _logger.Log(LogLevel.Debug, LogCategory.Security, $"{instanceTag} Progress callback recebendo status parcial...");

                    if (partial != _status)
                    {
                        Status = partial;
                        _isSmartScreenEnabled = partial.SmartScreenEnabled;
                        OnPropertyChanged(nameof(IsSmartScreenEnabled));
                        OnPropertyChanged(nameof(IsVbsEnabled));
                    }

                    if (_isLoading)
                    {
                        IsLoading = false;
                        _logger.Log(LogLevel.Debug, LogCategory.Security, $"{instanceTag} IsLoading=false no 1º progress — cards liberados com dados parciais");
                    }

                    sw.Stop();
                    _logger.Log(LogLevel.Debug, LogCategory.Security, $"{instanceTag} Progress callback processado em {sw.ElapsedMilliseconds}ms");
                });

                var s = await _securityService.GetSecurityStatusAsync(progress);
                
                if (s != _status)
                {
                    Status = s;
                    _isSmartScreenEnabled = s.SmartScreenEnabled;
                    OnPropertyChanged(nameof(IsSmartScreenEnabled));
                    OnPropertyChanged(nameof(IsVbsEnabled));
                }

                IsLoading = false;
                forceDisplayCts.Cancel();

                swTotal.Stop();
                _logger.Log(LogLevel.Success, LogCategory.Security, $"{instanceTag} Status carregado na UI em {swTotal.ElapsedMilliseconds}ms");
                _logger.Log(LogLevel.Info, LogCategory.Security, $"{instanceTag}   TamperProtection={s.TamperProtectionEnabled}, RTP={s.RealTimeProtectionEnabled}, UAC={s.UacEnabled}");
                _logger.Log(LogLevel.Info, LogCategory.Security, $"{instanceTag}   AV={s.AntivirusProduct} (enabled={s.AntivirusEnabled}), Firewall={s.FirewallEnabled}");
                _logger.Log(LogLevel.Info, LogCategory.Security, $"{instanceTag}   WinUpdate={s.WindowsUpdateEnabled}, SmartScreen={s.SmartScreenEnabled}");
                _logger.Log(LogLevel.Info, LogCategory.Security, $"{instanceTag}   BitLocker={s.BitLockerEnabled}, CFA={s.ControlledFolderAccessEnabled}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[SecurityVM:{GetHashCode()}] Erro ao carregar status de segurança: {ex.Message}", ex);
                if (_isLoading) IsLoading = false;
                if (_status == null)
                {
                    try { await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => Status = new SecurityStatus { AntivirusProduct = "Windows Defender" }); } catch { }
                }
            }
        }

        private async Task ApplyTweak(string tag, bool enable)
        {
            await _securityService.ApplyTweakAsync(tag, enable);
            // Pequeno delay para o sistema registrar a mudança antes de re-ler
            await Task.Delay(150); // Otimizado: 300→150ms para resposta mais rápida
            await LoadStatusAsync(forceRefresh: true);
        }

        private void OpenWindowsSecurityPage(string uri)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = uri,
                    UseShellExecute = true
                });

                // Polling automático: re-lê o status a cada 3s por até 60s
                // para refletir em tempo real quando o usuário ativar algo lá fora
                _ = PollStatusAfterExternalChangeAsync();
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, LogCategory.Security, $"Erro ao abrir página '{uri}': {ex.Message}");
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "windowsdefender://",
                        UseShellExecute = true
                    });
                    _ = PollStatusAfterExternalChangeAsync();
                }
                catch (Exception fallbackEx)
                {
                    _logger.Log(LogLevel.Error, LogCategory.Security, $"Erro ao abrir fallback Windows Security: {fallbackEx.Message}");
                }
            }
        }

        public void StopPolling()
        {
            var oldCts = Interlocked.Exchange(ref _pollingCts, new CancellationTokenSource());
            try { oldCts.Cancel(); oldCts.Dispose(); } catch { }
        }

        private async Task PollStatusAfterExternalChangeAsync()
        {
            var token = _pollingCts.Token;
            _logger.Log(LogLevel.Info, LogCategory.Security, "Iniciando polling de status após abertura de página externa...");

            if (_status == null)
            {
                _logger.Log(LogLevel.Debug, LogCategory.Security, "Status ainda não carregado — polling adiado para após EnsureInitializedAsync");
                await EnsureInitializedAsync();
            }

            if (token.IsCancellationRequested) return;

            var previousStatus = _status;
            var maxAttempts = 10;
            var changed = false;

            try { await Task.Delay(1000, token); } catch (OperationCanceledException) { return; }

            for (int i = 0; i < maxAttempts && !changed; i++)
            {
                if (token.IsCancellationRequested) { _logger.Log(LogLevel.Info, LogCategory.Security, "Polling cancelado (navegação)"); return; }

                try
                {
                    _securityService.InvalidateCache();
                    var newStatus = await _securityService.GetSecurityStatusAsync();

                    changed = previousStatus == null
                           || newStatus.AntivirusEnabled             != previousStatus.AntivirusEnabled
                           || newStatus.RealTimeProtectionEnabled    != previousStatus.RealTimeProtectionEnabled
                           || newStatus.FirewallEnabled              != previousStatus.FirewallEnabled
                           || newStatus.WindowsUpdateEnabled         != previousStatus.WindowsUpdateEnabled
                           || newStatus.SmartScreenEnabled           != previousStatus.SmartScreenEnabled
                           || newStatus.TamperProtectionEnabled      != previousStatus.TamperProtectionEnabled
                           || newStatus.UacEnabled                   != previousStatus.UacEnabled
                           || newStatus.DefenderServiceEnabled       != previousStatus.DefenderServiceEnabled;

                    if (changed)
                    {
                        _logger.Log(LogLevel.Info, LogCategory.Security, "Mudança de status detectada — atualizando UI...");
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            Status = newStatus;
                            _isSmartScreenEnabled = newStatus.SmartScreenEnabled;
                            OnPropertyChanged(nameof(IsSmartScreenEnabled));
                            OnPropertyChanged(nameof(Status));
                        });
                        break;
                    }

                    previousStatus = newStatus;
                }
                catch (OperationCanceledException) { _logger.Log(LogLevel.Info, LogCategory.Security, "Polling cancelado durante GetSecurityStatusAsync"); return; }
                catch (Exception ex)
                {
                    _logger.Log(LogLevel.Warning, LogCategory.Security, $"Erro no poll #{i + 1}: {ex.Message}");
                }

                try { await Task.Delay(3000, token); } catch (OperationCanceledException) { return; }
            }

            if (!changed)
                _logger.Log(LogLevel.Debug, LogCategory.Security, "Polling encerrado sem mudanças detectadas.");
        }

        private void LaunchUacSettings()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "UserAccountControlSettings.exe",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error launching UAC settings: {ex.Message}");
                OpenWindowsSecurityPage("ms-settings:useraccounts");
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
