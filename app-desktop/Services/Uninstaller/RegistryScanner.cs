using System;
using System.Collections.Generic;
using Microsoft.Win32;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Uninstaller
{
    public class RegistryScanner
    {
        private readonly ILoggingService _logger;

        public RegistryScanner(ILoggingService logger)
        {
            _logger = logger;
        }

        public List<AppInfo> ScanInstalledApps()
        {
            var apps = new List<AppInfo>();
            
            _logger.LogInfo("Iniciando escaneamento de aplicativos instalados no registro...");
            
            // CORRECTION 1: Scan all required registry locations
            // 1. HKLM 64-bit
            _logger.LogInfo("Escaneando HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall (64-bit)...");
            ScanLocation(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", RegistryView.Registry64, apps);
            
            // 2. HKLM 32-bit (WOW6432Node)
            _logger.LogInfo("Escaneando HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall (32-bit/WOW6432Node)...");
            ScanLocation(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", RegistryView.Registry32, apps);
            
            // 3. HKCU 64-bit
            _logger.LogInfo("Escaneando HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall (64-bit)...");
            ScanLocation(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall", RegistryView.Registry64, apps);
            
            // 4. HKCU 32-bit
            _logger.LogInfo("Escaneando HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall (32-bit)...");
            ScanLocation(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall", RegistryView.Registry32, apps);

            _logger.LogInfo($"Escaneamento de registro concluído: {apps.Count} aplicativos encontrados.");
            return apps;
        }

        private void ScanLocation(RegistryHive hive, string path, RegistryView view, List<AppInfo> apps)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(path);
                if (baseKey == null)
                {
                    _logger.LogWarning($"Não foi possível abrir {hive}\\{path} ({view})");
                    return;
                }

                var subKeyNames = baseKey.GetSubKeyNames();
                _logger.LogInfo($"Encontradas {subKeyNames.Length} subchaves em {hive}\\{path} ({view})");

                foreach (var subKeyName in subKeyNames)
                {
                    try
                    {
                        using var subKey = baseKey.OpenSubKey(subKeyName);
                        if (subKey == null) continue;

                        // CORRECTION 1: Read DisplayName, UninstallString, QuietUninstallString
                        var displayName = subKey.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(displayName)) continue;

                        // Ignore SystemComponent = 1
                        var systemComponent = subKey.GetValue("SystemComponent");
                        if (systemComponent != null && systemComponent.ToString() == "1") continue;

                        // Ignore entries with ParentKeyName (sub-components)
                        var parentKeyName = subKey.GetValue("ParentKeyName") as string;
                        if (!string.IsNullOrEmpty(parentKeyName)) continue;

                        // CORRECTION 1: Must have UninstallString or QuietUninstallString
                        var uninst = subKey.GetValue("UninstallString") as string;
                        var quietUninst = subKey.GetValue("QuietUninstallString") as string;
                        if (string.IsNullOrWhiteSpace(uninst) && string.IsNullOrWhiteSpace(quietUninst)) continue;

                        // CRITICAL FIX: Build correct registry path for deletion
                        // Format must be: HKEY_LOCAL_MACHINE\SOFTWARE\...\{GUID}
                        string hivePrefix = hive == RegistryHive.LocalMachine ? "HKEY_LOCAL_MACHINE" : "HKEY_CURRENT_USER";
                        string fullRegistryPath = $"{hivePrefix}\\{path}\\{subKeyName}";

                        _logger.LogInfo($"[RegistryScanner] App encontrado: {displayName} | RegistryPath: {fullRegistryPath}");

                        var app = new AppInfo
                        {
                            Name = displayName.TrimEnd('\0', ' '),
                            Publisher = subKey.GetValue("Publisher") as string,
                            Version = subKey.GetValue("DisplayVersion") as string,
                            InstallLocation = subKey.GetValue("InstallLocation") as string,
                            DisplayIcon = subKey.GetValue("DisplayIcon") as string,
                            UninstallString = uninst,
                            QuietUninstallString = quietUninst,
                            RegistryPath = fullRegistryPath, // CORRECTED: Now stores the correct path for deletion
                            IsWin32 = true,
                            Guid = subKeyName.StartsWith("{") && subKeyName.EndsWith("}") ? subKeyName : null
                        };

                        apps.Add(app);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Erro ao ler subkey {subKeyName} em {hive}\\{path}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Erro ao acessar {hive}\\{path} ({view}): {ex.Message}");
            }
        }

        public List<string> FindRelatedRegistryKeys(string appName, string? publisher, string? guid)
        {
            var keys = new List<string>();
            _logger.LogInfo($"Iniciando escaneamento profundo de registro para: {appName}");

            string[] searchPaths = { @"SOFTWARE", @"SOFTWARE\WOW6432Node" };
            RegistryHive[] hives = { RegistryHive.LocalMachine, RegistryHive.CurrentUser };

            foreach (var hive in hives)
            {
                foreach (var baseSearchPath in searchPaths)
                {
                    try
                    {
                        using var topKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default).OpenSubKey(baseSearchPath);
                        if (topKey == null) continue;

                        foreach (var subName in topKey.GetSubKeyNames())
                        {
                            bool match = false;
                            if (subName.Contains(appName, StringComparison.OrdinalIgnoreCase)) match = true;
                            
                            if (!match && !string.IsNullOrEmpty(publisher) && subName.Contains(publisher, StringComparison.OrdinalIgnoreCase))
                            {
                                using var pubKey = topKey.OpenSubKey(subName);
                                if (pubKey != null)
                                {
                                    foreach (var appSubName in pubKey.GetSubKeyNames())
                                    {
                                        if (appSubName.Contains(appName, StringComparison.OrdinalIgnoreCase))
                                        {
                                            keys.Add($@"{hive}\{baseSearchPath}\{subName}\{appSubName}");
                                        }
                                    }
                                }
                                continue;
                            }

                            if (match)
                            {
                                keys.Add($@"{hive}\{baseSearchPath}\{subName}");
                            }
                        }
                    }
                    catch { }
                }
            }

            if (!string.IsNullOrEmpty(guid))
            {
                keys.Add($@"HKEY_CLASSES_ROOT\Installer\Products\{guid.Replace("{", "").Replace("}", "").Replace("-", "")}");
            }

            return keys;
        }
    }
}
