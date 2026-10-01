// ============================================================================
//  DriverEngine.h  —  Voltris Driver Engine  (C++ Native Core)
//  Professional-grade driver detection, comparison and installation
//  Comparable to Driver Booster / Snappy Driver Installer internals
// ============================================================================
#pragma once

#include <windows.h>
#include <stdint.h>

// ── Export macro ──────────────────────────────────────────────────────────────
#ifdef DRIVERENGINE_EXPORTS
    #define DE_API extern "C" __declspec(dllexport)
#else
    #define DE_API extern "C" __declspec(dllimport)
#endif

// ── Limits ───────────────────────────────────────────────────────────────────
#define DE_MAX_DEVICES          512
#define DE_MAX_STR              512
#define DE_MAX_HWIDS            2048
#define DE_MAX_LOGS             8192
#define DE_LOG_LINE             1024

// ── Log levels ───────────────────────────────────────────────────────────────
#define DE_LOG_DEBUG            0
#define DE_LOG_INFO             1
#define DE_LOG_WARNING          2
#define DE_LOG_ERROR            3

// ── Device status flags ──────────────────────────────────────────────────────
#define DE_STATUS_OK            0x00
#define DE_STATUS_PROBLEM       0x01   // CM_PROB_* set
#define DE_STATUS_DISABLED      0x02
#define DE_STATUS_NO_DRIVER     0x04
#define DE_STATUS_UNSIGNED      0x08

// ── Signature verification result ────────────────────────────────────────────
#define DE_SIG_VALID_WHQL       0    // Microsoft WHQL signed
#define DE_SIG_VALID_CATALOG    1    // Signed via catalog
#define DE_SIG_SELF_SIGNED      2    // Self-signed (warn)
#define DE_SIG_UNSIGNED         3    // No signature (block)
#define DE_SIG_ERROR            4    // Could not verify

// ── Install result codes ─────────────────────────────────────────────────────
#define DE_INSTALL_OK           0
#define DE_INSTALL_REBOOT       1    // SUCCESS but needs reboot
#define DE_INSTALL_FAIL         2
#define DE_INSTALL_CANCELLED    3
#define DE_INSTALL_INF_NOTFOUND 4
#define DE_INSTALL_HW_MISMATCH  5
#define DE_INSTALL_BLOCKED_CLASS 6   // SCSIAdapter, HDC etc.

// ── Driver version (comparable u64) ─────────────────────────────────────────
#pragma pack(push, 1)
typedef struct {
    uint16_t major;
    uint16_t minor;
    uint16_t build;
    uint16_t revision;
} DE_VERSION;

// ── Device info record ────────────────────────────────────────────────────────
typedef struct {
    WCHAR  deviceInstanceId[DE_MAX_STR];   // e.g. PCI\VEN_10DE&DEV_1C82\4&...
    WCHAR  description[DE_MAX_STR];        // Human-readable name
    WCHAR  manufacturer[DE_MAX_STR];       // Mfr string from registry
    WCHAR  className[DE_MAX_STR];          // "Display", "Net", "Media"...
    WCHAR  classGuid[DE_MAX_STR];          // {4d36e968-...}
    WCHAR  hardwareIds[DE_MAX_HWIDS];      // Multi-sz (;-separated when exported)
    WCHAR  compatibleIds[DE_MAX_HWIDS];
    WCHAR  driverVersion[64];             // "27.21.14.5671"
    WCHAR  driverDate[32];               // "2024-03-15"
    WCHAR  driverProvider[DE_MAX_STR];    // "NVIDIA"
    WCHAR  infPath[MAX_PATH];            // Full path to .inf in DriverStore
    WCHAR  serviceKey[DE_MAX_STR];       // SCM service name
    UINT32 statusFlags;                   // DE_STATUS_* bitmask
    UINT32 problemCode;                   // CM_PROB_* if statusFlags & DE_STATUS_PROBLEM
    UINT32 signatureResult;               // DE_SIG_*
    DE_VERSION parsedVersion;
} DE_DEVICE_INFO;

// ── Log entry ────────────────────────────────────────────────────────────────
typedef struct {
    WCHAR  timestamp[32];    // "2026-04-07 19:45:23.456"
    INT32  level;            // DE_LOG_*
    WCHAR  message[DE_LOG_LINE];
} DE_LOG_ENTRY;

// ── Progress callback ────────────────────────────────────────────────────────
typedef void (CALLBACK *DE_PROGRESS_CALLBACK)(
    INT32   phase,         // 0=detecting, 1=verifying signatures, 2=complete
    INT32   current,
    INT32   total,
    LPCWSTR deviceName
);

// ── Install options ───────────────────────────────────────────────────────────
typedef struct {
    BOOL forceInstall;          // Bypass version check
    BOOL createRestorePoint;    // Call SRSetRestorePointW before install
    BOOL backupExisting;        // DISM export before install
    BOOL allowUnsigned;         // Allow non-WHQL (self-signed) — dangerous
    BOOL rankOnly;              // pnputil /add-driver without /install
    WCHAR backupDirectory[MAX_PATH];
} DE_INSTALL_OPTIONS;

// ── Scan results (opaque handle wrapper) ─────────────────────────────────────
typedef struct {
    INT32          deviceCount;
    DE_DEVICE_INFO devices[DE_MAX_DEVICES];
} DE_SCAN_RESULT;
#pragma pack(pop)

// ============================================================================
//  Exported API
// ============================================================================

// Lifecycle
DE_API INT32   DE_Initialize(LPCWSTR logDirectory);
DE_API void    DE_Shutdown();

// Device scanning
DE_API INT32   DE_ScanDevices(DE_SCAN_RESULT* outResult, DE_PROGRESS_CALLBACK callback);
DE_API INT32   DE_GetDeviceCount();

// Signature verification
DE_API INT32   DE_VerifyDriverSignature(LPCWSTR infPath, LPCWSTR catalogPath);
DE_API BOOL    DE_IsDriverWHQL(LPCWSTR infPath);

// Version comparison (returns >0 if a > b, 0 if equal, <0 if a < b)
DE_API INT32   DE_CompareVersions(LPCWSTR versionA, LPCWSTR versionB);
DE_API BOOL    DE_ParseVersion(LPCWSTR versionStr, DE_VERSION* outVersion);

// Installation
DE_API INT32   DE_InstallDriver(
    LPCWSTR            deviceInstanceId,
    LPCWSTR            infFilePath,
    DE_INSTALL_OPTIONS* options,
    DE_PROGRESS_CALLBACK callback
);
DE_API BOOL    DE_RollbackDriver(LPCWSTR deviceInstanceId);
DE_API BOOL    DE_ValidatePostInstall(LPCWSTR deviceInstanceId);

// Logging
DE_API INT32   DE_GetLogCount();
DE_API BOOL    DE_GetLogEntry(INT32 index, DE_LOG_ENTRY* outEntry);
DE_API void    DE_ClearLogs();
DE_API BOOL    DE_ExportLogs(LPCWSTR filePath);

// Utilities  
DE_API BOOL    DE_IsRunningAsAdmin();
DE_API BOOL    DE_ElevateProcess();
DE_API LPCWSTR DE_GetLastError();
DE_API LPCWSTR DE_GetVersion();
