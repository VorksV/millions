using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.UI.Commands;
using VoltrisOptimizer.Services.License.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace VoltrisOptimizer.UI.ViewModels
{
    public class DebloatViewModel : INotifyPropertyChanged
    {
        // ── Dependências ─────────────────────────────────────────────────────
        private readonly IDebloatTuningService _debloatService;
        private readonly ILoggingService _logger;

        // ── Estado de cancelamento ────────────────────────────────────────────
        private CancellationTokenSource? _uninstallCts;

        // ── Coleção de apps ───────────────────────────────────────────────────
        public ObservableCollection<AppInfo> Apps { get; } = new();

        // ── IsBusy: bloqueia toda a UI de seleção durante desinstalação ───────
        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (_isBusy == value) return;
                _isBusy = value;
                _logger.LogDebug($"[DebloatVM] IsBusy → {value}");
                OnPropertyChanged();
                // IsNotBusy é o inverso — controla IsEnabled da lista e botões
                OnPropertyChanged(nameof(IsNotBusy));
            }
        }

        /// <summary>
        /// Inverso de IsBusy. Usado no XAML para IsEnabled da lista e botões de seleção.
        /// </summary>
        public bool IsNotBusy => !_isBusy;

        // ── IsUninstalling: controla visibilidade do botão Cancelar ──────────
        private bool _isUninstalling;
        public bool IsUninstalling
        {
            get => _isUninstalling;
            set
            {
                if (_isUninstalling == value) return;
                _isUninstalling = value;
                _logger.LogDebug($"[DebloatVM] IsUninstalling → {value}");
                OnPropertyChanged();
            }
        }

        // ── Progresso inline (barra dentro da view) ───────────────────────────
        private int _uninstallProgress;
        public int UninstallProgress
        {
            get => _uninstallProgress;
            set { _uninstallProgress = value; OnPropertyChanged(); }
        }

        private string _uninstallStatusMessage = string.Empty;
        public string UninstallStatusMessage
        {
            get => _uninstallStatusMessage;
            set { _uninstallStatusMessage = value; OnPropertyChanged(); }
        }

        // ── Contagem de selecionados ──────────────────────────────────────────
        private int _selectedCount;
        public int SelectedCount
        {
            get => _selectedCount;
            private set
            {
                if (_selectedCount == value) return;
                _selectedCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedCountText));
                OnPropertyChanged(nameof(HasSelectedApps));
            }
        }

        public bool HasSelectedApps => SelectedCount > 0 && !IsBusy;

        public string SelectedCountText
        {
            get
            {
                if (SelectedCount == 0) return string.Empty;
                return SelectedCount == 1
                    ? LocalizationService.Instance.GetString("DebloatOneProgramSelected")
                    : string.Format(LocalizationService.Instance.GetString("DebloatProgramsSelected"), SelectedCount);
            }
        }

        // ── Comandos ──────────────────────────────────────────────────────────
        public ICommand RefreshCommand           { get; }
        public ICommand UninstallSelectedCommand { get; }
        public ICommand CancelUninstallCommand   { get; }
        public ICommand CleanTempCommand         { get; }
        private RelayCommand? _cleanTempInner;

        // ── Evento para abrir modal de sucesso (View se inscreve) ─────────────
        public event Action<int, int>? UninstallCompleted;

        // ── Construtor ────────────────────────────────────────────────────────
        public DebloatViewModel(IDebloatTuningService debloatService, ILoggingService logger)
        {
            _debloatService = debloatService;
            _logger = logger;

            RefreshCommand           = new AsyncRelayCommand(async _ => await LoadAppsAsync(), _ => !IsBusy);
            UninstallSelectedCommand = new AsyncRelayCommand(async _ => await UninstallSelectedAppsAsync(), _ => HasSelectedApps);
            CancelUninstallCommand   = new RelayCommand(_ => RequestCancelUninstall(), _ => IsUninstalling);
            CleanTempCommand         = (_cleanTempInner = new AsyncRelayCommand(async _ => await CleanTempAsync(), _ => !IsBusy));

            _logger.LogInfo("[DebloatVM] ViewModel inicializado — carregando lista de apps...");
            _ = LoadAppsAsync();
        }

        // ════════════════════════════════════════════════════════════════════
        // CARREGAMENTO DE APPS
        // ════════════════════════════════════════════════════════════════════

        private async Task LoadAppsAsync()
        {
            _logger.LogInfo("[DebloatVM] LoadAppsAsync iniciado");

            // Desinscrever eventos dos apps anteriores
            foreach (var app in Apps)
                app.PropertyChanged -= App_PropertyChanged;

            Apps.Clear();
            SelectedCount = 0;

            try
            {
                _logger.LogInfo("[DebloatVM] Carregando aplicativos de forma otimizada...");
                
                // Carregar apps de forma assíncrona direta (sem Task.Run desnecessário)
                var appList = await _debloatService.GetInstalledAppsAsync(false);
                _logger.LogInfo($"[DebloatVM] Carregamento retornou {appList.Count} aplicativos");

                // Adicionar apps diretamente na UI (já estamos em thread correta)
                foreach (var app in appList)
                {
                    app.PropertyChanged += App_PropertyChanged;
                    Apps.Add(app);
                }
                
                _logger.LogInfo($"[DebloatVM] Lista carregada na UI: {Apps.Count} itens");
                IsBusy = false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[DebloatVM] Erro ao carregar aplicativos: {ex.Message}", ex);
                IsBusy = false;
            }
        }

        private void App_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppInfo.IsSelected))
            {
                SelectedCount = Apps.Count(a => a.IsSelected);
                _logger.LogDebug($"[DebloatVM] Seleção alterada → {SelectedCount} app(s) selecionado(s)");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // DESINSTALAÇÃO COM CANCELAMENTO
        // ════════════════════════════════════════════════════════════════════

        private async Task UninstallSelectedAppsAsync()
        {
            if (IsBusy)
            {
                _logger.LogWarning("[DebloatVM] UninstallSelectedAppsAsync ignorado — IsBusy=true");
                return;
            }

            var selectedApps = Apps.Where(a => a.IsSelected).ToList();
            if (selectedApps.Count == 0)
            {
                _logger.LogWarning("[DebloatVM] Nenhum app selecionado para desinstalar");
                return;
            }

            _logger.LogInfo($"[DebloatVM] ═══════════════════════════════════════════════════════");
            _logger.LogInfo($"[DebloatVM] INICIANDO DESINSTALAÇÃO DE {selectedApps.Count} APP(S)");
            _logger.LogInfo($"[DebloatVM] Apps: {string.Join(", ", selectedApps.Select(a => a.Name))}");
            _logger.LogInfo($"[DebloatVM] ═══════════════════════════════════════════════════════");

            // ── Configurar estado de desinstalação ────────────────────────────
            _uninstallCts = new CancellationTokenSource();
            var token = _uninstallCts.Token;

            IsBusy         = true;
            IsUninstalling = true;
            UninstallProgress      = 0;
            UninstallStatusMessage = string.Format(LocalizationService.Instance.GetString("DebloatPreparingUninstall"), selectedApps.Count);

            // Bloquear checkboxes — desabilitar seleção de todos os apps
            foreach (var app in Apps)
                app.IsSelectionLocked = true;

            // Notificar comandos que o estado mudou
            (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (UninstallSelectedCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (CancelUninstallCommand as RelayCommand)?.RaiseCanExecuteChanged();
            _cleanTempInner?.RaiseCanExecuteChanged();

            // ── Iniciar operação na barra global ──────────────────────────────
            GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("DebloatUninstallTitle"), true);
            _logger.LogInfo("[DebloatVM] GlobalProgressService.StartOperation chamado");

            int total        = selectedApps.Count;
            int successCount = 0;
            int failureCount = 0;
            bool wasCancelled = false;

            try
            {
                for (int i = 0; i < selectedApps.Count; i++)
                {
                    // Verificar cancelamento antes de cada app
                    if (token.IsCancellationRequested)
                    {
                        _logger.LogInfo($"[DebloatVM] Cancelamento detectado antes de desinstalar '{selectedApps[i].Name}'");
                        wasCancelled = true;
                        break;
                    }

                    var app = selectedApps[i];
                    int progressBefore = (int)((i / (double)total) * 100);
                    int progressAfter  = (int)(((i + 1) / (double)total) * 100);

                    _logger.LogInfo($"[DebloatVM] [{i + 1}/{total}] Desinstalando '{app.Name}' (Win32={app.IsWin32})...");

                    var statusMsg = string.Format(LocalizationService.Instance.GetString("DebloatUninstallingApp"), app.Name, i + 1, total);
                    UninstallProgress      = progressBefore;
                    UninstallStatusMessage = statusMsg;
                    GlobalProgressService.Instance.UpdateProgress(progressBefore, statusMsg);

                    bool success = false;
                    try
                    {
                        success = await _debloatService.UninstallAppAsync(app.Name, app.IsWin32, token);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogInfo($"[DebloatVM] Desinstalação de '{app.Name}' cancelada pelo usuário");
                        wasCancelled = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"[DebloatVM] Exceção ao desinstalar '{app.Name}': {ex.Message}", ex);
                        success = false;
                    }

                    if (success)
                    {
                        successCount++;
                        var okMsg = string.Format(LocalizationService.Instance.GetString("DebloatUninstallSuccess"), app.Name, i + 1, total);
                        UninstallProgress      = progressAfter;
                        UninstallStatusMessage = okMsg;
                        GlobalProgressService.Instance.UpdateProgress(progressAfter, okMsg);
                        _logger.LogInfo($"[DebloatVM] ✓ '{app.Name}' desinstalado com sucesso");
                    }
                    else
                    {
                        failureCount++;
                        var failMsg = string.Format(LocalizationService.Instance.GetString("DebloatUninstallFailed"), app.Name, i + 1, total);
                        UninstallProgress      = progressAfter;
                        UninstallStatusMessage = failMsg;
                        GlobalProgressService.Instance.UpdateProgress(progressAfter, failMsg);
                        _logger.LogWarning($"[DebloatVM] ✗ Falha ao desinstalar '{app.Name}'");
                    }
                }

                _logger.LogInfo($"[DebloatVM] Loop de desinstalação finalizado — " +
                                $"sucesso={successCount} falha={failureCount} cancelado={wasCancelled}");

                // ── Pós-desinstalação: limpar atalhos e reconstruir lista ──────
                if (!wasCancelled && successCount > 0)
                {
                    var cleanMsg = LocalizationService.Instance.GetString("DebloatCleaningShortcuts");
                    UninstallStatusMessage = cleanMsg;
                    GlobalProgressService.Instance.UpdateProgress(90, cleanMsg);
                    _logger.LogInfo("[DebloatVM] Limpando atalhos órfãos...");

                    await Task.Run(() =>
                    {
                        try
                        {
                            var removed = StartMenuShortcutCleaner.CleanOrphanedShortcuts();
                            _logger.LogInfo($"[DebloatVM] Atalhos removidos: {removed}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"[DebloatVM] Erro ao limpar atalhos: {ex.Message}");
                        }
                    });
                }

                var rebuildMsg = LocalizationService.Instance.GetString("DebloatRebuildingList");
                UninstallStatusMessage = rebuildMsg;
                GlobalProgressService.Instance.UpdateProgress(95, rebuildMsg);
                _logger.LogInfo("[DebloatVM] Reconstruindo lista de aplicativos...");

                await Task.Delay(100); // Aguardar registry propagar (otimizado: 400→100ms)
                await LoadAppsAsync();

                // ── Finalizar barra global ────────────────────────────────────
                string finalMsg = wasCancelled
                    ? string.Format(LocalizationService.Instance.GetString("DebloatCancelledWithCount"), successCount)
                    : successCount > 0
                        ? string.Format(LocalizationService.Instance.GetString("DebloatSuccessWithCount"), successCount)
                        : LocalizationService.Instance.GetString("DebloatFinishedWithErrors");

                GlobalProgressService.Instance.CompleteOperation(finalMsg);
                if (successCount > 0)
                {
                    HistoryService.RecordActivity(LocalizationService.Instance.GetString("DebloatHistoryTitle"), string.Format(LocalizationService.Instance.GetString("DebloatHistoryRemoved"), successCount));
                    GlobalNotificationService.ShowSuccess(LocalizationService.Instance.GetString("DebloatNotificationTitle"), string.Format(LocalizationService.Instance.GetString("DebloatSuccessNotification"), successCount));
                }
                else if (wasCancelled)
                {
                    GlobalNotificationService.ShowWarning(LocalizationService.Instance.GetString("DebloatNotificationTitle"), LocalizationService.Instance.GetString("DebloatCancelledNotification"));
                }
                _logger.LogInfo($"[DebloatVM] GlobalProgressService.CompleteOperation: '{finalMsg}'");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[DebloatVM] Exceção inesperada no loop de desinstalação: {ex.Message}", ex);
                GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("DebloatUnexpectedError"));
                GlobalNotificationService.ShowError(LocalizationService.Instance.GetString("DebloatNotificationTitle"), string.Format(LocalizationService.Instance.GetString("DebloatUnexpectedErrorDetail"), ex.Message));
            }
            finally
            {
                _logger.LogInfo("[DebloatVM] Finalizando estado de desinstalação (finally)");

                // Desbloquear checkboxes
                foreach (var app in Apps)
                    app.IsSelectionLocked = false;

                IsBusy         = false;
                IsUninstalling = false;
                UninstallProgress      = 0;
                UninstallStatusMessage = string.Empty;

                _uninstallCts?.Dispose();
                _uninstallCts = null;

                // Notificar comandos
                (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (UninstallSelectedCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (CancelUninstallCommand as RelayCommand)?.RaiseCanExecuteChanged();
                _cleanTempInner?.RaiseCanExecuteChanged();

                _logger.LogInfo($"[DebloatVM] Estado restaurado — sucesso={successCount} falha={failureCount} cancelado={wasCancelled}");

                // Disparar evento para a View abrir o modal (apenas se não cancelado)
                if (!wasCancelled)
                {
                    _logger.LogInfo($"[DebloatVM] Disparando UninstallCompleted(success={successCount}, failure={failureCount})");
                    UninstallCompleted?.Invoke(successCount, failureCount);
                }
                else
                {
                    _logger.LogInfo("[DebloatVM] Desinstalação cancelada — modal de sucesso NÃO exibido");
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // CANCELAMENTO
        // ════════════════════════════════════════════════════════════════════

        private void RequestCancelUninstall()
        {
            if (_uninstallCts == null || _uninstallCts.IsCancellationRequested)
            {
                _logger.LogWarning("[DebloatVM] RequestCancelUninstall ignorado — CTS nulo ou já cancelado");
                return;
            }

            _logger.LogInfo("[DebloatVM] ⛔ Usuário solicitou CANCELAMENTO da desinstalação");
            UninstallStatusMessage = LocalizationService.Instance.GetString("DebloatCancelling");
            GlobalProgressService.Instance.UpdateProgress(UninstallProgress, LocalizationService.Instance.GetString("DebloatCancelling"));

            _uninstallCts.Cancel();
        }

        // ════════════════════════════════════════════════════════════════════
        // LIMPEZA DE TEMPORÁRIOS
        // ════════════════════════════════════════════════════════════════════

        private async Task CleanTempAsync()
        {
            if (IsBusy)
            {
                _logger.LogWarning("[DebloatVM] CleanTempAsync ignorado — IsBusy=true");
                return;
            }

            _logger.LogInfo("[DebloatVM] Iniciando limpeza de arquivos temporários...");
            IsBusy = true;

            GlobalProgressService.Instance.StartOperation(LocalizationService.Instance.GetString("DebloatCleanupTitle"), true);
            GlobalProgressService.Instance.UpdateProgress(20, LocalizationService.Instance.GetString("DebloatCleaningTempFiles"));

            bool success = await _debloatService.RemoveTempFilesAsync();

            if (success)
            {
                _logger.LogInfo("[DebloatVM] Limpeza de temporários concluída com sucesso");
                HistoryService.RecordActivity(LocalizationService.Instance.GetString("DebloatCleanupHistoryTitle"), LocalizationService.Instance.GetString("DebloatCleanupHistoryDetail"));
                GlobalProgressService.Instance.UpdateProgress(100, LocalizationService.Instance.GetString("DebloatTempFilesCleaned"));
                GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("DebloatCleanupSuccess"));
                GlobalNotificationService.ShowSuccess(LocalizationService.Instance.GetString("DebloatCleanupNotificationTitle"), LocalizationService.Instance.GetString("DebloatCleanupNotificationDetail"));
            }
            else
            {
                _logger.LogWarning("[DebloatVM] Limpeza de temporários finalizada com erros");
                GlobalProgressService.Instance.UpdateProgress(100, LocalizationService.Instance.GetString("DebloatTempFilesError"));
                GlobalProgressService.Instance.CompleteOperation(LocalizationService.Instance.GetString("DebloatCleanupFailed"));
                GlobalNotificationService.ShowWarning(LocalizationService.Instance.GetString("DebloatCleanupNotificationTitle"), LocalizationService.Instance.GetString("DebloatCleanupFailedDetail"));
            }

            IsBusy = false;
            _logger.LogInfo("[DebloatVM] CleanTempAsync finalizado");
        }

        // ════════════════════════════════════════════════════════════════════
        // INotifyPropertyChanged
        // ════════════════════════════════════════════════════════════════════

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
