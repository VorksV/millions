using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces; // Adicionando o namespace correto

namespace VoltrisOptimizer.Core.SystemIntelligenceProfiler
{
    public class RollbackManager : IRollbackManager
    {
        private readonly string _dir;
        private readonly List<RollbackAction> _stack = new List<RollbackAction>();
        public RollbackManager()
        {
            var appdata = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _dir = Path.Combine(appdata, "VoltrisOptimizer", "Backups");
            if (!Directory.Exists(_dir)) Directory.CreateDirectory(_dir);
            App.LoggingService?.LogTrace($"[ROLLBACK] Gerenciador inicializado. Diretório de backups: {_dir}");
        }

        public string BeginSession()
        {
            var id = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var path = Path.Combine(_dir, id);
            Directory.CreateDirectory(path);
            // Grava o snapshot AGORA, antes de qualquer alteração, para que o
            // rollback exista de fato. Antes esta pasta ficava vazia.
            PersistSnapshot(path);
            App.LoggingService?.LogInfo($"[ROLLBACK] Nova sessão de backup iniciada: {id}");
            return path;
        }

        public void SaveText(string sessionDir, string name, string content)
        {
            var p = Path.Combine(sessionDir, name);
            File.WriteAllText(p, content);
        }

        public List<string> ListBackups()
        {
            var list = new List<string>();
            foreach (var d in Directory.GetDirectories(_dir)) list.Add(d);
            return list;
        }

        public void PushAction(string description, string type, string backupPath)
        {
            _stack.Add(new RollbackAction
            {
                Description = description,
                Type = type,
                BackupLocation = backupPath,
                Timestamp = DateTime.Now
            });
        }

        public IReadOnlyList<RollbackAction> GetStack() => _stack.AsReadOnly();

        public void Push(RollbackAction action)
        {
            if (action == null) return;
            _stack.Add(action);
        }

        // ==================================================================
        // SNAPSHOT REAL — o rollback que antes só fingia existir
        // ==================================================================

        public sealed class RegValueSnapshot
        {
            public string Hive { get; set; } = "";
            public string Path { get; set; } = "";
            public string Name { get; set; } = "";
            public object? Value { get; set; }
            public string Kind { get; set; } = "DWord";
            public bool Existed { get; set; }
        }

        public sealed class ServiceSnapshot
        {
            public string Name { get; set; } = "";
            public string Mode { get; set; } = "";
            public bool Existed { get; set; }
        }

        public sealed class SafetySnapshot
        {
            public DateTime CapturedAt { get; set; } = DateTime.Now;
            public bool HibernationEnabled { get; set; }
            public List<RegValueSnapshot> Registry { get; set; } = new List<RegValueSnapshot>();
            public List<ServiceSnapshot> Services { get; set; } = new List<ServiceSnapshot>();
        }

        /// <summary>(Hive, Caminho, Valor) — tudo que as otimizações tocam.</summary>
        private static readonly (string Hive, string Path, string Name)[] TrackedRegistryValues =
        {
            ("HKLM", @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "TdrLevel"),
            ("HKLM", @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "TdrDelay"),
            ("HKLM", @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "TdrDdiDelay"),
            ("HKLM", @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode"),
            ("HKLM", @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsDisableLastAccessUpdate"),
            ("HKLM", @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters", "EnablePrefetcher"),
            ("HKLM", @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "DisablePagingExecutive"),
            ("HKLM", @"SYSTEM\CurrentControlSet\Control\WMI\Autologger\ReadyBoot", "Start"),
            ("HKLM", @"SYSTEM\CurrentControlSet\Services\SysMain", "Start"),
            ("HKLM", @"SYSTEM\CurrentControlSet\Services\WSearch", "Start"),
            ("HKLM", @"SOFTWARE\Microsoft\Dfrg\BootOptimizeFunction", "Enable"),
            ("HKLM", @"SOFTWARE\Policies\Microsoft\System\Filesystem", "Disable8dot3NameCreation"),
            ("HKLM", @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\Multimedia\SystemProfile", "NetworkThrottlingIndex"),
            ("HKLM", @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\Multimedia\SystemProfile", "SystemResponsiveness"),
            ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting"),
            ("HKCU", @"Control Panel\Desktop\WindowMetrics", "MinAnimate"),
        };

        private static readonly string[] TrackedServices = { "SysMain", "WSearch", "Spooler", "Themes" };

        private const string SnapshotFileName = "safety_snapshot.json";

        /// <summary>
        /// Grava o estado ORIGINAL de registro, serviços e hibernação.
        /// É o que permite desfazer de verdade.
        /// </summary>
        public SafetySnapshot CaptureSnapshot()
        {
            var snap = new SafetySnapshot();

            foreach (var t in TrackedRegistryValues)
            {
                try
                {
                    using var key = OpenKey(t.Hive, t.Path, false);
                    if (key == null)
                    {
                        snap.Registry.Add(new RegValueSnapshot { Hive = t.Hive, Path = t.Path, Name = t.Name, Existed = false });
                        continue;
                    }
                    var v = key.GetValue(t.Name, null);
                    snap.Registry.Add(new RegValueSnapshot
                    {
                        Hive = t.Hive,
                        Path = t.Path,
                        Name = t.Name,
                        Value = v,
                        Kind = v == null ? "DWord" : key.GetValueKind(t.Name).ToString(),
                        Existed = v != null
                    });
                }
                catch
                {
                    snap.Registry.Add(new RegValueSnapshot { Hive = t.Hive, Path = t.Path, Name = t.Name, Existed = false });
                }
            }

            foreach (var s in TrackedServices)
            {
                var mode = GetServiceStartMode(s);
                snap.Services.Add(new ServiceSnapshot { Name = s, Mode = mode ?? "", Existed = mode != null });
            }

            snap.HibernationEnabled = IsHibernationEnabled();

            App.LoggingService?.LogInfo($"[ROLLBACK] Snapshot capturado: {snap.Registry.Count(r => r.Existed)} valor(es) de registro, {snap.Services.Count(s => s.Existed)} serviço(s).");
            return snap;
        }

        /// <summary>Grava o snapshot em disco para sobreviver ao fechamento do app.</summary>
        public void PersistSnapshot(string sessionDir, SafetySnapshot? snapshot = null)
        {
            try
            {
                if (string.IsNullOrEmpty(sessionDir)) return;
                Directory.CreateDirectory(sessionDir);
                var snap = snapshot ?? CaptureSnapshot();
                var json = System.Text.Json.JsonSerializer.Serialize(snap,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(Path.Combine(sessionDir, SnapshotFileName), json);
                App.LoggingService?.LogInfo($"[ROLLBACK] Snapshot persistido em {sessionDir}");
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning("[ROLLBACK] Falha ao persistir snapshot: " + ex.Message);
            }
        }

        /// <summary>
        /// Restaura o estado original a partir de um snapshot. Este é o
        /// rollback de verdade: devolve registro, modo de inicialização de
        /// serviço e hibernação ao estado anterior.
        /// </summary>
        public int RestoreFromSnapshot(SafetySnapshot snap)
        {
            if (snap == null) return 0;
            int restored = 0;

            foreach (var s in snap.Registry)
            {
                try
                {
                    using var key = OpenKey(s.Hive, s.Path, true);
                    if (key == null) continue;

                    if (s.Existed && s.Value != null)
                    {
                        var kind = Enum.TryParse<Microsoft.Win32.RegistryValueKind>(s.Kind, out var k)
                            ? k : Microsoft.Win32.RegistryValueKind.DWord;
                        key.SetValue(s.Name, s.Value, kind);
                        restored++;
                    }
                    else if (!s.Existed)
                    {
                        // A otimização criou este valor: remover devolve o original.
                        try { key.DeleteValue(s.Name, false); restored++; } catch { }
                    }
                }
                catch { }
            }

            foreach (var s in snap.Services)
            {
                if (!s.Existed) continue;
                try
                {
                    var current = GetServiceStartMode(s.Name);
                    if (!string.IsNullOrEmpty(s.Mode) && current != s.Mode)
                    {
                        if (SetServiceStartMode(s.Name, s.Mode)) restored++;
                    }
                }
                catch { }
            }

            try
            {
                if (snap.HibernationEnabled && !IsHibernationEnabled())
                {
                    if (RunCapture("powercfg /hibernate on").Trim().Length > 0) restored++;
                }
            }
            catch { }

            App.LoggingService?.LogInfo($"[ROLLBACK] Restauração concluída: {restored} alteração(ões) desfeita(s).");
            return restored;
        }

        private static Microsoft.Win32.RegistryKey? OpenKey(string hive, string path, bool writable)
        {
            var h = hive == "HKCU" ? Microsoft.Win32.Registry.CurrentUser : Microsoft.Win32.Registry.LocalMachine;
            return writable ? h.OpenSubKey(path, true) : h.OpenSubKey(path, false);
        }

        private static string? GetServiceStartMode(string serviceName)
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT StartMode FROM Win32_Service WHERE Name='" + serviceName.Replace("'", "''") + "'");
                foreach (System.Management.ManagementObject mo in searcher.Get())
                {
                    var raw = (mo["StartMode"]?.ToString() ?? "").Trim();
                    if (raw == "Auto") return "Auto";
                    if (raw == "Manual") return "Manual";
                    if (raw == "Disabled") return "Disabled";
                    return raw;
                }
            }
            catch { }
            return null;
        }

        private static bool SetServiceStartMode(string serviceName, string wmiMode)
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT * FROM Win32_Service WHERE Name='" + serviceName.Replace("'", "''") + "'");
                foreach (System.Management.ManagementObject mo in searcher.Get())
                {
                    var result = mo.InvokeMethod("ChangeStartMode", new object[] { wmiMode }) as System.Management.ManagementBaseObject;
                    return result != null && Convert.ToUInt32(result["ReturnValue"] ?? 0u) == 0;
                }
            }
            catch { }
            return false;
        }

        private static bool IsHibernationEnabled()
        {
            var outp = RunCapture("powercfg /a");
            return outp.IndexOf("hibernation", StringComparison.OrdinalIgnoreCase) >= 0
                || outp.IndexOf("hibernação", StringComparison.OrdinalIgnoreCase) >= 0
                || outp.IndexOf("hibernacao", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string RunCapture(string command)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -NonInteractive -Command \"" + command.Replace("\"", "'") + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var p = System.Diagnostics.Process.Start(psi);
                if (p == null) return "";
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(30000);
                return output;
            }
            catch { return ""; }
        }

        public void ExportBundle(string sessionDir, string bundlePath)
        {
            try
            {
                var tmp = Path.Combine(sessionDir, "bundle");
                Directory.CreateDirectory(tmp);
                var stackJson = System.Text.Json.JsonSerializer.Serialize(_stack, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
                File.WriteAllText(Path.Combine(tmp, "stack.json"), stackJson);
                foreach (var f in Directory.GetFiles(sessionDir))
                {
                    var name = Path.GetFileName(f);
                    File.Copy(f, Path.Combine(tmp, name), true);
                }
                if (File.Exists(bundlePath)) File.Delete(bundlePath);
                System.IO.Compression.ZipFile.CreateFromDirectory(tmp, bundlePath);
            }
            catch { }
        }

        // Implementação correta dos métodos da interface IRollbackManager
        public async Task CreateRollbackPointAsync(string pointId, string description)
        {
            await Task.Run(() =>
            {
                var path = Path.Combine(_dir, pointId);
                Directory.CreateDirectory(path);
                // Aqui você pode salvar informações adicionais sobre o ponto de rollback
            });
        }

        // Rollback REAL: lê o snapshot persistido e devolve o sistema ao estado
        // original. Antes retornava Success = true sem desfazer absolutely nada.
        public async Task<RollbackResult> ExecuteRollbackAsync(string pointId)
        {
            App.LoggingService?.LogWarning($"[ROLLBACK] Executando rollback real para o ponto: {pointId}");
            return await Task.Run(() =>
            {
                try
                {
                    var path = Path.Combine(_dir, pointId ?? "");
                    var snapFile = Path.Combine(path, SnapshotFileName);
                    if (!File.Exists(snapFile))
                    {
                        App.LoggingService?.LogWarning($"[ROLLBACK] Nenhum snapshot encontrado em {snapFile}. Nada a desfazer.");
                        return new RollbackResult
                        {
                            TransactionId = pointId ?? "",
                            Success = false,
                            RollbackActionsExecuted = 0
                        };
                    }

                    var json = File.ReadAllText(snapFile);
                    var snap = System.Text.Json.JsonSerializer.Deserialize<SafetySnapshot>(json,
                        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (snap == null)
                    {
                        return new RollbackResult { TransactionId = pointId ?? "", Success = false, RollbackActionsExecuted = 0 };
                    }

                    int count = RestoreFromSnapshot(snap);
                    App.LoggingService?.LogSuccess($"[ROLLBACK] Rollback concluído: {count} alteração(ões) desfeita(s).");
                    return new RollbackResult
                    {
                        TransactionId = pointId ?? "",
                        Success = count > 0,
                        RollbackActionsExecuted = count
                    };
                }
                catch (Exception ex)
                {
                    App.LoggingService?.LogError("[ROLLBACK] Falha no rollback: " + ex.Message);
                    return new RollbackResult { TransactionId = pointId ?? "", Success = false, RollbackActionsExecuted = 0 };
                }
            });
        }

        public async Task RemoveRollbackPointAsync(string pointId)
        {
            await Task.Run(() =>
            {
                var path = Path.Combine(_dir, pointId);
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            });
        }

        // Corrigindo o tipo de retorno para corresponder exatamente à interface
        public List<RollbackPoint> GetAvailableRollbackPoints()
        {
            var points = new List<RollbackPoint>();
            if (Directory.Exists(_dir))
            {
                foreach (var dir in Directory.GetDirectories(_dir))
                {
                    var point = new RollbackPoint
                    {
                        Id = Path.GetFileName(dir),
                        Description = "Ponto de rollback",
                        CreatedAt = Directory.GetCreationTime(dir)
                    };
                    points.Add(point);
                }
            }
            return points;
        }

        public bool RollbackPointExists(string pointId)
        {
            var path = Path.Combine(_dir, pointId);
            return Directory.Exists(path);
        }
    }

    public class RollbackAction
    {
        public string Description { get; set; } = "";
        public string Type { get; set; } = ""; // registry/service/powerplan/affinity
        public string BackupLocation { get; set; } = "";
        public DateTime Timestamp { get; set; }
    }
}
