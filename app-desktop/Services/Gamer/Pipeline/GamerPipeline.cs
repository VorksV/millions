using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Gamer.Models;
using VoltrisOptimizer.Services.Gamer.Pipeline.Models;
using VoltrisOptimizer.Services.Gamer.Pipeline.Stages;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services.Gamer.Pipeline
{
    public class GamerPipeline : IGamerPipeline
    {
        private readonly ILoggingService _logger;
        private readonly IEnumerable<IPipelineStage> _stages;

        public GamerPipeline(ILoggingService logger, IEnumerable<IPipelineStage> stages)
        {
            _logger.LogEntry(nameof(GamerPipeline));
            _logger = logger;
            _stages = stages.OrderBy(s => s.Order);

            var stageList = _stages.ToList();
            _logger.LogInfo($"[Pipeline] 🏗 Inicializado com {stageList.Count} estágios: {string.Join(" → ", stageList.Select(s => s.Name))}");
            _logger.LogExit(nameof(GamerPipeline));
        }

        public async Task<GamerSessionContext> ExecuteAsync(
            string? gameExecutable,
            GamerOptimizationOptions userOptions,
            CancellationToken ct = default)
        {
            _logger.LogEntry(nameof(ExecuteAsync));
            var context = new GamerSessionContext
            {
                GameExecutable = gameExecutable,
                GameProcessName = gameExecutable != null
                    ? System.IO.Path.GetFileNameWithoutExtension(gameExecutable)
                    : null,
                UserOptions = userOptions ?? new GamerOptimizationOptions()
            };

            var sw = Stopwatch.StartNew();

            _logger.LogInfo("""
═══════════════════════════════════
  🎮 PIPELINE MODO GAMER — INICIADO
═══════════════════════════════════
""");

            foreach (var stage in _stages)
            {
                try
                {
                    ct.ThrowIfCancellationRequested();

                    var stageSw = Stopwatch.StartNew();
                    await stage.ExecuteAsync(context, ct);
                    stageSw.Stop();

                    _logger.LogInfo($"[Pipeline] ✓ {stage.Name} concluído em {stageSw.ElapsedMilliseconds}ms");
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning($"[Pipeline] ⏹ Pipeline cancelado no estágio {stage.Name}");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[Pipeline] ❌ Erro no estágio {stage.Name}: {ex.Message}");

                    if (stage.Order > 6)
                    {
                        _logger.LogWarning("[Pipeline] Tentando continuar apesar do erro (estágio não-crítico)...");
                    }
                    else
                    {
                        _logger.LogError("[Pipeline] Erro em estágio crítico — abortando pipeline");
                        break;
                    }
                }
            }

            sw.Stop();
            _logger.LogSuccess($"[Pipeline] ✅ Pipeline concluído em {sw.ElapsedMilliseconds}ms");

            _logger.LogInfo("""
═══════════════════════════════════
  🎮 PIPELINE MODO GAMER — CONCLUÍDO
═══════════════════════════════════
""");

            _logger.LogExit(nameof(ExecuteAsync));
            return context;
        }
    }
}
