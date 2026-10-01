using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.SmartRepair.Architecture;

namespace VoltrisOptimizer.Services.SmartRepair.Modules
{
    public class ChkDskModule : ISmartRepairModule
    {
        private readonly ILoggingService _logger;
        private readonly ISmartRepairEventBus _eventBus;
        private readonly string _correlationId;

        public string ModuleId => "ChkDsk";
        public string Category => "System";

        public string Name => Services.LocalizationService.Instance.GetString("SmartRepair_Module_ChkDsk_Name") ?? "Verificação de Disco (CHKDSK)";
        public string Description => Services.LocalizationService.Instance.GetString("SmartRepair_Module_ChkDsk_Desc") ?? "Escaneia e repara erros do sistema de arquivos e setores danificados.";

        public ModuleCapabilities Capabilities { get; } = new ModuleCapabilities
        {
            SupportsScan = true,
            SupportsSimulation = true,
            SupportsExecution = true,
            SupportsRollback = false,
            SupportsParallelism = false,
            HasDestructiveOperations = true,
            MaxRiskLevel = RiskLevel.Moderate,
            RequiresAdmin = true,
            RequiresReboot = true
        };

        public IReadOnlyList<string> Dependencies => Array.Empty<string>();

        public ChkDskModule(ILoggingService logger, ISmartRepairEventBus eventBus, string correlationId)
        {
            _logger = logger;
            _eventBus = eventBus;
            _correlationId = correlationId;
        }

        private class DiskTarget
        {
            public string DriveLetter { get; set; } = string.Empty;
            public string VolumeLabel { get; set; } = string.Empty;
            public string FileSystem { get; set; } = string.Empty;
            public long TotalSize { get; set; }
            public long FreeSpace { get; set; }
            public bool IsSystemDrive { get; set; }
            public bool IsDirty { get; set; }
            public DateTime? LastChkDskTime { get; set; }
            public string LastChkDskResult { get; set; } = string.Empty;
            public bool ErrorsFound { get; set; }
        }

        public async Task<ModuleScanResult> ScanAsync(IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleScanResult();
            var sw = Stopwatch.StartNew();
            var drives = new List<DiskTarget>();

            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_ChkDsk_Starting") ?? "Iniciando verificação de discos...", SmartRepairEventType.OperationStarted);

            await Task.Run(() =>
            {
                try
                {
                    foreach (var di in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
                    {
                        ct.ThrowIfCancellationRequested();
                        var drive = di.Name.TrimEnd('\\');
                        result.Statistics.ItemsScanned++;

                        _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_ChkDsk_AnalyzingDrive") ?? "Analisando unidade {0}...", drive), SmartRepairEventType.ProgressChanged);

                        var target = new DiskTarget
                        {
                            DriveLetter = drive,
                            VolumeLabel = di.VolumeLabel,
                            FileSystem = di.DriveFormat,
                            TotalSize = di.TotalSize,
                            FreeSpace = di.AvailableFreeSpace,
                            IsSystemDrive = di.Name.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.System)[..2], StringComparison.OrdinalIgnoreCase),
                            IsDirty = CheckDirtyBit(drive),
                            LastChkDskTime = GetLastChkDskTime(drive),
                            LastChkDskResult = GetLastChkDskResult(drive)
                        };

                        if (target.IsDirty)
                        {
                            target.ErrorsFound = true;
                            result.Statistics.ItemsFound++;
                        }

                        drives.Add(target);

                        _logger.LogInfo($"[ChkDskModule] Drive {drive}: IsDirty={target.IsDirty}, LastCheck={target.LastChkDskTime?.ToString("g") ?? "Nunca"}, Result={target.LastChkDskResult}");
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogError($"[ChkDskModule] Erro no scan de discos", ex);
                    result.Success = false;
                    result.Message = ex.Message;
                }
            }, ct);

            sw.Stop();
            result.FoundItems.AddRange(drives);

            _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_ChkDsk_ScanComplete") ?? "Verificação concluída. {0} discos analisados, {1} com problemas.", drives.Count, result.Statistics.ItemsFound), SmartRepairEventType.OperationFinished);
            _logger.LogInfo($"[ChkDskModule] Scan concluído em {sw.ElapsedMilliseconds}ms. Drives={drives.Count}, Problemas={result.Statistics.ItemsFound}, CorrelationId={_correlationId}");

            return result;
        }

        private static bool CheckDirtyBit(string drive)
        {
            try
            {
                using var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "fsutil",
                        Arguments = $"dirty query {drive}",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                proc.Start();
                var output = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit(5000);
                return output.Contains("- SUJO", StringComparison.OrdinalIgnoreCase) ||
                       output.Contains("is dirty", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static DateTime? GetLastChkDskTime(string drive)
        {
            try
            {
                using var log = EventLog.GetEventLogs().FirstOrDefault(l => l.Log == "Application");
                if (log == null) return null;

                var entries = log.Entries.Cast<EventLogEntry>()
                    .Where(e => e.InstanceId == 1001 &&
                                e.Source.Contains("Wininit", StringComparison.OrdinalIgnoreCase) &&
                                e.Message.Contains(drive, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(e => e.TimeGenerated)
                    .ToList();

                return entries.FirstOrDefault()?.TimeGenerated;
            }
            catch
            {
                return null;
            }
        }

        private static string GetLastChkDskResult(string drive)
        {
            try
            {
                using var log = EventLog.GetEventLogs().FirstOrDefault(l => l.Log == "Application");
                if (log == null) return "Unknown";

                var entries = log.Entries.Cast<EventLogEntry>()
                    .Where(e => e.InstanceId == 1001 &&
                                e.Source.Contains("Wininit", StringComparison.OrdinalIgnoreCase) &&
                                e.Message.Contains(drive, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(e => e.TimeGenerated)
                    .ToList();

                var last = entries.FirstOrDefault();
                if (last == null) return "Never checked";

                var msg = last.Message;
                if (msg.Contains("0x0", StringComparison.OrdinalIgnoreCase) || msg.Contains("no problems", StringComparison.OrdinalIgnoreCase))
                    return "No errors found";
                if (msg.Contains("0x1") || msg.Contains("errors found", StringComparison.OrdinalIgnoreCase) || msg.Contains("corrected", StringComparison.OrdinalIgnoreCase))
                    return "Errors found and fixed";
                if (msg.Contains("0x2") || msg.Contains("cleanup", StringComparison.OrdinalIgnoreCase))
                    return "Disk cleanup performed";
                if (msg.Contains("0x3") || msg.Contains("could not check", StringComparison.OrdinalIgnoreCase))
                    return "Check could not complete";

                return $"Result: {msg[..Math.Min(100, msg.Length)]}";
            }
            catch
            {
                return "Unknown";
            }
        }

        public Task<ModuleSimulationResult> SimulateAsync(ModuleScanResult scanResult, CancellationToken ct)
        {
            var sim = new ModuleSimulationResult();
            var drives = scanResult.FoundItems.Cast<DiskTarget>().ToList();

            sim.EstimatedStatistics.ItemsFound = drives.Count;
            sim.EstimatedStatistics.SpaceRecoveredBytes = 0;
            sim.RiskLevel = RiskLevel.Moderate;

            foreach (var drive in drives)
            {
                sim.ItemsToProcess.Add(drive);
                if (drive.ErrorsFound)
                {
                    _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_ChkDsk_Needed") ?? "{0}: Chkdsk necessário (dirty bit ativo).", drive.DriveLetter), SmartRepairEventType.ProgressChanged);
                }
                else
                {
                    _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_ChkDsk_Preventive") ?? "{0}: Recomenda-se verificação preventiva.", drive.DriveLetter), SmartRepairEventType.ProgressChanged);
                }
            }

            return Task.FromResult(sim);
        }

        public async Task<ModuleExecutionResult> ExecuteAsync(ModuleSimulationResult simResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            var result = new ModuleExecutionResult();
            var drives = simResult.ItemsToProcess.Cast<DiskTarget>().ToList();
            int total = drives.Count;
            int current = 0;

            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_ChkDsk_Starting") ?? "Iniciando verificação de discos...", SmartRepairEventType.OperationStarted);

            foreach (var drive in drives)
            {
                ct.ThrowIfCancellationRequested();
                current++;
                int percent = (int)((double)current / total * 100);

                var sb = new StringBuilder();
                sb.AppendLine($"=== CHKDSK para {drive.DriveLetter} ===");
                sb.AppendLine($"FileSystem: {drive.FileSystem}");
                sb.AppendLine($"IsSystemDrive: {drive.IsSystemDrive}");
                sb.AppendLine($"IsDirty: {drive.IsDirty}");
                sb.AppendLine($"LastCheck: {drive.LastChkDskTime?.ToString("g") ?? "Nunca"}");
                sb.AppendLine($"LastResult: {drive.LastChkDskResult}");

                if (drive.FileSystem.Equals("NTFS", StringComparison.OrdinalIgnoreCase))
                {
                    var driveSw = Stopwatch.StartNew();
                    string commandOutput = "";
                    int exitCode = -1;

                    _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_ChkDsk_Executing") ?? "Executando chkdsk em {0}...", drive.DriveLetter), SmartRepairEventType.ProgressChanged);
                    progress?.Report(new RepairProgress { StepPercent = percent, StatusMessage = string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_ChkDsk_VerifyingProgress") ?? "Verificando {0}...", drive.DriveLetter) });

                    (exitCode, commandOutput) = await RunChkDskOnlineScanAsync(drive.DriveLetter, ct);
                    sb.AppendLine($"Online scan exit code: {exitCode}");

                    if (exitCode == 0 || exitCode == 1)
                    {
                        _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_ChkDsk_Scheduling") ?? "Agendando verificação completa para {0} na próxima reinicialização...", drive.DriveLetter), SmartRepairEventType.ProgressChanged);
                        var (schedExitCode, schedOutput) = await ScheduleChkDskAsync(drive.DriveLetter, ct);
                        exitCode = schedExitCode;
                        commandOutput += "\n" + schedOutput;
                        sb.AppendLine($"Schedule exit code: {schedExitCode}");
                        if (schedExitCode == 0)
                        {
                            result.FinalStatistics.ItemsRepaired++;
                            _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_ChkDsk_ScheduledBoot") ?? "CHKDSK agendado para {0} no próximo boot.", drive.DriveLetter), SmartRepairEventType.ProgressChanged);
                        }
                        else
                        {
                            _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_ChkDsk_ScheduleFailed") ?? "Falha ao agendar CHKDSK para {0} (código {1})", drive.DriveLetter, schedExitCode), SmartRepairEventType.WarningRaised);
                            result.FinalStatistics.ItemsIgnored++;
                        }
                    }
                    else
                    {
                        _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_ChkDsk_OnlineFailed") ?? "Falha no scan online de {0} (código {1})", drive.DriveLetter, exitCode), SmartRepairEventType.WarningRaised);
                        result.FinalStatistics.ItemsIgnored++;
                    }

                    driveSw.Stop();

                    if (!string.IsNullOrWhiteSpace(commandOutput))
                    {
                        foreach (var line in commandOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                        {
                            var t = line.Trim();
                            if (t.Length > 0)
                                sb.AppendLine($"  {t}");
                        }
                    }

                    sb.AppendLine($"Duration: {driveSw.ElapsedMilliseconds}ms");

                    var postDirty = CheckDirtyBit(drive.DriveLetter);
                    sb.AppendLine($"Post-check dirty bit: {postDirty}");

                    _logger.LogInfo($"[ChkDskModule] Execução para {drive.DriveLetter} concluída. ExitCode={exitCode}, Duration={driveSw.ElapsedMilliseconds}ms, CorrelationId={_correlationId}, PostDirty={postDirty}");
                }
                else
                {
                    _eventBus.PublishMessage(_correlationId, ModuleId, string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_ChkDsk_NotNtfs") ?? "{0} ({1}) não é NTFS — pulando CHKDSK.", drive.DriveLetter, drive.FileSystem), SmartRepairEventType.WarningRaised);
                    result.FinalStatistics.ItemsIgnored++;
                }

                progress?.Report(new RepairProgress { StepPercent = percent, StatusMessage = string.Format(Services.LocalizationService.Instance.GetString("SmartRepair_Module_ChkDsk_DriveDone") ?? "Verificação de {0} concluída.", drive.DriveLetter) });
            }

            _eventBus.PublishMessage(_correlationId, ModuleId, Services.LocalizationService.Instance.GetString("SmartRepair_Module_ChkDsk_AllDone") ?? "Verificação de discos concluída.", SmartRepairEventType.OperationFinished);
            return result;
        }

        private async Task<(int ExitCode, string Output)> RunProcessAsync(string fileName, string arguments, CancellationToken ct, TimeSpan? timeout = null)
        {
            var output = new StringBuilder();
            try
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                if (timeout.HasValue)
                    linkedCts.CancelAfter(timeout.Value);

                using var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };

                var outputLock = new object();
                proc.OutputDataReceived += (_, e) => { if (e.Data != null) lock (outputLock) output.AppendLine(e.Data); };
                proc.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (outputLock) output.AppendLine($"ERR: {e.Data}"); };

                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                try
                {
                    await proc.WaitForExitAsync(linkedCts.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    try { proc.Kill(entireProcessTree: true); } catch (Exception exKill) { _logger?.LogWarning($"[ChkDsk] Erro ao matar processo: {exKill.Message}"); }
                    _logger.LogWarning($"[ChkDskModule] Processo {fileName} {arguments} excedeu o tempo limite.");
                    return (-2, "TIMEOUT");
                }

                lock (outputLock)
                {
                    return (proc.ExitCode, output.ToString());
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError($"[ChkDskModule] Falha ao executar {fileName} {arguments}", ex);
                return (-1, ex.Message);
            }
        }

        private Task<(int ExitCode, string Output)> RunChkDskOnlineScanAsync(string drive, CancellationToken ct)
            => RunProcessAsync("chkdsk", $"{drive} /scan /perf", ct, TimeSpan.FromMinutes(10));

        private Task<(int ExitCode, string Output)> ScheduleChkDskAsync(string drive, CancellationToken ct)
            => RunProcessAsync("fsutil", $"dirty set {drive}", ct, TimeSpan.FromSeconds(30));

        public Task<ModuleRollbackResult> RollbackAsync(ModuleExecutionResult execResult, IProgress<RepairProgress> progress, CancellationToken ct)
        {
            throw new NotSupportedException(Services.LocalizationService.Instance.GetString("SmartRepair_Module_ChkDsk_NoRollback") ?? "CHKDSK não pode ser desfeito via rollback.");
        }
    }
}
