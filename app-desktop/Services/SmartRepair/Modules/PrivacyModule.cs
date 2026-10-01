using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Services.SmartRepair.Architecture;

namespace VoltrisOptimizer.Services.SmartRepair.Modules
{
    public class PrivacyModule : ISmartRepairModule
    {
        private readonly ILoggingService _logger;
        private readonly ISmartRepairEventBus _eventBus;
        private readonly IExclusionService _exclusionService;
        private readonly string _correlationId;

        public string ModuleId => "Privacy";
        public string Category => "System";
        public string Name => VoltrisOptimizer.Services.LocalizationService.Instance.GetString("SmartRepair_Module_Privacy_Name") ?? "Privacidade e Rastros";
        public string Description => VoltrisOptimizer.Services.LocalizationService.Instance.GetString("SmartRepair_Module_Privacy_Desc") ?? "Limpa histórico do Explorer, MRUs e logs de telemetria da Microsoft.";

        public ModuleCapabilities Capabilities { get; } = new ModuleCapabilities
        {
            SupportsScan = true,
            SupportsSimulation = true,
            SupportsExecution = true,
            SupportsRollback = false,
            SupportsParallelism = true,
            HasDestructiveOperations = true,
            MaxRiskLevel = RiskLevel.Safe,
            RequiresAdmin = true // To access ProgramData/Microsoft/Diagnosis
        };

        public IReadOnlyList<string> Dependencies => Array.Empty<string>();

        public PrivacyModule(ILoggingService logger, ISmartRepairEventBus eventBus, IExclusionService exclusionService, string correlationId)
        {
            _logger = logger;
            _eventBus = eventBus;
            _exclusionService = exclusionService;
            _correlationId = correlationId;
        }

        private class PrivacyTarget
        {
            public string PathOrKey { get; set; } = string.Empty;
            public long Size { get; set; }
            public bool IsRegistry { get; set; }
            public string SubCategory { get; set; } = string.Empty;
        }

        private IEnumerable<string> GetTargetDirectories()
        {
            var list = new List<string>
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Recent"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Burn"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Diagnosis", "ETLLogs")
            };
            return list.Where(d => Directory.Exists(d));
        }

        private IEnumerable<string> GetTargetRegistryKeys()
        {
            return new List<string>
            {
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU",
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\TypedPaths",
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\RecentDocs"
            };
        }

        public async Task<ModuleScanResult> ScanAsync(IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleScanResult();
            var targets = new List<PrivacyTarget>();

            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Privacy_ScanStarting") ?? "Iniciando scan de rastros de privacidade...", SmartRepairEventType.OperationStarted);

            // Scan Files
            foreach (var dir in GetTargetDirectories())
            {
                ct.ThrowIfCancellationRequested();
                _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Privacy_ScanningDir") ?? "Escaneando diretório de privacidade: {0}", dir), SmartRepairEventType.ProgressChanged);
                await Task.Run(() => ScanDirectory(dir, targets, result.Statistics, ct), ct);
            }

            // Scan Registry
            foreach (var key in GetTargetRegistryKeys())
            {
                ct.ThrowIfCancellationRequested();
                _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Privacy_ScanningRegistry") ?? "Escaneando MRU do registro: {0}", key), SmartRepairEventType.ProgressChanged);
                await Task.Run(() => ScanRegistryKey(key, targets, result.Statistics, ct), ct);
            }

            result.FoundItems.AddRange(targets);
            _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Privacy_ScanComplete") ?? "Scan de privacidade concluído. {0} rasto(s) encontrado(s).", targets.Count), SmartRepairEventType.OperationFinished);
            return result;
        }

        private void ScanDirectory(string dir, List<PrivacyTarget> targets, ModuleStatistics stats, CancellationToken ct)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    ct.ThrowIfCancellationRequested();
                    stats.ItemsScanned++;

                    if (_exclusionService.IsExcludedPath(file))
                    {
                        stats.ItemsProtected++;
                        continue;
                    }

                    try
                    {
                        var info = new FileInfo(file);
                        targets.Add(new PrivacyTarget
                        {
                            PathOrKey = file,
                            Size = info.Length,
                            IsRegistry = false,
                            SubCategory = dir
                        });
                        stats.ItemsFound++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"[PrivacyModule] Cannot access info for {file}: {ex.Message}");
                        stats.WarningCount++;
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                _logger.LogWarning($"[PrivacyModule] Access denied to directory: {dir}");
                stats.WarningCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[PrivacyModule] Error scanning directory {dir}", ex);
                stats.ErrorCount++;
            }
        }

        private void ScanRegistryKey(string keyPath, List<PrivacyTarget> targets, ModuleStatistics stats, CancellationToken ct)
        {
            try
            {
                using var rk = Registry.CurrentUser.OpenSubKey(keyPath);
                if (rk == null) return;

                stats.ItemsScanned++;

                var values = rk.GetValueNames();
                foreach (var val in values)
                {
                    if (val.Equals("MRUList", StringComparison.OrdinalIgnoreCase) || val.Equals("MRUListEx", StringComparison.OrdinalIgnoreCase))
                        continue; // Keep MRUList structure usually, or delete everything. Actually it's safe to delete the values.

                    targets.Add(new PrivacyTarget
                    {
                        PathOrKey = $@"{keyPath}\{val}",
                        Size = 100, // Dummy size for registry keys
                        IsRegistry = true,
                        SubCategory = keyPath
                    });
                    stats.ItemsFound++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[PrivacyModule] Error scanning registry {keyPath}: {ex.Message}");
                stats.WarningCount++;
            }
        }

        public Task<ModuleSimulationResult> SimulateAsync(ModuleScanResult scanResult, CancellationToken ct)
        {
            var sim = new ModuleSimulationResult();
            var targets = scanResult.FoundItems.Cast<PrivacyTarget>().ToList();

            sim.EstimatedStatistics.ItemsFound = targets.Count;
            sim.EstimatedStatistics.SpaceRecoveredBytes = targets.Sum(t => t.Size);
            sim.RiskLevel = RiskLevel.Safe;
            sim.ItemsToProcess.AddRange(targets);

            return Task.FromResult(sim);
        }

        public async Task<ModuleExecutionResult> ExecuteAsync(ModuleSimulationResult simResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleExecutionResult();
            var targets = simResult.ItemsToProcess.Cast<PrivacyTarget>().ToList();

            int total = targets.Count;
            int current = 0;

            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Privacy_ExecStarting") ?? "Iniciando limpeza de rastros de privacidade...", SmartRepairEventType.OperationStarted);

            foreach (var target in targets)
            {
                ct.ThrowIfCancellationRequested();
                current++;

                if (current % 20 == 0)
                {
                    int percent = (int)((double)current / total * 100);
                    _eventBus.Publish(new SmartRepairEventArgs
                    {
                        CorrelationId = _correlationId,
                        ModuleId = ModuleId,
                        EventType = SmartRepairEventType.ProgressChanged,
                        ProgressPercentage = percent,
                        Message = string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Privacy_ExecProgress") ?? "Limpando rastros {0}/{1}...", current, total)
                    });
                    progress?.Report(new RepairProgress { StepPercent = percent, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_Privacy_ExecStatus") ?? "Limpando rastros..." });
                }

                if (target.IsRegistry)
                {
                    try
                    {
                        int lastSlash = target.PathOrKey.LastIndexOf('\\');
                        string keyPath = target.PathOrKey.Substring(0, lastSlash);
                        string valName = target.PathOrKey.Substring(lastSlash + 1);

                        using var rk = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
                        if (rk != null)
                        {
                            rk.DeleteValue(valName, throwOnMissingValue: false);
                            result.FinalStatistics.ItemsRepaired++;
                            result.FinalStatistics.SpaceRecoveredBytes += target.Size;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"[PrivacyModule] Failed to delete registry trace {target.PathOrKey}: {ex.Message}");
                        result.FinalStatistics.ItemsIgnored++;
                    }
                }
                else
                {
                    try
                    {
                        File.Delete(target.PathOrKey);
                        result.FinalStatistics.ItemsRepaired++;
                        result.FinalStatistics.SpaceRecoveredBytes += target.Size;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"[PrivacyModule] Failed to delete file {target.PathOrKey}: {ex.Message}");
                        result.FinalStatistics.ItemsIgnored++;
                    }
                }
            }

            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_Privacy_Done") ?? "Limpeza de privacidade concluída.", SmartRepairEventType.OperationFinished);
            return result;
        }

        public Task<ModuleRollbackResult> RollbackAsync(ModuleExecutionResult execResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            throw new NotSupportedException(Services.LocalizationService.Instance.GetString("SmartRepair_Module_Privacy_NoRollback") ?? "Rastros de privacidade não podem ser restaurados.");
        }
    }
}
