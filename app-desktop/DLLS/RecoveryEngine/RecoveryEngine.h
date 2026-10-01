#ifndef RECOVERY_ENGINE_H
#define RECOVERY_ENGINE_H

#define NOMINMAX
#include <windows.h>
#include <stdint.h>

#ifdef EXPORT_RECOVERY_ENGINE
#define RECOVERY_API __declspec(dllexport)
#else
#define RECOVERY_API __declspec(dllimport)
#endif

extern "C" {

    // ─────────────────────────────────────────────────────────────────
    // VERSÃO DO ABI.
    //
    // A engine é nativa e o lado gerenciado empacota structs em buffer
    // CRU. Divergir o layout não dá erro de compilação: dá lixo em
    // memória. Pior, uma DLL nativa VELHA no diretório de saída é
    // silenciosamente compatível na assinatura das funções e
    // incompatível no layout dos structs.
    //
    // Por isso a primeira coisa que o lado gerenciado faz é ler este
    // número. Se não bater, ele se recusa a carregar — em vez de
    // devolver uma lista de arquivos corrompida para o usuário.
    //
    //   0x0001 — layout original (FoundFile = 328 bytes, sem
    //             OriginalPath/RunCount/Flags, sem get_found_count)
    //   0x0002 — layout atual (FoundFile = 848 bytes)
    //
    // INCREMENTE a cada vez que FoundFile ou RecoveryStats mudarem.
    // ─────────────────────────────────────────────────────────────────
    #define RECOVERY_ABI_VERSION 0x0003

    // Bits de FoundFile.Flags
    #define RF_FRAGMENTED   0x1u   // $DATA com mais de um run
    #define RF_RESIDENT     0x2u   // conteúdo dentro do próprio registro MFT
    #define RF_SIG_VERIFIED 0x4u   // assinatura do formato confere com a extensão

      // Bits de FoundFile.CategoryFlags
      #define CF_DELETED      0x1u   // registro marcado "não em uso"
      #define CF_CONTIGUOUS   0x2u   // recuperável por cópia direta
      #define CF_TEXTLIKE     0x4u   // formato sem magic number (validação por nome)
      #define CF_TRUNCATED    0x8u   // caminho original não coube no buffer
      // A lixeira: o registro é um $R<id><ext> e o caminho/nome/tamanho
      // verdadeiros vieram do $I<id> pareado. Sem este bit o item aparece
      // como "C:\$Recycle.Bin\S-1-5-21-...\$R7FS92DJ.tmp", que não é um
      // caminho de onde o usuário possa extrair o arquivo.
      #define CF_FROM_RECYCLE 0x10u
      // Cada degrau da cadeia de pastas foi conferido pelo número de
      // sequência da referência MFT e pelo bit de diretório. Ausente: a
      // cadeia quebrou em algum ponto e o caminho exibido é parcial.
      #define CF_PATH_VERIFIED 0x20u
      // O caminho veio do rewind do USN Journal, e nao da cadeia de pais da
      // MFT. Mais confiavel ainda: a pasta-mãe ja foi reatribuida no volume,
      // mas o journal guardou a arvore como ela era.
      #define CF_PATH_FROM_USN  0x40u
      // Item é uma PASTA esvaziada da lixeira. Pasta não ocupa clusters de
      // dado, então não há o que extrair — o que o usuário ganha é o NOME,
      // e os arquivos dentro dela aparecem individualmente, com os registros
      // MFT próprios. A interface usa este bit para mostrar o ícone de pasta
      // e esconder o botão de recuperar, que não teria o que copiar.
      #define CF_IS_FOLDER     0x80u
      // O registro MFT deste arquivo está INTACTO — nome, tamanho e
      // caminho original corretos — mas os clusters que ele aponta já não
      // contêm mais o conteúdo. É o caso de arquivo apagado num SSD com
      // TRIM ligado: o controlador zera os blocos em segundos, enquanto o
      // registro de metadados sobrevive.
      //
      // Até aqui esses itens eram DESCARTADOS em silêncio, e o efeito foi o
      // pior possível: o usuário apagou "teste.pdf", a varredura achou o
      // registro dele, e a lista não mostrou nada. Sem o item, a resposta é
      // "não achei" — que é falso. O registro existe; o que não existe é o
      // conteúdo. Essa diferença é a informação mais importante que a
      // ferramenta pode entregar, e ela estava sendo jogada fora.
      //
      // Recuperadores de topo listam esses itens com aviso explícito. O
      // usuário decide se vale extrair um arquivo de tamanho correto cujo
      // conteúdo já foi sobrescrito — às vezes ainda há resíduo útil no
      // começo, e o arquivo sai com o nome certo, que já é metade do
      // trabalho.
      #define CF_CONTENT_GONE 0x100u



    typedef struct {
        uint64_t Id;
        char Filename[260];
        char Extension[16];
        uint64_t Size;
        double Recoverability;
        uint64_t Offset;
        uint64_t ParentId; // ID MFT da pasta pai para reconstrução de árvore

        // ── campos adicionados ─────────────────────────────────────────
        // Tudo que vem DEPOIS deste ponto é novo. O lado gerenciado
        // (FoundFileNative em Services/Recovery/RecoveryEngineInterop.cs)
        // precisa ter exatamente o mesmo layout, NESTA ordem. O
        // static_assert abaixo existe para o compilador gritar se um dos
        // lados mudar sem o outro.
        char     OriginalPath[512]; // caminho real resolvido, UTF-8
        uint32_t RunCount;          // nº de runs da $DATA (1 = contíguo)
        uint32_t Flags;             // RF_*
        uint32_t CategoryFlags;     // CF_*
        uint32_t Reserved;          // alinhamento / espaço futuro
    } FoundFile;

    // Resumo do por que a lista ficou do jeito que ficou. Sem isto, um filtro
    // que come demais é indistinguível de um parser quebrado.
    typedef struct {
        uint64_t recordsRead;
        uint64_t notMagic;
        uint64_t isDirectory;
        uint64_t stillAlive;         // não deletado — fora do escopo da recuperação
        uint64_t metaRecord;         // índice MFT < 24
        uint64_t systemName;         // começa com '$'
        uint64_t tempName;           // "~$..."
        uint64_t invalidName;        // lixo Unicode
        uint64_t rootLevel;          // na raiz do volume (C:)
        uint64_t systemFolder;       // sob Windows/ProgramFiles/AppData/...
        uint64_t noDataAttribute;
        uint64_t zeroSize;
        uint64_t absurdSize;
        uint64_t unknownExtension;
        uint64_t signatureMismatch;  // <-- runlist obsoleta: nome ok, dados de outro
          uint64_t signatureUnreadable;
          uint64_t accepted;           // entrou na lista
          // Cadeia de pastas nenhuma conferida — o primeiro ancestral já não
          // bateu com a referência MFT. Não entram na lista: não há caminho
          // honesto para mostrar.
          uint64_t pathUnverified;
          // Por que a cadeia de pastas parou no primeiro degrau. Sem estes
          // contadores, "caminho nao verificavel = 390.822" nao diz se a
          // conferencia esta errada ou se os diretorios foram mesmo
          // reutilizados — e as correcoes sao opostas.
          uint64_t pathFailMagic;      // registro nao e "FILE"
          uint64_t pathFailSequence;   // numero de sequencia difere
          uint64_t pathFailNotDir;     // registro nao e diretorio
          uint64_t pathFailParse;      // sem $FILE_NAME legivel
          uint64_t pathFailNoRoot;     // so acabou a profundidade (nao e falha)
          uint64_t pathSeqReused;      // atual > referenciado: slot REUTILIZADO (prova)
          uint64_t pathSeqBackward;    // atual <= referenciado: extracao suspeita
          uint64_t pathFromUsn;        // caminho reconstruido pelo rewind do USN Journal
          uint64_t usnRecordsParsed;   // registros USN lidos
          uint64_t usnEntriesInMap;    // (entrada,seq) no mapa do rewind
          uint64_t usnResolved;        // amostra que fechou caminho ate a raiz
          uint64_t usnUnknown;         // amostra sem ancestral no journal
          // Lixeira
          uint64_t recycleInfoFound;   // $I lidos e decodificados
          uint64_t recycleRecovered;   // $R reemitidos com nome/caminho do $I
          uint64_t recycleOrphan;      // $R sem $I pareado ($I sobrescrito)
          uint64_t recycleNameSeen;     // registros com nome $I* vistos
          uint64_t recycleIdRejected;   // $I* recusados pelo validador de id
          uint64_t recycleFolderSeen;
          uint64_t recycleParseFailed;
          uint64_t recycleStillInBin;
          uint64_t recycleFoldersNamed;
          uint64_t recycleFoldersUnnamed;
          uint64_t contentGone;    // $R* sem extensao = PASTA esvaziada da lixeira
      } RecoveryStats;


    typedef void(*ProgressCallback)(double percentage, uint64_t currentSector, uint64_t totalSectors, uint32_t filesFound);

    // Initialization
    RECOVERY_API int get_abi_version();
    RECOVERY_API int init_scan(const char* drivePath); // e.g., "\\.\PhysicalDrive0" or "\\.\C:"
    RECOVERY_API void stop_scan();

    // Scan Operations
    RECOVERY_API int start_quick_scan(ProgressCallback callback);
    RECOVERY_API int start_deep_scan(ProgressCallback callback); // Signature carving

    // Data Retrieval
    RECOVERY_API int get_found_files(FoundFile* buffer, int maxCount);

    // Quantos arquivos a varredura terminou e encontrou. Existe para que o
    // lado gerenciado não precise alocar um buffer de tamanho chutado — a
    // versão anterior reservava ~1,5 MILHÕES de structs (centenas de MB)
    // "por via das dúvidas" e ainda assim truncava em 50.000.
    RECOVERY_API int get_found_count();

    // Estatísticas do filtro. Chamar depois que a varredura terminar.
    RECOVERY_API int get_stats(RecoveryStats* out);

    // Recovery
    RECOVERY_API int recover_file(uint64_t fileId, const char* outputPath);
}

// O layout é contrato entre C++ e C#. Se este assert quebrar, o struct do
// lado gerenciado precisa mudar junto — e NÃO deve ser "corrigido" ajustando
// o C# para calçar o C++, porque os dois são empacotados em buffers crus.
static_assert(sizeof(FoundFile) == 848, "FoundFile layout mudou: ajustar FoundFileNative em C# tambem");
static_assert(sizeof(RecoveryStats) == 328, "RecoveryStats mudou (lixeira + diagnostico de caminho): ajustar RecoveryStatsNative em C#");


#endif // RECOVERY_ENGINE_H
