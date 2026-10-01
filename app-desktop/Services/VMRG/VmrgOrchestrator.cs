using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Core;
using VoltrisOptimizer.Core.VMRG.Interfaces;
using VoltrisOptimizer.Core.VMRG.Models;
using VoltrisOptimizer.Services.Scheduling;
using VoltrisOptimizer.Services.Gamer.Interfaces;

namespace VoltrisOptimizer.Services.VMRG
{
    public class VmrgOrchestrator : IVmrgOrchestrator, IDisposable
    {
        private readonly IVmMonitorService _monitor;
        private readonly IVmDecisionEngine _decisionEngine;
        private readonly IVmActionExecutor _actionExecutor;
        private readonly IVmValidationService _validationService;
        private readonly IVmLearningService _learningService;
        private readonly ILoggingService _logger;
        private readonly IGamerModeOrchestrator _gamerMode;
        private readonly SemaphoreSlim _metricsLock = new(1, 1);

        private CancellationTokenSource? _cts;
        private bool _disposed;

        private const string SchedulerTaskId = "VMRG.Governance";

        public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;

        public VmrgOrchestrator(
            IVmMonitorService monitor,
            IVmDecisionEngine decisionEngine,
            IVmActionExecutor actionExecutor,
            IVmValidationService validationService,
            IVmLearningService learningService,
            ILoggingService logger,
            IGamerModeOrchestrator gamerMode)
        {
            _monitor = monitor;
            _decisionEngine = decisionEngine;
            _actionExecutor = actionExecutor;
            _validationService = validationService;
            _learningService = learningService;
            _logger = logger;
            _gamerMode = gamerMode;
        }

        public async Task StartAsync(CancellationToken ct)
        {
            if (IsRunning) return;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            _logger.LogInfo("[VMRG] Inicializando VMRG...");

            await _learningService.LoadAsync(_cts.Token);

            SystemMetricsCache.Instance.MetricsUpdated += OnMetricsUpdatedHandler;

            CentralizedBackgroundScheduler.Global.RegisterTaskAsync(
                SchedulerTaskId,
                GovernanceCycleAsync,
                SchedulerPriority.Normal,
                TimeSpan.FromSeconds(5));

            _logger.LogSuccess("[VMRG] VMRG iniciado (5s refresh, SystemMetricsCache events)");
        }

        public async Task StopAsync()
        {
            _logger.LogInfo("[VMRG] Parando VMRG...");

            SystemMetricsCache.Instance.MetricsUpdated -= OnMetricsUpdatedHandler;
            CentralizedBackgroundScheduler.Global.UnregisterTask(SchedulerTaskId);

            _cts?.Cancel();

            await RestoreAllVmsAsync();

            await _learningService.SaveAsync(CancellationToken.None);

            _logger.LogSuccess("[VMRG] VMRG parado e recursos restaurados");
        }

        private readonly SemaphoreSlim _governanceLock = new(1, 1);

        private async Task OnMetricsUpdatedAsync(object? sender, EventArgs e)
        {
            if (!await _metricsLock.WaitAsync(0)) return;

            try
            {
                var vms = _monitor.ActiveVms;
                if (vms.Count == 0) return;

                var foregroundVm = vms.FirstOrDefault(v => v.WindowState == VmWindowState.Foreground);
                if (foregroundVm != null)
                {
                    await ProcessVmAsync(foregroundVm, isFromEvent: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[VMRG] Erro no callback de métricas: {ex.Message}", ex);
            }
            finally
            {
                _metricsLock.Release();
            }
        }

        private async Task GovernanceCycleAsync(CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return;

            // Prevenir execução concorrente do ciclo de governança
            if (!await _governanceLock.WaitAsync(0))
            {
                _logger.LogTrace("[VMRG] Ciclo anterior ainda em execução — pulando");
                return;
            }

            var sw = Stopwatch.StartNew();

            try
            {
                await _monitor.RefreshAsync(ct);

                var vms = _monitor.ActiveVms;
                var hosts = _monitor.HostProcesses;

                if (vms.Count == 0 && hosts.Count == 0)
                {
                    _logger.LogTrace("[VMRG] Nenhuma VM ou Host detectado — ciclo ocioso");
                    return;
                }

                var context = BuildContext();
                LogVmSummary(vms, context); // Podemos melhorar o log no futuro para incluir os hosts

                foreach (var vm in vms)
                {
                    await ProcessVmAsync(vm, context, false, ct);
                }

                foreach (var host in hosts)
                {
                    await ProcessVmAsync(host, context, false, ct);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError($"[VMRG] Erro no ciclo de governança: {ex.Message}", ex);
            }
            finally
            {
                sw.Stop();
                if (sw.ElapsedMilliseconds > 100)
                {
                    _logger.LogTrace($"[VMRG] Ciclo concluído em {sw.ElapsedMilliseconds}ms");
                }
                _governanceLock.Release();
            }
        }

        private async Task ProcessVmAsync(VmInfo vm, VmContext? context = null, bool isFromEvent = false, CancellationToken ct = default)
        {
            var ctx = context ?? BuildContext();

            var snapshotBefore = CaptureSnapshot(vm);

            var decision = await _decisionEngine.DecideAsync(vm, ctx, ct);

            LogDecision(vm, decision, ctx);

            if (decision.Action == VmDecision.DecisionType.NoAction)
                return;

            var executed = await _actionExecutor.ExecuteAsync(decision, ct);

            if (!executed)
            {
                _logger.LogWarning($"[VMRG] Falha na execução: {decision.Action} para VM {vm.ProcessName} (PID:{vm.ProcessId})");
                return;
            }

            if (decision.RequiresValidation)
            {
                await Task.Delay(1000, ct);

                var snapshotAfter = CaptureSnapshot(vm);
                var validation = await _validationService.ValidateAsync(snapshotBefore, snapshotAfter, decision, ct);

                LogValidation(decision, validation);

                if (validation.ShouldRollback)
                {
                    _logger.LogWarning($"[VMRG] Rollback automático: {decision.Action} não produziu benefício — {validation.Details}");
                    await _actionExecutor.RollbackAsync(vm, ct);
                }

                await _learningService.RecordOutcomeAsync(new VmLearningEntry
                {
                    Hypervisor = vm.Hypervisor,
                    MachineId = Environment.MachineName,
                    DayOfWeek = DateTime.UtcNow.DayOfWeek,
                    Hour = DateTime.UtcNow.Hour,
                    UserActive = ctx.LastInputMs < 30000,
                    SystemCpuLoad = ctx.SystemCpuPercent,
                    SystemRamLoad = ctx.SystemRamPercent,
                    AppliedAction = decision.Action,
                    WasEffective = validation.Success && !validation.ShouldRollback,
                    ImprovementScore = -validation.CpuDelta,
                    RecordedAt = DateTime.UtcNow
                }, ct);
            }
        }

        private async void OnMetricsUpdatedHandler(object? sender, EventArgs e)
        {
            try
            {
                await OnMetricsUpdatedAsync(sender, e);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[VMRG] Erro no handler de métricas: {ex.Message}", ex);
            }
        }

        private VmContext BuildContext()
        {
            var cache = SystemMetricsCache.Instance;
            return new VmContext
            {
                SystemCpuPercent = cache.CpuPercent,
                SystemRamPercent = cache.MemoryUsedPercent,
                SystemDiskPercent = cache.DiskUsagePercent,
                AvailableRamMb = (long)cache.AvailableRamMb,
                LastInputMs = (int)Math.Min(cache.LastInputMs, int.MaxValue),
                GamerModeActive = _gamerMode.IsActive,
                GameRunning = CheckGameRunning(),
                ResponsivenessScore = ReadVisgScore()
            };
        }

        private bool CheckGameRunning()
        {
            try
            {
                return _gamerMode.Status.IsActive && !string.IsNullOrEmpty(_gamerMode.Status.ActiveGameName);
            }
            catch
            {
                return false;
            }
        }

        private static double ReadVisgScore()
        {
            return 100;
        }

        private static VmInfo CaptureSnapshot(VmInfo vm)
        {
            return new VmInfo
            {
                ProcessId = vm.ProcessId,
                ProcessName = vm.ProcessName,
                Hypervisor = vm.Hypervisor,
                WindowState = vm.WindowState,
                CpuUsagePercent = vm.CpuUsagePercent,
                WorkingSetMb = vm.WorkingSetMb,
                CurrentPriority = vm.CurrentPriority,
                AffinityMask = vm.AffinityMask,
                OriginalPriority = vm.OriginalPriority,
                OriginalAffinityMask = vm.OriginalAffinityMask
            };
        }

        private async Task RestoreAllVmsAsync()
        {
            foreach (var vm in _monitor.ActiveVms)
            {
                if (vm.ChangesApplied)
                {
                    await _actionExecutor.RollbackAsync(vm, CancellationToken.None);
                }
            }
        }

        private void LogVmSummary(IReadOnlyList<VmInfo> vms, VmContext context)
        {
            foreach (var vm in vms)
            {
                _logger.LogDebug($"[VMRG] VM ativa: {vm.ProcessName} (PID:{vm.ProcessId}) | " +
                    $"Hipervisor:{vm.HypervisorDisplayName} | " +
                    $"Janela:{vm.WindowState} | " +
                    $"CPU:{vm.CpuUsagePercent:F1}% | " +
                    $"RAM:{vm.WorkingSetMb}MB | " +
                    $"Pri:{vm.CurrentPriority}");
            }
        }

        private void LogDecision(VmInfo vm, VmDecision decision, VmContext context)
        {
            var entry = $"[VMRG] Decisão: VM={vm.ProcessName} PID={vm.ProcessId} " +
                $"Hipervisor={vm.HypervisorDisplayName} " +
                $"Janela={vm.WindowState} " +
                $"Ação={decision.Action} " +
                $"Confiança={decision.Confidence:P0} " +
                $"Motivo=\"{decision.Reason}\" " +
                $"Sistema=CPU:{context.SystemCpuPercent:F0}% RAM:{context.SystemRamPercent:F0}% " +
                $"GamerMode={context.GamerModeActive} " +
                $"JogoRodando={context.GameRunning}";

            if (decision.Action == VmDecision.DecisionType.NoAction)
                _logger.LogTrace(entry);
            else
                _logger.LogInfo(entry);

            App.TelemetryService?.TrackEvent("VMRG_Decision", new System.Collections.Generic.Dictionary<string, object>
            {
                ["vm"] = vm.ProcessName,
                ["hypervisor"] = vm.HypervisorDisplayName,
                ["pid"] = vm.ProcessId,
                ["window_state"] = vm.WindowState.ToString(),
                ["action"] = decision.Action.ToString(),
                ["confidence"] = decision.Confidence,
                ["reason"] = decision.Reason,
                ["system_cpu"] = context.SystemCpuPercent,
                ["system_ram"] = context.SystemRamPercent,
                ["gamer_mode"] = context.GamerModeActive,
                ["game_running"] = context.GameRunning
            });
        }

        private void LogValidation(VmDecision decision, VmValidationResult validation)
        {
            _logger.LogInfo($"[VMRG] Validação: Ação={decision.Action} " +
                $"Sucesso={validation.Success} " +
                $"CPUΔ={validation.CpuDelta:F1}% " +
                $"RAMΔ={validation.RamDelta}MB " +
                $"Rollback={validation.ShouldRollback} " +
                $"Detalhes=\"{validation.Details}\"");

            App.TelemetryService?.TrackEvent("VMRG_Validation", new System.Collections.Generic.Dictionary<string, object>
            {
                ["action"] = decision.Action.ToString(),
                ["success"] = validation.Success,
                ["cpu_delta"] = validation.CpuDelta,
                ["ram_delta"] = validation.RamDelta,
                ["should_rollback"] = validation.ShouldRollback,
                ["details"] = validation.Details
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cts?.Cancel();
            _cts?.Dispose();
            _metricsLock.Dispose();
            SystemMetricsCache.Instance.MetricsUpdated -= OnMetricsUpdatedHandler;
        }
    }
}
