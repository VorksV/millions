#pragma once
#include <windows.h>
#include <vector>
#include <string>
#include <cstring>
#include <cstdint>
#include <unordered_map>
#include "Scanner.h"
#include "UsnJournal.h"

// Definida em Scanner.cpp; escreve em RecoveryEngine_Debug.log, ao lado da DLL
// carregada.
//
// Declarada no header porque as outras duas unidades da engine tambem precisam
// falar: sem log nos pontos de saida, uma falha de MFT e uma falha de disco
// produzem o MESMO sintoma no lado gerenciado (init_scan == 0), e nao ha como
// distinguir uma da outra sem abrir o log nativo na mao. Foi exatamente assim
// que um bug de nomes de API ficou invisivel por meses.
void LogDebug(const std::string& msg);

namespace NTFS {

    /// <summary>
    /// Buffer alinhado para I/O sem cache (FILE_FLAG_NO_BUFFERING).
    ///
    /// O handle do volume e aberto com NO_BUFFERING, e o Windows exige que o
    /// BUFFER esteja alinhado ao tamanho do setor logico. std::vector alinha a
    /// 16 bytes, o que viola a exigencia e faz ReadFile falhar com
    /// ERROR_INVALID_PARAMETER (87) — sem mensagem util, apenas "nao consegui
    /// ler o registro".
    ///
    /// O codigo original usava VirtualAlloc direto e funcionava. Trocar por
    /// std::vector — que parece uma refatoracao inocente — quebrou a leitura da
    /// MFT inteira. VirtualAlloc devolve memoria alinhada a pagina, que
    /// satisfaz qualquer tamanho de setor.
    /// </summary>
    class AlignedBuffer {
    public:
        AlignedBuffer() = default;
        ~AlignedBuffer() { Free(); }
        AlignedBuffer(const AlignedBuffer&) = delete;
        AlignedBuffer& operator=(const AlignedBuffer&) = delete;

        bool Allocate(size_t bytes) {
            Free();
            if (bytes == 0) return false;
            _data = static_cast<uint8_t*>(VirtualAlloc(nullptr, bytes, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE));
            if (_data) { _bytes = bytes; Zero(); }
            return _data != nullptr;
        }

        void Zero() { if (_data) memset(_data, 0, _bytes); }

        uint8_t* Data() { return _data; }
        size_t Size() const { return _bytes; }

    private:
        void Free() {
            if (_data) { VirtualFree(_data, 0, MEM_RELEASE); _data = nullptr; _bytes = 0; }
        }
        uint8_t* _data = nullptr;
        size_t _bytes = 0;
    };

    // =====================================================================
    // ÍNDICE DE REGISTRO QUE NÃO É ARQUIVO DE USUÁRIO
    // =====================================================================
    //  0 $MFT          8 $BadClus        16 reservado
    //  1 $LogFile      9 $Secure         17 reservado
    //  2 $Volume      10 $UpCase          18 reservado
    //  3 $AttrDef    11 $Extend           19 reservado
    //  4 .           12 $Quota            20 reservado
    //  5 .           13 $ObjId            21 reservado
    //  6 $Bitmap     14 $Reparse          22 reservado
    //  7 $Boot       15 reservado          23 reservado
    //
    // Nada abaixo de 24 é recuperável pelo usuário, e vários ($Secure, $Extend)
    // têm conteúdo que parece "arquivo" para um filtro ingênuo.
    inline const uint64_t kFirstUserRecord = 24;

    // MFT reference da raiz (o "." do volume). parentDirectory igual a isto
    // significa que o arquivo estava DIRETAMENTE em C:\.
    inline const uint64_t kRootDirectory = 5;

    // =====================================================================
    // ESTRUTURAS ON-DISK
    // =====================================================================
#pragma pack(push, 1)

    struct BootSector {
        uint8_t  jump[3];
        uint8_t  oemId[8];
        uint16_t bytesPerSector;
        uint8_t  sectorsPerCluster;
        uint16_t reservedSectors;
        uint8_t  fats;
        uint16_t rootDirs;
        uint16_t totalSectors16;
        uint8_t  mediaDescriptor;
        uint16_t sectorsPerFat;
        uint16_t sectorsPerTrack;
        uint16_t heads;
        uint32_t hiddenSectors;
        uint32_t totalSectors32;
        uint8_t  driveNumber;
        uint8_t  reserved1;
        uint8_t  bootSignature;
        uint8_t  reserved2;
        uint64_t totalSectors64;
        uint64_t mftLcn;
        uint64_t mftMirrLcn;
        int8_t   clustersPerMftRecord;
        uint8_t  reserved3[3];
        int8_t   clustersPerIndexBuffer;
        uint8_t  reserved4[3];
        uint64_t volumeSerialNumber;
        uint32_t checksum;
        uint8_t  bootstrap[426];
        uint16_t endSignature;
    };

    struct MFTHeader {
        uint32_t magic;      // "FILE"
        uint16_t updateOffset;
        uint16_t updateSize;
        uint64_t logSeqNum;
        uint16_t sequenceNum;
        uint16_t hardLinkCount;
        uint16_t attrOffset;
        uint16_t flags;      // 0x01 in use, 0x02 directory, 0x04 extension, 0x08 index
        uint32_t usedSize;
        uint32_t allocatedSize;
        uint64_t baseRecord;
        uint16_t nextAttrId;
    };

    struct AttributeHeader {
        uint32_t type;
        uint32_t length;
        uint8_t  nonResident;
        uint8_t  nameLength;
        uint16_t nameOffset;
        uint16_t flags;
        uint16_t attributeId;

        union {
            struct {
                uint32_t valueLength;
                uint16_t valueOffset;
                uint8_t  reserved[2];
            } resident;
            struct {
                uint64_t startVcn;
                uint64_t endVcn;
                uint16_t runOffset;
                uint16_t compressionSize;
                uint32_t reserved;
                uint64_t allocatedSize;
                uint64_t actualSize;
                uint64_t initializedSize;
            } nonResidentInfo;
        };
    };

    struct FileNameAttr {
        uint64_t parentDirectory;
        uint64_t creationTime;
        uint64_t modificationTime;
        uint64_t mftModificationTime;
        uint64_t accessTime;
        uint64_t allocatedSize;
        uint64_t actualSize;
        uint32_t flags;
        uint32_t reparseTag;
        uint8_t  nameLength;
        uint8_t  nameNamespace;
        wchar_t  name[1];
    };

    struct StandardInfoAttr {
        uint64_t creationTime;
        uint64_t modificationTime;
        uint64_t mftModificationTime;
        uint64_t accessTime;
        uint32_t fileAttributes;
        uint32_t maxVersions;
        uint32_t versionNum;
        uint32_t classId;
        uint32_t ownerId;
        uint32_t securityId;
        uint64_t quotaCharged;
        uint64_t usn;
    };

#pragma pack(pop)

    // =====================================================================
    // ESTATÍSTICAS DA VARREDURA
    // =====================================================================
    // Sem isto, um bug de filtro é invisível: a lista fica curta e ninguém
    // sabe se foi o filtro funcionando ou o parser quebrando.
    struct ScanStats {
        uint64_t recordsRead        = 0;
        uint64_t notMagic           = 0;   // registro ilegível / não é FILE
        uint64_t isDirectory        = 0;
        uint64_t stillAlive         = 0;   // flag "in use" — nunca foi deletado
        uint64_t metaRecord         = 0;   // índice < 24
        uint64_t systemName         = 0;   // começa com '$'
        uint64_t tempName           = 0;   // "~$...", ".tmp"
        uint64_t invalidName        = 0;   // lixo Unicode / caracteres de controle
        uint64_t rootLevel          = 0;   // parent == raiz
        uint64_t systemFolder       = 0;   // ancestral em Windows/ProgramFiles/...
        uint64_t noDataAttribute    = 0;
        uint64_t zeroSize           = 0;
        uint64_t absurdSize         = 0;
        uint64_t unknownExtension   = 0;
          uint64_t signatureMismatch  = 0;   // <-- o bug do runlist obsoleto
          uint64_t signatureUnreadable= 0;
          uint64_t accepted           = 0;
          // Cadeia de pastas nenhuma conferida: o primeiro ancestral já não
          // bateu com a referência. Estos não entram na lista — não há
          // caminho honesto para mostrar, e um caminho inventado é pior que
          // a ausência dele.
          uint64_t pathUnverified     = 0;
          // Por que a cadeia parou no primeiro degrau. Sem estes quatro, um
          // volume com centenas de milhares de "caminho não verificável" não
          // diz se a conferência está errada ou se os diretórios foram mesmo
          // reutilizados — e as duas coisas exigem correções opostas.
          uint64_t pathFailMagic      = 0;
          uint64_t pathFailSequence   = 0;
          uint64_t pathFailNotDir     = 0;
          uint64_t pathFailParse      = 0;
          uint64_t pathFailNoRoot     = 0;   // so passou da profundidade
          uint64_t pathSeqReused      = 0;   // atual > ref: slot reutilizado (PROVA)
          uint64_t pathSeqBackward    = 0;   // atual <= ref: extracao suspeita
          uint64_t pathFromUsn        = 0;   // caminho veio do REWIND do USN Journal
          uint64_t usnRecordsParsed   = 0;
          uint64_t usnEntriesInMap    = 0;
          uint64_t usnResolved        = 0;
          uint64_t usnUnknown         = 0;
          uint64_t usnJournalAbsent   = 0;   //  nao existe na MFT (journal desligado)
          // Lixeira
          uint64_t recycleInfoFound   = 0;   // $I lidos e decodificados
          uint64_t recycleRecovered   = 0;   // $R reemitidos com nome/caminho do $I
          uint64_t recycleOrphan      = 0;   // $R sem $I pareado (o $I já foi sobrescrito)
          uint64_t recycleNameSeen     = 0;   // registros com nome $I* vistos, antes de validar o id
          uint64_t recycleIdRejected   = 0;   // $I* que o validador de id recusou
          uint64_t recycleFolderSeen    = 0;
          uint64_t recycleParseFailed  = 0;   //  existem mas o parser recusou
          uint64_t recycleStillInBin   = 0;
          uint64_t recycleFoldersNamed  = 0;   // pastas COM nome real: listadas
          uint64_t recycleFoldersUnnamed = 0;
          uint64_t contentGone         = 0;   // registro intacto, conteudo sobrescrito   // pastas SEM nome: nao listadas   // registros R/I EM USO: item AINDA esta na lixeira   // $R* sem extensão = PASTA esvaziada da lixeira
      };


    class Parser {
    public:
        Parser(HANDLE hDrive);
        bool Init();
        void ScanMFT(RecoveryScanner* scanner);
        const ScanStats& Stats() const { return _stats; }

    private:
        HANDLE _hDrive;
        BootSector _bs;
        uint64_t _mftOffset;
        uint32_t _mftRecordSize;
        uint32_t _clusterSize;
        uint64_t _volumeBytes;
        ScanStats _stats;

        // Runs da $MFT, em clusters. Guardados porque o walk de ancestrais
        // precisa ler um registro pelo ÍNDICE, e o índice só se traduz em
        // posição física através dos runs.
        std::vector<std::pair<uint64_t, uint64_t>> _mftRuns;

        // Cache de nomes de diretório, resolvidos sob demanda. Centenas de
        // arquivos compartilham o mesmo pai, então o cache transforma
        // milhares de buscas em algumas dezenas de leituras.
        std::unordered_map<uint64_t, std::wstring> _dirNameCache;

        // Scratch reused entre registros — evita alocar por registro.
        // AlignedBuffer, e nao std::vector: ver a classe. NO_BUFFERING exige
        // alinhamento de pagina no buffer.
        AlignedBuffer _probe;
        AlignedBuffer _recordBuf;

        // Quantas amostras ja foram logadas. O log e limitado de proposito:
        // registrar 50.000 linhas por varredura torna o arquivo ilegivel e
        // atrasa a varredura, que e exatamente o oposto do que um log de
        // diagnostico deve fazer.
        unsigned _shownMismatch = 0;
        unsigned _shownAccepted = 0;
        unsigned _shownRecycle  = 0;
        unsigned _shownSeqMismatch = 0;
        unsigned _shownUsnPath = 0;
        unsigned _shownNameAudit = 0;
        unsigned _shownRecycleDiag = 0;
        unsigned _shownStillInBin = 0;
        unsigned _shownExtendChild = 0;
        uint64_t _extendIndex = 0;

        bool ParseMftRuns(const uint8_t* mft0, uint32_t recordBytes);
        void ProcessMFTRecord(uint8_t* buffer, uint64_t mftIndex, uint64_t physicalRecordOffset, RecoveryScanner* scanner);

        // Lê o registro MFT de índice pelo runs da $MFT. Usado para resolver
        // o nome de um diretório pai sem varrer a MFT inteira de novo.
        bool ReadMFTRecordByIndex(uint64_t index, uint8_t* out);
        bool ReadMFTRecordZero(uint8_t* out);
        bool ReadMFTRecordAtOffset(uint64_t byteOffset, uint8_t* out);

        // Nome (Win32) de um diretório, com cache. Devolve false se não
        // der para ler o registro.
        bool ResolveDirectoryName(uint64_t index, std::wstring& out);

        // Reconstrói o caminho do arquivo subindo a árvore de diretórios.
        // `hitSystemFolder` recebe true se qualquer ancestral for uma pasta
        // de sistema.
        bool ResolveParentPath(uint64_t parentIndex, std::wstring& out, bool& hitSystemFolder);

        bool ReadContentProbe(uint64_t offset, size_t want);
        void LogProbeFailure(const char* what, uint64_t offset, DWORD err);

        // -----------------------------------------------------------------
        // REFERÊNCIA MFT COM NÚMERO DE SÉQUÊNCIA
        //
        // O campo parentDirectory do $FILE_NAME não é um índice solto: é uma
        // referência de 64 bits feita de
        //     bits  0..47  -> índice do registro no $MFT
        //     bits 48..63  -> número de sequência do registro
        //
        // O número de sequência é a peça que os recuperadores profissionais
        // usam e que a versão anterior descartava com um & 0xFFFFFFFFFF. Ele
        // resolve o problema que motivou os caminhos inventados: quando um
        // registro é apagado, o NTFS devolve o índice para a lista de livres e
        // o próximo arquivo criado ocupa aquele índice COM UM NÚMERO DE
        // SÉQUÊNCIA MAIOR. Então, se a referência guardada no arquivo
        // deletado aponta para o registro N com sequência S, e o registro N
        // hoje tem sequência S' != S, fica PROVADO que aquele registro foi
        // substituído — e a cadeia de pastas para aí. Sem essa conferência
        // não há como distinguir "pasta real" de "pasta que já foi
        // reutilizada", e é por isso que aparecia
        //     ...\Feedback\1789710115297975000.jpg\...
        // com uma FOTO no meio de um caminho de pastas.
        // -----------------------------------------------------------------
        struct MftRef {
            uint64_t index;
            uint16_t sequence;
            static MftRef From(uint64_t raw) {
                MftRef r;
                r.index   = raw & 0x0000FFFFFFFFFFFFULL;
                r.sequence = static_cast<uint16_t>(raw >> 48);
                return r;
            }
        };

        // Resultado da caminhada da cadeia de ancestrales.
        struct PathResult {
            std::wstring path;        // caminho montado (pode ser parcial)
            int  verifiedLevels = 0;  // quantos degraus foram PROVADOS
            bool complete = false;    // chegou à raiz sem quebrar
            bool hitRecycleBin = false;
            bool hitSystemFolder = false;
            // Por que a cadeia parou no primeiro degrau. Sem isto, um
            // volume com 390 mil rejeições por "caminho não verificável" não
            // diz se a conferência de sequência está errada ou se são
            // diretórios realmente reutilizados — e as duas correções são
            // opostas.
            int failMagic = 0;        // registro não é "FILE"
            int failSequence = 0;     // número de sequência não bate
            int failNotDirectory = 0;  // o registro não é diretório
            int failParse = 0;        // sem $FILE_NAME legível
            int failNoRoot = 0;       // terminou por profundidade, não por quebra
            // A DIREÇÃO da diferença de sequência é o que separa

            // "o disco reutilizou o slot" de "a referência foi lida errada".
            //   atual >  ref  -> reuso real; a conferência está certa.

            //   atual <= ref  -> a extração da referência está suspeita.

            // Se o segundo for o número grande, 390 mil diretórios
            // "reutilizados" seria absurdo, e a conclusão inverte.
            int seqReused = 0;
            int seqBackward = 0;
        };

        bool ResolveParentPath(MftRef parent, PathResult& out);

        // Localiza $UsnJrnl:$J (stream nomeado do journal) na MFT.
        bool FindUsnJournal(uint64_t& dataOffset, uint64_t& dataBytes);
        // Parse de runlist compartilhado entre o scan e a busca do journal.
        bool ParseRunList(const AttributeHeader* attr, const uint8_t* bufEnd, const uint8_t* runListStart,
                          uint64_t& firstByteOffset, uint64_t& totalBytes, uint32_t& runCount);

        // -----------------------------------------------------------------
        // LIXEIRA
        //
        // Esvaziar a lixeira NÃO apaga o conteúdo: os dois registros ficam
        // na MFT marcados "não em uso", e o arquivo de dados continua nos
        // clusters. O NTFS ainda cria, para cada item, um par:
        //
        //     $I<id>    metadados: caminho e tamanho ORIGINAIS (UTF-16)
        //     $R<id><ext>  o conteúdo, com a extensão original
        //
        // Descartar $Recycle.Bin como "pasta de sistema" (o que a versão
        // anterior fazia) jogava fora exatamente o caso que o usuário mais
        // quer: o arquivo que ele acabou de esvaziar da lixeira. Lendo o $I
        // dá-se o caminho de verdade, em vez de
        //     C:\$Recycle.Bin\S-1-5-21-...\$R7FS92DJ.tmp
        // que não ajuda ninguém a reencontrar o arquivo.
        // -----------------------------------------------------------------
        struct RecycleEntry {
            std::wstring originalPath;   // ex.: C:\Users\VOLTRIS\Desktop\a.pdf
            std::wstring originalName;   // ex.: a.pdf
            uint64_t originalSize = 0;
            bool valid = false;
        };

        static bool RecycleIdFromName(const std::wstring& name, wchar_t kind, std::wstring& id);
        bool ParseRecycleInfo(const uint8_t* data, size_t len, RecycleEntry& out) const;
        bool ReadFirstBytes(uint64_t byteOffset, size_t want, uint8_t* dst);

        // $I é lido durante a varredura; $R pode aparecer ANTES do seu par, já
        // que a ordem dos registros na MFT é por índice e não por data. Por
        // isso os candidatos $R ficam pendentes e são resolvidos no fim.
        std::unordered_map<std::wstring, RecycleEntry> _recycleInfo;
        // Journal carregado antes do loop de varredura, para que a cadeia
        // de pais tenha uma segunda fonte quando a MFT já não serve.
        Usn::Journal _usn;
        bool _usnLoaded = false;
        struct PendingRecycle {
            FoundFile file;
            std::wstring id;
        };
        std::vector<PendingRecycle> _pendingRecycle;
        // Pastas da lixeira ficam separadas: so entram na lista com nome real.
        std::vector<FoundFile> _pendingRecycleFolders;
        // Itens com registro intacto e conteudo sobrescrito/zerado.
        // Vao para a lista marcados com CF_CONTENT_GONE, escondidos por padrao.
        std::vector<FoundFile> _pendingContentGone;
        std::vector<std::string> _pendingRecycleFolderIds;

        static std::wstring ToWstring(const std::string& s) {
            if (s.empty()) return std::wstring();
            int n = MultiByteToWideChar(CP_UTF8, 0, s.c_str(), (int)s.size(), nullptr, 0);
            if (n <= 0) return std::wstring();
            std::wstring w;
            w.resize(n);
            MultiByteToWideChar(CP_UTF8, 0, s.c_str(), (int)s.size(), &w[0], n);
            return w;
        }
        void FlushPendingRecycle(RecoveryScanner* scanner);

        // Deduplicacao do log de falha do probe: uma linha por motivo, com
        // contador. Dezenas de milhares de candidatos nao podem gerar
        // dezenas de milhares de linhas.
        const char* _lastProbeFailure = nullptr;
        uint64_t _probeFailureCount = 0;
    };

}

