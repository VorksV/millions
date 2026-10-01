using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.SmartRepair.Architecture;

namespace VoltrisOptimizer.Services.SmartRepair.Modules
{
    public class BloatwareRemovalModule : ISmartRepairModule
    {
        private readonly ILoggingService _logger;
        private readonly ISmartRepairEventBus _eventBus;
        private readonly string _correlationId;
        private readonly List<BloatwareApp> _supportedApps;
        private readonly List<string> _protectedPackages;

        public string ModuleId => "BloatwareRemoval";
        public string Category => "System";
        public string Name => Services.LocalizationService.Instance.GetString("SmartRepair_Module_Bloatware_Name") ?? "Remoção de Bloatwares";
        public string Description => Services.LocalizationService.Instance.GetString("SmartRepair_Module_Bloatware_Desc") ?? "Remove aplicativos nativos não essenciais do Windows com segurança.";

        public ModuleCapabilities Capabilities { get; } = new ModuleCapabilities
        {
            SupportsScan = true,
            SupportsSimulation = true,
            SupportsExecution = true,
            SupportsRollback = false,
            SupportsParallelism = false,
            HasDestructiveOperations = true,
            MaxRiskLevel = RiskLevel.Safe,
            RequiresAdmin = true
        };

        public IReadOnlyList<string> Dependencies => Array.Empty<string>();

        public BloatwareRemovalModule(ILoggingService logger, ISmartRepairEventBus eventBus, string correlationId)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _correlationId = correlationId ?? throw new ArgumentNullException(nameof(correlationId));
            _supportedApps = InitializeSupportedApps();
            _protectedPackages = InitializeProtectedPackages();

            _logger.LogInfo($"[{ModuleId}] Modulo inicializado. {_supportedApps.Count} apps suportados, {_protectedPackages.Count} protegidos. CorrelationId={_correlationId}");
        }

        private class BloatwareApp
        {
            public string FriendlyName { get; set; } = "";
            public string PackageName { get; set; } = "";
            public string Category { get; set; } = "";
            public bool IsSafeToRemove { get; set; } = true;
            public bool RequiresXboxCheck { get; set; } = false;
        }

        private class BloatwareItem
        {
            public string PackageName { get; set; } = "";
            public string PackageFullName { get; set; } = "";
            public string Version { get; set; } = "";
            public string Architecture { get; set; } = "";
            public bool IsRemovable { get; set; }
            public string Reason { get; set; } = "";
        }

        private class AppxPackageJson
        {
            public string Name { get; set; } = "";
            public string PackageFullName { get; set; } = "";
            public string Version { get; set; } = "";
            public int Architecture { get; set; }
        }

        private List<BloatwareApp> InitializeSupportedApps()
        {
            return new List<BloatwareApp>
            {
                new BloatwareApp { FriendlyName = "Clipchamp", PackageName = "Clipchamp.Clipchamp", Category = "Multimedia", IsSafeToRemove = true },
                new BloatwareApp { FriendlyName = "Microsoft News", PackageName = "Microsoft.BingNews", Category = "News", IsSafeToRemove = true },
                new BloatwareApp { FriendlyName = "Microsoft Family", PackageName = "Microsoft.MicrosoftFamily", Category = "System", IsSafeToRemove = false },
                new BloatwareApp { FriendlyName = "Microsoft To Do", PackageName = "Microsoft.Todos", Category = "Productivity", IsSafeToRemove = true },
                new BloatwareApp { FriendlyName = "Microsoft Whiteboard", PackageName = "Microsoft.Whiteboard", Category = "Productivity", IsSafeToRemove = true },
                new BloatwareApp { FriendlyName = "Microsoft Journal", PackageName = "Microsoft.Journal", Category = "Productivity", IsSafeToRemove = true },
                new BloatwareApp { FriendlyName = "Windows Media Player", PackageName = "Microsoft.WindowsMediaPlayer", Category = "Multimedia", IsSafeToRemove = true },
                new BloatwareApp { FriendlyName = "Microsoft Solitaire Collection", PackageName = "Microsoft.MicrosoftSolitaireCollection", Category = "Games", IsSafeToRemove = true },
                new BloatwareApp { FriendlyName = "Xbox Console Companion", PackageName = "Microsoft.XboxApp", Category = "Games", IsSafeToRemove = true, RequiresXboxCheck = true },
                new BloatwareApp { FriendlyName = "Xbox TCUI", PackageName = "Microsoft.Xbox.TCUI", Category = "Games", IsSafeToRemove = true, RequiresXboxCheck = true },
                new BloatwareApp { FriendlyName = "Xbox Game Speech Window", PackageName = "Microsoft.XboxGameSpeechWindow", Category = "Games", IsSafeToRemove = true, RequiresXboxCheck = true },
                new BloatwareApp { FriendlyName = "Windows Maps", PackageName = "Microsoft.WindowsMaps", Category = "Utilities", IsSafeToRemove = true },
                new BloatwareApp { FriendlyName = "Microsoft People", PackageName = "Microsoft.People", Category = "Social", IsSafeToRemove = true },
                new BloatwareApp { FriendlyName = "Quick Assist", PackageName = "Microsoft.CoronaVirusIndicator", Category = "Utilities", IsSafeToRemove = true },
                new BloatwareApp { FriendlyName = "Microsoft Sticky Notes", PackageName = "Microsoft.MicrosoftStickyNotes", Category = "Productivity", IsSafeToRemove = true },
                new BloatwareApp { FriendlyName = "Windows Sound Recorder", PackageName = "Microsoft.WindowsSoundRecorder", Category = "Multimedia", IsSafeToRemove = true },
                new BloatwareApp { FriendlyName = "Paint 3D", PackageName = "Microsoft.MSPaint", Category = "Graphics", IsSafeToRemove = true },
                new BloatwareApp { FriendlyName = "Mixed Reality Portal", PackageName = "Microsoft.MixedReality.Portal", Category = "System", IsSafeToRemove = true },
                new BloatwareApp { FriendlyName = "3D Viewer", PackageName = "Microsoft.Microsoft3DViewer", Category = "Graphics", IsSafeToRemove = true },
                new BloatwareApp { FriendlyName = "Office Hub", PackageName = "Microsoft.MicrosoftOfficeHub", Category = "Productivity", IsSafeToRemove = true },
                new BloatwareApp { FriendlyName = "Get Help", PackageName = "Microsoft.GetHelp", Category = "System", IsSafeToRemove = true },
                new BloatwareApp { FriendlyName = "Feedback Hub", PackageName = "Microsoft.WindowsFeedbackHub", Category = "System", IsSafeToRemove = true },
                new BloatwareApp { FriendlyName = "Microsoft Start", PackageName = "Microsoft.MicrosoftStart", Category = "News", IsSafeToRemove = true }
            };
        }

        private List<string> InitializeProtectedPackages()
        {
            return new List<string>
            {
                "Microsoft.WindowsStore", "Microsoft.GamingServices", "Microsoft.XboxGameCallableUI",
                "Microsoft.XboxGameOverlay", "Microsoft.XboxGamingOverlay", "Microsoft.XboxIdentityProvider",
                "Microsoft.MicrosoftEdge", "Microsoft.WebView2", "Microsoft.Windows.SecHealthUI",
                "Microsoft.Windows.Security.Center", "Microsoft.WindowsUpdate", "Microsoft.WindowsInstaller",
                "Microsoft.RuntimeBroker", "Microsoft.ShellExperienceHost", "Microsoft.StartMenuExperienceHost",
                "Microsoft.SearchHost", "Microsoft.VCLibs", "Microsoft.UI.Xaml", "Microsoft.NET",
                "Microsoft.VisualC", "DirectX", "Microsoft.Windows.WinSxS", "Microsoft.RPC",
                "Microsoft.WMI", "Microsoft.BITS", "Microsoft.EventLog", "Microsoft.DeviceAssociationFramework",
                "Microsoft.SecurityCenter", "Microsoft.CredentialManager"
            };
        }

        public async Task<ModuleScanResult> ScanAsync(IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleScanResult();
            var sw = Stopwatch.StartNew();

            _eventBus.PublishMessage(_correlationId, ModuleId,
                Services.LocalizationService.Instance.GetString("SmartRepair_Module_Bloatware_ScanStarting") ?? "Escaneando aplicativos nativos do Windows...",
                SmartRepairEventType.OperationStarted);

            _logger.LogInfo($"[{ModuleId}] >>> ENTRY: ScanAsync INICIADO. CorrelationId={_correlationId}");

            try
            {
                await Task.Run(async () =>
                {
                    ct.ThrowIfCancellationRequested();

                    progress?.Report(new RepairProgress { StepPercent = 10, StatusMessage = "Detectando sistema..." });
                    var systemInfo = DetectSystemInfo();
                    _logger.LogInfo($"[{ModuleId}] Sistema: Windows {systemInfo.Edition} {systemInfo.Version} ({systemInfo.Architecture})");

                    progress?.Report(new RepairProgress { StepPercent = 20, StatusMessage = "Listando pacotes AppX..." });
                    _eventBus.PublishMessage(_correlationId, ModuleId, "Listando pacotes AppX instalados...", SmartRepairEventType.ProgressChanged);

                    var installedPackages = await GetInstalledAppXPackagesAsync(ct);
                    _logger.LogInfo($"[{ModuleId}] {installedPackages.Count} pacotes AppX encontrados");

                    progress?.Report(new RepairProgress { StepPercent = 30, StatusMessage = "Verificando aplicativos..." });
                    int removableCount = 0;

                    foreach (var app in _supportedApps)
                    {
                        ct.ThrowIfCancellationRequested();

                        var package = installedPackages.FirstOrDefault(p =>
                            p.Name.Equals(app.PackageName, StringComparison.OrdinalIgnoreCase) ||
                            p.PackageFullName.Contains(app.PackageName, StringComparison.OrdinalIgnoreCase));

                        if (!string.IsNullOrEmpty(package.Name))
                        {
                            if (_protectedPackages.Any(p => app.PackageName.Contains(p, StringComparison.OrdinalIgnoreCase)))
                            {
                                _logger.LogDebug($"[{ModuleId}] {app.FriendlyName} e PROTEGIDO - ignorado");
                                continue;
                            }

                            if (app.RequiresXboxCheck)
                            {
                                var xboxInUse = await CheckXboxEcosystemAsync(ct);
                                if (xboxInUse)
                                {
                                    _logger.LogDebug($"[{ModuleId}] {app.FriendlyName} nao removido (Xbox em uso)");
                                    continue;
                                }
                            }

                            result.FoundItems.Add(new BloatwareItem
                            {
                                PackageName = app.PackageName,
                                PackageFullName = package.PackageFullName,
                                Version = package.Version,
                                Architecture = package.Architecture,
                                IsRemovable = app.IsSafeToRemove,
                                Reason = app.IsSafeToRemove ? "Safe to remove" : "Protected by policy"
                            });

                            if (app.IsSafeToRemove)
                                removableCount++;

                            _logger.LogDebug($"[{ModuleId}] {app.FriendlyName} instalado - {(app.IsSafeToRemove ? "REMOVIVEL" : "PROTEGIDO")}");
                        }
                        else
                        {
                            _logger.LogDebug($"[{ModuleId}] {app.FriendlyName} nao esta instalado");
                        }
                    }

                    result.Statistics.ItemsFound = removableCount;
                    result.Statistics.ItemsScanned = _supportedApps.Count;

                    progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = $"{removableCount} bloatwares encontrados para remocao." });

                    string scanCompleteMsg = Services.LocalizationService.Instance.GetString("SmartRepair_Module_Bloatware_ScanComplete") ??
                        $"Scan concluido. {removableCount} aplicativos removiveis encontrados.";

                    _eventBus.PublishMessage(_correlationId, ModuleId, scanCompleteMsg, SmartRepairEventType.OperationFinished);
                    _logger.LogInfo($"[{ModuleId}] <<< EXIT: ScanAsync concluido em {sw.ElapsedMilliseconds}ms. Encontrados={removableCount}. CorrelationId={_correlationId}");

                }, ct);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning($"[{ModuleId}] Scan cancelado pelo usuario. CorrelationId={_correlationId}");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{ModuleId}] Erro no scan: {ex.Message}. CorrelationId={_correlationId}", ex);
                result.Message = $"Erro no scan: {ex.Message}";
            }

            return result;
        }

        public Task<ModuleSimulationResult> SimulateAsync(ModuleScanResult scanResult, CancellationToken ct)
        {
            var sim = new ModuleSimulationResult();
            sim.RiskLevel = RiskLevel.Safe;
            sim.EstimatedStatistics.ItemsFound = scanResult.FoundItems.Count;

            foreach (var item in scanResult.FoundItems)
            {
                if (item is BloatwareItem bloatware && bloatware.IsRemovable)
                    sim.ItemsToProcess.Add(item);
            }

            _logger.LogInfo($"[{ModuleId}] Simulacao concluida. Itens a processar={sim.ItemsToProcess.Count}. CorrelationId={_correlationId}");
            return Task.FromResult(sim);
        }

        public async Task<ModuleExecutionResult> ExecuteAsync(ModuleSimulationResult simResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleExecutionResult();
            var sw = Stopwatch.StartNew();

            _eventBus.PublishMessage(_correlationId, ModuleId,
                Services.LocalizationService.Instance.GetString("SmartRepair_Module_Bloatware_ExecStarting") ?? "Removendo bloatwares do Windows...",
                SmartRepairEventType.OperationStarted);

            _logger.LogInfo($"[{ModuleId}] >>> ENTRY: ExecuteAsync INICIADO. Itens={simResult.ItemsToProcess.Count}. CorrelationId={_correlationId}");

            try
            {
                int removedCount = 0;
                int total = simResult.ItemsToProcess.Count;
                int currentIndex = 0;

                foreach (var item in simResult.ItemsToProcess)
                {
                    ct.ThrowIfCancellationRequested();

                    if (item is not BloatwareItem bloatware)
                        continue;

                    currentIndex++;
                    progress?.Report(new RepairProgress
                    {
                        StepPercent = (currentIndex * 100) / total,
                        StatusMessage = $"Removendo {bloatware.PackageName} ({currentIndex}/{total})..."
                    });

                    _eventBus.PublishMessage(_correlationId, ModuleId,
                        string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Bloatware_Removing") ?? "Removendo {0}...", bloatware.PackageName),
                        SmartRepairEventType.ProgressChanged);

                    _logger.LogInfo($"[{ModuleId}] Removendo {bloatware.PackageName} ({currentIndex}/{total})...");

                    bool success = await RemoveAppxPackageAsync(bloatware.PackageFullName, ct);

                    if (success)
                    {
                        removedCount++;
                        result.FinalStatistics.ItemsRepaired++;
                        _logger.LogInfo($"[{ModuleId}] {bloatware.PackageName} removido com sucesso");

                        _eventBus.PublishMessage(_correlationId, ModuleId,
                            $"{bloatware.PackageName} removido.",
                            SmartRepairEventType.ProgressChanged);
                    }
                    else
                    {
                        result.FinalStatistics.ItemsIgnored++;
                        _logger.LogWarning($"[{ModuleId}] {bloatware.PackageName} falha ao remover");
                    }
                }

                progress?.Report(new RepairProgress { StepPercent = 100, StatusMessage = $"{removedCount}/{total} bloatwares removidos." });

                string execCompleteMsg = Services.LocalizationService.Instance.GetString("SmartRepair_Module_Bloatware_ExecDone") ??
                    $"Bloatwares removidos: {removedCount}/{total}.";

                _eventBus.PublishMessage(_correlationId, ModuleId, execCompleteMsg, SmartRepairEventType.OperationFinished);
                _logger.LogInfo($"[{ModuleId}] <<< EXIT: ExecuteAsync concluido em {sw.ElapsedMilliseconds}ms. Removidos={removedCount}/{total}. CorrelationId={_correlationId}");

                result.Message = $"{removedCount} bloatwares removidos.";
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning($"[{ModuleId}] Execucao cancelada pelo usuario. CorrelationId={_correlationId}");
                result.Message = LocalizationService.Instance.GetString("BloatwareOperationCancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{ModuleId}] Erro na execucao: {ex.Message}. CorrelationId={_correlationId}", ex);
                result.Message = $"Erro: {ex.Message}";
            }

            return result;
        }

        public Task<ModuleRollbackResult> RollbackAsync(ModuleExecutionResult execResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            _logger.LogWarning($"[{ModuleId}] Rollback nao implementado para BloatwareRemoval. CorrelationId={_correlationId}");
            return Task.FromResult(new ModuleRollbackResult());
        }

        #region Helper Methods

        private (string Version, string Edition, string Architecture) DetectSystemInfo()
        {
            var version = Environment.OSVersion.Version.ToString();
            var architecture = Environment.Is64BitOperatingSystem ? "x64" : "x86";
            string edition = "Unknown";

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                edition = key?.GetValue("EditionID")?.ToString() ?? "Unknown";
            }
            catch (Exception ex) { _logger?.LogWarning($"[BloatwareRemoval] Erro ao ler edição do Windows: {ex.Message}"); }

            return (version, edition, architecture);
        }

        private async Task<List<(string Name, string PackageFullName, string Version, string Architecture)>> GetInstalledAppXPackagesAsync(CancellationToken ct)
        {
            var packages = new List<(string, string, string, string)>();

            try
            {
                string script = "Get-AppxPackage | Select-Object Name, PackageFullName, Version, Architecture | ConvertTo-Json -Compress";
                var (exitCode, stdout, stderr) = await RunPowerShellAsync(script, ct);

                if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
                {
                    _logger.LogError($"[{ModuleId}] Erro ao listar AppX. ExitCode={exitCode}. stderr={stderr}");
                    return packages;
                }

                var options = new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,  PropertyNameCaseInsensitive = true };

                if (stdout.TrimStart().StartsWith("["))
                {
                    var items = JsonSerializer.Deserialize<List<AppxPackageJson>>(stdout, options);
                    if (items != null)
                    {
                        foreach (var item in items)
                        {
                            packages.Add((
                                item.Name ?? "",
                                item.PackageFullName ?? "",
                                item.Version ?? "",
                                MapArchitecture(item.Architecture)
                            ));
                        }
                    }
                }
                else if (stdout.TrimStart().StartsWith("{"))
                {
                    var item = JsonSerializer.Deserialize<AppxPackageJson>(stdout, options);
                    if (item != null)
                    {
                        packages.Add((
                            item.Name ?? "",
                            item.PackageFullName ?? "",
                            item.Version ?? "",
                            MapArchitecture(item.Architecture)
                        ));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{ModuleId}] Erro ao listar AppX: {ex.Message}");
            }

            return packages;
        }

        private static string MapArchitecture(int arch)
        {
            return arch switch
            {
                0 => "X86",
                6 => "Arm64",
                9 => "X64",
                10 => "Neutral",
                11 => "Arm",
                _ => "Unknown"
            };
        }

        private async Task<bool> CheckXboxEcosystemAsync(CancellationToken ct)
        {
            try
            {
                string script = @"
                    $xboxApp = Get-AppxPackage | Where-Object { $_.Name -like '*XboxGamingOverlay*' -or $_.Name -like '*XboxGamePass*' }
                    $gamingServices = Get-AppxPackage | Where-Object { $_.Name -like '*GamingServices*' }
                    if (($xboxApp -ne $null) -or ($gamingServices -ne $null)) { $true } else { $false }
                ";
                var (exitCode, stdout, stderr) = await RunPowerShellAsync(script, ct);

                bool hasXbox = stdout.Trim().Equals("True", StringComparison.OrdinalIgnoreCase);
                _logger.LogDebug($"[{ModuleId}] Xbox ecosystem check: {hasXbox} (exitCode={exitCode})");
                return hasXbox;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{ModuleId}] Erro ao verificar Xbox: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> RemoveAppxPackageAsync(string packageFullName, CancellationToken ct)
        {
            try
            {
                string escapedName = packageFullName.Replace("'", "''");
                string shortName = escapedName.Split('_')[0];
                
                string script = $@"
$ErrorActionPreference = 'SilentlyContinue'

# 1. Tentar finalizar processos associados para destravar o arquivo
Get-Process -Name '{shortName}' -ErrorAction SilentlyContinue | Stop-Process -Force

# 2. Remover o pacote provisionado (impede que seja reinstalado em novos perfis)
Get-AppxProvisionedPackage -Online | Where-Object {{ $_.PackageName -eq '{escapedName}' }} | Remove-AppxProvisionedPackage -Online

# 3. Remover para todos os usuários (mais agressivo)
Remove-AppxPackage -Package '{escapedName}' -AllUsers

# 4. Fallback (caso AllUsers falhe, tenta o default)
if (-not $?) {{
    Remove-AppxPackage -Package '{escapedName}'
}}
exit 0
";
                var (exitCode, stdout, stderr) = await RunPowerShellAsync(script, ct);

                // Como usamos SilentlyContinue e exit 0, o ExitCode sempre será 0 a menos que o PowerShell morra.
                // Verificamos a remoção via Get-AppxPackage
                await Task.Delay(1000, ct);

                string verifyScript = $"Get-AppxPackage -AllUsers -Name '{shortName}'";
                var (verifyExitCode, verifyStdout, _) = await RunPowerShellAsync(verifyScript, ct);

                bool removed = string.IsNullOrWhiteSpace(verifyStdout);
                if (!removed)
                {
                    _logger.LogError($"[{ModuleId}] Falha persistente ao remover {packageFullName}. verifyStdout: {verifyStdout}");
                }
                else
                {
                    _logger.LogDebug($"[{ModuleId}] Verificacao de remocao de {packageFullName}: sucesso.");
                }
                return removed;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{ModuleId}] Erro ao remover {packageFullName}: {ex.Message}");
                return false;
            }
        }

        private async Task<(int ExitCode, string Stdout, string Stderr)> RunPowerShellAsync(string script, CancellationToken ct)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script.Replace("\"", "\\\"")}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.Unicode,
                StandardErrorEncoding = System.Text.Encoding.Unicode
            };

            using var process = new Process { StartInfo = psi };
            var outputBuilder = new System.Text.StringBuilder();
            var errorBuilder = new System.Text.StringBuilder();

            process.OutputDataReceived += (_, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                await process.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    try { process.Kill(entireProcessTree: true); } catch (Exception exKill) { _logger?.LogWarning($"[BloatwareRemoval] Erro ao matar processo: {exKill.Message}"); }
                }
                throw;
            }

            string stdout = outputBuilder.ToString().Trim();
            string stderr = errorBuilder.ToString().Trim();

            if (!string.IsNullOrEmpty(stderr) || process.ExitCode != 0)
            {
                _logger.LogError($"[{ModuleId}] PowerShell command failed. ExitCode={process.ExitCode}. Cmd={script}. stderr={stderr}");
            }

            return (process.ExitCode, stdout, stderr);
        }

        #endregion
    }
}
