using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Drivers;
using VoltrisOptimizer.Services.SmartRepair.Modules;
using VoltrisOptimizer.Services.SystemSnapshot;

namespace VoltrisOptimizer.UI.ViewModels
{
    /// <summary>
    /// Operações REAIS de recuperação, agrupadas para alimentar as abas da página.
    ///
    /// ANTES: as abas 2 a 6 da página de Recuperação eram MOCKUPS — exibiam pontos de
    /// restauração, backups, atributos SMART, histórico e snapshots com dados FIXOS no XAML
    /// e botões sem Command (inertes). Isso é indistinguível de funcionalidade real e
    /// exatamente o que esta fase proíbe. Nenhuma implementação de leitura de SMART existe no
    /// repositório, portanto a tabela de atributos SMART foi REMOVIDA em vez de simulada.
    ///
    /// Agora cada aba aponta para infraestrutura que existe de fato:
    ///  - Pontos de restauração : SystemSafetyService + SystemToolsService (reuso, sem sistema paralelo)
    ///  - Backups               : SystemSnapshotService + DriverBackupManager
    ///  - Reparo                : SystemToolsService.RepairSystemAsync (DISM + SFC) + módulos SmartRepair
    ///  - Histórico             : HistoryService (persistido em %LOCALAPPDATA%)
    ///
    /// Tudo é registrado no sistema de logs existente com operação, duração e resultado.
    /// </summary>
    public sealed class RecoveryOperationsViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly SystemToolsService _systemTools;
        private readonly DriverBackupManager _driverBackup = new();
        private SystemSafetyService? _safety;
        private SystemSnapshotService? _snapshots;

        private CancellationTokenSource? _cts;
        private bool _isBusy;
        private string _busyMessage = string.Empty;
        private double _repairProgress;
        private string _repairConsole = string.Empty;
        private bool _canOperate = true;
        private string _environmentNote = string.Empty;
        private bool _disposed;

        // ------------------------------------------------------------------ estado

        public ObservableCollection<SystemRestorePoint> RestorePoints { get; } = new();
        public ObservableCollection<SnapshotItem> Backups { get; } = new();
        public ObservableCollection<HistoryItem> History { get; } = new();
        public ObservableCollection<RepairActionItem> RepairActions { get; } = new();

        public bool IsBusy
        {
            get => _isBusy;
            private set { Set(ref _isBusy, value); RaiseCommands(); }
        }

        public string BusyMessage
        {
            get => _busyMessage;
            private set { Set(ref _busyMessage, value); }
        }

        public double RepairProgress
        {
            get => _repairProgress;
            private set { Set(ref _repairProgress, value); }
        }

        public string RepairConsole
        {
            get => _repairConsole;
            private set { Set(ref _repairConsole, value); }
        }

        /// <summary>
        /// False quando falta privilégio de administrador. Nesse caso as abas mostram a
        /// explicação em vez de botões que falhariam silenciosamente.
        /// </summary>
        public bool CanOperate
        {
            get => _canOperate;
            private set { Set(ref _canOperate, value); }
        }

        public string EnvironmentNote
        {
            get => _environmentNote;
            private set { Set(ref _environmentNote, value); }
        }

        // ------------------------------------------------------------------ comandos

        public ICommand RefreshCommand { get; }
        public ICommand CreateRestorePointCommand { get; }
        public ICommand RestorePointCommand { get; }
        public ICommand OpenWindowsRestoreCommand { get; }
        public ICommand CreateBackupCommand { get; }
        public ICommand RestoreBackupCommand { get; }
        public ICommand BackupDriversCommand { get; }
        public ICommand RunSystemRepairCommand { get; }
        public ICommand RunRepairActionCommand { get; }
        public ICommand RefreshHistoryCommand { get; }
        public ICommand CancelCommand { get; }

        public RecoveryOperationsViewModel()
        {
            _systemTools = new SystemToolsService(App.LoggingService);

            RefreshCommand = new AsyncRelayCommand(RefreshAllAsync, () => !IsBusy);
            CreateRestorePointCommand = new AsyncRelayCommand(() => CreateRestorePointAsync(null), () => !IsBusy);
            // Comando com parâmetro: o overload (Func<object?, Task>) exige a conversion explicita.
            RestorePointCommand = new AsyncRelayCommand((Func<object?, Task>)(p => CreateRestorePointAsync(p as string)), (Predicate<object?>)(_ => !IsBusy));
            OpenWindowsRestoreCommand = new RelayCommand(OpenWindowsRestore, () => !IsBusy);
            CreateBackupCommand = new AsyncRelayCommand(CreateBackupAsync, () => !IsBusy);
            RestoreBackupCommand = new AsyncRelayCommand((Func<object?, Task>)(p => RestoreBackupAsync(p as SnapshotItem)), (Predicate<object?>)(item => !IsBusy && item is SnapshotItem));
            BackupDriversCommand = new AsyncRelayCommand(BackupDriversAsync, () => !IsBusy);
            RunSystemRepairCommand = new AsyncRelayCommand(RunSystemRepairAsync, () => !IsBusy);
            RunRepairActionCommand = new AsyncRelayCommand((Func<object?, Task>)(p => RunRepairActionAsync(p as RepairActionItem)), (Predicate<object?>)(item => !IsBusy && item is RepairActionItem));
            RefreshHistoryCommand = new AsyncRelayCommand(RefreshHistoryAsync, () => !IsBusy);
            CancelCommand = new RelayCommand(Cancel, () => IsBusy);

            InitializeEnvironment();
            BuildRepairActions();

            App.LoggingService?.LogInfo("[RecoveryOps] Operações de recuperação inicializadas.");
        }

        private void InitializeEnvironment()
        {
            bool isAdmin = DriverSecurityService.IsRunningAsAdministrator();
            CanOperate = isAdmin;

            try
            {
                _safety = new SystemSafetyService(App.LoggingService);
            }
            catch (Exception ex)
            {
                _safety = null;
                App.LoggingService?.LogWarning(
                    $"[RecoveryOps] SystemSafetyService indisponível (ponto de restauração interno): {ex.GetType().Name}: {ex.Message}");
            }

            try
            {
                _snapshots = new SystemSnapshotService(App.LoggingService ?? new NullLog());
            }
            catch (Exception ex)
            {
                _snapshots = null;
                App.LoggingService?.LogWarning(
                    $"[RecoveryOps] SystemSnapshotService indisponível: {ex.GetType().Name}: {ex.Message}");
            }

            EnvironmentNote = isAdmin
                ? "Privilégios de administrador disponíveis — todas as ações estão liberadas."
                : "Sem privilégios de administrador: criar pontos de restauração, backups e reparos exige abrir o VOLTRIS como administrador.";

            if (!isAdmin)
                App.LoggingService?.LogWarning("[RecoveryOps] Processo sem elevação: ações destrutivas serão bloqueadas pela UI.");
        }

        /// <summary>
        /// Ações de reparo que EXISTEM como ferramenta nativa do Windows e são seguras em
        /// execução online. Cada item executa um comando real e reporta a saída real.
        /// </summary>
        private void BuildRepairActions()
        {
            RepairActions.Clear();
            RepairActions.Add(new RepairActionItem("chkdsk", "Verificar disco (CHKDSK /scan)",
                "Varre o volume do sistema online, sem agendar reinicialização."));
            RepairActions.Add(new RepairActionItem("defrag", "Desfragmentar metadados NTFS (defrag /L)",
                "Compacta apenas os metadados NTFS — não move dados, seguro online."));
            RepairActions.Add(new RepairActionItem("dismcheck", "Verificar integridade da imagem (DISM /ScanHealth)",
                "Verifica a imagem do Windows sem aplicar reparo."));
        }


        // ------------------------------------------------------------------ carga

        public async Task RefreshAllAsync()
        {
            using var op = new DriverOperationScope("RECOVERY_REFRESH", "todas as abas");
            IsBusy = true;
            BusyMessage = "Coletando informações reais do sistema…";

            await Task.Run(() =>
            {
                RefreshRestorePoints();
                RefreshSnapshots();
                RefreshHistory();
            }).ConfigureAwait(true);

            IsBusy = false;
            BusyMessage = string.Empty;
            op.Succeed($"pontos={RestorePoints.Count} | backups={Backups.Count} | historico={History.Count}");
        }

        private void RefreshRestorePoints()
        {
            RestorePoints.Clear();
            if (_safety == null) return;

            try
            {
                foreach (var point in _safety.GetRestorePoints().OrderByDescending(p => p.CreatedAt))
                    RestorePoints.Add(point);
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[RecoveryOps] Falha ao listar pontos de restauração. {ex.Describe()}", ex);
            }
        }

        private void RefreshSnapshots()
        {
            Backups.Clear();
            if (_snapshots == null) return;

            try
            {
                var snapshots = _snapshots.ListSnapshotsAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                foreach (var s in snapshots.OrderByDescending(x => x.Timestamp))
                    Backups.Add(new SnapshotItem { Id = s.Id, Label = s.OperationName, Timestamp = s.Timestamp, ItemCount = s.RegistryBackups?.Count ?? 0 });
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[RecoveryOps] Falha ao listar backups. {ex.Describe()}", ex);
            }
        }

        private void RefreshHistory()
        {
            History.Clear();
            try
            {
                var entries = HistoryService.Instance.GetHistory(200);
                foreach (var e in entries.OrderByDescending(x => x.Timestamp).Take(200))
                    History.Add(new HistoryItem(e));
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[RecoveryOps] Falha ao carregar o histórico. {ex.Describe()}", ex);
            }
        }

        private Task RefreshHistoryAsync()
        {
            IsBusy = true;
            try { RefreshHistory(); }
            finally { IsBusy = false; }
            return Task.CompletedTask;
        }

        // ------------------------------------------------------------------ pontos de restauração

        private async Task CreateRestorePointAsync(string? description)
        {
            if (!CanOperate)
            {
                App.LoggingService?.LogWarning("[RecoveryOps] Criação de ponto de restauração bloqueada: sem privilégio de administrador.");
                return;
            }

            using var op = new DriverOperationScope("RECOVERY_RESTORE_POINT", description ?? "Manual");
            IsBusy = true;
            _cts = new CancellationTokenSource();
            BusyMessage = "Criando ponto de restauração…";

            try
            {
                // Reutiliza a infraestrutura existente (SystemToolsService), sem criar
                // um sistema paralelo de restauração.
                bool created = await _systemTools
                    .CreateSystemRestorePointAsync(description ?? "Voltris - Ponto de restauração manual")
                    .ConfigureAwait(true);

                // Registra também o ponto interno (registro + serviços + plano de energia),
                // que permite reversão granular pelo VOLTRIS sem tocar no Windows.
                if (_safety != null)
                {
                    var internalPoint = await _safety.CreateRestorePointAsync(
                        description ?? "Ponto manual").ConfigureAwait(true);
                    op.Stage("PONTO_INTERNO", $"id={internalPoint.Id} | chaves={internalPoint.RegistryBackups.Count}");
                }

                RefreshRestorePoints();

                if (created) op.Succeed("ponto de restauração do Windows criado");
                else op.Fail("o Windows não confirmou a criação do ponto de restauração");
            }
            catch (Exception ex)
            {
                op.Fail("falha ao criar o ponto de restauração", ex);
            }
            finally
            {
                IsBusy = false;
                BusyMessage = string.Empty;
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void OpenWindowsRestore()
        {
            try
            {
                _systemTools.OpenSystemRestoreSelector();
                App.LoggingService?.LogInfo("[RecoveryOps] rstrui.exe (Restauração do Sistema do Windows) aberto.");
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[RecoveryOps] Falha ao abrir a Restauração do Sistema. {ex.Describe()}", ex);
            }
        }

        // ------------------------------------------------------------------ backups

        private async Task CreateBackupAsync()
        {
            if (_snapshots == null)
            {
                App.LoggingService?.LogError("[RecoveryOps] Backup indisponível: SystemSnapshotService não pôde ser inicializado.");
                return;
            }

            using var op = new DriverOperationScope("RECOVERY_BACKUP_CREATE", "snapshot do sistema");
            IsBusy = true;
            BusyMessage = "Criando snapshot do sistema…";

            try
            {
                var snapshot = await _snapshots
                    .CreateSnapshotAsync("Snapshot manual da Central de Recuperação")
                    .ConfigureAwait(true);

                RefreshSnapshots();
                op.Succeed($"id={snapshot.Id} | chaves={snapshot.RegistryBackups?.Count ?? 0}");
            }
            catch (Exception ex)
            {
                op.Fail("falha ao criar o snapshot", ex);
            }
            finally
            {
                IsBusy = false;
                BusyMessage = string.Empty;
            }
        }

        private async Task RestoreBackupAsync(SnapshotItem? item)
        {
            if (item == null || _snapshots == null) return;

            using var op = new DriverOperationScope("RECOVERY_BACKUP_RESTORE", item.Label);
            IsBusy = true;
            BusyMessage = $"Restaurando '{item.Label}'…";

            try
            {
                bool ok = await _snapshots.RestoreSnapshotAsync(item.Id).ConfigureAwait(true);
                if (ok) op.Succeed($"snapshot {item.Id} restaurado");
                else op.Fail("o snapshot não pôde ser restaurado");
            }
            catch (Exception ex)
            {
                op.Fail("falha ao restaurar o snapshot", ex);
            }
            finally
            {
                IsBusy = false;
                BusyMessage = string.Empty;
            }
        }

        private async Task BackupDriversAsync()
        {
            using var op = new DriverOperationScope("RECOVERY_DRIVERSTORE_BACKUP", "exportação via DISM");
            IsBusy = true;
            BusyMessage = "Exportando o DriverStore do sistema (DISM)…";

            try
            {
                string folder = await _driverBackup.BackupAllDriversAsync().ConfigureAwait(true);
                if (string.IsNullOrEmpty(folder))
                {
                    op.Fail("o DISM não produziu uma pasta de backup");
                    return;
                }
                op.Succeed($"DriverStore exportado para '{folder}'");
            }
            catch (Exception ex)
            {
                op.Fail("falha ao exportar o DriverStore", ex);
            }
            finally
            {
                IsBusy = false;
                BusyMessage = string.Empty;
            }
        }

        // ------------------------------------------------------------------ reparo

        private async Task RunSystemRepairAsync()
        {
            using var op = new DriverOperationScope("RECOVERY_SYSTEM_REPAIR", "DISM + SFC");
            IsBusy = true;
            RepairProgress = 0;
            RepairConsole = "Iniciando reparo da imagem do sistema (DISM /RestoreHealth)…";
            _cts = new CancellationTokenSource();

            try
            {
                bool ok = await _systemTools
                    .RepairSystemAsync(p => RepairProgress = p)
                    .ConfigureAwait(true);

                RepairProgress = 100;
                RepairConsole = ok
                    ? "Reparo concluído com sucesso. Reinicie para que todas as alterações entrem em vigor."
                    : "O reparo terminou com erros. Consulte o log de operações para o detalhamento.";

                if (ok) op.Succeed("DISM e SFC concluídos");
                else op.Fail("DISM/SFC retornaram erro");
            }
            catch (Exception ex)
            {
                RepairConsole = $"Falha durante o reparo: {ex.Message}";
                op.Fail("exceção durante o reparo do sistema", ex);
            }
            finally
            {
                IsBusy = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        private async Task RunRepairActionAsync(RepairActionItem? item)
        {
            if (item == null) return;

            using var op = new DriverOperationScope("RECOVERY_REPAIR_ACTION", item.Label);
            IsBusy = true;
            RepairConsole = $"{item.Label}: iniciando…";
            _cts = new CancellationTokenSource();

            try
            {
                string systemDrive = System.Environment.SystemDirectory.StartsWith(@"C:\", StringComparison.OrdinalIgnoreCase)
                    ? System.Environment.SystemDirectory.Substring(0, 3)
                    : "C:\\";
                string result = await ExecuteSystemToolAsync(item, systemDrive, _cts.Token);
                RepairConsole = $"{item.Label}: {result}";
                op.Succeed(result);
            }
            catch (OperationCanceledException)
            {
                RepairConsole = $"{item.Label}: cancelado.";
                op.Fail("cancelado");
            }
            catch (Exception ex)
            {
                RepairConsole = $"{item.Label}: falhou — {ex.Message}";
                op.Fail("falha na execução do módulo", ex);
            }
            finally
            {
                IsBusy = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        /// <summary>
        /// Executa uma ferramenta nativa do Windows e devolve a saída REAL.
        /// Nenhuma destas operações é destrutiva e nenhuma agenda reinicialização.
        /// </summary>
        private async Task<string> ExecuteSystemToolAsync(RepairActionItem item, string systemDrive, CancellationToken token)
        {
            var (fileName, arguments) = item.ModuleId switch
            {
                "chkdsk" => ("chkdsk.exe", $"{systemDrive} /scan"),
                "defrag" => ("defrag.exe", $"{systemDrive} /L"),
                "dismcheck" => ("dism.exe", "/Online /Cleanup-Image /ScanHealth"),
                _ => throw new InvalidOperationException($"Ferramenta desconhecida: {item.ModuleId}")
            };

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Não foi possível iniciar {fileName}.");

            // Leitura concorrente dos dois fluxos: um await por vez pode deadlock se o outro
            // pipe encher antes de ser consumido.
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            using (token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } }))
            {
                if (!process.WaitForExit(15 * 60 * 1000))
                {
                    try { process.Kill(true); } catch { }
                    throw new TimeoutException($"{fileName} excedeu 15 minutos e foi encerrado.");
                }
            }

            string output = (await stdoutTask.ConfigureAwait(false) + "\n" + await stderrTask.ConfigureAwait(false)).Trim();

            if (process.ExitCode == 0)
                return $"concluído com sucesso (código 0).\n{Summarize(output)}";

            // Exit codes do CHKDSK têm semântica própria (0=nenhum erro, 1=erros corrigidos,
            // 2=corrupção corrigida, 3=erros não corrigidos). Reportar o código real, nunca
            // um "sucesso" genérico.
            return $"finalizou com código {process.ExitCode}.\n{Summarize(output)}";
        }

        private static string Summarize(string output)
        {
            if (string.IsNullOrWhiteSpace(output)) return "(sem saída)";
            string flat = output.Replace("\r", " ").Replace("\n", " | ").Trim();
            return flat.Length <= 600 ? flat : flat[..600] + "...";
        }

        private void Cancel()
        {
            try { _cts?.Cancel(); } catch { }
        }

        // ------------------------------------------------------------------ notificação

        public event PropertyChangedEventHandler? PropertyChanged;

        private void RaiseCommands()
        {
            foreach (var cmd in new ICommand?[]
            {
                RefreshCommand, CreateRestorePointCommand, RestorePointCommand, OpenWindowsRestoreCommand,
                CreateBackupCommand, RestoreBackupCommand, BackupDriversCommand, RunSystemRepairCommand,
                RunRepairActionCommand, RefreshHistoryCommand, CancelCommand
            })
            {
                if (cmd is AsyncRelayCommand asyncCmd) asyncCmd.RaiseCanExecuteChanged();
                else if (cmd is RelayCommand relayCmd) relayCmd.RaiseCanExecuteChanged();
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _cts?.Cancel(); } catch { }
            _cts?.Dispose();
        }

        // ------------------------------------------------------------------ modelos de linha

        /// <summary>Backup exibido na aba, adaptando o snapshot real para a UI.</summary>
        public sealed class SnapshotItem
        {
            public string Id { get; init; } = string.Empty;
            public string Label { get; init; } = string.Empty;
            public DateTime Timestamp { get; init; }
            public int ItemCount { get; init; }
            public string TimestampText => Timestamp == default ? "—" : Timestamp.ToString("dd/MM/yyyy HH:mm");
            public string ItemCountText => $"{ItemCount} item(ns)";
            public string SizeHint => "Pasta: %LOCALAPPDATA%\\Voltris\\Snapshots";
        }

        /// <summary>Entrada real do HistoryService.</summary>
        public sealed class HistoryItem
        {
            public HistoryItem(OptimizationHistory entry)
            {
                ActionType = entry.ActionType;
                Description = entry.Description;
                Timestamp = entry.Timestamp;
                Success = entry.Success;
                DurationMs = (long)entry.Duration.TotalMilliseconds;
            }

            public string ActionType { get; }
            public string Description { get; }
            public DateTime Timestamp { get; }
            public bool Success { get; }
            public long DurationMs { get; }

            public string TimestampText => Timestamp == default ? "—" : Timestamp.ToString("dd/MM/yyyy HH:mm:ss");
            public string DurationText => DurationMs > 0 ? $"{DurationMs} ms" : "—";
            public string ResultText => Success ? "Sucesso" : "Falha";
        }

        /// <summary>Ação de reparo disponível, ligada a uma ferramenta nativa real.</summary>
        public sealed class RepairActionItem
        {
            public RepairActionItem(string moduleId, string label, string description)
            {
                ModuleId = moduleId;
                Label = label;
                Description = description;
            }

            public string ModuleId { get; }
            public string Label { get; }
            public string Description { get; }
        }

        /// <summary>Logger no-op para quando o DI ainda não está pronto.</summary>
        private sealed class NullLog : ILoggingService
        {
            public event EventHandler<string>? LogEntryAdded { add { } remove { } }
            public void LogInfo(string message) { }
            public void LogSuccess(string message) { }
            public void LogWarning(string message) { }
            public void LogError(string message, Exception? exception = null) { }
            public void LogDebug(string message, string? source = null) { }
            public void LogTrace(string message, string? source = null) { }
            public void LogCritical(string message, Exception? exception = null, string? source = null) { }
            public void Log(LogLevel level, LogCategory category, string message, Exception? exception = null, string? source = null) { }
            public void Flush() { }
            public void ClearLogs() { }
            public string[] GetLogs() => Array.Empty<string>();
            public void ExportLogs(string destPath) { }
            public string GetLogDirectory() => string.Empty;
            public void Dispose() { }
        }
    }
}
