using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace VoltrisUninstaller.Core
{
    /// <summary>
    /// Classe principal que executa a desinstalação
    /// </summary>
    public class Uninstaller
    {
        private readonly ILogger _logger;
        private readonly UninstallOptions _options;

        private const string ProductName = "Voltris Optimizer";
        private const string RegistryKeyName = "VoltrisOptimizer";
        private const string CompanyName = "Voltris Corporation";
        private const string TAG = "[Uninstaller]";
        // Constants for Windows 11 icon revert
        private const string SHELL_ICONS_KEY = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons";
        private const string CLSID_BASE_KEY = @"Software\Microsoft\Windows\CurrentVersion\Explorer\CLSID";
        private const string RECYCLE_BIN_CLSID = "{645FF040-5081-101B-9F08-00AA002F954E}";
        private const string THIS_PC_CLSID = "{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
        private const string NETWORK_CLSID = "{208D2C60-3AEA-1069-A2D7-08002B30309D}";

        // DllImports for Explorer refresh
        [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);
        private const int SHCNE_ASSOCCHANGED = 0x08000000;
        private const int SHCNF_IDLIST = 0x0000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, string lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);
        private const uint WM_SETTINGCHANGE = 0x1A;
        private const uint SMTO_ABORTIFHUNG = 0x0002;
        private static readonly IntPtr HWND_BROADCAST = new IntPtr(0xffff);

        public Uninstaller(ILogger logger, UninstallOptions options)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// Executa a desinstalação completa
        /// </summary>
public async Task<UninstallResult> ExecuteAsync(IProgress<UninstallProgress>? progress = null)
        {
            // Fast‑path for dry‑run: skip all side‑effects and return a successful result.
            if (_options.DryRun)
            {
                _logger.LogInfo("[Uninstaller] Dry‑run mode – skipping actual uninstallation steps.");
                return new UninstallResult { Success = true };
            }

            var result = new UninstallResult { Success = false };
            string? installPath = null;

            try
            {
                _logger.LogInfo("=== INÍCIO DA DESINSTALAÇÃO ===");
                _logger.LogInfo($"Modo: {(_options.Silent ? "Silent" : "Interativo")}");
                _logger.LogInfo($"Manter dados do usuário: {_options.KeepUserData}");

                progress?.Report(new UninstallProgress { Step = Services.LocalizationService.Instance.GetString("ProgressDetecting"), Percent = 5 });
                installPath = DetectInstallLocation();

                if (string.IsNullOrEmpty(installPath))
                {
                    _logger.LogWarning("Instalação não detectada. Continuando limpeza de artefatos...");
                }
                else
                {
                    _logger.LogInfo($"Instalação detectada em: {installPath}");
                }

                progress?.Report(new UninstallProgress { Step = Services.LocalizationService.Instance.GetString("ProgressTerminatingProcesses"), Percent = 20 });
                await StopProcessesAsync(installPath);

                progress?.Report(new UninstallProgress { Step = Services.LocalizationService.Instance.GetString("ProgressRemovingServices"), Percent = 30 });
                await RemoveServicesAsync();

                progress?.Report(new UninstallProgress { Step = Services.LocalizationService.Instance.GetString("ProgressRemovingTasks"), Percent = 35 });
                await RemoveScheduledTasksAsync();

                progress?.Report(new UninstallProgress { Step = Services.LocalizationService.Instance.GetString("ProgressRemovingShortcuts"), Percent = 40 });
                await RemoveShortcutsAsync();

                bool isWindows11 = Environment.OSVersion.Version.Build >= 22000;
                if (!isWindows11)
                {
                    progress?.Report(new UninstallProgress { Step = Services.LocalizationService.Instance.GetString("ProgressRevertingIcons"), Percent = 45 });
                    await RevertWindowsIconsAsync();
                }
                else
                {
                    _logger.LogInfo("Windows 11 detectado. Pulando a reversão de ícones (nativos do SO).");
                }

                progress?.Report(new UninstallProgress { Step = Services.LocalizationService.Instance.GetString("ProgressRemovingVisualIntegration"), Percent = 48 });
                await RemoveVoltrisBlurAsync();

                progress?.Report(new UninstallProgress { Step = Services.LocalizationService.Instance.GetString("ProgressRemovingRegistry"), Percent = 50 });
                RemoveRegistryEntries();

                progress?.Report(new UninstallProgress { Step = Services.LocalizationService.Instance.GetString("ProgressRemovingFiles"), Percent = 60 });
                await RemoveInstallationFoldersAsync(installPath, progress);

                progress?.Report(new UninstallProgress { Step = Services.LocalizationService.Instance.GetString("ProgressRemovingSystemData"), Percent = 75 });
                await RemoveProgramDataAsync();

                if (!_options.KeepUserData)
                {
                    progress?.Report(new UninstallProgress { Step = Services.LocalizationService.Instance.GetString("ProgressRemovingUserData"), Percent = 80 });
                    await RemoveUserDataAsync();
                }

                progress?.Report(new UninstallProgress { Step = Services.LocalizationService.Instance.GetString("ProgressCreatingCleanupScript"), Percent = 88 });
                await CreateFinalCleanupScriptAsync(installPath);
                
                progress?.Report(new UninstallProgress { Step = Services.LocalizationService.Instance.GetString("ProgressFinalizing"), Percent = 90 });
                await Task.Delay(500);

                progress?.Report(new UninstallProgress { Step = Services.LocalizationService.Instance.GetString("ProgressDone"), Percent = 100 });
                _logger.LogInfo("=== DESINSTALAÇÃO CONCLUÍDA COM SUCESSO ===");
                
                result.Success = true;
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro durante a desinstalação", ex);
                result.ErrorMessage = ex.Message;
                result.Success = false;
                return result;
            }
        }

        private string? DetectInstallLocation()
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
                using var key = baseKey.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{RegistryKeyName}");
                
                if (key != null)
                {
                    var installLocation = key.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrEmpty(installLocation) && Directory.Exists(installLocation))
                    {
                        return installLocation;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Erro ao detectar localização: {ex.Message}");
            }
            
            return null;
        }

        private async Task StopProcessesAsync(string? installPath)
        {
            await Task.Run(() =>
            {
                try
                {
                    var processes = Process.GetProcesses()
                        .Where(p => 
                            p.ProcessName.Contains("VoltrisOptimizer", StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrEmpty(installPath) && 
                             !string.IsNullOrEmpty(p.MainModule?.FileName) &&
                             p.MainModule.FileName.StartsWith(installPath, StringComparison.OrdinalIgnoreCase)))
                        .ToList();

                    foreach (var proc in processes)
                    {
                        try
                        {
                            _logger.LogInfo($"Encerrando processo: {proc.ProcessName} (PID: {proc.Id})");
                            proc.Kill();
                            proc.WaitForExit(5000);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Não foi possível encerrar processo {proc.ProcessName}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Erro ao encerrar processos: {ex.Message}");
                }
            });
        }

        private async Task RemoveServicesAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    _logger.LogInfo("Verificando e removendo serviços...");
                    string[] services = { "VoltrisOptimizerService", "VoltrisBackgroundService", "VoltrisMonitor" };
                    foreach (var svc in services)
                    {
                        try
                        {
                            var psiStop = new ProcessStartInfo("sc.exe", $"stop \"{svc}\"") { CreateNoWindow = true, UseShellExecute = false };
                            Process.Start(psiStop)?.WaitForExit(3000);
                            
                            var psiDelete = new ProcessStartInfo("sc.exe", $"delete \"{svc}\"") { CreateNoWindow = true, UseShellExecute = false };
                            Process.Start(psiDelete)?.WaitForExit(3000);
                            _logger.LogInfo($"Serviço {svc} parado e removido (se existia).");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Erro ao remover serviço {svc}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Erro geral ao remover serviços: {ex.Message}");
                }
            });
        }

        private async Task RemoveScheduledTasksAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    _logger.LogInfo("Verificando e removendo tarefas agendadas...");
                    string[] tasks = { "VoltrisOptimizer", "VoltrisOptimizer_Startup", "VoltrisOptimizer_Background" };
                    foreach (var task in tasks)
                    {
                        try
                        {
                            var psi = new ProcessStartInfo("schtasks.exe", $"/delete /tn \"{task}\" /f") { CreateNoWindow = true, UseShellExecute = false };
                            Process.Start(psi)?.WaitForExit(3000);
                            _logger.LogInfo($"Tarefa {task} removida (se existia).");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Erro ao remover tarefa {task}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Erro geral ao remover tarefas agendadas: {ex.Message}");
                }
            });
        }

        private async Task RemoveVoltrisBlurAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    _logger.LogInfo("Removendo injeção do VoltrisBlur.dll do Explorer...");
                    
                    var pfX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                    var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                    
                    var candidates = new List<string> {
                        Path.Combine(pfX86, "Voltris Corporation", "VoltrisOptimizer", "VoltrisBlur", "VoltrisBlur.dll"),
                        Path.Combine(pf, "Voltris Corporation", "VoltrisOptimizer", "VoltrisBlur", "VoltrisBlur.dll"),
                        Path.Combine(pfX86, "VoltrisOptimizer", "VoltrisBlur", "VoltrisBlur.dll"),
                        Path.Combine(pf, "VoltrisOptimizer", "VoltrisBlur", "VoltrisBlur.dll")
                    };
                    
                    foreach(var dll in candidates)
                    {
                        if (File.Exists(dll))
                        {
                            _logger.LogInfo($"Desregistrando DLL: {dll}");
                            var psi = new ProcessStartInfo("regsvr32.exe", $"/u /s \"{dll}\"") { CreateNoWindow = true, UseShellExecute = false };
                            Process.Start(psi)?.WaitForExit(5000);
                        }
                    }

                    // Limpeza bruta de CLSIDs no registro para garantir
                    string[] clsids = { "{887D3A6A-502E-4AF5-9CE6-D515E12AFE89}", "{B44BD3C8-E597-4E08-AE43-246CE24698E7}" };
                    foreach (var clsid in clsids)
                    {
                        try { Registry.ClassesRoot.DeleteSubKeyTree($@"CLSID\{clsid}", false); } catch { }
                        try { Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\CLSID\{clsid}", false); } catch { }
                        try { Registry.LocalMachine.DeleteSubKeyTree($@"SOFTWARE\Classes\CLSID\{clsid}", false); } catch { }
                    }
                    
                    _logger.LogInfo("Remoção do VoltrisBlur concluída.");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Erro ao remover VoltrisBlur: {ex.Message}");
                }
            });
        }

        private async Task RemoveShortcutsAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    var startMenuPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
                    
                    var shortcuts = new List<string>();
                    
                    if (Directory.Exists(desktopPath))
                    {
                        shortcuts.AddRange(Directory.GetFiles(desktopPath, "*Voltris*Optimizer*.lnk"));
                    }
                    
                    if (Directory.Exists(startMenuPath))
                    {
                        shortcuts.AddRange(Directory.GetFiles(startMenuPath, "*Voltris*Optimizer*.lnk", SearchOption.AllDirectories));
                    }
                    
                    foreach (var shortcut in shortcuts)
                    {
                        try
                        {
                            File.Delete(shortcut);
                            _logger.LogInfo($"Atalho removido: {shortcut}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Não foi possível remover atalho {shortcut}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Erro ao remover atalhos: {ex.Message}");
                }
            });
        }

        private void RemoveRegistryEntries()
        {
            try
            {
                _logger.LogInfo("Iniciando remoção completa de entradas de registro...");

                var registryViews = new[] { RegistryView.Registry32, RegistryView.Registry64 };
                var hives = new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser };

                foreach (var hive in hives)
                {
                    foreach (var view in registryViews)
                    {
                        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                        if (baseKey == null) continue;

                        _logger.LogInfo($"Verificando {hive}\\{view}...");

                        // 0. Remover entradas de startup automático
                        RemoveStartupRegistryEntries(hive, view);

                        // 1. Remover chave do ARP (Add/Remove Programs)
                        var uninstallKeyPath = $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{RegistryKeyName}";
                        TryDeleteRegistryKey(baseKey, uninstallKeyPath, "ARP", hive, view);

                        // 2. Remover chaves de software da empresa e produto
                        var companyKeyPaths = new[]
                        {
                            $@"SOFTWARE\{CompanyName}",
                            $@"SOFTWARE\{ProductName}",
                            $@"SOFTWARE\Voltris", // Para pegar pastas genéricas
                            $@"SOFTWARE\Voltris Optimizer" // Para pegar variações
                        };

                        foreach (var keyPath in companyKeyPaths)
                        {
                            TryDeleteRegistryKey(baseKey, keyPath, "Software", hive, view);
                        }

                        // 3. Buscar por chaves que contenham "Voltris" no nome e remover
                        var softwareKey = baseKey.OpenSubKey(@"SOFTWARE", true);
                        if (softwareKey != null)
                        {
                            foreach (var subKeyName in softwareKey.GetSubKeyNames())
                            {
                                if (subKeyName.Contains("Voltris", StringComparison.OrdinalIgnoreCase))
                                {
                                    TryDeleteRegistryKey(softwareKey, subKeyName, "Software (Search)", hive, view);
                                }
                            }
                            softwareKey.Close();
                        }
                    }
                }
                _logger.LogInfo("Remoção de entradas de registro concluída.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Erro ao remover entradas de registro: {ex.Message}");
            }
        }

private void TryDeleteRegistryKey(RegistryKey parentKey, string subKeyName, string description, RegistryHive hive, RegistryView view)
        {
            try
            {
                parentKey.DeleteSubKeyTree(subKeyName, false);
                _logger.LogInfo($"Chave de registro removida ({description}): {hive}\\{parentKey.Name}\\{subKeyName} ({view})");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Não foi possível remover chave de registro ({description}) {hive}\\{parentKey.Name}\\{subKeyName} ({view}): {ex.Message}");
            }


        }
        /// <summary>
        /// Remove entradas de startup automático do registro
        /// </summary>
        private void RemoveStartupRegistryEntries(RegistryHive hive, RegistryView view)
        {
            try
            {
                _logger.LogInfo($"Removendo entradas de startup para {hive}\\{view}...");
                
                // Remover da chave Run
                using var runKey = RegistryKey.OpenBaseKey(hive, view)
                    .OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                
                if (runKey != null)
                {
                    var startupNames = new[] { "Voltris Optimizer", "VoltrisOptimizer" };
                    foreach (var name in startupNames)
                    {
                        try
                        {
                            if (runKey.GetValue(name) != null)
                            {
                                runKey.DeleteValue(name, false);
                                _logger.LogInfo($"Entrada de startup removida: {name}");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Erro ao remover entrada de startup '{name}': {ex.Message}");
                        }
                    }
                    runKey.Close();
                }
                
                // Remover da chave StartupApproved
                using var approvedKey = RegistryKey.OpenBaseKey(hive, view)
                    .OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", true);
                
                if (approvedKey != null)
                {
                    var startupNames = new[] { "Voltris Optimizer", "VoltrisOptimizer" };
                    foreach (var name in startupNames)
                    {
                        try
                        {
                            if (approvedKey.GetValue(name) != null)
                            {
                                approvedKey.DeleteValue(name, false);
                                _logger.LogInfo($"Entrada de StartupApproved removida: {name}");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Erro ao remover entrada de StartupApproved '{name}': {ex.Message}");
                        }
                    }
                    approvedKey.Close();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Erro ao remover entradas de startup: {ex.Message}");
            }
        }

        // ---------------------
        // Windows 11 icon revert helpers (copied from Windows11IconsService)
        // ---------------------
        private void ClearIconCache()
        {
            _logger.LogInfo($"{TAG} Limpando o cache de ícones do Windows (IconCache.db)...");
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string iconCacheDir = Path.Combine(localAppData, "Microsoft", "Windows", "Explorer");
                if (!Directory.Exists(iconCacheDir))
                {
                    _logger.LogWarning($"{TAG} Diretório do cache de ícones não encontrado: {iconCacheDir}");
                    return;
                }
                // Encerrar Explorer temporariamente
                try
                {
                    using var killExplorer = new System.Diagnostics.Process();
                    killExplorer.StartInfo.FileName = "taskkill.exe";
                    killExplorer.StartInfo.Arguments = "/F /IM explorer.exe";
                    killExplorer.StartInfo.UseShellExecute = false;
                    killExplorer.StartInfo.CreateNoWindow = true;
                    killExplorer.Start();
                    killExplorer.WaitForExit(3000);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"{TAG} Não foi possível encerrar o Explorer: {ex.Message}");
                }
                System.Threading.Thread.Sleep(500);
                var cacheFiles = Directory.GetFiles(iconCacheDir, "iconcache*.db", SearchOption.TopDirectoryOnly)
                    .Concat(Directory.GetFiles(iconCacheDir, "thumbcache*.db", SearchOption.TopDirectoryOnly))
                    .ToArray();
                int deleted = 0;
                foreach (var file in cacheFiles)
                {
                    try
                    {
                        File.Delete(file);
                        deleted++;
                        _logger.LogDebug($"{TAG} Cache deletado: {Path.GetFileName(file)}");
                    }
                    catch { }
                }
                _logger.LogInfo($"{TAG} {deleted} arquivo(s) de cache de ícones deletados.");
                // Reiniciar Explorer
                System.Threading.Thread.Sleep(300);
                try
                {
                    using var startExplorer = new System.Diagnostics.Process();
                    startExplorer.StartInfo.FileName = Path.Combine(Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows", "explorer.exe");
                    startExplorer.StartInfo.UseShellExecute = false;
                    startExplorer.Start();
                    _logger.LogInfo($"{TAG} Explorer reiniciado com sucesso após limpeza do cache.");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"{TAG} Não foi possível reiniciar o Explorer automaticamente: {ex.Message}");
                }
                System.Threading.Thread.Sleep(1500);
            }
            catch (Exception ex)
            {
                _logger.LogError($"{TAG} Erro ao limpar cache de ícones: {ex.Message}", ex);
            }
        }

        private void RefreshExplorer()
        {
            _logger.LogInfo($"{TAG} Enviando flush de ícones para o Explorer...");
            try
            {
                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
                SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero, "Environment", SMTO_ABORTIFHUNG, 5000, out _);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"{TAG} Falha ao notificar Explorer: {ex.Message}");
            }
        }

        private async Task RevertWindowsIconsAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    // 1. Deletar a chave HKLM Shell Icons
                    try { Registry.LocalMachine.DeleteSubKeyTree(SHELL_ICONS_KEY, false); }
                    catch { }
                    // 2. Remover overlays 27/29 de HKCU Shell Icons
                    try
                    {
                        using var hkcuShell = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons", true);
                        if (hkcuShell != null)
                        {
                            foreach (var val in new[] { "27", "29" })
                            {
                                try { hkcuShell.DeleteValue(val, false); } catch { }
                            }
                        }
                    }
                    catch { }
                    // 3. Remover CLSIDs personalizados (Lixeira, Este PC, Rede) em HKCU
                    foreach (var guid in new[] { RECYCLE_BIN_CLSID, THIS_PC_CLSID, NETWORK_CLSID })
                    {
                        try { Registry.CurrentUser.DeleteSubKeyTree($"{CLSID_BASE_KEY}\\{guid}\\DefaultIcon", false); } catch { }
                        try { Registry.CurrentUser.DeleteSubKeyTree($"Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\CLSID\\{guid}\\DefaultIcon", false); } catch { }
                    }
                    // 4. Remover CLSIDs de pastas de usuário e Quick Access
                    var userFolderGuids = new[]
                    {
                        "{B4BFCC3A-DB2C-424C-B029-7FE99A87C641}",
                        "{d3162b92-9365-467a-956b-92703aca08af}",
                        "{088e3905-0323-4b02-9826-5d99428e115f}",
                        "{3dfdf296-dbec-4fb4-81d1-6a3438bcf4de}",
                        "{24ad3ad4-a569-4530-98e1-ab02f9417aa8}",
                        "{f86fa3ab-70d2-4fc7-9c99-fcbf05467f3a}",
                        "{0DB7E03F-FC29-4DC6-9020-FF41B59E513A}",
                        "{679f85cb-0220-4080-b29b-5540cc05aab6}"
                    };
                    foreach (var guid in userFolderGuids)
                    {
                        try { Registry.CurrentUser.DeleteSubKeyTree($"Software\\Classes\\CLSID\\{guid}", false); } catch { }
                        try { Registry.CurrentUser.DeleteSubKeyTree($"Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\CLSID\\{guid}", false); } catch { }
                    }
                    // 5. Limpar desktop.ini overrides nas pastas padrão
                    var paths = new[]
                    {
                        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
                    };
                    foreach (var p in paths)
                    {
                        var ini = Path.Combine(p, "desktop.ini");
                        if (File.Exists(ini))
                        {
                            try
                            {
                                File.SetAttributes(ini, File.GetAttributes(ini) & ~FileAttributes.ReadOnly & ~FileAttributes.Hidden & ~FileAttributes.System);
                                var lines = File.ReadAllLines(ini).Where(l =>
                                    !l.StartsWith("IconResource", StringComparison.OrdinalIgnoreCase) &&
                                    !l.StartsWith("IconFile", StringComparison.OrdinalIgnoreCase) &&
                                    !l.StartsWith("IconIndex", StringComparison.OrdinalIgnoreCase)).ToList();
                                File.WriteAllLines(ini, lines);
                                File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System);
                            }
                            catch { }
                        }
                    }
                    // 6. Limpar cache e notificar Explorer
                    ClearIconCache();
                    RefreshExplorer();
                    _logger.LogInfo($"{TAG} Ícones do Windows 11 revertidos ao padrão Windows 10.");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"{TAG} Erro ao reverter ícones: {ex.Message}", ex);
                }
            });
        }




        /// <summary>
        /// Remove todas as pastas de instalação, incluindo variações e pastas em Program Files
        /// </summary>
        private async Task RemoveInstallationFoldersAsync(string? detectedInstallPath, IProgress<UninstallProgress>? progress = null)
        {
            await Task.Run(() =>
            {
                try
                {
                    progress?.Report(new UninstallProgress { Step = Services.LocalizationService.Instance.GetString("ProgressSearchingFolders"), Percent = 60 });
                    // Lista de caminhos possíveis em Program Files
                    var programFilesPaths = new List<string>();
                    
                    // Program Files (x86)
                    try
                    {
                        var pfX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                        if (!string.IsNullOrEmpty(pfX86) && Directory.Exists(pfX86))
                        {
                            programFilesPaths.Add(Path.Combine(pfX86, "Voltris Corporation", "VoltrisOptimizer"));
                            programFilesPaths.Add(Path.Combine(pfX86, "Voltris Corporation", "VoltrisOptmizer")); // Com typo
                            programFilesPaths.Add(Path.Combine(pfX86, "VoltrisOptimizer"));
                            programFilesPaths.Add(Path.Combine(pfX86, "VoltrisOptmizer")); // Com typo
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Erro ao obter Program Files (x86): {ex.Message}");
                    }

                    // Program Files (64-bit)
                    try
                    {
                        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                        if (!string.IsNullOrEmpty(pf) && Directory.Exists(pf))
                        {
                            programFilesPaths.Add(Path.Combine(pf, "Voltris Corporation", "VoltrisOptimizer"));
                            programFilesPaths.Add(Path.Combine(pf, "Voltris Corporation", "VoltrisOptmizer")); // Com typo
                            programFilesPaths.Add(Path.Combine(pf, "VoltrisOptimizer"));
                            programFilesPaths.Add(Path.Combine(pf, "VoltrisOptmizer")); // Com typo
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Erro ao obter Program Files: {ex.Message}");
                    }

                    // Adicionar o caminho detectado se existir
                    if (!string.IsNullOrEmpty(detectedInstallPath) && Directory.Exists(detectedInstallPath))
                    {
                        if (!programFilesPaths.Contains(detectedInstallPath, StringComparer.OrdinalIgnoreCase))
                        {
                            programFilesPaths.Add(detectedInstallPath);
                        }
                    }

                    // Buscar agressivamente por pastas "Voltris" em Program Files
                    foreach (var pfBase in new[] 
                    { 
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
                    })
                    {
                        if (string.IsNullOrEmpty(pfBase) || !Directory.Exists(pfBase))
                            continue;

                        try
                        {
                            // Buscar em "Voltris Corporation"
                            var voltrisCorpPath = Path.Combine(pfBase, "Voltris Corporation");
                            if (Directory.Exists(voltrisCorpPath))
                            {
                                var subDirs = Directory.GetDirectories(voltrisCorpPath);
                                foreach (var subDir in subDirs)
                                {
                                    var dirName = Path.GetFileName(subDir);
                                    if (dirName.Contains("Voltris", StringComparison.OrdinalIgnoreCase) ||
                                        dirName.Contains("Optimizer", StringComparison.OrdinalIgnoreCase) ||
                                        dirName.Contains("Optmizer", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (!programFilesPaths.Contains(subDir, StringComparer.OrdinalIgnoreCase))
                                        {
                                            programFilesPaths.Add(subDir);
                                        }
                                    }
                                }
                            }

                            // Buscar diretamente por pastas que começam com "Voltris"
                            var directDirs = Directory.GetDirectories(pfBase, "Voltris*", SearchOption.TopDirectoryOnly);
                            foreach (var dir in directDirs)
                            {
                                if (!programFilesPaths.Contains(dir, StringComparer.OrdinalIgnoreCase))
                                {
                                    programFilesPaths.Add(dir);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Erro ao buscar pastas em {pfBase}: {ex.Message}");
                        }
                    }

                    // Remover todas as pastas encontradas de forma mais eficiente
                    // Nota: Se chegamos aqui, o desinstalador já foi movido para temp (se necessário) no Program.cs
                    int totalPaths = programFilesPaths.Count;
                    int currentPath = 0;
                    
                    foreach (var path in programFilesPaths)
                    {
                        currentPath++;
                        var pathProgress = 60 + (int)((currentPath / (double)totalPaths) * 10); // 60-70%
                        progress?.Report(new UninstallProgress { Step = string.Format(Services.LocalizationService.Instance.GetString("ProgressRemovingFolder"), currentPath, totalPaths), Percent = pathProgress });
                        
                        if (Directory.Exists(path))
                        {
                            try
                            {
                                _logger.LogInfo($"Removendo pasta de instalação: {path}");
                                
                                // Remover de forma mais rápida e direta (máximo 5 tentativas para garantir)
                                RemoveDirectoryRecursiveFast(path, maxRetries: 5);
                                
                                // Se ainda existe, tentar método alternativo
                                if (Directory.Exists(path))
                                {
                                    _logger.LogWarning($"Pasta ainda existe, tentando método alternativo: {path}");
                                    ForceRemoveDirectory(path, maxRetries: 3);
                                }
                                
                                // Se AINDA existe, tentar remover arquivos individualmente
                                if (Directory.Exists(path))
                                {
                                    _logger.LogWarning($"Pasta ainda existe após métodos normais, tentando remoção agressiva: {path}");
                                    RemoveDirectoryAggressive(path);
                                }
                                
                                // Tentar remover a pasta pai "Voltris Corporation" de forma mais agressiva
                                var parentDir = Path.GetDirectoryName(path);
                                if (!string.IsNullOrEmpty(parentDir) && 
                                    parentDir.EndsWith("Voltris Corporation", StringComparison.OrdinalIgnoreCase))
                                {
                                    RemoveVoltrisCorporationFolder(parentDir);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning($"Não foi possível remover pasta {path}: {ex.Message}");
                            }
                        }
                    }
                    
                    // Remoção final garantida da pasta "Voltris Corporation" em Program Files
                    progress?.Report(new UninstallProgress { Step = Services.LocalizationService.Instance.GetString("ProgressRemovingMainFolder"), Percent = 70 });
                    RemoveVoltrisCorporationFoldersFromProgramFiles();
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Erro ao remover pastas de instalação: {ex.Message}", ex);
                }
            });
        }

        /// <summary>
        /// Remove a pasta "Voltris Corporation" de forma agressiva
        /// </summary>
        private void RemoveVoltrisCorporationFolder(string parentDir)
        {
            if (string.IsNullOrEmpty(parentDir) || !Directory.Exists(parentDir))
                return;

            try
            {
                _logger.LogInfo($"Tentando remover pasta 'Voltris Corporation': {parentDir}");
                
                // Verificar se está vazia
                var entries = Directory.EnumerateFileSystemEntries(parentDir).ToList();
                if (entries.Count == 0)
                {
                    // Está vazia, tentar remover
                    for (int attempt = 1; attempt <= 5; attempt++)
                    {
                        try
                        {
                            var dirInfo = new DirectoryInfo(parentDir);
                            dirInfo.Attributes = FileAttributes.Normal;
                            Directory.Delete(parentDir, false);
                            _logger.LogInfo($"Pasta 'Voltris Corporation' removida: {parentDir}");
                            return;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Tentativa {attempt}: Não foi possível remover pasta 'Voltris Corporation' {parentDir}: {ex.Message}");
                            if (attempt < 5)
                            {
                                System.Threading.Thread.Sleep(500 * attempt);
                            }
                        }
                    }
                }
                else
                {
                    // Não está vazia, tentar remover conteúdo primeiro
                    _logger.LogInfo($"Pasta 'Voltris Corporation' não está vazia, removendo conteúdo primeiro...");
                    foreach (var entry in entries)
                    {
                        try
                        {
                            if (File.Exists(entry))
                            {
                                File.SetAttributes(entry, FileAttributes.Normal);
                                File.Delete(entry);
                            }
                            else if (Directory.Exists(entry))
                            {
                                RemoveDirectoryRecursiveFast(entry, maxRetries: 3);
                                if (Directory.Exists(entry))
                                {
                                    ForceRemoveDirectory(entry, maxRetries: 2);
                                }
                            }
                        }
                        catch { }
                    }
                    
                    // Tentar remover novamente
                    entries = Directory.EnumerateFileSystemEntries(parentDir).ToList();
                    if (entries.Count == 0)
                    {
                        for (int attempt = 1; attempt <= 5; attempt++)
                        {
                            try
                            {
                                var dirInfo = new DirectoryInfo(parentDir);
                                dirInfo.Attributes = FileAttributes.Normal;
                                Directory.Delete(parentDir, false);
                                _logger.LogInfo($"Pasta 'Voltris Corporation' removida após limpeza: {parentDir}");
                                return;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning($"Tentativa {attempt}: Não foi possível remover pasta 'Voltris Corporation' após limpeza: {ex.Message}");
                                if (attempt < 5)
                                {
                                    System.Threading.Thread.Sleep(500 * attempt);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Erro ao remover pasta 'Voltris Corporation' {parentDir}: {ex.Message}");
            }
        }

        /// <summary>
        /// Remove todas as pastas "Voltris Corporation" de Program Files
        /// </summary>
        private void RemoveVoltrisCorporationFoldersFromProgramFiles()
        {
            try
            {
                var programFilesPaths = new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
                };

                foreach (var pfBase in programFilesPaths)
                {
                    if (string.IsNullOrEmpty(pfBase) || !Directory.Exists(pfBase))
                        continue;

                    var voltrisCorpPath = Path.Combine(pfBase, "Voltris Corporation");
                    if (Directory.Exists(voltrisCorpPath))
                    {
                        _logger.LogInfo($"Removendo pasta 'Voltris Corporation' de: {voltrisCorpPath}");
                        RemoveVoltrisCorporationFolder(voltrisCorpPath);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Erro ao remover pastas 'Voltris Corporation' de Program Files: {ex.Message}");
            }
        }

        private async Task RemoveUserDataAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    var basePaths = new[]
                    {
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
                    };

                    // Lista expandida incluindo variações com typos
                    var folderNames = new[]
                    {
                        "Voltris",
                        "Voltris Corporation",
                        "Voltris Optimizer",
                        "VoltrisOptimizer", // Sem espaço
                        "VoltrisOptmizer", // Com typo (sem 'i')
                        "Voltris Optmizer", // Com typo e espaço
                        CompanyName,
                        ProductName
                    };

                    foreach (var basePath in basePaths)
                    {
                        if (string.IsNullOrEmpty(basePath) || !Directory.Exists(basePath))
                            continue;

                        // Método 1: Remover pastas conhecidas
                        foreach (var folderName in folderNames)
                        {
                            var path = Path.Combine(basePath, folderName);
                            if (Directory.Exists(path))
                            {
                                try
                                {
                                    RemoveDirectoryRecursive(path, maxRetries: 5);
                                    RemoveParentDirectoriesIfEmpty(path);
                                    _logger.LogInfo($"Dados do usuário removidos: {path}");
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning($"Não foi possível remover dados do usuário em {path}: {ex.Message}");
                                }
                            }
                        }

                        // Método 2: Busca agressiva - procurar TODAS as pastas que contenham "Voltris"
                        try
                        {
                            var allDirs = Directory.GetDirectories(basePath, "*", SearchOption.TopDirectoryOnly);
                            foreach (var dir in allDirs)
                            {
                                var dirName = Path.GetFileName(dir);
                                
                                // Verificar se contém "Voltris" ou variações
                                if (dirName.Contains("Voltris", StringComparison.OrdinalIgnoreCase) ||
                                    (dirName.Contains("Optimizer", StringComparison.OrdinalIgnoreCase) && 
                                     dirName.Contains("Opt", StringComparison.OrdinalIgnoreCase)) ||
                                    dirName.Contains("Optmizer", StringComparison.OrdinalIgnoreCase))
                                {
                                    // Verificar se já não foi removida
                                    if (Directory.Exists(dir))
                                    {
                                        try
                                        {
                                            _logger.LogInfo($"Removendo pasta encontrada por busca: {dir}");
                                            RemoveDirectoryRecursive(dir, maxRetries: 5);
                                            RemoveParentDirectoriesIfEmpty(dir);
                                            _logger.LogInfo($"Dados do usuário removidos (busca): {dir}");
                                        }
                                        catch (Exception ex)
                                        {
                                            _logger.LogWarning($"Não foi possível remover pasta encontrada {dir}: {ex.Message}");
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Erro ao buscar pastas em {basePath}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Erro ao remover dados do usuário: {ex.Message}");
                }
            });
        }

        private void RemoveDirectoryRecursive(string directory, int maxRetries = 5)
        {
            if (!Directory.Exists(directory))
                return;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    // Primeiro, remover todos os arquivos com tratamento melhorado
                    var files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        try
                        {
                            // Remover atributos de somente leitura, oculto, sistema
                            File.SetAttributes(file, FileAttributes.Normal);
                            
                            // Tentar desbloquear arquivo se estiver em uso
                            try
                            {
                                using (var fs = File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.Delete))
                                {
                                    fs.Close();
                                }
                            }
                            catch { }
                            
                            File.Delete(file);
                            _logger.LogInfo($"Arquivo removido: {file}");
                        }
                        catch (UnauthorizedAccessException)
                        {
                            // Tentar tomar posse do arquivo
                            try
                            {
                                var fileInfo = new FileInfo(file);
                                fileInfo.Attributes = FileAttributes.Normal;
                                File.Delete(file);
                                _logger.LogInfo($"Arquivo removido (após ajuste de atributos): {file}");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning($"Tentativa {attempt}: Não foi possível remover arquivo {file}: {ex.Message}");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Tentativa {attempt}: Não foi possível remover arquivo {file}: {ex.Message}");
                        }
                    }

                    // Depois, remover todos os diretórios (do mais profundo para o mais raso)
                    var dirs = Directory.GetDirectories(directory, "*", SearchOption.AllDirectories)
                        .OrderByDescending(d => d.Length)
                        .ToList();
                    
                    foreach (var dir in dirs)
                    {
                        try
                        {
                            // Remover atributos do diretório
                            var dirInfo = new DirectoryInfo(dir);
                            dirInfo.Attributes = FileAttributes.Normal;
                            
                            Directory.Delete(dir, true);
                            _logger.LogInfo($"Diretório removido: {dir}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Tentativa {attempt}: Não foi possível remover diretório {dir}: {ex.Message}");
                        }
                    }

                    // Finalmente, remover o diretório raiz
                    try
                    {
                        var rootDirInfo = new DirectoryInfo(directory);
                        rootDirInfo.Attributes = FileAttributes.Normal;
                        
                        Directory.Delete(directory, true);
                        _logger.LogInfo($"Diretório raiz removido: {directory}");
                        return; // Sucesso, sair do loop
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Tentativa {attempt}: Não foi possível remover diretório raiz {directory}: {ex.Message}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Tentativa {attempt}: Erro geral ao remover diretório {directory}: {ex.Message}");
                }
                
                // Esperar antes de tentar novamente (reduzido para ser mais rápido)
                System.Threading.Thread.Sleep(500 * attempt);
            }
            _logger.LogError($"Falha ao remover diretório {directory} após {maxRetries} tentativas.");
        }

        /// <summary>
        /// Versão otimizada e mais rápida de remoção recursiva
        /// </summary>
        private void RemoveDirectoryRecursiveFast(string directory, int maxRetries = 3)
        {
            if (!Directory.Exists(directory))
                return;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    // Tentar remover diretamente primeiro (mais rápido)
                    try
                    {
                        Directory.Delete(directory, true);
                        _logger.LogInfo($"Diretório removido rapidamente: {directory}");
                        return;
                    }
                    catch { }

                    // Se falhar, remover arquivos e diretórios manualmente
                    var files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        try
                        {
                            File.SetAttributes(file, FileAttributes.Normal);
                            File.Delete(file);
                        }
                        catch { }
                    }

                    var dirs = Directory.GetDirectories(directory, "*", SearchOption.AllDirectories)
                        .OrderByDescending(d => d.Length)
                        .ToList();
                    
                    foreach (var dir in dirs)
                    {
                        try
                        {
                            Directory.Delete(dir, true);
                        }
                        catch { }
                    }

                    // Tentar remover raiz novamente
                    try
                    {
                        Directory.Delete(directory, true);
                        _logger.LogInfo($"Diretório removido: {directory}");
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Tentativa {attempt}: Não foi possível remover diretório {directory}: {ex.Message}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Tentativa {attempt}: Erro geral ao remover diretório {directory}: {ex.Message}");
                }
                
                // Esperar menos tempo (200ms por tentativa)
                System.Threading.Thread.Sleep(200 * attempt);
            }
        }

        /// <summary>
        /// Remove pastas em ProgramData\Voltris
        /// </summary>
        private async Task RemoveProgramDataAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    var programDataPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                    if (string.IsNullOrEmpty(programDataPath) || !Directory.Exists(programDataPath))
                        return;

                    var voltrisPaths = new[]
                    {
                        Path.Combine(programDataPath, "Voltris"),
                        Path.Combine(programDataPath, "Voltris Corporation"),
                        Path.Combine(programDataPath, "VoltrisOptimizer"),
                        Path.Combine(programDataPath, "VoltrisOptmizer")
                    };

                    foreach (var path in voltrisPaths)
                    {
                        if (Directory.Exists(path))
                        {
                            try
                            {
                                _logger.LogInfo($"Removendo ProgramData: {path}");
                                RemoveDirectoryRecursiveFast(path, maxRetries: 3);
                                
                                if (Directory.Exists(path))
                                {
                                    ForceRemoveDirectory(path, maxRetries: 2);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning($"Não foi possível remover ProgramData {path}: {ex.Message}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Erro ao remover ProgramData: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Remove o conteúdo de um diretório exceto o próprio desinstalador
        /// </summary>
        private void RemoveDirectoryContentsExceptSelf(string directory, string excludeFile)
        {
            if (!Directory.Exists(directory))
                return;

            try
            {
                var excludeFileName = Path.GetFileName(excludeFile);
                var excludeDir = Path.GetDirectoryName(excludeFile);
                
                // Remover todos os arquivos exceto o desinstalador
                var files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    try
                    {
                        // Pular o arquivo do desinstalador
                        if (string.Equals(file, excludeFile, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        
                        File.SetAttributes(file, FileAttributes.Normal);
                        File.Delete(file);
                        _logger.LogInfo($"Arquivo removido: {file}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Não foi possível remover arquivo {file}: {ex.Message}");
                    }
                }

                // Remover todos os diretórios exceto o que contém o desinstalador
                var dirs = Directory.GetDirectories(directory, "*", SearchOption.AllDirectories)
                    .OrderByDescending(d => d.Length)
                    .ToList();

                foreach (var dir in dirs)
                {
                    try
                    {
                        // Pular o diretório que contém o desinstalador
                        if (!string.IsNullOrEmpty(excludeDir) && 
                            dir.StartsWith(excludeDir, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        
                        var dirInfo = new DirectoryInfo(dir);
                        dirInfo.Attributes = FileAttributes.Normal;
                        Directory.Delete(dir, true);
                        _logger.LogInfo($"Diretório removido: {dir}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Não foi possível remover diretório {dir}: {ex.Message}");
                    }
                }
                
                _logger.LogInfo($"Conteúdo da pasta removido (exceto desinstalador): {directory}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Erro ao remover conteúdo da pasta {directory}: {ex.Message}");
            }
        }

        /// <summary>
        /// Método ultra-agressivo para remover diretório (último recurso)
        /// </summary>
        private void RemoveDirectoryAggressive(string directory)
        {
            if (!Directory.Exists(directory))
                return;

            try
            {
                _logger.LogInfo($"Tentando remoção agressiva de: {directory}");
                
                // Tentar remover todos os arquivos com múltiplas tentativas
                var files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    for (int i = 0; i < 5; i++)
                    {
                        try
                        {
                            // Remover atributos
                            var fileInfo = new FileInfo(file);
                            fileInfo.Attributes = FileAttributes.Normal;
                            
                            // Tentar desbloquear
                            try
                            {
                                using (var fs = File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.Delete))
                                {
                                    fs.Close();
                                }
                            }
                            catch { }
                            
                            File.Delete(file);
                            break; // Sucesso
                        }
                        catch
                        {
                            if (i < 4)
                            {
                                System.Threading.Thread.Sleep(200);
                            }
                        }
                    }
                }

                // Tentar remover diretórios
                var dirs = Directory.GetDirectories(directory, "*", SearchOption.AllDirectories)
                    .OrderByDescending(d => d.Length)
                    .ToList();

                foreach (var dir in dirs)
                {
                    try
                    {
                        var dirInfo = new DirectoryInfo(dir);
                        dirInfo.Attributes = FileAttributes.Normal;
                        Directory.Delete(dir, true);
                    }
                    catch { }
                }

                // Tentar remover raiz
                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        var rootInfo = new DirectoryInfo(directory);
                        rootInfo.Attributes = FileAttributes.Normal;
                        Directory.Delete(directory, true);
                        _logger.LogInfo($"Diretório removido agressivamente: {directory}");
                        return;
                    }
                    catch
                    {
                        if (i < 4)
                        {
                            System.Threading.Thread.Sleep(300);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Erro na remoção agressiva de {directory}: {ex.Message}");
            }
        }

        /// <summary>
        /// Método alternativo para forçar remoção de diretório com mais agressividade (otimizado)
        /// </summary>
        private void ForceRemoveDirectory(string directory, int maxRetries = 2)
        {
            if (!Directory.Exists(directory))
                return;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    // Tentar remover todos os arquivos primeiro
                    var allFiles = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
                    foreach (var file in allFiles)
                    {
                        try
                        {
                            File.SetAttributes(file, FileAttributes.Normal);
                            File.Delete(file);
                        }
                        catch { }
                    }

                    // Tentar remover diretórios
                    var allDirs = Directory.GetDirectories(directory, "*", SearchOption.AllDirectories)
                        .OrderByDescending(d => d.Length)
                        .ToList();

                    foreach (var dir in allDirs)
                    {
                        try
                        {
                            var dirInfo = new DirectoryInfo(dir);
                            dirInfo.Attributes = FileAttributes.Normal;
                            Directory.Delete(dir, true);
                        }
                        catch { }
                    }

                    // Tentar remover diretório raiz
                    try
                    {
                        var rootInfo = new DirectoryInfo(directory);
                        rootInfo.Attributes = FileAttributes.Normal;
                        Directory.Delete(directory, true);
                        _logger.LogInfo($"Diretório removido com método alternativo: {directory}");
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Tentativa {attempt} (método alternativo): Não foi possível remover {directory}: {ex.Message}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Tentativa {attempt} (método alternativo): Erro geral: {ex.Message}");
                }

                // Esperar menos tempo (300ms por tentativa)
                System.Threading.Thread.Sleep(300 * attempt);
            }
        }

        private void RemoveParentDirectoriesIfEmpty(string path)
        {
            try
            {
                var currentDir = Path.GetDirectoryName(path);
                while (!string.IsNullOrEmpty(currentDir) && Directory.Exists(currentDir))
                {
                    if (!Directory.EnumerateFileSystemEntries(currentDir).Any())
                    {
                        try
                        {
                            Directory.Delete(currentDir, false);
                            _logger.LogInfo($"Diretório pai vazio removido: {currentDir}");
                            currentDir = Path.GetDirectoryName(currentDir);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Não foi possível remover diretório pai vazio {currentDir}: {ex.Message}");
                            break;
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Erro ao remover diretórios pais vazios: {ex.Message}");
            }
        }

        /// <summary>
        /// Cria um script batch agressivo para remover a pasta de instalação após o desinstalador fechar
        /// </summary>
        private async Task CreateFinalCleanupScriptAsync(string? installPath)
        {
            await Task.Run(() =>
            {
                try
                {
                    // Obter todas as pastas que precisam ser removidas
                    var pathsToRemove = new List<string>();
                    
                    if (!string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
                    {
                        pathsToRemove.Add(installPath);
                    }
                    
                    // Adicionar caminhos padrão
                    var pfX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                    if (!string.IsNullOrEmpty(pfX86))
                    {
                        var defaultPath = Path.Combine(pfX86, "Voltris Corporation", "VoltrisOptimizer");
                        if (Directory.Exists(defaultPath) && !pathsToRemove.Contains(defaultPath, StringComparer.OrdinalIgnoreCase))
                        {
                            pathsToRemove.Add(defaultPath);
                        }
                    }
                    
                    if (pathsToRemove.Count == 0)
                        return;
                    
                    // Criar script batch na pasta temp
                    var tempDir = Path.GetTempPath();
                    var batchPath = Path.Combine(tempDir, $"VoltrisCleanup_{Guid.NewGuid():N}.bat");
                    
                    var batchContent = new System.Text.StringBuilder();
                    batchContent.AppendLine("@echo off");
                    batchContent.AppendLine("REM Script de limpeza final do Voltris Optimizer");
                    batchContent.AppendLine("REM Aguardar desinstalador fechar completamente");
                    batchContent.AppendLine("timeout /t 2 /nobreak >nul 2>&1");
                    batchContent.AppendLine();
                    
                    foreach (var path in pathsToRemove)
                    {
                        var parentDir = Path.GetDirectoryName(path);
                        var retryLabel = $"retry_{Guid.NewGuid():N}";
                        batchContent.AppendLine($"REM Removendo: {path}");
                        batchContent.AppendLine($"if exist \"{path}\" (");
                        batchContent.AppendLine($"    cd /d \"{path}\"");
                        batchContent.AppendLine($"    del /f /q /a *.* >nul 2>&1");
                        batchContent.AppendLine($"    for /d %%d in (*) do (");
                        batchContent.AppendLine($"        rd /s /q \"%%d\" >nul 2>&1");
                        batchContent.AppendLine($"    )");
                        batchContent.AppendLine($"    cd /d \"{parentDir}\"");
                        batchContent.AppendLine($")");
                        batchContent.AppendLine();
                        batchContent.AppendLine($":{retryLabel}");
                        batchContent.AppendLine($"if exist \"{path}\" (");
                        batchContent.AppendLine($"    rd /s /q \"{path}\" >nul 2>&1");
                        batchContent.AppendLine($"    timeout /t 1 /nobreak >nul 2>&1");
                        batchContent.AppendLine($"    if exist \"{path}\" (");
                        batchContent.AppendLine($"        goto {retryLabel}");
                        batchContent.AppendLine($"    )");
                        batchContent.AppendLine($")");
                        batchContent.AppendLine();
                        
                    }
                    
                    // Remover pasta "Voltris Corporation" de forma agressiva
                    var pfX86ForBatch = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                    if (!string.IsNullOrEmpty(pfX86ForBatch))
                    {
                        var voltrisCorpPath = Path.Combine(pfX86ForBatch, "Voltris Corporation");
                        var retryCorpLabel = $"retry_corp_{Guid.NewGuid():N}";
                        batchContent.AppendLine($"REM Removendo pasta 'Voltris Corporation': {voltrisCorpPath}");
                        batchContent.AppendLine($":{retryCorpLabel}");
                        batchContent.AppendLine($"if exist \"{voltrisCorpPath}\" (");
                        batchContent.AppendLine($"    cd /d \"{voltrisCorpPath}\"");
                        batchContent.AppendLine($"    del /f /q /a *.* >nul 2>&1");
                        batchContent.AppendLine($"    for /d %%d in (*) do (");
                        batchContent.AppendLine($"        rd /s /q \"%%d\" >nul 2>&1");
                        batchContent.AppendLine($"    )");
                        batchContent.AppendLine($"    cd /d \"{pfX86ForBatch}\"");
                        batchContent.AppendLine($"    rd /s /q \"{voltrisCorpPath}\" >nul 2>&1");
                        batchContent.AppendLine($"    timeout /t 1 /nobreak >nul 2>&1");
                        batchContent.AppendLine($"    if exist \"{voltrisCorpPath}\" (");
                        batchContent.AppendLine($"        goto {retryCorpLabel}");
                        batchContent.AppendLine($"    )");
                        batchContent.AppendLine($")");
                        batchContent.AppendLine();
                    }
                    
                    batchContent.AppendLine("REM Remover este script");
                    batchContent.AppendLine("del \"%~f0\" >nul 2>&1");
                    
                    File.WriteAllText(batchPath, batchContent.ToString());
                    
                    // Executar o script em background
                    var processInfo = new ProcessStartInfo
                    {
                        FileName = batchPath,
                        UseShellExecute = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        CreateNoWindow = true
                    };
                    
                    Process.Start(processInfo);
                    _logger.LogInfo($"Script de limpeza final criado e executado: {batchPath}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Erro ao criar script de limpeza final: {ex.Message}");
                }
            });
        }
}
}
