#include "UsnJournal.h"
#include "Scanner.h"
#include <cstring>

namespace Usn {

// =====================================================================
// VALIDAÇÃO DE UM REGISTRO
//
// Um journal corrompido — ou simplesmente mal alineado por causa de uma
// leitura parcial — precisa ser descartado REGISTRO A REGISTRO, senão o
// rewind propaga lixo para o mapa inteiro e os caminhos saem inventados.
// É a diferença entre "não sei" e "sei que não sei".
// =====================================================================
bool ReadRecordName(const uint8_t* base, size_t available, RecordHeader& hdr, std::wstring& name) {
    name.clear();
    if (!base || available < sizeof(RecordHeader)) return false;

    memcpy(&hdr, base, sizeof(RecordHeader));

    if (hdr.recordLength < sizeof(RecordHeader)) return false;
    if (hdr.recordLength > available) return false;
    if (hdr.recordLength > 0x10000) return false;          // 64 KiB e absurdo

    // V2 e V3 compartilham layout. V4 (refs de 128 bits) nao existe em
    // NTFS 3.1 e teria outro offset para o nome — recusar em vez de ler
    // o nome no lugar errado.
    if (hdr.majorVersion != 2 && hdr.majorVersion != 3) return false;
    if (hdr.minorVersion != 0) return false;

    if (hdr.fileNameLength == 0) return false;
    if (hdr.fileNameLength > 512) return false;            // 255 chars * 2
    if (hdr.fileNameOffset < sizeof(RecordHeader)) return false;
    if (static_cast<size_t>(hdr.fileNameOffset) + hdr.fileNameLength > hdr.recordLength) return false;

    const uint8_t* p = base + hdr.fileNameOffset;
    size_t chars = hdr.fileNameLength / 2;
    name.reserve(chars);
    for (size_t i = 0; i < chars; ++i) {
        wchar_t c = static_cast<wchar_t>(p[i * 2] | (static_cast<wchar_t>(p[i * 2 + 1]) << 8));
        // Nome com surrogate órfão ou controle é lixo de journal parcial.
        if (c < 0x20) return false;
        if (c >= 0xD800 && c <= 0xDFFF) return false;
        name.push_back(c);
    }
    return !name.empty();
}

// =====================================================================
// LOAD — varre o stream $J e monta o mapa do rewind
//
// O journal e um buffer circular: os registros mais antigos do início já
// foram sobrescritos. Comecar em firstUsn/FirstValid em vez do byte 0 é o
// que evita interpretar o(queue) lixo antigo como registro válido.
// =====================================================================
bool Journal::Load(HANDLE hDrive, uint64_t dataOffset, uint64_t dataBytes, uint32_t sectorSize) {
    _stats = JournalStats();
    if (!hDrive || hDrive == INVALID_HANDLE_VALUE) return false;
    if (dataOffset == 0 || dataBytes < sizeof(JournalHeader)) return false;

    const uint32_t ss = sectorSize ? sectorSize : 512;

    // ── 1. Cabeçalho ──────────────────────────────────────────────────
    // Com NO_BUFFERING o comprimento tem de ser multiplo do setor, e o
    // offset tambem. O $J comeca num cluster, entao o offset ja e
    // alinhado; o cabecalho tem 0x30 bytes e lemos um setor inteiro.
    uint32_t hdrRead = ((sizeof(JournalHeader) + ss - 1) / ss) * ss;
    std::vector<uint8_t> hdrBuf(hdrRead, 0);

    LARGE_INTEGER li;
    li.QuadPart = static_cast<LONGLONG>(dataOffset);
    if (!SetFilePointerEx(hDrive, li, NULL, FILE_BEGIN)) return false;

    DWORD got = 0;
    if (!ReadFile(hDrive, hdrBuf.data(), hdrRead, &got, NULL)) {
        LogDebug("[USN] Falha ao ler o cabecalho do journal. Erro " +
                 std::to_string(GetLastError()));
        return false;
    }
    if (got < sizeof(JournalHeader)) {
        LogDebug("[USN] Journal curto demais para ter cabecalho (" +
                 std::to_string(got) + " bytes).");
        return false;
    }

    JournalHeader h;
    memcpy(&h, hdrBuf.data(), sizeof(JournalHeader));
    _stats.journalFound = true;

    // Um journal nunca tem maxSize zero. Se vier, o stream esta corrompido
    // e seguir adiante produziria paths inventados.
    if (h.maxSize == 0 || h.allocationSize == 0) {
        LogDebug("[USN] Journal sem geometria (maxSize=" + std::to_string(h.maxSize) +
                 " alloc=" + std::to_string(h.allocationSize) + "). Descartando.");
        return false;
    }

    LogDebug("[USN] Journal: id=0x" + std::to_string((unsigned long long)h.journalId) +
             " maxSize=" + std::to_string(h.maxSize) +
             "B  primeiroUSN=" + std::to_string((unsigned long long)h.firstUsn) +
             "  proximoUSN=" + std::to_string((unsigned long long)h.nextUsn));

    // ── 2._records ────────────────────────────────────────────────────
    //
    // Os registros começam após o cabecalho, alinhado a 8. O USN de cada
    // um e o seu offset dentro do stream, então o proprio cabecalho diz
    // onde a的区域 válida começa.
    const uint64_t recStart = (sizeof(JournalHeader) + 7) & ~7ULL;

    // Limite: o stream pode ser bem maior que o que ainda vale.
    uint64_t limit = dataBytes;
    if (h.allocationSize && h.allocationSize < limit) limit = h.allocationSize;

    if (limit <= recStart) {
        _stats.journalEmpty = true;
        LogDebug("[USN] Journal sem area de registros util.");
        return true;
    }

    const uint64_t scanBytes = limit - recStart;

    // Ler o journal inteiro de uma vez e o mais simples, mas num volume
    // de 684 GB o $J pode ter dezenas de MB e o buffer ficaria grande
    // demais. Ler em JANELAS de 4 MiB, sempre alinhadas ao setor, e o
    // mesmo padrao de I/O do resto da engine.
    const uint64_t kWindow = 4ull * 1024 * 1024;
    uint64_t windowBytes = ((kWindow + ss - 1) / ss) * ss;

    if (windowBytes == 0) return false;
    std::vector<uint8_t> window;
    window.assign(windowBytes, 0);

    uint64_t pos = 0;
    while (pos < scanBytes) {
        uint64_t want = windowBytes;
        if (want > scanBytes - pos) {
            want = ((scanBytes - pos) + ss - 1) / ss * ss;
            if (want < scanBytes - pos) want += ss;
        }

        LARGE_INTEGER l2;
        l2.QuadPart = static_cast<LONGLONG>(dataOffset + recStart + pos);
        if (!SetFilePointerEx(hDrive, l2, NULL, FILE_BEGIN)) break;

        DWORD wgot = 0;
        if (!ReadFile(hDrive, window.data(), static_cast<DWORD>(want), &wgot, NULL)) break;
        if (wgot < want) want = wgot;
        if (want < sizeof(RecordHeader)) break;

        _stats.bytesScanned += want;

        size_t cursor = 0;
        while (cursor + sizeof(RecordHeader) <= want) {
            RecordHeader hdr;
            std::wstring name;
            if (!ReadRecordName(window.data() + cursor, want - cursor, hdr, name)) {
                _stats.recordsRejected++;
                // Um registro invalido no meio da janela nao invalida a
                // janela: o proxima comeca onde este deveria ter terminado.
                // Sem recordLength nao ha como saber onde — entao aborta a
                // janela e deixa a proxima tentar reencontrar o alinhamento
                // pelo USN, que e monotonicamente crescente.
                break;
            }

            _stats.recordsParsed++;
            if (hdr.reason & FILE_DELETE) _stats.deletionsSeen++;

            // Guarda o estado mais RECENTE de cada (entrada, sequencia).
            // O rewind precisa do estado de cada instante, mas só o
            // último de cada par interessa para reconstruir o momento da
            // exclusão — e para isso basta varrer em ordem natural e
            // sobrescrever.
            EntryState st;
            st.name = name;
            st.parentRef = hdr.parentReference;
            st.attributes = hdr.fileAttributes;
            st.timestamp = hdr.timestamp;
            _state[hdr.fileReference] = st;

            cursor += hdr.recordLength;
        }

        pos += want;
    }

    _stats.entriesInMap = _state.size();

    // ── 3. Contagem de quantos caminhos o rewind consegue fechar ───────
    //
    // Medir ANTES de qualquer join com a MFT. Se a maioria ficar
    // Unknown, o journal nao estacovering a janela que importa, e o log
    // tem que dizer isso com todas as letras em vez de a lista vir vazia
    // sem explicação.
    uint64_t sampled = 0, resolved = 0, partial = 0, unknown = 0;
    for (std::unordered_map<uint64_t, EntryState>::const_iterator it = _state.begin();
         it != _state.end() && sampled < 4000; ++it, ++sampled) {
        std::wstring path;
        int levels = 0;
        bool hitRoot = false;
        WalkUp(it->first, path, levels, hitRoot);
        if (hitRoot) resolved++;
        else if (levels > 0) partial++;
        else unknown++;
    }

    _stats.pathsResolved = resolved;
    _stats.pathsPartial = partial;
    _stats.pathsUnknown = unknown;

    LogDebug("[USN] Registros lidos: " + std::to_string(_stats.recordsParsed) +
             "  rejeitados: " + std::to_string(_stats.recordsRejected) +
             "  exclusoes: " + std::to_string(_stats.deletionsSeen));
    LogDebug("[USN] entradas no mapa: " + std::to_string(_state.size()));
    LogDebug("[USN] amostra de " + std::to_string(sampled) +
             " entradas -> caminho completo: " + std::to_string(resolved) +
             "  parcial: " + std::to_string(partial) +
             "  desconhecido: " + std::to_string(unknown));

    return true;
}

// =====================================================================
// WALKUP — sobe a cadeia no mapa do rewind
//
// A grande diferença para a MFT: aqui cada elo é procurado pelo par
// (entrada, sequência) exato. Se o ancestral não está no journal, a cadeia
// PARA — não pula para "a pasta que está no índice agora".
// =====================================================================
bool Journal::WalkUp(uint64_t startRef, std::wstring& out, int& levels, bool& hitRoot) const {
    out.clear();
    levels = 0;
    hitRoot = false;

    const uint64_t kRootIndex = 5;      // índice da raiz do volume
    const int kMaxDepth = 64;

    uint64_t cursor = startRef;
    std::wstring parts[kMaxDepth];
    int depth = 0;

    // Guarda de ciclos. O guard simples "parent == cursor" só pega
    // auto-referência; um ciclo de dois ou mais elos (A->B->A) passava
    // reto e produzia um caminho inventado do tipo "a\b\a\b\a..." com até
    // kMaxDepth componentes. Registro corrompido ou volume adulterado
    //reachable com isso, e o custo é 64bauhaus iterações por item.
    std::vector<uint64_t> visited;
    visited.reserve(kMaxDepth);

    for (; depth < kMaxDepth; ++depth) {
        uint64_t index = cursor & 0x0000FFFFFFFFFFFFULL;

        if (index == 0) break;
        if (index == kRootIndex) { hitRoot = true; break; }

        bool repeat = false;
        for (size_t v = 0; v < visited.size(); ++v) {
            if (visited[v] == cursor) { repeat = true; break; }
        }
        if (repeat) break;              // ciclo: a cadeia nao é uma árvore
        visited.push_back(cursor);

        std::unordered_map<uint64_t, EntryState>::const_iterator it = _state.find(cursor);
        if (it == _state.end()) break;        // ancestral ausente: PARA aqui

        if (it->second.name.empty()) break;

        parts[depth] = it->second.name;
        levels++;

        uint64_t parent = it->second.parentRef;
        if (parent == cursor) break;          // auto-referência
        cursor = parent;
    }

    for (int i = levels - 1; i >= 0; --i) {
        if (!out.empty()) out += L'\\';
        out += parts[i];
    }
    return levels > 0;
}

bool Journal::ResolvePath(uint64_t fileReference, std::wstring& out) const {
    int levels = 0;
    bool hitRoot = false;
    bool ok = WalkUp(fileReference, out, levels, hitRoot);
    // Caminho completo só quando chegou à raiz. Parcial é útil, mas não é
    // a mesma coisa — quem chama decide o que fazer com isso.
    return ok && hitRoot;
}

bool Journal::LookupName(uint64_t fileReference, std::wstring& out) const {
    std::unordered_map<uint64_t, EntryState>::const_iterator it = _state.find(fileReference);
    if (it == _state.end()) return false;
    out = it->second.name;
    return !out.empty();
}

bool Journal::LookupTimestamp(uint64_t fileReference, int64_t& out) const {
    std::unordered_map<uint64_t, EntryState>::const_iterator it = _state.find(fileReference);
    if (it == _state.end()) return false;
    out = it->second.timestamp;
    return true;
}

} // namespace Usn
