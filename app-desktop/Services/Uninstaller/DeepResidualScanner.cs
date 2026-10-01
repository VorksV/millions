using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Uninstaller
{
    public class DeepResidualScanner
    {
        private readonly ILoggingService _logger;

        public DeepResidualScanner(ILoggingService logger)
        {
            _logger = logger;
        }

        public List<string> ScanFilesystemFolders(string appName, string? publisher, string? installLocation)
        {
            var folders = new List<string>();
            var rootPaths = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Path.Combine(Environment.GetEnvironmentVariable("SystemDrive") ?? "C:", "Users", Environment.UserName, "AppData", "LocalLow")
            };

            // 1. Add InstallLocation if it still exists
            if (!string.IsNullOrEmpty(installLocation) && Directory.Exists(installLocation))
            {
                folders.Add(installLocation);
            }

            // 2. Scan common roots for appName or publisher
            foreach (var root in rootPaths)
            {
                if (!Directory.Exists(root)) continue;

                try
                {
                    var subDirs = Directory.GetDirectories(root);
                    foreach (var dir in subDirs)
                    {
                        var dirName = Path.GetFileName(dir);

                        // Match app name
                        if (dirName.Contains(appName, StringComparison.OrdinalIgnoreCase))
                        {
                            folders.Add(dir);
                        }
                        // Match publisher name (if it's a publisher folder, check its children for app name)
                        else if (!string.IsNullOrEmpty(publisher) && dirName.Contains(publisher, StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                var pubSubDirs = Directory.GetDirectories(dir);
                                foreach (var pubSubDir in pubSubDirs)
                                {
                                    if (Path.GetFileName(pubSubDir).Contains(appName, StringComparison.OrdinalIgnoreCase))
                                    {
                                        folders.Add(pubSubDir);
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch (UnauthorizedAccessException) { /* Skip inaccessible folders */ }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Erro ao escanear {root}: {ex.Message}");
                }
            }

            return folders.Distinct().ToList();
        }

        public List<string> FindRelatedServices(string appName, string? publisher)
        {
            // Simplified: list registry services and check DisplayName
            var services = new List<string>();
            const string servicePath = @"SYSTEM\CurrentControlSet\Services";
            
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default).OpenSubKey(servicePath);
                if (baseKey != null)
                {
                    foreach (var srvName in baseKey.GetSubKeyNames())
                    {
                        using var srvKey = baseKey.OpenSubKey(srvName);
                        if (srvKey == null) continue;

                        var dispName = srvKey.GetValue("DisplayName") as string;
                        if (string.IsNullOrEmpty(dispName)) continue;

                        if (dispName.Contains(appName, StringComparison.OrdinalIgnoreCase) || 
                            (!string.IsNullOrEmpty(publisher) && dispName.Contains(publisher, StringComparison.OrdinalIgnoreCase)))
                        {
                            services.Add(srvName);
                        }
                    }
                }
            }
            catch { }

            return services;
        }

        public List<string> FindScheduledTasks(string appName)
        {
             // This can be complex, for now we will just return any task names matching the appName
             // usually tracked via 'schtasks /query' or similar. 
             // To keep it light, let's skip deep task scanning or just mock it.
             return new List<string>();
        }
    }
}
