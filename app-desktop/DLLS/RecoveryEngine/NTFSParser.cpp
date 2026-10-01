#include "NTFSParser.h"
#include "FileSignatures.h"
#include <algorithm>
#include <cwctype>

namespace NTFS {

    // Bytes lidos do conteúdo para conferir a assinatura. 64 cobre o maior
    // offset que usamos (ftyp em 4, subtipo RIFF em 8) com folga.
    static const size_t kProbeBytes = 64;

    // Profundidade máxima do walk de ancestrais. NTFS não impõe limite, mas
    // na prática a árvore tem < 20 níveis; o teto evita laço infinito se
    // houver um ciclo de references corrompidas (o que acontece em registros
    // parcialmente sobrescritos).
    static const int kMaxDepth = 24;

    // Teto de tamanho para "arquivo plausível". Um $DATA de 8 EiB significa
    // runlist corrompido, não um arquivo de 8 EiB.
    static const uint64_t kAbsurdSize = 4ULL * 1024 * 1024 * 1024 * 1024; // 4 TiB

    // ---------------------------------------------------------------------
    // Pastas de sistema: o usuário não recupera C:\Windows\System32, e essas
    // entradas dominam a lista — cada atualização do Windows deleta centenas
    // de DLLs, drivers e arquivos de log, todos com nome plausível e
    // assinatura PERFEITA no cabeçalho. Sem esta exclusão, "validar a
    // assinatura" não resolve o problema: eles são arquivos de verdade, só
    // que não são o que o usuário quer.
    // ---------------------------------------------------------------------
// =====================================================================
// ESCOLHA DO $FILE_NAME POR NAMESPACE
//
// Um registro MFT pode ter ate 4 atributos $FILE_NAME, um por namespace:
//
//   0 = POSIX        mesmo conteudo do Win32
//   1 = Win32        O NOME LONGO — o que o usuario digitou
//   2 = DOS + Win32  contem o nome longo
//   3 = DOS          SO o 8.3 truncado: "TEST~1.PDF"
//
// A escolha errada aqui e o que faria "test.pdf" aparecer como
// "TEST~1.PDF" na lista — exatamente o defeito que o usuario nao aceita.
//
// A logica anterior aceitava (ns == 3 && bestNs == 2), ou seja, deixava o
// nome DOS 8.3 SUBSTITUIR um DOS+Win32 que ja continha o nome longo. Era um
// rebaixamento silencioso, e o sintoma seria nome truncado na lista.
//
//   rank 3 = Win32        melhor
//   rank 2 = POSIX        mesmo conteudo do Win32
//   rank 1 = DOS + Win32  contem o nome longo
//   rank 0 = DOS 8.3      ultimo recurso
// =====================================================================
static int NsRank(uint8_t ns) {
    switch (ns) {
        case 1: return 3;   // Win32
        case 0: return 2;   // POSIX
        case 2: return 1;   // DOS + Win32
        default: return 0;  // DOS (8.3) e o ultimo recurso
    }
}

static const char* NsName(uint8_t ns) {
    switch (ns) {
        case 0: return "POSIX";
        case 1: return "Win32";
        case 2: return "DOS+Win32";
        default: return "DOS-8.3";
    }
}

// =====================================================================
// ESTAMPA DE TEMPO -> ISO
//
// A data de criacao/modificacao do $STANDARD_INFORMATION e a prova
// independente de que o registro NAO foi reaproveitado. Um registro
// reatribuido tem data de criacao recente, e o nome que ele carrega e o do
// arquivo novo — nao do que foi apagado. Sem isso, "o nome esta certo" e
// so uma afirmacao; com isso, e verificavel.
// =====================================================================
static std::string FileTimeToIso(uint64_t ft) {
    if (ft == 0 || ft < 116444736000000000ULL) return "(sem data)";
    uint64_t secs = (ft - 116444736000000000ULL) / 10000000ULL;
    time_t t = (time_t)secs;
    struct tm tmv;
    if (gmtime_s(&tmv, &t) != 0) return "(data invalida)";
    char buf[32];
    if (strftime(buf, sizeof(buf), "%Y-%m-%d %H:%M:%S", &tmv) == 0) return "(data invalida)";
    return buf;
}

static bool IsSystemFolderName(const std::wstring& name) {
  static const wchar_t* const kSystem[] = {
  L"Windows", L"Program Files", L"Program Files (x86)",
  L"ProgramData", L"System Volume Information",
  L"PerfLogs", L"AppData", L"Windows.old", L"Recovery",
  L"MSOCache", L"DriverStore", L"Config.Msi", L"$WinREAgent",
  L"$SysReset"
  };
  // $Recycle.Bin NAO esta aqui de proposito.
  //
  // Esvaziar a lixeira e' o cenario mais comum de recuperacao que existe, e
  // o item deletado tem a cadeia de pais passando por $Recycle.Bin. Listar
  // essa pasta como "de sistema" descartava exatamente o arquivo que o
  // usuario acabou de perder. A lixeira e' tratada a parte: o registro $R
  // vira um item com o nome e o caminho verdadeiros, vindos do $I pareado.
  for (size_t i = 0; i < sizeof(kSystem) / sizeof(kSystem[0]); ++i) {
  if (_wcsicmp(name.c_str(), kSystem[i]) == 0) return true;
  }
  return false;
  }


    // ---------------------------------------------------------------------
    // Um nome de arquivo sobrescrito vira lixo Unicode: surrogates órfãos,
    // U+FFFD, caracteres de controle. Isso NÃO é cosmético — um nome com
    // surrogates órfãos quebra a conversão para UTF-8 e produz uma string
    // com bytes lixo, que é exatamente o "arquivo sem sentido" que o usuário
    // viu na lista.
    // ---------------------------------------------------------------------
    static bool IsNameSane(const std::wstring& name) {
        if (name.empty() || name.size() > 200) return false;

        size_t dots = 0;
        for (size_t i = 0; i < name.size(); ++i) {
            wchar_t c = name[i];

            if (c == L'.') { dots++; continue; }
            if (c < 0x20 || c == 0x7F) return false;        // controle
            if (c == 0xFFFD || c == 0xFFFE || c == 0xFFFF) return false; // substituição / reservados
            if (c == L'<' || c == L'>' || c == L':' || c == L'"' ||
                c == L'/' || c == L'\\' || c == L'|' || c == L'?' || c == L'*')
                return false;                                 // proibido pelo Windows

            // Surrogate: precisa vir em par seguido de um low surrogate.
            if (c >= 0xD800 && c <= 0xDBFF) {
                if (i + 1 >= name.size()) return false;
                wchar_t n = name[i + 1];
                if (n < 0xDC00 || n > 0xDFFF) return false;
                ++i;
            } else if (c >= 0xDC00 && c <= 0xDFFF) {
                return false;                                 // low surrogate sem par
            }
        }

        if (dots == name.size()) return false;                // "." ou ".."
        return true;
    }

    // "~$documento.docx" é o arquivo de lock do Office: zero conteúdo
    // útil, sempre presente quando algo foi aberto e não fechado direito.
    static bool IsTempName(const std::wstring& name) {
        if (name.size() >= 2 && name[0] == L'~' && name[1] == L'$') return true;
        return false;
    }

    // =====================================================================
    Parser::Parser(HANDLE hDrive)
        : _hDrive(hDrive), _mftOffset(0), _mftRecordSize(1024),
          _clusterSize(0), _volumeBytes(0) {
        memset(&_bs, 0, sizeof(_bs));
    }

    bool Parser::Init() {
        if (_hDrive == INVALID_HANDLE_VALUE) {
            LogDebug("[MFT] Init FALHOU: handle do disco invalido. "
                     "A origem nao e o parser — e a abertura do volume (veja o log acima).");
            return false;
        }

        uint8_t sector[4096] = {0};
        LARGE_INTEGER li; li.QuadPart = 0;
        if (!SetFilePointerEx(_hDrive, li, NULL, FILE_BEGIN)) {
            LogDebug("[MFT] Init FALHOU: nao foi possoir posicionar no inicio do volume.");
            return false;
        }

        DWORD bytesRead = 0;
        if (!ReadFile(_hDrive, sector, 512, &bytesRead, NULL)) {
            LogDebug("[MFT] Init FALHOU: ReadFile do boot sector falhou. "
                     "O handle nao concede leitura raw — o app precisa rodar como Administrador.");
            return false;
        }
        if (bytesRead < 512) {
            LogDebug("[MFT] Init FALHOU: boot sector leu apenas " + std::to_string(bytesRead) +
                     " de 512 bytes. Provavelmente o handle nao e um disco.");
            return false;
        }

        memcpy(&_bs, sector, sizeof(BootSector));
        if (_bs.endSignature != 0xAA55) {
            char sig[8];
            snprintf(sig, sizeof(sig), "%04X", _bs.endSignature);
            LogDebug(std::string("[MFT] Init FALHOU: assinatura 0xAA55 ausente. Este volume ") +
                     "NAO e NTFS, ou o handle nao aponta para o inicio do volume. " +
                     "Assinatura lida: 0x" + sig);
            return false;
        }
        if (_bs.bytesPerSector == 0 || _bs.sectorsPerCluster == 0) {
            LogDebug("[MFT] Init FALHOU: geometria do volume invalida (bytesPorSetor=" +
                     std::to_string(_bs.bytesPerSector) + ", setoresPorCluster=" +
                     std::to_string(_bs.sectorsPerCluster) + ").");
            return false;
        }

        // clustersPerMftRecord negativo => tamanho é 2^|n|. Com clusters de
        // 4096 bytes o registro MFT tem 4096 bytes, e não 1024 — ler fixo
        // 1024 truncaria o registro e a walk de atributos leria memória fora
        // do buffer. É um bug que só aparecia em volumes formatados com
        // clusters grandes (ou seja, em discos de SSD/NVMe modernos).
        if (_bs.clustersPerMftRecord < 0) {
            int shift = -static_cast<int>(_bs.clustersPerMftRecord);
            if (shift > 16) {
                LogDebug("[MFT] Init FALHOU: clustersPerMftRecord absurdo (2^" +
                         std::to_string(shift) + ").");
                return false;
            }
            _mftRecordSize = 1u << shift;
        } else {
            _mftRecordSize = static_cast<uint32_t>(_bs.clustersPerMftRecord) *
                             _bs.sectorsPerCluster * _bs.bytesPerSector;
        }
        if (_mftRecordSize < 512 || _mftRecordSize > 65536) {
            LogDebug("[MFT] Init FALHOU: tamanho de registro MFT invalido (" +
                     std::to_string(_mftRecordSize) + " bytes; esperado 512..65536).");
            return false;
        }

        _clusterSize = static_cast<uint32_t>(_bs.sectorsPerCluster) * _bs.bytesPerSector;
        if (_clusterSize == 0) {
            LogDebug("[MFT] Init FALHOU: clusterSize resulted em zero.");
            return false;
        }

        _mftOffset = _bs.mftLcn * static_cast<uint64_t>(_clusterSize);

        if (_bs.totalSectors64 != 0)
            _volumeBytes = _bs.totalSectors64 * _bs.bytesPerSector;
        else
            _volumeBytes = static_cast<uint64_t>(_bs.totalSectors32) * _bs.bytesPerSector;

        // O probe precisa de uma JANELA alinhada ao setor, nao dos 64 bytes
        // que o chamador quer ver: com NO_BUFFERING a transferencia tem de ser
        // multipla do setor. Um cluster cobre qualquer geometria NTFS
        // (setor <= 4096) com folga.
        const size_t probeWindow = (_clusterSize > 4096) ? _clusterSize : 4096;
        if (!_probe.Allocate(probeWindow) || !_recordBuf.Allocate(_mftRecordSize)) {
            LogDebug("[MFT] Init FALHOU: nao foi possivel alocar os buffers de I/O.");
            return false;
        }

        LogDebug("[MFT] Init OK: cluster=" + std::to_string(_clusterSize) +
                 "B  registroMFT=" + std::to_string(_mftRecordSize) +
                 "B  mftLcn=" + std::to_string(_bs.mftLcn) +
                 "  volume=" + std::to_string(_volumeBytes) + "B");
        return true;
    }

    // =====================================================================
    // RUNLIST DA $MFT
    // =====================================================================
    // A posição de um registro depende de em qual run ele cai, porque um run
    // é um trecho CONTÍGUO e o próximo pode estar em outro lugar do disco.
    // Sem os runs, não dá para traduzir índice -> offset físico, e portanto
    // não dá para resolver o nome de um diretório pai.
    bool Parser::ParseMftRuns(const uint8_t* mft0, uint32_t recordBytes) {
        _mftRuns.clear();

        MFTHeader* header = (MFTHeader*)mft0;
        if (header->magic != 0x454C4946) return false;   // "FILE"

        uint32_t limit = header->usedSize;
        if (limit < sizeof(MFTHeader) || limit > recordBytes) limit = recordBytes;

        AttributeHeader* attr = (AttributeHeader*)(mft0 + header->attrOffset);
        while ((uint8_t*)attr + sizeof(AttributeHeader) <= mft0 + limit && attr->type != 0xFFFFFFFF) {
            if (attr->length < sizeof(AttributeHeader)) break;

            if (attr->type == 0x80 && attr->nonResident) {   // $DATA
                const uint8_t* runList = (const uint8_t*)attr + attr->nonResidentInfo.runOffset;
                const uint8_t* runEnd = mft0 + limit;
                uint64_t currentLcn = 0;

                while (runList < runEnd && *runList != 0) {
                    uint8_t lenLength = (*runList) & 0x0F;
                    uint8_t offsetLength = (*runList) >> 4;
                    if (lenLength == 0 || offsetLength == 0) break;
                    runList++;
                    if (runList + lenLength + offsetLength > runEnd) break;

                    uint64_t length = 0;
                    for (int i = 0; i < lenLength; i++) length |= (static_cast<uint64_t>(runList[i]) << (8 * i));
                    runList += lenLength;

                    int64_t offset = 0;
                    for (int i = 0; i < offsetLength; i++) offset |= (static_cast<int64_t>(runList[i]) << (8 * i));

                    // Extensão de sinal para deltas negativos. Sem ela, um run
                    // localizado antes do anterior (comum em MFT fragmentada)
                    // produz um LCN absurdo e a varredura lê lixo.
                    if (offsetLength > 0 && (runList[offsetLength - 1] & 0x80)) {
                        for (int i = offsetLength; i < 8; ++i) offset |= (static_cast<int64_t>(0xFF) << (8 * i));
                    }
                    runList += offsetLength;

                    if (currentLcn + static_cast<uint64_t>(offset) > _volumeBytes / _clusterSize + 1) break;

                    currentLcn += static_cast<uint64_t>(offset);
                    _mftRuns.push_back(std::make_pair(currentLcn, length));
                }
                break;   // $DATA só (a $MFT tem um)
            }
            attr = (AttributeHeader*)((uint8_t*)attr + attr->length);
        }

        if (_mftRuns.empty()) {
            // Fallback: a $MFT começa no LCN do boot sector. Só é válido se o
            // registro 0 realmente estiver lá, e vale a pena arriscar porque
            // sem runs não temos varredura nenhuma.
            _mftRuns.push_back(std::make_pair(_bs.mftLcn, (uint64_t)512 * 1024));
        }
        return !_mftRuns.empty();
    }

    // =====================================================================
    // O REGISTRO 0 PRECISA DE UM LEITOR PROPRIO.
    //
    // ReadMFTRecordByIndex() traduz "indice" -> "offset de disco" andando a
    // runlist da $MFT. Mas a runlist ESTA DENTRO do registro 0. Logo, na
    // primeira chamada a lista esta vazia e a funcao retorna false sem
    // deixar rastro nenhum no log — foi exatamente o que aconteceu.
    //
    // O registro 0 nao depende de runlist: o boot sector ja diz onde a MFT
    // comeca (mftLcn), e _mftOffset = mftLcn * clusterSize. Esse e o unico
    // caminho valido para o primeiro registro, e o mesmo que o codigo
    // original usava.
    bool Parser::ReadMFTRecordZero(uint8_t* out) {
        if (_mftRecordSize == 0 || _mftOffset == 0) {
            LogDebug("[MFT] ReadMFTRecordZero: volume sem informacao de MFT no boot sector.");
            return false;
        }
        LogDebug("[MFT] Lendo registro 0 da $MFT no offset " + std::to_string(_mftOffset) + "...");
        return ReadMFTRecordAtOffset(_mftOffset, out);
    }

    // =====================================================================
    // Leitura crua de um unico registro, a partir de um offset de disco ja
    // calculado. Centraliza o I/O NO_BUFFERING para que o erro do Windows
    // (tipicamente 87, buffer desalinhado) apareca sempre com nome.
    bool Parser::ReadMFTRecordAtOffset(uint64_t byteOffset, uint8_t* out) {
        LARGE_INTEGER li;
        li.QuadPart = static_cast<LONGLONG>(byteOffset);
        if (!SetFilePointerEx(_hDrive, li, NULL, FILE_BEGIN)) {
            DWORD err = GetLastError();
            LogDebug("[MFT] SetFilePointerEx falhou no offset " + std::to_string(byteOffset) +
                     " -> erro " + std::to_string(err) + ".");
            return false;
        }

        DWORD got = 0;
        if (!ReadFile(_hDrive, out, _mftRecordSize, &got, NULL)) {
            DWORD err = GetLastError();
            LogDebug("[MFT] ReadFile falhou no offset " + std::to_string(byteOffset) + ", " +
                     std::to_string(_mftRecordSize) + " bytes -> erro " + std::to_string(err) +
                     (err == 87 ? " (87 = ERROR_INVALID_PARAMETER: buffer nao alinhado ao setor; o handle usa NO_BUFFERING)"
                                : ""));
            return false;
        }
        if (got < _mftRecordSize) {
            LogDebug("[MFT] Leitura curta no offset " + std::to_string(byteOffset) +
                     ": " + std::to_string(got) + " de " + std::to_string(_mftRecordSize) + " bytes.");
            memset(out + got, 0, _mftRecordSize - got);
        }
        return true;
    }

    // =====================================================================
    bool Parser::ReadMFTRecordByIndex(uint64_t index, uint8_t* out) {
        if (_mftRecordSize == 0 || _clusterSize == 0) return false;
        if (_mftRuns.empty()) {
            // NUNCA mais um retorno mudo aqui. A ausencia de log foi o que
            // escondeu a dependencia circular acima por um ciclo inteiro de
            // build e teste.
            LogDebug("[MFT] ReadMFTRecordByIndex: runlist vazia ao pedir o registro " +
                     std::to_string(index) + ". Chame ReadMFTRecordZero() primeiro.");
            return false;
        }

        // ─────────────────────────────────────────────────────────────────
        // A CONTA PRECISA SER EM BYTES, E NAO EM CLUSTERS.
        //
        // A versao anterior fazia:
        //     startCluster = index * (_mftRecordSize / _clusterSize)
        // Nestes dados registroMFT=1024 e cluster=4096, entao 1024/4096 = 0 em
        // divisao INTEIRA — e startCluster dava 0 para TODO registro. A MFT
        // inteira colapsava no primeiro cluster.
        //
        // Num volume NTFS moderno o registro MFT tem 1024 bytes mesmo com
        // clusters de 4096, entao registro e cluster NAO tem relacao de
        // multiplo inteiro. O certo e tratar a MFT como um fluxo de BYTES e
        // achar em qual run o offset do registro cai.
        // ─────────────────────────────────────────────────────────────────
        uint64_t targetByte = index * static_cast<uint64_t>(_mftRecordSize);
        uint64_t cumulative = 0;   // bytes ja consumidos dos runs anteriores

        for (size_t i = 0; i < _mftRuns.size(); ++i) {
            uint64_t runBytes = _mftRuns[i].second * static_cast<uint64_t>(_clusterSize);
            if (targetByte < cumulative + runBytes) {
                uint64_t offsetInRun = targetByte - cumulative;
                uint64_t byteOffset = _mftRuns[i].first * static_cast<uint64_t>(_clusterSize) + offsetInRun;
                return ReadMFTRecordAtOffset(byteOffset, out);
            }
            cumulative += runBytes;
        }

        LogDebug("[MFT] ReadMFTRecordByIndex: indice " + std::to_string(index) +
                 " fora dos runs da MFT (" + std::to_string(targetByte) + " bytes).");
        return false;
    }

    // =====================================================================
    bool Parser::ResolveDirectoryName(uint64_t index, std::wstring& out) {
        std::unordered_map<uint64_t, std::wstring>::const_iterator it = _dirNameCache.find(index);
        if (it != _dirNameCache.end()) {
            out = it->second;
            return !out.empty();
        }

        if (!_recordBuf.Data()) return false;
        uint8_t* buf = _recordBuf.Data();
        if (!ReadMFTRecordByIndex(index, buf)) return false;

        MFTHeader* header = (MFTHeader*)buf;
        if (header->magic != 0x454C4946) {
            _dirNameCache[index] = L"";
            return false;
        }

        uint32_t limit = header->usedSize;
        if (limit < sizeof(MFTHeader) || limit > _mftRecordSize) limit = _mftRecordSize;

        std::wstring best;
        uint8_t bestNs = 0xFF;
        AttributeHeader* attr = (AttributeHeader*)(buf + header->attrOffset);
        while ((uint8_t*)attr + sizeof(AttributeHeader) <= buf + limit && attr->type != 0xFFFFFFFF) {
            if (attr->length < sizeof(AttributeHeader)) break;
            if (attr->type == 0x30 && !attr->nonResident) {   // $FILE_NAME
                const uint8_t* val = (const uint8_t*)attr + attr->resident.valueOffset;
                if (val + sizeof(FileNameAttr) <= buf + limit) {
                    const FileNameAttr* fn = (const FileNameAttr*)val;
                    if ((const uint8_t*)fn->name + (size_t)fn->nameLength * 2 <= buf + limit) {
                        uint8_t ns = fn->nameNamespace;
                        if (bestNs == 0xFF || (ns == 1 && bestNs != 1) ||
                            (ns == 3 && bestNs == 2) || (ns == 0 && bestNs == 2)) {
                            best.assign(fn->name, fn->nameLength);
                            bestNs = ns;
                        }
                    }
                }
            }
            attr = (AttributeHeader*)((uint8_t*)attr + attr->length);
        }

        _dirNameCache[index] = best;
        out = best;
        return !out.empty();
    }

    // =====================================================================
    // LIXEIRA — IDENTIFICAÇÃO DO PAR $I / $R
    //
    // O NTFS nomeia os dois arquivos da lixeira com o MESMO identificador:
    //     $I3N7QK2M.tmp    metadados  (caminho e tamanho originais)
    //     $R3N7QK2M.pdf     conteúdo    (extensão original preservada)
    //
    // O $I normalmente NÃO tem extensão; o $R sempre tem, porque é ela que
    // diz ao Explorer qual ícone mostrar. Por isso o id vai até o primeiro
    // ponto, e não até o fim da string.
    // =====================================================================
    bool Parser::RecycleIdFromName(const std::wstring& name, wchar_t kind, std::wstring& id) {
        id.clear();
        if (name.size() < 4) return false;
        if (name[0] != L'$' || name[1] != kind) return false;

        size_t end = 2;
        while (end < name.size() && name[end] != L'.' && name[end] != L'\0') end++;
        if (end == 2) return false;

        // =================================================================
        // O FORMATO REAL DO ID DA LIXEIRA
        //
        // Eu assumi 24 caracteres (de memoria, e estava errado). No Windows
        // real o NTFS gera NOME de 6 caracteres:
        //
        //     $RL809P1.node      -> id "L809P1"  (6)
        //     $RHNV9LV.tmp       -> id "HNV9LV"  (6)
        //     $R6IW0YN           -> id "6IW0YN"  (6, e sem extensao = PASTA)
        //
        // Com o minimo em 7, NENHUM item real passava.Foi assim que uma
        // pasta inteira esvaziada da lixeira simplesmente nao apareceu, e o
        // log dizia so "$I lidos = 0", sem sugerir que o validador era o
        // culpado.
        //
        // O piso de 6 sozinho deixaria "$Recycle.Bin" passar, porque o id
        // dele ("ecycle") tem exatamente 6 caracteres. Por isso o piso de 6
        // vem acompanhado da guarda explicita de nome logo abaixo — as duas
        // coisas juntas, e nao uma so.
        // =================================================================
        size_t len = end - 2;
        if (len < 6 || len > 32) return false;

        // Guarda explicita para os nomes do proprio NTFS que comecam com $R.
        // Nao depende do comprimento, entao continua valendo se o Windows
        // mudar o formato do identificador.
        if (_wcsicmp(name.c_str(), L"$Recycle.Bin") == 0 ||
            _wcsicmp(name.c_str(), L"$Recycle") == 0) return false;

        // O alfabeto real do Windows e' hexadecimal em CAIXA ALTA
        // (ex.: $R3N7QK2M...), entao aceitar so [a-f] rejeitaria praticamente
        // todo item de verdade. Alfanumerico nas duas caixas cobre o formato
        // sem aceitar '!' ou espaco, que nunca aparecem num id legitimo.
        for (size_t i = 2; i < end; ++i) {
            wchar_t c = name[i];
            bool alnum = (c >= L'0' && c <= L'9') ||
                         (c >= L'A' && c <= L'Z') ||
                         (c >= L'a' && c <= L'z');
            if (!alnum) return false;
        }
        id.assign(name, 2, len);
        return true;
    }

    // =====================================================================
    // LIXEIRA — LEITURA DO $I
    //
    // Layout do $I (NTFS, Windows 10/11):
    //     0x00  int64   versão (1 ou 2)
    //     0x08  int64   tamanho do arquivo original
    //     0x10  FILETIME data da exclusão        <- só na versão 2
    //     0x18  uint32  comprimento do caminho em WCHARs
    //     0x1C  UTF-16LE caminho original
    //
    // A versão 1 (Windows XP/2003) não tem o campo de data, então o
    // comprimento do caminho começa em 0x10. Ler sempre em 0x18 faria o
    // caminho sair torto — e o sintoma seria exatamente o que vemos hoje:
    // um caminho que parece correto mas não é.
    // =====================================================================
    bool Parser::ParseRecycleInfo(const uint8_t* data, size_t len, RecycleEntry& out) const {
        out = RecycleEntry();
        if (!data || len < 0x14) return false;

        int64_t version = 0;
        memcpy(&version, data, 8);
        if (version != 1 && version != 2) return false;

        uint64_t originalSize = 0;
        memcpy(&originalSize, data + 8, 8);
        if (originalSize == 0 || originalSize > (uint64_t)1 << 40) return false;

        size_t pathLenOffset = (version == 2) ? 0x18 : 0x10;
        if (len < pathLenOffset + 4) return false;

        uint32_t chars = 0;
        memcpy(&chars, data + pathLenOffset, 4);
        if (chars == 0 || chars > 32767) return false;

        const uint8_t* pathBytes = data + pathLenOffset + 4;
        size_t available = len - (pathLenOffset + 4);
        size_t needed = static_cast<size_t>(chars) * 2;
        if (needed > available) needed = available & ~static_cast<size_t>(1);

        // Decodifica UTF-16LE -> wstring.
        std::wstring path;
        path.reserve(needed / 2);
        for (size_t i = 0; i + 1 < needed; i += 2) {
            wchar_t c = static_cast<wchar_t>(data[pathLenOffset + 4 + i] |
                                             (static_cast<wchar_t>(data[pathLenOffset + 5 + i]) << 8));
            if (c == L'\0') break;
            path.push_back(c);
        }
        if (path.size() < 3) return false;

        // O caminho original é absoluto (C:\...). Cortar na barra invertida
        // final dá o nome do arquivo, que é o que o usuário reconhece.
        size_t slash = path.find_last_of(L'\\');
        out.originalName = (slash == std::wstring::npos) ? path : path.substr(slash + 1);
        out.originalPath = path;
        out.originalSize = originalSize;
        out.valid = true;
        return true;
    }

    // =====================================================================
    // Lê os primeiros bytes de um arquivo não-residente, numa janela alinhada
    // ao setor. Mesma regra do probe: com NO_BUFFERING o comprimento tem de
    // ser múltiplo do setor, então lê-se a janela e copia-se o trecho.
    // =====================================================================
    bool Parser::ReadFirstBytes(uint64_t byteOffset, size_t want, uint8_t* dst) {
        if (!dst || want == 0) return false;
        const uint32_t sector = _bs.bytesPerSector ? _bs.bytesPerSector : 512;

        uint64_t aligned = (byteOffset / sector) * sector;
        size_t delta = static_cast<size_t>(byteOffset - aligned);
        size_t readLen = ((delta + want + sector - 1) / sector) * sector;

        // Sector + want cobre o pior caso; a janela nunca passa de 1 cluster.
        size_t cap = (size_t)sector + want;
        std::vector<uint8_t> window(readLen, 0);
        if (readLen > cap) return false;

        LARGE_INTEGER li; li.QuadPart = static_cast<LONGLONG>(aligned);
        if (!SetFilePointerEx(_hDrive, li, NULL, FILE_BEGIN)) return false;
        DWORD got = 0;
        if (!ReadFile(_hDrive, window.data(), static_cast<DWORD>(readLen), &got, NULL)) return false;
        if (got < delta + want) return false;

        memcpy(dst, window.data() + delta, want);
        return true;
    }

    // =====================================================================
    // PARSE DE RUNLIST — extraído porque ProcessMFTRecord e a busca do
    // USN Journal precisam exatamente do mesmo cálculo, e duas cópias
    // divergem.
    //
    // devolve o offset físico do PRIMEIRO cluster e o total de bytes.
    // =====================================================================
    bool Parser::ParseRunList(const AttributeHeader* attr, const uint8_t* bufEnd, const uint8_t* runListStart,
                              uint64_t& firstByteOffset, uint64_t& totalBytes, uint32_t& runCount) {
        firstByteOffset = 0;
        totalBytes = 0;
        runCount = 0;
        if (!attr || !runListStart || runListStart >= bufEnd) return false;

        const uint8_t* p = runListStart;
        uint64_t currentLcn = 0;

        while (p < bufEnd && *p != 0) {
            uint8_t lenLength = (*p) & 0x0F;
            uint8_t offsetLength = (*p) >> 4;
            if (lenLength == 0) break;
            p++;
            if (p + lenLength + offsetLength > bufEnd) break;

            uint64_t length = 0;
            for (int i = 0; i < lenLength; i++) length |= (static_cast<uint64_t>(p[i]) << (8 * i));
            p += lenLength;

            int64_t delta = 0;
            for (int i = 0; i < offsetLength; i++) delta |= (static_cast<int64_t>(p[i]) << (8 * i));
            if (offsetLength > 0 && (p[offsetLength - 1] & 0x80)) {
                for (int i = offsetLength; i < 8; ++i) delta |= (static_cast<int64_t>(0xFF) << (8 * i));
            }
            p += offsetLength;

            // Run sparse (sem LCN) não tem dado endereçável.
            if (offsetLength == 0) break;
            if (static_cast<uint64_t>(currentLcn) + static_cast<uint64_t>(delta) >
                _volumeBytes / _clusterSize + 1) break;

            currentLcn += static_cast<uint64_t>(delta);
            runCount++;
            if (runCount == 1) firstByteOffset = currentLcn * static_cast<uint64_t>(_clusterSize);
            totalBytes += length * static_cast<uint64_t>(_clusterSize);
        }
        return runCount > 0;
    }

    // =====================================================================
    // LOCALIZA O USN JOURNAL
    //
    // $UsnJrnl mora em $Extend e, na prática, tem índice baixo (24..30).
    // Varrer os 512 primeiros registros custa 512 KB de leitura em vez de
    // 1,4 GB, e cobre a totalidade dos casos reais. Se não achar, o log
    // diz — em vez de a lista de recuperação simplesmente vir sem
    // caminhos e ninguém saber por quê.
    //
    // O que interessa é o stream NOMEADO "$J" do atributo $DATA: é ele que
    // guarda o journal. O $DATA anônimo do $UsnJrnl é o próprio índice de
    // journals, com outro layout.
    // =====================================================================
    bool Parser::FindUsnJournal(uint64_t& dataOffset, uint64_t& dataBytes) {
        dataOffset = 0;
        dataBytes = 0;
        if (_mftRuns.empty()) return false;      // runs ainda nao resolvidos
        if (!_recordBuf.Data()) return false;

        // A Amplitude NAO pode ser presumida. $UsnJrnl mora em $Extend e o
        // NTFS nao garante indice fixo para ele: o $Extend tem $Quota, $ObjId,
        // $RmMetadata, $Reparse, $UsnJrnl e $LogFile, e a ordem de atribuicao
        // dos indices depende de como o volume foi formatado. Numa varredura
        // real o $UsnJrnl NAO estava nos primeiros 512 registros e o caminho
        // ficou irreconstituivel sem que nada indicasse o motivo.
        //
        // 4096 registros sao 4 MiB de leitura — barato mesmo numa engine que
        // varre 1,4 GB de MFT — e cobrem com folga o indice real.
        const uint32_t kProbe = 4096;
        uint8_t* buf = _recordBuf.Data();

        unsigned named = 0;         // quantos registros com nome '$' foram vistos
        bool sawExtend = false;
        uint64_t extendIndex = 0;

        for (uint64_t i = 1; i <= kProbe; ++i) {
            if (!ReadMFTRecordByIndex(i, buf)) continue;

            MFTHeader* header = (MFTHeader*)buf;
            if (header->magic != 0x454C4946) continue;

            uint32_t limit = header->usedSize;
            if (limit < sizeof(MFTHeader) || limit > _mftRecordSize) limit = _mftRecordSize;

            // Nome do registro
            std::wstring recordName;
            AttributeHeader* attr = (AttributeHeader*)(buf + header->attrOffset);
            while ((const uint8_t*)attr + sizeof(AttributeHeader) <= buf + limit && attr->type != 0xFFFFFFFF) {
                if (attr->length < sizeof(AttributeHeader)) break;

                if (attr->type == 0x30 && !attr->nonResident) {
                    const uint8_t* val = (const uint8_t*)attr + attr->resident.valueOffset;
                    if (val + sizeof(FileNameAttr) <= buf + limit) {
                        const FileNameAttr* fn = (const FileNameAttr*)val;
                        if ((const uint8_t*)fn->name + (size_t)fn->nameLength * 2 <= buf + limit &&
                            (fn->nameNamespace == 1 || fn->nameNamespace == 3)) {
                            recordName.assign(fn->name, fn->nameLength);
                        }
                    }
                }

                // $DATA com NOME == "$J"
                if (attr->type == 0x80 && attr->nonResident && attr->nameLength == 4) {
                    const uint8_t* nm = (const uint8_t*)attr + attr->nameOffset;
                    if (nm + 4 <= buf + limit && nm[0] == '$' && nm[1] == 0 &&
                        nm[2] == 'J' && nm[3] == 0) {
                        const uint8_t* rl = (const uint8_t*)attr + attr->nonResidentInfo.runOffset;
                        uint64_t off = 0, bytes = 0;
                        uint32_t runs = 0;
                        if (ParseRunList(attr, buf + limit, rl, off, bytes, runs)) {
                            dataOffset = off;
                            // O journal ocupa a area ALOCADA. O campo
                            // actualSize pode ser menor se o journal ainda
                            // nao deu a volta; alocado e o limite seguro.
                            dataBytes = attr->nonResidentInfo.allocatedSize;
                            if (dataBytes == 0) dataBytes = bytes;
                            LogDebug("[USN] $UsnJrnl:$J encontrado no registro MFT " +
                                     std::to_string(i) + ": offset=" + std::to_string(off) +
                                     "B  tamanho=" + std::to_string(dataBytes) +
                                     "B  runs=" + std::to_string(runs));
                            return dataOffset != 0 && dataBytes > 0;
                        }
                        // Achou o $J mas a runlist nao parseou: sem o offset
                        // fisico nao ha leitura possivel. Dizer isso e
                        // obrigatorio — o sintoma silencioso seria "0
                        // caminhos", identico ao de um journal vazio.
                        {
                            char roff[16];
                            snprintf(roff, sizeof(roff), "0x%X", attr->nonResidentInfo.runOffset);
                            LogDebug("[USN] Registro " + std::to_string(i) +
                                     " tem o stream $J mas a runlist nao produziu offset." +
                                     " nonResident=" + std::to_string((int)attr->nonResident) +
                                     " runOffset=" + roff);
                        }
                    }
                }

                attr = (AttributeHeader*)((const uint8_t*)attr + attr->length);
            }

            // Diagnostico: quais registros de metadados existem e onde. Sem
            // isto, "nao encontrado" nao diferencia "$UsnJrnl esta no
            // indice 8000" de "$UsnJrnl nao existe neste volume" — e as
            // duas situacoes pedem correcoes completamente diferentes.
            if (!recordName.empty() && recordName[0] == L'$') {
                if (_wcsicmp(recordName.c_str(), L"$Extend") == 0) { sawExtend = true; extendIndex = i; }
                if (named < 24) {
                    char nb[128] = {0};
                    WideCharToMultiByte(CP_UTF8, 0, recordName.c_str(), -1, nb, sizeof(nb) - 1, NULL, NULL);
                    LogDebug("[USN]   registro " + std::to_string(i) + " = \"" + nb + "\"");
                    named++;
                }
            }
        }

        LogDebug("[USN] $UsnJrnl nao encontrado nos primeiros " + std::to_string(kProbe) +
                 " registros (" + std::to_string(named) + " metadados '$' vistos" +
                 (sawExtend ? ", $Extend PRESENTE" : ", $Extend AUSENTE") + ").");

        // ── DIAGNÓSTICO CONCLUSIVO ────────────────────────────────────
        //
        // "Não achei em N registros" e "o journal não existe" são coisas
        // diferentes, e exigem respostas diferentes do usuário: a primeira
        // seria bug meu (recorte curto demais); a segunda é uma decisão do
        // sistema operacional que ninguém pode corrigir por inspetiva.
        //
        // $Extend está ACHADO e os filhos dele vêm logo em seguida. Se os
        // 64 registros depois de $Extend não contiverem $UsnJrnl, o journal
        // não está habilitado — porque o NTFS cria o registro no momento em
        // que o journal é ligado, e não depois.
        if (sawExtend && extendIndex > 0 && extendIndex < kProbe) {
            unsigned after = 0;
            uint64_t lastChild = 0;
            for (uint64_t i = extendIndex + 1; i <= extendIndex + 64 && i < kProbe; ++i) {
                if (!ReadMFTRecordByIndex(i, buf)) continue;
                MFTHeader* h = (MFTHeader*)buf;
                if (h->magic != 0x454C4946) continue;
                uint32_t lim = h->usedSize;
                if (lim < sizeof(MFTHeader) || lim > _mftRecordSize) lim = _mftRecordSize;
                AttributeHeader* a = (AttributeHeader*)(buf + h->attrOffset);
                while ((const uint8_t*)a + sizeof(AttributeHeader) <= buf + lim && a->type != 0xFFFFFFFF) {
                    if (a->length < sizeof(AttributeHeader)) break;
                    if (a->type == 0x30 && !a->nonResident) {
                        const uint8_t* v = (const uint8_t*)a + a->resident.valueOffset;
                        if (v + sizeof(FileNameAttr) <= buf + lim) {
                            const FileNameAttr* fn = (const FileNameAttr*)v;
                            if ((const uint8_t*)fn->name + (size_t)fn->nameLength * 2 <= buf + lim &&
                                (fn->nameNamespace == 1 || fn->nameNamespace == 3)) {
                                std::wstring nm2(fn->name, fn->nameLength);
                                char nb2[128] = {0};
                                WideCharToMultiByte(CP_UTF8, 0, nm2.c_str(), -1, nb2, sizeof(nb2) - 1, NULL, NULL);
                                if (nm2[0] == L'$') {
                                    after++;
                                    if (lastChild == 0) lastChild = i;
                                    if (_shownExtendChild < 12) {
                                        LogDebug("[USN]   filho de $Extend: registro " + std::to_string(i) +
                                                 " = \"" + nb2 + "\"");
                                        _shownExtendChild++;
                                    }
                                }
                            }
                        }
                    }
                    a = (AttributeHeader*)((const uint8_t*)a + a->length);
                }
            }
            LogDebug("[USN] $Extend esta no indice " + std::to_string(extendIndex) +
                     " e tem " + std::to_string(after) +
                     " filho(s) de metadados. $UsnJrnl NAO esta entre eles.");
            LogDebug("[USN] CONCLUSAO: o USN Journal nao esta habilitado neste volume. "
                     "Nao e falha de leitura — o registro $UsnJrnl nao existe na MFT. "
                     "Para o rewind funcionar seria preciso 'fsutil usn createjournal C:' "
                     "(Administrador), e so passa a valer para exclusoes FUTURAS.");
            _stats.usnJournalAbsent = true;
        } else if (!sawExtend) {
            LogDebug("[USN] CONCLUSAO: $Extend nao encontrado em " + std::to_string(kProbe) +
                     " registros — a MFT esta com estrutura inesperada.");
        }
        return false;
    }

    // =====================================================================
    //
    // Sobe do pai até a raiz conferindo, a cada degrau, TRÊS coisas:
    //
    //   1. o registro existe e é "FILE"
    //   2. o número de sequência bate com o da referência   <- a peça-chave
    //   3. o bit 0x02 de flags está ligado, ou seja, é DIRETÓRIO
    //
    // Se qualquer uma falha, a cadeia quebrou: para ali e o caminho é
    // marcado como parcial. Antes, nenhuma dessas três conferências era feita
    // e o resultado eram caminhos com arquivos no meio.
    // =====================================================================
    bool Parser::ResolveParentPath(MftRef parent, PathResult& out) {
        out = PathResult();
        if (parent.index == kRootDirectory) { out.complete = true; return true; }
        if (!_recordBuf.Data()) return false;

        std::wstring parts[kMaxDepth];
        MftRef cursor = parent;
        int depth = 0;

        for (; depth < kMaxDepth; ++depth) {
            if (cursor.index == 0 || cursor.index == kRootDirectory) { out.complete = true; break; }

            uint8_t* buf = _recordBuf.Data();
            if (!ReadMFTRecordByIndex(cursor.index, buf)) { out.failParse++; break; }

            MFTHeader* header = (MFTHeader*)buf;
            if (header->magic != 0x454C4946) { out.failMagic++; break; }

            // (2) A CONFERÊNCIA DE SÉQUÊNCIA. Se o registro foi reaproveitado,
            //     o número de sequência atual é maior que o gravado na
            //     referência — e a partir daí a cadeia é de outra árvore.
            if (header->sequenceNum != cursor.sequence) {
                out.failSequence++;
                // A DIREÇÃO da diferença é o dado que decide se a conferência
                // está certa. Reuso real de slot produz atual > referenciado.
                // Se aparecer o contrário — ou uma diferença enorme — o
                // problema é a EXTRAÇÃO da referência, não o disco, e aí a
                // conclusão inverte: 390 mil diretórios "reutilizados" seria
                // absurdo num volume, mas 390 mil referências mal lidas é
                // exatamente o que um erro de layout produz.
                if (header->sequenceNum > cursor.sequence) out.seqReused++;
                else                                    out.seqBackward++;
                // Amostra com nome do ancestral, para ver o que está lá.
                if (_shownSeqMismatch < 6) {
                    std::wstring nm;
                    uint32_t lim = header->usedSize;
                    if (lim < sizeof(MFTHeader) || lim > _mftRecordSize) lim = _mftRecordSize;
                    AttributeHeader* a2 = (AttributeHeader*)(buf + header->attrOffset);
                    while ((const uint8_t*)a2 + sizeof(AttributeHeader) <= buf + lim && a2->type != 0xFFFFFFFF) {
                        if (a2->length < sizeof(AttributeHeader)) break;
                        if (a2->type == 0x30 && !a2->nonResident) {
                            const uint8_t* v2 = (const uint8_t*)a2 + a2->resident.valueOffset;
                            if (v2 + sizeof(FileNameAttr) <= buf + lim) {
                                const FileNameAttr* f2 = (const FileNameAttr*)v2;
                                if (f2->nameNamespace == 1 || f2->nameNamespace == 3) {
                                    nm.assign(f2->name, f2->nameLength);
                                    break;
                                }
                            }
                        }
                        a2 = (AttributeHeader*)((const uint8_t*)a2 + a2->length);
                    }
                    char head[32];
                    snprintf(head, sizeof(head), "%02llu", (unsigned long long)_shownSeqMismatch + 1);
                    _shownSeqMismatch++;
                    char nmBuf[128] = {0};
                    if (!nm.empty())
                        WideCharToMultiByte(CP_UTF8, 0, nm.c_str(), -1, nmBuf, sizeof(nmBuf) - 1, NULL, NULL);
                    LogDebug(std::string("[MFT]   SEQ#") + head +
                             ": ref=" + std::to_string(cursor.sequence) +
                             " atual=" + std::to_string(header->sequenceNum) +
                             " idx=" + std::to_string(cursor.index) +
                             " flags=0x" + std::to_string((unsigned)header->flags) +
                             " ancestral=\"" + nmBuf + "\"");
                }
                break;
            }

            // (3) Tem que ser diretório. Um arquivo no meio da cadeia
            //     significa que a referência está errada.
            if (!(header->flags & 0x02)) { out.failNotDirectory++; break; }

            uint32_t limit = header->usedSize;
            if (limit < sizeof(MFTHeader) || limit > _mftRecordSize) limit = _mftRecordSize;

            std::wstring name;
            MftRef next;
            bool haveName = false, haveNext = false;
            uint8_t bestNs = 0xFF;

            AttributeHeader* attr = (AttributeHeader*)(buf + header->attrOffset);
            while ((const uint8_t*)attr + sizeof(AttributeHeader) <= buf + limit && attr->type != 0xFFFFFFFF) {
                if (attr->length < sizeof(AttributeHeader)) break;
                if (attr->type == 0x30 && !attr->nonResident) {          // $FILE_NAME
                    const uint8_t* val = (const uint8_t*)attr + attr->resident.valueOffset;
                    if (val + sizeof(FileNameAttr) <= buf + limit) {
                        const FileNameAttr* fn = (const FileNameAttr*)val;
                        if ((const uint8_t*)fn->name + (size_t)fn->nameLength * 2 <= buf + limit) {
                            uint8_t ns = fn->nameNamespace;
                            if (bestNs == 0xFF || (ns == 1 && bestNs != 1) || (ns == 3 && bestNs != 2)) {
                                name.assign(fn->name, fn->nameLength);
                                next = MftRef::From(fn->parentDirectory);
                                haveNext = true;
                                haveName = !name.empty();
                                bestNs = ns;
                                if (ns == 1) break;
                            }
                        }
                    }
                }
                attr = (AttributeHeader*)((const uint8_t*)attr + attr->length);
            }
            if (!haveName || !haveNext) { out.failParse++; break; }

            if (_wcsicmp(name.c_str(), L"$Recycle.Bin") == 0) out.hitRecycleBin = true;
            else if (IsSystemFolderName(name)) out.hitSystemFolder = true;

            parts[depth] = name;
            out.verifiedLevels++;
            cursor = next;
        }

        // Monta de trás para frente, descartando o que não foi verificado.
        for (int i = out.verifiedLevels - 1; i >= 0; --i) {
            if (!out.path.empty()) out.path += L'\\';
            out.path += parts[i];
        }
        // A cadeia ter terminado por profundidade (kMaxDepth) não é uma
        // quebra: é um caminho legitimamente profundo que não cabe no
        // orçamento de degraus. Differente de sequence mismatch, que é
        //-proof de reuso.
        if (!out.complete && out.failMagic == 0 && out.failSequence == 0 &&
            out.failNotDirectory == 0 && out.failParse == 0 && out.verifiedLevels > 0) {
            out.failNoRoot++;
        }
        return out.verifiedLevels > 0;
    }

    // =====================================================================
    bool Parser::ReadContentProbe(uint64_t offset, size_t want) {
        if (!_probe.Data()) return false;
        if (want > _probe.Size()) want = _probe.Size();
        if (want == 0) return false;

        // ─────────────────────────────────────────────────────────────────
        // COM NO_BUFFERING, O TAMANHO DA TRANSFERENCIA TAMBEM PRECISA SER
        // MULTIPLO DO SETOR. O buffer ja esta alinhado, mas 64 bytes nao sao
        // multiplos de 512, entao o Windows recusa a leitura INTEIRA com
        // ERROR_INVALID_PARAMETER (87).
        //
        // Como todo candidato que chegava aqui era descartado, o sintoma era
        // "conteudo ilegivel" em tudo e "ASSINATURA NAO CONFERE = 0" — a
        // validacao de conteudo jamais rodou. Nenhum log apontava a causa,
        // porque o falso do ReadFile nao dizia nada.
        //
        // A solucao e ler uma JANELA alinhada (setor inteiro) e brought de
        // volta para o inicio do buffer os bytes que o chamador quer.
        // ─────────────────────────────────────────────────────────────────
        const uint32_t sector = _bs.bytesPerSector ? _bs.bytesPerSector : 512;

        uint64_t alignedOffset = (offset / sector) * sector;
        size_t delta = static_cast<size_t>(offset - alignedOffset);
        size_t needed = delta + want;
        size_t readLen = ((needed + sector - 1) / sector) * sector;
        if (readLen > _probe.Size()) readLen = _probe.Size();

        LARGE_INTEGER li; li.QuadPart = static_cast<LONGLONG>(alignedOffset);
        if (!SetFilePointerEx(_hDrive, li, NULL, FILE_BEGIN)) {
            LogProbeFailure("SetFilePointerEx", offset, GetLastError());
            return false;
        }

        DWORD got = 0;
        if (!ReadFile(_hDrive, _probe.Data(), static_cast<DWORD>(readLen), &got, NULL)) {
            DWORD err = GetLastError();
            LogProbeFailure("ReadFile", offset, err);
            return false;
        }
        if (got < needed) {
            LogProbeFailure("leitura curta", offset, 0);
            return false;
        }

        // Traz os bytes pedidos para o inicio do buffer, porque o chamador
        // le sempre a partir de _probe.Data().
        if (delta) memmove(_probe.Data(), _probe.Data() + delta, want);
        return true;
    }

    // O probe roda uma vez por candidato e pode haver dezenas de milhares.
    // Logar cada falha enche o arquivo e nao acrescenta nada depois da
    // primeira — o que importa e o TOTAL e o motivo. Por isso: uma linha por
    // motivo, com contador.
    void Parser::LogProbeFailure(const char* what, uint64_t offset, DWORD err) {
        const char* key = (err == 87) ? "ReadFile(87 desalinhado)" : what;
        if (key == _lastProbeFailure) {
            _probeFailureCount++;
            return;
        }
        if (_lastProbeFailure && _probeFailureCount > 1) {
            LogDebug("[MFT] probe: " + std::string(_lastProbeFailure) + " x" +
                     std::to_string(_probeFailureCount) + " (ocorrido antes)");
        }
        _lastProbeFailure = key;
        _probeFailureCount = 1;
        LogDebug("[MFT] probe: " + std::string(what) + " falhou no offset " +
                 std::to_string(offset) + " -> erro " + std::to_string(err) +
                 (err == 87 ? " (87 = ERROR_INVALID_PARAMETER: tamanho/offset nao multiplo do setor com NO_BUFFERING)" : ""));
    }

    // =====================================================================
    // VARREDURA DA MFT
    // =====================================================================
    void Parser::ScanMFT(RecoveryScanner* scanner) {
        if (_mftRecordSize == 0) {
            LogDebug("[MFT] ScanMFT abortado: tamanho de registro zero (Init nao rodou).");
            return;
        }

        // Registro 0: de onde tiramos os runs da $MFT. Usa o leitor proprio
        // (offset do boot sector), NAO ReadMFTRecordByIndex — a runlist que
        // este ultimo precisa ainda esta dentro do registro que ele deveria ler.
        if (!_recordBuf.Data() || !ReadMFTRecordZero(_recordBuf.Data())) {
            // Sem o registro 0 não há scan confiável. A versão anterior caía
            // num LCN inventado e varria bytes aleatórios, produzindo a
            // lista de lixo que o usuário viu.
            LogDebug("[MFT] ScanMFT abortado: nao foi possivel ler o registro MFT 0 "
                     "(a $MFT). Sem ele nao ha onde comecar.");
            return;
        }
        if (!ParseMftRuns(_recordBuf.Data(), _mftRecordSize)) {
            LogDebug("[MFT] ScanMFT abortado: o registro 0 nao tem atributo $DATA "
                     "utilizavel para localizar a MFT.");
            return;
        }

        {
            std::string runs;
            uint64_t totalClusters = 0;
            for (size_t i = 0; i < _mftRuns.size(); ++i) {
                if (i) runs += ", ";
                runs += "run" + std::to_string(i) + "[lcn=" + std::to_string(_mftRuns[i].first) +
                        ",len=" + std::to_string(_mftRuns[i].second) + "]";
                totalClusters += _mftRuns[i].second;
            }
            LogDebug("[MFT] MFT localizada: " + std::to_string(_mftRuns.size()) +
                     " run(s), " + std::to_string(totalClusters) + " clusters -> " + runs);
        }

        // =================================================================
        // USN JOURNAL — carregado ANTES do loop, para que cada registro
        // Percent ja tenha a segunda fonte disponivel na hora de montar o
        // caminho. Carregar depois exigiria uma segunda passada de 1,4
        // milhao de registros.
        // =================================================================
        {
            uint64_t usnOffset = 0, usnBytes = 0;
            if (FindUsnJournal(usnOffset, usnBytes)) {
                _usnLoaded = _usn.Load(_hDrive, usnOffset, usnBytes, _bs.bytesPerSector);
                if (!_usnLoaded)
                    LogDebug("[USN] Journal localizado mas a leitura falhou. Seguindo so com a MFT.");
            }
        }

        const uint32_t recordsPerChunk = 8192;               // 8 MiB por leitura
        const uint32_t chunkBytes = recordsPerChunk * _mftRecordSize;

        // Alinhado a PAGINA, e nao std::vector: o handle do volume usa
        // FILE_FLAG_NO_BUFFERING, que exige buffer alinhado ao setor. Um
        // std::vector alinha a 16 bytes e o ReadFile falharia com erro 87.
        AlignedBuffer chunk;
        if (!chunk.Allocate(chunkBytes)) {
            LogDebug("[MFT] ScanMFT abortado: nao foi possivel alocar o buffer de " +
                     std::to_string(chunkBytes) + " bytes.");
            return;
        }

        uint64_t globalIndex = 0;
        uint64_t bytesDone = 0;
        uint64_t bytesTotal = 0;
        for (size_t r = 0; r < _mftRuns.size(); ++r)
            bytesTotal += _mftRuns[r].second * static_cast<uint64_t>(_clusterSize);

        for (size_t r = 0; r < _mftRuns.size(); ++r) {
            if (!scanner->IsScanningNow()) {
                LogDebug("[MFT] Varredura interrompida pelo usuario.");
                break;
            }

            uint64_t runOffsetBytes = _mftRuns[r].first * static_cast<uint64_t>(_clusterSize);
            uint64_t runLengthBytes = _mftRuns[r].second * static_cast<uint64_t>(_clusterSize);
            uint64_t bytesProcessed = 0;

            while (bytesProcessed < runLengthBytes && scanner->IsScanningNow()) {
                uint64_t remaining = runLengthBytes - bytesProcessed;
                uint32_t toRead = static_cast<uint32_t>(chunkBytes < remaining ? chunkBytes : remaining);

                LARGE_INTEGER li;
                li.QuadPart = static_cast<LONGLONG>(runOffsetBytes + bytesProcessed);
                if (!SetFilePointerEx(_hDrive, li, NULL, FILE_BEGIN)) {
                    LogDebug("[MFT] ERRO de leitura: SetFilePointerEx falhou no offset " +
                             std::to_string(runOffsetBytes + bytesProcessed) + ".");
                    break;
                }

                DWORD bytesRead = 0;
                if (!ReadFile(_hDrive, chunk.Data(), toRead, &bytesRead, NULL) || bytesRead < _mftRecordSize) {
                    // Normal no fim do volume; suspeito no meio dele.
                    LogDebug("[MFT] fim da leitura no run " + std::to_string(r) + ": " +
                             std::to_string(bytesRead) + " de " + std::to_string(toRead) +
                             " bytes. Normal se for o fim do volume.");
                    break;
                }

                uint32_t recordsRead = bytesRead / _mftRecordSize;
                for (uint32_t j = 0; j < recordsRead; ++j) {
                    uint8_t* recordPtr = chunk.Data() + (j * _mftRecordSize);
                    uint64_t physical = runOffsetBytes + bytesProcessed + (uint64_t)j * _mftRecordSize;
                    ProcessMFTRecord(recordPtr, globalIndex++, physical, scanner);
                }
                bytesProcessed += bytesRead;
                bytesDone += bytesRead;

                if (bytesTotal > 0) {
                    double pct = 100.0 * static_cast<double>(bytesDone) / static_cast<double>(bytesTotal);
                    scanner->UpdateProgress(pct);
                }
            }
        }

        // ── O RESUMO ────────────────────────────────────────────────
        // Uma linha por motivo de descarte. E o que responde "por que a lista
        // ficou curta?" sem precisar de instrumentacao por arquivo.
        // O journal vive no Parser, entao e aqui que as estativas dele viram
        // numeros do resumo.
        _stats.usnRecordsParsed = _usn.Stats().recordsParsed;
        _stats.usnEntriesInMap  = _usn.Stats().entriesInMap;
        _stats.usnResolved      = _usn.Stats().pathsResolved;
        _stats.usnUnknown       = _usn.Stats().pathsUnknown;

        const ScanStats& s = _stats;

        // Os $R da lixeira dependem dos $I, e um $I pode vir depois do seu
        // $R. Resolver antes de fechar o relatório.
        FlushPendingRecycle(scanner);

        LogDebug("[MFT] ==== RESUMO DO FILTRO ====");
        LogDebug("[MFT]   registros lidos ............. " + std::to_string(s.recordsRead));
        LogDebug("[MFT]   ok: nao e registro FILE ..... " + std::to_string(s.notMagic));
        LogDebug("[MFT]   ok: e pasta .................. " + std::to_string(s.isDirectory));
        LogDebug("[MFT]   ok: ainda existe no disco ... " + std::to_string(s.stillAlive));
        LogDebug("[MFT]   ok: metadado NTFS (idx<24) ... " + std::to_string(s.metaRecord));
        LogDebug("[MFT]   ok: nome de sistema ($) ..... " + std::to_string(s.systemName));
        LogDebug("[MFT]   ok: temporario (~$...) ....... " + std::to_string(s.tempName));
        LogDebug("[MFT]   ok: nome corrompido ......... " + std::to_string(s.invalidName));
        LogDebug("[MFT]   ok: na raiz do volume ....... " + std::to_string(s.rootLevel));
        LogDebug("[MFT]   ok: pasta de sistema ........ " + std::to_string(s.systemFolder));
        LogDebug("[MFT]   ok: caminho nao verificavel .. " + std::to_string(s.pathUnverified));
        LogDebug("[MFT]        por que a cadeia parou (1o degrau):");
        LogDebug("[MFT]          registro nao e FILE ..... " + std::to_string(s.pathFailMagic));
        LogDebug("[MFT]          SEQUENCIA diferente ...... " + std::to_string(s.pathFailSequence) +
                 "   <- pasta foi reutilizada (prova real)");
        LogDebug("[MFT]          registro nao e pasta .... " + std::to_string(s.pathFailNotDir));
        LogDebug("[MFT]          sem $FILE_NAME legivel ... " + std::to_string(s.pathFailParse));
        LogDebug("[MFT]          so profundidade (ok) .... " + std::to_string(s.pathFailNoRoot));
        LogDebug("[MFT]          direcao da diferenca:" );
        LogDebug("[MFT]            atual > ref (REUSO) .... " + std::to_string(s.pathSeqReused) + "   <- prova real" );
        LogDebug("[MFT]            atual <= ref (SUSPEITA) . " + std::to_string(s.pathSeqBackward) + "   <- extracao da referencia pode estar errada" );
        LogDebug("[MFT]   ok: sem atributo $DATA ....... " + std::to_string(s.noDataAttribute));
        LogDebug("[MFT]   ok: tamanho zero ............. " + std::to_string(s.zeroSize));
        LogDebug("[MFT]   ok: tamanho absurdo ......... " + std::to_string(s.absurdSize));
        LogDebug("[MFT]   ok: extensao nao suportada .. " + std::to_string(s.unknownExtension));
        LogDebug("[MFT]   ASSINATURA NAO CONFERE ...... " + std::to_string(s.signatureMismatch) +
                 "   <-- registro intacto, conteudo sobrescrito/zerado");
        LogDebug("[MFT]     -> na lista como 'so a estrutura': " + std::to_string(s.contentGone) +
                 "   (escondidos por padrao; filtro explicito na interface)");
        LogDebug("[MFT]   conteudo ilegivel ....... " + std::to_string(s.signatureUnreadable));
        LogDebug("[MFT]   ACEITOS ...................... " + std::to_string(s.accepted));
        LogDebug("[MFT]   ---- USN JOURNAL (rewind) ----");
        LogDebug("[MFT]     registros lidos ............. " + std::to_string(s.usnRecordsParsed));
        LogDebug("[MFT]     entradas no mapa ............ " + std::to_string(s.usnEntriesInMap));
        LogDebug("[MFT]     amostra c/ caminho completo . " + std::to_string(s.usnResolved));
        LogDebug("[MFT]     amostra c/ pai ausente ...... " + std::to_string(s.usnUnknown));
        LogDebug("[MFT]     caminhos recuperados ....... " + std::to_string(s.pathFromUsn) + "   <- a MFT nao dava" );
        LogDebug("[MFT]   ---- LIXEIRA ----");
        LogDebug("[MFT]   $I lidos (caminho original) .. " + std::to_string(s.recycleInfoFound));
        LogDebug("[MFT]   recuperados com nome real .... " + std::to_string(s.recycleRecovered));
        LogDebug("[MFT]   orfaos ($R sem $I) .......... " + std::to_string(s.recycleOrphan));
        LogDebug("[MFT]   nomes $I* vistos ........... " + std::to_string(s.recycleNameSeen));
        LogDebug("[MFT]   $I* recusados pelo id ...... " + std::to_string(s.recycleIdRejected));
        LogDebug("[MFT]   $I RECUSADOS PELO PARSER .. " + std::to_string(s.recycleParseFailed) +
                 "   <-- se >0, o parser de $I esta errado");
        LogDebug("[MFT]   pastas da lixeira .......... " + std::to_string(s.recycleFolderSeen));
        LogDebug("[MFT]   AINDA ESTAO NA LIXEIRA ..... " + std::to_string(s.recycleStillInBin) + "   <-- em uso, NAO apagados");

        scanner->UpdateProgress(100.0);
    }

    // =====================================================================
    // UM REGISTRO
    // =====================================================================
    void Parser::ProcessMFTRecord(uint8_t* buffer, uint64_t mftIndex, uint64_t physicalRecordOffset,
                                 RecoveryScanner* scanner) {
        _stats.recordsRead++;

        MFTHeader* header = (MFTHeader*)buffer;
        if (header->magic != 0x454C4946) { _stats.notMagic++; return; }

        // (1) O arquivo ainda está no disco. Não é recuperação, é o índice
        //     de tudo que existe. A versão anterior emitia TODOS estes — daí
        //     a lista com "milhares de arquivos sem sentido".
        // =================================================================
        // O ITEM AINDA ESTÁ NA LIXEIRA?
        //
        // Este é o dado que fecha a questão, e ele precisa ser medido AQUI,
        // no filtro de "ainda existe", porque é o único ponto da varredura
        // por onde passam os registros EM USO.
        //
        // Um item que está na lixeira tem o registro "in use": ele NÃO está
        // apagado, foi MOVIDO. $R e $I nessa situação são registro vivo, e
        // um recuperador que os trata como apagadosSHOW would estar
        // inventando arquivos que ainda estão lá — na sua frente.
        //
        // Sem esta contagem, "não achei o que você apagou" é indistinguível
        // de "o que você apagou ainda está na lixeira". São diagnosticamente
        // opostos: o primeiro é bug meu, o segundo é o programa funcionando.
        // =================================================================
        if (header->flags & 0x01) {
            // Nome do registro, só para classificar. Barato: é um $FILE_NAME.
            uint32_t lim0 = header->usedSize;
            if (lim0 < sizeof(MFTHeader) || lim0 > _mftRecordSize) lim0 = _mftRecordSize;
            AttributeHeader* a0 = (AttributeHeader*)(buffer + header->attrOffset);
            while ((const uint8_t*)a0 + sizeof(AttributeHeader) <= buffer + lim0 && a0->type != 0xFFFFFFFF) {
                if (a0->length < sizeof(AttributeHeader)) break;
                if (a0->type == 0x30 && !a0->nonResident) {
                    const uint8_t* v0 = (const uint8_t*)a0 + a0->resident.valueOffset;
                    if (v0 + sizeof(FileNameAttr) <= buffer + lim0) {
                        const FileNameAttr* f0 = (const FileNameAttr*)v0;
                        if ((const uint8_t*)f0->name + (size_t)f0->nameLength * 2 <= buffer + lim0 &&
                            f0->nameLength >= 2) {
                            const wchar_t* nm = f0->name;
                            if (nm[0] == L'$' && (nm[1] == L'R' || nm[1] == L'I')) {
                                _stats.recycleStillInBin++;
                                if (_shownStillInBin < 12) {
                                    char nb[160] = {0};
                                    WideCharToMultiByte(CP_UTF8, 0, f0->name, (int)f0->nameLength,
                                                        nb, sizeof(nb) - 1, NULL, NULL);
                                    LogDebug(std::string("[LIXEIRA] AINDA ESTA NA LIXEIRA (in use): \"") + nb +
                                             "\"  registro=" + std::to_string(mftIndex) +
                                             "  -> este item NAO esta apagado; recovera-lo aqui seria errado");
                                    _shownStillInBin++;
                                }
                            }
                        }
                    }
                }
                a0 = (AttributeHeader*)((const uint8_t*)a0 + a0->length);
            }
            _stats.stillAlive++;
            return;
        }

        // (2) Metadados e reservas do NTFS.
        if (mftIndex < kFirstUserRecord) { _stats.metaRecord++; return; }

        bool isDirectory = (header->flags & 0x02) != 0;

        uint32_t limit = header->usedSize;
        if (limit < sizeof(MFTHeader) || limit > _mftRecordSize) limit = _mftRecordSize;

        std::wstring fileName;
        uint8_t bestNs = 0xFF;
        MftRef parent;
        uint64_t nameSize = 0;

        // Todos os $FILE_NAME vistos neste registro, para o log mostrar
        // exatamente qual namespace ganhou e o que havia em competencia.
        // Sem isso, um nome truncado apareceria sem explicação.
        struct NameCandidate { std::wstring name; uint8_t ns; };
        NameCandidate candidates[4];
        int candidateCount = 0;
        uint64_t siCreated = 0, siModified = 0;

        bool hasData = false;
        bool isResident = false;
        uint64_t fileSize = 0;
        uint64_t allocatedSize = 0;
        uint64_t initializedSize = 0;
        uint64_t dataPhysicalOffset = 0;
        uint32_t runCount = 0;
        // O $I da lixeira é pequeno e quase sempre RESIDENTE: o conteúdo está
        // dentro do próprio registro MFT. Guardar o ponteiro evita uma
        // leitura de disco extra por item.
        const uint8_t* residentData = nullptr;
        uint32_t residentLen = 0;

        AttributeHeader* attr = (AttributeHeader*)(buffer + header->attrOffset);
        while ((const uint8_t*)attr + sizeof(AttributeHeader) <= buffer + limit && attr->type != 0xFFFFFFFF) {
            if (attr->length < sizeof(AttributeHeader)) break;

            if (attr->type == 0x30 && !attr->nonResident) {           // $FILE_NAME
                const uint8_t* val = (const uint8_t*)attr + attr->resident.valueOffset;
                if (val + sizeof(FileNameAttr) <= buffer + limit) {
                    const FileNameAttr* fn = (const FileNameAttr*)val;
                    if ((const uint8_t*)fn->name + (size_t)fn->nameLength * 2 <= buffer + limit) {
                        uint8_t ns = fn->nameNamespace;

                        // A escolha do $FILE_NAME é o que decide se o
                        // usuário vê "test.pdf" ou "TEST~1.PDF". Ver
                        // NsRank() para a tabela de namespaces.
                        //
                        // A lógica anterior aceitava (ns == 3 && bestNs == 2),
                        // deixado o nome DOS 8.3 SUBSTITUIR um DOS+Win32 que
                        // já continha o nome longo. Era um rebaixamento
                        // silencioso, e o sintoma seria exatamente o que o
                        // usuário não aceita: nome truncado na lista.
                        bool better = (bestNs == 0xFF) || (NsRank(ns) > NsRank(bestNs));
                        if (candidateCount < 4) {
                            candidates[candidateCount].name.assign(fn->name, fn->nameLength);
                            candidates[candidateCount].ns = ns;
                            candidateCount++;
                        }
                        if (better) {
                            fileName.assign(fn->name, fn->nameLength);
                            // A referência carrega o número de sequência; sem
                            // ele não há como detectar pai reutilizado.
                            parent = MftRef::From(fn->parentDirectory);
                            if (nameSize == 0) { nameSize = fn->actualSize; allocatedSize = fn->allocatedSize; }
                            bestNs = ns;
                        }
                    }
                }
            }
              else if (attr->type == 0x10) {                            // $STANDARD_INFORMATION
                  // As datas sao a prova independente de que este registro
                  // NAO foi reaproveitado por outro arquivo. Um registro
                  // reatribuido tem criacao recente e carrega o nome do
                  // arquivo novo — o nome "correto" seria o errado.
                  if (!attr->nonResident) {
                      const uint8_t* val = (const uint8_t*)attr + attr->resident.valueOffset;
                      if (val + 4 * sizeof(uint64_t) + 4 <= buffer + limit) {
                          memcpy(&siCreated, val, sizeof(uint64_t));
                          // modificationTime está no offset 8 — o segundo
                          // FILETIME. Ler em "8 * sizeof(uint64_t)" apontava
                          // 64 bytes adiante, além dos quatro timestamps, e
                          // produzia "(sem data)" em tudo. Era a
                          // instrumentação errada, não o dado.
                          memcpy(&siModified, val + sizeof(uint64_t), sizeof(uint64_t));
                      }
                  }
              }
              else if (attr->type == 0x80) {                            // $DATA

                if (!attr->nonResident) {
                    // RESIDENTE: o conteúdo está DENTRO do registro MFT.
                    // Offset no disco = posição do registro + posição do
                    // valor dentro dele. Um arquivo pequeno (tipicamente
                    // < 700 bytes) aparece aqui.
                    fileSize = attr->resident.valueLength;
                    dataPhysicalOffset = physicalRecordOffset +
                                         (static_cast<uint64_t>((const uint8_t*)attr - buffer)) +
                                         attr->resident.valueOffset;
                      isResident = true;
                      allocatedSize = fileSize;
                      initializedSize = fileSize;
                      runCount = 1;
                      hasData = true;
                      residentData = (const uint8_t*)attr + attr->resident.valueOffset;
                      residentLen = attr->resident.valueLength;

                } else {
                    fileSize = attr->nonResidentInfo.actualSize;
                    allocatedSize = attr->nonResidentInfo.allocatedSize;
                    initializedSize = attr->nonResidentInfo.initializedSize;

                    const uint8_t* runList = (const uint8_t*)attr + attr->nonResidentInfo.runOffset;
                    const uint8_t* runEnd = buffer + limit;
                    uint64_t currentLcn = 0;
                    runCount = 0;

                    while (runList < runEnd && *runList != 0) {
                        uint8_t lenLength = (*runList) & 0x0F;
                        uint8_t offsetLength = (*runList) >> 4;
                        if (lenLength == 0) break;
                        runList++;
                        if (runList + lenLength + offsetLength > runEnd) break;

                        uint64_t length = 0;
                        for (int i = 0; i < lenLength; i++) length |= (static_cast<uint64_t>(runList[i]) << (8 * i));
                        runList += lenLength;

                        int64_t delta = 0;
                        for (int i = 0; i < offsetLength; i++) delta |= (static_cast<int64_t>(runList[i]) << (8 * i));
                        if (offsetLength > 0 && (runList[offsetLength - 1] & 0x80)) {
                            for (int i = offsetLength; i < 8; ++i) delta |= (static_cast<int64_t>(0xFF) << (8 * i));
                        }
                        runList += offsetLength;

                        if (offsetLength == 0) break;   // run sparse: sem LCN
                        if (static_cast<uint64_t>(currentLcn) + static_cast<uint64_t>(delta) >
                            _volumeBytes / _clusterSize + 1) break;

                        currentLcn += static_cast<uint64_t>(delta);
                        runCount++;
                        if (runCount == 1) {
                            dataPhysicalOffset = currentLcn * static_cast<uint64_t>(_clusterSize);
                        }
                    }
                    hasData = (runCount > 0);
                }
            }

            attr = (AttributeHeader*)((uint8_t*)attr + attr->length);
        }

        // =================================================================
        // LIXEIRA — TEM DE VIR ANTES DE QUALQUER RECUSA
        //
        // A ordem antiga era:
        //     if (isDirectory || fileName.empty()) return;   <-- aqui
        //     ... só depois o tratamento de $I / $R
        //
        // Uma PASTA esvaziada da lixeira é um registro com o bit de
        // diretório ligado, então era descartada como "e pasta" ANTES de
        // qualquer código de lixeira rodar. O usuário esvaziou a pasta
        // DriverEngine da lixeira e ela nunca teve chance de aparecer.
        //
        // Pasta deletada também não tem extensão, e o filtro de extensão
        // mais abaixo exige uma. Então mesmo reaching aqui, seria
        // descartado. Por isso o tratamento vem antes, e pasta é tratada
        // como item informacional — o conteúdo a recuperar são os arquivos
        // dentro dela, que têm registros próprios.
        // =================================================================
        if (isDirectory || fileName.empty()) {
            std::wstring folderId;
            if (!fileName.empty() && RecycleIdFromName(fileName, L'R', folderId)) {
                // Pasta vinda da lixeira.
                //
                // DECISÃO: só entra na lista se tiver NOME REAL.
                //
                // Uma pasta esvaziada da lixeira é uma entrada sem conteúdo
                // (pasta não ocupa clusters) e, na maioria das vezes, sem
                // nome: o $I correspondente já foi sobrescrito pelo NTFS, e
                // o que resta é o nome interno "$RD712BV.fnu". Esse item não
                // tem nome reconhecível e não tem byte para extrair — é
                // ruído que enterra o que é acionável. O usuário Zachou ver
                // exatamente isso: selecionou "Pastas" e ganhou 273 linhas de
                // ".fnu" e ".ddj" semrelationamento com o que procurava.
                //
                // Entra apenas quando o $I devolveu o nome de verdade. E
                // nesse caso o conteúdo recuperável são os ARQUIVOS dentro
                // dela, que têm registros MFT próprios e aparecem na lista
                // individualmente, com os nomes originais.
                _stats.recycleFolderSeen++;

                FoundFile ff;
                memset(&ff, 0, sizeof(ff));
                ff.Id = mftIndex;
                char fnMb[MAX_PATH] = {0};
                WideCharToMultiByte(CP_UTF8, 0, fileName.c_str(), -1, fnMb, MAX_PATH - 1, NULL, NULL);
                strncpy_s(ff.Filename, sizeof(ff.Filename), fnMb, _TRUNCATE);
                ff.CategoryFlags = 0x1u | 0x10u | 0x80u;
                _pendingRecycleFolders.push_back(ff);

                // Guarda o id em paralelo, sem alocar std::wstring por item.
                char idMb[64] = {0};
                WideCharToMultiByte(CP_UTF8, 0, folderId.c_str(), -1, idMb, sizeof(idMb) - 1, NULL, NULL);
                _pendingRecycleFolderIds.push_back(idMb);

                if (_shownRecycleDiag < 10) {
                    LogDebug(std::string("[LIXEIRA] PASTA esvaziada: \"") + fnMb +
                             "\"  registro=" + std::to_string(mftIndex) +
                             "  id=" + idMb +
                             "  -> aguardando o $I; SEM NOME REAL ela nao entra na lista");
                }
            }
            _stats.isDirectory++;
            return;
        }

        // =================================================================
        // LIXEIRA — TRATADA ANTES DE QUALQUER FILTRO DE NOME
        //

        // Todo o resto deste função rejeita nomes que começam com '$' como
        // metadado do NTFS. É o filtro certo para $MFT, $LogFile, $Secure —
        // e ERRADO para $I e $R, que são o paradeiro do arquivo que o
        // usuário acabou de apagar. Verificar antes do filtro é o que
        // separa "descartar metadado" de "recuperar da lixeira".
        // =================================================================
        std::wstring recycleId;
        bool isRecycleInfo   = RecycleIdFromName(fileName, L'I', recycleId);
        bool isRecyclePayload = RecycleIdFromName(fileName, L'R', recycleId);

        // =================================================================
        // DIAGNÓSTICO DA LIXEIRA — antes de qualquer validação
        //
        // "Nenhum $I lido" é uma informação inútil sozinha: não diz se os
        // registros não existem, se existem mas meu validador de id os
        // rejeitou, ou se eles nem chegaram aqui. Numa varredura real o
        // contador veio 0 depois de o usuário esvaziar a lixeira, e as três
        // hipóteses pedem correções completamente diferentes.
        //
        // Aqui conta-se TUDO que começar com $I ou $R, e o motivo da
        // rejeição fica visível.
        // =================================================================
        if (fileName.size() >= 2 && fileName[0] == L'$' &&
            (fileName[1] == L'I' || fileName[1] == L'R')) {
            bool isI = (fileName[1] == L'I');
            if (isI) _stats.recycleNameSeen++;

            if (!RecycleIdFromName(fileName, isI ? L'I' : L'R', recycleId)) {
                if (isI) _stats.recycleIdRejected++;
                if (_shownRecycleDiag < 10) {
                    char head[8];
                    snprintf(head, sizeof(head), "%02d", ++_shownRecycleDiag);
                    char nb[160] = {0};
                    WideCharToMultiByte(CP_UTF8, 0, fileName.c_str(), -1, nb, sizeof(nb) - 1, NULL, NULL);
                    LogDebug(std::string("[LIXEIRA-DIAG] #") + head +
                             " nome=\"" + nb + "\"  RECUSADO pelo validador de id" +
                             "  (motivo: tamanho do id fora de 7..32, caractere invalido," +
                             " ou nome de sistema $Recycle/$Recycle.Bin)");
                }
            }
        }

        if (isRecycleInfo) {
            // $I = caminho e tamanho ORIGINAIS. Lê-lo é o que permite
            // devolver "C:\Users\...\relatorio.pdf" em vez de
            // "$Recycle.Bin\S-1-5-21-...\$R3N7QK2M.pdf".
            RecycleEntry entry;
            bool parsed = false;
            if (isResident && residentData && residentLen > 0) {
                parsed = ParseRecycleInfo(residentData, residentLen, entry);
            } else if (hasData && runCount >= 1 && dataPhysicalOffset > 0) {
                std::vector<uint8_t> tmp(4096, 0);
                size_t want = (fileSize < tmp.size()) ? (size_t)fileSize : tmp.size();
                if (want > 0 && ReadFirstBytes(dataPhysicalOffset, want, tmp.data()))
                    parsed = ParseRecycleInfo(tmp.data(), want, entry);
            }
                if (parsed) {
                    // O $I e a FONTE DO NOME ORIGINAL. Loggar cada um e o
                    // que permite ao usuario conferir de proprio olho se o
                    // nome real saiu — em vez de confiar na affirmativa.
                    char nb[128] = {0};
                    WideCharToMultiByte(CP_UTF8, 0, entry.originalName.c_str(), -1, nb, sizeof(nb) - 1, NULL, NULL);
                    char pb[320] = {0};
                    WideCharToMultiByte(CP_UTF8, 0, entry.originalPath.c_str(), -1, pb, sizeof(pb) - 1, NULL, NULL);
                    char ib[64] = {0};
                    WideCharToMultiByte(CP_UTF8, 0, recycleId.c_str(), -1, ib, sizeof(ib) - 1, NULL, NULL);
                    LogDebug(std::string("[LIXEIRA] $I lido  id=") + ib +
                             "  nome=\"" + nb + "\"" +
                             "  original=\"" + pb + "\"" +
                             "  tamanho=" + std::to_string((unsigned long long)entry.originalSize) + "B");
                    _recycleInfo[recycleId] = entry;
                    _stats.recycleInfoFound++;
                } else {
                    // O $I EXISTE mas o parser recusou. Este e o contador que
                    // separa "o registro nao existe" de "meu parser esta
                    // errado" — hipoteses com correcoes opostas.
                    _stats.recycleParseFailed++;
                    if (_shownRecycleDiag < 14) {
                        char ib2[64] = {0};
                        WideCharToMultiByte(CP_UTF8, 0, recycleId.c_str(), -1, ib2, sizeof(ib2) - 1, NULL, NULL);
                        char nb2[160] = {0};
                        WideCharToMultiByte(CP_UTF8, 0, fileName.c_str(), -1, nb2, sizeof(nb2) - 1, NULL, NULL);
                        char dbg[400] = {0};
                        if (residentData && residentLen > 0) {
                            uint64_t ver = 0, sz = 0;
                            memcpy(&ver, residentData, 8);
                            memcpy(&sz, residentData + 8, 8);
                            snprintf(dbg, sizeof(dbg),
                                     "resident len=%u versao=%llu tamanhoOriginal=%llu",
                                     residentLen, (unsigned long long)ver, (unsigned long long)sz);
                        } else {
                            snprintf(dbg, sizeof(dbg),
                                     "NAO RESIDENTE runs=%u offset=%llu tamanho=%llu",
                                     runCount, (unsigned long long)dataPhysicalOffset,
                                     (unsigned long long)fileSize);
                        }
                        LogDebug(std::string("[LIXEIRA-PARSE] FALHOU ao ler o $I: \"") + nb2 +
                                 "\"  id=" + ib2 + "  " + dbg);
                    }
                }

            return;   // o $I em si nunca é um item recuperável
        }

        // (3) Metadados do NTFS ($MFT, $LogFile, $Secure, $ObjId, ...).
        //     Os $R da lixa NÃO entram aqui — ver acima.
        if (fileName[0] == L'$' && !isRecyclePayload) { _stats.systemName++; return; }
        if (IsTempName(fileName)) { _stats.tempName++; return; }

        // (4) Nome corrompido por sobrescrita parcial.
        if (!IsNameSane(fileName)) { _stats.invalidName++; return; }

        // (5) Diretamente na raiz (C:\). Pedido explícito do usuário, e
        //     também o que mais aparece de lixo.
        if (parent.index == kRootDirectory) { _stats.rootLevel++; return; }

        // (6) Dentro de uma pasta de sistema.
        //
        //     A cadeia é conferida por número de sequência e por bit de
        //     diretório, então o que volta em 'verifiedLevels' é o que foi
        //     PROVADO, não o que foi simplesmente lido.
        //
        //     A política aqui é deliberadamente permissiva, e o motivo é a
        //     ordem das operações: em 390 mil registros o primeiro ancestral
        //     não conferiu, e a versão anterior descartava TODOS eles. O
        //     resultado foi uma lista com 4 itens num volume que tem
        //     milhares de arquivos apagados — e 7.942 recusas por assinatura
        //     evaporaram porque esses arquivos nunca chegaram à prova.
        //
        //     Não se descarta por caminho quebrado. Descartar exige PROVA de
        //     que o arquivo era irrelevante, e um caminho que não se pode
        //     verificar não é prova de nada — é ausência de informação. O
        //     arquivo continua na lista, sem caminho inventado, com o flag
        //     de caminho não verificado. Já um caminho VERIFICADO que aponte
        //     para Windows/AppData é prova, e esse sim descarta.
        PathResult path;
        ResolveParentPath(parent, path);

        _stats.pathFailMagic    += (uint64_t)path.failMagic;
        _stats.pathFailSequence += (uint64_t)path.failSequence;
        _stats.pathFailNotDir   += (uint64_t)path.failNotDirectory;
        _stats.pathFailParse    += (uint64_t)path.failParse;
        _stats.pathFailNoRoot   += (uint64_t)path.failNoRoot;
        _stats.pathSeqReused    += (uint64_t)path.seqReused;
        _stats.pathSeqBackward  += (uint64_t)path.seqBackward;

        // Um item da lixeira tem $Recycle.Bin na cadeia por definição. Não é
        // pasta de sistema para efeito de descarte — é o lugar onde o
        // usuário deletou o arquivo.
        if (path.hitSystemFolder && !path.hitRecycleBin) { _stats.systemFolder++; return; }
        if (path.verifiedLevels == 0) { _stats.pathUnverified++; }

        // (7) Sem $DATA não há o que recuperar.
        if (!hasData) { _stats.noDataAttribute++; return; }

        if (fileSize == 0) { _stats.zeroSize++; return; }
        if (fileSize > kAbsurdSize || allocatedSize > kAbsurdSize) { _stats.absurdSize++; return; }

        // (8) Extensão.
        size_t dot = fileName.find_last_of(L'.');
        if (dot == std::wstring::npos || dot == fileName.size() - 1) { _stats.unknownExtension++; return; }

        std::wstring extW = fileName.substr(dot + 1);
        if (extW.size() > 15) { _stats.unknownExtension++; return; }

        char nameMb[MAX_PATH] = {0};
        if (!WideCharToMultiByte(CP_UTF8, 0, fileName.c_str(), -1, nameMb, MAX_PATH - 1, NULL, NULL)) {
            _stats.invalidName++;
            return;
        }
        std::string extMb;
        for (size_t i = 0; i < extW.size(); ++i) {
            wchar_t c = extW[i];
            if (c < 0x20 || c == 0x7F) { _stats.invalidName++; return; }
            extMb += (char)((c < 128) ? c : '_');
        }
        for (size_t i = 0; i < extMb.size(); ++i)
            if (extMb[i] >= 'A' && extMb[i] <= 'Z') extMb[i] = (char)(extMb[i] + 32);

        if (!Sig::IsKnownExtension(extMb.c_str())) { _stats.unknownExtension++; return; }

        // (9) >>> A PROVA <<<
        //     A runlist de um arquivo apagado aponta para clusters já
        //     realocados. Só a assinatura do conteúdo separa "arquivo
        //     recuperável" de "nome certo sobre dados de outro".
        if (dataPhysicalOffset == 0 || dataPhysicalOffset >= _volumeBytes) {
            _stats.signatureUnreadable++;
            return;
        }
        if (!ReadContentProbe(dataPhysicalOffset, kProbeBytes)) {
            _stats.signatureUnreadable++;
            return;
        }

        Sig::Result verdict = Sig::ValidateContent(extMb.c_str(), _probe.Data(), kProbeBytes);

        // Formatos com magic number e assinatura errada.
        //
        // ANTES: descartado em silêncio. E o efeito era o pior possível — o
        // usuário apagou "teste.pdf", o registro MFT dele estava intacto
        // (nome, tamanho, caminho), a runlist apontava para clusters que já
        // não eram mais o PDF, e a lista NÃO mostrava nada. A resposta que
        // o usuário recebia era "não achei", que é factualmente falsa: o
        // registro existe, o que não existe é o conteúdo.
        //
        // AGORA: entra na lista marcado com CF_CONTENT_GONE. O nome, o
        // tamanho e o caminho original estão corretos, e isso é informação
        // real e útil — às vezes ainda há resíduo no início do arquivo, e o
        // usuário extrai com o nome certo, que já é metade do trabalho.
        // A interface esconde esses itens por padrão e oferece um filtro
        // explícito, para a lista limpa continuar limpa.
        if (verdict == Sig::Result::Mismatch) {
            _stats.signatureMismatch++;
            if (_shownMismatch < 12) {
                char head[32];
                snprintf(head, sizeof(head), "%02llu", (unsigned long long)_shownMismatch + 1);
                _shownMismatch++;
                LogDebug(std::string("[MFT]   conteudo sobrescrito #") + head + ": \"" +
                         nameMb + "\" (" + extMb + ") | registro intacto em " +
                         std::to_string(dataPhysicalOffset) +
                         " | os clusters NAO contem mais o arquivo (TRIM ou reatribuicao)"
                         " -> entra na lista como 'so a estrutura', filtro oculto por padrao");
            }

            FoundFile gone;
            memset(&gone, 0, sizeof(gone));
            gone.Id = mftIndex;
            strncpy_s(gone.Filename, sizeof(gone.Filename), nameMb, _TRUNCATE);
            strncpy_s(gone.Extension, sizeof(gone.Extension), extMb.c_str(), _TRUNCATE);
            gone.Size = fileSize;
            gone.Recoverability = 0.0;          // nada de conteúdo a recuperar
            gone.Offset = dataPhysicalOffset;
            gone.ParentId = parent.index;
            gone.RunCount = runCount;
            gone.Flags = (runCount > 1 ? 0x1u : 0u);
            gone.CategoryFlags = 0x1u           // CF_DELETED
                                 | (runCount == 1 ? 0x2u : 0u)
                                 | 0x100u;        // CF_CONTENT_GONE

            // Caminho: só o que a validação provou. Nada de inventar.
            if (path.verifiedLevels > 0) {
                std::wstring full = path.path + L"\\" + fileName;
                const size_t cap = sizeof(gone.OriginalPath) / sizeof(gone.OriginalPath[0]);
                if (full.size() + 1 > cap) full = full.substr(0, cap - 4) + L"...";
                else gone.CategoryFlags |= 0x20u;
                WideCharToMultiByte(CP_UTF8, 0, full.c_str(), -1, gone.OriginalPath,
                                    (int)sizeof(gone.OriginalPath), NULL, NULL);
            } else {
                strncpy_s(gone.OriginalPath, sizeof(gone.OriginalPath),
                          "[conteudo sobrescrito; localizacao nao recuperavel]", _TRUNCATE);
            }

            _stats.contentGone++;
            _pendingContentGone.push_back(gone);
            return;
        }

        // (10) Recuperabilidade REAL.

        //      Não é mais "deletado? então 70". É a soma de fatores que
        //      determinam se a leitura vai devolver bytes coerentes.
        double score = 0.0;
        if (verdict == Sig::Result::Match) score += 46;   // conteúdo confere
        else score += 18;                                 // sem magic number: não provado

        if (runCount == 1) score += 30;                   // contíguo: copia direta
        else if (runCount <= 4) score += 16;              // pouca fragmentação
        else score += 6;

        if (initializedSize >= fileSize) score += 10;     // dados foram escritos
        if (bestNs == 1) score += 8;                      // nome longo, não 8.3
        if (isResident) score += 12;                      // conteúdo dentro do registro

        if (score > 97) score = 97;                       // nunca 100: não está mais lá
        if (score < 4) score = 4;

        // (11) Emite.
        FoundFile f;
        memset(&f, 0, sizeof(f));
        f.Id = mftIndex;
        strncpy_s(f.Filename, sizeof(f.Filename), nameMb, _TRUNCATE);
        strncpy_s(f.Extension, sizeof(f.Extension), extMb.c_str(), _TRUNCATE);
        f.Size = fileSize;
        f.Recoverability = score;
        f.Offset = dataPhysicalOffset;
        f.ParentId = parent.index;
        f.RunCount = runCount;
        f.Flags = (runCount > 1 ? 0x1u : 0u)
                | (isResident ? 0x2u : 0u)
                | (verdict == Sig::Result::Match ? 0x4u : 0u);

        // Categoria: o que a interface precisa saber para explicar a
        // confiança ao usuário sem ter que adivinhar.
        f.CategoryFlags = 0x1u                                        // CF_DELETED (sempre, aqui)
                        | (runCount == 1 ? 0x2u : 0u)                 // CF_CONTIGUOUS
                        | (verdict == Sig::Result::NoSignature ? 0x4u : 0u); // CF_TEXTLIKE

        // Caminho original, para o usuário reconhecer o arquivo.
        //
        // Usa SOMENTE os degraus que a validação por número de sequência
        // conseguiu provar. Se a cadeia quebrou no meio, o que aparece é o
        // trecho verificado — nunca um nome de pasta que pertence a outro
        // arquivo. E se nada pode ser provado, o caminho fica vazio em vez de
        //Mentira: o NOME do arquivo continua, que sozinho já é útil, e a
        // recuperação não depende de o usuário saber onde o arquivo morava.
        bool pathFullyVerified = path.complete;
        if (path.verifiedLevels == 0 && _usnLoaded) {
            // -------------------------------------------------------
            // SEGUNDA FONTE: O REWIND DO USN JOURNAL
            //
            // A cadeia da MFT falhou porque a pasta-mãe teve o registro
            // reatribuído. O journal guardou o nome e o pai na época, e o
            // rewind reconstrói a árvore como ela ERA — inclusive quando o
            // registro da MFT já não existe mais.
            //
            // O casamento é pela referência de 64 bits (entrada + sequência).
            // Há um detalhe do NTFS: na exclusão a sequência do registro
            //sometimes incrementa, então o USN pode gravar seq-1 em
            // relação ao que a MFT mostra agora. Por isso as duas são
            // testadas.
            // -------------------------------------------------------
            uint64_t ref = mftIndex | (static_cast<uint64_t>(header->sequenceNum) << 48);
            std::wstring usnPath;
            if (_usn.ResolvePath(ref, usnPath) ||
                _usn.ResolvePath(ref - (1ULL << 48), usnPath)) {
                std::wstring full = usnPath + L"\\" + fileName;
                const size_t cap = sizeof(f.OriginalPath) / sizeof(f.OriginalPath[0]);
                if (full.size() + 1 > cap) {
                    full = full.substr(0, cap - 4) + L"...";
                    f.CategoryFlags |= 0x8u;                       // CF_TRUNCATED
                }
                WideCharToMultiByte(CP_UTF8, 0, full.c_str(), -1, f.OriginalPath,
                                    (int)sizeof(f.OriginalPath), NULL, NULL);
                f.CategoryFlags |= 0x20u;                          // CF_PATH_VERIFIED
                f.CategoryFlags |= 0x40u;                          // CF_PATH_FROM_USN
                pathFullyVerified = true;
                _stats.pathFromUsn++;

                if (_shownUsnPath < 8) {
                    char head[32];
                    snprintf(head, sizeof(head), "%02llu", (unsigned long long)_shownUsnPath + 1);
                    _shownUsnPath++;
                    LogDebug(std::string("[USN]   caminho #") + head + ": \"" + f.Filename +
                             "\" -> " + f.OriginalPath + "   (pasta-mãe na MFT ja nao existe)");
                }
            }
        }
        if (f.OriginalPath[0] == 0) {
            if (path.verifiedLevels > 0) {
                std::wstring full = path.path + L"\\" + fileName;
                const size_t cap = sizeof(f.OriginalPath) / sizeof(f.OriginalPath[0]);
                if (full.size() + 1 > cap) {
                    full = full.substr(0, cap - 4) + L"...";
                    f.CategoryFlags |= 0x8u;                       // CF_TRUNCATED
                }
                WideCharToMultiByte(CP_UTF8, 0, full.c_str(), -1, f.OriginalPath,
                                    (int)sizeof(f.OriginalPath), NULL, NULL);
                if (pathFullyVerified) f.CategoryFlags |= 0x20u;   // CF_PATH_VERIFIED
            } else {
                // Sem cadeia verificável E sem o journal: não inventar. O
                // item continua na lista, com o nome — que sozinho já
                // serve, e a recuperação não depende de saber onde o
                // arquivo morava.
                strncpy_s(f.OriginalPath, sizeof(f.OriginalPath),
                          "[localizacao original nao recuperavel]", _TRUNCATE);
            }
        }

        // -----------------------------------------------------------------
        // ITEM DA LIXEIRA: adia a emissão.
        //
        // O $R pode aparecer na MFT ANTES do seu $I pareado — a ordem é por
        // índice de registro, não por data. Emitir aqui mostraria
        // "$Recycle.Bin\S-1-5-21-...\$R3N7QK2M.pdf", e o $I (que tem o nome
        // verdadeiro) talvez só seja lido daqui a vinte mil registros. Por
        // isso o item fica pendente e é resolvido no fim da varredura.
        //
        // A PROVA DE ASSINATURA já foi feita acima, com a extensão real do
        // $R — que é a extensão original. Não há como ler este item sem a
        // extensão, e por isso a validação não espera o $I.
        // -----------------------------------------------------------------
        if (isRecyclePayload) {
            f.CategoryFlags |= 0x10u;                                  // CF_FROM_RECYCLE
            PendingRecycle pending;
            pending.file = f;
            pending.id = recycleId;
            _pendingRecycle.push_back(pending);
            return;
        }

        _stats.accepted++;
        // Amostra do que ENTROU, pela mesma razao do log de rejeitados: o
        // usuario precisa poder conferir se o filtro deixou passar o que
        // devia, olhando os nomes.
        if (_shownAccepted < 20) {
            char head[32];
            snprintf(head, sizeof(head), "%02llu", (unsigned long long)_shownAccepted + 1);
            _shownAccepted++;
            LogDebug(std::string("[MFT]   ACEITO #") + head + ": \"" + f.Filename +
                     "\" | " + extMb + " | " + std::to_string(fileSize) + "B | runs=" +
                     std::to_string(runCount) + " | recup=" +
                     std::to_string(static_cast<int>(score)) + "% | " +
                     (verdict == Sig::Result::Match ? "assinatura OK" : "sem magic number") +
                     " | " + f.OriginalPath);
        }

        // =================================================================
        // LOG DE NOME — o que o usuario pediu para conferir de proprio olho
        //
        // Uma amostra das primeiras N entradas, mostrando TODOS os $FILE_NAME
        // do registro, qual namespace venceu, e as datas do
        // $STANDARD_INFORMATION. As tres informacoes juntas respondem:
        //
        //   1. o nome exibido e o nome LONGO, ou o 8.3 truncado?
        //   2. o registro foi reaproveitado (criacao recente = nome de outro)?
        //   3. o nome e mesmo o arquivo apagado, ou de algo que veio depois?
        //
        // Sem este log, um nome truncado ou de outro arquivo apareceria sem
        // que nada na tela explicasse por que.
        // =================================================================
        if (_shownNameAudit < 25) {
            char head[32];
            snprintf(head, sizeof(head), "%02llu", (unsigned long long)_shownNameAudit + 1);
            _shownNameAudit++;

            std::string cands;
            for (int k = 0; k < candidateCount; ++k) {
                char nb[160] = {0};
                WideCharToMultiByte(CP_UTF8, 0, candidates[k].name.c_str(), -1, nb, sizeof(nb) - 1, NULL, NULL);
                if (k) cands += " | ";
                cands += std::string(NsName(candidates[k].ns)) + "=\"" + nb + "\"";
                if (candidates[k].ns == bestNs) cands += " <=ESCOLHIDO";
            }
            LogDebug(std::string("[NOME] #") + head + " mftIdx=" + std::to_string(mftIndex) +
                     " seq=" + std::to_string((int)header->sequenceNum) +
                     "  exibido=\"" + f.Filename + "\"" +
                     "  candidatos: " + (cands.empty() ? "(nenhum)" : cands) +
                     "  criado=" + FileTimeToIso(siCreated) +
                     " modificado=" + FileTimeToIso(siModified));
        }
        scanner->AddFoundFileEntry(f);
    }

    // =====================================================================
    // Resolve os $R pendentes contra os $I lidos e emite.
    //
    // É aqui que "item esvaziado da lixeira" deixa de ser
    //     C:\$Recycle.Bin\S-1-5-21-...\$R3N7QK2M.pdf
    // e passa a ser o arquivo que o usuário reconheceu.
    // =====================================================================
    void Parser::FlushPendingRecycle(RecoveryScanner* scanner) {
        // ── Itens de "só a estrutura" ───────────────────────────────
        //
        // Registros MFT intactos cujo conteúdo não sobreviveu. Não vão
        // para a lista de confiance: entram e a interface os esconde por
        // padrão, com um filtro explícito para quem quiser ver.
        //
        // 92 mil itens de uma vez no mesmo lote custa caro de memória e de
        // layout no DataGrid. Como a lista já é paginada em 1.000, não há
        // ganho em emitir todos: o que o usuário precisa é que o filtro
        // funcione e que a contagem seja verdadeira. Por isso o limite é
        // alto o bastante para qualquer busca útil e o resto fica
        // contabilizado.
        const size_t kMaxContentGone = 8000;
        size_t emitted = 0;
        for (size_t i = 0; i < _pendingContentGone.size(); ++i) {
            if (emitted >= kMaxContentGone) break;
            _stats.accepted++;
            emitted++;
            scanner->AddFoundFileEntry(_pendingContentGone[i]);
        }
        if (_pendingContentGone.size() > emitted) {
            LogDebug("[MFT] conteudo sobrescrito: " + std::to_string(_pendingContentGone.size()) +
                     " no total; emitidos " + std::to_string(emitted) +
                     " (limite). O filtro 'so a estrutura' busca sobre a lista completa.");
        }
        _pendingContentGone.clear();

        // ── Pastas: só entram com NOME REAL ──────────────────────────
        //
        // Decisão de produto, não limitação técnica. A pasta existe no
        // registro, mas não tem conteúdo (pasta não ocupa clusters) e
        // geralmente não tem nome (o $I foi sobrescrito). Um item que o
        // usuário não consegue reconhecer e não tem byte para extrair é
        // ruído: enterrava a lista e fazia o filtro "Pastas" devolver
        // 273 linhas de "$Rxxxxx.fnu".
        //
        // A pasta que TEM nome real entra, com ícone de pasta. O conteúdo
        // recuperável dela são os arquivos, que têm registros próprios.
        int foldersNamed = 0, foldersUnnamed = 0;
        for (size_t i = 0; i < _pendingRecycleFolders.size(); ++i) {
            std::unordered_map<std::wstring, RecycleEntry>::const_iterator it =
                _recycleInfo.find(ToWstring(_pendingRecycleFolderIds[i]));
            if (it == _recycleInfo.end() || !it->second.valid ||
                it->second.originalName.empty()) {
                foldersUnnamed++;
                continue;
            }

            FoundFile ff = _pendingRecycleFolders[i];
            char nb[128];
            WideCharToMultiByte(CP_UTF8, 0, it->second.originalName.c_str(), -1,
                                nb, sizeof(nb) - 1, NULL, NULL);
            strncpy_s(ff.Filename, sizeof(ff.Filename), nb, _TRUNCATE);

            const size_t cap = sizeof(ff.OriginalPath) / sizeof(ff.OriginalPath[0]);
            if (it->second.originalPath.size() + 1 > cap) {
                std::wstring cut = it->second.originalPath.substr(0, cap - 4) + L"...";
                WideCharToMultiByte(CP_UTF8, 0, cut.c_str(), -1, ff.OriginalPath, (int)cap, NULL, NULL);
                ff.CategoryFlags |= 0x8u;
            } else {
                WideCharToMultiByte(CP_UTF8, 0, it->second.originalPath.c_str(), -1,
                                    ff.OriginalPath, (int)cap, NULL, NULL);
            }
            ff.CategoryFlags |= 0x20u;   // CF_PATH_VERIFIED
            ff.Size = it->second.originalSize;
            ff.Recoverability = 0.0;    // pasta: nada a extrair

            _stats.accepted++;
            foldersNamed++;
            scanner->AddFoundFileEntry(ff);

            if (_shownRecycle < 12) {
                char head[8];
                snprintf(head, sizeof(head), "%02d", ++_shownRecycle);
                LogDebug(std::string("[LIXEIRA] PASTA #") + head + " com nome real: \"" +
                         ff.Filename + "\"  -> " + ff.OriginalPath);
            }
        }
        _stats.recycleFoldersNamed = (uint64_t)foldersNamed;
        _stats.recycleFoldersUnnamed = (uint64_t)foldersUnnamed;
        LogDebug("[LIXEIRA] pastas: " + std::to_string(foldersNamed) + " com nome real (listadas), " +
                 std::to_string(foldersUnnamed) + " sem nome (NAO listadas — sem conteudo e sem nome reconhecivel)");
        _pendingRecycleFolders.clear();
        _pendingRecycleFolderIds.clear();

        // Diagnóstico do CASA-MENTO. "orfaos = 7" sem dizer QUAIS ids
        // estavam pendentes e quais $I existiam não ajuda ninguém: as
        // causas possíveis (id divergente, $I sobrescrito, $R descartado por
        // filtro antes de chegar aqui) pedem correções diferentes.
        {
            char ids[512] = {0};
            size_t n = 0;
            for (size_t i = 0; i < _pendingRecycle.size() && n < 400; ++i) {
                char b[32];
                WideCharToMultiByte(CP_UTF8, 0, _pendingRecycle[i].id.c_str(), -1, b, sizeof(b) - 1, NULL, NULL);
                n += (size_t)snprintf(ids + n, sizeof(ids) - n, "%s%s",
                                      (i ? ", " : ""), b);
            }
            char known[512] = {0};
            size_t k = 0;
            int shown = 0;
            for (std::unordered_map<std::wstring, RecycleEntry>::const_iterator it = _recycleInfo.begin();
                 it != _recycleInfo.end() && shown < 12; ++it, ++shown) {
                char b[32];
                WideCharToMultiByte(CP_UTF8, 0, it->first.c_str(), -1, b, sizeof(b) - 1, NULL, NULL);
                k += (size_t)snprintf(known + k, sizeof(known) - k, "%s%s",
                                      (shown ? ", " : ""), b);
            }
            LogDebug("[LIXEIRA-MATCH] pendentes (" + std::to_string(_pendingRecycle.size()) + "): " + ids);
            LogDebug("[LIXEIRA-MATCH] $I em memoria (" + std::to_string(_recycleInfo.size()) + "): " + known);
            if (_recycleInfo.empty() && !_pendingRecycle.empty()) {
                LogDebug("[LIXEIRA-MATCH] NENHUM $I foi lido. Ou nao havia $I "
                         "(o Windows so cria $I para itens avulsos, e nao para o "
                         "conteudo de uma pasta inteira), ou os registros $I "
                         "foram sobrescritos depois que a lixeira foi esvaziada.");
            }
        }

        for (size_t i = 0; i < _pendingRecycle.size(); ++i) {
            FoundFile& f = _pendingRecycle[i].file;
            std::unordered_map<std::wstring, RecycleEntry>::const_iterator it =
                _recycleInfo.find(_pendingRecycle[i].id);

            if (it == _recycleInfo.end() || !it->second.valid) {
                // O $I foi sobrescrito antes da leitura. O $R continua válido
                // e o conteúdo ainda está lá — mostra o nome interno, que é
                // feio mas honesto, em vez de descartar o arquivo.
                _stats.recycleOrphan++;
                f.CategoryFlags &= ~0x20u;      // sem CF_PATH_VERIFIED
                if (_shownRecycle < 12) {
                    char head[32];
                    snprintf(head, sizeof(head), "%02llu", (unsigned long long)_shownRecycle + 1);
                    _shownRecycle++;
                    char ib[32];
                    WideCharToMultiByte(CP_UTF8, 0, _pendingRecycle[i].id.c_str(), -1, ib, sizeof(ib) - 1, NULL, NULL);
                    LogDebug(std::string("[LIXEIRA] #") + head + " SEM $I: \"" +
                             f.Filename + "\"  id=" + ib +
                             "  — o arquivo de metadados foi sobrescrito. "
                             "Conteudo intacto, nome original desconhecido.");
                }
            } else {
                const RecycleEntry& e = it->second;
                _stats.recycleRecovered++;
                if (f.CategoryFlags & 0x80u) {
                    // É uma PASTA: o $I traz o nome real dela.
                    if (!e.originalName.empty()) {
                        char nb2[128];
                        WideCharToMultiByte(CP_UTF8, 0, e.originalName.c_str(), -1, nb2, sizeof(nb2) - 1, NULL, NULL);
                        strncpy_s(f.Filename, sizeof(f.Filename), nb2, _TRUNCATE);
                    }
                    strncpy_s(f.OriginalPath, sizeof(f.OriginalPath), "[pasta - sem conteudo proprio]", _TRUNCATE);
                    f.CategoryFlags |= 0x20u;      // CF_PATH_VERIFIED
                    if (_shownRecycle < 12) {
                        char head[32];
                        snprintf(head, sizeof(head), "%02llu", (unsigned long long)_shownRecycle + 1);
                        _shownRecycle++;
                        LogDebug(std::string("[LIXEIRA] PASTA #") + head +
                                 " com nome real: \"" + f.Filename + "\"");
                    }
                } else {
                if (!e.originalName.empty()) {
                    WideCharToMultiByte(CP_UTF8, 0, e.originalName.c_str(), -1,
                                        f.Filename, (int)sizeof(f.Filename), NULL, NULL);
                    // A extensão que o $R carrega é a original; se o $I
                    // discordar, o $I tem o caminho completo e portanto
                    // prioridade.
                    size_t dot = e.originalName.find_last_of(L'.');
                    if (dot != std::wstring::npos && dot < e.originalName.size() - 1) {
                        std::wstring extW = e.originalName.substr(dot + 1);
                        std::string extMb;
                        for (size_t kk = 0; kk < extW.size() && kk < 15; ++kk) {
                            wchar_t c = extW[kk];
                            extMb += (char)((c < 128) ? ((c >= L'A' && c <= L'Z') ? c + 32 : c) : '_');
                        }
                        strncpy_s(f.Extension, sizeof(f.Extension), extMb.c_str(), _TRUNCATE);
                    }
                }
                // O caminho do $I é o do USUÁRIO, não o do NTFS. Não há
                // cadeia de registros envolvida, então é verificável por
                // construção.
                const size_t cap = sizeof(f.OriginalPath) / sizeof(f.OriginalPath[0]);
                if (e.originalPath.size() + 1 > cap) {
                    std::wstring cut = e.originalPath.substr(0, cap - 4) + L"...";
                    WideCharToMultiByte(CP_UTF8, 0, cut.c_str(), -1, f.OriginalPath, (int)cap, NULL, NULL);
                    f.CategoryFlags |= 0x8u;                      // CF_TRUNCATED
                } else {
                    WideCharToMultiByte(CP_UTF8, 0, e.originalPath.c_str(), -1, f.OriginalPath, (int)cap, NULL, NULL);
                }
                f.CategoryFlags |= 0x20u;                          // CF_PATH_VERIFIED
                f.Size = e.originalSize ? e.originalSize : f.Size;

                if (_shownRecycle < 12) {
                    char head[32];
                    snprintf(head, sizeof(head), "%02llu", (unsigned long long)_shownRecycle + 1);
                    _shownRecycle++;
                    LogDebug(std::string("[LIXEIRA] #") + head + ": \"" + f.Filename +
                             "\" | " + f.Extension + " | " + std::to_string(f.Size) + "B | " +
                             "caminho do $I | " + f.OriginalPath);
                }
                }
            }

            _stats.accepted++;
            scanner->AddFoundFileEntry(f);
        }
        _pendingRecycle.clear();
    }

}
