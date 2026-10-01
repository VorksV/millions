using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.SmartRepair.Architecture;

namespace VoltrisOptimizer.Services.SmartRepair.Modules
{
    public class DeepCleanModule : ISmartRepairModule
    {
        private readonly ILoggingService _logger;
        private readonly ISmartRepairEventBus _eventBus;
        private readonly string _correlationId;

        public string ModuleId => "DeepClean";
        public string Category => "System";

        public string Name => Services.LocalizationService.Instance.GetString("SmartRepair_Module_DeepClean_Name") ?? "Limpeza Profunda";
        public string Description => Services.LocalizationService.Instance.GetString("SmartRepair_Module_DeepClean_Desc") ?? "Remove caches obsoletos, logs antigos e arquivos mortos do Windows e aplicativos.";

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

        public DeepCleanModule(ILoggingService logger, ISmartRepairEventBus eventBus, string correlationId)
        {
            _logger = logger;
            _eventBus = eventBus;
            _correlationId = correlationId;
        }

        private class CleanTarget
        {
            public string Path { get; set; } = string.Empty;
            public long SizeBytes { get; set; }
            public string Category { get; set; } = string.Empty;
            public bool IsDirectory { get; set; }
            public string? Pattern { get; set; }
        }

        public async Task<ModuleScanResult> ScanAsync(IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleScanResult();
            var targets = new List<CleanTarget>();
            var sw = Stopwatch.StartNew();

            _eventBus.PublishMessage(_correlationId, ModuleId, "Iniciando scan de limpeza profunda...", SmartRepairEventType.OperationStarted);

            await Task.Run(() =>
            {
                ScanWindowsTemp(targets, result.Statistics, ct);
                ScanUserTemp(targets, result.Statistics, ct);
                ScanPrefetch(targets, result.Statistics, ct);
                ScanLogFiles(targets, result.Statistics, ct);
                ScanDumpFiles(targets, result.Statistics, ct);
                ScanOldWindows(targets, result.Statistics, ct);
            }, ct);

            sw.Stop();
            result.FoundItems.AddRange(targets);

            _eventBus.PublishMessage(_correlationId, ModuleId, $"Scan profundo concluído. {targets.Count} itens encontrados ({result.Statistics.SpaceRecoveredBytes} bytes) em {sw.ElapsedMilliseconds}ms.", SmartRepairEventType.OperationFinished);
            _logger.LogInfo($"[DeepCleanModule] Scan concluído. Itens={targets.Count}, Space={result.Statistics.SpaceRecoveredBytes}bytes, Duration={sw.ElapsedMilliseconds}ms, CorrelationId={_correlationId}");

            return result;
        }

        private void ScanWindowsTemp(List<CleanTarget> targets, ModuleStatistics stats, CancellationToken ct)
        {
            var tempDir = Environment.GetEnvironmentVariable("TEMP", EnvironmentVariableTarget.Machine) ?? @"C:\Windows\Temp";
            ScanDirectoryForSize(tempDir, "WindowsTemp", targets, stats, ct);
        }

        private void ScanUserTemp(List<CleanTarget> targets, ModuleStatistics stats, CancellationToken ct)
        {
            var tempDir = Path.GetTempPath();
            ScanDirectoryForSize(tempDir, "UserTemp", targets, stats, ct);
        }

        private void ScanPrefetch(List<CleanTarget> targets, ModuleStatistics stats, CancellationToken ct)
        {
            var prefetchDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch");
            ScanDirectoryForSize(prefetchDir, "Prefetch", targets, stats, ct);
        }

        private void ScanLogFiles(List<CleanTarget> targets, ModuleStatistics stats, CancellationToken ct)
        {
            var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

            var logDirs = new[]
            {
                Path.Combine(windowsDir, "Logs"),
                Path.Combine(windowsDir, "System32", "LogFiles"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "WER")};

            foreach (var dir in logDirs.Where(Directory.Exists))
            {
                ScanDirectoryForSize(dir, "Logs", targets, stats, ct);
            }
        }

        private void ScanDumpFiles(List<CleanTarget> targets, ModuleStatistics stats, CancellationToken ct)
        {
            var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var dumpDir = Path.Combine(windowsDir, "Minidump");
            ScanDirectoryForSize(dumpDir, "Minidump", targets, stats, ct);

            var memoryDmp = Path.Combine(windowsDir, "MEMORY.DMP");
            if (File.Exists(memoryDmp))
            {
                try
                {
                    var info = new FileInfo(memoryDmp);
                    stats.ItemsScanned++;
                    stats.ItemsFound++;
                    stats.SpaceRecoveredBytes += info.Length;
                    targets.Add(new CleanTarget
                    {
                        Path = memoryDmp,
                        SizeBytes = info.Length,
                        Category = "MemoryDump",
                        IsDirectory = false
                    });
                }
                catch (Exception ex) { _logger?.LogWarning($"[DeepClean] Erro ao escanear memory dump: {ex.Message}"); }
            }
        }

        private void ScanOldWindows(List<CleanTarget> targets, ModuleStatistics stats, CancellationToken ct)
        {
            var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var parentDir = Directory.GetParent(windowsDir)?.FullName;
            if (parentDir == null) return;

            var oldDirs = new[] { "Windows.old", "Windows~BT", "Windows~WS" };
            foreach (var dirName in oldDirs)
            {
                var dir = Path.Combine(parentDir, dirName);
                if (Directory.Exists(dir))
                {
                    try
                    {
                        var dirInfo = new DirectoryInfo(dir);
                        long size = 0;
                        try
                        {
                            size = dirInfo.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f =>
                            {
                                try { return f.Length; } catch { return 0L; }
                            });
                        }
                        catch { size = 5_000_000_000L; }

                        stats.ItemsScanned++;
                        stats.ItemsFound++;
                        stats.SpaceRecoveredBytes += size;
                        targets.Add(new CleanTarget
                        {
                            Path = dir,
                            SizeBytes = size,
                            Category = "OldWindows",
                            IsDirectory = true
                        });

                        _logger.LogInfo($"[DeepCleanModule] Instalação antiga encontrada: {dir} (~{size} bytes)");
                    }
                    catch (Exception exWindowsOld) { _logger?.LogWarning($"[DeepClean] Erro ao escanear Windows.old: {exWindowsOld.Message}"); }
                }
            }
        }

        private void ScanDirectoryForSize(string dirPath, string category, List<CleanTarget> targets, ModuleStatistics stats, CancellationToken ct)
        {
            if (!Directory.Exists(dirPath)) return;

            try
            {
                foreach (var file in Directory.EnumerateFiles(dirPath, "*", SearchOption.TopDirectoryOnly))
                {
                    ct.ThrowIfCancellationRequested();
                    stats.ItemsScanned++;
                    try
                    {
                        var info = new FileInfo(file);
                        if (info.LastAccessTime < DateTime.Now.AddDays(-7) || info.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || info.Name.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
                        {
                            stats.ItemsFound++;
                            stats.SpaceRecoveredBytes += info.Length;
                            targets.Add(new CleanTarget
                            {
                                Path = file,
                                SizeBytes = info.Length,
                                Category = category,
                                IsDirectory = false
                            });
                        }
                    }
                    catch { stats.WarningCount++; }
                }

                foreach (var subDir in Directory.EnumerateDirectories(dirPath, "*", SearchOption.TopDirectoryOnly))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var dirInfo = new DirectoryInfo(subDir);
                        var files = dirInfo.EnumerateFiles("*", SearchOption.AllDirectories);
                        long totalSize = 0;
                        int fileCount = 0;
                        foreach (var f in files)
                        {
                            ct.ThrowIfCancellationRequested();
                            try { totalSize += f.Length; fileCount++; } catch (Exception exFile) { _logger?.LogWarning($"[DeepClean] Erro ao ler arquivo {f.FullName}: {exFile.Message}"); }
                        }

                        if (fileCount > 0)
                        {
                            stats.ItemsScanned++;
                            stats.ItemsFound++;
                            stats.SpaceRecoveredBytes += totalSize;
                            targets.Add(new CleanTarget
                            {
                                Path = subDir,
                                SizeBytes = totalSize,
                                Category = category,
                                IsDirectory = true
                            });
                        }
                    }
                    catch (Exception exSubDir) { _logger?.LogWarning($"[DeepClean] Erro ao escanear subdiretório {subDir}: {exSubDir.Message}"); }
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (Exception ex)
            {
                _logger.LogWarning($"[DeepCleanModule] Erro escaneando {dirPath}: {ex.Message}");
                stats.WarningCount++;
            }
        }

        public Task<ModuleSimulationResult> SimulateAsync(ModuleScanResult scanResult, CancellationToken ct)
        {
            var sim = new ModuleSimulationResult();
            var targets = scanResult.FoundItems.Cast<CleanTarget>().ToList();

            sim.EstimatedStatistics.ItemsFound = targets.Count;
            sim.EstimatedStatistics.SpaceRecoveredBytes = targets.Sum(t => t.SizeBytes);
            sim.RiskLevel = RiskLevel.Safe;
            sim.ItemsToProcess.AddRange(targets);

            return Task.FromResult(sim);
        }

        public async Task<ModuleExecutionResult> ExecuteAsync(ModuleSimulationResult simResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleExecutionResult();
            var targets = simResult.ItemsToProcess.Cast<CleanTarget>().ToList();
            var sw = Stopwatch.StartNew();

            int total = targets.Count;
            int current = 0;

            _eventBus.PublishMessage(_correlationId, ModuleId, "Iniciando limpeza profunda...", SmartRepairEventType.OperationStarted);

            foreach (var target in targets)
            {
                ct.ThrowIfCancellationRequested();
                current++;
                int percent = total > 0 ? (int)((double)current / total * 100) : 0;

                try
                {
                    if (target.IsDirectory)
                    {
                        if (Directory.Exists(target.Path))
                        {
                            var beforeSize = GetDirectorySize(target.Path);
                            Directory.Delete(target.Path, true);
                            _logger.LogInfo($"[DeepCleanModule] Removido diretório: {target.Path} ({target.SizeBytes} bytes), CorrelationId={_correlationId}");
                            result.FinalStatistics.ItemsRepaired++;
                            result.FinalStatistics.SpaceRecoveredBytes += target.SizeBytes;
                        }
                    }
                    else
                    {
                        if (File.Exists(target.Path))
                        {
                            var fi = new FileInfo(target.Path);
                            File.Delete(target.Path);
                            _logger.LogInfo($"[DeepCleanModule] Removido arquivo: {target.Path} ({fi.Length} bytes), CorrelationId={_correlationId}");
                            result.FinalStatistics.ItemsRepaired++;
                            result.FinalStatistics.SpaceRecoveredBytes += fi.Length;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[DeepCleanModule] Falha ao remover {target.Path}: {ex.Message}, CorrelationId={_correlationId}");
                    result.FinalStatistics.ItemsIgnored++;
                }

                if (current % 50 == 0 || current == total)
                {
                    _eventBus.PublishMessage(_correlationId, ModuleId, $"Limpeza profunda: {current}/{total} itens processados.", SmartRepairEventType.ProgressChanged);
                    progress?.Report(new RepairProgress { StepPercent = percent, StatusMessage = $"Limpando... {current}/{total}" });
                }
            }

            sw.Stop();
            var postVerified = VerifyCleanResult(targets);

            _eventBus.PublishMessage(_correlationId, ModuleId, $"Limpeza profunda concluída. {result.FinalStatistics.ItemsRepaired} removidos, {result.FinalStatistics.ItemsIgnored} ignorados, {result.FinalStatistics.SpaceRecoveredBytes} bytes recuperados. Verificados: {postVerified}. Duração: {sw.ElapsedMilliseconds}ms.", SmartRepairEventType.OperationFinished);
            _logger.LogInfo($"[DeepCleanModule] Execução concluída. Removed={result.FinalStatistics.ItemsRepaired}, Ignored={result.FinalStatistics.ItemsIgnored}, Space={result.FinalStatistics.SpaceRecoveredBytes}, Verified={postVerified}, Duration={sw.ElapsedMilliseconds}ms, CorrelationId={_correlationId}");

            return result;
        }

        private int VerifyCleanResult(List<CleanTarget> targets)
        {
            int verified = 0;
            foreach (var t in targets)
            {
                bool exists = t.IsDirectory ? Directory.Exists(t.Path) : File.Exists(t.Path);
                if (!exists) verified++;
            }
            return verified;
        }

        private static long GetDirectorySize(string dirPath)
        {
            try
            {
                return new DirectoryInfo(dirPath).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f =>
                {
                    try { return f.Length; } catch { return 0L; }
                });
            }
            catch { return 0; }
        }

        public Task<ModuleRollbackResult> RollbackAsync(ModuleExecutionResult execResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            throw new NotSupportedException("Arquivos deletados não podem ser restaurados.");
        }
    }
}
