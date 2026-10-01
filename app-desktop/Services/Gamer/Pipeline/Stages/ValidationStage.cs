using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Gamer.Pipeline.Models;
using VoltrisOptimizer.Services.Gamer.TelemetryEngine;

namespace VoltrisOptimizer.Services.Gamer.Pipeline.Stages
{
    public class ValidationStage : IPipelineStage
    {
        private readonly ILoggingService _logger;
        private readonly IAntiPlaceboValidator _placeboValidator;
        private readonly ISessionMemory _sessionMemory;

        public string Name => "Validation";
        public int Order => 9;

        public ValidationStage(ILoggingService logger, IAntiPlaceboValidator placeboValidator, ISessionMemory sessionMemory)
        {
            _logger = logger;
            _placeboValidator = placeboValidator;
            _sessionMemory = sessionMemory;
        }

        public async Task ExecuteAsync(GamerSessionContext context, CancellationToken ct)
        {
            _logger.LogEntry(nameof(ExecuteAsync));
            _logger.LogInfo($"[Pipeline] [GO] Estágio 9/11: Validation — validando resultados...");

            var appliedDecisions = context.Decisions.Where(d => d.Applied).ToList();
            if (!appliedDecisions.Any())
            {
                _logger.LogInfo("[Validation] Nenhuma otimização aplicada para validar");
                return;
            }

            // Coletar baseline do histórico da sessão (antes das otimizações)
            var baselineSnapshot = context.TelemetryHistory.FirstOrDefault();
            double baselineFps = baselineSnapshot?.Fps > 0 ? baselineSnapshot.Fps : await EstimateFpsFromGame(context);

            // Aguardar estabilização após aplicar otimizações
            _logger.LogInfo("[Validation] ⏳ Aguardando 3s para estabilização...");
            await Task.Delay(3000, ct);

            // Capturar snapshot atual
            var currentSnapshot = await CaptureCurrentTelemetryAsync(context);
            context.TelemetryHistory.Add(currentSnapshot);

            double currentFps = currentSnapshot.Fps > 0 ? currentSnapshot.Fps : baselineFps;

            _logger.LogInfo($"[Validation] 📊 Baseline FPS: {baselineFps:F1} → Atual: {currentFps:F1} | Baseline FrameTime: {1000.0 / Math.Max(baselineFps, 1):F1}ms → Atual: {1000.0 / Math.Max(currentFps, 1):F1}ms");

            foreach (var decision in appliedDecisions)
            {
                try
                {
                    ct.ThrowIfCancellationRequested();

                    var passed = await _placeboValidator.ValidateOptimizationAsync(
                        decision.Id,
                        baselineFps,
                        currentFps,
                        1000.0 / Math.Max(baselineFps, 1),
                        1000.0 / Math.Max(currentFps, 1));

                    decision.Validated = passed;

                    if (passed)
                    {
                        decision.MeasuredImprovementPercent = baselineFps > 0
                            ? ((currentFps - baselineFps) / baselineFps) * 100
                            : 0;

                        _logger.LogInfo($"[Validation] [OK] {decision.DisplayName}: aprovado (melhoria: {decision.MeasuredImprovementPercent:F1}%)");

                        context.Results.Add(new OptimizationResult
                        {
                            OptimizationId = decision.Id,
                            Success = true,
                            PassedValidation = true,
                            MeasuredFpsDelta = currentFps - baselineFps,
                            AppliedAt = DateTime.UtcNow
                        });
                    }
                    else
                    {
                        _logger.LogWarning($"[Validation] [FAIL] {decision.DisplayName}: reprovado — sem ganho mensurável. Revertendo...");

                        context.Results.Add(new OptimizationResult
                        {
                            OptimizationId = decision.Id,
                            Success = false,
                            PassedValidation = false,
                            FailureReason = "Placebo detectado — sem melhoria mensurável",
                            AppliedAt = DateTime.UtcNow,
                            RolledBackAt = DateTime.UtcNow
                        });

                        try { await decision.RollbackAsync(ct); }
                        catch (Exception ex) { _logger.LogError($"[Validation] Erro no rollback: {ex.Message}"); }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError($"[Validation] Erro validando {decision.DisplayName}: {ex.Message}");
                }

                await Task.Delay(500, ct);
            }

            int approved = context.Results.Count(r => r.PassedValidation);
            int rejected = context.Results.Count(r => !r.PassedValidation);

            _logger.LogInfo($"[Validation] [OK] {approved} aprovadas | [FAIL] {rejected} rejeitadas (placebo)");

            // Persistir sessão na memória
            await _sessionMemory.SaveSessionAsync(context);
            _logger.LogExit(nameof(ExecuteAsync));
        }

        private async Task<TelemetrySnapshot> CaptureCurrentTelemetryAsync(GamerSessionContext context)
        {
            _logger.LogEntry(nameof(CaptureCurrentTelemetryAsync));
            var snapshot = new TelemetrySnapshot { Timestamp = DateTime.UtcNow };

            try
            {
                // Tentar obter FPS do processo do jogo (via overlay ou fallback)
                if (context.GameProcessName != null)
                {
                    var processes = Process.GetProcessesByName(context.GameProcessName);
                    if (processes.Length > 0)
                    {
                        var proc = processes[0];
                        snapshot.Fps = await EstimateFpsFromPerformanceCounterAsync(proc.ProcessName);
                        proc.Dispose();
                    }
                }

                if (snapshot.Fps <= 0)
                {
                    using var perfCpu = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                    snapshot.CpuUsage = Math.Round(perfCpu.NextValue(), 1);
                }

                if (snapshot.Fps > 0)
                    snapshot.FrameTimeMs = Math.Round(1000.0 / snapshot.Fps, 1);

                _logger.LogDebug($"[Validation] Snapshot: FPS={snapshot.Fps:F1} FrameTime={snapshot.FrameTimeMs:F1}ms CPU={snapshot.CpuUsage:F1}%");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Validation] [WARN] Erro ao capturar telemetria: {ex.Message}");
            }

            _logger.LogExit(nameof(CaptureCurrentTelemetryAsync));
            return snapshot;
        }

        private async Task<double> EstimateFpsFromPerformanceCounterAsync(string processName)
        {
            try
            {
                using var counter = new PerformanceCounter("Process", "% Processor Time", processName);
                counter.NextValue();
                await Task.Delay(200);
                double cpuPct = counter.NextValue();

                if (cpuPct > 0)
                {
                    double totalCpu = Environment.ProcessorCount;
                    double estimatedFps = (cpuPct / totalCpu) * 60;
                    return Math.Round(Math.Min(estimatedFps, 240), 0);
                }
            }
            catch (Exception exFps) { _logger?.LogWarning($"[Validation] Erro ao ler PerformanceCounter: {exFps.Message}"); }
            _logger.LogExit(nameof(EstimateFpsFromPerformanceCounterAsync));
            return 0;
        }

        private async Task<double> EstimateFpsFromGame(GamerSessionContext context)
        {
            if (context.GameProcessName == null) return 0;

            try
            {
                var proc = Process.GetProcessesByName(context.GameProcessName).FirstOrDefault();
                if (proc == null) return 0;

                var startCpu = proc.TotalProcessorTime;
                var startTime = DateTime.UtcNow;

                await Task.Delay(500);

                var endCpu = proc.TotalProcessorTime;
                var elapsed = (DateTime.UtcNow - startTime).TotalSeconds;

                if (elapsed > 0)
                {
                    double cpuUsed = (endCpu - startCpu).TotalSeconds;
                    double cpuPct = (cpuUsed / elapsed) / Environment.ProcessorCount * 100;

                    if (cpuPct > 5)
                    {
                        double estimatedFps = cpuPct * 1.5;
                        return Math.Round(Math.Min(estimatedFps, 240), 0);
                    }
                }

                proc.Dispose();
            }
            catch { }
            _logger.LogExit(nameof(EstimateFpsFromGame));
            return 0;
        }
    }
}
