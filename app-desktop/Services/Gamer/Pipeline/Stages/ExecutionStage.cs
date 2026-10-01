using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Helpers;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Gamer.Overlay.Implementation;
using VoltrisOptimizer.Services.Gamer.Pipeline.Models;

namespace VoltrisOptimizer.Services.Gamer.Pipeline.Stages
{
    public class ExecutionStage : IPipelineStage
    {
        private readonly ILoggingService _logger;

        public string Name => "Execution";
        public int Order => 8;

        public ExecutionStage(ILoggingService logger)
        {
            _logger = logger;
        }

        public async Task ExecuteAsync(GamerSessionContext context, CancellationToken ct)
        {
            _logger.LogEntry(nameof(ExecuteAsync));
            _logger.LogInfo($"[Pipeline] [GO] Estágio 8/11: Execution — aplicando {context.Decisions.Count} otimizações...");

            foreach (var decision in context.Decisions.Where(d => !d.Applied))
            {
                try
                {
                    ct.ThrowIfCancellationRequested();

                    _logger.LogInfo($"[Execution] ▶ Aplicando: {decision.DisplayName}...");
                    var success = await decision.ApplyAsync(ct);

                    if (success)
                    {
                        decision.Applied = true;
                        _logger.LogInfo($"[Execution] ✅ {decision.DisplayName} — aplicado com sucesso");
                    }
                    else
                    {
                        _logger.LogWarning($"[Execution] ⚠ {decision.DisplayName} — falha na aplicação");
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning($"[Execution] ⏹ {decision.DisplayName} — cancelado");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[Execution] ❌ {decision.DisplayName} — erro: {ex.Message}");

                    if (decision.HasRollback)
                    {
                        try { await decision.RollbackAsync(ct); } catch { }
                    }
                }

                await Task.Delay(200, ct);
            }

            int appliedCount = context.Decisions.Count(d => d.Applied);
            _logger.LogInfo($"[Execution] ✅ {appliedCount}/{context.Decisions.Count} otimizações aplicadas");

            if (context.GameProcessId.HasValue)
            {
                try
                {
                    var gameProc = Process.GetProcessById(context.GameProcessId.Value);
                    if (gameProc != null && context.Policies.AllowProcessPriorityChanges)
                    {
                        gameProc.PriorityClass = ProcessPriorityClass.High;
                        _logger.LogInfo($"[Execution] 🎮 Prioridade do jogo {context.GameProcessName} definida: High");
                    }
                }
                catch { }
            }
            _logger.LogExit(nameof(ExecuteAsync));
        }
    }
}
