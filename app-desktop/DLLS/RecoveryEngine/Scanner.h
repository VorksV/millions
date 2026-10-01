#ifndef SCANNER_H
#define SCANNER_H

#include "RecoveryEngine.h"
#include <windows.h>
#include <string>
#include <vector>
#include <atomic>
#include <thread>
#include <mutex>

// Definida em Scanner.cpp; escreve em RecoveryEngine_Debug.log, ao lado do
// executavel.
//
// Declarada no header porque as outras duas unidades da engine tambem
// precisam falar: sem log nos pontos de saida, uma falha de MFT e uma falha
// de disco produzem o MESMO sintomo no lado gerenciado (init_scan == 0), e
// nao ha como distinguir uma da outra sem abrir o log nativo na mao. Foi
// exatamente assim que um bug de nomes de API ficou invisivel por meses.
void LogDebug(const std::string& msg);

class RecoveryScanner {
public:
    RecoveryScanner();
    ~RecoveryScanner();

    bool Initialize(const std::string& drivePath);
    bool StartQuickScan(ProgressCallback callback);
    bool StartDeepScan(ProgressCallback callback);
    void Stop();
    
    void GetFoundFiles(FoundFile* outBuffer, int maxCount, int& outCount);
    bool RecoverFile(uint64_t fileId, const std::string& outputPath);

    // NTFS Support Helpers
    bool IsScanningNow() const { return isScanning.load(); }
    void UpdateProgress(double pct);
    void AddFoundFileEntry(const FoundFile& file);

    // Quantidade exata de entradas, sem montar o buffer. O lado C# usa isto
    // para alocar o buffer do tamanho certo — a versão anterior chutava
    // 1.500.000 structs e reservava meio gigabyte por via das dúvidas.
    int GetFoundCount();

    // Estatísticas do último filtro aplicado. Sem elas, um filtro que come
    // demais é indistinguível de um parser quebrado.
    void SetLastStats(const RecoveryStats& s) { lastStats = s; }
    void GetLastStats(RecoveryStats& out) const { out = lastStats; }

private:
    std::string currentDrive;
    HANDLE hDevice;
    uint64_t totalBytes;
    uint64_t sectorSize;
    
    std::atomic<bool> isScanning;
    std::atomic<bool> isPaused;
    std::atomic<double> currentProgress;
    std::atomic<uint64_t> currentSector;
    
    std::vector<FoundFile> foundFiles;
    std::mutex filesMutex;
    RecoveryStats lastStats;
    ProgressCallback currentCallback;
    std::thread scanThread;

    void DeepScanWorker();
    void QuickScanWorker();
    void CloseDrive();
    bool OpenDrive(const std::string& drivePath);
    uint64_t GetDriveSize();

    void ScanBufferForSignatures(uint8_t* buffer, size_t bytesRead, uint64_t offset);
    void AddFoundFile(const std::string& ext, uint64_t offset, uint64_t estimatedSize);
};

#endif // SCANNER_H
