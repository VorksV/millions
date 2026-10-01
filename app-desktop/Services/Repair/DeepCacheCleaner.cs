using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace VoltrisOptimizer.Services.Repair
{
    public class DeepCacheCleaner
    {
        private readonly ILoggingService _logger;

        public Action<string, string>? OnLog { get; set; }
        public Action<int, string>? OnProgress { get; set; }

        public DeepCacheCleaner(ILoggingService logger)
        {
            _logger = logger;
        }

        private void Log(string msg, string color = "#AAAAAA")
        {
            _logger.LogDebug($"[DeepCacheCleaner] {msg}", source: "DeepCacheCleaner");
            OnLog?.Invoke(msg, color);
        }

        private void Progress(int pct, string msg)
        {
            OnProgress?.Invoke(pct, msg);
        }

        public async Task<AdvancedRepairStepResult> CleanCacheAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var stepName = LocalizationService.Instance.GetString("Repair_DeepCache_Name");
            var step = new AdvancedRepairStepResult { StepName = stepName };
            
            Log($"═══ {LocalizationService.Instance.GetString("Repair_DeepCache_Title")} ═══", "#00BFFF");
            Progress(10, LocalizationService.Instance.GetString("Repair_DeepCache_Progress"));

            long totalFreed = 0;
            int totalFiles = 0;

            try
            {
                var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var tempPath = Path.GetTempPath();

                // 1. Pastas Temporárias Padrão
                Progress(20, LocalizationService.Instance.GetString("RepairProgress_Cache_TempDirs"));
                var tempFolders = new[]
                {
                    tempPath,
                    Path.Combine(winDir, "Temp"),
                    Path.Combine(winDir, "Prefetch"),
                    Path.Combine(winDir, "SoftwareDistribution", "Download"),
                    Path.Combine(localAppData, "Temp")
                };

                foreach (var folder in tempFolders)
                {
                    ct.ThrowIfCancellationRequested();
                    if (Directory.Exists(folder))
                    {
                        var (freed, count) = SafeDeleteDirectoryContents(folder);
                        totalFreed += freed;
                        totalFiles += count;
                    }
                }
                Log($"  ✓ Arquivos temporários e Prefetch limpos.", "#00FF88");

                // 1b. Cache do Windows Installer (somente itens órfãos/seguros)
                Progress(68, LocalizationService.Instance.GetString("RepairProgress_Cache_Installer"));
                var installerDir = Path.Combine(winDir, "Installer");
                var referencedInstaller = GetReferencedInstallerFiles();
                int installerCount = 0;
                if (Directory.Exists(installerDir))
                {
                    installerCount = CleanInstallerFolder(installerDir, referencedInstaller, ref totalFreed);
                    totalFiles += installerCount;
                }
                Log(installerCount > 0
                    ? $"  ✓ Cache do Instalador limpo ({installerCount} item(ns) órfãos removidos)."
                    : "  ✓ Cache do Instalador verificado — nenhum item órfão encontrado.", "#00FF88");

                // 2. Caches de Shader (NVIDIA, AMD, DirectX)
                Progress(40, LocalizationService.Instance.GetString("RepairProgress_Cache_GPUCache"));
                var shaderFolders = new[]
                {
                    Path.Combine(localAppData, "NVIDIA", "DXCache"),
                    Path.Combine(localAppData, "NVIDIA", "GLCache"),
                    Path.Combine(localAppData, "AMD", "DxCache"),
                    Path.Combine(localAppData, "AMD", "GLCache"),
                    Path.Combine(localAppData, "D3DSCache")
                };

                foreach (var folder in shaderFolders)
                {
                    ct.ThrowIfCancellationRequested();
                    if (Directory.Exists(folder))
                    {
                        var (freed, count) = SafeDeleteDirectoryContents(folder);
                        totalFreed += freed;
                        totalFiles += count;
                    }
                }
                Log($"  ✓ Caches de Shader (GPU) esvaziados.", "#00FF88");

                // 3. Delivery Optimization e Memory Dumps
                Progress(60, LocalizationService.Instance.GetString("RepairProgress_Cache_DeliveryOptimization"));
                var deliveryOptPath = Path.Combine(winDir, "ServiceProfiles", "NetworkService", "AppData", "Local", "Microsoft", "Windows", "DeliveryOptimization", "Cache");
                if (Directory.Exists(deliveryOptPath))
                {
                    var (freed, count) = SafeDeleteDirectoryContents(deliveryOptPath);
                    totalFreed += freed;
                    totalFiles += count;
                }

                var dumpPath = Path.Combine(winDir, "Minidump");
                if (Directory.Exists(dumpPath))
                {
                    var (freed, count) = SafeDeleteDirectoryContents(dumpPath);
                    totalFreed += freed;
                    totalFiles += count;
                }
                Log($"  ✓ Dumps e Otimização de Entrega limpos.", "#00FF88");

                // 4. DNS Cache Flush
                Progress(75, LocalizationService.Instance.GetString("RepairProgress_Cache_DNS"));
                await RunHiddenProcessAsync("ipconfig.exe", "/flushdns", ct);
                Log($"  ✓ Cache DNS resetado.", "#00FF88");

                // 5. WinSxS Cleanup (Pode ser demorado, limitamos o tempo)
                Progress(85, LocalizationService.Instance.GetString("RepairProgress_Cache_WinSxS"));
                Log("  → Executando DISM StartComponentCleanup (isso pode levar alguns minutos)...", "#AAAAAA");
                
                // Rode assincronamente com timeout de 3 minutos
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    cts.CancelAfter(180_000); // 3 mins timeout
                    try
                    {
                        await RunHiddenProcessAsync("dism.exe", "/Online /Cleanup-Image /StartComponentCleanup", cts.Token);
                        Log($"  ✓ WinSxS compactado.", "#00FF88");
                    }
                    catch (OperationCanceledException)
                    {
                        Log($"  ⚠ WinSxS Cleanup ignorado (timeout/cancelado).", "#FFA500");
                    }
                }

                Progress(100, LocalizationService.Instance.GetString("RepairProgress_Cache_Complete"));

                step.Success = true;
                step.Summary = $"Caches limpos. {(totalFreed / 1048576.0):F2} MB liberados imediatamente.";
                step.Details.Add($"Total de arquivos órfãos/temporários removidos: {totalFiles}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[DeepCacheCleaner] Erro na limpeza profunda: {ex.Message}");
                step.Success = false;
                step.Summary = LocalizationService.Instance.GetString("Repair_DeepCache_Warn");
                step.Details.Add(ex.Message);
            }

            step.Duration = sw.Elapsed;
            return step;
        }

        private static bool IsInstallerFilePath(string path)
        {
            try
            {
                var ext = Path.GetExtension(path);
                return string.Equals(ext, ".msi", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(ext, ".msp", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(ext, ".exe", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static HashSet<string> GetReferencedInstallerFiles()
        {
            var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var baseKey = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Installer");
                if (baseKey == null) return referenced;

                using (var userData = baseKey.OpenSubKey("UserData"))
                {
                    if (userData != null)
                    {
                        foreach (var sid in userData.GetSubKeyNames())
                        {
                            try
                            {
                                using var sidKey = userData.OpenSubKey(sid);
                                using var products = sidKey?.OpenSubKey("Products");
                                if (products == null) continue;
                                foreach (var prod in products.GetSubKeyNames())
                                {
                                    try
                                    {
                                        using var prodKey = products.OpenSubKey(prod);
                                        using var installProps = prodKey?.OpenSubKey("InstallProperties");
                                        if (installProps == null) continue;
                                        foreach (var valName in installProps.GetValueNames())
                                        {
                                            if (valName.Equals("LocalPackage", StringComparison.OrdinalIgnoreCase)
                                                && installProps.GetValue(valName) is string p && !string.IsNullOrWhiteSpace(p))
                                                referenced.Add(p);
                                        }
                                    }
                                    catch { }
                                }
                            }
                            catch { }
                        }
                    }
                }

                using (var patches = baseKey.OpenSubKey("Patches"))
                {
                    if (patches != null)
                    {
                        foreach (var patch in patches.GetSubKeyNames())
                        {
                            try
                            {
                                using var patchKey = patches.OpenSubKey(patch);
                                if (patchKey == null) continue;
                                foreach (var valName in patchKey.GetValueNames())
                                {
                                    if (patchKey.GetValue(valName) is string s && IsInstallerFilePath(s))
                                        referenced.Add(s);
                                }
                                using var sourceList = patchKey.OpenSubKey("SourceList");
                                if (sourceList == null) continue;
                                foreach (var sub in sourceList.GetSubKeyNames())
                                {
                                    try
                                    {
                                        using var subKey = sourceList.OpenSubKey(sub);
                                        if (subKey == null) continue;
                                        foreach (var valName in subKey.GetValueNames())
                                        {
                                            if (subKey.GetValue(valName) is string s2 && IsInstallerFilePath(s2))
                                                referenced.Add(s2);
                                        }
                                    }
                                    catch { }
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }
            return referenced;
        }

        private static bool IsFileLocked(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return false;
            }
            catch { return true; }
        }

        private static int CleanInstallerFolder(string installerDir, HashSet<string> referenced, ref long freed)
        {
            int count = 0;
            var cutoff = DateTime.Now.AddDays(-7);
            try
            {
                foreach (var file in Directory.EnumerateFiles(installerDir, "*", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        if (referenced.Contains(file)) continue;        // usado por produto/patch instalado
                        if (fi.LastWriteTime > cutoff) continue;        // muito recente (instalação ativa/recente)
                        if (IsFileLocked(file)) continue;               // arquivo em uso
                        fi.Delete();
                        freed += fi.Length;
                        count++;
                    }
                    catch { }
                }
            }
            catch { }
            return count;
        }

        private (long freed, int count) SafeDeleteDirectoryContents(string dirPath)
        {
            long freed = 0;
            int count = 0;
            try
            {
                foreach (var file in Directory.EnumerateFiles(dirPath, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        if (fi.Exists)
                        {
                            long length = fi.Length;
                            fi.Delete();
                            freed += length;
                            count++;
                        }
                    }
                    catch { } // Ignore in-use files
                }
            }
            catch { }
            return (freed, count);
        }

        private async Task RunHiddenProcessAsync(string exe, string args, CancellationToken ct)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                await proc.WaitForExitAsync(ct);
            }
        }
    }
}
