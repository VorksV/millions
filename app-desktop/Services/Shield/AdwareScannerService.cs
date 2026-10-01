using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Shield
{
    public class AdwareScannerService
    {
        private readonly ILoggingService _logger;
        private readonly SecurityLogService _securityLog;
        private bool _backgroundScansPaused;
        
        private readonly string[] _adwarePatterns = new[]
        {
            "toolbar", "searchbar", "browser helper", "adware", "pup", "bundler",
            "conduit", "babylon", "ask toolbar", "mindspark", "myway", "searchprotect",
            "sweetpage", "delta-homes", "omiga-plus", "qvo6", "webssearches"
        };
        
        // Whitelist de software legítimo que não deve ser detectado como adware
        private readonly string[] _whitelist = new[]
        {
            "intel", "nvidia", "amd", "microsoft", "windows", "device stage",
            "windowsapps", "common files", "system32", "syswow64",
            "program files", "dotnet", ".net", "visual studio",
            "task scheduler", "taskbar", "voltris"
        };
        
        public event EventHandler<AdwareDetectedEventArgs>? AdwareDetected;
        
        /// <summary>
        /// Quantidade real de itens EXAMINADOS no scan corrente. Antes o log registrava
        /// "Scanned: 0 items" porque os sub-scanners retornam apenas as ameaças encontradas
        /// e o total examinado não era contabilizado.
        /// </summary>
        private int _itemsExamined;
        
        private void ResetExaminedCounter() => Interlocked.Exchange(ref _itemsExamined, 0);
        
        private void CountExamined(int amount = 1)
        {
            if (amount > 0) Interlocked.Add(ref _itemsExamined, amount);
        }
        
        /// [FIX:B-1] Tornada pública: o consumidor do scan (VoltrisShieldService)
        /// precisa da contagem REAL de itens examinados. Sem acesso a ela, o
        /// chamador usava threats.Count como substituto — e a UI exibia
        /// "N ameaças em N itens varridos", o que é um número inventado: por
        /// definição o número de ameaças é menor que o de itens varridos, então
        /// igualdade entre os dois era um tell de que o valor estava errado.
        /// </summary>
        public int ExaminedCount => Volatile.Read(ref _itemsExamined);
        
        public AdwareScannerService(ILoggingService logger, SecurityLogService securityLog)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _securityLog = securityLog ?? throw new ArgumentNullException(nameof(securityLog));
        }
        
        public async Task<List<AdwareItem>> ScanForAdwareAsync()
        {
            var items = new List<AdwareItem>();
            
            try
            {
                ResetExaminedCounter();
                _logger.LogInfo("[AdwareScanner] Iniciando scan completo de adware...");
                _securityLog.LogSecurityEvent("AdwareScanner", "SCAN_STARTED", "Full adware scan initiated");
                
                // Scan de diretórios
                items.AddRange(await ScanProgramFilesAsync());
                items.AddRange(await ScanAppDataAsync());
                items.AddRange(await ScanProgramDataAsync());
                
                // Scan de registry
                items.AddRange(await ScanRegistryAsync());
                
                _logger.LogSuccess($"[AdwareScanner] Scan concluído: {items.Count} ameaças em {ExaminedCount} itens examinados");
                _securityLog.LogScanCompleted("AdwareScanner", ExaminedCount, items.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError("[AdwareScanner] Erro no scan de adware", ex);
            }
            
            return items;
        }
        
        public async Task<List<AdwareItem>> ScanTempFoldersAsync()
        {
            var items = new List<AdwareItem>();
            try
            {
                _logger.LogInfo("[AdwareScanner] Escaneando pastas temporárias...");
                var tempPath = Path.GetTempPath();
                items = await ScanDirectoryForAdwareAsync(tempPath, searchDepth: 1);
                _logger.LogSuccess($"[AdwareScanner] Temp scan concluído: {items.Count} ameaças");
            }
            catch (Exception ex)
            {
                _logger.LogError("[AdwareScanner] Erro no scan de temp", ex);
            }
            return items;
        }
        
        public async Task<List<AdwareItem>> ScanDownloadsFolderAsync()
        {
            var items = new List<AdwareItem>();
            try
            {
                _logger.LogInfo("[AdwareScanner] Escaneando Downloads...");
                var downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (Directory.Exists(downloadsPath))
                {
                    items = await ScanDirectoryForAdwareAsync(downloadsPath, searchDepth: 1);
                }
                _logger.LogSuccess($"[AdwareScanner] Downloads scan concluído: {items.Count} ameaças");
            }
            catch (Exception ex)
            {
                _logger.LogError("[AdwareScanner] Erro no scan de downloads", ex);
            }
            return items;
        }
        
        public async Task<int> ScanFullSystemAsync()
        {
            var items = await ScanForAdwareAsync();
            return items.Count;
        }
        
        private bool IsSafeRemovalPath(string path)
        {
            try
            {
                var fullPath = Path.GetFullPath(path);
                var roots = new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop"),
                    Path.GetTempPath()
                };

                return roots.Where(root => !string.IsNullOrWhiteSpace(root))
                    .Select(root => Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar)
                    .Any(root => fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> RemoveAdwareItemAsync(AdwareItem item)
        {
            try
            {
                _logger.LogInfo($"[AdwareScanner] Removendo: {item.Name}");

                if (item.Type == AdwareType.Directory && Directory.Exists(item.Path))
                {
                    if (!IsSafeRemovalPath(item.Path))
                    {
                        _logger.LogWarning($"[AdwareScanner] Remoção de diretório bloqueada por segurança: {item.Path}");
                        return false;
                    }

                    Directory.Delete(item.Path, true);
                    _securityLog.LogSecurityEvent("AdwareScanner", "ADWARE_REMOVED", $"Directory: {item.Path}");
                    return true;
                }
                else if (item.Type == AdwareType.File && File.Exists(item.Path))
                {
                    if (!IsSafeRemovalPath(item.Path))
                    {
                        _logger.LogWarning($"[AdwareScanner] Remoção de arquivo bloqueada por segurança: {item.Path}");
                        return false;
                    }

                    File.Delete(item.Path);
                    _securityLog.LogSecurityEvent("AdwareScanner", "ADWARE_REMOVED", $"File: {item.Path}");
                    return true;
                }
                else if (item.Type == AdwareType.RegistryKey)
                {
                    return await Task.Run(() =>
                    {
                        try
                        {
                            // Parse do path: "HKLM\SOFTWARE\SubKey"
                            var parts = item.Path.Split(new[] { '\\' }, 2);
                            if (parts.Length < 2)
                            {
                                _logger.LogWarning($"[AdwareScanner] Path de registry inválido: {item.Path}");
                                return false;
                            }
                            
                            var hiveName = parts[0].ToUpperInvariant();
                            var subKeyPath = parts[1];
                            
                            RegistryKey? baseKey = hiveName switch
                            {
                                "HKLM" => Registry.LocalMachine,
                                "HKCU" => Registry.CurrentUser,
                                _ => null
                            };
                            
                            if (baseKey == null)
                            {
                                _logger.LogWarning($"[AdwareScanner] Hive desconhecido: {hiveName}");
                                return false;
                            }
                            
                            // Verificar se a chave existe antes de tentar deletar
                            using var testKey = baseKey.OpenSubKey(subKeyPath);
                            if (testKey == null)
                            {
                                _logger.LogInfo($"[AdwareScanner] Chave já não existe: {item.Path}");
                                return true; // Já removida
                            }
                            testKey.Close();
                            
                            baseKey.DeleteSubKeyTree(subKeyPath, throwOnMissingSubKey: false);
                            _securityLog.LogSecurityEvent("AdwareScanner", "ADWARE_REMOVED", $"Registry key deleted: {item.Path}");
                            _logger.LogSuccess($"[AdwareScanner] Chave de registro removida: {item.Path}");
                            return true;
                        }
                        catch (UnauthorizedAccessException)
                        {
                            _logger.LogWarning($"[AdwareScanner] Sem permissão para remover chave: {item.Path}");
                            return false;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"[AdwareScanner] Erro ao remover chave de registro: {item.Path}", ex);
                            return false;
                        }
                    });
                }
                
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[AdwareScanner] Erro ao remover {item.Name}", ex);
                return false;
            }
        }
        
        public void PauseBackgroundScans()
        {
            _backgroundScansPaused = true;
            _logger.LogInfo("[AdwareScanner] Scans em background pausados (Modo Gamer)");
        }
        
        public void ResumeBackgroundScans()
        {
            _backgroundScansPaused = false;
            _logger.LogInfo("[AdwareScanner] Scans em background retomados");
        }
        
        /// <summary>
        /// Escaneia extensões de browser suspeitas (Chrome, Edge, Firefox)
        /// </summary>
        public async Task<List<AdwareItem>> ScanBrowserExtensionsAsync()
        {
            var items = new List<AdwareItem>();
            try
            {
                var examined = new System.Runtime.CompilerServices.StrongBox<int>(0);
                _logger.LogInfo("[AdwareScanner] Escaneando extensões de browser...");
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                
                var chromeExtPath = Path.Combine(localAppData, @"Google\Chrome\User Data\Default\Extensions");
                items.AddRange(await ScanBrowserExtensionFolderAsync(chromeExtPath, "Chrome", examined));
                
                var edgeExtPath = Path.Combine(localAppData, @"Microsoft\Edge\User Data\Default\Extensions");
                items.AddRange(await ScanBrowserExtensionFolderAsync(edgeExtPath, "Edge", examined));
                
                var firefoxProfilesPath = Path.Combine(roamingAppData, @"Mozilla\Firefox\Profiles");
                if (Directory.Exists(firefoxProfilesPath))
                {
                    foreach (var profileDir in Directory.GetDirectories(firefoxProfilesPath))
                    {
                        var extensionsDir = Path.Combine(profileDir, "extensions");
                        items.AddRange(await ScanBrowserExtensionFolderAsync(extensionsDir, "Firefox", examined));
                    }
                }
                
                _logger.LogSuccess($"[AdwareScanner] Browser scan concluído: {items.Count} extensões suspeitas em {examined.Value} extensões examinadas");
                _securityLog.LogScanCompleted("AdwareScanner-Browser", examined.Value, items.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError("[AdwareScanner] Erro no scan de extensões de browser", ex);
            }
            return items;
        }
        
        private async Task<List<AdwareItem>> ScanBrowserExtensionFolderAsync(string extensionsPath, string browserName, System.Runtime.CompilerServices.StrongBox<int> examined)
        {
            var items = new List<AdwareItem>();
            if (!Directory.Exists(extensionsPath)) return items;
            
            await Task.Run(() =>
            {
                try
                {
                    foreach (var extDir in Directory.GetDirectories(extensionsPath))
                    {
                        // Cada extensão analisada conta como item examinado.
                        Interlocked.Increment(ref examined.Value);
                        
                        var manifestFiles = Directory.GetFiles(extDir, "manifest.json", SearchOption.AllDirectories);
                        foreach (var manifest in manifestFiles)
                        {
                            try
                            {
                                if (!IsSuspiciousBrowserManifest(manifest))
                                    continue;
                                {
                                    var item = new AdwareItem
                                    {
                                        Name = $"Extensão suspeita ({browserName})",
                                        Path = extDir,
                                        Type = AdwareType.Directory,
                                        Severity = AdwareSeverity.Medium
                                    };
                                    items.Add(item);
                                    _logger.LogWarning($"[AdwareScanner] Extensão suspeita em {browserName}: {extDir}");
                                    _securityLog.LogSecurityEvent("AdwareScanner", "SUSPICIOUS_EXTENSION", $"{browserName}: {extDir}");
                                    AdwareDetected?.Invoke(this, new AdwareDetectedEventArgs { Name = item.Name, Path = extDir });
                                    break;
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            });
            return items;
        }

        private bool IsSuspiciousBrowserManifest(string manifestPath)
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
                var values = new List<string>();
                AddManifestString(document.RootElement, "name", values);
                AddManifestString(document.RootElement, "short_name", values);
                AddManifestString(document.RootElement, "description", values);
                AddManifestString(document.RootElement, "permissions", values);
                AddManifestString(document.RootElement, "host_permissions", values);
                return values.Any(MatchesAdwarePattern);
            }
            catch
            {
                return false;
            }
        }

        private static void AddManifestString(JsonElement root, string propertyName, ICollection<string> values)
        {
            if (!root.TryGetProperty(propertyName, out var property))
                return;

            if (property.ValueKind == JsonValueKind.String)
            {
                var value = property.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    values.Add(value);
            }
            else if (property.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in property.EnumerateArray())
                {
                    if (element.ValueKind != JsonValueKind.String)
                        continue;

                    var value = element.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        values.Add(value);
                }
            }
        }
        
        /// <summary>
        /// Escaneia Scheduled Tasks suspeitas no Windows
        /// </summary>
        public async Task<List<AdwareItem>> ScanScheduledTasksAsync()
        {
            var items = new List<AdwareItem>();
            try
            {
                var tasksExamined = new System.Runtime.CompilerServices.StrongBox<int>(0);
                _logger.LogInfo("[AdwareScanner] Escaneando tarefas agendadas...");
                var tasksPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\Tasks");
                
                if (Directory.Exists(tasksPath))
                {
                    items = await Task.Run(() =>
                    {
                        var found = new List<AdwareItem>();
                        try
                        {
                            var taskFiles = Directory.GetFiles(tasksPath, "*", SearchOption.AllDirectories);
                            foreach (var taskFile in taskFiles)
                            {
                                try
                                {
                                    // Cada tarefa analisada conta como item examinado.
                                    Interlocked.Increment(ref tasksExamined.Value);
                                    
                                    if (IsMicrosoftTaskPath(taskFile))
                                        continue;

                                    var content = File.ReadAllText(taskFile);
                                    var fileName = Path.GetFileName(taskFile);
                                    if (MatchesAdwarePattern(fileName) || MatchesAdwarePattern(content))
                                    {
                                        var item = new AdwareItem
                                        {
                                            Name = "Tarefa agendada suspeita",
                                            Path = taskFile,
                                            Type = AdwareType.File,
                                            Severity = AdwareSeverity.Medium
                                        };
                                        found.Add(item);
                                        _logger.LogWarning($"[AdwareScanner] Tarefa agendada suspeita: {taskFile}");
                                        _securityLog.LogSecurityEvent("AdwareScanner", "SUSPICIOUS_TASK", taskFile);
                                    }
                                }
                                catch { }
                            }
                        }
                        catch (UnauthorizedAccessException)
                        {
                            _logger.LogWarning("[AdwareScanner] Sem permissão para escanear todas as tarefas agendadas");
                        }
                        return found;
                    });
                }
                _logger.LogSuccess($"[AdwareScanner] Scan de tarefas concluído: {items.Count} suspeitas em {tasksExamined.Value} tarefas examinadas");
                _securityLog.LogScanCompleted("AdwareScanner-Tasks", tasksExamined.Value, items.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError("[AdwareScanner] Erro no scan de tarefas agendadas", ex);
            }
            return items;
        }
        
        private async Task<List<AdwareItem>> ScanProgramFilesAsync()
        {
            var items = new List<AdwareItem>();
            
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            items.AddRange(await ScanDirectoryForAdwareAsync(programFiles, searchDepth: 2));
            
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (Directory.Exists(programFilesX86))
            {
                items.AddRange(await ScanDirectoryForAdwareAsync(programFilesX86, searchDepth: 2));
            }
            
            return items;
        }
        
        private async Task<List<AdwareItem>> ScanAppDataAsync()
        {
            var items = new List<AdwareItem>();
            
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            items.AddRange(await ScanDirectoryForAdwareAsync(localAppData, searchDepth: 2));
            
            var roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            items.AddRange(await ScanDirectoryForAdwareAsync(roamingAppData, searchDepth: 2));
            
            return items;
        }
        
        private async Task<List<AdwareItem>> ScanProgramDataAsync()
        {
            var items = new List<AdwareItem>();
            
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            items.AddRange(await ScanDirectoryForAdwareAsync(programData, searchDepth: 2));
            
            return items;
        }
        
        private async Task<List<AdwareItem>> ScanRegistryAsync()
        {
            var items = new List<AdwareItem>();
            
            await Task.Run(() =>
            {
                try
                {
                    var registryPaths = new[]
                    {
                        @"SOFTWARE",
                        @"SOFTWARE\WOW6432Node"
                    };
                    
                    foreach (var basePath in registryPaths)
                    {
                        try
                        {
                            using var key = Registry.LocalMachine.OpenSubKey(basePath);
                            if (key == null) continue;
                            
                            foreach (var subKeyName in key.GetSubKeyNames())
                            {
                                // Cada chave de registro visitada conta como item examinado.
                                CountExamined();
                                
                                if (IsAdwareRegistryKey(subKeyName))
                                {
                                    var item = new AdwareItem
                                    {
                                        Name = subKeyName,
                                        Path = $@"HKLM\{basePath}\{subKeyName}",
                                        Type = AdwareType.RegistryKey,
                                        Severity = AdwareSeverity.Medium
                                    };
                                    
                                    items.Add(item);
                                    _logger.LogWarning($"[AdwareScanner] Registry adware: {subKeyName}");
                                }
                            }
                        }
                        catch
                        {
                            // Ignorar erros de acesso
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError("[AdwareScanner] Erro no scan de registry", ex);
                }
            });
            
            return items;
        }
        
        private async Task<int> ScanDirectoryAsync(string path, int searchDepth)
        {
            var items = await ScanDirectoryForAdwareAsync(path, searchDepth);
            return items.Count;
        }
        
        private async Task<List<AdwareItem>> ScanDirectoryForAdwareAsync(string path, int searchDepth)
        {
            var items = new List<AdwareItem>();
            
            try
            {
                if (!Directory.Exists(path))
                    return items;
                
                // Filtrar junction points e symlinks que causam "Access denied"
                var dirInfo = new DirectoryInfo(path);
                if (dirInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    return items;
                
                string[] directories;
                try
                {
                    directories = Directory.GetDirectories(path);
                }
                catch (UnauthorizedAccessException)
                {
                    // Silenciar erros de acesso negado em diretórios protegidos do sistema
                    return items;
                }
                
                foreach (var dir in directories)
                {
                    try
                    {
                        // Cada diretório visitado conta como item examinado.
                        CountExamined();
                        
                        // Pular junction points/symlinks dentro do diretório
                        var subDirInfo = new DirectoryInfo(dir);
                        if (subDirInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
                            continue;
                        
                        var dirName = Path.GetFileName(dir).ToLowerInvariant();
                        
                        if (IsAdwareDirectory(dirName))
                        {
                            var item = new AdwareItem
                            {
                                Name = Path.GetFileName(dir),
                                Path = dir,
                                Type = AdwareType.Directory,
                                Severity = DetermineSeverity(dirName)
                            };
                            
                            items.Add(item);
                            _logger.LogWarning($"[AdwareScanner] Adware detectado: {dir}");
                            
                            AdwareDetected?.Invoke(this, new AdwareDetectedEventArgs
                            {
                                Name = item.Name,
                                Path = dir
                            });
                        }
                        
                        // Scan recursivo limitado
                        if (searchDepth > 0)
                        {
                            var subItems = await ScanDirectoryForAdwareAsync(dir, searchDepth - 1);
                            items.AddRange(subItems);
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Silenciar erros de acesso negado em subdiretórios
                    }
                    catch
                    {
                        // Ignorar outros erros de acesso
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Silenciar - diretórios protegidos do sistema (junction points, etc.)
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[AdwareScanner] Erro ao escanear {path}: {ex.Message}");
            }
            
            return items;
        }
        
        private bool IsAdwareDirectory(string dirName)
        {
            if (_whitelist.Any(safe => dirName.Contains(safe)))
                return false;
            
            return MatchesAdwarePattern(dirName);
        }
        
        private bool IsAdwareRegistryKey(string keyName)
        {
            var keyLower = keyName.ToLowerInvariant();
            
            if (_whitelist.Any(safe => keyLower.Contains(safe)))
                return false;
            
            return MatchesAdwarePattern(keyLower);
        }

        private bool MatchesAdwarePattern(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var normalized = value.ToLowerInvariant();
            return _adwarePatterns.Any(pattern =>
            {
                if (pattern.Contains(' '))
                    return normalized.Contains(pattern, StringComparison.Ordinal);

                var searchStart = 0;
                while (searchStart < normalized.Length)
                {
                    var matchStart = normalized.IndexOf(pattern, searchStart, StringComparison.Ordinal);
                    if (matchStart < 0)
                        return false;

                    var matchEnd = matchStart + pattern.Length;
                    var leftBoundary = matchStart == 0 || !char.IsLetterOrDigit(normalized[matchStart - 1]);
                    var rightBoundary = matchEnd == normalized.Length || !char.IsLetterOrDigit(normalized[matchEnd]);
                    if (leftBoundary && rightBoundary)
                        return true;

                    searchStart = matchStart + 1;
                }

                return false;
            });
        }

        private static bool IsMicrosoftTaskPath(string path)
        {
            var normalized = path.Replace('/', '\\').ToLowerInvariant();
            return normalized.Contains("\\tasks\\microsoft\\windows\\", StringComparison.Ordinal);
        }
        
        private AdwareSeverity DetermineSeverity(string name)
        {
            var highRiskPatterns = new[] { "toolbar", "hijacker", "searchprotect" };
            
            if (highRiskPatterns.Any(p => name.Contains(p)))
                return AdwareSeverity.High;
            
            return AdwareSeverity.Medium;
        }
    }
    
    public class AdwareItem
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public AdwareType Type { get; set; }
        public AdwareSeverity Severity { get; set; }
    }
    
    public enum AdwareType
    {
        Directory,
        RegistryKey,
        File
    }
    
    public enum AdwareSeverity
    {
        Low,
        Medium,
        High
    }
    
    public class AdwareDetectedEventArgs : EventArgs
    {
        public string Name { get; set; }
        public string Path { get; set; }
    }
}
