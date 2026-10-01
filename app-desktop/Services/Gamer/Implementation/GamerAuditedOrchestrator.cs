using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Services.Gamer.Interfaces;
using VoltrisOptimizer.Services.Gamer.Models;

namespace VoltrisOptimizer.Services.Gamer.Implementation
{
    public class GamerAuditedOrchestrator : IGamerModeOrchestrator, IDisposable
    {
        private readonly ILoggingService _logger;
        private readonly GamerExecutionTracer _tracer;
        private readonly GamerValidationEngine _validator;
        private readonly GamerModeAuditor _auditor;

        private readonly ICpuGamingOptimizer _cpuOptimizer;
        private readonly IGpuGamingOptimizer _gpuOptimizer;
        private readonly IMemoryGamingOptimizer _memoryOptimizer;
        private readonly IProcessPrioritizer _processPrioritizer;
        private readonly IHardwareDetector _hardwareDetector;

        private bool _isActive = true;
        private int? _activeGameProcessId = null;
        private readonly SemaphoreSlim _activationLock = new(1, 1);

        public GamerAuditedOrchestrator(ILoggingService logger, ICpuGamingOptimizer cpuOptimizer, IGpuGamingOptimizer gpuOptimizer, IMemoryGamingOptimizer memoryOptimizer, IProcessPrioritizer processPrioritizer, IHardwareDetector hardwareDetector, GamerModeAuditor auditor)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogEntry(nameof(GamerAuditedOrchestrator));
            _cpuOptimizer = cpuOptimizer ?? throw new ArgumentNullException(nameof(cpuOptimizer));
            _gpuOptimizer = gpuOptimizer ?? throw new ArgumentNullException(nameof(gpuOptimizer));
            _memoryOptimizer = memoryOptimizer ?? throw new ArgumentNullException(nameof(memoryOptimizer));
            _processPrioritizer = processPrioritizer ?? throw new ArgumentNullException(nameof(processPrioritizer));
            _hardwareDetector = hardwareDetector ?? throw new ArgumentNullException(nameof(hardwareDetector));
            _auditor = auditor ?? throw new ArgumentNullException(nameof(auditor));

            _tracer = new GamerExecutionTracer(_logger);
            _validator = new GamerValidationEngine(_logger, _hardwareDetector, _cpuOptimizer, _gpuOptimizer);

            _tracer.RegisterExpectedModules("CPU", "GPU", "MEMORY", "PROCESS", "PRIORITY", "POWER");
            _logger.LogExit(nameof(GamerAuditedOrchestrator));
        }

        public async Task<bool> ActivateAsync(Models.GamerOptimizationOptions options, string? gameExecutable = null, IProgress<int>? progress = null, CancellationToken cancellationToken = default, bool isManual = true)
        {
            _logger.LogEntry(nameof(ActivateAsync));
            await _activationLock.WaitAsync(cancellationToken);

            try
            {
                if (_isActive)
                {
                    _logger.LogWarning("[GamerAuditor] Modo Gamer já está ativo");
                    _logger.LogExit(nameof(ActivateAsync));
                    return false;
                }

                _logger.LogInfo("[GamerAuditor] INICIANDO ATIVAÇÃO COMPLETA DO MODO GAMER");
                progress?.Report(0);

                int? gameProcessId = await DetectGameProcessAsync(gameExecutable, cancellationToken);

                if (gameProcessId.HasValue)
                {
                    _activeGameProcessId = gameProcessId.Value;
                    _logger.LogInfo($"[GamerAuditor] JOGO DETECTADO (PID: {gameProcessId})");
                }
                else
                {
                    _logger.LogWarning("[GamerAuditor] Nenhum processo de jogo detectado - aplicando otimizações globais");
                }

                var cpuSuccess = await _tracer.ExecuteWithTraceAsync("CPU", "Optimization", async () =>
                {
                    return await _cpuOptimizer.OptimizeAsync(cancellationToken);
                }, validator: result => result);

                if (!cpuSuccess)
                {
                    _logger.LogError("[GamerAuditor] FALHA CRÍTICA: Otimização de CPU falhou");
                    await RollbackAllAsync(cancellationToken);
                    _logger.LogExit(nameof(ActivateAsync));
                    return false;
                }

                progress?.Report(25);

                var gpuSuccess = await _tracer.ExecuteWithTraceAsync("GPU", "Optimization", async () =>
                {
                    return await _gpuOptimizer.OptimizeAsync(cancellationToken);
                }, validator: result => result);

                if (!gpuSuccess)
                {
                    _logger.LogError("[GamerAuditor] FALHA CRÍTICA: Otimização de GPU falhou");
                    await RollbackAllAsync(cancellationToken);
                    _logger.LogExit(nameof(ActivateAsync));
                    return false;
                }

                progress?.Report(50);

                var memorySuccess = await _tracer.ExecuteWithTraceAsync("MEMORY", "Optimization", async () =>
                {
                    return _memoryOptimizer.CleanStandbyList();
                }, validator: result => result);

                progress?.Report(65);

                if (gameProcessId.HasValue)
                {
                    var processSuccess = await _tracer.ExecuteWithTraceAsync("PROCESS", "Priority", async () =>
                    {
                        _processPrioritizer.SetPriority(gameProcessId.Value, ProcessPriorityLevel.High);
                        return true;
                    }, validator: result => result);

                    if (!processSuccess)
                    {
                        _logger.LogWarning("[GamerAuditor] Falha na prioridade do processo - continuando");
                    }
                }

                progress?.Report(80);

                _logger.LogInfo("[GamerAuditor] INICIANDO VALIDAÇÃO REAL DE OTIMIZAÇÕES");

                var validationReport = await _validator.ValidateAllOptimizationsAsync(gameProcessId, cancellationToken);

                if (!validationReport.IsOverallSuccess)
                {
                    _logger.LogError($"[GamerAuditor] VALIDAÇÃO FALHOU: {validationReport.PassedChecks}/{validationReport.TotalChecks} checks passaram");

                    foreach (var validation in new[] { validationReport.CpuValidation, validationReport.GpuValidation, validationReport.ProcessValidation })
                    {
                        foreach (var check in validation.Checks.Where(c => !c.Passed))
                        {
                            _logger.LogError($"[GamerAuditor] {validation.ModuleName}.{check.Name}: Expected = {check.Expected}, Actual = {check.Actual}");
                        }
                    }

                    _auditor.LogOptimization("VALIDATION", "RealCheck", false, $"{validationReport.PassedChecks}/{validationReport.TotalChecks} validações passaram");
                }
                else
                {
                    _logger.LogSuccess($"[GamerAuditor] VALIDAÇÃO SUCESSO: {validationReport.PassedChecks}/{validationReport.TotalChecks} checks passaram");
                    _auditor.LogOptimization("VALIDATION", "RealCheck", true, $"{validationReport.PassedChecks}/{validationReport.TotalChecks} validações passaram");
                }

                progress?.Report(90);

                var executionValidation = _tracer.ValidateExecution();

                if (!executionValidation.IsComplete)
                {
                    _logger.LogError($"[GamerAuditor] EXECUÇÃO INCOMPLETA: Módulos não executados: {string.Join(",", executionValidation.MissingModules)}");
                    await RollbackAllAsync(cancellationToken);
                    _logger.LogExit(nameof(ActivateAsync));
                    return false;
                }

                _isActive = true;
                progress?.Report(100);

                var finalReport = _tracer.GenerateReport();
                LogFinalReport(finalReport, validationReport);

                _logger.LogSuccess($"[GamerAuditor] MODO GAMER ATIVADO COM SUCESSO em {finalReport.Duration.TotalMilliseconds:F0} ms");

                _logger.LogExit(nameof(ActivateAsync));
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("[GamerAuditor] FALHA CRÍTICA NA ATIVAÇÃO", ex);
                await RollbackAllAsync(cancellationToken);
                _logger.LogExit(nameof(ActivateAsync));
                return false;
            }
            finally
            {
                _activationLock.Release();
            }
        }

        public async Task<bool> DeactivateAsync(IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        {
            _logger.LogEntry(nameof(DeactivateAsync));
            await _activationLock.WaitAsync(cancellationToken);

            try
            {
                if (!_isActive)
                {
                    _logger.LogInfo("[GamerAuditor] Modo Gamer não está ativo");
                    _logger.LogExit(nameof(DeactivateAsync));
                    return true;
                }

                _logger.LogInfo("[GamerAuditor] INICIANDO DESATIVAÇÃO COMPLETA DO MODO GAMER");

                var success = await RollbackAllAsync(cancellationToken);

                if (success)
                {
                    _isActive = false;
                    _activeGameProcessId = null;
                    _logger.LogSuccess("[GamerAuditor] MODO GAMER DESATIVADO COM SUCESSO");
                }
                else
                {
                    _logger.LogError("[GamerAuditor] FALHA PARCIAL NA DESATIVAÇÃO - ALGUMAS OTIMIZAÇÕES PODEM PERMANECER ATIVAS");
                }

                _logger.LogExit(nameof(DeactivateAsync));
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError("[GamerAuditor] FALHA CRÍTICA NA DESATIVAÇÃO", ex);
                _logger.LogExit(nameof(DeactivateAsync));
                return false;
            }
            finally
            {
                _activationLock.Release();
            }
        }

        private async Task<bool> RollbackAllAsync(CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(RollbackAllAsync));
            _logger.LogInfo("[GamerAuditor] INICIANDO ROLLBACK COMPLETO");
            var rollbackSuccess = true;

            try
            {
                await _tracer.ExecuteWithTraceAsync("CPU", "Rollback", async () =>
                {
                    return await _cpuOptimizer.RestoreAsync(cancellationToken);
                });
            }
            catch (Exception ex)
            {
                _logger.LogError("[GamerAuditor] Falha no rollback de CPU", ex);
                rollbackSuccess = false;
            }

            try
            {
                await _tracer.ExecuteWithTraceAsync("GPU", "Rollback", async () =>
                {
                    return await _gpuOptimizer.RestoreAsync(cancellationToken);
                });
            }
            catch (Exception ex)
            {
                _logger.LogError("[GamerAuditor] Falha no rollback de GPU", ex);
                rollbackSuccess = false;
            }

            try
            {
                await _tracer.ExecuteWithTraceAsync("MEMORY", "Rollback", async () =>
                {
                    return true;
                });
            }
            catch (Exception ex)
            {
                _logger.LogError("[GamerAuditor] Falha no rollback de Memory", ex);
                rollbackSuccess = false;
            }

            _tracer.GenerateReport();

            _logger.LogExit(nameof(RollbackAllAsync));
            return rollbackSuccess;
        }

        private async Task<int?> DetectGameProcessAsync(string? gameExecutable, CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(DetectGameProcessAsync));
            if (string.IsNullOrEmpty(gameExecutable))
            {
                _logger.LogExit(nameof(DetectGameProcessAsync));
                return null;
            }

            try
            {
                var processName = System.IO.Path.GetFileNameWithoutExtension(gameExecutable);
                var processes = Process.GetProcessesByName(processName);

                if (processes.Length > 0)
                {
                    var process = processes[0];
                    var processId = process.Id;

                    if (!process.HasExited)
                    {
                        _logger.LogInfo($"[GamerAuditor] Processo de jogo validado: {processName} (PID: {processId})");
                        _logger.LogExit(nameof(DetectGameProcessAsync));
                        return processId;
                    }
                }

                _logger.LogExit(nameof(DetectGameProcessAsync));
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[GamerAuditor] Erro ao detectar processo do jogo: {ex.Message}");
                _logger.LogExit(nameof(DetectGameProcessAsync));
                return null;
            }
        }

        private void LogFinalReport(ExecutionReport executionReport, FullValidationReport validationReport)
        {
            _logger.LogEntry(nameof(LogFinalReport));
            _logger.LogInfo("");
            _logger.LogInfo("RELATÓRIO FINAL DE EXECUÇÃO");
            _logger.LogInfo("");
            _logger.LogInfo($"Sessão: {executionReport.SessionId}");
            _logger.LogInfo($"Duração: {executionReport.Duration.TotalMilliseconds:F0} ms");
            _logger.LogInfo($"Operações: {executionReport.TotalOperations}");
            _logger.LogInfo("Módulos executados:");

            foreach (var trace in executionReport.Traces)
            {
                var status = trace.Status == ExecutionStatus.Success ? "?" : "?";
                _logger.LogInfo($"{status} {trace.ModuleName}.{trace.OperationName}: {trace.DurationMs} ms");
            }

            _logger.LogInfo("Validação real:");
            _logger.LogInfo($"Validação: {validationReport.PassedChecks}/{validationReport.TotalChecks}");
            _logger.LogInfo($"Sucesso: {(validationReport.IsOverallSuccess ? "?" : "?")}");

            if (!validationReport.IsOverallSuccess)
            {
                _logger.LogInfo("Falhas de validação:");
                foreach (var validation in new[] { validationReport.CpuValidation, validationReport.GpuValidation, validationReport.ProcessValidation })
                {
                    foreach (var check in validation.Checks.Where(c => !c.Passed))
                    {
                        _logger.LogInfo($"{validation.ModuleName}.{check.Name}: Expected = {check.Expected}, Actual = {check.Actual}");
                    }
                }
            }
            _logger.LogExit(nameof(LogFinalReport));
        }

        public bool IsActive => _isActive;
        public int? ActiveGameProcessId => _activeGameProcessId;

        public Models.GamerModeStatus Status => _isActive ? new Models.GamerModeStatus() : new Models.GamerModeStatus();

        public event EventHandler<Models.GamerModeStatus>? StatusChanged;

        public Models.GamerOptimizationOptions GetCurrentOptions()
        {
            _logger.LogEntry(nameof(GetCurrentOptions));
            _logger.LogExit(nameof(GetCurrentOptions));
            return new Models.GamerOptimizationOptions();
        }

        public void SetOptions(Models.GamerOptimizationOptions options)
        {
            _logger.LogEntry(nameof(SetOptions));
            _logger.LogInfo("[GamerAuditor] Opções definidas (implementação básica)");
            _logger.LogExit(nameof(SetOptions));
        }

        public void StartAutoPilot()
        {
            _logger.LogEntry(nameof(StartAutoPilot));
            _logger.LogInfo("[GamerAuditor] Auto-Pilot iniciado (implementação básica)");
            _logger.LogExit(nameof(StartAutoPilot));
        }

        public void StopAutoPilot()
        {
            _logger.LogEntry(nameof(StopAutoPilot));
            _logger.LogInfo("[GamerAuditor] Auto-Pilot parado (implementação básica)");
            _logger.LogExit(nameof(StopAutoPilot));
        }

        public async Task ApplyPersistentOptimizationsAsync(GamerOptimizationOptions options, CancellationToken cancellationToken)
        {
            _logger.LogEntry(nameof(ApplyPersistentOptimizationsAsync));
            _logger.LogInfo("[GamerAuditor] Aplicando otimizações persistentes (implementação básica)");
            await Task.CompletedTask;
            _logger.LogExit(nameof(ApplyPersistentOptimizationsAsync));
        }

        public async Task RevertPersistentOptimizationsAsync()
        {
            _logger.LogEntry(nameof(RevertPersistentOptimizationsAsync));
            _logger.LogInfo("[GamerAuditor] Revertendo otimizações persistentes (implementação básica)");
            await Task.CompletedTask;
            _logger.LogInfo("[GamerAuditor] Revertendo otimizações persistentes (implementação básica)");
            await Task.CompletedTask;
            _logger.LogExit(nameof(RevertPersistentOptimizationsAsync));
        }

        public async Task<bool> RestoreIfCrashedAsync()
        {
            _logger.LogEntry(nameof(RestoreIfCrashedAsync));
            _logger.LogInfo("[GamerAuditor] Verificando crash recovery (implementação básica)");
            _logger.LogExit(nameof(RestoreIfCrashedAsync));
            return await Task.FromResult(true);
        }

        public GamerExecutionStatus GetDetailedStatus()
        {
            _logger.LogEntry(nameof(GetDetailedStatus));
            _logger.LogExit(nameof(GetDetailedStatus));
            return _tracer.GetCurrentStatus();
        }

        public ExecutionReport GetExecutionReport()
        {
            _logger.LogEntry(nameof(GetExecutionReport));
            _logger.LogExit(nameof(GetExecutionReport));
            return _tracer.GenerateReport();
        }

        public void Dispose()
        {
            _logger.LogEntry(nameof(Dispose));
            try
            {
                if (_isActive)
                {
                    _ = Task.Run(async () =>
                    {
                        try { await DeactivateAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
                        catch { }
                    });
                }
            }
            catch
            {
            }
            _logger.LogExit(nameof(Dispose));
        }
    }
}
