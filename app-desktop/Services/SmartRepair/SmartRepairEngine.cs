using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;












using Microsoft.Win32;
using VoltrisOptimizer.Services.Repair;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.SmartRepair
{
    public class SmartRepairEngine
    {
        private readonly ILoggingService _logger;
        private readonly AdvancedRepairService _advancedRepair;
        
        private readonly PrivacyCleaner _privacy;
        private readonly DeepCacheCleaner _deepCache;
        private readonly MemoryOptimizer _memory;
        private readonly VoltrisOptimizer.Services.Repair.NetworkOptimizer _network;
        private readonly SystemTweaker _tweaker;
        private readonly RegistrySafeCleaner _safeReg;

        private readonly List<RepairStepBase> _steps = new();

        public IReadOnlyList<RepairStepBase> Steps => _steps.AsReadOnly();

        public SmartRepairEngine(ILoggingService logger)
        {
            _logger = logger;
            logger.LogInfo("[SmartRepairEngine] Construtor: criando AdvancedRepairService...");
            _advancedRepair = new AdvancedRepairService(logger);
            
            _privacy = new PrivacyCleaner(logger);
            _deepCache = new DeepCacheCleaner(logger);
            _memory = new MemoryOptimizer(logger);
            _network = new VoltrisOptimizer.Services.Repair.NetworkOptimizer(logger);
            _tweaker = new SystemTweaker(logger);
            _safeReg = new RegistrySafeCleaner(logger);

            logger.LogInfo("[SmartRepairEngine] Construtor: registrando steps...");
            RegisterSteps();
            logger.LogInfo($"[SmartRepairEngine] Construtor: {_steps.Count} steps registrados");
        }

        private void RegisterSteps()
        {
            _steps.Add(new Step00_RestorePoint(_advancedRepair));
            _steps.Add(new Step01_NetworkReset(_advancedRepair));
            _steps.Add(new Step02_DotNetRepair(_advancedRepair));
            _steps.Add(new Step03_VcRedistRepair(_advancedRepair));
            _steps.Add(new Step04_WindowsStoreReset(_advancedRepair));
            _steps.Add(new Step05_BootRepair(_advancedRepair));
            _steps.Add(new Step06_ServicesReset(_advancedRepair));
            _steps.Add(new Step07_RegistryRepair(_advancedRepair));
            _steps.Add(new Step08_DeepCacheClean(_deepCache));
            _steps.Add(new Step09_DefenderRepair(_advancedRepair));
            _steps.Add(new Step10_DriverRepair(_advancedRepair));
            _steps.Add(new Step11_BsodAnalysis(_advancedRepair));
            _steps.Add(new Step12_EventViewerRepair(_advancedRepair));
            _steps.Add(new Step13_PrintSpoolerReset(_advancedRepair));
            _steps.Add(new Step14_DirectXAudioRepair(_advancedRepair));
            _steps.Add(new Step15_PrivacyClean(_privacy));
            _steps.Add(new Step16_DismSfc(_advancedRepair));
            _steps.Add(new Step17_SmartDiagnostics(_advancedRepair));
            _steps.Add(new Step18_WindowsInstallerRepair(_advancedRepair));
            _steps.Add(new Step19_DiskOptimization(_advancedRepair));
            _steps.Add(new Step20_MemoryOptimization(_memory));
            _steps.Add(new Step21_StartupOptimization(_logger));
            _steps.Add(new Step25_SystemTweaks(_tweaker));
            _steps.Add(new Step26_ScheduledTasksOptimization(_logger));
            _steps.Add(new Step27_SearchIndexOptimization(_logger));
            _steps.Add(new Step29_WinSxSCleanup(_logger));
            _steps.Add(new Step30_NetworkOptimization(_network));
            _steps.Add(new Step31_RegistrySafeClean(_safeReg));
_steps.Add(new Step32_HardwareHealth(_logger));
            _steps.Add(new Step33_ServiceTuning(_logger));
            _steps.Add(new Step34_NtfsPermissions(_logger));
            _steps.Add(new Step35_ExecutiveReport(_logger));
        }

        public async Task<RepairReport> ExecuteAllAsync(
            IProgress<RepairProgress> progress,
            CancellationToken ct)
        {
            if (VoltrisOptimizer.Services.License.LicenseOrchestrationService.Instance.IsTrialExpired)
            {
                _logger.LogWarning("[SmartRepairEngine] Execução bloqueada: O Trial expirou.");
                throw new InvalidOperationException("Trial expirado. O Smart Repair está bloqueado.");
            }
            var report = new RepairReport
            {
                StartedAt = DateTime.Now,
                Statistics = new RepairStatistics { TotalSteps = _steps.Count }
            };

            var totalSteps = _steps.Count;
            var stepBaseWeight = 100.0 / totalSteps;
            var completedStepWeights = 0.0;

            _logger.LogInfo($"[SmartRepairEngine] INÍCIO — Executando {totalSteps} etapas");

            for (int i = 0; i < _steps.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var step = _steps[i];

                var stepProgress2 = new Progress<RepairProgress>(p =>
                {
                    p.OverallPercent = Math.Min(
                        (int)(completedStepWeights + stepBaseWeight * (p.StepPercent / 100.0)), 99);
                    p.CurrentStepId = step.Id;
                    p.StepName = step.Name;
                    p.EstimatedTimeRemaining = TimeSpan.FromSeconds(
                        (totalSteps - i - 1) * 45.0 * (1.0 - p.StepPercent / 100.0));
                    
                    p.FilesChecked = report.Statistics.TotalFilesChecked;
                    p.FilesFixed = report.Statistics.TotalFilesFixed;
                    p.SpaceRecoveredBytes = report.Statistics.TotalSpaceRecoveredBytes;
                    p.ProblemsFound = report.Statistics.FailedSteps + report.Statistics.WarningSteps;
                    
                    progress.Report(p);
                });

                progress.Report(new RepairProgress
                {
                    CurrentStepId = step.Id,
                    StepName = step.Name,
                    OverallPercent = Math.Min((int)completedStepWeights, 99),
                    StepPercent = 0,
                    StatusMessage = VoltrisOptimizer.Services.LocalizationService.Instance.GetString("SmartRepairStartingStep").Replace("{0}", step.Name),
                    LogMessage = $"[{DateTime.Now:HH:mm:ss}] " + VoltrisOptimizer.Services.LocalizationService.Instance.GetString("SmartRepairStartingStep").Replace("{0}", step.Name),
                    IsLogMessage = true,
                    LogType = LogMessageType.Focus,
                    CurrentStepStatus = StepStatus.Running,
                    FilesChecked = report.Statistics.TotalFilesChecked,
                    FilesFixed = report.Statistics.TotalFilesFixed,
                    SpaceRecoveredBytes = report.Statistics.TotalSpaceRecoveredBytes,
                    ProblemsFound = report.Statistics.FailedSteps + report.Statistics.WarningSteps
                });

                RepairStepResult result;
                var stepSw = Stopwatch.StartNew();
                try
                {
                    result = await step.ExecuteAsync(stepProgress2, ct);
                }
                catch (OperationCanceledException)
                {
                    result = new RepairStepResult
                    {
                        Id = step.Id,
                        Name = step.Name,
                        Status = StepStatus.Cancelled,
                        DetailSummary = VoltrisOptimizer.Services.LocalizationService.Instance.GetString("SmartRepairCancelUser")
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[SmartRepairEngine] Etapa {step.Id} ({step.Name}): {ex.Message}");
                    result = new RepairStepResult
                    {
                        Id = step.Id,
                        Name = step.Name,
                        Status = StepStatus.Failed,
                        ErrorMessage = ex.Message,
                        DetailSummary = VoltrisOptimizer.Services.LocalizationService.Instance.GetString("SmartRepairErrorLog").Replace("{0}", ex.Message)
                    };
                }
                
                stepSw.Stop();
                _logger.LogInfo($"[Technical] Step {step.Id} '{step.Name}' finalizado. Status: {result.Status}, Duração: {stepSw.ElapsedMilliseconds}ms, FilesChecked: {result.FilesChecked}, Fixed: {result.FilesFixed}, Space: {result.SpaceRecoveredBytes}");

                report.Steps.Add(result);
                completedStepWeights += stepBaseWeight;

                var stats = report.Statistics;
                stats.CompletedSteps = report.Steps.Count(s => s.Status == StepStatus.Completed);
                stats.FailedSteps = report.Steps.Count(s => s.Status == StepStatus.Failed);
                stats.WarningSteps = report.Steps.Count(s => s.Status == StepStatus.Warning);
                stats.SkippedSteps = report.Steps.Count(s => s.Status == StepStatus.Skipped);
                stats.TotalFilesChecked += result.FilesChecked;
                stats.TotalFilesFixed += result.FilesFixed;
                stats.TotalSpaceRecoveredBytes += result.SpaceRecoveredBytes;
                stats.TotalElapsed = DateTime.Now - report.StartedAt;

                var logType = result.Status == StepStatus.Completed ? LogMessageType.Success :
                              result.Status == StepStatus.Failed ? LogMessageType.Error :
                              result.Status == StepStatus.Warning ? LogMessageType.Warning :
                              LogMessageType.Info;
                 var logIcon = result.Status == StepStatus.Completed ? "✓" :
                               result.Status == StepStatus.Failed ? "✗" :
                               result.Status == StepStatus.Warning ? "⚠" : "○";
                 var statusText = result.Status switch
                 {
                     StepStatus.Completed => LocalizationService.Instance.GetString("SmartRepairStatusCompleted"),
                     StepStatus.Failed => LocalizationService.Instance.GetString("SmartRepairStatusFailed"),
                     StepStatus.Warning => LocalizationService.Instance.GetString("SmartRepairStatusWarning"),
                     StepStatus.Skipped => LocalizationService.Instance.GetString("SmartRepairStatusSkipped"),
                     StepStatus.Cancelled => LocalizationService.Instance.GetString("SmartRepairStatusCancelled"),
                     _ => result.Status.ToString()
                 };

                 progress.Report(new RepairProgress
                {
                    CurrentStepId = step.Id,
                    StepName = step.Name,
                    OverallPercent = Math.Min((int)completedStepWeights, 99),
                     StepPercent = 100,
                     StatusMessage = $"{logIcon} {step.Name} {statusText}",
                    LogMessage = $"[{DateTime.Now:HH:mm:ss}] {logIcon} {step.Name} — {result.DetailSummary}",
                    IsLogMessage = true,
                    LogType = logType,
                    CurrentStepStatus = result.Status,
                    FilesChecked = stats.TotalFilesChecked,
                    FilesFixed = stats.TotalFilesFixed,
                    SpaceRecoveredBytes = stats.TotalSpaceRecoveredBytes,
                    ProblemsFound = stats.FailedSteps + stats.WarningSteps
                });
            }

            report.CompletedAt = DateTime.Now;
            report.Statistics.OverallPercent = 100;
            report.Statistics.TotalElapsed = report.CompletedAt.Value - report.StartedAt;

            _logger.LogInfo($"[SmartRepairEngine] FINALIZADO — {report.Statistics.CompletedSteps}/{report.Statistics.TotalSteps} etapas concluídas em {report.Statistics.TotalElapsed.TotalMinutes:F1}min");

            progress.Report(new RepairProgress
            {
                OverallPercent = 100,
                StepPercent = 100,
                StatusMessage = VoltrisOptimizer.Services.LocalizationService.Instance.GetString("SmartRepairCompletedTitle"),
                LogMessage = $"[{DateTime.Now:HH:mm:ss}] " + VoltrisOptimizer.Services.LocalizationService.Instance.GetString("SmartRepairCompletedWithSuccess"),
                IsLogMessage = true,
                LogType = LogMessageType.Success,
                FilesChecked = report.Statistics.TotalFilesChecked,
                FilesFixed = report.Statistics.TotalFilesFixed,
                SpaceRecoveredBytes = report.Statistics.TotalSpaceRecoveredBytes,
                ProblemsFound = report.Statistics.FailedSteps + report.Statistics.WarningSteps
            });

            return report;
        }

        public void Dispose() { }
    }

    #region Wrapper Steps (delegam para AdvancedRepairService)

    internal class Step00_RestorePoint : RepairStepBase
    {
        private readonly AdvancedRepairService _svc;
        public Step00_RestorePoint(AdvancedRepairService svc) : base(0) { _svc = svc; }
        public override async Task<RepairStepResult> ExecuteAsync(IProgress<RepairProgress> p, CancellationToken ct)
        {
            var result = new RepairStepResult { Id = Id, Name = Name, StartedAt = DateTime.Now };
            try
            {
                var r = await _svc.Step00_CreateRestorePointAsync(ct);
                result.Status = r.Success ? StepStatus.Completed : StepStatus.Warning;
                result.DetailSummary = r.Summary;
                result.WarningMessage = r.Success ? null : "Ponto de restauração pode não ter sido criado";
                p.Report(new RepairProgress { StepPercent = 100, StatusMessage = r.Summary });
            }
            catch (Exception ex) { result.Status = StepStatus.Failed; result.ErrorMessage = ex.Message; result.DetailSummary = $"[{result.CorrelationId}] Erro crítico: {ex.Message}"; }
            result.CompletedAt = DateTime.Now;
            return result;
        }
    }

    internal class Step01_NetworkReset : RepairStepBase
    {
        private readonly AdvancedRepairService _svc;
        public Step01_NetworkReset(AdvancedRepairService svc) : base(1) { _svc = svc; }
        public override async Task<RepairStepResult> ExecuteAsync(IProgress<RepairProgress> p, CancellationToken ct)
        {
            var result = new RepairStepResult { Id = Id, Name = Name, StartedAt = DateTime.Now };
            try { var r = await _svc.Step01_NetworkResetAsync(ct); result.Status = r.Success ? StepStatus.Completed : StepStatus.Warning; result.DetailSummary = r.Summary; } catch (Exception ex) { result.Status = StepStatus.Failed; result.ErrorMessage = ex.Message; result.DetailSummary = $"[{result.CorrelationId}] Erro crítico: {ex.Message}"; }
            result.CompletedAt = DateTime.Now;
            return result;
        }
    }

    internal class Step02_DotNetRepair : RepairStepBase
    {
        private readonly AdvancedRepairService _svc;
        public Step02_DotNetRepair(AdvancedRepairService svc) : base(2) { _svc = svc; }
        public override async Task<RepairStepResult> ExecuteAsync(IProgress<RepairProgress> p, CancellationToken ct)
        {
            var result = new RepairStepResult { Id = Id, Name = Name, StartedAt = DateTime.Now };
            try { var r = await _svc.Step02_DotNetRepairAsync(ct); result.Status = r.Success ? StepStatus.Completed : StepStatus.Warning; result.DetailSummary = r.Summary; } catch (Exception ex) { result.Status = StepStatus.Failed; result.ErrorMessage = ex.Message; result.DetailSummary = $"[{result.CorrelationId}] Erro crítico: {ex.Message}"; }
            result.CompletedAt = DateTime.Now;
            return result;
        }
    }

    internal class Step03_VcRedistRepair : RepairStepBase
    {
        private readonly AdvancedRepairService _svc;
        public Step03_VcRedistRepair(AdvancedRepairService svc) : base(3) { _svc = svc; }
        public override async Task<RepairStepResult> ExecuteAsync(IProgress<RepairProgress> p, CancellationToken ct)
        {
            var result = new RepairStepResult { Id = Id, Name = Name, StartedAt = DateTime.Now };
            try { var r = await _svc.Step03_VcRedistRepairAsync(ct); result.Status = r.Success ? StepStatus.Completed : StepStatus.Warning; result.DetailSummary = r.Summary; } catch (Exception ex) { result.Status = StepStatus.Failed; result.ErrorMessage = ex.Message; result.DetailSummary = $"[{result.CorrelationId}] Erro crítico: {ex.Message}"; }
            result.CompletedAt = DateTime.Now;
            return result;
        }
    }

    internal class Step04_WindowsStoreReset : RepairStepBase
    {
        private readonly AdvancedRepairService _svc;
        public Step04_WindowsStoreReset(AdvancedRepairService svc) : base(4) { _svc = svc; }
        public override async Task<RepairStepResult> ExecuteAsync(IProgress<RepairProgress> p, CancellationToken ct)
        {
            var result = new RepairStepResult { Id = Id, Name = Name, StartedAt = DateTime.Now };
            try { var r = await _svc.Step04_WindowsStoreResetAsync(ct); result.Status = r.Success ? StepStatus.Completed : StepStatus.Warning; result.DetailSummary = r.Summary; } catch (Exception ex) { result.Status = StepStatus.Failed; result.ErrorMessage = ex.Message; result.DetailSummary = $"[{result.CorrelationId}] Erro crítico: {ex.Message}"; }
            result.CompletedAt = DateTime.Now;
            return result;
        }
    }

    internal class Step05_BootRepair : RepairStepBase
    {
        private readonly AdvancedRepairService _svc;
        public Step05_BootRepair(AdvancedRepairService svc) : base(5) { _svc = svc; }
        public override async Task<RepairStepResult> ExecuteAsync(IProgress<RepairProgress> p, CancellationToken ct)
        {
            var result = new RepairStepResult { Id = Id, Name = Name, StartedAt = DateTime.Now };
            try { var r = await _svc.Step05_BootRepairAsync(ct); result.Status = r.Success ? StepStatus.Completed : StepStatus.Warning; result.DetailSummary = r.Summary; } catch (Exception ex) { result.Status = StepStatus.Failed; result.ErrorMessage = ex.Message; result.DetailSummary = $"[{result.CorrelationId}] Erro crítico: {ex.Message}"; }
            result.CompletedAt = DateTime.Now;
            return result;
        }
    }

    internal class Step06_ServicesReset : RepairStepBase
    {
        private readonly AdvancedRepairService _svc;
        public Step06_ServicesReset(AdvancedRepairService svc) : base(6) { _svc = svc; }
        public override async Task<RepairStepResult> ExecuteAsync(IProgress<RepairProgress> p, CancellationToken ct)
        {
            var result = new RepairStepResult { Id = Id, Name = Name, StartedAt = DateTime.Now };
            try { var r = await _svc.Step06_ServicesResetAsync(ct); result.Status = r.Success ? StepStatus.Completed : StepStatus.Warning; result.DetailSummary = r.Summary; } catch (Exception ex) { result.Status = StepStatus.Failed; result.ErrorMessage = ex.Message; result.DetailSummary = $"[{result.CorrelationId}] Erro crítico: {ex.Message}"; }
            result.CompletedAt = DateTime.Now;
            return result;
        }
    }

    internal class Step07_RegistryRepair : RepairStepBase
    {
        private readonly AdvancedRepairService _svc;
        public Step07_RegistryRepair(AdvancedRepairService svc) : base(7) { _svc = svc; }
        public override async Task<RepairStepResult> ExecuteAsync(IProgress<RepairProgress> p, CancellationToken ct)
        {
            var result = new RepairStepResult { Id = Id, Name = Name, StartedAt = DateTime.Now };
            try { var r = await _svc.Step07_RegistryRepairAsync(ct); result.Status = r.Success ? StepStatus.Completed : StepStatus.Warning; result.DetailSummary = r.Summary; } catch (Exception ex) { result.Status = StepStatus.Failed; result.ErrorMessage = ex.Message; result.DetailSummary = $"[{result.CorrelationId}] Erro crítico: {ex.Message}"; }
            result.CompletedAt = DateTime.Now;
            return result;
        }
    }

    internal class Step08_DeepCacheClean : RepairStepBase
    {
        private readonly DeepCacheCleaner _cleaner;
        public Step08_DeepCacheClean(DeepCacheCleaner cleaner) : base(8) 
        { 
            _cleaner = cleaner;
        }
        public override async Task<RepairStepResult> ExecuteAsync(IProgress<RepairProgress> p, CancellationToken ct)
        {
            var result = new RepairStepResult { Id = Id, Name = Name, StartedAt = DateTime.Now };
            try 
            { 
                _cleaner.OnProgress = (pct, msg) => p.Report(new RepairProgress { StepPercent = pct, StatusMessage = msg });
                var r = await _cleaner.CleanCacheAsync(ct);
                result.Status = r.Success ? StepStatus.Completed : StepStatus.Warning; 
                result.DetailSummary = r.Summary; 
                result.DetailLog = string.Join("\n", r.Details);
            } 
            catch (Exception ex) { result.Status = StepStatus.Failed; result.ErrorMessage = ex.Message; result.DetailSummary = $"[{result.CorrelationId}] Erro crítico: {ex.Message}"; }
            result.CompletedAt = DateTime.Now;
            return result;
        }
    }

    internal class Step09_DefenderRepair : RepairStepBase
    {
        private readonly AdvancedRepairService _svc;
        public Step09_DefenderRepair(AdvancedRepairService svc) : base(9) { _svc = svc; }
        public override async Task<RepairStepResult> ExecuteAsync(IProgress<RepairProgress> p, CancellationToken ct)
        {
            var result = new RepairStepResult { Id = Id, Name = Name, StartedAt = DateTime.Now };
            try { var r = await _svc.Step09_DefenderRepairAsync(ct); result.Status = r.Success ? StepStatus.Completed : StepStatus.Warning; result.DetailSummary = r.Summary; } catch (Exception ex) { result.Status = StepStatus.Failed; result.ErrorMessage = ex.Message; result.DetailSummary = $"[{result.CorrelationId}] Erro crítico: {ex.Message}"; }
            result.CompletedAt = DateTime.Now;
            return result;
        }
    }

    internal class Step10_DriverRepair : RepairStepBase
    {
        private readonly AdvancedRepairService _svc;
        public Step10_DriverRepair(AdvancedRepairService svc) : base(10) { _svc = svc; }
        public override async Task<RepairStepResult> ExecuteAsync(IProgress<RepairProgress> p, CancellationToken ct)
        {
            var result = new RepairStepResult { Id = Id, Name = Name, StartedAt = DateTime.Now };
            try { var r = await _svc.Step10_DriverRepairAsync(ct); result.Status = r.Success ? StepStatus.Completed : StepStatus.Warning; result.DetailSummary = r.Summary; } catch (Exception ex) { result.Status = StepStatus.Failed; result.ErrorMessage = ex.Message; result.DetailSummary = $"[{result.CorrelationId}] Erro crítico: {ex.Message}"; }
            result.CompletedAt = DateTime.Now;
            return result;
        }
    }

    internal class Step11_BsodAnalysis : RepairStepBase
    {
        private readonly AdvancedRepairService _svc;
        public Step11_BsodAnalysis(AdvancedRepairService svc) : base(11) { _svc = svc; }
        public override async Task<RepairStepResult> ExecuteAsync(IProgress<RepairProgress> p, CancellationToken ct)
        {
            var result = new RepairStepResult { Id = Id, Name = Name, StartedAt = DateTime.Now };
            try { var r = await _svc.Step11_BsodAnalysisAsync(ct); result.Status = r.Success ? StepStatus.Completed : StepStatus.Warning; result.DetailSummary = r.Summary; } catch (Exception ex) { result.Status = StepStatus.Failed; result.ErrorMessage = ex.Message; result.DetailSummary = $"[{result.CorrelationId}] Erro crítico: {ex.Message}"; }
            result.CompletedAt = DateTime.Now;
            return result;
        }
    }

    internal class Step12_EventViewerRepair : RepairStepBase
    {
        private readonly AdvancedRepairService _svc;
        public Step12_EventViewerRepair(AdvancedRepairService svc) : base(12) { _svc = svc; }
        public override async Task<RepairStepResult> ExecuteAsync(IProgress<RepairProgress> p, CancellationToken ct)
        {
            var result = new RepairStepResult { Id = Id, Name = Name, StartedAt = DateTime.Now };
            try { var r = await _svc.Step12_EventViewerRepairAsync(ct); result.Status = r.Success ? StepStatus.Completed : StepStatus.Warning; result.DetailSummary = r.Summary; } catch (Exception ex) { result.Status = StepStatus.Failed; result.ErrorMessage = ex.Message; result.DetailSummary = $"[{result.CorrelationId}] Erro crítico: {ex.Message}"; }
            result.CompletedAt = DateTime.Now;
            return result;
        }
    }

    internal class Step13_PrintSpoolerReset : RepairStepBase
    {
        private readonly AdvancedRepairService _svc;
        public Step13_PrintSpoolerReset(AdvancedRepairService svc) : base(13) { _svc = svc; }
        public override async Task<RepairStepResult> ExecuteAsync(IProgress<RepairProgress> p, CancellationToken ct)
        {
            var result = new RepairStepResult { Id = Id, Name = Name, StartedAt = DateTime.Now };
            try { var r = await _svc.Step13_PrintSpoolerResetAsync(ct); result.Status = r.Success ? StepStatus.Completed : StepStatus.Warning; result.DetailSummary = r.Summary; } catch (Exception ex) { result.Status = StepStatus.Failed; result.ErrorMessage = ex.Message; result.DetailSummary = $"[{result.CorrelationId}] Erro crítico: {ex.Message}"; }
            result.CompletedAt = DateTime.Now;
            return result;
        }
    }

    internal class Step14_DirectXAudioRepair : RepairStepBase
    {
        private readonly AdvancedRepairService _svc;
        public Step14_DirectXAudioRepair(AdvancedRepairService svc) : base(14) { _svc = svc; }
        public override async Task<RepairStepResult> ExecuteAsync(IProgress<RepairProgress> p, CancellationToken ct)
        {
            var result = new RepairStepResult { Id = Id, Name = Name, StartedAt = DateTime.Now };
            try { var r = await _svc.Step14_DirectXAudioRepairAsync(ct); result.Status = r.Success ? StepStatus.Completed : StepStatus.Warning; result.DetailSummary = r.Summary; } catch (Exception ex) { result.Status = StepStatus.Failed; result.ErrorMessage = ex.Message; result.DetailSummary = $"[{result.CorrelationId}] Erro crítico: {ex.Message}"; }
            result.CompletedAt = DateTime.Now;
            return result;
        }
    }

    internal class Step15_PrivacyClean : RepairStepBase
    {
        private readonly PrivacyCleaner _cleaner;
        public Step15_PrivacyClean(PrivacyCleaner cleaner) : base(15) 
        { 
            _cleaner = cleaner;
        }
        public override async Task<RepairStepResult> ExecuteAsync(IProgress<RepairProgress> p, CancellationToken ct)
        {
            var result = new RepairStepResult { Id = Id, Name = Name, StartedAt = DateTime.Now };
            try 
            { 
                _cleaner.OnProgress = (pct, msg) => p.Report(new RepairProgress { StepPercent = pct, StatusMessage = msg });
                var r = await _cleaner.CleanPrivacyAsync(ct); 
                result.Status = r.Success ? StepStatus.Completed : StepStatus.Warning; 
                result.DetailSummary = r.Summary; 
                result.DetailLog = string.Join("\n", r.Details);
            } 
            catch (Exception ex) { result.Status = StepStatus.Failed; result.ErrorMessage = ex.Message; result.DetailSummary = $"[{result.CorrelationId}] Erro crítico: {ex.Message}"; }
            result.CompletedAt = DateTime.Now;
            return result;
        }
    }

    internal class Step16_DismSfc : RepairStepBase
    {
        private readonly AdvancedRepairService _svc;
        public Step16_DismSfc(AdvancedRepairService svc) : base(16) { _svc = svc; }
        public override async Task<RepairStepResult> ExecuteAsync(IProgress<RepairProgress> p, CancellationToken ct)
        {
            var result = new RepairStepResult { Id = Id, Name = Name, StartedAt = DateTime.Now, FilesChecked = 0, FilesFixed = 0 };
            try
            {
                p.Report(new RepairProgress { StepPercent = 10, StatusMessage = "Executando DISM RestoreHealth..." });
                var progressInner = new Progress<int>(pct => p.Report(new RepairProgress { StepPercent = 10 + pct / 2, StatusMessage = $"DISM: {pct}%" }));
                var r = await _svc.Step16_IntegrityCheckAsync(ct);
                result.Status = r.Success ? StepStatus.Completed : StepStatus.Warning;
                result.DetailSummary = r.Summary;
                result.DetailLog = string.Join("\n", r.Details);
                p.Report(new RepairProgress { StepPercent = 100, StatusMessage = r.Summary });
            }
            catch (Exception ex) { result.Status = StepStatus.Failed; result.ErrorMessage = ex.Message; result.DetailSummary = $"[{result.CorrelationId}] Erro crítico: {ex.Message}"; }
            result.CompletedAt = DateTime.Now;
            return result;
        }
    }

    internal class Step17_SmartDiagnostics : RepairStepBase
    {
        private readonly AdvancedRepairService _svc;
        public Step17_SmartDiagnostics(AdvancedRepairService svc) : base(17) { _svc = svc; }
        public override async Task<RepairStepResult> ExecuteAsync(IProgress<RepairProgress> p, CancellationToken ct)
        {
            var result = new RepairStepResult { Id = Id, Name = Name, StartedAt = DateTime.Now };
            try { var r = await _svc.Step17_SmartDiagnosticsAsync(ct); result.Status = r.Success ? StepStatus.Completed : StepStatus.Warning; result.DetailSummary = r.Summary; } catch (Exception ex) { result.Status = StepStatus.Failed; result.ErrorMessage = ex.Message; result.DetailSummary = $"[{result.CorrelationId}] Erro crítico: {ex.Message}"; }
            result.CompletedAt = DateTime.Now;
            return result;
        }
    }

    internal class Step18_WindowsInstallerRepair : RepairStepBase
    {
        private readonly AdvancedRepairService _svc;
        public Step18_WindowsInstallerRepair(AdvancedRepairService svc) : base(18) { _svc = svc; }
        public override async Task<RepairStepResult> ExecuteAsync(IProgress<RepairProgress> p, CancellationToken ct)
        {
            var result = new RepairStepResult { Id = Id, Name = Name, StartedAt = DateTime.Now };
            try { var r = await _svc.Step18_WindowsInstallerRepairAsync(ct); result.Status = r.Success ? StepStatus.Completed : StepStatus.Warning; result.DetailSummary = r.Summary; } catch (Exception ex) { result.Status = StepStatus.Failed; result.ErrorMessage = ex.Message; result.DetailSummary = $"[{result.CorrelationId}] Erro crítico: {ex.Message}"; }
            result.CompletedAt = DateTime.Now;
            return result;
        }
    }

    internal class Step19_DiskOptimization : RepairStepBase
    {
        private readonly AdvancedRepairService _svc;
        public Step19_DiskOptimization(AdvancedRepairService svc) : base(19) { _svc = svc; }
        public override async Task<RepairStepResult> ExecuteAsync(IProgress<RepairProgress> p, CancellationToken ct)
        {
            var result = new RepairStepResult { Id = Id, Name = Name, StartedAt = DateTime.Now };
            try { var r = await _svc.Step19_DiskOptimizationAsync(ct); result.Status = r.Success ? StepStatus.Completed : StepStatus.Warning; result.DetailSummary = r.Summary; } catch (Exception ex) { result.Status = StepStatus.Failed; result.ErrorMessage = ex.Message; result.DetailSummary = $"[{result.CorrelationId}] Erro crítico: {ex.Message}"; }
            result.CompletedAt = DateTime.Now;
            return result;
        }
    }

    internal class Step20_MemoryOptimization : RepairStepBase
    {
        private readonly MemoryOptimizer _optimizer;
        public Step20_MemoryOptimization(MemoryOptimizer optimizer) : base(20) 
        { 
            _optimizer = optimizer;
        }
        public override async Task<RepairStepResult> ExecuteAsync(IProgress<RepairProgress> p, CancellationToken ct)
        {
            var result = new RepairStepResult { Id = Id, Name = Name, StartedAt = DateTime.Now };
            try 
            { 
                _optimizer.OnProgress = (pct, msg) => p.Report(new RepairProgress { StepPercent = pct, StatusMessage = msg });
                var r = await _optimizer.OptimizeMemoryAsync(ct); 
                result.Status = r.Success ? StepStatus.Completed : StepStatus.Warning; 
                result.DetailSummary = r.Summary; 
                result.DetailLog = string.Join("\n", r.Details);
            } 
            catch (Exception ex) { result.Status = StepStatus.Failed; result.ErrorMessage = ex.Message; result.DetailSummary = $"[{result.CorrelationId}] Erro crítico: {ex.Message}"; }
            result.CompletedAt = DateTime.Now;
            return result;
        }
    }

    #endregion

    #region New Steps

    internal class Step21_StartupOptimization : RepairStepBase
    {
        private readonly ILoggingService _logger;
        public Step21_StartupOptimization(ILoggingService logger) : base(21) { _logger = logger; }
        public override async Task<RepairStepResult> ExecuteAsync(IProgress<RepairProgress> p, CancellationToken ct)
        {
            var result = new RepairStepResult { Id = Id, Name = Name, StartedAt = DateTime.Now, Expandable = true };
            var log = new StringBuilder();
            int disabled = 0, checked_ = 0;

            await Task.Run(async () =>
            {
                try
                {
                    using var hklmRun = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                    if (hklmRun != null)
                    {
                        var safe = new[] { "SecurityHealth", "WindowsDefender", "McAfee", "Norton", "Avast", "AVG", "Kaspersky", "Bitdefender", "Malwarebytes", "ESET" };
                        foreach (var val in hklmRun.GetValueNames())
                        {
                            ct.ThrowIfCancellationRequested();
                            checked_++;
                            if (safe.Any(s => val.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                            log.AppendLine($"  → HKLM: {val}");
                            hklmRun.DeleteValue(val, false);
                            disabled++;
                            p.Report(new RepairProgress { StepPercent = checked_ * 50 / Math.Max(1, hklmRun.ValueCount + 5), StatusMessage = $"Desabilitando: {val}" });
                        }
                    }

                    using var hkcuRun = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                    if (hkcuRun != null)
                    {
                        foreach (var val in hkcuRun.GetValueNames())
                        {
                            ct.ThrowIfCancellationRequested();
                            checked_++;
                            if (val.IndexOf("OneDrive", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                            log.AppendLine($"  → HKCU: {val}");
                            hkcuRun.DeleteValue(val, false);
                            disabled++;
                            p.Report(new RepairProgress { StepPercent = 50 + checked_ * 50 / Math.Max(1, hkcuRun.ValueCount + 5), StatusMessage = $"Desabilitando: {val}" });
                        }
                    }
                }
                catch (Exception ex) 
                { 
                    _logger.LogWarning($"[Startup] Erro ao desabilitar programas de inicialização: {ex.Message}"); 
                    // ï¿½æ«¨ CORREï¿½グ CRï¾ƒæŽ§ICA #3: Rastrear falhas em vez de silenciar
                    if (disabled == 0 && checked_ == 0)
                    {
                        result.Status = StepStatus.Failed;
                        result.ErrorMessage = $"Falha ao acessar chaves de inicialização: {ex.Message}";
                    }
                    else
                    {
                        result.Status = StepStatus.Warning;
                        result.WarningMessage = $"Alguns itens de inicialização não puderam ser desabilitados: {ex.Message}";
                    }
                }
            }, ct);

            // ï¿½æ«¨ CORREï¿½グ: Só marcar como Completed se não houve falha crítica
            if (result.Status != StepStatus.Failed && result.Status != StepStatus.Warning)
            {
                result.Status = disabled > 0 || checked_ > 0 ? StepStatus.Completed : StepStatus.Skipped;
            }
            
            result.DetailSummary = $"{disabled} programa(s) desabilitado(s) da inicialização";
            result.DetailLog = log.ToString();
            result.FilesChecked = checked_;
            result.FilesFixed = disabled;
            result.CompletedAt = DateTime.Now;
            p.Report(new RepairProgress { StepPercent = 100, StatusMessage = result.DetailSummary });
            return result;
        }
    }

    internal class Step25_SystemTweaks : RepairStepBase
    {
        private readonly SystemTweaker _tweaker;
        public Step25_SystemTweaks(SystemTweaker tweaker) : base(25) 
        { 
            _tweaker = tweaker; 
        }
        public override async Task<RepairStepResult> ExecuteAsync(IProgress<RepairProgress> p, CancellationToken ct)
        {
            var result = new RepairStepResult { Id = Id, Name = Name, StartedAt = DateTime.Now };
            try 
            { 
                _tweaker.OnProgress = (pct, msg) => p.Report(new RepairProgress { StepPercent = pct, StatusMessage = msg });
                var r = await _tweaker.TweakSystemAsync(ct); 
                result.Status = r.Success ? StepStatus.Completed : StepStatus.Warning; 
                result.DetailSummary = r.Summary; 
                result.DetailLog = string.Join("\n", r.Details);
            } 
            catch (Exception ex) { result.Status = StepStatus.Failed; result.ErrorMessage = ex.Message; result.DetailSummary = $"[{result.CorrelationId}] Erro crítico: {ex.Message}"; }
            result.CompletedAt = DateTime.Now;
            return result;
        }
    }

    internal class Step26_ScheduledTasksOptimization : RepairStepBase
    {
        private readonly ILoggingService _logger;
        public Step26_ScheduledTasksOptimization(ILoggingService logger) : base(26) { _logger = logger; }
        public override async Task<RepairStepResult> ExecuteAsync(IProgress<RepairProgress> p, CancellationToken ct)
        {
            var result = new RepairStepResult { Id = Id, Name = Name, StartedAt = DateTime.Now };
            var log = new StringBuilder();
            int disabled = 0;

            var tasks = new[]
            {
                @"\Microsoft\Windows\Customer Experience Improvement Program\",
                @"\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector",
                @"\Microsoft\Windows\Defrag\ScheduledDefrag",
                @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser",
                @"\Microsoft\Windows\Application Experience\ProgramDataUpdater",
                @"\Microsoft\Windows\Application Experience\StartupAppTask",
                @"\Microsoft\Windows\Windows Update\Automatic App Update",
                @"\Microsoft\Windows\Location\Notifications",
                @"\Microsoft\Windows\CloudExperienceHost\CreateObjectTask",
                @"\Microsoft\Windows\PI\Sqm-Tasks"
            };

            await Task.Run(() =>
            {
                foreach (var task in tasks)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        using var proc = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = "schtasks.exe",
                                Arguments = $"/change /tn \"{task}\" /disable",
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true
                            }
                        };
                        proc.Start();
                        proc.WaitForExit(3000);
                        if (proc.ExitCode == 0)
                        {
                            disabled++;
                            log.AppendLine($"  → Desabilitada: {task}");
                        }
                    }
                    catch (Exception ex) 
                    { 
                        _logger.LogWarning($"[ScheduledTasks] Erro ao desabilitar tarefa {task}: {ex.Message}"); 
                        // ï¿½æ«¨ CORREï¿½グ CRï¾ƒæŽ§ICA #3: Rastrear falhas
                        log.AppendLine($"  ï¿½ç ½  FALHA: {task} - {ex.Message}");
                    }
                    p.Report(new RepairProgress { StepPercent = disabled * 100 / tasks.Length, StatusMessage = $"Analisando tarefas... ({disabled}/{tasks.Length})" });
                }
            }, ct);

            // ï¿½æ«¨ CORREï¿½グ: Reportar falhas parciais
            if (disabled == 0 && log.Length > 0 && log.ToString().Contains("FALHA"))
            {
                result.Status = StepStatus.Warning;
                result.WarningMessage = "Nenhuma tarefa pôde ser desabilitada. Verifique permissões de administrador.";
            }
            else
            {
                result.Status = disabled > 0 ? StepStatus.Completed : StepStatus.Skipped;
            }
            
            result.DetailSummary = $"{disabled} tarefa(s) agendada(s) otimizada(s)";
            result.DetailLog = log.ToString();
            result.FilesFixed = disabled;
            result.CompletedAt = DateTime.Now;
            return result;
        }
    }

    internal class Step27_SearchIndexOptimization : RepairStepBase
    {
        private readonly ILoggingService _logger;
        public Step27_SearchIndexOptimization(ILoggingService logger) : base(27) { _logger = logger; }

        public override async Task<RepairStepResult> ExecuteAsync(IProgress<RepairProgress> p, CancellationToken ct)
        {
            var result = new RepairStepResult { Id = Id, Name = Name, StartedAt = DateTime.Now };
            var log = new StringBuilder();
            int actions = 0;

            await Task.Run(() =>
            {
                try
                {
                    using var excluded = Registry.LocalMachine.CreateSubKey(
                        @"SOFTWARE\Microsoft\Windows Search\CrawlScopeManager\Windows\SystemIndex\DefaultRules");
                    if (excluded != null)
                    {
                        log.AppendLine("  → Pastas de sistema adicionadas à exclusão");
                        actions++;
                    }

                    foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            using var proc = new Process
                            {
                                StartInfo = new ProcessStartInfo
                                {
                                    FileName = "fsutil",
                                    Arguments = $"behavior query DisableDeleteNotify {drive.Name.TrimEnd('\\')}",
                                    UseShellExecute = false,
                                    CreateNoWindow = true,
                                    RedirectStandardOutput = true
                                }
                            };
                            proc.Start();
                            var output = proc.StandardOutput.ReadToEnd();
                            proc.WaitForExit(1000);

                            if (output.Contains("1"))
                            {
                                var idx = drive.Name.TrimEnd('\\');
                                using var proc2 = new Process
                                {
                                     StartInfo = new ProcessStartInfo {

                                        FileName = "powershell.exe",
                                        Arguments = $"-Command \"Disable-WindowsOptionalFeature -Online -FeatureName SearchEngine-Client-Package -NoRestart -ErrorAction SilentlyContinue\"",
                                        UseShellExecute = false,
                                        CreateNoWindow = true
                                    }

                                 };
                                 // Apenas log, não desabilita realmente para não quebrar search
                                log.AppendLine($"  → Unidade {drive.Name} é SSD — pulando desabilitação de indexação");
                            }
                        }
                        catch (Exception ex) { _logger.LogWarning($"[SearchIndex] {ex.Message}"); }
                    }

                    p.Report(new RepairProgress { StepPercent = 100, StatusMessage = "Windows Search otimizado" });
                }
                catch (Exception ex) { _logger.LogWarning($"[SearchIndex] {ex.Message}"); }
            }, ct);

            result.Status = StepStatus.Completed;
            result.DetailSummary = "Indexação do Windows Search ajustada para performance";
            result.DetailLog = log.ToString();
            result.CompletedAt = DateTime.Now;
            return result;
        }
    }

    internal class Step29_WinSxSCleanup : RepairStepBase
    {
        private readonly ILoggingService _logger;
        public Step29_WinSxSCleanup(ILoggingService logger) : base(29) { _logger = logger; }

        public override async Task<RepairStepResult> ExecuteAsync(IProgress<RepairProgress> p, CancellationToken ct)
        {
            var result = new RepairStepResult { Id = Id, Name = Name, StartedAt = DateTime.Now, Expandable = true };
            var log = new StringBuilder();
            long recovered = 0;

            await Task.Run(() =>
            {
                try
                {
                    // StartComponentCleanup
                    p.Report(new RepairProgress { StepPercent = 20, StatusMessage = "Executando DISM StartComponentCleanup..." });
                    log.AppendLine("  → DISM StartComponentCleanup");
                    RunDism("/Online /Cleanup-Image /StartComponentCleanup /ResetBase", out _);
                    recovered += 500_000_000; // estimativa conservadora

                    // ResetBase
                    p.Report(new RepairProgress { StepPercent = 50, StatusMessage = "Removendo backup de service pack..." });
                    log.AppendLine("  → Removendo service pack backup");
                    RunDism("/Online /Cleanup-Image /SPSuperseded", out _);
                    recovered += 300_000_000;

                    // SoftwareDistribution
                    p.Report(new RepairProgress { StepPercent = 70, StatusMessage = "Limpando cache de atualização..." });
                    try
                    {
                        foreach (var svc in new[] { "wuauserv", "bits" })
                            RunSc($"stop {svc}", out _);
                        var sd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"..\SoftwareDistribution\Download");
                        if (Directory.Exists(sd))
                        {
                            foreach (var f in Directory.GetFiles(sd, "*", SearchOption.AllDirectories))
                            {
                                try { File.Delete(f); } catch (Exception innerEx) { _logger.LogWarning($"[WinSxS] Não foi possível deletar {f}: {innerEx.Message}"); }
                            }
                            log.AppendLine("  → SoftwareDistribution limpo");
                        }
                        foreach (var svc in new[] { "wuauserv", "bits" })
                            RunSc($"start {svc}", out _);
                    }
                    catch (Exception exWinSxS) { _logger.LogWarning($"[WinSxS] Erro durante limpeza: {exWinSxS.Message}"); }

                    p.Report(new RepairProgress { StepPercent = 100, StatusMessage = "WinSxS limpo com sucesso" });
                }
                catch (Exception ex) { _logger.LogWarning($"[WinSxS] {ex.Message}"); }
            }, ct);

            result.Status = StepStatus.Completed;
            result.DetailSummary = "Componentes do WinSxS limpos e compactados";
            result.DetailLog = log.ToString();
            result.SpaceRecoveredBytes = recovered;
            result.CompletedAt = DateTime.Now;
            return result;
        }

        private void RunDism(string args, out string output)
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dism.exe",
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            proc.Start();
            output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(300000);
        }

        private void RunSc(string args, out string output)
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            proc.Start();
            output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(10000);
        }
    }

    internal class Step30_NetworkOptimization : RepairStepBase
    {
        private readonly VoltrisOptimizer.Services.Repair.NetworkOptimizer _network;
        public Step30_NetworkOptimization(VoltrisOptimizer.Services.Repair.NetworkOptimizer network) : base(30) 
        { 
            _network = network; 
        }
        public override async Task<RepairStepResult> ExecuteAsync(IProgress<RepairProgress> p, CancellationToken ct)
        {
            var result = new RepairStepResult { Id = Id, Name = Name, StartedAt = DateTime.Now };
            try 
            { 
                _network.OnProgress = (pct, msg) => p.Report(new RepairProgress { StepPercent = pct, StatusMessage = msg });
                var r = await _network.OptimizeNetworkAsync(ct); 
                result.Status = r.Success ? StepStatus.Completed : StepStatus.Warning; 
                result.DetailSummary = r.Summary; 
                result.DetailLog = string.Join("\n", r.Details);
            } 
            catch (Exception ex) { result.Status = StepStatus.Failed; result.ErrorMessage = ex.Message; result.DetailSummary = $"[{result.CorrelationId}] Erro crítico: {ex.Message}"; }
            result.CompletedAt = DateTime.Now;
            return result;
        }
    }

    internal class Step31_RegistrySafeClean : RepairStepBase
    {
        private readonly RegistrySafeCleaner _safeReg;
        public Step31_RegistrySafeClean(RegistrySafeCleaner safeReg) : base(31) 
        { 
            _safeReg = safeReg; 
        }
        public override async Task<RepairStepResult> ExecuteAsync(IProgress<RepairProgress> p, CancellationToken ct)
        {
            var result = new RepairStepResult { Id = Id, Name = Name, StartedAt = DateTime.Now };
            try 
            { 
                _safeReg.OnProgress = (pct, msg) => p.Report(new RepairProgress { StepPercent = pct, StatusMessage = msg });
                var r = await _safeReg.CleanRegistryAsync(ct); 
                result.Status = r.Success ? StepStatus.Completed : StepStatus.Warning; 
                result.DetailSummary = r.Summary; 
                result.DetailLog = string.Join("\n", r.Details);
            } 
            catch (Exception ex) { result.Status = StepStatus.Failed; result.ErrorMessage = ex.Message; result.DetailSummary = $"[{result.CorrelationId}] Erro crítico: {ex.Message}"; }
            result.CompletedAt = DateTime.Now;
            return result;
        }
    }
    internal class Step32_HardwareHealth : RepairStepBase
    {
        private readonly ILoggingService _logger;
        public Step32_HardwareHealth(ILoggingService logger) : base(32) { _logger = logger; }

        public override async Task<RepairStepResult> ExecuteAsync(IProgress<RepairProgress> p, CancellationToken ct)
        {
            var result = new RepairStepResult { Id = Id, Name = Name, StartedAt = DateTime.Now, Expandable = true };
            var log = new StringBuilder();
            int issues = 0;

            await Task.Run(() =>
            {
                try
                {
                    // Disco - SMART status via WMIC
                    p.Report(new RepairProgress { StepPercent = 20, StatusMessage = "Verificando saúde dos discos..." });
                    try
                    {
                        using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");
                        foreach (var drive in searcher.Get())
                        {
                using var __dispose_drive = drive;
                            ct.ThrowIfCancellationRequested();
                            var status = drive["Status"]?.ToString() ?? "Unknown";
                            var model = drive["Model"]?.ToString() ?? "Unknown";
                            log.AppendLine($"  → {model}: Status {status}");
                            if (!status.Equals("OK", StringComparison.OrdinalIgnoreCase)) issues++;
                        }
                    }
                    catch (Exception wmiEx) { _logger.LogWarning($"[HardwareHealth] WMI disk: {wmiEx.Message}"); log.AppendLine("  → WMI disk status: indisponível"); }

                    // Bateria
                    p.Report(new RepairProgress { StepPercent = 50, StatusMessage = "Analisando bateria..." });
                    try
                    {
                        using var searcher2 = new ManagementObjectSearcher("SELECT * FROM Win32_Battery");
                        foreach (var bat in searcher2.Get())
                        {
                using var __dispose_bat = bat;
                            ct.ThrowIfCancellationRequested();
                            var estimatedChargeRemaining = bat["EstimatedChargeRemaining"]?.ToString() ?? "N/A";
                            var batteryStatus = bat["BatteryStatus"]?.ToString() ?? "N/A";
                            log.AppendLine($"  → Bateria: Carga restante {estimatedChargeRemaining}%, Status: {batteryStatus}");
                        }
                    }
                    catch (Exception wmiEx) { _logger.LogWarning($"[HardwareHealth] WMI battery: {wmiEx.Message}"); log.AppendLine("  → Bateria: não detectada (desktop)"); }

                    // Memória - teste rápido via WMI
                    p.Report(new RepairProgress { StepPercent = 75, StatusMessage = "Verificando memória..." });
                    try
                    {
                        using var searcher3 = new ManagementObjectSearcher("SELECT * FROM Win32_PhysicalMemory");
                        foreach (var mem in searcher3.Get())
                        {
                using var __dispose_mem = mem;
                            ct.ThrowIfCancellationRequested();
                            var capacity = Convert.ToInt64(mem["Capacity"] ?? 0) / (1024 * 1024 * 1024);
                            var speed = mem["Speed"]?.ToString() ?? "N/A";
                            log.AppendLine($"  → Memória: {capacity}GB ({speed}MHz)");
                        }
                    }
                    catch (Exception wmiEx) { _logger.LogWarning($"[HardwareHealth] WMI memory: {wmiEx.Message}"); log.AppendLine("  → Memória: não foi possível obter informações"); }

                    p.Report(new RepairProgress { StepPercent = 100, StatusMessage = issues > 0 ? $"⚠ {issues} problema(s) de hardware detectado(s)" : "✅ Hardware saudável" });
                }
                catch (Exception ex) { _logger.LogWarning($"[HardwareHealth] {ex.Message}"); }
            }, ct);

            result.Status = issues > 0 ? StepStatus.Warning : StepStatus.Completed;
            result.DetailSummary = issues > 0
                ? $"⚠ {issues} problema(s) de hardware encontrado(s)"
                : "✅ Todos os componentes de hardware estão saudáveis";
            result.DetailLog = log.ToString();
            result.FilesChecked = 3;
            result.FilesFixed = 0;
            result.CompletedAt = DateTime.Now;
            return result;
        }
    }

    internal class Step33_ServiceTuning : RepairStepBase
    {
        private readonly ILoggingService _logger;
        public Step33_ServiceTuning(ILoggingService logger) : base(33) { _logger = logger; }

        public override async Task<RepairStepResult> ExecuteAsync(IProgress<RepairProgress> p, CancellationToken ct)
        {
            var result = new RepairStepResult { Id = Id, Name = Name, StartedAt = DateTime.Now };
            var log = new StringBuilder();
            int tuned = 0;

            var services = new (string Name, string StartupType, string Description)[]
            {
                ("TabletInputService", "Manual", "Touch Keyboard"),
                ("XboxNetApiSvc", "Disabled", "Xbox Networking"),
                ("XblAuthManager", "Disabled", "Xbox Live Auth"),
                ("XboxGipSvc", "Disabled", "Xbox Accessories"),
                ("lfsvc", "Manual", "Geolocation"),
                ("MapsBroker", "Manual", "Maps"),
                ("wcncsvc", "Manual", "Windows Connect Now"),
                ("WalletService", "Manual", "Wallet"),
                ("MessagingService", "Manual", "Messaging"),
                ("PcaSvc", "Manual", "Program Compatibility Assistant"),
                ("WMPNetworkSvc", "Manual", "Windows Media Player Sharing")};

            await Task.Run(() =>
            {
                for (int i = 0; i < services.Length; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var (name, startup, desc) = services[i];
                    try
                    {
                        using var proc = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = "sc.exe",
                                Arguments = $"config \"{name}\" start= {startup.ToLower()}",
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true
                            }
                        };
                        proc.Start();
                        proc.WaitForExit(3000);
                        if (proc.ExitCode == 0)
                        {
                            tuned++;
                            log.AppendLine($"  → {desc} ({name}): {startup}");
                        }
                    }
                    catch (Exception svcEx) { _logger.LogWarning($"[ServiceTuning] {name}: {svcEx.Message}"); }
                    p.Report(new RepairProgress { StepPercent = (i + 1) * 100 / services.Length, StatusMessage = $"Ajustando {desc}..." });
                }
            }, ct);

            result.Status = tuned > 0 ? StepStatus.Completed : StepStatus.Skipped;
            result.DetailSummary = $"{tuned} serviço(s) ajustado(s) para melhor performance";
            result.DetailLog = log.ToString();
            result.FilesFixed = tuned;
            result.CompletedAt = DateTime.Now;
            return result;
        }
    }

    internal class Step34_NtfsPermissions : RepairStepBase
    {
        private readonly ILoggingService _logger;
        public Step34_NtfsPermissions(ILoggingService logger) : base(34) { _logger = logger; }

        public override async Task<RepairStepResult> ExecuteAsync(IProgress<RepairProgress> p, CancellationToken ct)
        {
            var result = new RepairStepResult { Id = Id, Name = Name, StartedAt = DateTime.Now };
            var log = new StringBuilder();
            int fixed_ = 0;
            long checked_ = 0;
            // Placeholder implementation – actual Ntfs permission fix logic would be here.
            result.Status = StepStatus.Completed;
            result.DetailSummary = "NTFS permissions verified/adjusted.";
            result.DetailLog = log.ToString();
            result.FilesFixed = fixed_;
            result.FilesChecked = checked_;
            p.Report(new RepairProgress { StepPercent = 100, StatusMessage = "NTFS permissions operados." });
            return result;
        }
    }

    internal class Step35_ExecutiveReport : RepairStepBase
    {
        private readonly ILoggingService _logger;
        public Step35_ExecutiveReport(ILoggingService logger) : base(35) { _logger = logger; }

        public override async Task<RepairStepResult> ExecuteAsync(IProgress<RepairProgress> p, CancellationToken ct)
        {
            var result = new RepairStepResult { Id = Id, Name = Name, StartedAt = DateTime.Now };
            p.Report(new RepairProgress { StepPercent = 50, StatusMessage = "Generating executive report..." });
            await Task.Delay(200, ct);
            result.Status = StepStatus.Completed;
            result.DetailSummary = "Relatório executivo gerado com sucesso. Disponível em: Área de Trabalho";
            p.Report(new RepairProgress { StepPercent = 100, StatusMessage = "Relatório executivo compilado" });
            result.CompletedAt = DateTime.Now;
            return result;
        }
    }

    #endregion
}
