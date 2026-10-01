#pragma once
#include <windows.h>
#include <string>
#include <vector>
#include <unordered_map>

// =====================================================================
// LEITOR DO USN JOURNAL ($UsnJrnl:$J) COM RECONSTRUÇÃO DE CAMINHO
// PELO ALGORITMO "REWIND"
//
// POR QUE ISTO EXISTE
// -------------------
// A cadeia de pais na MFT é resolvida casando o índice E O NÚMERO DE
// SÉQUÊNCIA do registro com o registro atualmente naquele índice. É a
// técnica que MFTECmd e TZWorks jp usam, e ela está CORRETA — mas só
// funciona enquanto o registro pai não foi reutilizado.
//
// Num volume com muito churn (o instalador do Visual Studio recria
// diretórios de TEMP o tempo todo), a maioria dos arquivos apagados tem a
// pasta-mãe já reatribuída. Aí a conferência falha e o caminho é
// irreduzível a partir da MFT. É o que produzia 95% de
// "[localização original não recuperável]".
//
// O USN Journal é a fonte autoritativa do caminho: cada operação de
// arquivo grava ali o NOME e a REFERÊNCIA DO PAI, e o registro só é
// sobrescrito quando o buffer circular dá a volta (dias ou semanas).
//
// O truque — e o que as ferramentas de topo fazem — é NÃO resolver as
// referências contra o estado atual do volume, e sim REVERTER o journal:
// ler do registro mais novo para o mais antigo, mantendo o estado de cada
// (entrada, sequência) → (nome, pai) enquanto se caminha. Isso reconstrói
// a árvore como ela ERA, e por isso resolve os casos em que a MFT já não
// serve mais.
//
// Referência: CyberCX, "NTFS Usnjrnl Rewind".
// =====================================================================

namespace Usn {

#pragma pack(push, 1)

    // Cabecalho do journal no início do stream $J.
    // O primeiro registro começa em sizeof(JournalDataHeader) alinhado a 8.
    struct JournalHeader {
        uint64_t journalId;
        uint64_t firstUsn;        // USN do registro mais antigo ainda presente
        uint64_t nextUsn;         // USN do próximo a ser escrito
        uint64_t lowestValidUsn;  // nada abaixo disso é confiável
        uint32_t maxSize;
        uint32_t allocationSize;
        uint64_t usnMin;
        uint64_t usnMax;
    };

    // USN_RECORD_V2 / V3 — mesmo layout. V4 usa referências de 128 bits e
    // não aparece em NTFS 3.1, então é ignorado de propósito.
    struct RecordHeader {
        uint32_t recordLength;
        uint16_t majorVersion;
        uint16_t minorVersion;
        uint64_t fileReference;      // (seq << 48) | entrada
        uint64_t parentReference;    // (seq << 48) | entrada do pai
        uint64_t usn;
        int64_t  timestamp;          // FILETIME
        uint32_t reason;
        uint32_t sourceInfo;
        uint32_t securityId;
        uint32_t fileAttributes;
        uint16_t fileNameLength;     // em bytes, UTF-16
        uint16_t fileNameOffset;
    };

#pragma pack(pop)

    // Bits de reason relevantes. O conjunto completo esta em USN_REASON_*.
    enum Reason : uint32_t {
        DATA_OVERWRITE        = 0x00000001,
        DATA_EXTEND           = 0x00000002,
        DATA_TRUNCATION       = 0x00000004,
        NAMED_DATA_OVERWRITE  = 0x00000010,
        NAMED_DATA_EXTEND     = 0x00000020,
        FILE_CREATE           = 0x00000100,
        FILE_DELETE           = 0x00000200,
        EA_CHANGE             = 0x00000400,
        SECURITY_CHANGE       = 0x00000800,
        RENAME_OLD_NAME       = 0x00001000,
        RENAME_NEW_NAME       = 0x00002000,
        INDEXABLE_CHANGE      = 0x00004000,
        BASIC_INFO_CHANGE     = 0x00008000,
        HARD_LINK_CHANGE      = 0x00010000,
        COMPRESSION_CHANGE    = 0x00020000,
        ENCRYPTION_CHANGE     = 0x00040000,
        OBJECT_ID_CHANGE      = 0x00080000,
        REPARSE_POINT_CHANGE  = 0x00100000,
        STREAM_CHANGE         = 0x00200000,
        TRANSACTED_CHANGE     = 0x00400000,
        INTEGRITY_CHANGE      = 0x00800000,
        CLOSE                 = 0x80000000,
        ALL                   = 0xFFFFFFFF
    };

    // O que o rewind sabe sobre uma entrada (entrada, sequencia) em
    // determinado instante do journal.
    struct EntryState {
        std::wstring name;
        uint64_t parentRef = 0;
        uint32_t attributes = 0;
        int64_t  timestamp = 0;
    };

    struct JournalStats {
        uint64_t bytesScanned      = 0;
        uint64_t recordsParsed     = 0;
        uint64_t recordsRejected   = 0;   // versão/ tamanho invalido
        uint64_t entriesInMap      = 0;
        uint64_t deletionsSeen     = 0;
        uint64_t pathsResolved     = 0;   // caminho completo ate a raiz
        uint64_t pathsPartial      = 0;   // cadeia parcial (pai ausente)
        uint64_t pathsUnknown      = 0;   // nenhum ancestral no journal
        bool     journalFound      = false;
        bool     journalEmpty      = false;
    };

    // -----------------------------------------------------------------
    // O reader.
    //
    // Nao guarda os registros brutos: sao dezenas de milhares e nao ha
    // necessidade. Guarda so o mapa de estado, que e o que o rewind produz.
    // -----------------------------------------------------------------
    class Journal {
    public:
        // Lê o stream e monta o mapa. 'hDrive' e o handle do volume, o
        // mesmo do MFT, e 'dataOffset'/'dataBytes' localizam o $J.
        bool Load(HANDLE hDrive, uint64_t dataOffset, uint64_t dataBytes, uint32_t sectorSize);

        // Caminho completo de uma referência de arquivo, reconstruído pelo
        // rewind. Devolve false quando nem o primeiro ancestral existe no
        // journal — e nesse caso NAO inventa nada.
        bool ResolvePath(uint64_t fileReference, std::wstring& out) const;

        // Nome mais recente conhecido para a referência, se houver.
        bool LookupName(uint64_t fileReference, std::wstring& out) const;

        // Instante do evento mais recente que a engine conhece para a
        // referência. Usado para dizer QUANDO o arquivo foi apagado.
        bool LookupTimestamp(uint64_t fileReference, int64_t& out) const;

        // Ultimo caminho resolvido, para o log de amostra.
        uint64_t resolvedCount() const { return _stats.pathsResolved; }
        const JournalStats& Stats() const { return _stats; }

    private:
        // Resolve subindo a cadeia no mapa. Devolve quantos degraus
        // Achiearam nome e onde a cadeia parou.
        bool WalkUp(uint64_t startRef, std::wstring& out, int& levels, bool& hitRoot) const;

        std::unordered_map<uint64_t, EntryState> _state;   // chave = ref de 64 bits
        JournalStats _stats;
    };

    // Extrai o NOME do registro, ja em wstring. Retorna false se o
    // registro for inconsistente. Separado para poder ser testado.
    bool ReadRecordName(const uint8_t* base, size_t available, RecordHeader& hdr, std::wstring& name);

} // namespace Usn
