using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.Recovery;
using System.Windows.Input;
using System.Windows;
using System.Linq;
using System.Collections.Generic;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.UI.ViewModels
{
    public class RecoveryViewModel : ViewModelBase
    {
        private readonly VoltrisOptimizer.Services.Recovery.DataRecoveryService _recoveryService;
        private readonly VoltrisOptimizer.Services.Recovery.VSSRecoveryService _vssService = new();
        private bool _isScanning;
        private double _scanProgress;
        private string _statusMessage = LocalizationService.Instance.GetString("Ready");
        private VoltrisOptimizer.Services.Recovery.NativeRecoveredFile? _selectedFile;
        private ObservableCollection<string> _availableDrives = new();
        private string _selectedDrive = "C:";
        private int _shadowCopyCount;

        private string _searchQuery = string.Empty;
        private CancellationTokenSource? _searchCts;
        private long _searchTimeMs;
        private ObservableCollection<VoltrisOptimizer.Services.Recovery.NativeRecoveredFile> _filteredList = new();

        /// <summary>
        /// Seletor de tipo. Montado a partir do que a varredura encontrou,
        /// com contagem em cada linha — o usuário vê o que existe no volume
        /// antes de escolher, em vez de filtrar às cegas.
        ///
        /// Combina com a busca por texto em vez de competir com ela:
        /// escolher "Documentos" e digitar "relatorio" dá a interseção.
        /// </summary>
        public ObservableCollection<UI.Models.FileTypeOption> TypeOptions { get; } = new();

        private int _filterGeneration;
        private UI.Models.FileTypeOption? _selectedTypeOption;
        public UI.Models.FileTypeOption? SelectedTypeOption
        {
            get => _selectedTypeOption;
            set
            {
                if (SetProperty(ref _selectedTypeOption, value))
                {
                    App.LoggingService?.LogInfo(
                        $"[Recovery] SelectedTypeOption = '{value?.Label ?? "(todos)"}' (chave='{value?.Key ?? ""}')");
                    _ = ApplyTypeFilterAsync();
                }
            }
        }

        private async Task ApplyTypeFilterAsync()
        {
            var cts = new CancellationTokenSource();
            var old = _searchCts;
            _searchCts = cts;
            old?.Cancel();
            try
            {
                await FilterFilesAsync(SearchQuery, cts.Token);
            }
            catch (TaskCanceledException) { }
        }

        public List<VoltrisOptimizer.Services.Recovery.NativeRecoveredFile> FullList { get; } = new();

        /// <summary>
        /// Quantas linhas o DataGrid recebe de uma vez.
        ///
        /// Este número NÃO é um filtro: a lista completa está em
        /// <see cref="FullList"/> e a busca trabalha sobre ela. É só o
        /// tamanho do lote handed to the grid.
        ///
        /// O valor é baixo por um motivo medido, não por paleio. O DataGrid
        /// desta aba usa colunas com Width="*", e o WPF precisa MEDIR o
        /// conteúdo de todas as linhas para resolver a largura automática —
        /// a virtualização de linha não evita isso. Subir de 500 para 5.000
        /// com o template de célula enriquecido (duas colunas com StackPanel
        /// + 4 TextBlocks em vez de 2 TextBlocks) deu um congelamento de
        /// interface logo ao fim da varredura. A coleta de dados levou 117 ms
        /// em 44.464 itens; o travamento foi 100% renderização.
        ///
        /// Para ver mais, o usuário clica em "carregar mais" — que cresce a
        /// lista em lotes sob demanda, sem nunca bloquear a UI.
        /// </summary>
        public const int PageSize = 1000;

        /// <summary>
        /// Operações REAIS de recuperação (pontos de restauração, backups, reparo, histórico).
        /// Antes estas abas eram mockups com dados fixos no XAML e botões inertes.
        /// </summary>
        public RecoveryOperationsViewModel Operations { get; }

        public ObservableCollection<VoltrisOptimizer.Services.Recovery.NativeRecoveredFile> FilteredList
        {
            get => _filteredList;
            set { SetProperty(ref _filteredList, value); }
        }

        public string SearchQuery
        {
            get => _searchQuery;
            set 
            { 
                if (SetProperty(ref _searchQuery, value))
                {
                    DebounceSearch();
                }
            }
        }

        /// <summary>
        /// Rodapé da lista. Duas contagens, não uma.
        ///
        /// A engine encontra dezenas de milhares de arquivos num volume de
        /// centenas de GB, e o DataGrid recebe só um lote por vez. Mostrar
        /// apenas o número do lote faria o usuário acreditar que aquilo é o
        /// total — e foi exatamente o que aconteceu: o rodapé dizia
        /// "500 arquivos" quando o motor tinha 44.515.
        /// </summary>
        public string SearchStats
        {
            get
            {
                string tmpl = LocalizationService.Instance.GetString("SearchStats");
                int shown = FilteredList.Count;
                string s = string.Format(tmpl, shown, _searchTimeMs);

                int total = _searchQuery.Length == 0 ? FullList.Count : _lastFilterTotal;
                if (total > shown)
                {
                    s += $"  (de {total:N0} encontrados — buscando por nome você alcança todos)";
                }
                return s;
            }
        }

        private int _lastFilterTotal;

        /// <summary>
        /// Monta o seletor de tipo a partir do resultado real.
        ///
        /// Só aqui, e não durante o <c>GetFoundFiles</c>, porque é neste
        /// ponto que o total está fechado e as contagens por tipo são
        /// exatas. Montar antes daria contagem parciais.
        /// </summary>
        private void RebuildTypeOptions()
        {
            var options = UI.Models.FileTypeOptionBuilder.Build(FullList);

            TypeOptions.Clear();
            foreach (var o in options) TypeOptions.Add(o);

            // Preserva a seleção se ela ainda existir depois de uma nova
            // varredura; senão volta para "Todos". Sem isso, o combo
            // marcava um item que não está mais na lista e o filtro
            // esvaziava a tela sem explicação.
            var keep = SelectedTypeOption;
            SelectedTypeOption = null;
            if (keep != null && keep.Key.Length > 0)
            {
                var again = TypeOptions.FirstOrDefault(o => o.Key == keep.Key && o.IsFolder == keep.IsFolder);
                if (again != null) { SelectedTypeOption = again; return; }
            }
            if (TypeOptions.Count > 0) SelectedTypeOption = TypeOptions[0];

            App.LoggingService?.LogInfo(
                $"[Recovery] RebuildTypeOptions: {TypeOptions.Count} opcoes -> " +
                string.Join(" | ", TypeOptions.Take(12).Select(o => $"{o.Label}={o.Count}")));
        }

        /// <summary>
        /// Resultado completo do último filtro. A busca roda sobre
        /// <see cref="FullList"/> inteiro, então um termo encontra item que
        /// nunca chegou a ser desenhado — este snapshot é o que permite
        /// mostrar todos eles por paginação.
        /// </summary>
        private List<VoltrisOptimizer.Services.Recovery.NativeRecoveredFile> _filterSnapshot = new();

        /// <summary>
        /// Cresce a lista em mais um lote, a pedido do usuário.
        ///
        /// Isto substitui subir o limite do DataGrid de uma vez. O travamento
        /// de interface vinha de entregar milhares de linhas de uma só vez, e
        /// nenhum número alto resolve isso — a única solução é não pedir
        /// milhares de linhas de uma vez. O usuário decide quando quer mais,
        /// e cada clique é um lote que a UI aguenta.
        /// </summary>
        private RelayCommand? _loadMoreCommand;
        public RelayCommand LoadMoreCommand => _loadMoreCommand ??= new RelayCommand(_ => LoadMore());

        private void LoadMore()
        {
            var source = _searchQuery.Length == 0 ? (IReadOnlyList<VoltrisOptimizer.Services.Recovery.NativeRecoveredFile>)FullList : _filterSnapshot;
            int target = FilteredList.Count + PageSize;
            if (target > source.Count) target = source.Count;
            if (target <= FilteredList.Count) return;

            for (int i = FilteredList.Count; i < target; i++)
                FilteredList.Add(source[i]);

            OnPropertyChanged(nameof(SearchStats));
            OnPropertyChanged(nameof(CanLoadMore));
        }

        public bool CanLoadMore
        {
            get
            {
                int total = _searchQuery.Length == 0 ? FullList.Count : _lastFilterTotal;
                return FilteredList.Count < total && FilteredList.Count > 0;
            }
        }



        public ObservableCollection<string> AvailableDrives
        {
            get => _availableDrives;
            set { SetProperty(ref _availableDrives, value); }
        }

        public string SelectedDrive
        {
            get => _selectedDrive;
            set { SetProperty(ref _selectedDrive, value); }
        }

        public VoltrisOptimizer.Services.Recovery.NativeRecoveredFile? SelectedFile
        {
            get => _selectedFile;
            set 
            { 
                SetProperty(ref _selectedFile, value);
                ((AsyncRelayCommand)RecoverFileCommand).RaiseCanExecuteChanged();
            }
        }

        public bool IsScanning
        {
            get => _isScanning;
            set 
            { 
                SetProperty(ref _isScanning, value);
                ((AsyncRelayCommand)QuickScanCommand).RaiseCanExecuteChanged();
                ((AsyncRelayCommand)DeepScanCommand).RaiseCanExecuteChanged();
                ((AsyncRelayCommand)CancelScanCommand).RaiseCanExecuteChanged();
                ((AsyncRelayCommand)RecoverFileCommand).RaiseCanExecuteChanged();
            }
        }

        public double ScanProgress
        {
            get => _scanProgress;
            set { SetProperty(ref _scanProgress, value); }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { SetProperty(ref _statusMessage, value); }
        }

        public ICommand QuickScanCommand { get; }
        public ICommand DeepScanCommand { get; }
        public ICommand VssScanCommand { get; }
        public ICommand CancelScanCommand { get; }
        public ICommand RecoverFileCommand { get; }

        public int ShadowCopyCount
        {
            get => _shadowCopyCount;
            set { SetProperty(ref _shadowCopyCount, value); OnPropertyChanged(nameof(HasShadowCopies)); OnPropertyChanged(nameof(ShadowCopyCountText)); }
        }
        public bool HasShadowCopies => _shadowCopyCount > 0;

        public string ShadowCopyCountText => string.Format(LocalizationService.Instance.GetString("Loc_SnapshotCountAvailable"), ShadowCopyCount);

        public RecoveryViewModel()
        {
            App.LoggingService?.LogInfo("[Recovery] RecoveryViewModel: Initializing components.");
            _recoveryService = new VoltrisOptimizer.Services.Recovery.DataRecoveryService();

            using (var scope = new VoltrisOptimizer.Services.Drivers.DriverOperationScope("RECOVERY_PAGE_OPEN", "Central de Recuperação"))
            {
                try
                {
                    Operations = new RecoveryOperationsViewModel();
                    scope.Stage("SERVICOS", "operações de recuperação disponíveis");
                }
                catch (Exception ex)
                {
                    // A página de recuperação de arquivos continua utilizável mesmo se as
                    // operações de sistema não puderem ser inicializadas.
                    Operations = new RecoveryOperationsViewModel();
                    scope.StageFailed("SERVICOS", "falha ao inicializar as operações de sistema", ex);
                }
            }

            QuickScanCommand  = new AsyncRelayCommand(() => RunScan(false),   () => !IsScanning);
            DeepScanCommand   = new AsyncRelayCommand(() => RunScan(true),    () => !IsScanning);
            VssScanCommand    = new AsyncRelayCommand(RunVssScan,             () => !IsScanning);
            CancelScanCommand = new AsyncRelayCommand(() => Task.Run(() => CancelScan()), () => IsScanning);
            RecoverFileCommand = new AsyncRelayCommand(RecoverSelectedFile,   () => SelectedFile != null && !IsScanning);

            LoadDrives();

            // Carrega os dados REAIS das abas de sistema logo na abertura, em background,
            // para que a interface já apresente informação verdadeira.
            _ = Operations.RefreshAllAsync();

            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
            App.LoggingService?.LogInfo("[Recovery] RecoveryViewModel: Constructor completed.");
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            System.Windows.Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                OnPropertyChanged(nameof(ShadowCopyCountText));
                OnPropertyChanged(nameof(StatusMessage));
            });
        }

        private void LoadDrives()
        {
            App.LoggingService?.LogInfo("[Recovery] LoadDrives: entry.");
            _ = Task.Run(() =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var drives = new List<string>();
                try
                {
                    foreach (var drive in DriveInfo.GetDrives())
                    {
                        if (drive.IsReady)
                            drives.Add(drive.Name.TrimEnd('\\'));
                    }
                }
                catch (Exception ex)
                {
                    App.LoggingService?.LogError($"[Recovery] Error listing drives: {ex.Message}", ex);
                }

                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    AvailableDrives.Clear();
                    foreach (var d in drives)
                    {
                        AvailableDrives.Add(d);
                    }
                    if (AvailableDrives.Count > 0) SelectedDrive = AvailableDrives[0];

                    App.LoggingService?.LogInfo($"[Recovery] Available drives: {string.Join(", ", AvailableDrives)}");

                    // Verificar shadow copies disponíveis em background
                    if (AvailableDrives.Count > 0)
                    {
                        _ = Task.Run(() =>
                        {
                            var copies = _vssService.GetAvailableShadowCopies(SelectedDrive);
                            Application.Current.Dispatcher.BeginInvoke(() => ShadowCopyCount = copies.Count);
                        });
                    }

                    App.LoggingService?.LogInfo($"[Recovery] LoadDrives: completed in {sw.ElapsedMilliseconds}ms, drives: {string.Join(", ", AvailableDrives)}");
                });
            });
        }

        private async void DebounceSearch()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            App.LoggingService?.LogInfo($"[Recovery] DebounceSearch: entry, query=\"{SearchQuery}\"");
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var token = _searchCts.Token;

            try
            {
                await Task.Delay(300, token);
                await FilterFilesAsync(SearchQuery, token);
                App.LoggingService?.LogInfo($"[Recovery] DebounceSearch: filter completed in {sw.ElapsedMilliseconds}ms.");
            }
            catch (TaskCanceledException)
            {
                App.LoggingService?.LogInfo($"[Recovery] DebounceSearch: cancelled after {sw.ElapsedMilliseconds}ms.");
            }
        }

        private bool MatchesSearch(VoltrisOptimizer.Services.Recovery.NativeRecoveredFile file, string query)
        {
            // SEM LOG POR ITEM.
            //
            // Este método roda uma vez por arquivo, e a lista tem 44 mil.
            // Com as três chamadas de LogInfo que existiam aqui, cada
            // passagem de filtro escrevia ~134 mil linhas no disco. O log
            // parou de ser diagnóstico e virou o gargalo: o filtro levava
            // 287 ms — quase tudo I/O de log — e a UI parecia travada.
            // Quem filtra 44 mil itens não pode narrar cada um deles.
            if (string.IsNullOrWhiteSpace(query))
            {
                return true;
            }
            
            var tokens = query.Split(new[] { '+', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string nameLower = file.Filename.ToLowerInvariant();
            string categoryLower = file.Category.ToString().ToLowerInvariant();
            string extLower = file.Extension.ToLowerInvariant();

            foreach(var token in tokens)
            {
                var t = token.Trim().ToLowerInvariant();
                bool tokenMatched = false;

                if (nameLower.Contains(t) || categoryLower.Contains(t) || extLower.Contains(t))
                {
                    tokenMatched = true;
                }
                else if (t.StartsWith(">") && t.EndsWith("mb"))
                {
                    if (double.TryParse(t.Substring(1, t.Length - 3), out double mb))
                    {
                        if (file.SizeBytes > mb * 1024 * 1024) tokenMatched = true;
                    }
                }
                else if (t.StartsWith("<") && t.EndsWith("mb"))
                {
                    if (double.TryParse(t.Substring(1, t.Length - 3), out double mb))
                    {
                        if (file.SizeBytes < mb * 1024 * 1024) tokenMatched = true;
                    }
                }

                if (!tokenMatched)
                {
                    return false;
                }
            }

            return true;
        }

        private async Task FilterFilesAsync(string query, CancellationToken token)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // ── GERAÇÃO ──────────────────────────────────────────────
            // Cada filtro recebe um número. Só o mais recente pode
            // publicar resultado.
            //
            // Antes, dois cliques rápidos em tipos diferentes produziam
            // isto: o clique em "Imagens" começava, o clique em
            // "Compactados" cancelava o token, e o primeiro saía pelo
            // `return` de cancelamento — sem publicar. Se o cancelamento
            // chegasse DEPOIS do BeginInvoke, o resultado velho era
            // aplicado por cima do novo, e a tela mostrava o filtro
            // anterior sem nenhuma indicação. É por isso que o dropdown
            // mostrava a contagem e a lista não mudava.
            int generation = Interlocked.Increment(ref _filterGeneration);
            App.LoggingService?.LogInfo(
                $"[Recovery] FilterFilesAsync: gen={generation} query=\"{query}\" " +
                $"tipo=\"{SelectedTypeOption?.Label}\" fonte={FullList.Count}");

            var sourceList = FullList.ToList();
            var q = query?.ToLowerInvariant() ?? "";
            // O tipo é lido AQUI, na thread da UI, e não dentro do Task.Run.
            // Ler uma propriedade do ViewModel de uma thread de fundo é
            // exatamente o tipo de acesso que faz o binding do WPF se
            // comportar de forma imprevisível.
            var type = SelectedTypeOption;

            NativeRecoveredFile[] filtered;
            try
            {
                filtered = await Task.Run(() =>
                {
                    var result = new List<NativeRecoveredFile>(sourceList.Count);
                    foreach (var file in sourceList)
                    {
                        if (token.IsCancellationRequested) break;
                        // Tipo E texto se combinam: é interseção, e não um
                        // sobrescrevendo o outro. Escolher "Documentos" e
                        // digitar "relatorio" tem que dar documentos com
                        // "relatorio".
                        if (!UI.Models.FileTypeOptionBuilder.Matches(file, type)) continue;
                        if (MatchesSearch(file, q)) result.Add(file);
                    }
                    return result.ToArray();
                }, token);
            }
            catch (TaskCanceledException)
            {
                App.LoggingService?.LogInfo(
                    $"[Recovery] FilterFilesAsync: gen={generation} cancelado durante a varredura");
                return;
            }

            if (token.IsCancellationRequested)
            {
                App.LoggingService?.LogInfo(
                    $"[Recovery] FilterFilesAsync: gen={generation} cancelado, resultado descartado");
                return;
            }

            // Publica no Dispatcher e ESPERA. BeginInvoke era fogo-e-esquece:
            // a método devolvia antes de a UI ser atualizada, e a corrida
            // entre dois filtros acabava com o resultado antigo na tela.
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                // Só o mais recente publica. Um filtro antigo que ficou
                // pronto depois não pode sobrescrever o atual.
                if (generation != _filterGeneration) return;

                _filterSnapshot = new List<NativeRecoveredFile>(filtered);
                _lastFilterTotal = filtered.Length;

                var shown = filtered.Length > PageSize
                    ? filtered.Take(PageSize).ToList()
                    : filtered.ToList();

                FilteredList = new ObservableCollection<NativeRecoveredFile>(shown);
                _searchTimeMs = sw.ElapsedMilliseconds;
                OnPropertyChanged(nameof(SearchStats));
                OnPropertyChanged(nameof(CanLoadMore));
                App.LoggingService?.LogInfo(
                    $"[Recovery] FilterFilesAsync: gen={generation} publicado em " +
                    $"{sw.ElapsedMilliseconds}ms, {filtered.Length} resultados ({shown.Count} desenhados), " +
                    $"tipo=\"{type?.Label}\" query=\"{query}\"");
            });
        }

        private async Task RunVssScan()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            App.LoggingService?.LogInfo($"[Recovery] Starting VSS (Shadow Copy) scan on drive: {SelectedDrive}");
            var progressToken = VoltrisOptimizer.Services.GlobalProgressService.Instance.BeginOperation("Scan VSS (Shadow Copy)", isPriority: true);
            IsScanning = true;
            ScanProgress = 0;
            FullList.Clear();
            FilteredList.Clear();

            try
            {
                StatusMessage = LocalizationService.Instance.GetString("SearchingWindowsSnapshots");
                var copies = await Task.Run(() => _vssService.GetAvailableShadowCopies(SelectedDrive));

                if (copies.Count == 0)
                {
                    App.LoggingService?.LogWarning("[Recovery] VSS scan aborted: no active Shadow Copy found.");
                    StatusMessage = LocalizationService.Instance.GetString("NoShadowCopyFound");
                    progressToken.Complete(LocalizationService.Instance.GetString("NoSnapshots"));
                    return;
                }

                ShadowCopyCount = copies.Count;
                var latest = copies.First(); // Mais recente
                StatusMessage = string.Format(LocalizationService.Instance.GetString("ScanningSnapshot"), latest.CreationTime.ToString("dd/MM/yyyy HH:mm"));
                progressToken.UpdateProgress(10, StatusMessage);

                var lastVssProgressTime = DateTime.MinValue;
                var progress = new Progress<(double percent, int count)>(p =>
                {
                    var now = DateTime.UtcNow;
                    if ((now - lastVssProgressTime).TotalMilliseconds >= 200 || p.percent >= 100)
                    {
                        lastVssProgressTime = now;
                        ScanProgress = p.percent;
                        StatusMessage = string.Format(LocalizationService.Instance.GetString("AnalyzingSnapshotFiles"), p.count);
                        progressToken.UpdateProgress((int)p.percent, StatusMessage);
                    }
                });

                using var cts = new CancellationTokenSource();
                var deletedFiles = await _vssService.ScanShadowCopyAsync(latest, SelectedDrive, progress, cts.Token);

                var (tempFullList, tempFilteredList) = await Task.Run(() => 
                {
                    var full = new List<VoltrisOptimizer.Services.Recovery.NativeRecoveredFile>(deletedFiles.Count);
                    var filtered = new List<VoltrisOptimizer.Services.Recovery.NativeRecoveredFile>(deletedFiles.Count);

                    // Converter VSSRecoveredFile → NativeRecoveredFile para reusar a UI existente
                    foreach (var vssFile in deletedFiles)
                    {
                        var nf = new VoltrisOptimizer.Services.Recovery.NativeRecoveredFile
                        {
                            Id        = (ulong)vssFile.OriginalPath.GetHashCode(),
                            Filename  = vssFile.Filename,
                            Extension = vssFile.Extension,
                            SizeBytes = (ulong)vssFile.SizeBytes,
                            Category  = vssFile.Category,
                            FullPath  = $"Snapshot {latest.CreationTime:dd/MM}\\{vssFile.OriginalPath}",
                            RecoverabilityPercent = 100.0
                        };
                        full.Add(nf);
                        filtered.Add(nf);
                    }
                    return (full, filtered);
                });

                // Adicionar em lote
                FullList.AddRange(tempFullList);

                if (tempFilteredList.Count > 40)
                {
                    FilteredList = new ObservableCollection<VoltrisOptimizer.Services.Recovery.NativeRecoveredFile>(tempFilteredList);
                }
                else
                {
                    foreach (var nf in tempFilteredList)
                    {
                        FilteredList.Add(nf);
                    }
                }

                App.LoggingService?.LogInfo($"[Recovery] VSS scan completed successfully. Found: {deletedFiles.Count} files.");
                StatusMessage = string.Format(LocalizationService.Instance.GetString("VssScanComplete"), deletedFiles.Count);
                progressToken.Complete(string.Format(LocalizationService.Instance.GetString("FilesFound"), deletedFiles.Count));
                App.LoggingService?.LogInfo($"[Recovery] VSS scan completed, found {deletedFiles.Count} deleted files.");
                GlobalNotificationService.ShowSuccess(LocalizationService.Instance.GetString("VssRecovery"), string.Format(LocalizationService.Instance.GetString("VssFilesFoundViaShadowCopy"), deletedFiles.Count));
                App.LoggingService?.LogInfo($"[Recovery] VSS scan completed, reported {deletedFiles.Count} deleted files.");
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[Recovery] VSS scan error: {ex.Message}", ex);
                StatusMessage = string.Format(LocalizationService.Instance.GetString("VssError"), ex.Message);
                progressToken.Complete(LocalizationService.Instance.GetString("Error"));
                GlobalNotificationService.ShowError(LocalizationService.Instance.GetString("VssRecovery"), string.Format(LocalizationService.Instance.GetString("VssScanErrorDetails"), ex.Message));
            }
            finally
            {
                IsScanning = false;
                progressToken.Dispose();
                App.LoggingService?.LogInfo($"[Recovery] RunVssScan: exit in {sw.ElapsedMilliseconds}ms.");
            }
        }

        private async Task RunScan(bool isDeepScan)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            App.LoggingService?.LogInfo($"[Recovery] Starting {(isDeepScan ? "deep" : "quick")} scan on drive: {SelectedDrive}");
            var progressToken = VoltrisOptimizer.Services.GlobalProgressService.Instance.BeginOperation(
                isDeepScan ? LocalizationService.Instance.GetString("DeepScan") : LocalizationService.Instance.GetString("QuickScan"), isPriority: true);

            try
            {
                StatusMessage = LocalizationService.Instance.GetString("InitializingRecoveryEngine");
                bool initSuccess = await Task.Run(() => _recoveryService.InitializeDrive(SelectedDrive));

                if (!initSuccess)
                {
                    App.LoggingService?.LogError($"[Recovery] Failed to initialize disk access on drive: {SelectedDrive}");
                    StatusMessage = LocalizationService.Instance.GetString("DriveAccessError");
                    progressToken.Complete(LocalizationService.Instance.GetString("AccessFailed"));
                    return;
                }

                IsScanning = true;
                ScanProgress = 0;
                StatusMessage = LocalizationService.Instance.GetString("AnalyzingDisk");
                FullList.Clear();
                FilteredList.Clear();

                var progressLock = new object();
                var lastProgressTime = DateTime.MinValue;
                var lastLogTime = DateTime.MinValue;

                bool hasSent100Log = false;
                bool hasSent100Progress = false;

                await _recoveryService.WaitForCompletionAsync((pct, fileCount) =>
                {
                    lock (progressLock)
                    {
                        var now = DateTime.UtcNow;

                        // Throttle logging to at most once per 500ms
                        if ((now - lastLogTime).TotalMilliseconds >= 500 || (pct >= 100 && !hasSent100Log))
                        {
                            if (pct >= 100) hasSent100Log = true;
                            lastLogTime = now;
                            App.LoggingService?.LogInfo($"[Recovery] Scan progress: {pct}% ({fileCount} arquivos identificados)");
                        }

                        // Throttle UI updates to at most once per 200ms
                        if ((now - lastProgressTime).TotalMilliseconds >= 200 || (pct >= 100 && !hasSent100Progress))
                        {
                            if (pct >= 100) hasSent100Progress = true;
                            lastProgressTime = now;
                            Application.Current?.Dispatcher.BeginInvoke(() =>
                            {
                                ScanProgress = pct;
                                StatusMessage = string.Format(LocalizationService.Instance.GetString("AnalyzingSectorsObjects"), fileCount);
                                progressToken.UpdateProgress((int)pct, StatusMessage);
                            });
                        }
                    }
                }, isDeepScan);

                await UpdateFoundFilesListAsync();
                App.LoggingService?.LogInfo($"[Recovery] Scan completed successfully. Identified: {FullList.Count} recoverable files.");
                StatusMessage = LocalizationService.Instance.GetString("ScanCompleted");
                progressToken.Complete(LocalizationService.Instance.GetString("ScanCompletedShort"));
                GlobalNotificationService.ShowSuccess(LocalizationService.Instance.GetString("Recovery"), string.Format(LocalizationService.Instance.GetString("ScanCompleteFilesIdentified"), FullList.Count));
                App.LoggingService?.LogInfo($"[Recovery] Scan completed successfully, {FullList.Count} recoverable files identified.");
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[Recovery] Scan error: {ex.Message}", ex);
                StatusMessage = string.Format(LocalizationService.Instance.GetString("ErrorWithDetails"), ex.Message);
                progressToken.Complete(LocalizationService.Instance.GetString("Error"));
                GlobalNotificationService.ShowError(LocalizationService.Instance.GetString("Recovery"), string.Format(LocalizationService.Instance.GetString("ScanErrorDetails"), ex.Message));
            }
            finally
            {
                IsScanning = false;
                progressToken.Dispose();
                App.LoggingService?.LogInfo($"[Recovery] RunScan: exit in {sw.ElapsedMilliseconds}ms.");
            }
        }

        private async Task UpdateFoundFilesListAsync()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            App.LoggingService?.LogInfo("[Recovery] UpdateFoundFilesListAsync: entry.");
            var allFiles = await Task.Run(() => _recoveryService.GetFoundFiles());
            if (allFiles.Count == FullList.Count)
            {
                App.LoggingService?.LogInfo($"[Recovery] UpdateFoundFilesListAsync: no new files (count={FullList.Count}), exit in {sw.ElapsedMilliseconds}ms.");
                return;
            }

            int currentCount = FullList.Count;
            bool applyFilterNow = !string.IsNullOrWhiteSpace(SearchQuery);
            var query = SearchQuery?.ToLowerInvariant() ?? "";

            var (newFullItems, newFilteredItems) = await Task.Run(() =>
            {
                var full = new List<VoltrisOptimizer.Services.Recovery.NativeRecoveredFile>(allFiles.Count - currentCount);
                var filtered = new List<VoltrisOptimizer.Services.Recovery.NativeRecoveredFile>();

                for (int i = currentCount; i < allFiles.Count; i++)
                {
                    var file = allFiles[i];
                    full.Add(file);
                    
                    if (applyFilterNow)
                    {
                        if (MatchesSearch(file, query))
                            filtered.Add(file);
                    }
                    else
                    {
                        filtered.Add(file);
                    }
                }
                return (full, filtered);
            });

            // Otimização de lote (batch): Adiciona os itens à FullList
            FullList.AddRange(newFullItems);

            // O seletor de tipo e montado AQUI, e nao durante o$GetFoundFiles, porque so aqui ja sabemos o total real e podemos contar por tipo. E antes de popular a lista visivel, para que o filtro ja entre em vigor no primeiro paint.
            RebuildTypeOptions();

            // O filtro de tipo tambem vale no carregamento inicial, e nao so quando o usuario mexe no combo.
            var typeSel = SelectedTypeOption;
            if (typeSel != null && typeSel.Key.Length > 0)
            {
                newFilteredItems = newFilteredItems
                    .Where(x => UI.Models.FileTypeOptionBuilder.Matches(x, typeSel))
                    .ToList();
            }

            // Otimização de lote (batch): Atualiza a FilteredList minimizando notificações à UI
            if (newFilteredItems.Count > 0)
            {
                if (newFilteredItems.Count > 40)
                {
                    // Evita disparar dezenas/centenas de eventos individuais de alteração na coleção
                    var combined = new List<VoltrisOptimizer.Services.Recovery.NativeRecoveredFile>(FilteredList);
                    combined.AddRange(newFilteredItems);

                    // WPF DataGrid trava a UI com muitos itens, mesmo com
                    // Virtualização ativa, por causa das colunas com
                    // Width="*". Ver a nota de PageSize.
                    //
                    // O que NÃO fazemos mais é esconder o resultado: o
                    // rodapé informa o total real e a busca alcança a lista
                    // inteira, então nenhum arquivo encontrado fica
                    // inalcançável.
                    if (combined.Count > PageSize)
                    {
                        combined = combined.Take(PageSize).ToList();
                    }

                    FilteredList = new ObservableCollection<VoltrisOptimizer.Services.Recovery.NativeRecoveredFile>(combined);
                }
                else
                {
                    foreach (var match in newFilteredItems)
                    {
                        FilteredList.Add(match);
                    }
                }
            }

            OnPropertyChanged(nameof(SearchStats));
            App.LoggingService?.LogInfo($"[Recovery] UpdateFoundFilesListAsync: completed, added {newFullItems.Count} items, total={FullList.Count}, elapsed={sw.ElapsedMilliseconds}ms.");
        }

        private void CancelScan()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            App.LoggingService?.LogInfo("[Recovery] CancelScan: entry, cancellation requested by user.");
            _recoveryService.StopScan();
            Application.Current.Dispatcher.BeginInvoke(() => 
            {
                StatusMessage = LocalizationService.Instance.GetString("ScanStopped");
                IsScanning = false;
                App.LoggingService?.LogInfo($"[Recovery] CancelScan: completed in {sw.ElapsedMilliseconds}ms.");
            });
        }

        private async Task RecoverSelectedFile()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            App.LoggingService?.LogInfo("[Recovery] RecoverSelectedFile: entry.");
            if (SelectedFile == null)
            {
                App.LoggingService?.LogWarning("[Recovery] RecoverSelectedFile: SelectedFile is null, aborting.");
                return;
            }
            App.LoggingService?.LogInfo($"[Recovery] RecoverSelectedFile: recovering file \"{SelectedFile.Filename}\" (id={SelectedFile.Id}).");

            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = LocalizationService.Instance.GetString("SelectDestinationFolder");
                dialog.UseDescriptionForTitle = true;
                dialog.ShowNewFolderButton = true;

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    string targetDir = dialog.SelectedPath;
                    IsScanning = true;
                    StatusMessage = LocalizationService.Instance.GetString("RestoringFile");

                    var progressToken = VoltrisOptimizer.Services.GlobalProgressService.Instance.BeginOperation($"Restaurando {SelectedFile.Filename}", isPriority: true);

                    try
                    {
                        string extension = SelectedFile.Extension.StartsWith(".") ? SelectedFile.Extension : "." + SelectedFile.Extension;
                        string finalPath = Path.Combine(targetDir, SelectedFile.Filename);
                        if (!finalPath.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                            finalPath += extension;

                        // ✅ Lógica Profissional de Não-Sobrescrita
                        int count = 1;
                        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(finalPath);
                        string dir = Path.GetDirectoryName(finalPath)!;

                        while (File.Exists(finalPath))
                        {
                            finalPath = Path.Combine(dir, $"{fileNameWithoutExt} ({count++}){extension}");
                        }

                        progressToken.UpdateProgress(50, LocalizationService.Instance.GetString("CopyingData"));
                        
                        bool success = await Task.Run(() => _recoveryService.RecoverFile(SelectedFile.Id, finalPath));

                        if (success)
                        {
                            StatusMessage = string.Format(LocalizationService.Instance.GetString("FileSavedAs"), Path.GetFileName(finalPath));
                            progressToken.Complete(LocalizationService.Instance.GetString("Restored"));
                            GlobalNotificationService.ShowSuccess(LocalizationService.Instance.GetString("FileRestored"), string.Format(LocalizationService.Instance.GetString("FileSavedIn"), SelectedFile.Filename, targetDir));
                            App.LoggingService?.LogInfo($"[Recovery] File restored: {SelectedFile.Filename} -> {finalPath}");
                        }
                        else
                        {
                            StatusMessage = LocalizationService.Instance.GetString("ExtractionFailedSectors");
                            progressToken.Complete(LocalizationService.Instance.GetString("Failed"));
                            GlobalNotificationService.ShowWarning(LocalizationService.Instance.GetString("Recovery"), LocalizationService.Instance.GetString("ExtractionFailedCorrupted"));
                        }
                    }
                    catch (Exception ex)
                    {
                        App.LoggingService?.LogError($"[Recovery] RecoverSelectedFile error: {ex.Message}", ex);
                        StatusMessage = string.Format(LocalizationService.Instance.GetString("ErrorWithDetails"), ex.Message);
                        progressToken.Complete(LocalizationService.Instance.GetString("CriticalError"));
                    }
                    finally
                    {
                        IsScanning = false;
                        progressToken.Dispose();
                    }
                }
            }
            App.LoggingService?.LogInfo($"[Recovery] RecoverSelectedFile: exit in {sw.ElapsedMilliseconds}ms.");
        }
    }
}
