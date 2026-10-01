using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Uninstaller
{
    /// <summary>
    /// FASE 3: Rastreamento profundo de resíduos no sistema
    /// </summary>
    public class ResidualTracer
    {
        private readonly ILoggingService _logger;

        // Pastas críticas do sistema que NUNCA devem ser removidas
        private readonly HashSet<string> _protectedFolders = new(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\Windows",
            @"C:\Windows\System32",
            @"C:\Windows\SysWOW64",
            @"C:\Program Files\Windows",
            @"C:\Program Files (x86)\Windows",
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows)
        };

        public ResidualTracer(ILoggingService logger)
        {
            _logger = logger;
        }

        public async Task<ApplicationResidues> TraceAllResidues(ApplicationProfile profile)
        {
            var residues = new ApplicationResidues();

            // Executar todos os scans em paralelo para melhor performance
            var tasks = new List<Task>
            {
                Task.Run(() => TraceFolders(profile, residues)),
                Task.Run(() => TraceFiles(profile, residues)),
                Task.Run(() => TraceRegistryKeys(profile, residues)),
                Task.Run(() => TraceServices(profile, residues)),
                Task.Run(() => TraceScheduledTasks(profile, residues)),
                Task.Run(() => TraceStartupEntries(profile, residues))
            };

            await Task.WhenAll(tasks);

            // Adicionar processos em execução
            residues.RunningProcesses.AddRange(profile.RunningProcesses);

            return residues;
        }

        private void TraceFolders(ApplicationProfile profile, ApplicationResidues residues)
        {
            try
            {
                _logger.LogInfo("[Tracer] Rastreando pastas residuais (MODO AGRESSIVO)...");

                var searchPaths = new[]
                {
                    // Pastas de programas
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    
                    // AppData do usuário
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow"),
                    
                    // Menu Iniciar - CRÍTICO para remover pastas de atalhos
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Start Menu", "Programs"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "Start Menu", "Programs"),
                    Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                    
                    // Temp
                    Path.GetTempPath(),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp")
                };

                var foundFolders = new List<string>();

                // 1. Adicionar InstallLocation se ainda existir
                if (!string.IsNullOrEmpty(profile.InstallLocation) && 
                    Directory.Exists(profile.InstallLocation) &&
                    !IsProtectedFolder(profile.InstallLocation))
                {
                    foundFolders.Add(profile.InstallLocation);
                    _logger.LogInfo($"[Tracer]   ✓ InstallLocation: {profile.InstallLocation}");
                }

                // 2. Escanear todas as pastas comuns
                foreach (var searchPath in searchPaths.Where(p => !string.IsNullOrEmpty(p) && Directory.Exists(p)))
                {
                    try
                    {
                        _logger.LogInfo($"[Tracer] Escaneando pastas em: {searchPath}");
                        
                        var directories = Directory.GetDirectories(searchPath);
                        
                        foreach (var dir in directories)
                        {
                            if (IsProtectedFolder(dir)) continue;

                            var dirName = Path.GetFileName(dir);

                            // Match EXATO ou CONTÉM o nome do app
                            bool matchApp = dirName.Equals(profile.Name, StringComparison.OrdinalIgnoreCase) ||
                                          dirName.Contains(profile.Name, StringComparison.OrdinalIgnoreCase);
                            
                            if (matchApp)
                            {
                                foundFolders.Add(dir);
                                _logger.LogInfo($"[Tracer]   ✓ Pasta encontrada: {dir}");
                            }
                            // Match por publisher (escanear subpastas)
                            else if (!string.IsNullOrEmpty(profile.Publisher) && 
                                     dirName.Contains(profile.Publisher, StringComparison.OrdinalIgnoreCase))
                            {
                                try
                                {
                                    var subDirs = Directory.GetDirectories(dir);
                                    foreach (var subDir in subDirs)
                                    {
                                        var subDirName = Path.GetFileName(subDir);
                                        if (subDirName.Contains(profile.Name, StringComparison.OrdinalIgnoreCase))
                                        {
                                            foundFolders.Add(subDir);
                                            _logger.LogInfo($"[Tracer]   ✓ Subpasta encontrada: {subDir}");
                                        }
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        _logger.LogWarning($"[Tracer]   ✗ Acesso negado: {searchPath}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"[Tracer]   ✗ Erro ao escanear {searchPath}: {ex.Message}");
                    }
                }

                // Remover duplicatas e adicionar
                lock (residues.Folders)
                {
                    residues.Folders.AddRange(foundFolders.Distinct());
                }

                _logger.LogInfo($"[Tracer] Total de pastas residuais encontradas: {foundFolders.Count}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[Tracer] Erro crítico ao rastrear pastas: {ex.Message}");
            }
        }

        private void TraceFiles(ApplicationProfile profile, ApplicationResidues residues)
        {
            try
            {
                _logger.LogInfo("[Tracer] Rastreando arquivos residuais (MODO AGRESSIVO)...");

                // EXPANSÃO MASSIVA: Todos os locais possíveis de atalhos e arquivos
                var searchPaths = new[]
                {
                    // Desktop do usuário atual
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop"),
                    
                    // Desktop público
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)),
                    
                    // Menu Iniciar do usuário
                    Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Start Menu"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Start Menu", "Programs"),
                    
                    // Menu Iniciar público/todos os usuários
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "Start Menu"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "Start Menu", "Programs"),
                    
                    // Barra de tarefas fixada
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar"),
                    
                    // Inicialização
                    Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
                    
                    // Quick Launch
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Internet Explorer", "Quick Launch"),
                    
                    // SendTo
                    Environment.GetFolderPath(Environment.SpecialFolder.SendTo),
                    
                    // Recent
                    Environment.GetFolderPath(Environment.SpecialFolder.Recent)
                };

                var foundFiles = new List<string>();

                foreach (var searchPath in searchPaths.Where(p => !string.IsNullOrEmpty(p) && Directory.Exists(p)))
                {
                    try
                    {
                        _logger.LogInfo($"[Tracer] Escaneando: {searchPath}");
                        
                        // Procurar TODOS os arquivos relacionados (atalhos, executáveis, etc)
                        var files = Directory.GetFiles(searchPath, "*.*", SearchOption.AllDirectories)
                            .Where(f =>
                            {
                                var fileName = Path.GetFileNameWithoutExtension(f);
                                var fullName = Path.GetFileName(f);
                                
                                // Match por nome do app
                                if (fileName.Contains(profile.Name, StringComparison.OrdinalIgnoreCase) ||
                                    fullName.Contains(profile.Name, StringComparison.OrdinalIgnoreCase))
                                {
                                    return true;
                                }
                                
                                // Match por publisher
                                if (!string.IsNullOrEmpty(profile.Publisher) && 
                                    (fileName.Contains(profile.Publisher, StringComparison.OrdinalIgnoreCase) ||
                                     fullName.Contains(profile.Publisher, StringComparison.OrdinalIgnoreCase)))
                                {
                                    return true;
                                }
                                
                                return false;
                            })
                            .ToList();

                        if (files.Any())
                        {
                            _logger.LogInfo($"[Tracer]   ✓ Encontrados {files.Count} arquivos em {searchPath}");
                            foundFiles.AddRange(files);
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        _logger.LogWarning($"[Tracer]   ✗ Acesso negado: {searchPath}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"[Tracer]   ✗ Erro ao escanear {searchPath}: {ex.Message}");
                    }
                }

                lock (residues.Files)
                {
                    residues.Files.AddRange(foundFiles.Distinct());
                }

                _logger.LogInfo($"[Tracer] Total de arquivos residuais encontrados: {foundFiles.Count}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[Tracer] Erro crítico ao rastrear arquivos: {ex.Message}");
            }
        }

        private void TraceRegistryKeys(ApplicationProfile profile, ApplicationResidues residues)
        {
            try
            {
                _logger.LogInfo("[Tracer] Rastreando chaves de registro residuais...");

                var foundKeys = new List<string>();

                // 1. Adicionar chave de desinstalação se existir
                if (!string.IsNullOrEmpty(profile.RegistryPath))
                {
                    foundKeys.Add(profile.RegistryPath);
                }

                // 2. Escanear SOFTWARE
                var searchPaths = new[]
                {
                    @"SOFTWARE",
                    @"SOFTWARE\WOW6432Node"
                };

                var hives = new[]
                {
                    (Hive: RegistryHive.LocalMachine, Name: "HKEY_LOCAL_MACHINE"),
                    (Hive: RegistryHive.CurrentUser, Name: "HKEY_CURRENT_USER")
                };

                foreach (var (hive, hiveName) in hives)
                {
                    foreach (var searchPath in searchPaths)
                    {
                        try
                        {
                            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default).OpenSubKey(searchPath);
                            if (baseKey == null) continue;

                            foreach (var subKeyName in baseKey.GetSubKeyNames())
                            {
                                // Match direto por nome do app
                                if (subKeyName.Contains(profile.Name, StringComparison.OrdinalIgnoreCase))
                                {
                                    foundKeys.Add($"{hiveName}\\{searchPath}\\{subKeyName}");
                                }
                                // Match por publisher (escanear subchaves)
                                else if (!string.IsNullOrEmpty(profile.Publisher) && 
                                         subKeyName.Contains(profile.Publisher, StringComparison.OrdinalIgnoreCase))
                                {
                                    try
                                    {
                                        using var pubKey = baseKey.OpenSubKey(subKeyName);
                                        if (pubKey != null)
                                        {
                                            foreach (var appSubKey in pubKey.GetSubKeyNames())
                                            {
                                                if (appSubKey.Contains(profile.Name, StringComparison.OrdinalIgnoreCase))
                                                {
                                                    foundKeys.Add($"{hiveName}\\{searchPath}\\{subKeyName}\\{appSubKey}");
                                                }
                                            }
                                        }
                                    }
                                    catch { }
                                }
                            }
                        }
                        catch { }
                    }
                }

                // 3. Verificar chaves de inicialização (Run)
                TraceStartupRegistryKeys(profile, foundKeys);

                lock (residues.RegistryKeys)
                {
                    residues.RegistryKeys.AddRange(foundKeys.Distinct());
                }

                _logger.LogInfo($"[Tracer] Encontradas {foundKeys.Count} chaves de registro residuais.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[Tracer] Erro ao rastrear registro: {ex.Message}");
            }
        }

        private void TraceStartupRegistryKeys(ApplicationProfile profile, List<string> foundKeys)
        {
            var runPaths = new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\RunOnce"
            };

            var hives = new[]
            {
                (Hive: RegistryHive.LocalMachine, Name: "HKEY_LOCAL_MACHINE"),
                (Hive: RegistryHive.CurrentUser, Name: "HKEY_CURRENT_USER")
            };

            foreach (var (hive, hiveName) in hives)
            {
                foreach (var runPath in runPaths)
                {
                    try
                    {
                        using var runKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default).OpenSubKey(runPath);
                        if (runKey == null) continue;

                        foreach (var valueName in runKey.GetValueNames())
                        {
                            if (valueName.Contains(profile.Name, StringComparison.OrdinalIgnoreCase) ||
                                (!string.IsNullOrEmpty(profile.Publisher) && 
                                 valueName.Contains(profile.Publisher, StringComparison.OrdinalIgnoreCase)))
                            {
                                // Não adicionar a chave inteira, apenas marcar para remoção do valor
                                // Isso será tratado separadamente
                            }
                        }
                    }
                    catch { }
                }
            }
        }

        private void TraceServices(ApplicationProfile profile, ApplicationResidues residues)
        {
            try
            {
                _logger.LogInfo("[Tracer] Rastreando serviços residuais...");

                // Já temos os serviços do perfil
                lock (residues.Services)
                {
                    residues.Services.AddRange(profile.RelatedServices);
                }

                _logger.LogInfo($"[Tracer] Encontrados {profile.RelatedServices.Count} serviços residuais.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[Tracer] Erro ao rastrear serviços: {ex.Message}");
            }
        }

        private void TraceScheduledTasks(ApplicationProfile profile, ApplicationResidues residues)
        {
            try
            {
                _logger.LogInfo("[Tracer] Rastreando tarefas agendadas residuais...");

                var foundTasks = new List<string>();

                // Usar schtasks para listar tarefas
                using var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "schtasks.exe",
                        Arguments = "/Query /FO CSV /NH",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    // CSV format: "TaskName","Next Run Time","Status"
                    var parts = line.Split(',');
                    if (parts.Length > 0)
                    {
                        var taskName = parts[0].Trim('"');
                        if (taskName.Contains(profile.Name, StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrEmpty(profile.Publisher) && 
                             taskName.Contains(profile.Publisher, StringComparison.OrdinalIgnoreCase)))
                        {
                            foundTasks.Add(taskName);
                        }
                    }
                }

                lock (residues.ScheduledTasks)
                {
                    residues.ScheduledTasks.AddRange(foundTasks.Distinct());
                }

                _logger.LogInfo($"[Tracer] Encontradas {foundTasks.Count} tarefas agendadas residuais.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[Tracer] Erro ao rastrear tarefas agendadas: {ex.Message}");
            }
        }

        private void TraceStartupEntries(ApplicationProfile profile, ApplicationResidues residues)
        {
            try
            {
                _logger.LogInfo("[Tracer] Rastreando entradas de inicialização residuais...");

                var foundEntries = new List<string>();

                // Verificar pastas de inicialização
                var startupFolders = new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)
                };

                foreach (var folder in startupFolders.Where(Directory.Exists))
                {
                    try
                    {
                        var files = Directory.GetFiles(folder)
                            .Where(f =>
                            {
                                var fileName = Path.GetFileNameWithoutExtension(f);
                                return fileName.Contains(profile.Name, StringComparison.OrdinalIgnoreCase) ||
                                       (!string.IsNullOrEmpty(profile.Publisher) && 
                                        fileName.Contains(profile.Publisher, StringComparison.OrdinalIgnoreCase));
                            })
                            .ToList();

                        foundEntries.AddRange(files);
                    }
                    catch { }
                }

                lock (residues.StartupEntries)
                {
                    residues.StartupEntries.AddRange(foundEntries.Distinct());
                }

                _logger.LogInfo($"[Tracer] Encontradas {foundEntries.Count} entradas de inicialização residuais.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[Tracer] Erro ao rastrear entradas de inicialização: {ex.Message}");
            }
        }

        private bool IsProtectedFolder(string path)
        {
            return _protectedFolders.Any(pf => 
                path.Equals(pf, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(pf + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Resíduos encontrados no sistema
    /// </summary>
    public class ApplicationResidues
    {
        public List<string> Folders { get; set; } = new();
        public List<string> Files { get; set; } = new();
        public List<string> RegistryKeys { get; set; } = new();
        public List<string> Services { get; set; } = new();
        public List<string> ScheduledTasks { get; set; } = new();
        public List<string> StartupEntries { get; set; } = new();
        public List<string> RunningProcesses { get; set; } = new();

        public bool HasResidues =>
            Folders.Any() || Files.Any() || RegistryKeys.Any() ||
            Services.Any() || ScheduledTasks.Any() || StartupEntries.Any();

        public int TotalCount =>
            Folders.Count + Files.Count + RegistryKeys.Count +
            Services.Count + ScheduledTasks.Count + StartupEntries.Count;
    }

    /// <summary>
    /// Resultado da desinstalação avançada
    /// </summary>
    public class UninstallResult
    {
        public string AppName { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public ApplicationProfile? Profile { get; set; }
        public ApplicationResidues? Residues { get; set; }
        public bool CleanupPerformed { get; set; }
    }
}
