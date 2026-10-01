using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Utils;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services
{
    public class GodModeService
    {
        private readonly ILoggingService _logger;
        private Thread? _monitorThread;
        private CancellationTokenSource? _monitorCts;
        private readonly string _backupDir;
        private readonly string _sysProfileBackup;
        private readonly string _gameDvrBackup;
        private SafePerformanceCounter? _standbyNormal;
        private SafePerformanceCounter? _standbyReserve;
        private SafePerformanceCounter? _standbyCore;

        [DllImport("ntdll.dll")]
        private static extern int NtSetSystemInformation(int SystemInformationClass, ref int SystemInformation, int SystemInformationLength);

        [DllImport("kernel32.dll")]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        private enum MemoryListCommand
        {
            MemoryPurgeStandbyList = 4,
            MemoryPurgeLowPriorityStandbyList = 5
        }

        private const int SystemMemoryListInformation = 80;

        public GodModeService(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _backupDir = AppDataPaths.Backups;
            _sysProfileBackup = AppDataPaths.GodModeSysProfileBackup;
            _gameDvrBackup = AppDataPaths.GodModeGameDvrBackup;
            AppDataPaths.EnsureDirectory(_backupDir);
            InitCounters();
        }

        public void MonitorAndCleanRam()
        {
            try
            {
                _logger.LogInfo("Limpando RAM (One-shot, sem monitoramento contínuo)");
                Task.Run(async () =>
                {
                    try
                    {
                        await global::VoltrisOptimizer.Services.Optimization.Memory.VoltrisMemoryCoordinator.Instance.OptimizeMemoryAsync(isManualClick: false)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("Falha na limpeza inicial de RAM", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro ao iniciar limpeza de RAM", ex);
            }
        }

        public void StopMonitor()
        {
            try
            {
                _monitorCts?.Cancel();
                _monitorThread = null;
                _logger.LogInfo("Monitor de RAM parado");
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro ao parar monitor de RAM", ex);
            }
        }

        public bool CleanStandbyList()
        {
            try
            {
                _logger.LogInfo("[GodModeService] Solicitando CleanStandbyList via Coordinator...");
                global::VoltrisOptimizer.Services.Optimization.Memory.VoltrisMemoryCoordinator.Instance.CleanStandbyList();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro ao limpar Standby List", ex);
                return false;
            }
        }

        public bool OptimizeSystemProfile()
        {
            try
            {
                if (!AdminHelper.IsRunningAsAdministrator())
                {
                    _logger.LogWarning("Permissões insuficientes para HKLM");
                    return false;
                }

                BackupSystemProfile();

                using var gamesKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", true);
                if (gamesKey == null)
                {
                    _logger.LogWarning("Chave de SystemProfile/Tasks/Games não encontrada");
                    return false;
                }

                try { gamesKey.SetValue("GPU Priority", 8, RegistryValueKind.DWord); } catch (Exception ex) { _logger.LogError("Falha em GPU Priority", ex); }
                try { gamesKey.SetValue("Priority", 6, RegistryValueKind.DWord); } catch (Exception ex) { _logger.LogError("Falha em Priority", ex); }
                try { gamesKey.SetValue("Scheduling Category", "High", RegistryValueKind.String); } catch (Exception ex) { _logger.LogError("Falha em Scheduling Category", ex); }
                try { gamesKey.SetValue("SFIO Priority", "High", RegistryValueKind.String); } catch (Exception ex) { _logger.LogError("Falha em SFIO Priority", ex); }

                _logger.LogSuccess("SystemProfile otimizado para jogos");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro ao otimizar SystemProfile", ex);
                return false;
            }
        }

        public bool RestoreSystemProfile()
        {
            try
            {
                if (!AdminHelper.IsRunningAsAdministrator())
                {
                    _logger.LogWarning("Permissões insuficientes para HKLM");
                    return false;
                }

                if (!File.Exists(_sysProfileBackup))
                {
                    _logger.LogWarning("Backup de SystemProfile não encontrado");
                    return false;
                }

                var json = File.ReadAllText(_sysProfileBackup);
                var b = JsonSerializer.Deserialize<SystemProfileBackup>(json) ?? new SystemProfileBackup();
                using var gamesKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", true);
                if (gamesKey == null) return false;

                try { SetOrDelete(gamesKey, "GPU Priority", b.GpuPriority); } catch (Exception ex) { _logger.LogError("Falha ao restaurar GPU Priority", ex); }
                try { SetOrDelete(gamesKey, "Priority", b.Priority); } catch (Exception ex) { _logger.LogError("Falha ao restaurar Priority", ex); }
                try { SetOrDelete(gamesKey, "Scheduling Category", b.SchedulingCategory); } catch (Exception ex) { _logger.LogError("Falha ao restaurar Scheduling Category", ex); }
                try { SetOrDelete(gamesKey, "SFIO Priority", b.SfioPriority); } catch (Exception ex) { _logger.LogError("Falha ao restaurar SFIO Priority", ex); }

                _logger.LogSuccess("SystemProfile restaurado");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro ao restaurar SystemProfile", ex);
                return false;
            }
        }

        public bool DisableGameBloat()
        {
            try
            {
                BackupGameDvr();

                using var gameDvrKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR", true) ?? Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR");
                using var gameConfigKey = Registry.CurrentUser.OpenSubKey(@"System\GameConfigStore", true) ?? Registry.CurrentUser.CreateSubKey(@"System\GameConfigStore");

                try { gameDvrKey?.SetValue("AppCaptureEnabled", 0, RegistryValueKind.DWord); } catch (Exception ex) { _logger.LogError("Falha em AppCaptureEnabled", ex); }
                try { gameConfigKey?.SetValue("GameDVR_FSEBehavior", 2, RegistryValueKind.DWord); } catch (Exception ex) { _logger.LogError("Falha em FSEBehavior", ex); }
                try { gameConfigKey?.SetValue("GameDVR_FSEBehaviorMode", 2, RegistryValueKind.DWord); } catch (Exception ex) { _logger.LogError("Falha em FSEBehaviorMode", ex); }
                try { gameConfigKey?.SetValue("GameDVR_Enabled", 0, RegistryValueKind.DWord); } catch (Exception ex) { _logger.LogError("Falha em GameDVR_Enabled", ex); }

                _logger.LogSuccess("GameDVR e FSO desativados");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro ao desativar GameDVR/FSO", ex);
                return false;
            }
        }

        public bool RestoreGameBloat()
        {
            try
            {
                if (!File.Exists(_gameDvrBackup))
                {
                    _logger.LogWarning("Backup de GameDVR não encontrado");
                    return false;
                }
                var json = File.ReadAllText(_gameDvrBackup);
                var b = JsonSerializer.Deserialize<GameDvrBackup>(json) ?? new GameDvrBackup();

                using var gameDvrKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR", true) ?? Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR");
                using var gameConfigKey = Registry.CurrentUser.OpenSubKey(@"System\GameConfigStore", true) ?? Registry.CurrentUser.CreateSubKey(@"System\GameConfigStore");

                try { SetOrDelete(gameDvrKey, "AppCaptureEnabled", b.AppCaptureEnabled); } catch (Exception ex) { _logger.LogError("Falha ao restaurar AppCaptureEnabled", ex); }
                try { SetOrDelete(gameConfigKey, "GameDVR_FSEBehavior", b.FseBehavior); } catch (Exception ex) { _logger.LogError("Falha ao restaurar FSEBehavior", ex); }
                try { SetOrDelete(gameConfigKey, "GameDVR_FSEBehaviorMode", b.FseBehaviorMode); } catch (Exception ex) { _logger.LogError("Falha ao restaurar FSEBehaviorMode", ex); }
                try { SetOrDelete(gameConfigKey, "GameDVR_Enabled", b.GameDvrEnabled); } catch (Exception ex) { _logger.LogError("Falha ao restaurar GameDVR_Enabled", ex); }

                _logger.LogSuccess("GameDVR/FSO restaurados");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro ao restaurar GameDVR/FSO", ex);
                return false;
            }
        }

        private void InitCounters()
        {
            try
            {
                _standbyNormal = new SafePerformanceCounter("Memory", "Standby Cache Normal Priority Bytes", readOnly: true);
                _standbyReserve = new SafePerformanceCounter("Memory", "Standby Cache Reserve Bytes", readOnly: true);
                _standbyCore = new SafePerformanceCounter("Memory", "Standby Cache Core Bytes", readOnly: true);
            }
            catch { }
        }

        private double GetFreeMemoryMB()
        {
            try
            {
                var ms = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
                if (GlobalMemoryStatusEx(ref ms)) return ms.ullAvailPhys / 1024.0 / 1024.0;
            }
            catch { }
            return 0;
        }

        private double GetStandbyCacheMB()
        {
            try
            {
                var n = _standbyNormal?.NextValue() ?? 0;
                var r = _standbyReserve?.NextValue() ?? 0;
                var c = _standbyCore?.NextValue() ?? 0;
                return (n + r + c) / 1024.0 / 1024.0;
            }
            catch { return 0; }
        }

        private void BackupSystemProfile()
        {
            try
            {
                var backup = new SystemProfileBackup();
                using var gamesKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", false);
                if (gamesKey != null)
                {
                    var v1 = gamesKey.GetValue("GPU Priority");
                    backup.GpuPriority = v1 is int i1 ? i1 : (int?)null;
                    var v2 = gamesKey.GetValue("Priority");
                    backup.Priority = v2 is int i2 ? i2 : (int?)null;
                    backup.SchedulingCategory = gamesKey.GetValue("Scheduling Category") as string;
                    backup.SfioPriority = gamesKey.GetValue("SFIO Priority") as string;
                }
                var json = JsonSerializer.Serialize(backup, new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,  WriteIndented = true });
                File.WriteAllText(_sysProfileBackup, json);
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro ao criar backup de SystemProfile", ex);
            }
        }

        private void BackupGameDvr()
        {
            try
            {
                var backup = new GameDvrBackup();
                using var gameDvrKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR", false);
                using var gameConfigKey = Registry.CurrentUser.OpenSubKey(@"System\GameConfigStore", false);
                if (gameDvrKey != null)
                {
                    var v = gameDvrKey.GetValue("AppCaptureEnabled");
                    backup.AppCaptureEnabled = v is int i ? i : (int?)null;
                }
                if (gameConfigKey != null)
                {
                    var v1 = gameConfigKey.GetValue("GameDVR_FSEBehavior");
                    backup.FseBehavior = v1 is int i1 ? i1 : (int?)null;
                    var v2 = gameConfigKey.GetValue("GameDVR_FSEBehaviorMode");
                    backup.FseBehaviorMode = v2 is int i2 ? i2 : (int?)null;
                    var v3 = gameConfigKey.GetValue("GameDVR_Enabled");
                    backup.GameDvrEnabled = v3 is int i3 ? i3 : (int?)null;
                }
                var json = JsonSerializer.Serialize(backup, new JsonSerializerOptions {   WriteIndented = true });
                File.WriteAllText(_gameDvrBackup, json);
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro ao criar backup de GameDVR", ex);
            }
        }

        private static void SetOrDelete(RegistryKey? key, string name, int? value)
        {
            if (key == null) return;
            if (value.HasValue) key.SetValue(name, value.Value, RegistryValueKind.DWord);
            else key.DeleteValue(name, false);
        }

        private static void SetOrDelete(RegistryKey? key, string name, string? value)
        {
            if (key == null) return;
            if (value != null) key.SetValue(name, value, RegistryValueKind.String);
            else key.DeleteValue(name, false);
        }

        private class SystemProfileBackup
        {
            public int? GpuPriority { get; set; }
            public int? Priority { get; set; }
            public string? SchedulingCategory { get; set; }
            public string? SfioPriority { get; set; }
        }

        private class GameDvrBackup
        {
            public int? AppCaptureEnabled { get; set; }
            public int? FseBehavior { get; set; }
            public int? FseBehaviorMode { get; set; }
            public int? GameDvrEnabled { get; set; }
        }
    }
}


