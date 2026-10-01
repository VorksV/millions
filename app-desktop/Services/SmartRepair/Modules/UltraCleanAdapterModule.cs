using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Models;
using VoltrisOptimizer.Services.SmartRepair.Architecture;

namespace VoltrisOptimizer.Services.SmartRepair.Modules
{
    public class UltraCleanAdapterModule : ISmartRepairModule
    {
        private readonly ILoggingService _logger;
        private readonly ISmartRepairEventBus _eventBus;
        private readonly string _correlationId;
        private UltraCleanAnalysis? _lastAnalysis;
        private readonly List<ItemAnalysis> _selectedForClean = new();

        public string ModuleId => "UltraClean";
        public string Category => LocalizationService.Instance.GetString("CleanupUltraTitle") ?? "DeepClean";

        public string Name => LocalizationService.Instance.GetString("CleanupUltraTitle") ?? "Limpeza Ultra";
        public string Description => LocalizationService.Instance.GetString("CleanupUltraDesc") ?? "Limpeza profunda e inteligente de todo o sistema.";

        public ModuleCapabilities Capabilities { get; } = new ModuleCapabilities
        {
            SupportsScan = true,
            SupportsSimulation = true,
            SupportsExecution = true,
            SupportsRollback = false,
            SupportsParallelism = true,
            HasDestructiveOperations = true,
            MaxRiskLevel = RiskLevel.Safe,
            RequiresAdmin = true
        };

        public IReadOnlyList<string> Dependencies => Array.Empty<string>();

        public UltraCleanAdapterModule(ILoggingService logger, ISmartRepairEventBus eventBus, string correlationId)
        {
            _logger = logger;
            _eventBus = eventBus;
            _correlationId = correlationId;
        }

        public async Task<ModuleScanResult> ScanAsync(IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleScanResult();

            if (App.UltraCleaner == null)
            {
                _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_UltraClean_NotAvailable") ?? "UltraCleaner não disponível.", SmartRepairEventType.ErrorRaised);
                result.Success = false;
                result.Message = LocalizationService.Instance.GetString("UltraCleanerNotAvailable");
                return result;
            }

            _eventBus.PublishMessage(_correlationId, ModuleId, LocalizationService.Instance.GetString("CleanupStartingDeepAnalysis") ?? "Iniciando análise profunda...", SmartRepairEventType.OperationStarted);

            var sw = Stopwatch.StartNew();

            var analysisProgress = new Progress<AnalysisProgress>(p =>
            {
                _eventBus.PublishMessage(_correlationId, ModuleId, $"{p.CurrentCategory}: {p.CurrentItem} ({p.PercentComplete}%)", SmartRepairEventType.ProgressChanged);
                progress?.Report(new RepairProgress
                {
                    StepPercent = p.PercentComplete,
                    StatusMessage = $"{p.CurrentCategory}: {p.CurrentItem}"
                });
            });

            try
            {
                _lastAnalysis = await App.UltraCleaner.AnalyzeAllAsync(
                    analysisProgress,
                    (catName, item) =>
                    {
                        _eventBus.PublishMessage(_correlationId, ModuleId, $"[{catName}] {item.Name}: {FormatBytes(item.FoundSize)}", SmartRepairEventType.ProgressChanged);
                        _logger.LogDebug($"[UltraCleanAdapter] Item: {catName}/{item.Name}, Size={item.FoundSize}, Safe={item.IsSafe}, CorrelationId={_correlationId}");
                    },
                    ct);

                sw.Stop();

                if (_lastAnalysis == null)
                {
                    result.Success = false;
                    result.Message = LocalizationService.Instance.GetString("UltraCleanerAnalysisNull");
                    return result;
                }

                foreach (var cat in _lastAnalysis.Categories)
                {
                    foreach (var item in cat.Items)
                    {
                        result.Statistics.ItemsScanned++;
                        if (item.FoundSize > 0)
                        {
                            result.Statistics.ItemsFound++;
                            result.FoundItems.Add(new UltraCleanFoundItem
                            {
                                CategoryName = cat.Name,
                                ItemName = item.Name,
                                SizeBytes = item.FoundSize,
                                IsSafe = item.IsSafe,
                                IsSelected = item.IsSelected
                            });
                            result.Statistics.SpaceRecoveredBytes += item.FoundSize;
                        }
                    }
                }

                _eventBus.PublishMessage(_correlationId, ModuleId,
                    string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_UltraClean_ItemsFound") ?? "{0} itens encontrados ({1}).", result.Statistics.ItemsFound, FormatBytes(result.Statistics.SpaceRecoveredBytes)),
                    SmartRepairEventType.OperationFinished);

                _logger.LogInfo($"[UltraCleanAdapter] Scan concluído. Itens={result.Statistics.ItemsFound}, Space={result.Statistics.SpaceRecoveredBytes}bytes, Duration={sw.ElapsedMilliseconds}ms, CorrelationId={_correlationId}");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError($"[UltraCleanAdapter] Falha no scan", ex);
                result.Success = false;
                result.Message = ex.Message;
            }

            return result;
        }

        public Task<ModuleSimulationResult> SimulateAsync(ModuleScanResult scanResult, CancellationToken ct)
        {
            var sim = new ModuleSimulationResult();
            var items = scanResult.FoundItems.Cast<UltraCleanFoundItem>().ToList();

            var safeItems = items.Where(i => i.IsSafe).ToList();
            sim.EstimatedStatistics.ItemsFound = safeItems.Count;
            sim.EstimatedStatistics.SpaceRecoveredBytes = safeItems.Sum(i => i.SizeBytes);
            sim.RiskLevel = RiskLevel.Safe;

            _selectedForClean.Clear();
            foreach (var item in safeItems)
            {
                sim.ItemsToProcess.Add(item);
                _selectedForClean.Add(new ItemAnalysis
                {
                    Name = item.ItemName,
                    Description = item.CategoryName,
                    FoundSize = item.SizeBytes,
                    IsSafe = true,
                    IsSelected = true
                });
            }

            return Task.FromResult(sim);
        }

        public async Task<ModuleExecutionResult> ExecuteAsync(ModuleSimulationResult simResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleExecutionResult();

            if (App.UltraCleaner == null || _lastAnalysis == null)
            {
                _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_UltraClean_NotAvailableExec") ?? "UltraCleaner ou análise não disponível para execução.", SmartRepairEventType.ErrorRaised);
                result.Success = false;
                return result;
            }

            var itemsToClean = _lastAnalysis.Categories
                .SelectMany(c => c.Items)
                .Where(i => i.FoundSize > 0 && i.IsSafe)
                .Select(i => { i.IsSelected = true; return i; })
                .ToList();

            if (itemsToClean.Count == 0)
            {
                _eventBus.PublishMessage(_correlationId, ModuleId, LocalizationService.Instance.GetString("CleanupNoItemSelected") ?? "Nenhum item para limpar.", SmartRepairEventType.WarningRaised);
                return result;
            }

            long totalBefore = GetDriveFreeSpace("C");
            _eventBus.PublishMessage(_correlationId, ModuleId, LocalizationService.Instance.GetString("CleanupStartingDeepClean") ?? "Iniciando limpeza profunda...", SmartRepairEventType.OperationStarted);
            _logger.LogInfo($"[UltraCleanAdapter] Iniciando limpeza de {itemsToClean.Count} itens. CorrelationId={_correlationId}");

            var sw = Stopwatch.StartNew();

            var cleanProgress = new Progress<CleanupProgress>(p =>
            {
                _eventBus.PublishMessage(_correlationId, ModuleId, p.CurrentItem ?? string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_UltraClean_ExecProgress") ?? "Limpando... {0}%", p.PercentComplete), SmartRepairEventType.ProgressChanged);
                progress?.Report(new RepairProgress
                {
                    StepPercent = p.PercentComplete,
                    StatusMessage = p.CurrentItem ?? Services.LocalizationService.Instance.GetString("SmartRepair_Module_UltraClean_ExecStatus") ?? "Limpando..."
                });
            });

            try
            {
                var cleanResult = await App.UltraCleaner.CleanSelectedAsync(itemsToClean, cleanProgress, ct);

                sw.Stop();

                long totalAfter = GetDriveFreeSpace("C");
                long actualRecovered = totalAfter - totalBefore;
                if (actualRecovered < 0) actualRecovered = cleanResult.SpaceCleaned;

                if (cleanResult.Success)
                {
                    result.FinalStatistics.ItemsRepaired = cleanResult.ItemsCleaned;
                    result.FinalStatistics.SpaceRecoveredBytes = Math.Max(actualRecovered, cleanResult.SpaceCleaned);
                    result.FinalStatistics.ItemsFound = itemsToClean.Count;

                    _eventBus.PublishMessage(_correlationId, ModuleId,
                        string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_UltraClean_SpaceFreed") ?? "{0} liberados.", FormatBytes(result.FinalStatistics.SpaceRecoveredBytes)),
                        SmartRepairEventType.OperationFinished);
                }
                else
                {
                    result.Success = false;
                    result.FinalStatistics.ItemsIgnored = itemsToClean.Count;
                    result.FinalStatistics.SpaceRecoveredBytes = cleanResult.SpaceCleaned;

                    _eventBus.PublishMessage(_correlationId, ModuleId,
                        string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_UltraClean_CompletedErrors") ?? "Limpeza concluída com falhas. {0} erro(s).", cleanResult.Errors.Count),
                        SmartRepairEventType.WarningRaised);

                    foreach (var err in cleanResult.Errors)
                    {
                        _logger.LogWarning($"[UltraCleanAdapter] Erro na limpeza: {err}, CorrelationId={_correlationId}");
                    }
                }

                var verifiedAfter = GetDriveFreeSpace("C");
                _eventBus.PublishMessage(_correlationId, ModuleId,
                    string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_UltraClean_PostVerify") ?? "Espaço verificado pós-limpeza: {0} livre em C:. Recuperado: {1}", FormatBytes(verifiedAfter), FormatBytes(result.FinalStatistics.SpaceRecoveredBytes)),
                    SmartRepairEventType.ProgressChanged);

                _logger.LogInfo($"[UltraCleanAdapter] Execução concluída. Repaired={result.FinalStatistics.ItemsRepaired}, Space={result.FinalStatistics.SpaceRecoveredBytes}bytes, Duration={sw.ElapsedMilliseconds}ms, CorrelationId={_correlationId}");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError($"[UltraCleanAdapter] Falha na execução da limpeza", ex);
                result.Success = false;
                result.Message = ex.Message;
            }

            return result;
        }

        public Task<ModuleRollbackResult> RollbackAsync(ModuleExecutionResult execResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            throw new NotSupportedException(Services.LocalizationService.Instance.GetString("SmartRepair_Module_UltraClean_NoRollback") ?? "Limpeza de arquivos não pode ser desfeita via rollback.");
        }

        private static long GetDriveFreeSpace(string drive)
        {
            try
            {
                var di = new System.IO.DriveInfo(drive);
                return di.AvailableFreeSpace;
            }
            catch { return 0; }
        }

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1) { order++; len /= 1024; }
            return $"{len:0.0} {sizes[order]}";
        }

        private class UltraCleanFoundItem
        {
            public string CategoryName { get; set; } = string.Empty;
            public string ItemName { get; set; } = string.Empty;
            public long SizeBytes { get; set; }
            public bool IsSafe { get; set; }
            public bool IsSelected { get; set; }
        }
    }
}
