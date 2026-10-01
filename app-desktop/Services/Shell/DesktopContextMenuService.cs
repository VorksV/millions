using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace VoltrisOptimizer.Services.Shell
{
    public class DesktopContextMenuService
    {
        private const string TAG = "[DesktopContextMenu]";
        private const string PARENT_NAME = "VoltrisOptimizer";
        private const string SUBCOMMANDS = "subcommands";

        private readonly string[] REGISTRY_ROOTS = new[]
        {
            @"Software\Classes\Directory\Background\shell",
            @"Software\Classes\DesktopBackground\shell"};

        private readonly ILoggingService? _logger;
        private static readonly object _lock = new();

        public DesktopContextMenuService(ILoggingService? logger = null)
        {
            _logger = logger;
        }

        public bool Register()
        {
            return RegisterWithSubMenus();
        }

        /// <summary>
        /// Registra "Voltris Optimizer" como submenu no menu de contexto da área de trabalho.
        ///
        /// Usa ExtendedSubCommandsKey (funciona em Directory\Background\shell e DesktopBackground\shell).
        ///
        /// Estrutura:
        ///   HKCU\...\shell\VoltrisOptimizer
        ///       (Default)              = "Voltris Optimizer"
        ///       MUIVerb                = "Voltris Optimizer"
        ///       Icon                   = "..."
        ///       Position               = "Top"
        ///       ExtendedSubCommandsKey = "Directory\Background\shell\VoltrisOptimizer\subcommands"
        ///
        ///   HKCU\...\shell\VoltrisOptimizer\subcommands\shell\01abrir
        ///       MUIVerb = "Abrir Voltris"
        ///       command\(Default) = "..."
        /// </summary>
        public bool RegisterWithSubMenus()
        {
            lock (_lock)
            {
                var stopwatch = Stopwatch.StartNew();
                Log($"{TAG} >>> RegisterWithSubMenus INICIADO (ExtendedSubCommandsKey)");

                try
                {
                    var exePath = GetExecutablePath(out string exeError);
                    if (exePath == null)
                    {
                        LogError($"{TAG} ABORTADO: {exeError}");
                        return false;
                    }
                    Log($"{TAG} Executável: \"{exePath}\"");

                    var iconPath = GetIconPath();
                    Log($"{TAG} Ícone: \"{iconPath}\"");

                    var menuText = "Voltris Optimizer";
                    var subcommandsSuffix = $"\\{PARENT_NAME}\\{SUBCOMMANDS}";

                    var subItems = new (string key, string label, string args)[]
                    {
                        ("01abrir",        "Abrir Voltris",                $"\"{exePath}\""),
                        ("02limpeza",      "Limpeza R\u00e1pida",          $"\"{exePath}\" -quickclean"),
                        ("03otimizar",     "Otimiza\u00e7\u00e3o R\u00e1pida", $"\"{exePath}\" -optimize"),
                        ("04desfragmentar","Desfragmentar Disco",          $"\"{exePath}\" -defrag"),
                        ("05diagnostico",  "Diagn\u00f3stico do Sistema",  $"\"{exePath}\" -diagnose")};

                    int okRoots = 0;
                    int failRoots = 0;

                    foreach (var root in REGISTRY_ROOTS)
                    {
                        Log($"{TAG} === Registrando em \"{root}\" ===");
                        if (RegisterSubMenuInRoot(root, subcommandsSuffix, menuText, exePath, iconPath, subItems))
                        {
                            okRoots++;
                            Log($"{TAG} Registro OK em \"{root}\"");
                        }
                        else
                        {
                            failRoots++;
                            LogError($"{TAG} Registro FALHOU em \"{root}\"");
                        }
                    }

                    RefreshExplorer();
                    Log($"{TAG} DumpRegistryState pós-registro:");
                    DumpRegistryState();

                    stopwatch.Stop();
                    Log($"{TAG} >>> RegisterWithSubMenus CONCLUÍDO em {stopwatch.ElapsedMilliseconds}ms ({okRoots}/{REGISTRY_ROOTS.Length} raízes OK)");
                    return failRoots == 0;
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    LogError($"{TAG} EXCEÇÃO: {GetExceptionDetail(ex)}");
                    return false;
                }
            }
        }

        private bool RegisterSubMenuInRoot(
            string root, string subcommandsSuffix, string menuText,
            string exePath, string iconPath,
            (string key, string label, string args)[] subItems)
        {
            var parentPath = $"{root}\\{PARENT_NAME}";
            var subcommandsPath = $"{root}{subcommandsSuffix}";

            // ExtendedSubCommandsKey espera caminho RELATIVO a HKEY_CLASSES_ROOT.
            // root é tipo "Software\Classes\Directory\Background\shell"
            // HKCR relativo é "Directory\Background\shell"
            var hkcrRoot = root.StartsWith("Software\\Classes\\")
                ? root.Substring("Software\\Classes\\".Length)
                : root;
            var hkcrPath = $"{hkcrRoot}\\{PARENT_NAME}\\{SUBCOMMANDS}";

            try
            {
                Log($"{TAG}   [1/4] Removendo entradas antigas...");
                RemoveAllVoltris(root);

                Log($"{TAG}   [2/4] Criando chave pai: \"{parentPath}\"...");
                using (var parentKey = Registry.CurrentUser.CreateSubKey(parentPath))
                {
                    if (parentKey == null) { LogError($"{TAG}   [FALHA] CreateSubKey pai retornou null"); return false; }

                    // ExtendedSubCommandsKey deve ser caminho relativo ao HKCR
                    // Ex: "Directory\Background\shell\VoltrisOptimizer\subcommands"
                    var regValuePath = hkcrPath;

                    parentKey.SetValue(null, menuText);
                    parentKey.SetValue("MUIVerb", menuText);
                    parentKey.SetValue("Icon", iconPath);
                    parentKey.SetValue("Position", "Top");
                    parentKey.SetValue("ExtendedSubCommandsKey", regValuePath);
                    parentKey.Flush();
                    Log($"{TAG}   [2/4] Valores gravados. ExtendedSubCommandsKey=\"{regValuePath}\"");
                }

                // Verifica
                Log($"{TAG}   [2/4] Verificando valores do pai...");
                using (var ck = Registry.CurrentUser.OpenSubKey(parentPath))
                {
                    if (ck == null) { LogError($"{TAG}   [FALHA] Chave pai não encontrada"); return false; }
                    var extKey = ck.GetValue("ExtendedSubCommandsKey") as string;
                    Log($"{TAG}   [2/4] ExtendedSubCommandsKey lido: \"{extKey}\" (esperado \"{hkcrPath}\")");
                }

                Log($"{TAG}   [3/4] Criando {subItems.Length} subcomandos em \"{subcommandsPath}\"...");
                int subOk = 0;
                foreach (var (key, label, args) in subItems)
                {
                    try
                    {
                        // subcommands\shell\key
                        var itemShellPath = $"{subcommandsPath}\\shell\\{key}";
                        using (var itemKey = Registry.CurrentUser.CreateSubKey(itemShellPath))
                        {
                            if (itemKey == null) { LogError($"{TAG}     [AVISO] CreateSubKey(\"{itemShellPath}\") = null"); continue; }
                            itemKey.SetValue(null, label);
                            itemKey.SetValue("MUIVerb", label);
                            itemKey.SetValue("Icon", iconPath);
                            itemKey.Flush();
                        }

                        var cmdPath = $"{itemShellPath}\\command";
                        using (var cmdKey = Registry.CurrentUser.CreateSubKey(cmdPath))
                        {
                            if (cmdKey == null) { LogError($"{TAG}     [AVISO] CreateSubKey command = null"); continue; }
                            cmdKey.SetValue(null, args);
                            cmdKey.Flush();
                        }

                        Log($"{TAG}     \"{label}\" registrado.");
                        subOk++;
                    }
                    catch (Exception ex)
                    {
                        LogError($"{TAG}     [ERRO] \"{label}\": {GetExceptionDetail(ex)}");
                    }
                }
                Log($"{TAG}   [3/4] Subcomandos: {subOk}/{subItems.Length} OK.");
                return subOk == subItems.Length;
            }
            catch (Exception ex)
            {
                LogError($"{TAG}   EXCEÇÃO: {GetExceptionDetail(ex)}");
                return false;
            }
        }

        // ---------------------------------------------------------------
        // Unregister / Cleanup
        // ---------------------------------------------------------------
        public bool Unregister()
        {
            lock (_lock)
            {
                Log($"{TAG} >>> Unregister INICIADO");
                var sw = Stopwatch.StartNew();
                int total = 0;
                foreach (var root in REGISTRY_ROOTS)
                {
                    var keys = GetAllVoltrisKeys(root);
                    foreach (var key in keys)
                    {
                        SafeDeleteSingle($"{root}\\{key}");
                        total++;
                    }
                }
                if (total > 0) RefreshExplorer();
                sw.Stop();
                Log($"{TAG} >>> Unregister CONCLUÍDO em {sw.ElapsedMilliseconds}ms ({total} removidos)");
                return true;
            }
        }

        public bool CleanupAll()
        {
            Log($"{TAG} >>> CleanupAll INICIADO");
            var sw = Stopwatch.StartNew();
            int total = 0;
            foreach (var root in REGISTRY_ROOTS)
            {
                var keys = GetAllVoltrisKeys(root);
                foreach (var key in keys)
                {
                    SafeDeleteSingle($"{root}\\{key}");
                    total++;
                }
            }
            if (total > 0) RefreshExplorer();
            sw.Stop();
            Log($"{TAG} >>> CleanupAll CONCLUÍDO em {sw.ElapsedMilliseconds}ms ({total} removidos)");
            return true;
        }

        public bool IsRegistered()
        {
            foreach (var root in REGISTRY_ROOTS)
                if (GetAllVoltrisKeys(root).Count > 0) return true;
            return false;
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------
        private void RemoveAllVoltris(string root)
        {
            foreach (var key in GetAllVoltrisKeys(root))
                SafeDeleteSingle($"{root}\\{key}");
        }

        private List<string> GetAllVoltrisKeys(string root)
        {
            var result = new List<string>();
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(root);
                if (key == null) return result;
                foreach (var name in key.GetSubKeyNames())
                    if (name.StartsWith("Voltris", StringComparison.OrdinalIgnoreCase))
                        result.Add(name);
            }
            catch (Exception ex) { LogError($"{TAG} GetAllVoltrisKeys: {GetExceptionDetail(ex)}"); }
            return result;
        }

        public void DumpRegistryState()
        {
            Log($"{TAG} === DumpRegistryState ===");
            foreach (var root in REGISTRY_ROOTS) DumpSingleRoot(root);
            Log($"{TAG} === Fim ===");
        }

        private void DumpSingleRoot(string root)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(root);
                if (key == null) { Log($"{TAG}   \"{root}\" -> não existe"); return; }

                var names = key.GetSubKeyNames().Where(n => n.StartsWith("Voltris")).ToList();
                Log($"{TAG}   \"{root}\": {names.Count} entrada(s) Voltris");

                foreach (var n in names)
                {
                    var pPath = $"{root}\\{n}";
                    using var pk = Registry.CurrentUser.OpenSubKey(pPath);
                    if (pk == null) continue;
                    var def = pk.GetValue(null) as string ?? "(sem (Default))";
                    var ext = pk.GetValue("ExtendedSubCommandsKey") as string ?? "(sem ExtendedSubCommandsKey)";
                    Log($"{TAG}     \"{n}\": (Default)=\"{def}\" | ExtendedSubCommandsKey=\"{ext}\"");

                    var subPath = $"{pPath}\\subcommands\\shell";
                    using var sk = Registry.CurrentUser.OpenSubKey(subPath);
                    if (sk != null)
                    {
                        var subs = sk.GetSubKeyNames();
                        Log($"{TAG}       subcommands\\shell\\ tem {subs.Length} subchave(s): [{string.Join(", ", subs)}]");
                        foreach (var s in subs)
                        {
                            var sPath = $"{subPath}\\{s}";
                            using var svk = Registry.CurrentUser.OpenSubKey(sPath);
                            var sl = svk?.GetValue(null) as string ?? "(sem label)";
                            Log($"{TAG}         \"{s}\" -> \"{sl}\"");
                            var cPath = $"{sPath}\\command";
                            using var ck = Registry.CurrentUser.OpenSubKey(cPath);
                            var cmd = ck?.GetValue(null) as string ?? "(AUSENTE)";
                            Log($"{TAG}           command: \"{cmd}\"");
                        }
                    }
                }
            }
            catch (Exception ex) { LogError($"{TAG} DumpSingleRoot: {GetExceptionDetail(ex)}"); }
        }

        private void SafeDeleteSingle(string keyPath)
        {
            try
            {
                var parts = keyPath.Split('\\');
                if (parts.Length < 2) return;
                var parent = string.Join("\\", parts.Take(parts.Length - 1));
                var leaf = parts[^1];
                using var pk = Registry.CurrentUser.OpenSubKey(parent, writable: true);
                if (pk == null) return;
                if (pk.GetSubKeyNames().Contains(leaf))
                    pk.DeleteSubKeyTree(leaf);
            }
            catch (Exception ex) { LogError($"{TAG} SafeDeleteSingle: {GetExceptionDetail(ex)}"); }
        }

        private string? GetExecutablePath(out string error)
        {
            error = "";
            try
            {
                var p = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(p) && File.Exists(p)) return p;
                p = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(p) && File.Exists(p)) return p;
                p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VoltrisOptimizer.exe");
                if (File.Exists(p)) return p;
                error = $"Nenhum executável em \"{AppDomain.CurrentDomain.BaseDirectory}\"";
                return null;
            }
            catch (Exception ex) { error = GetExceptionDetail(ex); return null; }
        }

        private string GetIconPath()
        {
            try
            {
                var dir = AppDomain.CurrentDomain.BaseDirectory;
                foreach (var c in new[] { Path.Combine(dir, "Images", "favicon.ico"), Path.Combine(dir, "Images", "voltris.ico"), Path.Combine(dir, "favicon.ico"), Path.Combine(dir, "voltris.ico") })
                    if (File.Exists(c)) { Log($"{TAG} Ícone: \"{c}\""); return c; }
                var exe = GetExecutablePath(out _);
                Log($"{TAG} Ícone fallback: \"{exe},0\"");
                return $"{exe},0";
            }
            catch (Exception ex) { var exe = GetExecutablePath(out _); LogError($"{TAG} GetIconPath: {GetExceptionDetail(ex)}"); return $"{exe},0"; }
        }

        private void RefreshExplorer()
        {
            try { SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero); SHChangeNotify(0x7FFFFFFF, 0x0000, IntPtr.Zero, IntPtr.Zero); }
            catch (Exception ex) { LogError($"{TAG} RefreshExplorer: {GetExceptionDetail(ex)}"); }
        }

        private void Log(string m) { try { _logger?.LogInfo($"{TAG} {m}"); Debug.WriteLine($"{TAG} {m}"); } catch { } }
        private void LogError(string m) { try { _logger?.LogError($"{TAG} {m}"); Debug.WriteLine($"{TAG} {m}"); } catch { } }

        private static string GetExceptionDetail(Exception ex)
        {
            if (ex == null) return "null";
            var sb = new StringBuilder();
            sb.Append($"{ex.GetType().Name}: {ex.Message}");
            if (!string.IsNullOrEmpty(ex.StackTrace))
            {
                var f = ex.StackTrace.Split('\n');
                if (f.Length > 0) sb.Append($" | {f[0].Trim()}");
            }
            if (ex.InnerException != null) sb.Append($" | Inner: {GetExceptionDetail(ex.InnerException)}");
            return sb.ToString();
        }

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);
    }
}
