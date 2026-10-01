using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Recovery
{
    public enum FileCategory
    {
        Images,
        Videos,
        Documents,
        Music,
        Archives,
        System,
        Others
    }

    /// <summary>
    /// Nível de confiança no ARQUIVO, não no caminho.
    ///
    /// A distinção importa porque são duas perguntas diferentes e o usuário
    /// não deve misturá-las: "os bytes que eu vou copiar são mesmo este
    /// arquivo?" e "era este o caminho onde ele morava?".
    ///
    /// recover_file faz uma cópia CONTÍNUA a partir de Offset. Isso significa
    /// que a confiança nos BYTES depende de duas coisas independentes:
    ///   1. a assinatura do formato bater com a extensão  -> prova de conteúdo
    ///   2. o arquivo ter UM único run                   -> sem buracos na cópia
    /// Um arquivo fragmentado é recuperado incompleto, mesmo com assinatura
    /// perfeita — o conteúdo é dele, mas faltam pedaços.
    /// </summary>
    public enum RecoveryConfidence
    {
        /// <summary>Bytes dentro do próprio registro MFT: íntegros por construção.</summary>
        Integral = 0,
        /// <summary>Assinatura confere e é contíguo: o melhor caso.</summary>
        Confirmado = 1,
        /// <summary>Assinatura confere, mas fragmentado: a cópia sai com buracos.</summary>
        Parcial = 2,
        /// <summary>Formato sem magic number, contíguo: nome plausível, conteúdo não provado.</summary>
        Provavel = 3,
        /// <summary>Sem magic number e fragmentado: nome plausível e cópia incompleta.</summary>
        Incerto = 4,

        /// <summary>
        /// Registro MFT intacto, mas o conteudo foi sobrescrito ou zerado pelo
        /// TRIM. O nome e o tamanho estao certos; os bytes nao existem mais.
        /// Extrair devolve um arquivo vazio ou com residuo do inicio.
        /// </summary>
        EstruturaApenas = 5
    }

    public class NativeRecoveredFile
    {
        public ulong Id { get; set; }
        public string Filename { get; set; } = string.Empty;
        public string Extension { get; set; } = string.Empty;
        public ulong SizeBytes { get; set; }
        public string SizeDisplay => FormatSize(SizeBytes);
        public double RecoverabilityPercent { get; set; }
        public ulong ParentId { get; set; }
        public FileCategory Category { get; set; }
        
        public string CategoryDisplay
        {
            get
            {
                var lang = VoltrisOptimizer.Services.LocalizationService.Instance.CurrentLanguage;
                switch (Category)
                {
                    case FileCategory.Images: return lang == VoltrisOptimizer.Services.Language.English ? "Images" : (lang == VoltrisOptimizer.Services.Language.Spanish ? "Imágenes" : "Imagens");
                    case FileCategory.Videos: return lang == VoltrisOptimizer.Services.Language.English ? "Videos" : (lang == VoltrisOptimizer.Services.Language.Spanish ? "Videos" : "Vídeos");
                    case FileCategory.Documents: return lang == VoltrisOptimizer.Services.Language.English ? "Documents" : (lang == VoltrisOptimizer.Services.Language.Spanish ? "Documentos" : "Documentos");
                    case FileCategory.Music: return lang == VoltrisOptimizer.Services.Language.English ? "Music" : (lang == VoltrisOptimizer.Services.Language.Spanish ? "Música" : "Música");
                    case FileCategory.Archives: return lang == VoltrisOptimizer.Services.Language.English ? "Archives" : (lang == VoltrisOptimizer.Services.Language.Spanish ? "Archivos" : "Arquivos");
                    case FileCategory.System: return lang == VoltrisOptimizer.Services.Language.English ? "System" : (lang == VoltrisOptimizer.Services.Language.Spanish ? "Sistema" : "Sistema");
                    default: return lang == VoltrisOptimizer.Services.Language.English ? "Others" : (lang == VoltrisOptimizer.Services.Language.Spanish ? "Otros" : "Outros");
                }
            }
        }
        public string FullPath { get; set; } = string.Empty;

        /// <summary>Nº de runs da $DATA. 1 = contíguo (recuperação direta).</summary>
        public uint RunCount { get; set; }

        /// <summary>Conteúdo dentro do registro MFT (arquivo pequeno).</summary>
        public bool IsResident { get; set; }

        /// <summary>A assinatura do formato confere com a extensão.</summary>
        public bool IsSignatureVerified { get; set; }

        /// <summary>Formato sem magic number — validado por nome e tamanho.</summary>
        public bool IsTextLike { get; set; }

        /// <summary>
        /// Veio da lixeira: o registro é um <c>$R&lt;id&gt;&lt;ext&gt;</c> e o
        /// nome/caminho verdadeiros vieram do <c>$I&lt;id&gt;</c> pareado.
        /// </summary>
        public bool IsFromRecycleBin { get; set; }

        /// <summary>
        /// O caminho foi PROVADO — cada degrau conferido pelo número de
        /// sequência da referência MFT, ou reconstruído pelo rewind do USN
        /// Journal. Falso significa que a pasta-mãe já foi reatribuída no
        /// volume e não há como saber onde o arquivo morava.
        /// </summary>
        public bool HasVerifiedPath { get; set; }

        /// <summary>
        /// O caminho veio do rewind do USN Journal, e não da MFT. Vale mais
        /// que o da MFT: a pasta já não existe no volume, mas o journal
        /// guardou a árvore como ela era.
        /// </summary>
        public bool PathFromUsnJournal { get; set; }

        /// <summary>
        /// O arquivo está fragmentado. IMPORTANTE: o recover_file atual faz
        /// uma cópia CONTÍNUA a partir de <see cref="SizeBytes"/>, então um
        /// arquivo com mais de um run é reconstruído de forma incompleta —
        /// os buracos entre os runs simplesmente não entram na cópia. Por
        /// isso a recuperabilidade desses itens é baixa, e a interface os
        /// mostra por último.
        /// </summary>
        public bool IsFragmented => RunCount > 1;

        /// <summary>Nível de confiança nos bytes. Ver a documentação do enum.</summary>
        public RecoveryConfidence Confidence
        {
            get
            {
                // Conteudo sobrescrito tem precedencia sobre tudo: nenhum outro
                // fator muda o fato de que os bytes nao estao la.
                if (ContentGone) return RecoveryConfidence.EstruturaApenas;
                if (IsResident) return RecoveryConfidence.Integral;
                if (IsSignatureVerified && !IsFragmented) return RecoveryConfidence.Confirmado;
                if (IsSignatureVerified) return RecoveryConfidence.Parcial;
                if (!IsFragmented) return RecoveryConfidence.Provavel;
                return RecoveryConfidence.Incerto;
            }
        }

        /// <summary>Rank numérico do nível, para ordenação (menor = melhor).</summary>
        public int ConfidenceRank => (int)Confidence;

        /// <summary>Rótulo curto para a coluna de confiança.</summary>
        public string ConfidenceLabel
        {
            get
            {
                switch (Confidence)
                {
                    case RecoveryConfidence.Integral: return "Integral";
                    case RecoveryConfidence.Confirmado: return "Confirmado";
                    case RecoveryConfidence.Parcial: return "Parcial";
                    case RecoveryConfidence.Provavel: return "Provável";
                    case RecoveryConfidence.EstruturaApenas:
                        return "so a estrutura";
                    default: return "Incerto";
                }
            }
        }

        /// <summary>
        /// Explica POR QUE o item está naquele nível. O rótulo sozinho diz o
        /// veredito; o usuário precisa do motivo para decidir se vale
        /// Extrair.
        /// </summary>
        public string ConfidenceNote
        {
            get
            {
                switch (Confidence)
                {
                    case RecoveryConfidence.Integral:
                        return "conteúdo dentro do registro MFT";
                    case RecoveryConfidence.Confirmado:
                        return "assinatura OK · contíguo";
                    case RecoveryConfidence.Parcial:
                        return "assinatura OK · fragmentado (copia com buracos)";
                    case RecoveryConfidence.Provavel:
                        return "formato sem magic number · contíguo";
                    case RecoveryConfidence.EstruturaApenas:
                        return "registro ok, conteudo sobrescrito (TRIM ou reatribuicao)";
                    default:
                        return "sem magic number · fragmentado";
                }
            }
        }

        /// <summary>
        /// Marcador do caminho. Separado de <see cref="FullPath"/> porque o
        /// caminho e a confiança são coisas diferentes: um item pode ter
        /// conteúdo provado e caminho unknowable, e vice-versa.
        /// </summary>
        public string PathNote
        {
            get
            {
                if (PathFromUsnJournal) return "caminho do USN Journal";
                if (HasVerifiedPath) return "caminho verificado";
                return "caminho desconhecido";
            }
        }

        /// <summary>
        /// Ícone real do Windows para este tipo de arquivo, resolvido pelo
        /// shell com SHGFI_USEFILEATTRIBUTES (nenhum acesso ao disco, porque
        /// o arquivo está apagado). Null = o shell não soube, e a UI usa o
        /// fallback em vez de mostrar um ícone errado.
        /// </summary>
        public System.Windows.Media.ImageSource? Icon { get; set; }

        /// <summary>
        /// Item e uma PASTA esvaziada da lixeira. Nao ha bytes para extrair, entao
        /// a interface mostra o nome e esconde o botao de recuperar. Os
        /// arquivos que estavam dentro dela aparecem na lista individualmente,
        /// com os registros MFT proprios.
        /// </summary>
        public bool IsRecycleFolder { get; set; }

        /// <summary>
        /// Registro intacto mas conteudo sobrescrito/zerado. Entra na lista so
        /// com o filtro 'so a estrutura' ligado.
        /// </summary>
        public bool ContentGone { get; set; }

        /// <summary>Texto do tooltip do icone: o tipo real, ou o aviso de pasta.</summary>
        public string IconTip => IsRecycleFolder
            ? "(pasta esvaziada da lixa - sem conteudo proprio)"
            : (string.IsNullOrEmpty(Extension) ? "arquivo sem extensao" : Extension.TrimStart('.').ToUpperInvariant() + "");



        private static string FormatSize(ulong bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F2} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }
    }

    public class DataRecoveryService
    {
        private ProgressCallbackDelegate? _activeCallback;
        private Action<double, uint>? _progressReporter;
        private volatile bool _isStopped;
        
        public DataRecoveryService()
        {
            var sw = Stopwatch.StartNew();
            App.LoggingService?.LogInfo("[DataRecoveryService] Constructor called.");
            // Initialize the correct architecture DLL
            try
            {
                System.Diagnostics.Debug.WriteLine("[DataRecoveryService] Inicializando arquitetura hibrida pra PInvoke...");
                App.LoggingService?.LogInfo("[DataRecoveryService] Inicializando arquitetura hibrida pra PInvoke...");
                RecoveryEngineInterop.InitializeNativeLibrary();
                System.Diagnostics.Debug.WriteLine("[DataRecoveryService] DLL Carregada com sucesso em memoria.");
                App.LoggingService?.LogInfo("[DataRecoveryService] DLL Carregada com sucesso em memoria.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DataRecoveryService] Não foi possível encontrar a DLL nativa: {ex.Message}");
                App.LoggingService?.LogError($"[DataRecoveryService] Não foi possível encontrar a DLL nativa: {ex.Message}", ex);
            }
            App.LoggingService?.LogInfo($"[DataRecoveryService] Constructor finished in {sw.ElapsedMilliseconds}ms.");
        }

        private void OnNativeProgress(double percentage, ulong currentSector, ulong totalSectors, uint filesFound)
        {
            App.LoggingService?.LogInfo($"[DataRecoveryService] OnNativeProgress: {percentage:F2}%, sector {currentSector}/{totalSectors}, files {filesFound}.");
            _progressReporter?.Invoke(percentage, filesFound);
        }

        /// <summary>
        /// Initializes the native scan engine for the specified drive letter.
        /// </summary>
        /// <param name="driveLetter">The drive letter to scan (e.g., "C:").</param>
        /// <returns>True if the drive was successfully initialized; otherwise false.</returns>
        public bool InitializeDrive(string driveLetter)
        {
            var sw = Stopwatch.StartNew();
            App.LoggingService?.LogInfo($"[DataRecoveryService] InitializeDrive chamado com driveLetter='{driveLetter}'.");
            try
            {
                if (!RecoveryEngineInterop.IsEngineCompatible)
                {
                    // A engine nao foi carregada, ou e de outra geracao. Sem
                    // esta checagem, o P/Invoke abaixo lancaria
                    // DllNotFoundException/EntryPointNotFoundException e o
                    // usuario veria "erro generico" em vez de "engine
                    // incompativel".
                    App.LoggingService?.LogError(
                        "[DataRecoveryService] Engine nativa ausente ou incompativel — a varredura nao pode iniciar. " +
                        "Veja [RecoveryEngineInterop] no log.");
                    return false;
                }

                string rawDrivePath = $@"\\.\{driveLetter.TrimEnd('\\')}";
                Debug.WriteLine($"[DataRecoveryService] Tentando acessar disco bruto: {rawDrivePath}");
                App.LoggingService?.LogInfo($"[DataRecoveryService] Chamando init_scan(\"{rawDrivePath}\")...");

                int result = RecoveryEngineInterop.init_scan(rawDrivePath);

                App.LoggingService?.LogInfo($"[DataRecoveryService] init_scan retornou {result} em {sw.ElapsedMilliseconds}ms.");

                if (result != 1)
                {
                    // O motivo esta no log nativo. Espelha aqui para o log
                    // gerenciado ser autossuficiente.
                    MirrorNativeLog($"init_scan(\"{rawDrivePath}\") retornou 0");
                    App.LoggingService?.LogError(
                        $"[DataRecoveryService] Nao foi possivel abrir o volume {driveLetter} em modo raw. " +
                        "Causas provaveis: (1) app sem privilegios de Administrador; " +
                        "(2) volume nao e NTFS; (3) handle nao concedido pelo sistema. " +
                        "O motivo nativo esta logo acima.");
                    return false;
                }

                App.LoggingService?.LogInfo("[DataRecoveryService] Volume aberto com sucesso.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DataRecoveryService] Falha critica no acesso C#: {ex.Message}");
                App.LoggingService?.LogError($"[DataRecoveryService] Falha critica no acesso C#: {ex.Message}", ex);
                MirrorNativeLog("excecao no acesso C#");
                return false;
            }
        }

        /// <summary>
        /// Stops the currently running scan asynchronously.
        /// </summary>
        public void StopScan()
        {
            var sw = Stopwatch.StartNew();
            App.LoggingService?.LogInfo("[DataRecoveryService] StopScan called.");
            try
            {
                _isStopped = true;
                System.Diagnostics.Debug.WriteLine("[DataRecoveryService] Parando scan por comando C# (Assíncrono)...");
                App.LoggingService?.LogInfo("[DataRecoveryService] Parando scan por comando C# (Assíncrono)...");
                
                // Roda o stop_scan em uma thread separada para evitar que trave a UI (Deadlock) caso a DLL nativa demore ou engasgue.
                Task.Run(() => 
                {
                    var innerSw = Stopwatch.StartNew();
                    App.LoggingService?.LogInfo("[DataRecoveryService] StopScan background task started.");
                    try 
                    {
                        RecoveryEngineInterop.stop_scan();
                        System.Diagnostics.Debug.WriteLine("[DataRecoveryService] Comando de stop enviado com sucesso.");
                        App.LoggingService?.LogInfo($"[DataRecoveryService] stop_scan executed successfully in {innerSw.ElapsedMilliseconds}ms.");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[DataRecoveryService] Erro ao parar scan nativo: {ex.Message}");
                        App.LoggingService?.LogError($"[DataRecoveryService] Erro ao parar scan nativo: {ex.Message}", ex);
                    }
                });
                App.LoggingService?.LogInfo($"[DataRecoveryService] StopScan returned in {sw.ElapsedMilliseconds}ms.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DataRecoveryService] StopScan outer: {ex.Message}");
                App.LoggingService?.LogWarning($"[DataRecoveryService] StopScan outer exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Starts a scan (quick or deep) and waits for completion, returning found files.
        /// </summary>
        /// <param name="isDeepScan">If true, performs a deep scan; otherwise a quick scan.</param>
        /// <param name="onProgress">Callback for scan progress updates (percentage, files found).</param>
        /// <returns>A list of recovered files found during the scan.</returns>
        public async Task<List<NativeRecoveredFile>> StartScanAsync(bool isDeepScan, Action<double, uint> onProgress)
        {
            var sw = Stopwatch.StartNew();
            App.LoggingService?.LogInfo($"[DataRecoveryService] StartScanAsync called (isDeepScan={isDeepScan}).");

            var result = await Task.Run(async () =>
            {
                var innerSw = Stopwatch.StartNew();
                App.LoggingService?.LogInfo("[DataRecoveryService] StartScanAsync background task started.");
                _isStopped = false;
                _progressReporter = onProgress;
                
                // Keep reference to prevent GC from collecting the callback delegate while unmanaged code calls it
                _activeCallback = new ProgressCallbackDelegate(OnNativeProgress);
                IntPtr fnPointer = Marshal.GetFunctionPointerForDelegate(_activeCallback);

                int started = isDeepScan 
                    ? RecoveryEngineInterop.start_deep_scan(fnPointer)
                    : RecoveryEngineInterop.start_quick_scan(fnPointer);

                if (started == 0)
                {
                    App.LoggingService?.LogWarning($"[DataRecoveryService] Scan failed to start (started={started}).");
                    return new List<NativeRecoveredFile>(); // Scan failed to start
                }

                App.LoggingService?.LogInfo($"[DataRecoveryService] Scan started successfully (started={started}).");

                // Wait loop for asynchronous native execution (Simplified)
                bool completed = false;
                _progressReporter += (pct, count) => { if (pct >= 100 || _isStopped) completed = true; };

                while (!completed && !_isStopped)
                {
                    // CORREÇÃO: Usando await Task.Delay(500) dentro de lambda async,
                    // liberando a thread do pool durante a espera (evita starvation).
                    await Task.Delay(500);
                }

                App.LoggingService?.LogInfo($"[DataRecoveryService] Scan loop exited (completed={completed}, _isStopped={_isStopped}).");

                var files = GetFoundFiles();
                App.LoggingService?.LogInfo($"[DataRecoveryService] StartScanAsync background finished in {innerSw.ElapsedMilliseconds}ms, found {files.Count} files.");
                return files;
            });

            App.LoggingService?.LogInfo($"[DataRecoveryService] StartScanAsync completed in {sw.ElapsedMilliseconds}ms, found {result.Count} files.");
            return result;
        }

        /// <summary>
        /// Waits asynchronously for a scan to complete, reporting progress via callback.
        /// </summary>
        /// <param name="onProgress">Callback for scan progress updates (percentage, files found).</param>
        /// <param name="isDeepScan">If true, performs a deep scan; otherwise a quick scan.</param>
        public async Task WaitForCompletionAsync(Action<double, uint> onProgress, bool isDeepScan)
        {
            var sw = Stopwatch.StartNew();
            App.LoggingService?.LogInfo($"[DataRecoveryService] WaitForCompletionAsync called (isDeepScan={isDeepScan}).");
            _isStopped = false;
            System.Diagnostics.Debug.WriteLine($"[DataRecoveryService] Entrando em rotina de espera Async (DeepScan: {isDeepScan})...");
            var tcs = new TaskCompletionSource<bool>();
            
            _activeCallback = new ProgressCallbackDelegate((pct, curSect, totSect, files) => {
                onProgress?.Invoke(pct, files);
                if (pct >= 100.0 || _isStopped) {
                    System.Diagnostics.Debug.WriteLine("[DataRecoveryService] Progresso nativo finalizado ou abortado.");
                    App.LoggingService?.LogInfo($"[DataRecoveryService] Native progress completed or aborted at {pct:F2}%.");
                    tcs.TrySetResult(true);
                }
            });
            
            IntPtr fnPointer = Marshal.GetFunctionPointerForDelegate(_activeCallback);
            System.Diagnostics.Debug.WriteLine($"[DataRecoveryService] Callback Pointer gerado: {fnPointer}");
            App.LoggingService?.LogInfo($"[DataRecoveryService] Callback Pointer generated: {fnPointer}");
            
            int started = await Task.Run(() => 
            {
                App.LoggingService?.LogInfo("[DataRecoveryService] Starting scan in background task for WaitForCompletionAsync...");
                return isDeepScan 
                    ? RecoveryEngineInterop.start_deep_scan(fnPointer)
                    : RecoveryEngineInterop.start_quick_scan(fnPointer);
            });
                
            if (started == 1)
            {
                System.Diagnostics.Debug.WriteLine("[DataRecoveryService] Scan disparado na DLL. Aguardando conclusao na Task...");
                App.LoggingService?.LogInfo("[DataRecoveryService] Scan started in DLL. Waiting for completion...");
                
                while (!tcs.Task.IsCompleted && !_isStopped)
                {
                    await Task.Delay(100);
                }
                
                tcs.TrySetResult(true);
                System.Diagnostics.Debug.WriteLine("[DataRecoveryService] Task liberada com sucesso.");
                App.LoggingService?.LogInfo($"[DataRecoveryService] WaitForCompletionAsync finished in {sw.ElapsedMilliseconds}ms.");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[DataRecoveryService] DLL retornou 0 no start_scan (Falhou em rodar).");
                App.LoggingService?.LogError($"[DataRecoveryService] DLL returned 0 for start_scan (failed to start).", null);
            }
        }

        /// <summary>
        /// Retrieves the list of files found during the last scan.
        /// </summary>
        /// <returns>A list of recovered files.</returns>
        public List<NativeRecoveredFile> GetFoundFiles()
        {
            var sw = Stopwatch.StartNew();
            App.LoggingService?.LogInfo("[DataRecoveryService] GetFoundFiles chamado.");
            try
            {
                int structSize = RecoveryEngineInterop.StructSize;
                if (structSize != RecoveryEngineInterop.ExpectedStructSize ||
                    !RecoveryEngineInterop.IsAbiConsistent)
                {
                    // Layout divergente entre C# e C++ = buffer lido é lixo.
                    // Melhor falhar alto e claro do que devolver 50.000 structs
                    // embaralhados sem o usuário perceber.
                    App.LoggingService?.LogError(
                        $"[DataRecoveryService] ABI DIVERGENTE: gerenciado={structSize}/{RecoveryEngineInterop.StatsStructSize}B, " +
                        $"nativo esperado={RecoveryEngineInterop.ExpectedStructSize}/{RecoveryEngineInterop.ExpectedStatsStructSize}B. " +
                        "A DLL nativa e o codigo gerenciado precisam ser recompilados juntos.");
                    return new List<NativeRecoveredFile>();
                }

                // Contagem exata, para o buffer ser do tamanho certo. A
                // versão anterior reservava 1.500.000 structs (mais de 400 MB
                // de memoria nao gerenciada) "por via das duvidas" — e mesmo
                // assim truncava a lista em 50.000.
                int count = RecoveryEngineInterop.get_found_count();
                LogFilterStats();
                App.LoggingService?.LogInfo($"[DataRecoveryService] Engine.native filtrou e aceitou {count} arquivos (struct={structSize}B).");

                if (count <= 0) return new List<NativeRecoveredFile>();

                int safeCount = Math.Min(count, 50000);
                if (count > safeCount)
                    App.LoggingService?.LogWarning(
                        $"[DataRecoveryService] {count} arquivos passaram o filtro; exibindo os {safeCount} de maior recuperabilidade.");

                IntPtr nativeBuffer = Marshal.AllocHGlobal(safeCount * structSize);
                var result = new List<NativeRecoveredFile>(safeCount);

                try
                {
                    int reported = RecoveryEngineInterop.get_found_files(nativeBuffer, safeCount);
                    int usable = Math.Min(reported, safeCount);

                    for (int i = 0; i < usable; i++)
                    {
                        IntPtr currentPtr = new IntPtr(nativeBuffer.ToInt64() + (i * structSize));
                        var nativeItem = Marshal.PtrToStructure<FoundFileNative>(currentPtr);

                        var flags = nativeItem.Flags;
                        var cat = nativeItem.CategoryFlags;

                        var file = new NativeRecoveredFile
                        {
                            Id = nativeItem.Id,
                            Filename = nativeItem.Filename,
                            Extension = (nativeItem.Extension ?? string.Empty).ToLowerInvariant(),
                            SizeBytes = nativeItem.Size,
                            RecoverabilityPercent = nativeItem.Recoverability,
                            ParentId = nativeItem.ParentId,
                            RunCount = nativeItem.RunCount,
                            IsResident = (flags & (uint)FoundFileFlags.Resident) != 0,
                            IsSignatureVerified = (flags & (uint)FoundFileFlags.SignatureVerified) != 0,
                            IsTextLike = (cat & (uint)FoundFileCategoryFlags.TextLike) != 0,
                            // Estes tres bits existem no enum e vinham do
                            // nativo sem nunca serem lidos. Sem eles a
                            // interface nao tem como dizer "veio da lixeira"
                            // nem "o caminho foi provado" — que sao justamente
                            // as duas coisas que o usuario mais precisa saber.
                            IsFromRecycleBin = (cat & (uint)FoundFileCategoryFlags.FromRecycleBin) != 0,
                            HasVerifiedPath = (cat & (uint)FoundFileCategoryFlags.PathVerified) != 0,
                            PathFromUsnJournal = (cat & (uint)FoundFileCategoryFlags.PathFromUsnJournal) != 0,
                            IsRecycleFolder = (cat & (uint)FoundFileCategoryFlags.IsFolder) != 0,
                            ContentGone = (cat & (uint)FoundFileCategoryFlags.ContentGone) != 0
                        };

                        file.Category = Categorize(file.Extension);

                        // Caminho REAL resolvido pela engine. Antes disto a
                        // interface mostrava "[Indice MFT 4821]\arquivo.docx",
                        // que nao ajuda ninguem a reconhecer o que recuperou.
                        //
                        // A engine sinaliza "nao achei este caminho" com uma
                        // mensagem entre colchetes. Tratar isso como caminho
                        // de verdade era pior que nao ter caminho: o usuario
                        // lia "[localizacao original nao recuperavel]" na
                        // coluna e nao sabia se era um caminho ou um aviso.
                        string original = nativeItem.OriginalPath;
                        bool looksLikePlaceholder =
                            string.IsNullOrWhiteSpace(original) ||
                            (original.Length > 0 && original[0] == '[');

                        if (!looksLikePlaceholder)
                        {
                            file.FullPath = original;
                        }
                        else
                        {
                            // Sem caminho, mas com o NOME — e o nome sozinho
                            // ja e o bastante para o usuario reconhecer e
                            // extrair o arquivo.
                            file.FullPath = file.Filename;
                        }

                        result.Add(file);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(nativeBuffer);
                }

                // Ordena pelo que o usuário quer primeiro: o que tem mais
                // chance de sair ÍNTEGRO por uma cópia contínua.
                //
                // A ordem anterior (recuperabilidade, depois assinatura) era
                // proxy ruim para isso: um arquivo sem magic number e
                // contíguo pontuava 58, e um fragmentado com assinatura
                // pontuava 62 — ou seja, o item cuja cópia sai com buracos
                // ficava ACIMA do item que sai inteiro e apenas não
                // comprovado. O nível de confiança ordena por evidência real,
                // e a recuperabilidade vira apenas o desempate.
                result = result
                    .OrderBy(f => f.ConfidenceRank)
                    .ThenByDescending(f => f.RecoverabilityPercent)
                    .ThenByDescending(f => f.IsSignatureVerified)
                    .ThenByDescending(f => f.SizeBytes)
                    .ToList();

                App.LoggingService?.LogInfo(
                    $"[DataRecoveryService] GetFoundFiles devolveu {result.Count} arquivos em {sw.ElapsedMilliseconds}ms.");

                // ── ÍCONES ────────────────────────────────────────────────
                // Resolvidos AQUI, uma vez por extensão (o cache é por
                // extensão), e não na UI: a lista pode ter milhares de linhas
                // e resolver ícone por linha dentro do binding travaria a
                // interface. O log sai por tipo, que é o que interessa para
                // conferir se o shell respondeu.
                var byExt = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in result)
                {
                    string ext = System.IO.Path.GetExtension(f.Filename);
                    if (string.IsNullOrEmpty(ext)) ext = "(sem extensao)";
                    byExt.TryGetValue(ext, out int c);
                    byExt[ext] = c + 1;
                }
                int iconOk = 0, iconFail = 0;
                foreach (var kv in byExt)
                {
                    var img = UI.Helpers.FileIconResolver.Resolve(
                        kv.Key == "(sem extensao)" ? "arquivo" : "arquivo" + kv.Key, false);
                    if (img != null) iconOk += kv.Value; else iconFail += kv.Value;
                    App.LoggingService?.LogInfo(
                        $"[ICONE] {kv.Key,-12} x{kv.Value,-6} -> {(img != null ? $"ok {((System.Windows.Media.Imaging.BitmapSource)img).PixelWidth}x{((System.Windows.Media.Imaging.BitmapSource)img).PixelHeight}" : "shell nao devolveu -> fallback")}");
                }
                foreach (var f in result)
                {
                    f.Icon = UI.Helpers.FileIconResolver.Resolve(f.Filename, f.IsRecycleFolder || f.ContentGone);
                }
                App.LoggingService?.LogInfo(
                    $"[ICONE] total: {iconOk} com icone nativo, {iconFail} sem (usa fallback).");
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DataRecoveryService] Erro ao obter arquivos: {ex.Message}");
                App.LoggingService?.LogError($"[DataRecoveryService] Erro ao obter arquivos: {ex.Message}", ex);
                return new List<NativeRecoveredFile>();
            }
        }

        /// <summary>
        /// Registra POR QUE a lista ficou do jeito que ficou.
        ///
        /// Sem isto, "a lista ficou curta" tem duas explicações indistinguíveis:
        /// o filtro funcionou, ou o parser quebrou. A segunda é Catástrofe
        /// silenciosa — o usuário só vê uma lista vazia e conclui que não
        /// havia nada para recuperar.
        /// </summary>
        /// <summary>
        /// Cola no log gerenciado as ÚLTIMAS LINHAS do log nativo.
        ///
        /// Antes, uma falha nativa aparecia no voltris.log.txt só como
        /// "init_scan returned: 0" e o motivo ficava em OUTRO arquivo
        /// (RecoveryEngine_Debug.log), aberto na mão. Duas fontes, um
        /// sintoma — e a ponte entre elas não existia. Aqui o motivo vem
        /// junto, prefixado com [NATIVO], para o log gerenciado ser
        /// autossuficiente.
        ///
        /// É best-effort por desenho: falhar ao ler o log do motor não pode
        /// derrubar a varredura.
        /// </summary>
        private static void MirrorNativeLog(string stage, int tailLines = 14)
        {
            try
            {
                string path = Path.Combine(LogDirectoryResolver.Resolve(), "RecoveryEngine_Debug.log");
                if (!File.Exists(path)) return;

                var lines = File.ReadAllLines(path);
                int from = Math.Max(0, lines.Length - tailLines);
                var recent = new List<string>(lines.Length - from);
                for (int i = from; i < lines.Length; i++) recent.Add(lines[i]);

                App.LoggingService?.LogWarning(
                    $"[DataRecoveryService] ===== MOTIVO NATIVO ({stage}) =====");
                foreach (var line in recent)
                    App.LoggingService?.LogWarning("  [NATIVO] " + line);
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogDebug(
                    $"[DataRecoveryService] nao foi possivel espelhar o log nativo: {ex.Message}");
            }
        }

        private static void LogFilterStats()
        {
            try
            {
                if (RecoveryEngineInterop.get_stats(out var s) == 0) return;

                App.LoggingService?.LogInfo(
                    "[DataRecoveryService] ==== FILTRO DA VARREDURA MFT ====\n" +
                    $"  registros lidos .............. {s.recordsRead}\n" +
                    $"  ignorados: registro invalido . {s.notMagic}\n" +
                    $"  ignorados: pasta ............. {s.isDirectory}\n" +
                    $"  ignorados: AINDA EXISTE ...... {s.stillAlive}\n" +
                    $"  ignorados: meta NTFS (idx<24) . {s.metaRecord}\n" +
                    $"  ignorados: nome de sistema ($)  {s.systemName}\n" +
                    $"  ignorados: temporario (~$...)  {s.tempName}\n" +
                    $"  ignorados: nome corrompido .... {s.invalidName}\n" +
                    $"  ignorados: na raiz do volume . {s.rootLevel}\n" +
                    $"  ignorados: pasta de sistema ... {s.systemFolder}\n" +
                    $"  ignorados: sem atributo DATA .. {s.noDataAttribute}\n" +
                    $"  ignorados: tamanho zero ....... {s.zeroSize}\n" +
                    $"  ignorados: tamanho absurdo .... {s.absurdSize}\n" +
                    $"  ignorados: extensao nao suportada {s.unknownExtension}\n" +
                    $"  ignorados: ASSINATURA NAO CONFERE {s.signatureMismatch}   <-- runlist obsoleta\n" +
                    $"  ignorados: conteudo ilegivel ... {s.signatureUnreadable}\n" +
                    $"  ACEITOS ...................... {s.accepted}");
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning(
                    $"[DataRecoveryService] Nao foi possivel ler as estatisticas do filtro: {ex.Message}");
            }
        }

        /// <summary>
        /// Classifica por extensão para o filtro da interface.
        ///
        /// SEM LOG POR CHAMADA, de propósito: a versão anterior escrevia uma
        /// linha por arquivo, ou seja, 50.000 linhas de log para uma única
        /// varredura — o que torna o log ilegível e adiciona latência de disco
        /// dentro do laço que monta a lista.
        /// </summary>
        private static FileCategory Categorize(string ext)
        {
            string e = (ext ?? string.Empty).TrimStart('.').ToLowerInvariant();
            if (e.Length == 0) return FileCategory.Others;

            return e switch
            {
                "jpg" or "jpeg" or "jpe" or "png" or "bmp" or "gif" or "tif" or "tiff" or "webp" or "ico" or "heic" or "psd"
                    => FileCategory.Images,
                "mp4" or "m4v" or "mkv" or "webm" or "avi" or "mov" or "wmv" or "flv" or "3gp"
                    => FileCategory.Videos,
                "pdf" or "docx" or "doc" or "xlsx" or "xls" or "txt" or "pptx" or "ppt" or "rtf" or "odt" or "ods" or "odp"
                    => FileCategory.Documents,
                "mp3" or "wav" or "flac" or "ogg" or "m4a"
                    => FileCategory.Music,
                "zip" or "rar" or "7z" or "tar" or "gz" or "tgz" or "bz2" or "cab"
                    => FileCategory.Archives,
                "exe" or "dll" or "sys" or "msi" or "bat" or "cmd" or "drv" or "ocx" or "cpl"
                    => FileCategory.System,
                _ => FileCategory.Others
            };
        }

        /// <summary>
        /// Recovers a file by its ID to the specified output path.
        /// </summary>
        /// <param name="fileId">The ID of the file to recover.</param>
        /// <param name="outputPath">The full destination path for the recovered file.</param>
        /// <returns>True if the file was recovered successfully; otherwise false.</returns>
        public bool RecoverFile(ulong fileId, string outputPath)
        {
            var sw = Stopwatch.StartNew();
            App.LoggingService?.LogInfo($"[DataRecoveryService] RecoverFile called (fileId={fileId}, outputPath='{outputPath}').");
            try
            {
                System.Diagnostics.Debug.WriteLine($"[DataRecoveryService] Solicitando recovery fisico do arquivo ID {fileId} => {outputPath}");
                App.LoggingService?.LogInfo($"[DataRecoveryService] Requesting physical recovery of file ID {fileId} => {outputPath}");
                int result = RecoveryEngineInterop.recover_file(fileId, outputPath);
                System.Diagnostics.Debug.WriteLine($"[DataRecoveryService] Resultado do Dump Fisico: {result}");
                App.LoggingService?.LogInfo($"[DataRecoveryService] Recovery result: {result}");
                App.LoggingService?.LogInfo($"[DataRecoveryService] RecoverFile finished in {sw.ElapsedMilliseconds}ms, success={result == 1}.");
                return result == 1;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DataRecoveryService] Erro ao recuperar via interop: {ex.Message}");
                App.LoggingService?.LogError($"[DataRecoveryService] Error recovering file {fileId}: {ex.Message}", ex);
                return false;
            }
        }
    }
}
