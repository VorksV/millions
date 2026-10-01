// ============================================================================
//  DriverEngine.cpp  —  Voltris Driver Engine Implementation
//  Native C++ core: SetupAPI + CfgMgr32 + WinVerifyTrust
//  Architecture: x86 / x64 universal
// ============================================================================
#include "DriverEngine.h"

// Windows SDK
#include <setupapi.h>
#include <cfgmgr32.h>
#include <wintrust.h>
#include <softpub.h>
#include <mscat.h>
#include <newdev.h>
#include <shlwapi.h>
#include <shellapi.h>
#include <aclapi.h>
#include <strsafe.h>

// C++ STL
#include <vector>
#include <string>
#include <mutex>
#include <deque>
#include <algorithm>
#include <chrono>
#include <sstream>
#include <fstream>
#include <atomic>
#include <iomanip>
#include <ctime>
#include <memory>

// Pragma libs
#pragma comment(lib, "setupapi.lib")
#pragma comment(lib, "cfgmgr32.lib")
#pragma comment(lib, "wintrust.lib")
#pragma comment(lib, "crypt32.lib")
#pragma comment(lib, "shlwapi.lib")
#pragma comment(lib, "newdev.lib")
#pragma comment(lib, "advapi32.lib")
#pragma comment(lib, "shell32.lib")

// ── Restore point structs (inline, avoids srrestoreptapi.h SDK dependency) ────
#ifndef BEGIN_SYSTEM_CHANGE
#define BEGIN_SYSTEM_CHANGE   100
#define END_SYSTEM_CHANGE     101
#define APPLICATION_INSTALL   0

typedef struct _RESTOREPOINTINFOW {
    DWORD  dwEventType;
    DWORD  dwRestorePtType;
    INT64  llSequenceNumber;
    WCHAR  szDescription[256];
} RESTOREPOINTINFOW, *PRESTOREPOINTINFOW;

typedef struct _STATEMGRSTATUS {
    DWORD nStatus;
    INT64 llSequenceNumber;
} STATEMGRSTATUS, *PSTATEMGRSTATUS;
#endif

// ── DIF_ROLLBACK (not always in SDK headers) ──────────────────────────────────
#ifndef DIF_ROLLBACK
#define DIF_ROLLBACK ((DI_FUNCTION)0x00000020)
#endif

// ── Internal state ────────────────────────────────────────────────────────────
static std::mutex           g_logMutex;
static std::deque<DE_LOG_ENTRY> g_logs;
static std::wstring         g_logDirectory;
static std::wstring         g_lastError;
static std::atomic<bool>    g_initialized{ false };

// Device classes that must NEVER be touched by automated install (BSOD risk)
static const WCHAR* BLOCKED_CLASSES[] = {
    L"SCSIAdapter",  L"HDC", L"Volume", L"DiskDrive",
    L"FloppyDisk",   L"SmBus", L"System", L"Processor",
    nullptr
};

// ── Logging ────────────────────────────────────────────────────────────────────
static std::wstring GetTimestamp() {
    auto now = std::chrono::system_clock::now();
    auto timeT = std::chrono::system_clock::to_time_t(now);
    auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(
                  now.time_since_epoch()) % 1000;
    std::tm tmBuf{};
    localtime_s(&tmBuf, &timeT);
    WCHAR buf[64]{};
    swprintf_s(buf, L"%04d-%02d-%02d %02d:%02d:%02d.%03d",
               tmBuf.tm_year + 1900, tmBuf.tm_mon + 1, tmBuf.tm_mday,
               tmBuf.tm_hour, tmBuf.tm_min, tmBuf.tm_sec,
               (int)ms.count());
    return buf;
}

static void DE_Log(INT32 level, const wchar_t* fmt, ...) {
    WCHAR buf[DE_LOG_LINE]{};
    va_list args;
    va_start(args, fmt);
    vswprintf_s(buf, fmt, args);
    va_end(args);

    DE_LOG_ENTRY entry{};
    entry.level = level;
    StringCchCopyW(entry.timestamp, 32, GetTimestamp().c_str());
    StringCchCopyW(entry.message, DE_LOG_LINE, buf);

    {
        std::lock_guard<std::mutex> lk(g_logMutex);
        if (g_logs.size() >= DE_MAX_LOGS) g_logs.pop_front();
        g_logs.push_back(entry);
    }

    // Also append to file if log dir is set
    if (!g_logDirectory.empty()) {
        std::wstring path = g_logDirectory + L"\\DriverEngine.log";
        static std::mutex fileMutex;
        std::lock_guard<std::mutex> lk(fileMutex);
        FILE* f = nullptr;
        if (_wfopen_s(&f, path.c_str(), L"a, ccs=UTF-8") == 0 && f) {
            const WCHAR* lvlStr = (level == DE_LOG_DEBUG)   ? L"DEBUG" :
                                  (level == DE_LOG_INFO)    ? L"INFO " :
                                  (level == DE_LOG_WARNING) ? L"WARN " : L"ERROR";
            fwprintf(f, L"[%s][%s] %s\n", entry.timestamp, lvlStr, buf);
            fclose(f);
        }
    }

#ifdef _DEBUG
    const WCHAR* lvlStr = (level == DE_LOG_DEBUG)   ? L"[DBG]" :
                          (level == DE_LOG_INFO)    ? L"[INF]" :
                          (level == DE_LOG_WARNING) ? L"[WRN]" : L"[ERR]";
    WCHAR out[DE_LOG_LINE + 64]{};
    swprintf_s(out, L"[DE]%s %s\n", lvlStr, buf);
    OutputDebugStringW(out);
#endif
}

// ── Version parsing ────────────────────────────────────────────────────────────
static BOOL ParseVersionInternal(LPCWSTR str, DE_VERSION* out) {
    if (!str || !out) return FALSE;
    ZeroMemory(out, sizeof(*out));
    // Format: A.B.C.D or A.B.C or A.B
    int a = 0, b = 0, c = 0, d = 0;
    int n = swscanf_s(str, L"%d.%d.%d.%d", &a, &b, &c, &d);
    if (n < 1) {
        // Try format without dots (common NVIDIA: "55186" → 551.86)
        n = swscanf_s(str, L"%d", &a);
        if (n != 1) return FALSE;
    }
    out->major    = (UINT16)a;
    out->minor    = (UINT16)b;
    out->build    = (UINT16)c;
    out->revision = (UINT16)d;
    return TRUE;
}

static UINT64 VersionToU64(const DE_VERSION& v) {
    return ((UINT64)v.major    << 48)
         | ((UINT64)v.minor    << 32)
         | ((UINT64)v.build    << 16)
         |  (UINT64)v.revision;
}

// ── SetupAPI helpers ──────────────────────────────────────────────────────────
static std::wstring GetDeviceRegistryPropertyW(
    HDEVINFO hDevInfo,
    SP_DEVINFO_DATA& devInfoData,
    DWORD property)
{
    DWORD dataType = 0;
    DWORD reqSize  = 0;

    // First call: get req size
    SetupDiGetDeviceRegistryPropertyW(hDevInfo, &devInfoData, property,
                                      &dataType, nullptr, 0, &reqSize);
    if (reqSize == 0) return L"";

    std::vector<BYTE> buf(reqSize + 2, 0);
    if (!SetupDiGetDeviceRegistryPropertyW(hDevInfo, &devInfoData, property,
                                           &dataType,
                                           buf.data(), (DWORD)buf.size(), &reqSize))
        return L"";

    // Multi-sz: collapse to ;-separated
    if (dataType == REG_MULTI_SZ) {
        std::wstring result;
        const WCHAR* p = reinterpret_cast<const WCHAR*>(buf.data());
        while (*p) {
            if (!result.empty()) result += L';';
            result += p;
            p += wcslen(p) + 1;
        }
        return result;
    }

    return std::wstring(reinterpret_cast<const WCHAR*>(buf.data()));
}

static std::wstring GetDriverRegistryValue(LPCWSTR instanceId, LPCWSTR valueName) {
    WCHAR regPath[MAX_PATH]{};
    StringCchPrintfW(regPath, MAX_PATH,
                     L"SYSTEM\\CurrentControlSet\\Enum\\%s", instanceId);
    HKEY hKey = nullptr;
    if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, regPath, 0, KEY_READ, &hKey) != ERROR_SUCCESS)
        return L"";

    WCHAR val[DE_MAX_STR]{};
    DWORD size = sizeof(val);
    DWORD type = REG_SZ;
    RegQueryValueExW(hKey, valueName, nullptr, &type,
                     reinterpret_cast<LPBYTE>(val), &size);
    RegCloseKey(hKey);
    return val;
}

static std::wstring GetDriverClassInfo(LPCWSTR instanceId,
                                       LPCWSTR& driverVersion,
                                       LPCWSTR& driverDate,
                                       LPCWSTR& driverProvider,
                                       LPCWSTR& infPath,
    std::wstring& outVersion,
    std::wstring& outDate,
    std::wstring& outProvider,
    std::wstring& outInf)
{
    // Navigate HKLM\SYSTEM\CCS\Enum\{instanceId}\Driver →
    // Then read from HKLM\SYSTEM\CCS\Control\Class\{classGuid}\{subKey}
    std::wstring driver = GetDriverRegistryValue(instanceId, L"Driver");
    if (driver.empty()) return L"";

    WCHAR classPath[MAX_PATH]{};
    StringCchPrintfW(classPath, MAX_PATH,
                     L"SYSTEM\\CurrentControlSet\\Control\\Class\\%s", driver.c_str());

    HKEY hKey = nullptr;
    if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, classPath, 0, KEY_READ, &hKey) != ERROR_SUCCESS)
        return driver;

    auto readStr = [&](const WCHAR* name) {
        WCHAR val[DE_MAX_STR]{};
        DWORD size = sizeof(val), type = REG_SZ;
        RegQueryValueExW(hKey, name, nullptr, &type,
                         reinterpret_cast<LPBYTE>(val), &size);
        return std::wstring(val);
    };

    outVersion  = readStr(L"DriverVersion");
    outDate     = readStr(L"DriverDate");
    outProvider = readStr(L"ProviderName");
    outInf      = readStr(L"InfPath");
    RegCloseKey(hKey);
    return driver;
}

// ── Signature verification ────────────────────────────────────────────────────
static INT32 VerifyFileSignature(LPCWSTR filePath) {
    WINTRUST_FILE_INFO fileInfo{};
    fileInfo.cbStruct    = sizeof(fileInfo);
    fileInfo.pcwszFilePath = filePath;

    GUID actionGuid = WINTRUST_ACTION_GENERIC_VERIFY_V2;
    WINTRUST_DATA wtd{};
    wtd.cbStruct            = sizeof(wtd);
    wtd.dwUIChoice          = WTD_UI_NONE;
    wtd.fdwRevocationChecks = WTD_REVOKE_NONE;
    wtd.dwUnionChoice       = WTD_CHOICE_FILE;
    wtd.dwStateAction       = WTD_STATEACTION_VERIFY;
    wtd.pFile               = &fileInfo;

    LONG result = WinVerifyTrust(nullptr, &actionGuid, &wtd);

    // Release state
    wtd.dwStateAction = WTD_STATEACTION_CLOSE;
    WinVerifyTrust(nullptr, &actionGuid, &wtd);

    if (result == ERROR_SUCCESS)    return DE_SIG_VALID_WHQL;
    if (result == TRUST_E_NOSIGNATURE) return DE_SIG_UNSIGNED;
    if (result == CERT_E_UNTRUSTEDROOT || result == TRUST_E_SUBJECT_NOT_TRUSTED)
        return DE_SIG_SELF_SIGNED;
    return DE_SIG_ERROR;
}

// ── Check if a device class is blocked ────────────────────────────────────────
static BOOL IsBlockedClass(LPCWSTR className) {
    for (int i = 0; BLOCKED_CLASSES[i]; i++) {
        if (_wcsicmp(className, BLOCKED_CLASSES[i]) == 0) return TRUE;
    }
    return FALSE;
}

// ── Check if device has problem code ─────────────────────────────────────────
static UINT32 GetCmProblemCode(LPCWSTR instanceId) {
    DEVINST devInst = 0;
    if (CM_Locate_DevNodeW(&devInst, const_cast<DEVINSTID_W>(instanceId),
                           CM_LOCATE_DEVNODE_NORMAL) != CR_SUCCESS)
        return 0;
    ULONG status = 0, problem = 0;
    CM_Get_DevNode_Status(&status, &problem, devInst, 0);
    return (UINT32)problem;
}

// ============================================================================
//  Public API Implementation
// ============================================================================

DE_API INT32 DE_Initialize(LPCWSTR logDirectory) {
    if (g_initialized) {
        DE_Log(DE_LOG_WARNING, L"DE_Initialize called again — already initialized.");
        return 0;
    }
    if (logDirectory && *logDirectory) {
        g_logDirectory = logDirectory;
        CreateDirectoryW(logDirectory, nullptr);
    }

    g_initialized = true;
    DE_Log(DE_LOG_INFO, L"═══════════════════════════════════════════════════");
    DE_Log(DE_LOG_INFO, L"Voltris Driver Engine v1.0  —  %s build",
           sizeof(void*) == 8 ? L"x64" : L"x86");
    DE_Log(DE_LOG_INFO, L"Initialized. Log dir: %s",
           logDirectory ? logDirectory : L"(none — memory only)");
    DE_Log(DE_LOG_INFO, L"═══════════════════════════════════════════════════");
    return 0;
}

DE_API void DE_Shutdown() {
    DE_Log(DE_LOG_INFO, L"DE_Shutdown called. Cleaning up.");
    g_initialized = false;
}

DE_API INT32 DE_ScanDevices(DE_SCAN_RESULT* outResult, DE_PROGRESS_CALLBACK callback) {
    if (!g_initialized) {
        g_lastError = L"Engine not initialized. Call DE_Initialize first.";
        return -1;
    }
    if (!outResult) {
        g_lastError = L"outResult is null.";
        return -1;
    }

    DE_Log(DE_LOG_INFO, L"DE_ScanDevices — starting SetupAPI enumeration...");
    ZeroMemory(outResult, sizeof(*outResult));

    HDEVINFO hDevInfo = SetupDiGetClassDevsW(
        nullptr, nullptr, nullptr,
        DIGCF_PRESENT | DIGCF_ALLCLASSES);

    if (hDevInfo == INVALID_HANDLE_VALUE) {
        g_lastError = L"SetupDiGetClassDevs failed — access denied?";
        DE_Log(DE_LOG_ERROR, L"%s  GLE=%lu", g_lastError.c_str(), GetLastError());
        return -2;
    }

    SP_DEVINFO_DATA devInfoData{};
    devInfoData.cbSize = sizeof(devInfoData);
    INT32 count = 0;

    // First pass: count
    INT32 total = 0;
    for (DWORD i = 0; SetupDiEnumDeviceInfo(hDevInfo, i, &devInfoData); i++)
        total++;
    DE_Log(DE_LOG_INFO, L"Total devices to enumerate: %d", total);

    devInfoData = {};
    devInfoData.cbSize = sizeof(devInfoData);

    for (DWORD i = 0; SetupDiEnumDeviceInfo(hDevInfo, i, &devInfoData); i++) {
        if (count >= DE_MAX_DEVICES) {
            DE_Log(DE_LOG_WARNING, L"Device limit (%d) reached.", DE_MAX_DEVICES);
            break;
        }

        DE_DEVICE_INFO& dev = outResult->devices[count];
        ZeroMemory(&dev, sizeof(dev));

        // ── Device Instance ID ────────────────────────────────────────────
        WCHAR instanceId[512]{};
        SetupDiGetDeviceInstanceIdW(hDevInfo, &devInfoData,
                                    instanceId, _countof(instanceId), nullptr);
        StringCchCopyW(dev.deviceInstanceId, DE_MAX_STR, instanceId);

        // ── Basic string properties ───────────────────────────────────────
        auto desc         = GetDeviceRegistryPropertyW(hDevInfo, devInfoData, SPDRP_DEVICEDESC);
        auto mfr          = GetDeviceRegistryPropertyW(hDevInfo, devInfoData, SPDRP_MFG);
        auto className    = GetDeviceRegistryPropertyW(hDevInfo, devInfoData, SPDRP_CLASS);
        auto classGuid    = GetDeviceRegistryPropertyW(hDevInfo, devInfoData, SPDRP_CLASSGUID);
        auto hwIds        = GetDeviceRegistryPropertyW(hDevInfo, devInfoData, SPDRP_HARDWAREID);
        auto compatIds    = GetDeviceRegistryPropertyW(hDevInfo, devInfoData, SPDRP_COMPATIBLEIDS);
        auto serviceKey   = GetDeviceRegistryPropertyW(hDevInfo, devInfoData, SPDRP_SERVICE);

        StringCchCopyW(dev.description,   DE_MAX_STR,   desc.c_str());
        StringCchCopyW(dev.manufacturer,  DE_MAX_STR,   mfr.c_str());
        StringCchCopyW(dev.className,     DE_MAX_STR,   className.c_str());
        StringCchCopyW(dev.classGuid,     DE_MAX_STR,   classGuid.c_str());
        StringCchCopyW(dev.hardwareIds,   DE_MAX_HWIDS, hwIds.c_str());
        StringCchCopyW(dev.compatibleIds, DE_MAX_HWIDS, compatIds.c_str());
        StringCchCopyW(dev.serviceKey,    DE_MAX_STR,   serviceKey.c_str());

        // ── Driver info from Class registry key ───────────────────────────
        std::wstring ver, date, provider, inf;
        LPCWSTR dummy1 = nullptr, dummy2 = nullptr, dummy3 = nullptr, dummy4 = nullptr;
        GetDriverClassInfo(instanceId, dummy1, dummy2, dummy3, dummy4,
                           ver, date, provider, inf);

        StringCchCopyW(dev.driverVersion,  64,         ver.c_str());
        StringCchCopyW(dev.driverDate,     32,         date.c_str());
        StringCchCopyW(dev.driverProvider, DE_MAX_STR, provider.c_str());
        StringCchCopyW(dev.infPath,        MAX_PATH,   inf.c_str());
        ParseVersionInternal(ver.c_str(), &dev.parsedVersion);

        // ── Hardware status ───────────────────────────────────────────────
        ULONG cmStatus = 0, cmProblem = 0;
        DEVINST devInst = 0;
        if (CM_Locate_DevNodeW(&devInst, instanceId,
                               CM_LOCATE_DEVNODE_NORMAL) == CR_SUCCESS) {
            CM_Get_DevNode_Status(&cmStatus, &cmProblem, devInst, 0);
        }
        if (cmProblem != 0) {
            dev.statusFlags |= DE_STATUS_PROBLEM;
            dev.problemCode  = cmProblem;
        }
        if (cmStatus & DN_DRIVER_BLOCKED) dev.statusFlags |= DE_STATUS_NO_DRIVER;
        if (ver.empty())                  dev.statusFlags |= DE_STATUS_NO_DRIVER;

        // ── Signature verification (only if .inf exists) ──────────────────
        if (!inf.empty() && GetFileAttributesW(inf.c_str()) != INVALID_FILE_ATTRIBUTES) {
            dev.signatureResult = (UINT32)VerifyFileSignature(inf.c_str());
            if (dev.signatureResult >= DE_SIG_SELF_SIGNED)
                dev.statusFlags |= DE_STATUS_UNSIGNED;
        } else {
            dev.signatureResult = DE_SIG_ERROR;
        }

        // ── Progress callback ─────────────────────────────────────────────
        if (callback)
            callback(0, (INT32)i + 1, total, dev.description);

        DE_Log(DE_LOG_DEBUG,
               L"[%03d] %-60s  v%-20s  sig=%d  prob=%lu",
               count, desc.empty() ? instanceId : desc.c_str(),
               ver.c_str(), dev.signatureResult, cmProblem);

        count++;
    }

    SetupDiDestroyDeviceInfoList(hDevInfo);
    outResult->deviceCount = count;

    DE_Log(DE_LOG_INFO, L"DE_ScanDevices complete — %d devices found.", count);
    if (callback) callback(2, count, count, L"Scan complete");
    return count;
}

DE_API INT32 DE_GetDeviceCount() {
    // Returns 0 without a scan — caller should use DE_ScanDevices return value
    return 0;
}

DE_API INT32 DE_VerifyDriverSignature(LPCWSTR infPath, LPCWSTR /*catalogPath*/) {
    if (!infPath) return DE_SIG_ERROR;
    DE_Log(DE_LOG_INFO, L"DE_VerifyDriverSignature: %s", infPath);
    INT32 res = VerifyFileSignature(infPath);
    const WCHAR* labels[] = { L"WHQL", L"Catalog", L"Self-signed", L"Unsigned", L"Error" };
    DE_Log(DE_LOG_INFO, L"  → %s", labels[res < 5 ? res : 4]);
    return res;
}

DE_API BOOL DE_IsDriverWHQL(LPCWSTR infPath) {
    return DE_VerifyDriverSignature(infPath, nullptr) == DE_SIG_VALID_WHQL;
}

DE_API INT32 DE_CompareVersions(LPCWSTR versionA, LPCWSTR versionB) {
    DE_VERSION va{}, vb{};
    if (!ParseVersionInternal(versionA, &va)) return -1;
    if (!ParseVersionInternal(versionB, &vb)) return  1;
    UINT64 a64 = VersionToU64(va);
    UINT64 b64 = VersionToU64(vb);
    if (a64 > b64) return  1;
    if (a64 < b64) return -1;
    return 0;
}

DE_API BOOL DE_ParseVersion(LPCWSTR versionStr, DE_VERSION* outVersion) {
    return ParseVersionInternal(versionStr, outVersion);
}

DE_API INT32 DE_InstallDriver(
    LPCWSTR             deviceInstanceId,
    LPCWSTR             infFilePath,
    DE_INSTALL_OPTIONS* options,
    DE_PROGRESS_CALLBACK callback)
{
    DE_Log(DE_LOG_INFO, L"═══ DE_InstallDriver ═══════════════════════════════");
    DE_Log(DE_LOG_INFO, L"  Device   : %s", deviceInstanceId);
    DE_Log(DE_LOG_INFO, L"  INF      : %s", infFilePath);

    if (!infFilePath || GetFileAttributesW(infFilePath) == INVALID_FILE_ATTRIBUTES) {
        g_lastError = L"INF file not found.";
        DE_Log(DE_LOG_ERROR, L"  %s", g_lastError.c_str());
        return DE_INSTALL_INF_NOTFOUND;
    }

    // ── 1. Class safety check ─────────────────────────────────────────────
    std::wstring className = GetDriverRegistryValue(deviceInstanceId, L"Class");
    DE_Log(DE_LOG_INFO, L"  Class    : %s", className.c_str());
    if (IsBlockedClass(className.c_str())) {
        g_lastError = L"Device class blocked for automated install (BSOD risk).";
        DE_Log(DE_LOG_ERROR, L"  BLOCKED: %s", className.c_str());
        return DE_INSTALL_BLOCKED_CLASS;
    }

    // ── 2. Signature verification ─────────────────────────────────────────
    INT32 sigResult = VerifyFileSignature(infFilePath);
    DE_Log(DE_LOG_INFO, L"  Signature: %d (0=WHQL,1=Catalog,2=Self,3=Unsigned)",
           sigResult);
    if (sigResult == DE_SIG_UNSIGNED && (!options || !options->allowUnsigned)) {
        g_lastError = L"Driver is unsigned. Install blocked by security policy.";
        DE_Log(DE_LOG_ERROR, L"  %s", g_lastError.c_str());
        return DE_INSTALL_FAIL;
    }

    // ── 3. Restore point (optional) ───────────────────────────────────────
    if (options && options->createRestorePoint) {
        DE_Log(DE_LOG_INFO, L"  Creating System Restore Point...");
        // SRSetRestorePointW via Srclient.dll
        typedef BOOL (WINAPI *pfnSRSetRestorePointW)(PRESTOREPOINTINFOW, PSTATEMGRSTATUS);
        HMODULE hSrc = LoadLibraryExW(L"Srclient.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
        if (hSrc) {
            auto fn = (pfnSRSetRestorePointW)GetProcAddress(hSrc, "SRSetRestorePointW");
            if (fn) {
                RESTOREPOINTINFOW rp{};
                rp.dwEventType    = BEGIN_SYSTEM_CHANGE;
                rp.dwRestorePtType = APPLICATION_INSTALL;
                StringCchCopyW(rp.szDescription, 64, L"Voltris Driver Update");
                STATEMGRSTATUS sms{};
                if (fn(&rp, &sms))
                    DE_Log(DE_LOG_INFO, L"  Restore point created (seq %lld).",
                           sms.llSequenceNumber);
                else
                    DE_Log(DE_LOG_WARNING, L"  Restore point FAILED (SR may be disabled).");
            }
            FreeLibrary(hSrc);
        }
    }

    // ── 4. Backup via DISM export ─────────────────────────────────────────
    if (options && options->backupExisting && *options->backupDirectory) {
        DE_Log(DE_LOG_INFO, L"  Backing up DriverStore to: %s",
               options->backupDirectory);
        WCHAR cmd[MAX_PATH * 2]{};
        StringCchPrintfW(cmd, _countof(cmd),
            L"pnputil.exe /export-driver * \"%s\"", options->backupDirectory);
        _wsystem(cmd);
        DE_Log(DE_LOG_INFO, L"  Backup pnputil command executed.");
    }

    if (callback) callback(1, 0, 3, L"Installing driver...");

    // ── 5. Core installation: UpdateDriverForPlugAndPlayDevicesW ──────────
    BOOL rebootRequired = FALSE;
    BOOL result = UpdateDriverForPlugAndPlayDevicesW(
        nullptr,                    // hWnd — headless
        deviceInstanceId,           // hardware ID (first HW ID from INF)
        infFilePath,
        INSTALLFLAG_FORCE,
        &rebootRequired);

    DWORD gle = GetLastError();
    DE_Log(DE_LOG_INFO,
           L"  UpdateDriverForPnP: result=%d  reboot=%d  GLE=%lu",
           result, rebootRequired, gle);

    if (!result) {
        // Fallback: SetupDiCallClassInstaller with DIF_INSTALLDEVICE
        DE_Log(DE_LOG_WARNING, L"  UpdateDriverForPnP FAILED. Trying SetupAPI fallback...");

        HDEVINFO hDev = SetupDiGetClassDevsW(nullptr, deviceInstanceId,
                                              nullptr, DIGCF_PRESENT | DIGCF_ALLCLASSES);
        if (hDev != INVALID_HANDLE_VALUE) {
            SP_DEVINFO_DATA did{};
            did.cbSize = sizeof(did);
            for (DWORD i = 0; SetupDiEnumDeviceInfo(hDev, i, &did); i++) {
                WCHAR id[512]{};
                SetupDiGetDeviceInstanceIdW(hDev, &did, id, _countof(id), nullptr);
                if (_wcsicmp(id, deviceInstanceId) != 0) continue;

                SP_DRVINFO_DATA drvData{};
                drvData.cbSize = sizeof(drvData);

                // Build driver info list from INF
                if (SetupDiBuildDriverInfoList(hDev, &did, SPDIT_CLASSDRIVER) &&
                    SetupDiEnumDriverInfoW(hDev, &did,
                                           SPDIT_CLASSDRIVER, 0, &drvData)) {
                    SetupDiSetSelectedDriverW(hDev, &did, &drvData);
                }

                result = SetupDiCallClassInstaller(DIF_INSTALLDEVICE, hDev, &did);
                gle    = GetLastError();
                DE_Log(DE_LOG_INFO,
                       L"  SetupDiCallClassInstaller: result=%d  GLE=%lu",
                       result, gle);
                break;
            }
            SetupDiDestroyDeviceInfoList(hDev);
        }
    }

    if (callback) callback(1, 2, 3, L"Validating device...");

    if (!result && gle != ERROR_SUCCESS && gle != ERROR_NO_SUCH_DEVINST) {
        g_lastError = L"Driver installation failed via all methods.";
        DE_Log(DE_LOG_ERROR, L"  INSTALL FAILED. GLE=%lu", gle);
        return DE_INSTALL_FAIL;
    }

    // ── 6. Post-install validation ────────────────────────────────────────
    if (!DE_ValidatePostInstall(deviceInstanceId)) {
        DE_Log(DE_LOG_ERROR, L"  Post-install validation FAILED — rolling back...");
        DE_RollbackDriver(deviceInstanceId);
        return DE_INSTALL_FAIL;
    }

    if (callback) callback(1, 3, 3, L"Install complete.");
    DE_Log(DE_LOG_INFO, L"  ✅ Install SUCCESS.  Reboot required: %s",
           rebootRequired ? L"YES" : L"NO");
    return rebootRequired ? DE_INSTALL_REBOOT : DE_INSTALL_OK;
}

DE_API BOOL DE_RollbackDriver(LPCWSTR deviceInstanceId) {
    DE_Log(DE_LOG_WARNING, L"DE_RollbackDriver: %s", deviceInstanceId);
    // Use devcon / SetupAPI rollback via DIF_ROLLBACK
    HDEVINFO hDev = SetupDiGetClassDevsW(nullptr, deviceInstanceId,
                                          nullptr, DIGCF_PRESENT | DIGCF_ALLCLASSES);
    if (hDev == INVALID_HANDLE_VALUE) {
        DE_Log(DE_LOG_ERROR, L"  SetupDiGetClassDevs failed for rollback.");
        return FALSE;
    }
    SP_DEVINFO_DATA did{};
    did.cbSize = sizeof(did);
    BOOL rolled = FALSE;
    for (DWORD i = 0; SetupDiEnumDeviceInfo(hDev, i, &did); i++) {
        WCHAR id[512]{};
        SetupDiGetDeviceInstanceIdW(hDev, &did, id, _countof(id), nullptr);
        if (_wcsicmp(id, deviceInstanceId) != 0) continue;
        rolled = SetupDiCallClassInstaller(DIF_ROLLBACK, hDev, &did);
        DE_Log(DE_LOG_INFO, L"  DIF_ROLLBACK: %s  GLE=%lu",
               rolled ? L"OK" : L"FAIL", GetLastError());
        break;
    }
    SetupDiDestroyDeviceInfoList(hDev);
    return rolled;
}

DE_API BOOL DE_ValidatePostInstall(LPCWSTR deviceInstanceId) {
    DE_Log(DE_LOG_INFO, L"DE_ValidatePostInstall: %s", deviceInstanceId);

    // Wait up to 3 seconds for device to settle
    Sleep(500);

    UINT32 prob = GetCmProblemCode(deviceInstanceId);
    if (prob != 0) {
        DE_Log(DE_LOG_ERROR,
               L"  Device has CM problem code %lu after install → FAIL", prob);
        return FALSE;
    }

    DE_Log(DE_LOG_INFO, L"  Device has no problem code → PASS");
    return TRUE;
}

DE_API BOOL DE_IsRunningAsAdmin() {
    BOOL isAdmin = FALSE;
    HANDLE hToken = nullptr;
    if (OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &hToken)) {
        TOKEN_ELEVATION elevation{};
        DWORD size = sizeof(elevation);
        if (GetTokenInformation(hToken, TokenElevation, &elevation,
                                 sizeof(elevation), &size))
            isAdmin = elevation.TokenIsElevated;
        CloseHandle(hToken);
    }
    return isAdmin;
}

DE_API BOOL DE_ElevateProcess() {
    if (DE_IsRunningAsAdmin()) return TRUE;
    WCHAR path[MAX_PATH]{};
    GetModuleFileNameW(nullptr, path, MAX_PATH);
    SHELLEXECUTEINFOW sei{};
    sei.cbSize  = sizeof(sei);
    sei.lpVerb  = L"runas";
    sei.lpFile  = path;
    sei.nShow   = SW_SHOW;
    return ShellExecuteExW(&sei);
}

DE_API INT32 DE_GetLogCount() {
    std::lock_guard<std::mutex> lk(g_logMutex);
    return (INT32)g_logs.size();
}

DE_API BOOL DE_GetLogEntry(INT32 index, DE_LOG_ENTRY* outEntry) {
    if (!outEntry) return FALSE;
    std::lock_guard<std::mutex> lk(g_logMutex);
    if (index < 0 || index >= (INT32)g_logs.size()) return FALSE;
    *outEntry = g_logs[(size_t)index];
    return TRUE;
}

DE_API void DE_ClearLogs() {
    std::lock_guard<std::mutex> lk(g_logMutex);
    g_logs.clear();
}

DE_API BOOL DE_ExportLogs(LPCWSTR filePath) {
    if (!filePath) return FALSE;
    std::lock_guard<std::mutex> lk(g_logMutex);
    FILE* f = nullptr;
    if (_wfopen_s(&f, filePath, L"w, ccs=UTF-8") != 0 || !f) return FALSE;
    for (auto& e : g_logs) {
        const WCHAR* lvl = (e.level == DE_LOG_DEBUG)   ? L"DEBUG" :
                           (e.level == DE_LOG_INFO)    ? L"INFO " :
                           (e.level == DE_LOG_WARNING) ? L"WARN " : L"ERROR";
        fwprintf(f, L"[%s][%s] %s\n", e.timestamp, lvl, e.message);
    }
    fclose(f);
    return TRUE;
}

DE_API LPCWSTR DE_GetLastError() {
    return g_lastError.c_str();
}

DE_API LPCWSTR DE_GetVersion() {
    return L"1.0.0.0";
}

// ── DLL entry ─────────────────────────────────────────────────────────────────
BOOL APIENTRY DllMain(HMODULE /*hMod*/, DWORD reason, LPVOID /*lpRes*/) {
    switch (reason) {
        case DLL_PROCESS_ATTACH:
            DisableThreadLibraryCalls(nullptr);
            break;
        case DLL_PROCESS_DETACH:
            if (g_initialized) DE_Shutdown();
            break;
    }
    return TRUE;
}
