using System;
using System.Collections.Concurrent;
using System.Threading;
using VoltrisOptimizer.Core.Configuration;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Core.Execution
{
    /// <summary>
    /// O verdadeiro Motor de Coordenação (Enterprise Scheduler).
    /// Não apenas serializa, mas detecta conflitos, consolida redundâncias,
    /// aplica debouncing rigoroso e resolve prioridades de contexto.
    /// </summary>
    public sealed class OptimizationScheduler
    {
        private static readonly Lazy<OptimizationScheduler> _instance = new(() => new OptimizationScheduler());
        public static OptimizationScheduler Instance => _instance.Value;

        private readonly ExecutionQueue _queue = new();
        private ILoggingService _logger; // Pode ser injetado pós instanciacão

        // TargetResource -> Last Execution Time
        private readonly ConcurrentDictionary<string, DateTime> _cooldowns = new(StringComparer.OrdinalIgnoreCase);
        
        // TargetResource -> (Context, Priority, DesiredState) da ação que está pendente na fila
        private readonly ConcurrentDictionary<string, PendingActionMeta> _pendingActions = new(StringComparer.OrdinalIgnoreCase);

        private struct PendingActionMeta
        {
            public OptimizationContext Context;
            public ActionPriority Priority;
            public string DesiredState;
            public DateTime RequestedAt;
        }

        private OptimizationScheduler() { }

        public void InitializeLogger(ILoggingService logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Solicita a execução de uma otimização com detecção de Thrashing e resolução de contexto.
        /// </summary>
        public bool RequestAction(OptimizationAction action)
        {
            if (!VoltrisFeatureFlags.Instance.UseEnterpriseScheduler)
            {
                // Bypass para rota legada
                _queue.Enqueue(action);
                return true;
            }

            string resourceKey = action.TargetResource;

            // 1. Debouncing e Cooldown (Thrashing Prevention)
            if (_cooldowns.TryGetValue(resourceKey, out var lastExecution))
            {
                if (DateTime.UtcNow - lastExecution < action.Cooldown)
                {
                    //_logger?.LogTrace($"[SCHEDULER] Ação para {resourceKey} ignorada por Cooldown.");
                    return false;
                }
            }

            // 2. Detecção de Conflitos e Agrupamento (Pending State)
            if (_pendingActions.TryGetValue(resourceKey, out var pending))
            {
                // Se o estado desejado é o mesmo, é uma requisição redundante e pode ser ignorada.
                if (string.Equals(pending.DesiredState, action.DesiredState, StringComparison.OrdinalIgnoreCase))
                {
                    //_logger?.LogTrace($"[SCHEDULER] Ação convergente agrupada: {resourceKey} -> {action.DesiredState}");
                    return false;
                }

                // Há conflito de estados! Regra de resolução:
                // Contexto Crítico (Gaming/VirtualMachine) ganha de Global/Idle.
                if (action.Context > pending.Context || (action.Context == pending.Context && action.Priority > pending.Priority))
                {
                    // A nova ação sobrescreve/tem prioridade. 
                    // Na nossa ExecutionQueue (que idealmente deveria suportar cancelamento da antiga), 
                    // a gente apenas registra que o estado final desejado mudou.
                    // Para simplificar, deixamos a fila fluir mas evitamos enfileirar a mesma.
                    _logger?.LogInfo($"[SCHEDULER] Conflito resolvido em favor do novo contexto: {action.Context} sobre {pending.Context} para {resourceKey}.");
                }
                else
                {
                    // A requisição antiga é mais importante. Ignorar a nova.
                    _logger?.LogInfo($"[SCHEDULER] Ação descartada devido à inferioridade de contexto: {action.Context} perde para {pending.Context}.");
                    return false;
                }
            }

            // 3. Aprovação para Fila
            _pendingActions[resourceKey] = new PendingActionMeta
            {
                Context = action.Context,
                Priority = action.Priority,
                DesiredState = action.DesiredState,
                RequestedAt = DateTime.UtcNow
            };

            // Atualizamos o cooldown antecipadamente
            _cooldowns[resourceKey] = DateTime.UtcNow;

            // Enfileira na fila concorrente assíncrona
            _queue.Enqueue(action);

            return true;
        }

        /// <summary>
        /// Invocado pelo Worker Thread da ExecutionQueue assim que a ação termina.
        /// </summary>
        public void AcknowledgeCompletion(string targetResource)
        {
            _pendingActions.TryRemove(targetResource, out _);
        }
    }
}
