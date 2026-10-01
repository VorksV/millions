using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace VoltrisOptimizer.Services.Gamer.OptimizationModules
{
    /// <summary>
    /// Módulo de offload de processos - reduz prioridade de processos de background
    /// e otimiza afinidade de processos do jogo
    /// 
    /// REUTILIZA: IProcessOptimizationService (não duplica lógica)
    /// </summary>
    public class ProcessCountOffloadModule : IGamerOptimizationModule
    {
        public string Name => "Process Count Offload";
        public string Description => "Reduz prioridade de processos de background e otimiza processos do jogo";

        public async Task<ModuleApplyResult> ApplyAsync(GamerSessionContext ctx, CancellationToken ct = default)
        {
            var result = new ModuleApplyResult();
            var changes = new System.Collections.Generic.List<string>();

            try
            {
                ctx.Logger.LogInfo("[ProcessOffload] Aplicando otimizações de processo...");

                // 1. Reduzir prioridade de processos de background
                // REUTILIZA: IProcessOptimizationService.LowerBackgroundPriorities()
                ctx.ProcessService.LowerBackgroundPriorities();
                changes.Add("Prioridades de background reduzidas");
                result.ChangesApplied++;

                // 2. Otimização do processo do jogo
                // NOTA: A otimização do processo do jogo (Prioridade/Afinidade) é gerenciada exclusivamente
                // pelo RealGameBoosterService para evitar conflitos de afinidade (Core 0 isolation).
                // Este módulo foca apenas em liberar recursos de processos em background.
                
                if (ctx.TargetGameProcessId.HasValue)
                {
                    changes.Add($"Processo alvo identificado: PID {ctx.TargetGameProcessId.Value} (Protegido)");
                }

                result.Success = true;
                result.AppliedChanges = changes.ToArray();
                ctx.Logger.LogSuccess($"[ProcessOffload] {result.ChangesApplied} otimizações aplicadas");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                ctx.Logger.LogError($"[ProcessOffload] Erro ao aplicar: {ex.Message}", ex);
            }

            return await Task.FromResult(result);
        }

        public async Task<ModuleRevertResult> RevertAsync(GamerSessionContext ctx, CancellationToken ct = default)
        {
            var result = new ModuleRevertResult();
            var changes = new System.Collections.Generic.List<string>();

            try
            {
                ctx.Logger.LogInfo("[ProcessOffload] Revertendo otimizações de processo...");

                // 1. Restaurar prioridades de background
                // REUTILIZA: IProcessOptimizationService.RestoreBackgroundPriorities()
                ctx.ProcessService.RestoreBackgroundPriorities();
                changes.Add("Prioridades de background restauradas");
                result.ChangesReverted++;

                // 2. Restaurar processos individuais (se houver snapshots legados ou de outras operações)
                var processSnapshots = ctx.RollbackRegistry.GetProcessSnapshots().ToList();
                foreach (var snapshot in processSnapshots)
                {
                    try
                    {
                        using var proc = Process.GetProcessById(snapshot.ProcessId);
                        
                        // Restaurar prioridade
                        proc.PriorityClass = snapshot.OriginalPriority;
                        changes.Add($"Prioridade restaurada: PID {snapshot.ProcessId}");
                        result.ChangesReverted++;

                        // Restaurar afinidade se disponível
                        if (snapshot.OriginalAffinity.HasValue)
                        {
                            proc.ProcessorAffinity = snapshot.OriginalAffinity.Value;
                            changes.Add($"Afinidade restaurada: PID {snapshot.ProcessId}");
                            result.ChangesReverted++;
                        }
                    }
                    catch (Exception ex)
                    {
                        ctx.Logger.LogWarning($"[ProcessOffload] Erro ao restaurar processo {snapshot.ProcessId}: {ex.Message}");
                    }
                }

                result.Success = true;
                result.RevertedChanges = changes.ToArray();
                ctx.Logger.LogSuccess($"[ProcessOffload] {result.ChangesReverted} otimizações revertidas");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                ctx.Logger.LogError($"[ProcessOffload] Erro ao reverter: {ex.Message}", ex);
            }

            return await Task.FromResult(result);
        }
    }
}

