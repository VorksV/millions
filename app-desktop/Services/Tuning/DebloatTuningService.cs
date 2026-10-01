using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services.Uninstaller;

namespace VoltrisOptimizer.Services.Tuning
{
    public class DebloatTuningService : IDebloatTuningService
    {
        private readonly ILoggingService _logger;
        private readonly UninstallEngine _uninstallEngine;

        private static readonly HashSet<string> CriticalApps = new(StringComparer.OrdinalIgnoreCase)
        {
            "Microsoft.Windows.ShellExperienceHost",
            "Microsoft.Windows.StartMenuExperienceHost",
            "MicrosoftWindows.Client.CBS",
            "Microsoft.AccountsControl",
            "Microsoft.BioEnrollment",
            "Microsoft.Windows.AppResolverUX",
            "Microsoft.LockApp",
            "Microsoft.Windows.CallingShellApp",
            "Microsoft.Windows.ParentalControls",
            "Microsoft.Windows.PeopleExperienceHost",
            "Microsoft.Windows.XGpuEjectDialog",
            "Microsoft.XboxGameCallableUI",
            "Microsoft.Windows.Apprep.ChxApp",
            "Microsoft.Windows.AssignedAccessLockApp",
            "Windows.CBSPreview",
            "Windows.PrintDialog",
            "Microsoft.Windows.NarratorQuickStart", // Win11 24H2+
            "Microsoft.Windows.PrintQueueActionCenter" // Win11 24H2+
        };

        public DebloatTuningService(ILoggingService logger)
        {
            _logger = logger;
            _uninstallEngine = new UninstallEngine(logger);
        }

        public async Task<List<AppInfo>> GetInstalledAppsAsync(bool uninstallableOnly)
        {
            var apps = new List<AppInfo>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                _logger.LogInfo("[Debloat] ═══════════════════════════════════════════════════════");
                _logger.LogInfo($"[Debloat] ESCANEANDO APLICATIVOS INSTALADOS (uninstallableOnly={uninstallableOnly})");
                _logger.LogInfo("[Debloat] ═══════════════════════════════════════════════════════");
                
                // ETAPA 3: Execução paralela de UWP e Win32
                _logger.LogDebug("[Debloat] Iniciando scan paralelo: UWP + Win32...");
                var uwpTask = GetUwpAppsAsync(uninstallableOnly);
                var win32Task = Task.Run(() => _uninstallEngine.GetInstalledPrograms());

                await Task.WhenAll(uwpTask, win32Task);

                var uwpApps = await uwpTask;
                var win32Apps = await win32Task;

                _logger.LogInfo($"[Debloat] UWP apps encontrados: {uwpApps.Count}");
                _logger.LogInfo($"[Debloat] Win32 apps encontrados: {win32Apps.Count}");

                apps.AddRange(uwpApps);
                apps.AddRange(win32Apps);

                var finalApps = apps.OrderByDescending(a => !string.IsNullOrEmpty(a.UninstallString))
                           .GroupBy(a => a.Name.ToLower().Trim())
                           .Select(g => g.First())
                           .OrderBy(a => a.Name)
                           .ToList();

                // Background loading for icons (Fire and Forget)
                _ = Task.Run(async () =>
                {
                    var iconTasks = finalApps.Select(async app =>
                    {
                        try
                        {
                            var icon = await AppIconExtractor.ExtractLogoPathAsync(app);
                            if (!string.IsNullOrEmpty(icon))
                            {
                                app.IconPath = icon;
                            }
                        }
                        catch { }
                    });

                    await Task.WhenAll(iconTasks);
                });

                _logger.LogInfo($"[Debloat] Escaneamento concluído: {finalApps.Count} aplicativos encontrados em {sw.ElapsedMilliseconds}ms");
                return finalApps;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[Debloat] ❌ Erro ao listar aplicativos: {ex.Message}", ex);
                return apps;
            }
        }

        public async Task<bool> UninstallAppAsync(string appName, bool isWin32, CancellationToken cancellationToken = default)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                _logger.LogInfo($"[Debloat] ═══════════════════════════════════════════════════════");
                _logger.LogInfo($"[Debloat] INICIANDO DESINSTALAÇÃO: {appName}");
                _logger.LogInfo($"[Debloat]   Tipo: {(isWin32 ? "Win32" : "UWP/MSIX")}");
                _logger.LogInfo($"[Debloat]   Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                _logger.LogInfo($"[Debloat] ═══════════════════════════════════════════════════════");

                // Verificar cancelamento antes de iniciar
                cancellationToken.ThrowIfCancellationRequested();

                var trimmedAppName = appName?.Trim();
                if (string.IsNullOrEmpty(trimmedAppName)) return false;

                if (CriticalApps.Contains(trimmedAppName))
                {
                    _logger.LogWarning($"[Debloat] ⚠ PROTEÇÃO ATIVA: '{trimmedAppName}' é um componente crítico do Windows. Remoção BLOQUEADA para segurança do sistema.");
                    return false;
                }

                if (isWin32)
                {
                    _logger.LogInfo($"[Debloat] [Win32] Buscando '{trimmedAppName}' no registro...");
                    var app = _uninstallEngine.GetInstalledPrograms()
                                .FirstOrDefault(a => a.Name.Equals(trimmedAppName, StringComparison.OrdinalIgnoreCase));

                    if (app != null)
                    {
                        _logger.LogInfo($"[Debloat] [Win32] App encontrado: Name='{app.Name}', UninstallString='{app.UninstallString}'");
                        var result = await _uninstallEngine.PerformProfessionalUninstall(app);
                        _logger.LogInfo($"[Debloat] [Win32] Resultado: {(result ? "SUCESSO" : "FALHA")} em {sw.ElapsedMilliseconds}ms");
                        return result;
                    }
                    else
                    {
                        _logger.LogError($"[Debloat] [Win32] App '{trimmedAppName}' NÃO encontrado no registro.");
                        return false;
                    }
                }
                else
                {
                    // ── ETAPA 1: Listar pacotes existentes antes da remoção ──
                    _logger.LogInfo($"[Debloat] [UWP] ── ETAPA 1: Verificando pacotes existentes ──");
                    var listCmd = $"Get-AppxPackage -AllUsers -Name '{trimmedAppName}' | Select-Object Name,PackageFullName,Status | Format-List";
                    var existingPackages = await RunPowerShellAsync(listCmd);
                    _logger.LogInfo($"[Debloat] [UWP] Pacotes encontrados:\n{(string.IsNullOrWhiteSpace(existingPackages) ? "(NENHUM)" : existingPackages.Trim())}");

                    // ── ETAPA 2: Remover pacote provisionado ──
                    _logger.LogInfo($"[Debloat] [UWP] ── ETAPA 2: Removendo pacote provisionado ──");
                    cancellationToken.ThrowIfCancellationRequested();
                    var cmdProvisioned = $"Get-AppxProvisionedPackage -Online | Where-Object {{ $_.DisplayName -eq '{trimmedAppName}' }} | ForEach-Object {{ Remove-AppxProvisionedPackage -Online -PackageName $_.PackageName -ErrorAction SilentlyContinue; Write-Output \"Removed provisioned: $($_.PackageName)\" }}";
                    var provOutput = await RunPowerShellAsync(cmdProvisioned);
                    if (!string.IsNullOrWhiteSpace(provOutput)) _logger.LogInfo($"[Debloat] [UWP] DISM-Appx output: {provOutput.Trim()}");

                    // ── ETAPA 3: Remover pacote do usuário ──
                    _logger.LogInfo($"[Debloat] [UWP] ── ETAPA 3: Removendo pacote AppxPackage ──");
                    cancellationToken.ThrowIfCancellationRequested();
                    var cmdAppx = $"Get-AppxPackage -AllUsers | Where-Object {{ $_.Name -eq '{trimmedAppName}' }} | ForEach-Object {{ Write-Output \"Removendo: $($_.PackageFullName)\"; Remove-AppxPackage -Package $_.PackageFullName -AllUsers -ErrorAction Stop; Write-Output \"Removido: $($_.PackageFullName)\" }}";
                    var appxOutput = await RunPowerShellAsync(cmdAppx);
                    
                    // ── ETAPA 4: Verificação ──
                    var verifyCmd = $"(Get-AppxPackage -AllUsers -Name '{trimmedAppName}' | Measure-Object).Count";
                    var countOutput = (await RunPowerShellAsync(verifyCmd)).Trim();
                    if (countOutput == "0" || string.IsNullOrWhiteSpace(countOutput))
                    {
                        sw.Stop();
                        _logger.LogSuccess($"[Debloat] [UWP] ✅ Aplicativo '{trimmedAppName}' removido em {sw.ElapsedMilliseconds}ms");
                        return true;
                    }

                    // Fallback winget inteligente com detecção multi-diretório
                    _logger.LogWarning($"[Debloat] [UWP] ⚠ Remanescente encontrado (count={countOutput}). Tentando winget...");

                    string wingetPath = "winget"; // Default PATH
                    var wingetCheck = await RunPowerShellAsync("(Get-Command winget -ErrorAction SilentlyContinue).Source", silent: true);
                    
                    if (string.IsNullOrWhiteSpace(wingetCheck))
                    {
                        // Matriz de possíveis caminhos para winget (User e System)
                        var possiblePaths = new[]
                        {
                            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "winget.exe"),
                            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps", "Microsoft.DesktopAppInstaller*\\\\winget.exe")
                        };

                        foreach (var pathPattern in possiblePaths)
                        {
                            if (pathPattern.Contains("*"))
                            {
                                // Resolver wildcards para diretórios da WindowsApps (requer permissão, mas o winget user-mode costuma estar no LocalAppData)
                                try {
                                    var parent = Path.GetDirectoryName(pathPattern);
                                    if (Directory.Exists(parent)) {
                                        var found = Directory.GetFiles(parent, "winget.exe", SearchOption.AllDirectories).FirstOrDefault();
                                        if (found != null) { wingetPath = $"\"{found}\""; break; }
                                    }
                                } catch { }
                            }
                            else if (File.Exists(pathPattern))
                            {
                                wingetPath = $"\"{pathPattern}\"";
                                break;
                            }
                        }
                        
                        if (wingetPath == "winget") { wingetPath = null; } // Não encontrado
                    }

                    if (wingetPath != null)
                    {
                        _logger.LogInfo($"[Debloat] [Winget] Utilizando: {wingetPath}");
                        var wingetCmd = $"{wingetPath} uninstall --id '{trimmedAppName}' --silent --accept-source-agreements --disable-interactivity 2>&1";
                        var wingetOutput = await RunPowerShellAsync(wingetCmd);
                         _logger.LogInfo($"[Debloat] [Winget] Resposta: {(string.IsNullOrWhiteSpace(wingetOutput) ? "(sem output)" : wingetOutput.Trim())}");
                    }
                    else
                    {
                        _logger.LogWarning($"[Debloat] [Winget] Fallback ignorado: winget.exe não localizado no sistema.");
                    }

                    // Verificação final
                    countOutput = (await RunPowerShellAsync(verifyCmd)).Trim();
                    _logger.LogInfo($"[Debloat] [Winget] Contagem final de pacotes: '{countOutput}'");

                    if (countOutput == "0" || string.IsNullOrWhiteSpace(countOutput))
                    {
                        sw.Stop();
                        _logger.LogSuccess($"[Debloat] ✅ Aplicativo '{appName}' removido via winget em {sw.ElapsedMilliseconds}ms");
                        return true;
                    }

                    // ── ETAPA 6: Último recurso - DISM ──
                    _logger.LogWarning($"[Debloat] [DISM] Tentando remoção via DISM como último recurso...");
                    var dismCmd = $"Get-AppxProvisionedPackage -Online | Where-Object {{ $_.DisplayName -eq '{appName}' }} | ForEach-Object {{ $pkg = $_.PackageName; Write-Output \"DISM removendo provisionado: $pkg\"; dism /Online /Remove-ProvisionedAppxPackage /PackageName:$pkg 2>&1 }}; Get-AppxPackage -AllUsers -Name '{appName}' | ForEach-Object {{ Write-Output \"Removendo AppxPackage: $($_.PackageFullName)\"; Remove-AppxPackage -Package $_.PackageFullName -AllUsers -ErrorAction SilentlyContinue 2>&1 }}";
                    var dismOutput = await RunPowerShellAsync(dismCmd);
                    _logger.LogInfo($"[Debloat] [DISM] Output: {(string.IsNullOrWhiteSpace(dismOutput) ? "(vazio)" : dismOutput.Trim())}");

                    // Verificação final após DISM
                    countOutput = (await RunPowerShellAsync(verifyCmd)).Trim();
                    if (countOutput == "0" || string.IsNullOrWhiteSpace(countOutput))
                    {
                        sw.Stop();
                        _logger.LogSuccess($"[Debloat] ✅ Aplicativo '{appName}' removido via DISM em {sw.ElapsedMilliseconds}ms");
                        return true;
                    }

                    sw.Stop();

                    // WHITELIST de pacotes protegidos do Windows: estes NUNCA podem ser removidos por design
                    // (são componentes críticos do sistema operacional). São identificados por:
                    //   1) PackageFamilyName/Marca de sistema (Microsoft.StorePurchaseApp, Microsoft.WindowsStore, etc.)
                    //   2) FullName no formato GUID (ex.: "E2A4F912-2574-4A75-9BB0-0D023378592B") — pacotes provisionados
                    //      que o Windows marca como protegidos (não remoção permitida).
                    // Identificar aqui permite logar como AVISO (não-fatal) em vez de FALHA TOTAL (fatal), pois
                    // tentar remover um pacote deste tipo SEMPRE vai falhar (comportamento ES PERADO do Windows).
                    var isProtectedPackage =
                        trimmedAppName.StartsWith("microsoft.", StringComparison.OrdinalIgnoreCase) &&
                        (trimmedAppName.Contains("StorePurchaseApp", StringComparison.OrdinalIgnoreCase) ||
                         trimmedAppName.Contains("WindowsStore", StringComparison.OrdinalIgnoreCase) ||
                         trimmedAppName.Contains("Microsoft.VCLibs", StringComparison.OrdinalIgnoreCase) ||
                         trimmedAppName.Contains("Microsoft.Advertising", StringComparison.OrdinalIgnoreCase) ||
                         trimmedAppName.Contains("Microsoft.NET.Native", StringComparison.OrdinalIgnoreCase)) ||
                        trimmedAppName.Length == 36 && trimmedAppName[8] == '-' && trimmedAppName[13] == '-' && trimmedAppName[18] == '-' && trimmedAppName[23] == '-' && trimmedAppName.IndexOf('-', 0) > 0;

                    if (isProtectedPackage)
                    {
                        _logger.LogWarning($"[Debloat] ⚠ AVISO (não-fatal): '{appName}' parece ser um PACOTE PROTEGIDO do Windows (componente de sistema não removível por design). {sw.ElapsedMilliseconds}ms");
                        _logger.LogWarning($"[Debloat] ⚠ Motivo: pacotes de sistema (ex.: Store, VCLibs, Advertising, NET.Native ou FullName-GUID) NÃO podem ser removidos. Isto NÃO é uma falha do app.");
                        _logger.LogWarning($"[Debloat] ⚠ Pacotes restantes: {countOutput}");
                        return false; // sinaliza "tentado mas bloqueado por design" (o wrapper trata como warning)
                    }

                    _logger.LogError($"[Debloat] ❌ FALHA TOTAL: Aplicativo '{appName}' NÃO pôde ser removido após todas as tentativas ({sw.ElapsedMilliseconds}ms)");
                    _logger.LogError($"[Debloat] ❌ Métodos tentados: Remove-AppxPackage, winget (--id e --name), DISM");
                    _logger.LogError($"[Debloat] ❌ Pacotes restantes: {countOutput}");

                    // Log detalhado do estado final
                    var finalState = await RunPowerShellAsync(listCmd);
                    _logger.LogError($"[Debloat] ❌ Estado final dos pacotes:\n{(string.IsNullOrWhiteSpace(finalState) ? "(nenhum)" : finalState.Trim())}");

                    return false;
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError($"[Debloat] ❌ EXCEÇÃO FATAL na desinstalação de '{appName}': {ex.Message}");
                _logger.LogError($"[Debloat] ❌ StackTrace: {ex.StackTrace}");
                return false;
            }
        }

        public async Task<bool> RemoveTempFilesAsync()
        {
            try
            {
                _logger.LogInfo("Limpando arquivos temporários do sistema...");
                var tempPath = Path.GetTempPath();
                var winTempPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");

                await Task.Run(() => {
                    DeleteFiles(tempPath);
                    DeleteFiles(winTempPath);
                });

                _logger.LogSuccess("Limpeza de temporários concluída.");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("Erro ao limpar temporários", ex);
                return false;
            }
        }

        private void DeleteFiles(string path)
        {
            if (!Directory.Exists(path)) return;
            var di = new DirectoryInfo(path);
            foreach (var file in di.GetFiles()) { try { file.Delete(); } catch { } }
            foreach (var dir in di.GetDirectories()) { try { dir.Delete(true); } catch { } }
        }

        private async Task<List<AppInfo>> GetUwpAppsAsync(bool uninstallableOnly)
        {
            var apps = new List<AppInfo>();
            try
            {
                var command = uninstallableOnly
                    ? "Get-AppxPackage | Where-Object { $_.NonRemovable -eq $false } | Select-Object Name,InstallLocation | Format-List"
                    : "Get-AppxPackage | Select-Object Name,InstallLocation | Format-List";

                _logger.LogDebug($"[Debloat] [UWP-Scan] Comando: {command}");
                var output = await RunPowerShellAsync(command);
                _logger.LogDebug($"[Debloat] [UWP-Scan] Output length: {output?.Length ?? 0} chars");
                
                string currentName = null;
                string currentLocation = null;

                foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.StartsWith("Name"))
                    {
                        if (!string.IsNullOrEmpty(currentName))
                        {
                             if (!CriticalApps.Contains(currentName))
                             {
                                 apps.Add(new AppInfo { Name = currentName, InstallLocation = currentLocation, IsWin32 = false });
                             }
                        }

                        var parts = line.Split(new[] { ':' }, 2);
                        if (parts.Length == 2) currentName = parts[1].Trim();
                        currentLocation = null;
                    }
                    else if (line.StartsWith("InstallLocation"))
                    {
                        var parts = line.Split(new[] { ':' }, 2);
                        if (parts.Length == 2) currentLocation = parts[1].Trim();
                    }
                    else if (!string.IsNullOrWhiteSpace(currentLocation) && line.StartsWith(" "))
                    {
                        currentLocation += " " + line.Trim();
                    }
                }

                if (!string.IsNullOrEmpty(currentName))
                {
                    if (!CriticalApps.Contains(currentName))
                    {
                        apps.Add(new AppInfo { Name = currentName, InstallLocation = currentLocation, IsWin32 = false });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Debloat] [UWP-Scan] ❌ Erro ao obter apps UWP: {ex.Message}");
                _logger.LogDebug($"[Debloat] [UWP-Scan] StackTrace: {ex.StackTrace}");
            }
            return apps;
        }

        private async Task<string> RunPowerShellAsync(string command, bool silent = false)
        {
            var psPath = Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysNative", "WindowsPowerShell", "v1.0", "powershell.exe")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");

            _logger.LogDebug($"[Debloat] [PS] Executando: {psPath}");
            _logger.LogDebug($"[Debloat] [PS] Comando: {command}");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = psPath,
                    Arguments = $"-NoProfile -NonInteractive -Command \"{command}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            process.Start();
            _logger.LogDebug($"[Debloat] [PS] Processo iniciado PID={process.Id}");

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            sw.Stop();
            
            var output = await outputTask;
            var error = await errorTask;
            
            _logger.LogDebug($"[Debloat] [PS] ExitCode={process.ExitCode} | Tempo={sw.ElapsedMilliseconds}ms");
            
            if (!string.IsNullOrWhiteSpace(output))
            {
                _logger.LogDebug($"[Debloat] [PS] Stdout ({output.Length} chars): {output.Trim().Substring(0, Math.Min(output.Trim().Length, 500))}");
            }
            
            if (!string.IsNullOrWhiteSpace(error) && !silent)
            {
                if (process.ExitCode != 0)
                {
                    _logger.LogError($"[Debloat] [PS] ❌ Stderr (ExitCode={process.ExitCode}): {error.Trim()}");
                }
                else
                {
                    _logger.LogWarning($"[Debloat] [PS] Stderr (ExitCode=0, não-fatal): {error.Trim()}");
                }
            }
            
            return output;
        }
    }
}
