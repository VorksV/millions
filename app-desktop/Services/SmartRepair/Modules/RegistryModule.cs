using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Services.SmartRepair.Architecture;

namespace VoltrisOptimizer.Services.SmartRepair.Modules
{
    public class RegistryModule : ISmartRepairModule
    {
        private readonly ILoggingService _logger;
        private readonly ISmartRepairEventBus _eventBus;
        private readonly IExclusionService _exclusionService;
        private readonly string _correlationId;
        private string _backupFilePath = string.Empty;
        private readonly List<RegistryTarget> _executedTargets = new();

        public string ModuleId => "Registry";
        public string Category => "System";
        public string Name => Services.LocalizationService.Instance.GetString("SmartRepair_Module_Registry_Name") ?? "Registro do Windows";
        public string Description => Services.LocalizationService.Instance.GetString("SmartRepair_Module_Registry_Desc") ?? "Limpa chaves órfãs, DLLs compartilhadas inexistentes e extensões inválidas.";

        public ModuleCapabilities Capabilities { get; } = new ModuleCapabilities
        {
            SupportsScan = true,
            SupportsSimulation = true,
            SupportsExecution = true,
            SupportsRollback = true,
            SupportsParallelism = false,
            HasDestructiveOperations = true,
            MaxRiskLevel = RiskLevel.Moderate,
            RequiresAdmin = true
        };

        public IReadOnlyList<string> Dependencies => Array.Empty<string>();

        public RegistryModule(ILoggingService logger, ISmartRepairEventBus eventBus, IExclusionService exclusionService, string correlationId)
        {
            _logger = logger;
            _eventBus = eventBus;
            _exclusionService = exclusionService;
            _correlationId = correlationId;
        }

        private class RegistryTarget
        {
            public string KeyPath { get; set; } = string.Empty;
            public string ValueName { get; set; } = string.Empty;
            public object? OriginalValue { get; set; }
            public bool IsKeyDeletion { get; set; }
            public string IssueType { get; set; } = string.Empty;
        }

        public async Task<ModuleScanResult> ScanAsync(IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleScanResult();
            var targets = new List<RegistryTarget>();

            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Registry_ScanStarting") ?? "Iniciando scan do Registro do Windows...", SmartRepairEventType.OperationStarted);

            await Task.Run(() =>
            {
                ScanSharedDlls(targets, result.Statistics, ct);
                ScanOrphanedSoftware(Registry.CurrentUser, @"Software", targets, result.Statistics, ct);
                ScanOrphanedSoftware(Registry.LocalMachine, @"SOFTWARE", targets, result.Statistics, ct);
                ScanRunAtStartup(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", targets, result.Statistics, ct);
                ScanRunAtStartup(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", targets, result.Statistics, ct);
            }, ct);

            result.FoundItems.AddRange(targets);
            _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Registry_ScanComplete") ?? "Scan do Registro concluído. {0} problema(s) encontrado(s).", targets.Count), SmartRepairEventType.OperationFinished);
            return result;
        }

        private void ScanSharedDlls(List<RegistryTarget> targets, ModuleStatistics stats, CancellationToken ct)
        {
            string path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDlls";
            try
            {
                ct.ThrowIfCancellationRequested();
                using var key = Registry.LocalMachine.OpenSubKey(path);
                if (key == null) return;

                _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Registry_ScanningDlls") ?? "Escaneando Shared DLLs...", SmartRepairEventType.ProgressChanged);

                foreach (var valName in key.GetValueNames())
                {
                    stats.ItemsScanned++;
                    if (!File.Exists(valName) && !Directory.Exists(valName))
                    {
                        stats.ItemsFound++;
                        var originalValue = key.GetValue(valName);
                        targets.Add(new RegistryTarget
                        {
                            KeyPath = @"HKLM\" + path,
                            ValueName = valName,
                            OriginalValue = originalValue,
                            IsKeyDeletion = false,
                            IssueType = "Missing Shared DLL"
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[RegistryModule] Erro escaneando SharedDlls: {ex.Message}");
                stats.WarningCount++;
            }
        }

        private void ScanOrphanedSoftware(RegistryKey root, string basePath, List<RegistryTarget> targets, ModuleStatistics stats, CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                using var key = root.OpenSubKey(basePath);
                if (key == null) return;

                _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Registry_ScanningOrphans") ?? "Escaneando Software Órfão em {0}...", root.Name), SmartRepairEventType.ProgressChanged);

                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    if (subKeyName.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase) ||
                        subKeyName.StartsWith("Classes", StringComparison.OrdinalIgnoreCase) ||
                        subKeyName.StartsWith("Policies", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    stats.ItemsScanned++;
                    try
                    {
                        using var subKey = key.OpenSubKey(subKeyName);
                        if (subKey == null) continue;

                        var installPath = subKey.GetValue("InstallLocation") as string ?? subKey.GetValue("InstallPath") as string;
                        if (!string.IsNullOrEmpty(installPath) && !Directory.Exists(installPath))
                        {
                            stats.ItemsFound++;
                            targets.Add(new RegistryTarget
                            {
                                KeyPath = $@"{root.Name}\{basePath}\{subKeyName}",
                                IsKeyDeletion = true,
                                IssueType = "Orphaned Software Key"
                            });
                        }
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
                    {
                        stats.ItemsProtected++;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[RegistryModule] Erro escaneando Software keys em {root.Name}: {ex.Message}");
                stats.WarningCount++;
            }
        }

        private void ScanRunAtStartup(RegistryKey root, string path, List<RegistryTarget> targets, ModuleStatistics stats, CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                using var key = root.OpenSubKey(path);
                if (key == null) return;

                foreach (var valName in key.GetValueNames())
                {
                    stats.ItemsScanned++;
                    var filePath = key.GetValue(valName) as string;
                    if (string.IsNullOrEmpty(filePath)) continue;

                    string cleanPath = filePath.Split('"').Length > 1 ? filePath.Split('"')[1] : filePath.Split(' ')[0];

                    if (!File.Exists(cleanPath))
                    {
                        stats.ItemsFound++;
                        var originalValue = key.GetValue(valName);
                        targets.Add(new RegistryTarget
                        {
                            KeyPath = $@"{root.Name}\{path}",
                            ValueName = valName,
                            OriginalValue = originalValue,
                            IsKeyDeletion = false,
                            IssueType = "Invalid Startup Entry"
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[RegistryModule] Erro escaneando Startup keys em {root.Name}: {ex.Message}");
                stats.WarningCount++;
            }
        }

        public Task<ModuleSimulationResult> SimulateAsync(ModuleScanResult scanResult, CancellationToken ct)
        {
            var sim = new ModuleSimulationResult();
            var targets = scanResult.FoundItems.Cast<RegistryTarget>().ToList();

            sim.EstimatedStatistics.ItemsFound = targets.Count;
            sim.EstimatedStatistics.SpaceRecoveredBytes = targets.Count * 256;
            sim.RiskLevel = RiskLevel.Moderate;
            sim.ItemsToProcess.AddRange(targets);

            return Task.FromResult(sim);
        }

        public async Task<ModuleExecutionResult> ExecuteAsync(ModuleSimulationResult simResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleExecutionResult();
            var targets = simResult.ItemsToProcess.Cast<RegistryTarget>().ToList();
            _executedTargets.Clear();

            int total = targets.Count;
            int current = 0;

            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Registry_ExecStarting") ?? "Iniciando limpeza do Registro...", SmartRepairEventType.OperationStarted);

            _backupFilePath = await CreateRegistryBackupAsync(ct);
            if (!string.IsNullOrEmpty(_backupFilePath))
            {
                _logger.LogInfo($"[RegistryModule] Backup do registro criado em: {_backupFilePath}, CorrelationId={_correlationId}");
            }

            await Task.Run(() =>
            {
                foreach (var target in targets)
                {
                    ct.ThrowIfCancellationRequested();
                    current++;

                    if (current % 10 == 0 || current == total)
                    {
                        int percent = (int)((double)current / total * 100);
                        _eventBus.Publish(new SmartRepairEventArgs
                        {
                            CorrelationId = _correlationId,
                            ModuleId = ModuleId,
                            EventType = SmartRepairEventType.ProgressChanged,
                            ProgressPercentage = percent,
                            Message = string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Registry_ExecProgress") ?? "Limpando Registro {0}/{1}...", current, total)
                        });
                        progress?.Report(new RepairProgress { StepPercent = percent, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_Registry_ExecStatus") ?? "Limpando Registro..." });
                    }

                    try
                    {
                        RegistryKey? root = target.KeyPath.StartsWith("HKEY_LOCAL_MACHINE") || target.KeyPath.StartsWith("HKLM") ? Registry.LocalMachine :
                                            target.KeyPath.StartsWith("HKEY_CURRENT_USER") || target.KeyPath.StartsWith("HKCU") ? Registry.CurrentUser : null;

                        if (root == null) continue;

                        string subPath = target.KeyPath.Substring(target.KeyPath.IndexOf('\\') + 1);

                        if (target.IsKeyDeletion)
                        {
                            int lastSlash = subPath.LastIndexOf('\\');
                            string parentPath = subPath.Substring(0, lastSlash);
                            string keyToDelete = subPath.Substring(lastSlash + 1);

                            using var parentKey = root.OpenSubKey(parentPath, writable: true);
                            if (parentKey != null)
                            {
                                parentKey.DeleteSubKeyTree(keyToDelete, throwOnMissingSubKey: false);
                                _executedTargets.Add(target);
                                result.FinalStatistics.ItemsRepaired++;
                            }
                        }
                        else
                        {
                            using var key = root.OpenSubKey(subPath, writable: true);
                            if (key != null)
                            {
                                key.DeleteValue(target.ValueName, throwOnMissingValue: false);
                                _executedTargets.Add(target);
                                result.FinalStatistics.ItemsRepaired++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"[RegistryModule] Falha ao reparar issue do registro {target.KeyPath}: {ex.Message}, CorrelationId={_correlationId}");
                        result.FinalStatistics.ItemsIgnored++;
                    }
                }
            }, ct);

            var verified = VerifyRegistryChanges(targets);
            _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Registry_ExecComplete") ?? "Limpeza do Registro concluída. {0} reparados, {1} ignorados.", result.FinalStatistics.ItemsRepaired, result.FinalStatistics.ItemsIgnored), SmartRepairEventType.OperationFinished);
            _logger.LogInfo($"[RegistryModule] Execução concluída. Repaired={result.FinalStatistics.ItemsRepaired}, Ignored={result.FinalStatistics.ItemsIgnored}, Verified={verified}, CorrelationId={_correlationId}");

            return result;
        }

        private int VerifyRegistryChanges(List<RegistryTarget> targets)
        {
            int confirmed = 0;
            foreach (var target in _executedTargets)
            {
                try
                {
                    RegistryKey? root = target.KeyPath.StartsWith("HKEY_LOCAL_MACHINE") || target.KeyPath.StartsWith("HKLM") ? Registry.LocalMachine :
                                        target.KeyPath.StartsWith("HKEY_CURRENT_USER") || target.KeyPath.StartsWith("HKCU") ? Registry.CurrentUser : null;
                    if (root == null) continue;

                    string subPath = target.KeyPath.Substring(target.KeyPath.IndexOf('\\') + 1);

                    if (target.IsKeyDeletion)
                    {
                        using var checkKey = root.OpenSubKey(subPath);
                        if (checkKey == null) confirmed++;
                    }
                    else
                    {
                        using var checkKey = root.OpenSubKey(subPath);
                        if (checkKey != null)
                        {
                            var val = checkKey.GetValue(target.ValueName);
                            if (val == null) confirmed++;
                        }
                        else
                        {
                            confirmed++;
                        }
                    }
                }
                catch
                {
                }
            }
            return confirmed;
        }

        private async Task<string> CreateRegistryBackupAsync(CancellationToken ct)
        {
            try
            {
                var backupDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RegistryBackups");
                Directory.CreateDirectory(backupDir);
                var backupFile = Path.Combine(backupDir, $"RegistryBackup_{DateTime.Now:yyyyMMdd_HHmmss}.reg");

                var regKeys = new[]
                {
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDlls",
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run",
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
                };

                using var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "reg.exe",
                        Arguments = $"export \"{regKeys[0]}\" \"{backupFile}\" /y",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                proc.Start();
                await proc.WaitForExitAsync(ct);

                for (int i = 1; i < regKeys.Length; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    using var mergeProc = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "reg.exe",
                            Arguments = $"export \"{regKeys[i]}\" \"{backupFile}\" /y",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        }
                    };
                    mergeProc.Start();
                    await mergeProc.WaitForExitAsync(ct);
                }

                return File.Exists(backupFile) ? backupFile : string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[RegistryModule] Falha ao criar backup do registro: {ex.Message}, CorrelationId={_correlationId}");
                return string.Empty;
            }
        }

        public async Task<ModuleRollbackResult> RollbackAsync(ModuleExecutionResult execResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleRollbackResult();

            if (string.IsNullOrEmpty(_backupFilePath) || !File.Exists(_backupFilePath))
            {
                _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Registry_NoBackup") ?? "Nenhum backup do registro disponível para rollback.", SmartRepairEventType.WarningRaised);
                result.Success = false;
                result.ItemsFailedToRestore = _executedTargets.Count;
                return result;
            }

            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Registry_RollbackStarting") ?? "Iniciando rollback do Registro...", SmartRepairEventType.RollbackStarted);

            try
            {
                using var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "reg.exe",
                        Arguments = $"import \"{_backupFilePath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                proc.Start();
                await proc.WaitForExitAsync(ct);

                if (proc.ExitCode == 0)
                {
                    result.Success = true;
                    result.ItemsRestored = _executedTargets.Count;
                    _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Registry_RollbackComplete") ?? "Rollback do Registro concluído. {0} itens restaurados.", result.ItemsRestored), SmartRepairEventType.RollbackFinished);
                }
                else
                {
                    result.Success = false;
                    result.ItemsFailedToRestore = _executedTargets.Count;
                    _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Registry_RollbackFailed") ?? "Rollback do Registro falhou (código {0}).", proc.ExitCode), SmartRepairEventType.RollbackFinished);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[RegistryModule] Falha no rollback do registro", ex);
                result.Success = false;
                result.ItemsFailedToRestore = _executedTargets.Count;
            }

            _logger.LogInfo($"[RegistryModule] Rollback concluído. Success={result.Success}, Restored={result.ItemsRestored}, Failed={result.ItemsFailedToRestore}, CorrelationId={_correlationId}");
            return result;
        }
    }
}
