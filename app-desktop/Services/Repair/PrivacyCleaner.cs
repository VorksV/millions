using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Repair
{
    public class PrivacyCleaner
    {
        private readonly ILoggingService _logger;

        public Action<string, string>? OnLog { get; set; }
        public Action<int, string>? OnProgress { get; set; }

        public PrivacyCleaner(ILoggingService logger)
        {
            _logger = logger;
        }

        private void Log(string msg, string color = "#AAAAAA")
        {
            _logger.LogDebug($"[PrivacyCleaner] {msg}", source: "PrivacyCleaner");
            OnLog?.Invoke(msg, color);
        }

        private void Progress(int pct, string msg)
        {
            OnProgress?.Invoke(pct, msg);
        }

        public async Task<AdvancedRepairStepResult> CleanPrivacyAsync(CancellationToken ct)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var stepName = LocalizationService.Instance.GetString("Repair_Privacy_Name");
            var step = new AdvancedRepairStepResult { StepName = stepName };
            
            Log($"═══ {LocalizationService.Instance.GetString("Repair_Privacy_Title")} ═══", "#00BFFF");
            Progress(10, LocalizationService.Instance.GetString("Repair_Privacy_Progress"));

            long totalFreed = 0;
            int totalFiles = 0;

            try
            {
                // 1. Limpeza de Navegadores Chromium (Chrome, Edge, Brave, Opera)
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

                var browserPaths = new[]
                {
                    ("Google Chrome", Path.Combine(localAppData, @"Google\Chrome\User Data\Default")),
                    ("Microsoft Edge", Path.Combine(localAppData, @"Microsoft\Edge\User Data\Default")),
                    ("Brave", Path.Combine(localAppData, @"BraveSoftware\Brave-Browser\User Data\Default")),
                    ("Opera", Path.Combine(roamingAppData, @"Opera Software\Opera Stable")),
                    ("Opera GX", Path.Combine(roamingAppData, @"Opera Software\Opera GX Stable"))
                };

                // Pastas alvo para limpeza dentro do perfil do navegador (Seguro)
                var targets = new[] { "Cache", "Code Cache", "GPUCache", "History", "Cookies", "Web Data", "Top Sites", "Visited Links", "Sessions", "Session Storage" };

                int browserPct = 10;
                foreach (var (name, path) in browserPaths)
                {
                    ct.ThrowIfCancellationRequested();
                    Progress(browserPct, $"Limpando privacidade do {name}...");
                    browserPct += 5;

                    if (Directory.Exists(path))
                    {
                        foreach (var target in targets)
                        {
                            var targetPath = Path.Combine(path, target);
                            if (Directory.Exists(targetPath))
                            {
                                var (freed, count) = SafeDeleteDirectoryContents(targetPath);
                                totalFreed += freed;
                                totalFiles += count;
                            }
                            else if (File.Exists(targetPath))
                            {
                                var (freed, count) = SafeDeleteFile(targetPath);
                                totalFreed += freed;
                                totalFiles += count;
                            }
                        }
                        Log($"  ✓ {name}: dados de privacidade limpos.", "#00FF88");
                    }
                }

                // 2. Limpeza Firefox
                Progress(40, LocalizationService.Instance.GetString("RepairProgress_Privacy_Firefox"));
                var firefoxProfiles = Path.Combine(roamingAppData, @"Mozilla\Firefox\Profiles");
                if (Directory.Exists(firefoxProfiles))
                {
                    foreach (var dir in Directory.GetDirectories(firefoxProfiles))
                    {
                        var (f, c) = SafeDeleteDirectoryContents(Path.Combine(dir, "cache2"));
                        var (f2, c2) = SafeDeleteFile(Path.Combine(dir, "places.sqlite")); // History
                        var (f3, c3) = SafeDeleteFile(Path.Combine(dir, "cookies.sqlite")); // Cookies
                        totalFreed += f + f2 + f3;
                        totalFiles += c + c2 + c3;
                    }
                    Log($"  ✓ Firefox: dados de privacidade limpos.", "#00FF88");
                }

                // 3. Windows Explorer History (Run MRU, Recent Docs, TypedPaths)
                Progress(60, LocalizationService.Instance.GetString("RepairProgress_Privacy_ExplorerMRU"));
                await CleanRegistryMRUAsync();
                
                var recentFolder = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
                if (Directory.Exists(recentFolder))
                {
                    var (f, c) = SafeDeleteDirectoryContents(recentFolder);
                    totalFreed += f;
                    totalFiles += c;
                }
                Log($"  ✓ Histórico do Windows Explorer limpo.", "#00FF88");

                // 4. Telemetry Caches e Logs de Diagnóstico
                Progress(80, LocalizationService.Instance.GetString("RepairProgress_Privacy_Telemetry"));
                var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                var telemetryPaths = new[]
                {
                    Path.Combine(programData, @"Microsoft\Diagnosis\ETLLogs"),
                    Path.Combine(localAppData, @"Microsoft\Windows\WebCache")
                };

                foreach (var path in telemetryPaths)
                {
                    if (Directory.Exists(path))
                    {
                        var (f, c) = SafeDeleteDirectoryContents(path);
                        totalFreed += f;
                        totalFiles += c;
                    }
                }
                Log($"  ✓ Caches de telemetria removidos.", "#00FF88");

                Progress(100, LocalizationService.Instance.GetString("RepairProgress_Privacy_Complete"));

                step.Success = true;
                step.Summary = $"Privacidade protegida. {(totalFreed / 1048576.0):F2} MB de rastros e históricos limpos.";
                step.Details.Add($"Total de arquivos de rastro removidos: {totalFiles}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[PrivacyCleaner] Erro na limpeza de privacidade: {ex.Message}");
                step.Success = false;
                step.Summary = LocalizationService.Instance.GetString("Repair_Privacy_Warn");
                step.Details.Add(ex.Message);
            }

            step.Duration = sw.Elapsed;
            return step;
        }

        private (long freed, int count) SafeDeleteDirectoryContents(string dirPath)
        {
            long freed = 0;
            int count = 0;
            try
            {
                foreach (var file in Directory.EnumerateFiles(dirPath, "*", SearchOption.AllDirectories))
                {
                    var res = SafeDeleteFile(file);
                    freed += res.freed;
                    count += res.count;
                }
            }
            catch { }
            return (freed, count);
        }

        private (long freed, int count) SafeDeleteFile(string filePath)
        {
            try
            {
                var fi = new FileInfo(filePath);
                if (fi.Exists)
                {
                    long length = fi.Length;
                    fi.Delete();
                    return (length, 1);
                }
            }
            catch { } // Arquivos em uso (ex: navegador aberto) não quebram o fluxo
            return (0, 0);
        }

        private async Task CleanRegistryMRUAsync()
        {
            // Apaga chaves RunMRU e TypedPaths de forma segura usando PowerShell (modo silencioso)
            var script = @"
$paths = @(
    'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\RunMRU',
    'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\TypedPaths',
    'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\WordWheelQuery'
)
foreach ($p in $paths) {
    if (Test-Path $p) {
        Remove-ItemProperty -Path $p -Name * -ErrorAction SilentlyContinue
    }
}
";
            try
            {
                var tmp = Path.GetTempFileName() + ".ps1";
                await File.WriteAllTextAsync(tmp, script, System.Text.Encoding.UTF8);
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{tmp}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc != null)
                {
                    await proc.WaitForExitAsync();
                }
                File.Delete(tmp);
            }
            catch { }
        }
    }
}
