#include "Scanner.h"
#include "NTFSParser.h"
#include <iostream>
#include <algorithm>
#include <cstring>
#include <fstream>
#include <chrono>
#include <ctime>
#include <unordered_set>
#include <vector>
#include <climits>

typedef HANDLE (WINAPI *PCreateFileA)(LPCSTR, DWORD, DWORD, LPSECURITY_ATTRIBUTES, DWORD, DWORD, HANDLE);
typedef BOOL (WINAPI *PDeviceIoControl)(HANDLE, DWORD, LPVOID, DWORD, LPVOID, DWORD, LPDWORD, LPOVERLAPPED);
typedef BOOL (WINAPI *PGetFileSizeEx)(HANDLE, PLARGE_INTEGER);
typedef BOOL (WINAPI *PReadFile)(HANDLE, LPVOID, DWORD, LPDWORD, LPOVERLAPPED);
typedef BOOL (WINAPI *PSetFilePointerEx)(HANDLE, LARGE_INTEGER, PLARGE_INTEGER, DWORD);
typedef LPVOID (WINAPI *PVirtualAlloc)(LPVOID, SIZE_T, DWORD, DWORD);
typedef BOOL (WINAPI *PWriteFile)(HANDLE, LPCVOID, DWORD, LPDWORD, LPOVERLAPPED);
typedef BOOL (WINAPI *PVirtualFree)(LPVOID, SIZE_T, DWORD);

static PCreateFileA _CreateFileA = nullptr;
static PDeviceIoControl _DeviceIoControl = nullptr;
static PGetFileSizeEx _GetFileSizeEx = nullptr;
static PReadFile _ReadFile = nullptr;
static PSetFilePointerEx _SetFilePointerEx = nullptr;
static PVirtualAlloc _VirtualAlloc = nullptr;
static PVirtualFree _VirtualFree = nullptr;
static PWriteFile _WriteFile = nullptr;



/*
    RESOLUCAO DAS API DE DISCO.

    HISTORICO QUE IMPORTA
    ---------------------
    Este codigo usava nomes de API ofuscados com XOR para "escapar de
    analise estatica". A ofuscacao estava CORROMPIDA: varios bytes estavam
    0x3A onde deveria estar 0x30, que e 'e' (0x65) XOR 0x55. O resultado era
    "CroatoFiloA" em vez de "CreateFileA", GetProcAddress devolvia NULL para
    TUDO, e a engine nao conseguia abrir NENHUM disco.

    O pior nao era a falha: era o SILENCIO. OpenDrive retornava em
    `if (!_CreateFileA) return false;` ANTES de qualquer log, entao o log
    mostrava "Tentando abrir particao RAW" e nada mais — sem erro, sem
    causa, sem pista. A DLL antiga (07/04) funcionava porque tinha sido
    compilada de um codigo correto; o fonte estava quebrado desde entao.

    Por que remover a ofuscacao, e nao so consertar a chave:
      1. E a unica forma de o erro de digitacao virar VISIVEL. Corrigir a
         chave e um numero; da proxima vez alguem edita um byte e o motor
         morre em silencio de novo.
      2. Ofuscar nome de API e o que faz o antivirus marcar a DLL como
         malware, num produto que se apresenta como recuperador de arquivos.
      3. Nao ha seguranca real aqui: o proprio Windows precisa desses nomes
         em tempo de execucao, via GetProcAddress.
     A estrutura de ponteiros foi mantida de proposito: todas as guardas
     `if (!_CreateFileA)` do restante do arquivo continuam valendo, entao a
     mudanca e local e nao exige reescrever o scanner.
*/
// LogDebug e definido mais abaixo neste arquivo; aqui so e preciso o
// prototipo, porque EnsureApis agora reporta falha em vez de falhar calado.
//
// SEM `static`: a declaracao em Scanner.h (que as outras unidades da engine
// include) e sem static, e um `static` aqui daria linkage interno ao
// simbolo — a definicao mais abaixo passaria a ser invisivel para o resto
// da DLL e o link falharia com LNK2001.
void LogDebug(const std::string& msg);

static void EnsureApis() {
    if (_CreateFileA && _DeviceIoControl && _GetFileSizeEx && _ReadFile &&
        _SetFilePointerEx && _VirtualAlloc && _VirtualFree && _WriteFile) return;

    HMODULE hK32 = GetModuleHandleA("kernel32.dll");
    if (!hK32) {
        LogDebug("EnsureApis: kernel32.dll nao encontrada (GetModuleHandleA retornou NULL).");
        return;
    }

    _CreateFileA      = (PCreateFileA)     GetProcAddress(hK32, "CreateFileA");
    _DeviceIoControl  = (PDeviceIoControl) GetProcAddress(hK32, "DeviceIoControl");
    _GetFileSizeEx    = (PGetFileSizeEx)   GetProcAddress(hK32, "GetFileSizeEx");
    _ReadFile         = (PReadFile)        GetProcAddress(hK32, "ReadFile");
    _SetFilePointerEx = (PSetFilePointerEx)GetProcAddress(hK32, "SetFilePointerEx");
    _VirtualAlloc     = (PVirtualAlloc)    GetProcAddress(hK32, "VirtualAlloc");
    _VirtualFree      = (PVirtualFree)     GetProcAddress(hK32, "VirtualFree");
    _WriteFile        = (PWriteFile)       GetProcAddress(hK32, "WriteFile");

    // Diagnostico. A versao anterior falhava em silencio e o log parava em
    // "Tentando abrir particao RAW", sem nenhuma pista do motivo. Se
    // qualquer uma destas falhar, o log diz exatamente qual.
    struct { const char* name; void* ptr; } api[] = {
        { "CreateFileA",      (void*)_CreateFileA },
        { "DeviceIoControl",  (void*)_DeviceIoControl },
        { "GetFileSizeEx",    (void*)_GetFileSizeEx },
        { "ReadFile",         (void*)_ReadFile },
        { "SetFilePointerEx", (void*)_SetFilePointerEx },
        { "VirtualAlloc",     (void*)_VirtualAlloc },
        { "VirtualFree",      (void*)_VirtualFree },
        { "WriteFile",        (void*)_WriteFile },
    };
    int missing = 0;
    std::string missingList;
    for (size_t i = 0; i < sizeof(api) / sizeof(api[0]); ++i) {
        if (api[i].ptr) continue;
        missing++;
        if (!missingList.empty()) missingList += ", ";
        missingList += api[i].name;
    }
    if (missing > 0) {
        LogDebug("EnsureApis: FALHOU ao resolver " + std::to_string(missing) +
                 " de " + std::to_string(sizeof(api) / sizeof(api[0])) +
                 " API de kernel32 -> " + missingList +
                 ". A varredura nao pode abrir o disco.");
    } else {
        LogDebug("EnsureApis: 8/8 API de disco resolvidas.");
    }
}

// ─────────────────────────────────────────────────────────────────
// LOG NATIVO — RecoveryEngine_Debug.log
//
// ONDE FICA
//   Ao lado da DLL CARREGADA, ou seja, na raiz do programa, junto do
//   recovery_engine_native.dll.
//
// POR QUE NAO UM CAMINHO RELATIVO
//   A versao anterior abria "RecoveryEngine_Debug.log" sem diretorio, e um
//   caminho relativo em Win32 significa "no diretorio de trabalho do
//   PROCESSO", nao "ao lado do executavel". Isso funciona so enquanto
//   alguem abre o app com o CWD ja no lugar certo — e quebra assim que o
//   app e lancado por um atalho com "Iniciar em" diferente, por um
//   instalador, pelo Agendador de Tarefas ou por um Start-Process de
//   outro diretorio.
//
//   E o lado gerenciado procura o log em AppDomain.CurrentDomain.
//   BaseDirectory. Com o caminho relativo, os dois podem apontar para
//   lugares diferentes e o espelho do motivo nativo nao acha nada —
//   silenciosamente, que e a pior forma de falhar.
//
//   A solucao e derivar o diretorio do proprio modulo carregado, via
//   GetModuleHandleEx com GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS sobre a
//   funcao de log. Assim o log SEMPRE nasce ao lado da DLL, e o espelho
//   gerenciado sempre encontra.
// ─────────────────────────────────────────────────────────────────
static std::string ResolveLogPath() {
    HMODULE self = nullptr;
    // FROM_ADDRESS localiza o modulo que contem ESTE codigo — ou seja, a
    // propria DLL da engine, independente do nome com que ela foi carregada.
    if (GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                           GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                           (LPCSTR)&LogDebug, &self) && self) {
        char buf[MAX_PATH] = {0};
        DWORD n = GetModuleFileNameA(self, buf, MAX_PATH);
        if (n > 0 && n < MAX_PATH) {
            std::string full(buf, n);
            size_t slash = full.find_last_of("\\/");
            if (slash != std::string::npos)
                return full.substr(0, slash + 1) + "RecoveryEngine_Debug.log";
        }
    }
    // Fallback: diretorio de trabalho. Melhor que nada escrever log nenhum.
    return "RecoveryEngine_Debug.log";
}

void LogDebug(const std::string& msg) {
    try {
        static std::string cachedPath;
        if (cachedPath.empty()) cachedPath = ResolveLogPath();

        std::ofstream logFile(cachedPath, std::ios_base::app);
        if (logFile.is_open()) {
            auto now = std::chrono::system_clock::now();
            std::time_t now_time = std::chrono::system_clock::to_time_t(now);
            char dt[64];
            ctime_s(dt, sizeof(dt), &now_time);
            std::string dts = dt;
            dts.erase(std::remove(dts.begin(), dts.end(), '\n'), dts.end());
            logFile << "[" << dts << "] " << msg << std::endl;
        }
    } catch(...) {}
}

#define BUFFER_SIZE (1024 * 1024 * 16) // 16 MB chunk for fast reading

bool RecoveryScanner::StartQuickScan(ProgressCallback callback) {
    LogDebug("Iniciando Quick Scan (NTFS MFT Parser)...");
    if (isScanning || hDevice == INVALID_HANDLE_VALUE) return false;
    
    currentCallback = callback;
    isScanning = true;
    currentProgress = 0.0;
    
    scanThread = std::thread(&RecoveryScanner::QuickScanWorker, this);
    return true;
}

void RecoveryScanner::QuickScanWorker() {
    LogDebug("Worker MFT iniciado.");
    NTFS::Parser ntfs(hDevice);
    if (ntfs.Init()) {
        ntfs.ScanMFT(this);

        // Publica o por que a lista ficou do jeito que ficou. Sem isto, um
        // filtro que come demais e um parser quebrado produzem o mesmo
        // sintoma (lista curta) e não dá para distinguir um do outro.
        const NTFS::ScanStats& s = ntfs.Stats();
        RecoveryStats rs;
        rs.recordsRead         = s.recordsRead;
        rs.notMagic            = s.notMagic;
        rs.isDirectory         = s.isDirectory;
        rs.stillAlive          = s.stillAlive;
        rs.metaRecord          = s.metaRecord;
        rs.systemName          = s.systemName;
        rs.tempName            = s.tempName;
        rs.invalidName         = s.invalidName;
        rs.rootLevel           = s.rootLevel;
        rs.systemFolder        = s.systemFolder;
        rs.noDataAttribute     = s.noDataAttribute;
        rs.zeroSize            = s.zeroSize;
        rs.absurdSize          = s.absurdSize;
        rs.unknownExtension    = s.unknownExtension;
        rs.signatureMismatch   = s.signatureMismatch;
        rs.signatureUnreadable = s.signatureUnreadable;
        rs.accepted            = s.accepted;
        rs.pathUnverified      = s.pathUnverified;
        rs.pathFailMagic       = s.pathFailMagic;
        rs.pathFailSequence    = s.pathFailSequence;
        rs.pathFailNotDir      = s.pathFailNotDir;
        rs.pathFailParse       = s.pathFailParse;
        rs.pathFailNoRoot      = s.pathFailNoRoot;
        rs.pathSeqReused       = s.pathSeqReused;
        rs.pathSeqBackward     = s.pathSeqBackward;
        rs.pathFromUsn         = s.pathFromUsn;
        rs.recycleInfoFound    = s.recycleInfoFound;
        rs.recycleRecovered    = s.recycleRecovered;
        rs.recycleOrphan       = s.recycleOrphan;
        rs.recycleNameSeen      = s.recycleNameSeen;
        rs.recycleIdRejected    = s.recycleIdRejected;
        rs.recycleFolderSeen     = s.recycleFolderSeen;
        rs.recycleParseFailed   = s.recycleParseFailed;
        rs.recycleStillInBin    = s.recycleStillInBin;
        rs.recycleFoldersNamed   = s.recycleFoldersNamed;
        rs.recycleFoldersUnnamed = s.recycleFoldersUnnamed;
        rs.contentGone          = s.contentGone;
        SetLastStats(rs);
    } else {
        LogDebug("Falha ao inicializar parser NTFS. Unidade pode nao ser NTFS.");
    }

    isScanning = false;
    LogDebug("MFT Scan finalizado.");
    if (currentCallback) currentCallback(100.0, 0, 0, (uint32_t)foundFiles.size());
}

void RecoveryScanner::UpdateProgress(double pct) {
    currentProgress = pct;
    if (currentCallback) {
        currentCallback(pct, 0, 0, (uint32_t)foundFiles.size());
    }
}

void RecoveryScanner::AddFoundFileEntry(const FoundFile& file) {
    std::lock_guard<std::mutex> lock(filesMutex);
    foundFiles.push_back(file);
}
// =====================================================================
// TABELA DE ASSINATURAS DE ARQUIVOS (40+ FORMATOS)
// =====================================================================
struct FileSignature {
    const uint8_t* sig;
    size_t         sigLen;
    const char*    ext;
    uint64_t       estimatedSize; // bytes
};

// Definições das assinaturas brutas
// Simple XOR to hide signatures from static analysis
static void Deobfuscate(uint8_t* data, size_t len) {
    for (size_t i = 0; i < len; i++) data[i] ^= 0x55;
}

static uint8_t S_JPG[]    = { 0xFF ^ 0x55, 0xD8 ^ 0x55, 0xFF ^ 0x55 };
static uint8_t S_PNG[]    = { 0x89 ^ 0x55, 0x50 ^ 0x55, 0x4E ^ 0x55, 0x47 ^ 0x55 };
static uint8_t S_GIF87[]  = { 0x47 ^ 0x55, 0x49 ^ 0x55, 0x46 ^ 0x55, 0x38 ^ 0x55, 0x37 ^ 0x55, 0x61 ^ 0x55 };
static uint8_t S_GIF89[]  = { 0x47 ^ 0x55, 0x49 ^ 0x55, 0x46 ^ 0x55, 0x38 ^ 0x55, 0x39 ^ 0x55, 0x61 ^ 0x55 };
static uint8_t S_BMP[]    = { 0x42 ^ 0x55, 0x4D ^ 0x55 };
static uint8_t S_TIFF_LE[] = { 0x49 ^ 0x55, 0x49 ^ 0x55, 0x2A ^ 0x55, 0x00 ^ 0x55 };
static uint8_t S_TIFF_BE[] = { 0x4D ^ 0x55, 0x4D ^ 0x55, 0x00 ^ 0x55, 0x2A ^ 0x55 };
static uint8_t S_ICO[]    = { 0x00 ^ 0x55, 0x00 ^ 0x55, 0x01 ^ 0x55, 0x00 ^ 0x55 };
static uint8_t S_PSD[]    = { 0x38 ^ 0x55, 0x42 ^ 0x55, 0x50 ^ 0x55, 0x53 ^ 0x55 };
static uint8_t S_MP3_ID3[] = { 0x49 ^ 0x55, 0x44 ^ 0x55, 0x33 ^ 0x55 };
static uint8_t S_MP3_FF[] = { 0xFF ^ 0x55, 0xFB ^ 0x55 };
static uint8_t S_FLAC[]   = { 0x66 ^ 0x55, 0x4C ^ 0x55, 0x61 ^ 0x55, 0x43 ^ 0x55 };
static uint8_t S_OGG[]    = { 0x4F ^ 0x55, 0x67 ^ 0x55, 0x67 ^ 0x55, 0x53 ^ 0x55 };
static uint8_t S_WAV[]    = { 0x52 ^ 0x55, 0x49 ^ 0x55, 0x46 ^ 0x55, 0x46 ^ 0x55 }; // RIFF
static uint8_t S_WEBP[]   = { 0x52 ^ 0x55, 0x49 ^ 0x55, 0x46 ^ 0x55, 0x46 ^ 0x55 }; // RIFF
static uint8_t S_AAC[]    = { 0xFF ^ 0x55, 0xF1 ^ 0x55 };
static uint8_t S_MP4[]    = { 0x00 ^ 0x55, 0x00 ^ 0x55, 0x00 ^ 0x55, 0x20 ^ 0x55, 0x66 ^ 0x55, 0x74 ^ 0x55, 0x79 ^ 0x55, 0x70 ^ 0x55 };
static uint8_t S_MP4B[]   = { 0x00 ^ 0x55, 0x00 ^ 0x55, 0x00 ^ 0x55, 0x18 ^ 0x55, 0x66 ^ 0x55, 0x74 ^ 0x55, 0x79 ^ 0x55, 0x70 ^ 0x55 };
static uint8_t S_MP4C[]   = { 0x00 ^ 0x55, 0x00 ^ 0x55, 0x00 ^ 0x55, 0x1C ^ 0x55, 0x66 ^ 0x55, 0x74 ^ 0x55, 0x79 ^ 0x55, 0x70 ^ 0x55 };
static uint8_t S_MOV[]    = { 0x6D ^ 0x55, 0x6F ^ 0x55, 0x6F ^ 0x55, 0x76 ^ 0x55 };
static uint8_t S_MKV[]    = { 0x1A ^ 0x55, 0x45 ^ 0x55, 0xDF ^ 0x55, 0xA3 ^ 0x55 };
static uint8_t S_AVI[]    = { 0x52 ^ 0x55, 0x49 ^ 0x55, 0x46 ^ 0x55, 0x46 ^ 0x55 }; // RIFF
static uint8_t S_WMV[]    = { 0x30 ^ 0x55, 0x26 ^ 0x55, 0xB2 ^ 0x55, 0x75 ^ 0x55, 0x8E ^ 0x55, 0x66 ^ 0x55, 0xCF ^ 0x55, 0x11 ^ 0x55 };
static uint8_t S_PDF[]    = { 0x25 ^ 0x55, 0x50 ^ 0x55, 0x44 ^ 0x55, 0x46 ^ 0x55 }; // %PDF
static uint8_t S_ZIP[]    = { 0x50 ^ 0x55, 0x4B ^ 0x55, 0x03 ^ 0x55, 0x04 ^ 0x55 };
static uint8_t S_RAR4[]   = { 0x52 ^ 0x55, 0x61 ^ 0x55, 0x72 ^ 0x55, 0x21 ^ 0x55, 0x1A ^ 0x55, 0x07 ^ 0x55, 0x00 ^ 0x55 };
static uint8_t S_RAR5[]   = { 0x52 ^ 0x55, 0x61 ^ 0x55, 0x72 ^ 0x55, 0x21 ^ 0x55, 0x1A ^ 0x55, 0x07 ^ 0x55, 0x01 ^ 0x55, 0x00 ^ 0x55 };
static uint8_t S_7Z[]     = { 0x37 ^ 0x55, 0x7A ^ 0x55, 0xBC ^ 0x55, 0xAF ^ 0x55, 0x27 ^ 0x55, 0x1C ^ 0x55 };
static uint8_t S_GZ[]     = { 0x1F ^ 0x55, 0x8B ^ 0x55 };
static uint8_t S_EXE[]    = { 0x4D ^ 0x55, 0x52 ^ 0x55 }; // MZ header (ofuscado)
static uint8_t S_ELF[]    = { 0x7F ^ 0x55, 0x45 ^ 0x55, 0x4C ^ 0x55, 0x46 ^ 0x55 };
static uint8_t S_DOC[]    = { 0xD0 ^ 0x55, 0xCF ^ 0x11 ^ 0x55, 0xE0 ^ 0x55, 0xA1 ^ 0x55, 0xB1 ^ 0x55, 0x1A ^ 0x55, 0xE1 ^ 0x55 }; // OLE2
static uint8_t S_DOCX[]   = { 0x50 ^ 0x55, 0x4B ^ 0x55, 0x03 ^ 0x55, 0x04 ^ 0x55 }; // Same as ZIP — handled via ZIP
static uint8_t S_XML[]    = { 0x3C ^ 0x55, 0x3F ^ 0x55, 0x78 ^ 0x55, 0x6D ^ 0x55, 0x6C ^ 0x55 }; // <?xml
static uint8_t S_HTML[]   = { 0x3C ^ 0x55, 0x68 ^ 0x55, 0x74 ^ 0x55, 0x6D ^ 0x55, 0x6C ^ 0x55 }; // <html
static uint8_t S_TXT[]    = { 0xEF ^ 0x55, 0xBB ^ 0x55, 0xBF ^ 0x55 }; // UTF-8 BOM
static uint8_t S_SQLITE[] = { 0x53 ^ 0x51 ^ 0x55, 0x4C ^ 0x69 ^ 0x55, 0x74 ^ 0x65 ^ 0x55, 0x20 ^ 0x66 ^ 0x55 }; // SQLite format 3
static uint8_t S_ISO[]    = { 0x43 ^ 0x55, 0x44 ^ 0x55, 0x30 ^ 0x55, 0x30 ^ 0x55, 0x31 ^ 0x55 }; // CD001

static bool g_sigsDeobfuscated = false;

static void EnsureSigs() {
    if (g_sigsDeobfuscated) return;
    Deobfuscate(S_JPG, sizeof(S_JPG));
    Deobfuscate(S_PNG, sizeof(S_PNG));
    Deobfuscate(S_GIF87, sizeof(S_GIF87));
    Deobfuscate(S_GIF89, sizeof(S_GIF89));
    Deobfuscate(S_BMP, sizeof(S_BMP));
    Deobfuscate(S_TIFF_LE, sizeof(S_TIFF_LE));
    Deobfuscate(S_TIFF_BE, sizeof(S_TIFF_BE));
    Deobfuscate(S_ICO, sizeof(S_ICO));
    Deobfuscate(S_PSD, sizeof(S_PSD));
    Deobfuscate(S_MP3_ID3, sizeof(S_MP3_ID3));
    Deobfuscate(S_MP3_FF, sizeof(S_MP3_FF));
    Deobfuscate(S_FLAC, sizeof(S_FLAC));
    Deobfuscate(S_OGG, sizeof(S_OGG));
    Deobfuscate(S_WAV, sizeof(S_WAV));
    Deobfuscate(S_WEBP, sizeof(S_WEBP));
    Deobfuscate(S_AAC, sizeof(S_AAC));
    Deobfuscate(S_MP4, sizeof(S_MP4));
    Deobfuscate(S_MP4B, sizeof(S_MP4B));
    Deobfuscate(S_MP4C, sizeof(S_MP4C));
    Deobfuscate(S_MOV, sizeof(S_MOV));
    Deobfuscate(S_MKV, sizeof(S_MKV));
    Deobfuscate(S_AVI, sizeof(S_AVI));
    Deobfuscate(S_WMV, sizeof(S_WMV));
    Deobfuscate(S_PDF, sizeof(S_PDF));
    Deobfuscate(S_ZIP, sizeof(S_ZIP));
    Deobfuscate(S_RAR4, sizeof(S_RAR4));
    Deobfuscate(S_RAR5, sizeof(S_RAR5));
    Deobfuscate(S_7Z, sizeof(S_7Z));
    Deobfuscate(S_GZ, sizeof(S_GZ));
    Deobfuscate(S_EXE, sizeof(S_EXE));
    Deobfuscate(S_ELF, sizeof(S_ELF));
    Deobfuscate(S_DOC, sizeof(S_DOC));
    Deobfuscate(S_DOCX, sizeof(S_DOCX));
    Deobfuscate(S_XML, sizeof(S_XML));
    Deobfuscate(S_HTML, sizeof(S_HTML));
    Deobfuscate(S_TXT, sizeof(S_TXT));
    Deobfuscate(S_SQLITE, sizeof(S_SQLITE));
    Deobfuscate(S_ISO, sizeof(S_ISO));
    g_sigsDeobfuscated = true;
}

#define SIG_ENTRY(name, ext, size) { name, sizeof(name), ext, size }

static const FileSignature FILE_SIGNATURES[] = {
    SIG_ENTRY(S_JPG,    "jpg",   5 * 1024 * 1024),
    SIG_ENTRY(S_PNG,    "png",   8 * 1024 * 1024),
    SIG_ENTRY(S_GIF87,  "gif",   2 * 1024 * 1024),
    SIG_ENTRY(S_GIF89,  "gif",   2 * 1024 * 1024),
    SIG_ENTRY(S_BMP,    "bmp",  10 * 1024 * 1024),
    SIG_ENTRY(S_TIFF_LE,"tif",  20 * 1024 * 1024),
    SIG_ENTRY(S_TIFF_BE,"tif",  20 * 1024 * 1024),
    SIG_ENTRY(S_ICO,    "ico",   1 * 1024 * 1024),
    SIG_ENTRY(S_PSD,    "psd",  50 * 1024 * 1024),
    SIG_ENTRY(S_MP3_ID3,"mp3",  10 * 1024 * 1024),
    SIG_ENTRY(S_FLAC,   "flac", 60 * 1024 * 1024),
    SIG_ENTRY(S_OGG,    "ogg",  10 * 1024 * 1024),
    SIG_ENTRY(S_WAV,    "wav",  50 * 1024 * 1024),
    SIG_ENTRY(S_AAC,    "aac",  10 * 1024 * 1024),
    SIG_ENTRY(S_MP4,    "mp4", 500 * 1024 * 1024),
    SIG_ENTRY(S_MP4B,   "mp4", 500 * 1024 * 1024),
    SIG_ENTRY(S_MP4C,   "mp4", 500 * 1024 * 1024),
    SIG_ENTRY(S_MOV,    "mov", 500 * 1024 * 1024),
    SIG_ENTRY(S_MKV,    "mkv",  (uint64_t)2 * 1024 * 1024 * 1024),
    SIG_ENTRY(S_AVI,    "avi", 700 * 1024 * 1024),
    SIG_ENTRY(S_WMV,    "wmv", 700 * 1024 * 1024),
    SIG_ENTRY(S_PDF,    "pdf",  20 * 1024 * 1024),
    SIG_ENTRY(S_ZIP,    "zip", 100 * 1024 * 1024),
    SIG_ENTRY(S_RAR4,   "rar", 100 * 1024 * 1024),
    SIG_ENTRY(S_RAR5,   "rar", 100 * 1024 * 1024),
    SIG_ENTRY(S_7Z,     "7z",  100 * 1024 * 1024),
    SIG_ENTRY(S_GZ,     "gz",   50 * 1024 * 1024),
    SIG_ENTRY(S_EXE,    "exe",  50 * 1024 * 1024),
    SIG_ENTRY(S_DOC,    "doc",  10 * 1024 * 1024),
    SIG_ENTRY(S_XML,    "xml",   1 * 1024 * 1024),
    SIG_ENTRY(S_HTML,   "html",  1 * 1024 * 1024),
    SIG_ENTRY(S_TXT,    "txt",   1 * 1024 * 1024),
    SIG_ENTRY(S_SQLITE, "db",   50 * 1024 * 1024),
    SIG_ENTRY(S_ISO,    "iso",  (uint64_t)4 * 1024 * 1024 * 1024),
};
static const size_t NUM_SIGNATURES = sizeof(FILE_SIGNATURES) / sizeof(FILE_SIGNATURES[0]);

// Deduplication: evitar adicionar o mesmo offset duas vezes
static std::unordered_set<uint64_t>* g_seenOffsets = nullptr;


static uint64_t g_fileIdCounter = 1;

RecoveryScanner::RecoveryScanner() 
    : hDevice(INVALID_HANDLE_VALUE), totalBytes(0), sectorSize(512), 
      isScanning(false), isPaused(false), currentProgress(0.0), currentSector(0), currentCallback(nullptr) 
{
}

RecoveryScanner::~RecoveryScanner() {
    Stop();
    CloseDrive();
}

void RecoveryScanner::CloseDrive() {
    if (hDevice != INVALID_HANDLE_VALUE) {
        CloseHandle(hDevice);
        hDevice = INVALID_HANDLE_VALUE;
    }
}

bool RecoveryScanner::OpenDrive(const std::string& drivePath) {
    EnsureApis();
    CloseDrive();
    LogDebug("Tentando abrir particao RAW: " + drivePath);
    if (!_CreateFileA) return false;

    hDevice = _CreateFileA(
        drivePath.c_str(),
        GENERIC_READ,
        FILE_SHARE_READ | FILE_SHARE_WRITE, // crucial for physical drives
        NULL,
        OPEN_EXISTING,
        FILE_FLAG_NO_BUFFERING, // requires sector alignment
        NULL
    );

    if (hDevice == INVALID_HANDLE_VALUE) {
        LogDebug("Falha ao abrir disco. Error Code: " + std::to_string(GetLastError()));
    } else {
        LogDebug("Disco aberto com sucesso via HANDLE.");
    }
    return hDevice != INVALID_HANDLE_VALUE;
}

uint64_t RecoveryScanner::GetDriveSize() {
    EnsureApis();
    GET_LENGTH_INFORMATION lengthInfo;
    DWORD bytesReturned;
    if (_DeviceIoControl && _DeviceIoControl(hDevice, IOCTL_DISK_GET_LENGTH_INFO, NULL, 0, &lengthInfo, sizeof(lengthInfo), &bytesReturned, NULL)) {
        return lengthInfo.Length.QuadPart;
    }
    
    // Fallback if not physical drive but a logical partition
    LARGE_INTEGER size;
    if (_GetFileSizeEx && _GetFileSizeEx(hDevice, &size)) {
        return size.QuadPart;
    }
    
    return 0;
}

bool RecoveryScanner::Initialize(const std::string& drivePath) {
    LogDebug("================ INICIO DA CADEIA DE INICIALIZACAO ================");
    Stop();
    if (!OpenDrive(drivePath)) {
        return false;
    }

    totalBytes = GetDriveSize();
    LogDebug("Tamanho do drive capturado (" + drivePath + "): " + std::to_string(totalBytes) + " bytes.");
    currentDrive = drivePath;
    
    std::lock_guard<std::mutex> lock(filesMutex);
    foundFiles.clear();
    
    return totalBytes > 0;
}

void RecoveryScanner::Stop() {
    isScanning = false;
    if (scanThread.joinable()) {
        scanThread.join();
    }
}

bool RecoveryScanner::StartDeepScan(ProgressCallback callback) {
    LogDebug("Solicitando inicio do Deep Scan...");
    if (isScanning || hDevice == INVALID_HANDLE_VALUE) {
        LogDebug("Deep Scan barrado: Ja escanenando ou HANDLE invalido.");
        return false;
    }
    
    currentCallback = callback;
    isScanning = true;
    currentProgress = 0.0;
    currentSector = 0;
    
    LogDebug("Lançando worker thread...");
    scanThread = std::thread(&RecoveryScanner::DeepScanWorker, this);
    return true;
}

void RecoveryScanner::AddFoundFile(const std::string& ext, uint64_t offset, uint64_t estimatedSize) {
    FoundFile f = {0};
    f.Id = g_fileIdCounter++;
      // O NOME ORIGINAL NAO EXISTE AQUI, e esta e a unica resposta honesta.
      //
      // A varredura profunda acha ASSINATURA no meio dos setores. Nao ha
      // registro MFT, nao ha $FILE_NAME, nao ha metadado nenhum — so bytes.
      // O nome e construido a partir da extensao que a assinatura revelou,
      // mais um contador. "RecoveredFile_0.pdf" nao e defeito: e o limite
      // fisico do carving.
      sprintf_s(f.Filename, "RecoveredFile_%llu.%s", f.Id, ext.c_str());
      strcpy_s(f.OriginalPath, "nome original nao recuperavel por carving");

      // Amostra logada, para deixar isso VISIVEL no log em vez de ser uma
      // afirmacao minha aqui no codigo.
      static int deepLogged = 0;
      if (deepLogged < 15 && LogDebug) {
          char head[8];
          snprintf(head, sizeof(head), "%02d", ++deepLogged);
          LogDebug(std::string("[PROFUNDA] #") + head + " \"" + f.Filename +
                   "\" | assinatura=" + ext +
                   " | offset=" + std::to_string(offset) +
                   " | tamanho estimado=" + std::to_string((unsigned long long)estimatedSize) +
                   " | NOME ORIGINAL: IMPOSSIVEL (carving por assinatura, sem metadados)");
      }

    sprintf_s(f.Extension, "%s", ext.c_str());
    f.Offset = offset;
    f.Size = estimatedSize;
    f.Recoverability = 100.0; // Raw carving assumes 100% until proven otherwise
    
    std::lock_guard<std::mutex> lock(filesMutex);
    foundFiles.push_back(f);
}

void RecoveryScanner::ScanBufferForSignatures(uint8_t* buffer, size_t bytesRead, uint64_t baseOffset) {
    EnsureSigs();
    if (bytesRead < 8) return;
    const size_t maxI = bytesRead - 8;
    for (size_t i = 0; i < maxI; i++) {
        for (size_t s = 0; s < NUM_SIGNATURES; s++) {
            const FileSignature& sig = FILE_SIGNATURES[s];
            if (i + sig.sigLen > bytesRead) continue;
            if (memcmp(&buffer[i], sig.sig, sig.sigLen) == 0) {
                uint64_t absoluteOffset = baseOffset + i;
                // WEBP/WAV/AVI share RIFF header: check sub-type
                if (strcmp(sig.ext, "bmp") == 0 && bytesRead > i + 2 && buffer[i+1] == 'M') {
                    // valid BMP
                } else if (sig.sig == S_WEBP && bytesRead > i + 11) {
                    if (memcmp(&buffer[i+8], "WEBP", 4) == 0) { AddFoundFile("webp", absoluteOffset, 8 * 1024 * 1024); i += sig.sigLen - 1; break; }
                    if (memcmp(&buffer[i+8], "WAVE", 4) == 0) { AddFoundFile("wav",  absoluteOffset, 50 * 1024 * 1024); i += sig.sigLen - 1; break; }
                    if (memcmp(&buffer[i+8], "AVI ", 4) == 0) { AddFoundFile("avi",  absoluteOffset, 700ULL * 1024 * 1024); i += sig.sigLen - 1; break; }
                    continue; // Unknown RIFF sub-type
                }
                AddFoundFile(sig.ext, absoluteOffset, sig.estimatedSize);
                i += sig.sigLen - 1; // Advance past this signature
                break; // Only match one signature per position
            }
        }
    }
}

void RecoveryScanner::DeepScanWorker() {
    EnsureApis();
    LogDebug("A thread Worker do DeepScan foi iniciada com SUCESSO.");
    if (!_VirtualAlloc || !_VirtualFree || !_SetFilePointerEx || !_ReadFile) {
        LogDebug("Falha critica: APIs dinamicas nao carregadas.");
        isScanning = false;
        return;
    }

    uint8_t* buffer = (uint8_t*)_VirtualAlloc(NULL, BUFFER_SIZE, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!buffer) {
        LogDebug("Falha critica: nao foi possivel alocar o BUFFER_SIZE.");
        isScanning = false;
        return;
    }

    uint64_t currentOffset = 0;
    DWORD bytesRead = 0;
    uint64_t totalSectors = totalBytes / 512;
    uint32_t lastLogMark = 0;

    LogDebug("Iniciando varredura bruta bloco a bloco...");
    while (isScanning && currentOffset < totalBytes) {
        LARGE_INTEGER li;
        li.QuadPart = currentOffset;
        _SetFilePointerEx(hDevice, li, NULL, FILE_BEGIN);

        if (!_ReadFile(hDevice, buffer, BUFFER_SIZE, &bytesRead, NULL) || bytesRead == 0) {
            currentOffset += BUFFER_SIZE; // Skip bad sector chunks
            continue;
        }

        ScanBufferForSignatures(buffer, bytesRead, currentOffset);
        currentOffset += bytesRead;
        currentSector = currentOffset / 512;
        currentProgress = ((double)currentOffset / (double)totalBytes) * 100.0;

        if (currentCallback) {
            uint32_t fCount = 0;
            {
                std::lock_guard<std::mutex> lock(filesMutex);
                fCount = (uint32_t)foundFiles.size();
            }
            // Throttle callbacks to not block main thread unnecessarily
            if ((currentSector % 10240) == 0 || currentOffset >= totalBytes) {
                currentCallback(currentProgress.load(), currentSector.load(), totalSectors, fCount);
            }
        }
        
        uint32_t pctInt = (uint32_t)currentProgress.load();
        if (pctInt > lastLogMark && (pctInt % 10) == 0) {
            uint32_t fCount = 0;
            {
                std::lock_guard<std::mutex> lock(filesMutex);
                fCount = (uint32_t)foundFiles.size();
            }
            LogDebug("Progresso atingiu: " + std::to_string(pctInt) + "% (Arquivos Encontrados: " + std::to_string(fCount) + ")");
            lastLogMark = pctInt;
        }
        
        // Anti-freeze throttle (cooperative multitasking)
        std::this_thread::sleep_for(std::chrono::milliseconds(1));
    }

    LogDebug("Laco do DeepScan foi interrompido ou finalizado.");
    _VirtualFree(buffer, 0, MEM_RELEASE);
    isScanning = false;
    
    // Final callback update
    if (currentCallback) {
        uint32_t fCount = 0;
        {
            std::lock_guard<std::mutex> lock(filesMutex);
            fCount = (uint32_t)foundFiles.size();
        }
        currentCallback(100.0, totalSectors, totalSectors, fCount);
    }
}

void RecoveryScanner::GetFoundFiles(FoundFile* outBuffer, int maxCount, int& outCount) {
    std::lock_guard<std::mutex> lock(filesMutex);
    int count = (int)std::min((size_t)maxCount, foundFiles.size());
    for (int i = 0; i < count; i++) {
        outBuffer[i] = foundFiles[i];
    }
    outCount = count;
}

int RecoveryScanner::GetFoundCount() {
    std::lock_guard<std::mutex> lock(filesMutex);
    if (foundFiles.size() > (size_t)INT_MAX) return INT_MAX;
    return (int)foundFiles.size();
}

bool RecoveryScanner::RecoverFile(uint64_t fileId, const std::string& outputPath) {
    EnsureApis();
    FoundFile target = {0};
    bool found = false;
    
    {
        std::lock_guard<std::mutex> lock(filesMutex);
        for (const auto& f : foundFiles) {
            if (f.Id == fileId) {
                target = f;
                found = true;
                break;
            }
        }
    }
    
    if (!found || hDevice == INVALID_HANDLE_VALUE || !_CreateFileA || !_WriteFile || !_ReadFile || !_SetFilePointerEx || !_VirtualAlloc || !_VirtualFree) return false;
    
    HANDLE hOut = _CreateFileA(outputPath.c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (hOut == INVALID_HANDLE_VALUE) return false;
    
    // Read raw data from disk
    uint8_t* buffer = (uint8_t*)_VirtualAlloc(NULL, 1024 * 1024, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (buffer) {
        // Find closest sector
        uint64_t alignedOffset = (target.Offset / 512) * 512;
        uint32_t shift = (uint32_t)(target.Offset - alignedOffset);
        
        LARGE_INTEGER li;
        li.QuadPart = alignedOffset;
        _SetFilePointerEx(hDevice, li, NULL, FILE_BEGIN);
        
        DWORD bytesRead;
        // In this basic carving we'll extract standard size (e.g., 2MB)
        uint64_t sizeToRead = target.Size; 
        uint64_t bytesWrittenTotal = 0;
        
        while (bytesWrittenTotal < sizeToRead) {
            uint32_t toRead = std::min((uint64_t)(1024 * 1024), sizeToRead - bytesWrittenTotal);
            // Need aligned read for NO_BUFFERING
            toRead = ((toRead + 511) / 512) * 512;
            
            if (_ReadFile(hDevice, buffer, toRead, &bytesRead, NULL)) {
                DWORD written;
                // Offset shift handling for first chunk
                uint8_t* wBuf = buffer;
                DWORD wSize = bytesRead;
                if (bytesWrittenTotal == 0) {
                    wBuf += shift;
                    wSize -= shift;
                }
                
                // Don't write beyond target size
                if (bytesWrittenTotal + wSize > sizeToRead) {
                    wSize = (DWORD)(sizeToRead - bytesWrittenTotal);
                }
                
                _WriteFile(hOut, wBuf, wSize, &written, NULL);
                bytesWrittenTotal += written;
            } else {
                break; // read error
            }
        }
        _VirtualFree(buffer, 0, MEM_RELEASE);
    }
    
    CloseHandle(hOut);
    return true;
}
