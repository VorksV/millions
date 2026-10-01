using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace VoltrisOptimizer.Services.Recovery
{
    /// <summary>Bits de <see cref="FoundFileNative.Flags"/>.</summary>
    [Flags]
    public enum FoundFileFlags
    {
        None = 0,
        /// <summary>$DATA com mais de um run — o arquivo está fragmentado.</summary>
        Fragmented = 0x1,

        /// <summary>Conteúdo dentro do próprio registro MFT (arquivo pequeno).</summary>
        Resident = 0x2,

        /// <summary>A assinatura do formato confere com a extensão.</summary>
        SignatureVerified = 0x4
    }

    /// <summary>Bits de <see cref="FoundFileNative.CategoryFlags"/>.</summary>
    [Flags]
    public enum FoundFileCategoryFlags
    {
        None = 0,
        Deleted = 0x1,
        Contiguous = 0x2,

        /// <summary>Formato sem magic number: a validação foi por nome e tamanho.</summary>
        TextLike = 0x4,

        /// <summary>O caminho original não coube no buffer de 512 bytes.</summary>
        Truncated = 0x8,

        /// <summary>
        /// Veio da lixeira: o registro é um <c>$R&lt;id&gt;&lt;ext&gt;</c> e o
        /// nome/caminho/tamanho verdadeiros vieram do <c>$I&lt;id&gt;</c>
        /// pareado. Sem este bit o item apareceria como
        /// <c>C:\$Recycle.Bin\S-1-5-21-...\$R7FS92DJ.tmp</c>, que não é um
        /// caminho de onde o usuário possa extrair o arquivo.
        /// </summary>
        FromRecycleBin = 0x10,

        /// <summary>
        /// Cada degrau da cadeia de pastas foi conferido pelo número de
        /// sequência da referência MFT e pelo bit de diretório. Ausente: a
        /// cadeia quebrou em algum ponto e o caminho exibido é só o trecho
        /// que deu para provar.
        /// </summary>
        PathVerified = 0x20,

        /// <summary>
        /// O caminho veio do rewind do USN Journal, e nao da cadeia de pais da
        /// MFT. Mais confiavel ainda: a pasta-mae ja foi reatribuida no volume,
        /// mas o journal guardou a arvore como ela era no momento da exclusao.
        /// </summary>
        PathFromUsnJournal = 0x40,

        /// <summary>
        /// Item e uma PASTA esvaziada da lixeira. Pasta nao ocupa clusters de
        /// dado, entao nao ha o que extrair — o que o usuario ganha e o NOME.
        /// A interface usa este bit para o icone de pasta e para esconder o
        /// botao de recuperar.
        /// </summary>
        IsFolder = 0x80,

        /// <summary>
        /// Registro MFT intacto (nome, tamanho e caminho corretos) mas os
        /// clusters ja nao contem o conteudo — arquivo apagado em SSD com TRIM,
        /// ou clusters reatribuidos. Hidden por padrao na lista.
        /// </summary>
        ContentGone = 0x100
    }

    /// <summary>
    /// ESPELHO EXATO de <c>FoundFile</c> em RecoveryEngine.h.
    ///
    /// A ORDEM E O TAMANHO DOS CAMPOS SÃO CONTRATO com o C++. A DLL empacota
    /// estes structs em buffer cru com <c>get_found_files</c>, e o C# lê com
    /// <c>Marshal.PtrToStructure</c>: qualquer divergência de layout não dá
    /// erro de compilação, dá CORRUPÇÃO DE MEMÓRIA em produção.
    ///
    /// O native tem um <c>static_assert(sizeof(FoundFile) == 848)</c> que
    /// garante o lado dele; o <see cref="StructSize"/> abaixo garante o nosso,
    /// e o log em GetFoundFiles imprime os dois para comparação.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct FoundFileNative
    {
        public ulong Id;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string Filename;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
        public string Extension;

        public ulong Size;
        public double Recoverability;
        public ulong Offset;
        public ulong ParentId;

        /// <summary>Caminho real resolvido (UTF-8). Vazio se não deu para resolver.</summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)]
        public string OriginalPath;

        /// <summary>Nº de runs da $DATA. 1 = contíguo, > 1 = fragmentado.</summary>
        public uint RunCount;

        public uint Flags;
        public uint CategoryFlags;
        public uint Reserved;

        public FoundFileFlags FlagSet => (FoundFileFlags)Flags;
        public FoundFileCategoryFlags CategorySet => (FoundFileCategoryFlags)CategoryFlags;
    }

    /// <summary>Espelho de <c>RecoveryStats</c> em RecoveryEngine.h.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RecoveryStatsNative
    {
        public ulong recordsRead;
        public ulong notMagic;
        public ulong isDirectory;
        public ulong stillAlive;
        public ulong metaRecord;
        public ulong systemName;
        public ulong tempName;
        public ulong invalidName;
        public ulong rootLevel;
        public ulong systemFolder;
        public ulong noDataAttribute;
        public ulong zeroSize;
        public ulong absurdSize;
        public ulong unknownExtension;
        public ulong signatureMismatch;
          public ulong signatureUnreadable;
          public ulong accepted;
          public ulong pathUnverified;
          public ulong pathFailMagic;
          public ulong pathFailSequence;
          public ulong pathFailNotDir;
          public ulong pathFailParse;
          public ulong pathFailNoRoot;
          public ulong pathSeqReused;
          public ulong pathSeqBackward;
          public ulong pathFromUsn;
          public ulong usnRecordsParsed;
          public ulong usnEntriesInMap;
          public ulong usnResolved;
          public ulong usnUnknown;
          public ulong recycleInfoFound;
          public ulong recycleRecovered;
          public ulong recycleOrphan;
          public ulong recycleNameSeen;
          public ulong recycleIdRejected;
          public ulong recycleFolderSeen;
          public ulong recycleParseFailed;
          public ulong recycleStillInBin;
          public ulong recycleFoldersNamed;
          public ulong recycleFoldersUnnamed;
          public ulong contentGone;
      }


    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void ProgressCallbackDelegate(double percentage, ulong currentSector, ulong totalSectors, uint filesFound);

    /// <summary>
    /// Wrapper profissional para a Engine nativa em C++
    /// Implementa arquitetura híbrida (carrega correta x86/x64)
    /// </summary>
    public static class RecoveryEngineInterop
    {
        /// <summary>
        /// Tamanho de <see cref="FoundFileNative"/>. Tem que bater com o
        /// static_assert do lado nativo (848). Se divergir, a DLL e o
        /// gerenciado estão com layouts diferentes e TODO o buffer lido é
        /// garbage.
        /// </summary>
        public static readonly int StructSize = Marshal.SizeOf<FoundFileNative>();

        public const int ExpectedStructSize = 848;

        /// <summary>
        /// Tamanho de <see cref="RecoveryStatsNative"/>. Mesmo contrato: o
        /// nativo escreve com <c>get_stats(RecoveryStats*)</c>, então divergir
        /// aqui também é corrupção de memória.
        /// </summary>
        public static readonly int StatsStructSize = Marshal.SizeOf<RecoveryStatsNative>();

        public const int ExpectedStatsStructSize = 328;

        /// <summary>
        /// Verdadeiro quando os dois lados concordam no layout. Se falso,
        /// nenhuma chamada que passe buffer para a DLL deve ser feita.
        /// </summary>
        public static bool IsAbiConsistent =>
            StructSize == ExpectedStructSize &&
            StatsStructSize == ExpectedStatsStructSize;

        // Path resolution for 32/64 bits environments
        // Note: The DLLs are exactly where they need to be in the RecoveryEngine root relative to output.
        private const string DllNameX64 = @"RecoveryEngine\recovery_engine_x64.dll";
        private const string DllNameX86 = @"RecoveryEngine\recovery_engine_x86.dll";

        private const string DllName = "recovery_engine_native.dll";

        [DllImport(DllName, EntryPoint = "get_abi_version", CallingConvention = CallingConvention.Cdecl)]
        public static extern int get_abi_version();

        [DllImport(DllName, EntryPoint = "init_scan", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int init_scan(string drivePath);

        [DllImport(DllName, EntryPoint = "stop_scan", CallingConvention = CallingConvention.Cdecl)]
        public static extern void stop_scan();

        [DllImport(DllName, EntryPoint = "start_quick_scan", CallingConvention = CallingConvention.Cdecl)]
        public static extern int start_quick_scan(IntPtr callback);

        [DllImport(DllName, EntryPoint = "start_deep_scan", CallingConvention = CallingConvention.Cdecl)]
        public static extern int start_deep_scan(IntPtr callback);

        [DllImport(DllName, EntryPoint = "get_found_files", CallingConvention = CallingConvention.Cdecl)]
        public static extern int get_found_files(IntPtr buffer, int maxCount);

        /// <summary>
        /// Quantos arquivos a varredura encontrou. Existe para o buffer ser
        /// alocado no tamanho exato.
        /// </summary>
        [DllImport(DllName, EntryPoint = "get_found_count", CallingConvention = CallingConvention.Cdecl)]
        public static extern int get_found_count();

        /// <summary>Por que cada candidato foi descartado. Diagnóstico do filtro.</summary>
        [DllImport(DllName, EntryPoint = "get_stats", CallingConvention = CallingConvention.Cdecl)]
        public static extern int get_stats(out RecoveryStatsNative stats);

        [DllImport(DllName, EntryPoint = "recover_file", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int recover_file(ulong fileId, string outputPath);

        /// <summary>
        /// Versão do ABI que este código gerenciado espera. Tem que bater com
        /// RECOVERY_ABI_VERSION em RecoveryEngine.h.
        /// </summary>
        /// <remarks>
        /// 0x0003 = lixeira. Acrescenta <c>CF_FROM_RECYCLE</c> e
        /// <c>CF_PATH_VERIFIED</c> em CategoryFlags, quatro contadores de
        /// lixeira em RecoveryStats (136 -&gt; 168 bytes) e troca a
        /// reconstrução de caminho por uma que confere o número de sequência
        /// da referência MFT. Subiu de geração porque a semântica do que é
        /// "caminho confiável" mudou — o tamanho da struct FoundFile não
        /// mudou, mas o que os bits significam sim.
        /// </remarks>
        public const int ExpectedAbiVersion = 0x0003;


        /// <summary>
        /// Verdadeiro quando a engine carregada é a mesma geração deste
        /// código. Falso significa: NAO CHAME nenhuma outra funcao desta
        /// classe.
        /// </summary>
        public static bool IsEngineCompatible { get; private set; }

        /// <summary>
        /// Prepara e carrega a engine nativa, e só marca como compatível se a
        /// versão do ABI bater.
        ///
        /// A checagem existe porque a assinatura das funções P/Invoke é a
        /// mesma entre gerações: uma DLL de abril e o código de hoje compilam,
        /// carregam, e devolvem structs lidas com o layout errado. Sem esta
        /// trava o sintoma é uma lista de arquivos corrompida — ou, pior, uma
        /// leitura fora dos limites do buffer.
        /// </summary>
        public static void InitializeNativeLibrary()
        {
            IsEngineCompatible = false;

            if (!IsAbiConsistent)
            {
                App.LoggingService?.LogError(
                    $"[RecoveryEngineInterop] ABI gerenciado divergente: struct={StructSize}/{ExpectedStructSize}B, " +
                    $"stats={StatsStructSize}/{ExpectedStatsStructSize}B. Esperado=0x{ExpectedAbiVersion:X4}.");
                return;
            }

            string relativePath = Environment.Is64BitProcess ? DllNameX64 : DllNameX86;
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            string fullPath = Path.Combine(basePath, relativePath);
            string destPath = Path.Combine(basePath, DllName);

            Debug.WriteLine("[RecoveryEngineInterop] Preparando DLL nativa...");
            App.LoggingService?.LogInfo(
                $"[RecoveryEngineInterop] Initializing native library (arch: {(Environment.Is64BitProcess ? "x64" : "x86")})");

            try
            {
                if (!File.Exists(fullPath))
                {
                    Debug.WriteLine($"[RecoveryEngineInterop] ERRO: Fonte nao encontrada: {fullPath}");
                    App.LoggingService?.LogError($"[RecoveryEngineInterop] Native DLL source not found: {fullPath}", null);
                    return;
                }

                // A cópia SEMPRE sobrescreve o recovery_engine_native.dll. Sem
                // isso, uma execução anterior do app deixa no disco a DLL
                // antiga — e se esta cópia falhar (arquivo em uso por outro
                // processo, antivírus em quarentena, diretório somente-leitura)
                // o LoadLibrary abaixo carregaria a engine errada.
                File.Copy(fullPath, destPath, true);
                Debug.WriteLine($"[RecoveryEngineInterop] DLL copiada para: {destPath}");
                App.LoggingService?.LogInfo($"[RecoveryEngineInterop] DLL copied to: {destPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RecoveryEngineInterop] Erro ao copiar DLL: {ex.Message}");
                App.LoggingService?.LogError("[RecoveryEngineInterop] Failed to copy native DLL", ex);
                return;
            }

            IntPtr hModule = LoadLibrary(destPath);
            if (hModule == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                Debug.WriteLine($"[RecoveryEngineInterop] Erro ao carregar DLL final: {error}");
                App.LoggingService?.LogError($"[RecoveryEngineInterop] Failed to load native DLL (error: {error})", null);
                return;
            }

            Debug.WriteLine("[RecoveryEngineInterop] DLL nativa carregada.");

            // ── O portão ──────────────────────────────────────────────
            int abi;
            try
            {
                abi = get_abi_version();
            }
            catch (Exception ex)
            {
                // EntryPointNotFoundException = engine de geracao antiga.
                Debug.WriteLine($"[RecoveryEngineInterop] Engine sem get_abi_version: {ex.GetType().Name}");
                App.LoggingService?.LogError(
                    "[RecoveryEngineInterop] A engine nativa carregada e de uma geracao antiga (sem get_abi_version). " +
                    "Recompile DLLS/RecoveryEngine. A varredura foi DESATIVADA para nao devolver dados corrompidos.", ex);
                return;
            }

            if (abi != ExpectedAbiVersion)
            {
                Debug.WriteLine($"[RecoveryEngineInterop] ABI divergente: engine={abi}, esperado={ExpectedAbiVersion}");
                App.LoggingService?.LogError(
                    $"[RecoveryEngineInterop] ABI DIVERGENTE: engine nativa=0x{abi:X4}, codigo gerenciado=0x{ExpectedAbiVersion:X4}. " +
                    "Recompile engine e gerenciado juntos. A varredura foi DESATIVADA.");
                return;
            }

            IsEngineCompatible = true;
            App.LoggingService?.LogInfo(
                $"[RecoveryEngineInterop] Engine nativa COMPATIVEL (ABI 0x{abi:X4}, struct {StructSize}B).");
        }

        [DllImport("kernel32.dll", EntryPoint = "LoadLibraryW", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibraryNative(string lpFileName);

        private static IntPtr LoadLibrary(string path) => LoadLibraryNative(path);
    }
}
