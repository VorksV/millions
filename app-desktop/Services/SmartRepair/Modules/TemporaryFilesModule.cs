using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.SmartRepair.Architecture;

namespace VoltrisOptimizer.Services.SmartRepair.Modules
{
    public class TemporaryFilesModule : ISmartRepairModule
    {
        private readonly ILoggingService _logger;
        private readonly ISmartRepairEventBus _eventBus;
        private readonly IExclusionService _exclusionService;
        private readonly string _correlationId;

        public string ModuleId => "TemporaryFiles";
        public string Category => "System";
        public string Name => VoltrisOptimizer.Services.LocalizationService.Instance.GetString("SmartRepair_Module_TempFiles_Name") ?? "Temporary Files";
        public string Description => VoltrisOptimizer.Services.LocalizationService.Instance.GetString("SmartRepair_Module_TempFiles_Desc") ?? "Limpeza de arquivos temporários do sistema e usuário.";

        public ModuleCapabilities Capabilities { get; } = new ModuleCapabilities
        {
            SupportsScan = true,
            SupportsSimulation = true,
            SupportsExecution = true,
            SupportsRollback = false,
            SupportsParallelism = true,
            HasDestructiveOperations = true,
            MaxRiskLevel = RiskLevel.Safe,
            RequiresAdmin = true // Requires admin to clean Windows Temp
        };

        public IReadOnlyList<string> Dependencies => Array.Empty<string>();

        public TemporaryFilesModule(ILoggingService logger, ISmartRepairEventBus eventBus, IExclusionService exclusionService, string correlationId)
        {
            _logger = logger;
            _eventBus = eventBus;
            _exclusionService = exclusionService;
            _correlationId = correlationId;
        }

        private class TempFileTarget
        {
            public string Path { get; set; } = string.Empty;
            public long Size { get; set; }
            public string SubCategory { get; set; } = string.Empty;
        }

        private IEnumerable<string> GetTargetDirectories()
        {
            var list = new List<string>
            {
                Path.GetTempPath(), // User temp
                Environment.GetEnvironmentVariable("TEMP", EnvironmentVariableTarget.Machine) ?? @"C:\Windows\Temp",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Minidump")
            };
            return list.Where(d => Directory.Exists(d));
        }

        public async Task<ModuleScanResult> ScanAsync(IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleScanResult();
            var targets = new List<TempFileTarget>();
            
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_TempFiles_ScanStarting") ?? "Iniciando scan de arquivos temporários...", SmartRepairEventType.OperationStarted);

            foreach (var dir in GetTargetDirectories())
            {
                ct.ThrowIfCancellationRequested();
                _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_TempFiles_ScanningDir") ?? "Escaneando diretório: {0}", dir), SmartRepairEventType.ProgressChanged);
                
                await Task.Run(() => ScanDirectory(dir, targets, result.Statistics, ct), ct);
            }
            
            // Lixeira (Recycle Bin) calculation is usually done via SHEmptyRecycleBin but we can scan it too or simulate it
            long recycleBinSize = GetRecycleBinSize();
            if (recycleBinSize > 0)
            {
                targets.Add(new TempFileTarget { Path = "RecycleBin", Size = recycleBinSize, SubCategory = "RecycleBin" });
                result.Statistics.ItemsFound++;
            }

            result.FoundItems.AddRange(targets);
            _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_TempFiles_ScanComplete") ?? "Scan concluído. {0} arquivo(s) encontrado(s).", targets.Count), SmartRepairEventType.OperationFinished);
            return result;
        }

        private void ScanDirectory(string dir, List<TempFileTarget> targets, ModuleStatistics stats, CancellationToken ct)
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
                        targets.Add(new TempFileTarget
                        {
                            Path = file,
                            Size = info.Length,
                            SubCategory = dir
                        });
                        stats.ItemsFound++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"[TemporaryFilesModule] Cannot access info for {file}: {ex.Message}");
                        stats.WarningCount++;
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                _logger.LogWarning($"[TemporaryFilesModule] Access denied to directory: {dir}");
                stats.WarningCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[TemporaryFilesModule] Error scanning directory {dir}", ex);
                stats.ErrorCount++;
            }
        }

        private long GetRecycleBinSize()
        {
            // Dummy for simulation, accurate size requires IShellFolder
            return 1024 * 1024 * 50; // Mock 50MB
        }

        public Task<ModuleSimulationResult> SimulateAsync(ModuleScanResult scanResult, CancellationToken ct)
        {
            var sim = new ModuleSimulationResult();
            var targets = scanResult.FoundItems.Cast<TempFileTarget>().ToList();
            
            sim.EstimatedStatistics.ItemsFound = targets.Count;
            sim.EstimatedStatistics.SpaceRecoveredBytes = targets.Sum(t => t.Size);
            sim.RiskLevel = RiskLevel.Safe;
            sim.ItemsToProcess.AddRange(targets);
            
            return Task.FromResult(sim);
        }

        public async Task<ModuleExecutionResult> ExecuteAsync(ModuleSimulationResult simResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleExecutionResult();
            var targets = simResult.ItemsToProcess.Cast<TempFileTarget>().ToList();
            var _failedDeletions = new System.Collections.Generic.List<(string Path, string Error)>();
            
            int total = targets.Count;
            int current = 0;
            
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_TempFiles_ExecStarting") ?? "Iniciando limpeza de arquivos temporários...", SmartRepairEventType.OperationStarted);

            foreach (var target in targets)
            {
                ct.ThrowIfCancellationRequested();
                current++;

                if (current % 100 == 0)
                {
                    int percent = (int)((double)current / total * 100);
                    _eventBus.Publish(new SmartRepairEventArgs
                    {
                        CorrelationId = _correlationId,
                        ModuleId = ModuleId,
                        EventType = SmartRepairEventType.ProgressChanged,
                        ProgressPercentage = percent,
                        Message = string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_TempFiles_ExecProgress") ?? "Limpando {0}/{1} itens...", current, total)
                    });
                    progress?.Report(new RepairProgress { StepPercent = percent, StatusMessage = Services.LocalizationService.Instance.GetString("SmartRepair_Module_TempFiles_ExecStatus") ?? "Limpando arquivos..." });
                }

                if (target.Path == "RecycleBin")
                {
                    try
                    {
                        VoltrisOptimizer.Utils.Win32.RecycleBinHelper.EmptyWithoutUi();
                        result.FinalStatistics.ItemsRepaired++;
                        result.FinalStatistics.SpaceRecoveredBytes += target.Size;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("[TemporaryFilesModule] Failed to empty recycle bin", ex);
                        result.FinalStatistics.ErrorCount++;
                    }
                    continue;
                }

                try
                {
                    File.Delete(target.Path);
                    result.FinalStatistics.ItemsRepaired++;
                    result.FinalStatistics.SpaceRecoveredBytes += target.Size;
                }
                catch (Exception ex)
                {
                    result.FinalStatistics.ItemsIgnored++;
                    _failedDeletions.Add((target.Path, ex.Message));
                }
            }

            if (_failedDeletions.Count > 0)
            {
                var failedByDir = _failedDeletions
                    .GroupBy(f => System.IO.Path.GetDirectoryName(f.Path))
                    .Select(g => $"  {g.Key}: {g.Count()} falhas (ex: {g.First().Error})");
                _logger.LogWarning($"[TemporaryFilesModule] Resumo: {_failedDeletions.Count} arquivos nao puderam ser deletados (acesso negado/em uso):\n{string.Join("\n", failedByDir.Take(10))}");
                if (failedByDir.Count() > 10)
                    _logger.LogDebug($"[TemporaryFilesModule] ... e mais {failedByDir.Count() - 10} diretorios com falhas (total {_failedDeletions.Count} arquivos ignorados)");
            }
            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_TempFiles_Done") ?? "Limpeza concluída.", SmartRepairEventType.OperationFinished);
            return result;
        }

        public Task<ModuleRollbackResult> RollbackAsync(ModuleExecutionResult execResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            throw new NotSupportedException(Services.LocalizationService.Instance.GetString("SmartRepair_Module_TempFiles_NoRollback") ?? "Arquivos temporários não podem ser restaurados.");
        }
    }
}
