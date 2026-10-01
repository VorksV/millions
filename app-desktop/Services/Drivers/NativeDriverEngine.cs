using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Drivers
{
    // =========================================================================
    //  Managed mirror of DE_VERSION (pack=1 in C++)
    // =========================================================================
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct DE_VERSION
    {
        public ushort Major;
        public ushort Minor;
        public ushort Build;
        public ushort Revision;

        public override string ToString() => $"{Major}.{Minor}.{Build}.{Revision}";
    }

    // =========================================================================
    //  Managed mirror of DE_DEVICE_INFO
    // =========================================================================
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
    public struct DE_DEVICE_INFO_NATIVE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)]
        public string DeviceInstanceId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)]
        public string Description;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)]
        public string Manufacturer;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)]
        public string ClassName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)]
        public string ClassGuid;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 2048)]
        public string HardwareIds;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 2048)]
        public string CompatibleIds;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string DriverVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DriverDate;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)]
        public string DriverProvider;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string InfPath;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)]
        public string ServiceKey;

        public uint StatusFlags;
        public uint ProblemCode;
        public uint SignatureResult;
        public DE_VERSION ParsedVersion;
    }

    // =========================================================================
    //  Managed DE_SCAN_RESULT (512 devices * sizeof(DE_DEVICE_INFO_NATIVE))
    // =========================================================================
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
    public struct DE_SCAN_RESULT_NATIVE
    {
        public int DeviceCount;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 512)]
        public DE_DEVICE_INFO_NATIVE[] Devices;
    }

    // =========================================================================
    //  Managed DE_INSTALL_OPTIONS
    // =========================================================================
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
    public struct DE_INSTALL_OPTIONS_NATIVE
    {
        [MarshalAs(UnmanagedType.Bool)] public bool ForceInstall;
        [MarshalAs(UnmanagedType.Bool)] public bool CreateRestorePoint;
        [MarshalAs(UnmanagedType.Bool)] public bool BackupExisting;
        [MarshalAs(UnmanagedType.Bool)] public bool AllowUnsigned;
        [MarshalAs(UnmanagedType.Bool)] public bool RankOnly;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string BackupDirectory;
    }

    // =========================================================================
    //  Managed DE_LOG_ENTRY
    // =========================================================================
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
    public struct DE_LOG_ENTRY_NATIVE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Timestamp;

        public int Level;  // 0=DEBUG, 1=INFO, 2=WARN, 3=ERROR

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)]
        public string Message;
    }

    // =========================================================================
    //  Managed progress delegate (mirrors DE_PROGRESS_CALLBACK)
    // =========================================================================
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    public delegate void DE_ProgressCallback(int phase, int current, int total,
                                             [MarshalAs(UnmanagedType.LPWStr)] string deviceName);

    // =========================================================================
    //  Status constants (mirror DriverEngine.h defines)
    // =========================================================================
    public static class DE_Status
    {
        public const uint OK       = 0x00;
        public const uint Problem  = 0x01;
        public const uint Disabled = 0x02;
        public const uint NoDriver = 0x04;
        public const uint Unsigned = 0x08;
    }

    public static class DE_Sig
    {
        public const uint WHQL       = 0;
        public const uint Catalog    = 1;
        public const uint SelfSigned = 2;
        public const uint Unsigned   = 3;
        public const uint Error      = 4;
    }

    public static class DE_InstallResult
    {
        public const int OK            = 0;
        public const int Reboot        = 1;
        public const int Fail          = 2;
        public const int Cancelled     = 3;
        public const int InfNotFound   = 4;
        public const int HwMismatch    = 5;
        public const int BlockedClass  = 6;
    }

    // =========================================================================
    //  Managed device info (clean model for C# consumption)
    // =========================================================================
    public class NativeDriverDeviceInfo
    {
        public string DeviceInstanceId  { get; init; } = string.Empty;
        public string Description       { get; init; } = string.Empty;
        public string Manufacturer      { get; init; } = string.Empty;
        public string ClassName         { get; init; } = string.Empty;
        public string ClassGuid         { get; init; } = string.Empty;
        public string HardwareIds       { get; init; } = string.Empty;
        public string CompatibleIds     { get; init; } = string.Empty;
        public string DriverVersion     { get; init; } = string.Empty;
        public string DriverDate        { get; init; } = string.Empty;
        public string DriverProvider    { get; init; } = string.Empty;
        public string InfPath           { get; init; } = string.Empty;
        public string ServiceKey        { get; init; } = string.Empty;
        public bool   HasProblem        { get; init; }
        public uint   ProblemCode       { get; init; }
        public bool   IsUnsigned        { get; init; }
        public uint   SignatureResult   { get; init; }
        public DE_VERSION ParsedVersion { get; init; }

        public string SignatureLabel => SignatureResult switch {
            DE_Sig.WHQL       => "✅ WHQL",
            DE_Sig.Catalog    => "✔ Catalog",
            DE_Sig.SelfSigned => "⚠ Self-signed",
            DE_Sig.Unsigned   => "❌ Unsigned",
            _ => "? Unknown"
        };

        public static NativeDriverDeviceInfo FromNative(in DE_DEVICE_INFO_NATIVE n) => new() {
            DeviceInstanceId = n.DeviceInstanceId ?? string.Empty,
            Description      = n.Description      ?? string.Empty,
            Manufacturer     = n.Manufacturer     ?? string.Empty,
            ClassName        = n.ClassName        ?? string.Empty,
            ClassGuid        = n.ClassGuid        ?? string.Empty,
            HardwareIds      = n.HardwareIds      ?? string.Empty,
            CompatibleIds    = n.CompatibleIds    ?? string.Empty,
            DriverVersion    = n.DriverVersion    ?? string.Empty,
            DriverDate       = n.DriverDate       ?? string.Empty,
            DriverProvider   = n.DriverProvider   ?? string.Empty,
            InfPath          = n.InfPath          ?? string.Empty,
            ServiceKey       = n.ServiceKey       ?? string.Empty,
            HasProblem       = (n.StatusFlags & DE_Status.Problem)  != 0,
            IsUnsigned       = (n.StatusFlags & DE_Status.Unsigned) != 0,
            ProblemCode      = n.ProblemCode,
            SignatureResult  = n.SignatureResult,
            ParsedVersion    = n.ParsedVersion};
    }

    // =========================================================================
    //  Log entry (managed)
    // =========================================================================
    public class DriverEngineLogEntry
    {
        public string Timestamp { get; init; } = string.Empty;
        public int    Level     { get; init; }
        public string Message   { get; init; } = string.Empty;
        public string LevelLabel => Level switch {
            0 => "DEBUG", 1 => "INFO", 2 => "WARN", 3 => "ERROR", _ => "UNKNOWN"
        };

        public static DriverEngineLogEntry FromNative(in DE_LOG_ENTRY_NATIVE n) => new() {
            Timestamp = n.Timestamp ?? string.Empty,
            Level     = n.Level,
            Message   = n.Message   ?? string.Empty};
    }

    // =========================================================================
    //  DriverEngineInterop  —  P/Invoke wrapper (auto-selects x64/x86 DLL)
    // =========================================================================
    public static class DriverEngineInterop
    {
        // DLL handle (loaded manually to pick correct arch)
        private static IntPtr _hLib = IntPtr.Zero;
        private static string _loadedPath = string.Empty;

        // ── Delegate types matching C++ exports ───────────────────────────
        private delegate int    d_Initialize([MarshalAs(UnmanagedType.LPWStr)] string logDir);
        private delegate void   d_Shutdown();
        private delegate int    d_ScanDevices(IntPtr outResult, IntPtr callback);
        private delegate int    d_VerifyDriverSignature(
            [MarshalAs(UnmanagedType.LPWStr)] string inf,
            [MarshalAs(UnmanagedType.LPWStr)] string? cat);
        private delegate bool   d_IsDriverWHQL([MarshalAs(UnmanagedType.LPWStr)] string inf);
        private delegate int    d_CompareVersions(
            [MarshalAs(UnmanagedType.LPWStr)] string a,
            [MarshalAs(UnmanagedType.LPWStr)] string b);
        private delegate bool   d_ParseVersion(
            [MarshalAs(UnmanagedType.LPWStr)] string str, out DE_VERSION v);
        private delegate int    d_InstallDriver(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            [MarshalAs(UnmanagedType.LPWStr)] string infPath,
            IntPtr options, IntPtr callback);
        private delegate bool   d_RollbackDriver([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        private delegate bool   d_ValidatePostInstall([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        private delegate bool   d_IsRunningAsAdmin();
        private delegate bool   d_ElevateProcess();
        private delegate int    d_GetLogCount();
        private delegate bool   d_GetLogEntry(int index, out DE_LOG_ENTRY_NATIVE entry);
        private delegate void   d_ClearLogs();
        private delegate bool   d_ExportLogs([MarshalAs(UnmanagedType.LPWStr)] string path);
        private delegate IntPtr d_GetLastError();
        private delegate IntPtr d_GetVersion();

        // ── Cached delegates (resolved on Load) ───────────────────────────
        private static d_Initialize?      _Initialize;
        private static d_Shutdown?        _Shutdown;
        private static d_ScanDevices?     _ScanDevices;
        private static d_VerifyDriverSignature? _VerifySig;
        private static d_IsDriverWHQL?   _IsWHQL;
        private static d_CompareVersions? _CompareVer;
        private static d_ParseVersion?   _ParseVer;
        private static d_InstallDriver?  _Install;
        private static d_RollbackDriver? _Rollback;
        private static d_ValidatePostInstall? _Validate;
        private static d_IsRunningAsAdmin? _IsAdmin;
        private static d_ElevateProcess? _Elevate;
        private static d_GetLogCount?    _GetLogCount;
        private static d_GetLogEntry?    _GetLogEntry;
        private static d_ClearLogs?      _ClearLogs;
        private static d_ExportLogs?     _ExportLogs;
        private static d_GetLastError?   _GetLastError;
        private static d_GetVersion?     _GetVersion;

        // WinAPI (Ofuscado para evitar heurística)
        [DllImport("kernel32.dll", EntryPoint = "LoadLibraryW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryNative(string lpLibFileName);

        [DllImport("kernel32.dll", EntryPoint = "GetProcAddress", SetLastError = true)]
        private static extern IntPtr GetProcAddressNative(IntPtr hModule, string procName);

        [DllImport("kernel32.dll", EntryPoint = "FreeLibrary", SetLastError = true)]
        private static extern bool FreeLibraryNative(IntPtr hModule);

        // Wrappers
        private static IntPtr LoadLibraryW(string path) => LoadLibraryNative(path);
        private static IntPtr GetProcAddress(IntPtr hMod, string name) => GetProcAddressNative(hMod, name);
        private static bool FreeLibrary(IntPtr hMod) => FreeLibraryNative(hMod);

        private static T? GetProc<T>(string name) where T : Delegate {
            App.LoggingService?.LogInfo($"[DriverEngineInterop] GetProc - resolving: {name}");
            var ptr = GetProcAddress(_hLib, name);
            if (ptr == IntPtr.Zero) {
                App.LoggingService?.LogWarning($"[DriverEngineInterop] GetProc - function not found: {name}");
                Debug.WriteLine($"[DriverEngineInterop] GetProc - function not found: {name}");
                return null;
            }
            App.LoggingService?.LogInfo($"[DriverEngineInterop] GetProc - resolved: {name} at 0x{ptr.ToInt64():X8}");
            Debug.WriteLine($"[DriverEngineInterop] GetProc - resolved: {name}");
            return Marshal.GetDelegateForFunctionPointer<T>(ptr);
        }

        // ── Load correct DLL for current process arch ─────────────────────
        public static bool Load(string dllDirectory) {
            if (_hLib != IntPtr.Zero) {
                Debug.WriteLine("[DriverEngineInterop] Load - already loaded, skipping");
                return true;
            }

            var sw = Stopwatch.StartNew();
            App.LoggingService?.LogInfo($"[DriverEngineInterop] Load - entry, dllDirectory={dllDirectory}");
            Debug.WriteLine($"[DriverEngineInterop] Load - entry, dllDirectory={dllDirectory}");

            string arch   = Environment.Is64BitProcess ? "x64" : "x86";
            string dllPath = Path.Combine(dllDirectory, $"driver_engine_{arch}.dll");

            App.LoggingService?.LogInfo($"[DriverEngineInterop] Load - target arch={arch}, path={dllPath}");
            Debug.WriteLine($"[DriverEngineInterop] Load - attempting to load {dllPath}");

            if (!File.Exists(dllPath)) {
                App.LoggingService?.LogError($"[DriverEngineInterop] Load - DLL not found: {dllPath}");
                Debug.WriteLine($"[DriverEngineInterop] DLL not found: {dllPath}");
                return false;
            }

            _hLib = LoadLibraryW(dllPath);
            if (_hLib == IntPtr.Zero) {
                int win32Err = Marshal.GetLastWin32Error();
                App.LoggingService?.LogError($"[DriverEngineInterop] Load - LoadLibraryW failed, Win32 error={win32Err}, path={dllPath}");
                Debug.WriteLine($"[DriverEngineInterop] LoadLibraryW failed: {dllPath}, error={win32Err}");
                return false;
            }
            _loadedPath = dllPath;
            App.LoggingService?.LogInfo($"[DriverEngineInterop] Load - LoadLibraryW OK, handle=0x{_hLib.ToInt64():X8}");
            Debug.WriteLine($"[DriverEngineInterop] LoadLibraryW succeeded: {dllPath}, handle=0x{_hLib.ToInt64():X8}");

            // Resolve all delegates
            _Initialize = GetProc<d_Initialize>("DE_Initialize");
            _Shutdown    = GetProc<d_Shutdown>("DE_Shutdown");
            _ScanDevices = GetProc<d_ScanDevices>("DE_ScanDevices");
            _VerifySig   = GetProc<d_VerifyDriverSignature>("DE_VerifyDriverSignature");
            _IsWHQL      = GetProc<d_IsDriverWHQL>("DE_IsDriverWHQL");
            _CompareVer  = GetProc<d_CompareVersions>("DE_CompareVersions");
            _ParseVer    = GetProc<d_ParseVersion>("DE_ParseVersion");
            _Install     = GetProc<d_InstallDriver>("DE_InstallDriver");
            _Rollback    = GetProc<d_RollbackDriver>("DE_RollbackDriver");
            _Validate    = GetProc<d_ValidatePostInstall>("DE_ValidatePostInstall");
            _IsAdmin     = GetProc<d_IsRunningAsAdmin>("DE_IsRunningAsAdmin");
            _Elevate     = GetProc<d_ElevateProcess>("DE_ElevateProcess");
            _GetLogCount = GetProc<d_GetLogCount>("DE_GetLogCount");
            _GetLogEntry = GetProc<d_GetLogEntry>("DE_GetLogEntry");
            _ClearLogs   = GetProc<d_ClearLogs>("DE_ClearLogs");
            _ExportLogs  = GetProc<d_ExportLogs>("DE_ExportLogs");
            _GetLastError = GetProc<d_GetLastError>("DE_GetLastError");
            _GetVersion  = GetProc<d_GetVersion>("DE_GetVersion");

            sw.Stop();
            App.LoggingService?.LogInfo($"[DriverEngineInterop] Load - success, DLL={dllPath}, elapsed={sw.ElapsedMilliseconds}ms");
            Debug.WriteLine($"[DriverEngineInterop] Loaded: {dllPath} in {sw.ElapsedMilliseconds}ms");
            return true;
        }

        public static void Unload() {
            App.LoggingService?.LogInfo("[DriverEngineInterop] Unload - entry");
            Debug.WriteLine("[DriverEngineInterop] Unload - entry");
            _Shutdown?.Invoke();
            if (_hLib != IntPtr.Zero) {
                FreeLibrary(_hLib);
                App.LoggingService?.LogInfo($"[DriverEngineInterop] Unload - FreeLibrary(0x{_hLib.ToInt64():X8})");
                Debug.WriteLine($"[DriverEngineInterop] FreeLibrary(0x{_hLib.ToInt64():X8})");
                _hLib = IntPtr.Zero;
                _loadedPath = string.Empty;
            }
            App.LoggingService?.LogInfo("[DriverEngineInterop] Unload - complete");
        }

        public static bool IsLoaded => _hLib != IntPtr.Zero;

        // ── Public safe wrappers ──────────────────────────────────────────
        public static int Initialize(string? logDirectory = null) {
            App.LoggingService?.LogInfo($"[DriverEngineInterop] Initialize - logDirectory={logDirectory}");
            Debug.WriteLine($"[DriverEngineInterop] Initialize - logDirectory={logDirectory}");
            var result = _Initialize?.Invoke(logDirectory ?? string.Empty) ?? -99;
            App.LoggingService?.LogInfo($"[DriverEngineInterop] Initialize - result={result}");
            Debug.WriteLine($"[DriverEngineInterop] Initialize - result={result}");
            return result;
        }

        public static string GetVersion() {
            var result = Marshal.PtrToStringUni(_GetVersion?.Invoke() ?? IntPtr.Zero) ?? "N/A";
            App.LoggingService?.LogInfo($"[DriverEngineInterop] GetVersion - {result}");
            Debug.WriteLine($"[DriverEngineInterop] GetVersion - {result}");
            return result;
        }

        public static bool IsRunningAsAdmin() {
            var result = _IsAdmin?.Invoke() ?? false;
            App.LoggingService?.LogInfo($"[DriverEngineInterop] IsRunningAsAdmin - {result}");
            Debug.WriteLine($"[DriverEngineInterop] IsRunningAsAdmin - {result}");
            return result;
        }

        public static int VerifyDriverSignature(string infPath, string? catalogPath = null) {
            App.LoggingService?.LogInfo($"[DriverEngineInterop] VerifyDriverSignature - infPath={infPath}, catalogPath={catalogPath}");
            Debug.WriteLine($"[DriverEngineInterop] VerifyDriverSignature - infPath={infPath}");
            var result = _VerifySig?.Invoke(infPath, catalogPath) ?? (int)DE_Sig.Error;
            App.LoggingService?.LogInfo($"[DriverEngineInterop] VerifyDriverSignature - result={result}");
            return result;
        }

        public static bool IsDriverWHQL(string infPath) {
            App.LoggingService?.LogInfo($"[DriverEngineInterop] IsDriverWHQL - infPath={infPath}");
            var result = _IsWHQL?.Invoke(infPath) ?? false;
            App.LoggingService?.LogInfo($"[DriverEngineInterop] IsDriverWHQL - {result}");
            return result;
        }

        public static int CompareVersions(string a, string b) {
            App.LoggingService?.LogInfo($"[DriverEngineInterop] CompareVersions - a={a}, b={b}");
            var result = _CompareVer?.Invoke(a, b) ?? -99;
            App.LoggingService?.LogInfo($"[DriverEngineInterop] CompareVersions - result={result}");
            return result;
        }

        public static bool ParseVersion(string versionStr, out DE_VERSION version) {
            version = default;
            App.LoggingService?.LogInfo($"[DriverEngineInterop] ParseVersion - versionStr={versionStr}");
            var result = _ParseVer?.Invoke(versionStr, out version) ?? false;
            App.LoggingService?.LogInfo($"[DriverEngineInterop] ParseVersion - result={result}, parsed={version}");
            return result;
        }

        public static bool RollbackDriver(string deviceInstanceId) {
            App.LoggingService?.LogInfo($"[DriverEngineInterop] RollbackDriver - device={deviceInstanceId}");
            Debug.WriteLine($"[DriverEngineInterop] RollbackDriver - device={deviceInstanceId}");
            var result = _Rollback?.Invoke(deviceInstanceId) ?? false;
            App.LoggingService?.LogInfo($"[DriverEngineInterop] RollbackDriver - result={result}");
            return result;
        }

        public static bool ValidatePostInstall(string deviceInstanceId) {
            App.LoggingService?.LogInfo($"[DriverEngineInterop] ValidatePostInstall - device={deviceInstanceId}");
            var result = _Validate?.Invoke(deviceInstanceId) ?? false;
            App.LoggingService?.LogInfo($"[DriverEngineInterop] ValidatePostInstall - result={result}");
            return result;
        }

        public static string GetNativeLastError() {
            var result = Marshal.PtrToStringUni(_GetLastError?.Invoke() ?? IntPtr.Zero) ?? string.Empty;
            App.LoggingService?.LogWarning($"[DriverEngineInterop] GetNativeLastError - {result}");
            return result;
        }

        public static int GetLogCount() {
            var result = _GetLogCount?.Invoke() ?? 0;
            Debug.WriteLine($"[DriverEngineInterop] GetLogCount - {result}");
            return result;
        }

        public static DriverEngineLogEntry? GetLogEntry(int index) {
            Debug.WriteLine($"[DriverEngineInterop] GetLogEntry - index={index}");
            if (_GetLogEntry == null) {
                Debug.WriteLine("[DriverEngineInterop] GetLogEntry - delegate is null");
                return null;
            }
            if (!_GetLogEntry(index, out var native)) {
                Debug.WriteLine($"[DriverEngineInterop] GetLogEntry - native returned false for index={index}");
                return null;
            }
            var entry = DriverEngineLogEntry.FromNative(native);
            return entry;
        }

        public static void ClearLogs() {
            App.LoggingService?.LogInfo("[DriverEngineInterop] ClearLogs");
            Debug.WriteLine("[DriverEngineInterop] ClearLogs");
            _ClearLogs?.Invoke();
        }

        public static bool ExportLogs(string filePath) {
            App.LoggingService?.LogInfo($"[DriverEngineInterop] ExportLogs - filePath={filePath}");
            var result = _ExportLogs?.Invoke(filePath) ?? false;
            App.LoggingService?.LogInfo($"[DriverEngineInterop] ExportLogs - result={result}");
            return result;
        }

        // ── ScanDevices with managed callback ────────────────────────────
        public static List<NativeDriverDeviceInfo> ScanDevices(
            Action<int, int, int, string>? progressCallback = null) {

            if (_ScanDevices == null) {
                App.LoggingService?.LogWarning("[DriverEngineInterop] ScanDevices - delegate is null");
                Debug.WriteLine("[DriverEngineInterop] ScanDevices - delegate is null");
                return new();
            }

            var sw = Stopwatch.StartNew();
            App.LoggingService?.LogInfo("[DriverEngineInterop] ScanDevices - starting native scan");
            Debug.WriteLine("[DriverEngineInterop] ScanDevices - entry");

            // Allocate unmanaged buffer for DE_SCAN_RESULT
            int structSize = Marshal.SizeOf<DE_SCAN_RESULT_NATIVE>();
            IntPtr pResult = Marshal.AllocHGlobal(structSize);
            try {
                // Zero-fill
                for (int k = 0; k < structSize; k++)
                    Marshal.WriteByte(pResult, k, 0);

                IntPtr callbackPtr = IntPtr.Zero;
                DE_ProgressCallback? managedCb = null;
                if (progressCallback != null) {
                    managedCb = (phase, cur, tot, name) =>
                        progressCallback(phase, cur, tot, name ?? string.Empty);
                    callbackPtr = Marshal.GetFunctionPointerForDelegate(managedCb);
                }

                int count = _ScanDevices(pResult, callbackPtr);
                App.LoggingService?.LogInfo($"[DriverEngineInterop] ScanDevices - native returned count={count}");
                Debug.WriteLine($"[DriverEngineInterop] ScanDevices - native returned count={count}");

                if (count <= 0) {
                    sw.Stop();
                    App.LoggingService?.LogInfo($"[DriverEngineInterop] ScanDevices - no devices found, elapsed={sw.ElapsedMilliseconds}ms");
                    return new();
                }

                var result = Marshal.PtrToStructure<DE_SCAN_RESULT_NATIVE>(pResult);
                var list   = new List<NativeDriverDeviceInfo>(count);

                for (int i = 0; i < count && i < (result.Devices?.Length ?? 0); i++) {
                    list.Add(NativeDriverDeviceInfo.FromNative(result.Devices![i]));
                }

                // Keep delegate alive during P/Invoke
                GC.KeepAlive(managedCb);

                sw.Stop();
                App.LoggingService?.LogInfo($"[DriverEngineInterop] ScanDevices - complete, devices={list.Count}, elapsed={sw.ElapsedMilliseconds}ms");
                Debug.WriteLine($"[DriverEngineInterop] ScanDevices - complete, devices={list.Count}, elapsed={sw.ElapsedMilliseconds}ms");
                return list;
            }
            finally { Marshal.FreeHGlobal(pResult); }
        }

        // ── InstallDriver with managed options + callback ─────────────────
        public static int InstallDriver(
            string deviceInstanceId,
            string infPath,
            bool   createRestorePoint = true,
            bool   backupExisting     = true,
            bool   forceInstall       = false,
            bool   allowUnsigned      = false,
            string? backupDirectory   = null,
            Action<int, int, int, string>? progressCallback = null)
        {
            if (_Install == null) {
                App.LoggingService?.LogError("[DriverEngineInterop] InstallDriver - delegate is null");
                Debug.WriteLine("[DriverEngineInterop] InstallDriver - delegate is null");
                return DE_InstallResult.Fail;
            }

            var sw = Stopwatch.StartNew();
            App.LoggingService?.LogInfo($"[DriverEngineInterop] InstallDriver - device={deviceInstanceId}, inf={infPath}, force={forceInstall}, restorePoint={createRestorePoint}, backup={backupExisting}, unsigned={allowUnsigned}");
            Debug.WriteLine($"[DriverEngineInterop] InstallDriver - device={deviceInstanceId}, inf={infPath}");

            var opts = new DE_INSTALL_OPTIONS_NATIVE {
                ForceInstall       = forceInstall,
                CreateRestorePoint = createRestorePoint,
                BackupExisting     = backupExisting,
                AllowUnsigned      = allowUnsigned,
                RankOnly           = false,
                BackupDirectory    = backupDirectory ?? string.Empty
            };

            IntPtr pOpts = Marshal.AllocHGlobal(Marshal.SizeOf(opts));
            try {
                Marshal.StructureToPtr(opts, pOpts, false);

                IntPtr callbackPtr = IntPtr.Zero;
                DE_ProgressCallback? managedCb = null;
                if (progressCallback != null) {
                    managedCb  = (phase, cur, tot, name) =>
                        progressCallback(phase, cur, tot, name ?? string.Empty);
                    callbackPtr = Marshal.GetFunctionPointerForDelegate(managedCb);
                }

                int r = _Install(deviceInstanceId, infPath, pOpts, callbackPtr);
                GC.KeepAlive(managedCb);

                sw.Stop();
                App.LoggingService?.LogInfo($"[DriverEngineInterop] InstallDriver - result={r}, elapsed={sw.ElapsedMilliseconds}ms");
                Debug.WriteLine($"[DriverEngineInterop] InstallDriver - result={r}, elapsed={sw.ElapsedMilliseconds}ms");
                return r;
            }
            finally {
                Marshal.DestroyStructure<DE_INSTALL_OPTIONS_NATIVE>(pOpts);
                Marshal.FreeHGlobal(pOpts);
            }
        }

        public static List<DriverEngineLogEntry> GetAllLogs() {
            Debug.WriteLine("[DriverEngineInterop] GetAllLogs - entry");
            int count = GetLogCount();
            App.LoggingService?.LogInfo($"[DriverEngineInterop] GetAllLogs - count={count}");
            var result = new List<DriverEngineLogEntry>(count);
            for (int i = 0; i < count; i++) {
                var e = GetLogEntry(i);
                if (e != null) result.Add(e);
            }
            App.LoggingService?.LogInfo($"[DriverEngineInterop] GetAllLogs - retrieved {result.Count} entries");
            return result;
        }
    }

    // =========================================================================
    //  NativeDriverEngineService  —  High-level service used by WPF ViewModels
    //  Replaces SetupApiEnumerator and UniversalDriverDetectionService
    // =========================================================================
    public class NativeDriverEngineService : IDisposable
    {
        private bool _disposed;
        private readonly string _dllDir;
        private readonly string _logDir;
        private readonly ConcurrentQueue<DriverEngineLogEntry> _liveLogQueue = new();

        /// <summary>Fires on every new native log entry (marshalled to UI thread externally).</summary>
        public event Action<DriverEngineLogEntry>? LogReceived;

        public bool IsAvailable { get; private set; }
        public string EngineVersion { get; private set; } = "N/A";

        public NativeDriverEngineService(string? dllDirectory = null, string? logDirectory = null) {
            var sw = Stopwatch.StartNew();
            App.LoggingService?.LogInfo("[NativeDriverEngineService] Constructor - entry");
            Debug.WriteLine("[NativeDriverEngineService] Constructor - entry");

            _dllDir = dllDirectory
                ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DriverEngine");
            _logDir = logDirectory
                ?? Path.Combine(Path.GetTempPath(), "VoltrisDriverEngine");

            App.LoggingService?.LogInfo($"[NativeDriverEngineService] Constructor - dllDir={_dllDir}, logDir={_logDir}");

            IsAvailable = DriverEngineInterop.Load(_dllDir);
            if (!IsAvailable) {
                App.LoggingService?.LogWarning(
                    "[NativeDriverEngineService] DLL not found — falling back to managed stack.");
                Debug.WriteLine("[NativeDriverEngineService] DLL load failed, falling back to managed stack");
                return;
            }

            DriverEngineInterop.Initialize(_logDir);
            EngineVersion = DriverEngineInterop.GetVersion();
            sw.Stop();
            App.LoggingService?.LogInfo(
                $"[NativeDriverEngineService] Engine v{EngineVersion} loaded from {_dllDir}, elapsed={sw.ElapsedMilliseconds}ms");
            Debug.WriteLine($"[NativeDriverEngineService] Engine v{EngineVersion} loaded, elapsed={sw.ElapsedMilliseconds}ms");
        }

        // ── Full SetupAPI device enumeration ─────────────────────────────
        public async Task<List<NativeDriverDeviceInfo>> EnumerateDevicesAsync(
            IProgress<(int Phase, int Current, int Total, string Device)>? progress = null,
            CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            App.LoggingService?.LogInfo("[NativeDriverEngineService] EnumerateDevicesAsync - starting SetupAPI device enumeration...");
            Debug.WriteLine("[NativeDriverEngineService] EnumerateDevicesAsync - entry");

            if (!IsAvailable) {
                App.LoggingService?.LogWarning("[NativeDriverEngineService] EnumerateDevicesAsync - engine not available, returning empty");
                sw.Stop();
                return new();
            }

            var result = await Task.Run(() => {
                return DriverEngineInterop.ScanDevices((phase, cur, tot, name) => {
                    progress?.Report((phase, cur, tot, name));
                    FlushNativeLogs();
                });
            }, ct);

            sw.Stop();
            App.LoggingService?.LogInfo(
                $"[NativeDriverEngineService] Enumeration complete: {result.Count} devices, elapsed={sw.ElapsedMilliseconds}ms");
            Debug.WriteLine($"[NativeDriverEngineService] Enumeration complete: {result.Count} devices, elapsed={sw.ElapsedMilliseconds}ms");
            FlushNativeLogs();
            return result;
        }

        // ── Version comparison (C++ numeric, not string) ──────────────────
        public int CompareVersions(string a, string b) {
            App.LoggingService?.LogInfo($"[NativeDriverEngineService] CompareVersions - a={a}, b={b}");
            Debug.WriteLine($"[NativeDriverEngineService] CompareVersions - a={a}, b={b}");

            if (!IsAvailable) {
                // Fallback: managed comparison
                if (Version.TryParse(a, out var va) && Version.TryParse(b, out var vb)) {
                    var result = va.CompareTo(vb);
                    App.LoggingService?.LogInfo($"[NativeDriverEngineService] CompareVersions - managed fallback result={result}");
                    return result;
                }
                var fallbackResult = StringComparer.Ordinal.Compare(a, b);
                App.LoggingService?.LogInfo($"[NativeDriverEngineService] CompareVersions - ordinal fallback result={fallbackResult}");
                return fallbackResult;
            }
            var nativeResult = DriverEngineInterop.CompareVersions(a, b);
            App.LoggingService?.LogInfo($"[NativeDriverEngineService] CompareVersions - native result={nativeResult}");
            return nativeResult;
        }

        // ── Signature verification ────────────────────────────────────────
        public int VerifySignature(string infPath) {
            App.LoggingService?.LogInfo($"[NativeDriverEngineService] VerifySignature - infPath={infPath}");
            Debug.WriteLine($"[NativeDriverEngineService] VerifySignature - infPath={infPath}");
            if (!IsAvailable) {
                App.LoggingService?.LogWarning("[NativeDriverEngineService] VerifySignature - engine not available, returning Error");
                return (int)DE_Sig.Error;
            }
            var result = DriverEngineInterop.VerifyDriverSignature(infPath);
            App.LoggingService?.LogInfo($"[NativeDriverEngineService] VerifySignature - result={result}");
            return result;
        }

        // ── Professional installation with all safety checks ──────────────
        public async Task<int> InstallDriverAsync(
            string deviceInstanceId,
            string infPath,
            InstallOptions? opts = null,
            IProgress<(int Phase, int Current, int Total, string Status)>? progress = null,
            CancellationToken ct = default)
        {
            if (!IsAvailable) {
                App.LoggingService?.LogError("[NativeDriverEngineService] InstallDriverAsync - engine not available");
                Debug.WriteLine("[NativeDriverEngineService] InstallDriverAsync - engine not available");
                return DE_InstallResult.Fail;
            }

            var sw = Stopwatch.StartNew();
            var o = opts ?? new InstallOptions();
            App.LoggingService?.LogInfo(
                $"[NativeDriverEngineService] InstallDriverAsync - device={deviceInstanceId}, inf={infPath}, force={o.ForceInstall}, restorePoint={o.CreateRestorePoint}");
            Debug.WriteLine($"[NativeDriverEngineService] InstallDriverAsync - device={deviceInstanceId}, inf={infPath}");

            int result = await Task.Run(() => {
                return DriverEngineInterop.InstallDriver(
                    deviceInstanceId, infPath,
                    o.CreateRestorePoint, o.BackupExisting,
                    o.ForceInstall, o.AllowUnsigned,
                    o.BackupDirectory,
                    (phase, cur, tot, name) => {
                        progress?.Report((phase, cur, tot, name));
                        FlushNativeLogs();
                    });
            }, ct);

            sw.Stop();
            string resultLabel = result switch {
                DE_InstallResult.OK       => "SUCCESS",
                DE_InstallResult.Reboot   => "REBOOT NEEDED",
                DE_InstallResult.Fail     => "FAIL",
                _ => $"CODE {result}"
            };

            FlushNativeLogs();
            App.LoggingService?.LogInfo(
                $"[NativeDriverEngineService] InstallDriverAsync - result: {result} ({resultLabel}), elapsed={sw.ElapsedMilliseconds}ms");
            Debug.WriteLine($"[NativeDriverEngineService] InstallDriverAsync - result: {result} ({resultLabel}), elapsed={sw.ElapsedMilliseconds}ms");
            return result;
        }

        // ── Export full native log ────────────────────────────────────────
        public List<DriverEngineLogEntry> GetAllLogs() {
            Debug.WriteLine("[NativeDriverEngineService] GetAllLogs - entry");
            if (!IsAvailable) {
                App.LoggingService?.LogWarning("[NativeDriverEngineService] GetAllLogs - engine not available");
                return new();
            }
            var result = DriverEngineInterop.GetAllLogs();
            App.LoggingService?.LogInfo($"[NativeDriverEngineService] GetAllLogs - {result.Count} entries");
            return result;
        }

        public bool ExportLogs(string filePath) {
            App.LoggingService?.LogInfo($"[NativeDriverEngineService] ExportLogs - filePath={filePath}");
            Debug.WriteLine($"[NativeDriverEngineService] ExportLogs - filePath={filePath}");
            if (!IsAvailable) {
                App.LoggingService?.LogWarning("[NativeDriverEngineService] ExportLogs - engine not available");
                return false;
            }
            var result = DriverEngineInterop.ExportLogs(filePath);
            App.LoggingService?.LogInfo($"[NativeDriverEngineService] ExportLogs - result={result}");
            return result;
        }

        // ── Pull new native logs and fire events ──────────────────────────
        private int _lastLogIndex;
        private void FlushNativeLogs() {
            if (!IsAvailable) return;
            int total = DriverEngineInterop.GetLogCount();
            int flushed = 0;
            while (_lastLogIndex < total) {
                var entry = DriverEngineInterop.GetLogEntry(_lastLogIndex++);
                if (entry == null) break;
                _liveLogQueue.Enqueue(entry);
                LogReceived?.Invoke(entry);
                App.LoggingService?.LogInfo($"[NativeDE][{entry.LevelLabel}] {entry.Message}");
                flushed++;
            }
            if (flushed > 0) {
                Debug.WriteLine($"[NativeDriverEngineService] FlushNativeLogs - flushed {flushed} entries");
            }
        }

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;
            App.LoggingService?.LogInfo("[NativeDriverEngineService] Dispose - entry");
            Debug.WriteLine("[NativeDriverEngineService] Dispose - entry");
            DriverEngineInterop.Unload();
            App.LoggingService?.LogInfo("[NativeDriverEngineService] Dispose - complete");
        }
    }

    // =========================================================================
    //  InstallOptions (strongly-typed options for managed callers)
    // =========================================================================
    public class InstallOptions
    {
        public bool    ForceInstall       { get; set; } = false;
        public bool    CreateRestorePoint { get; set; } = true;
        public bool    BackupExisting     { get; set; } = true;
        public bool    AllowUnsigned      { get; set; } = false;
        public string? BackupDirectory    { get; set; }
    }
}
