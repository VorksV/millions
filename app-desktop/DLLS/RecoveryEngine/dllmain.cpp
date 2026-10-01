#define EXPORT_RECOVERY_ENGINE
#include "RecoveryEngine.h"
#include "Scanner.h"

// Global singleton instance
RecoveryScanner g_Scanner;

BOOL APIENTRY DllMain(HMODULE hModule, DWORD  ul_reason_for_call, LPVOID lpReserved) {
    switch (ul_reason_for_call) {
    case DLL_PROCESS_ATTACH:
    case DLL_THREAD_ATTACH:
    case DLL_THREAD_DETACH:
    case DLL_PROCESS_DETACH:
        break;
    }
    return TRUE;
}

extern "C" {

    // Lido pelo lado gerenciado ANTES de qualquer outra chamada. Se o valor
    // não bater, ele não carrega a DLL — porque carregar uma engine de
    // outra geração significa ler structs com o layout errado.
    RECOVERY_API int get_abi_version() {
        return RECOVERY_ABI_VERSION;
    }

    RECOVERY_API int init_scan(const char* drivePath) {
        if (!drivePath) {
            LogDebug("[API] init_scan(devolveu 0): ponteiro de caminho nulo.");
            return 0;
        }
        bool ok = g_Scanner.Initialize(drivePath);
        // O motivo da falha ja esta no log do Scanner/parser. Aqui so se
        // registra o VEREDITO, para o log gerenciado e o nativo contarem a
        // mesma historia na mesma ordem.
        LogDebug(std::string("[API] init_scan(\"") + drivePath + "\") -> " + (ok ? "1 (OK)" : "0 (FALHOU)"));
        return ok ? 1 : 0;
    }

    RECOVERY_API void stop_scan() {
        g_Scanner.Stop();
    }

    RECOVERY_API int start_quick_scan(ProgressCallback callback) {
        return g_Scanner.StartQuickScan(callback) ? 1 : 0;
    }

    RECOVERY_API int start_deep_scan(ProgressCallback callback) {
        return g_Scanner.StartDeepScan(callback) ? 1 : 0;
    }

    RECOVERY_API int get_found_files(FoundFile* buffer, int maxCount) {
        if (!buffer || maxCount <= 0) return 0;
        int outCount = 0;
        g_Scanner.GetFoundFiles(buffer, maxCount, outCount);
        return outCount;
    }

    RECOVERY_API int get_found_count() {
        int n = g_Scanner.GetFoundCount();
        LogDebug("[API] get_found_count() -> " + std::to_string(n));
        return n;
    }

    RECOVERY_API int get_stats(RecoveryStats* out) {
        if (!out) return 0;
        g_Scanner.GetLastStats(*out);
        return 1;
    }

    RECOVERY_API int recover_file(uint64_t fileId, const char* outputPath) {
        if (!outputPath) return 0;
        return g_Scanner.RecoverFile(fileId, outputPath) ? 1 : 0;
    }

}
