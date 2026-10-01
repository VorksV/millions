using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Optimization.Unification
{
    /// <summary>
    /// Registro Central de Throttle — Enterprise Architecture Bridge.
    ///
    /// Problema resolvido:
    ///   Antes desta classe, VoltrisGuard e DynamicLoadStabilizer podiam
    ///   throttlar o MESMO processo de forma independente. Quando um deles
    ///   restaurava, ele escrevia a prioridade que ele havia salvo (que poderia
    ///   ser a prioridade já modificada pelo outro), criando um estado incoerente.
    ///
    /// Solução:
    ///   Único ponto de verdade para "quem está throttlando quem".
    ///   Funciona como um lock distribuído de prioridade de processo.
    /// </summary>
    public class SharedThrottleRegistry
    {
        private readonly ILoggingService _logger;

        // PID -> (dono do throttle, prioridade ORIGINAL antes de qualquer ação)
        private readonly ConcurrentDictionary<int, ThrottleEntry> _registry = new();

        // Multi-dono: um processo pode ser marcado por mais de um módulo
        // (ex: DSL aplica throttle leve, Guard aplica throttle forte)
        // A prioridade ORIGINAL é salva apenas pelo PRIMEIRO a agir
        private readonly object _lock = new();

        public SharedThrottleRegistry(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Tenta registrar um throttle. Retorna true se este módulo é o primeiro a agir no processo,
        /// e salva a prioridade original. Retorna false se outro módulo já agiu (proteção anti-conflito).
        /// </summary>
        public bool TryRegisterThrottle(int pid, string ownerModule, ProcessPriorityClass originalPriority)
        {
            lock (_lock)
            {
                if (_registry.TryGetValue(pid, out var existing))
                {
                    // Já está sendo throttlado por outro módulo — adicionar como co-dono
                    existing.Owners.Add(ownerModule);
                    _logger.LogInfo($"[SharedThrottleRegistry] PID {pid} já throttlado por '{existing.PrimaryOwner}'. " +
                                    $"'{ownerModule}' adicionado como co-dono. Prioridade original preservada: {existing.OriginalPriority}");
                    return false; // Não aplicar throttle novamente — já está reduzido
                }

                // Primeiro módulo a agir — registra e salva prioridade original real
                var entry = new ThrottleEntry
                {
                    ProcessId = pid,
                    PrimaryOwner = ownerModule,
                    OriginalPriority = originalPriority,
                    ThrottledAt = DateTime.UtcNow,
                    Owners = new HashSet<string> { ownerModule }
                };
                _registry[pid] = entry;
                _logger.LogInfo($"[SharedThrottleRegistry] Throttle REGISTRADO: PID {pid} por '{ownerModule}'. " +
                                $"Prioridade original: {originalPriority}");
                return true;
            }
        }

        /// <summary>
        /// Libera o throttle de um módulo. Se ainda houver co-donos, o processo
        /// permanece throttlado. Só restaura quando TODOS os módulos liberarem.
        /// Retorna a prioridade original para restauração, ou null se ainda há co-donos.
        /// </summary>
        public ProcessPriorityClass? TryRelease(int pid, string releasingModule)
        {
            lock (_lock)
            {
                if (!_registry.TryGetValue(pid, out var entry))
                    return null; // Não estava registrado

                entry.Owners.Remove(releasingModule);

                if (entry.Owners.Count == 0)
                {
                    // Todos os módulos liberaram — pode restaurar
                    _registry.TryRemove(pid, out _);
                    _logger.LogInfo($"[SharedThrottleRegistry] Throttle LIBERADO: PID {pid} por '{releasingModule}'. " +
                                    $"Restaurando para prioridade original: {entry.OriginalPriority}");
                    return entry.OriginalPriority;
                }

                // Ainda há co-donos — não restaurar ainda
                _logger.LogInfo($"[SharedThrottleRegistry] '{releasingModule}' liberou PID {pid}, " +
                                $"mas co-donos restantes: [{string.Join(", ", entry.Owners)}]. Mantendo throttle.");
                return null;
            }
        }

        /// <summary>
        /// Verifica se um processo já está sendo throttlado por qualquer módulo.
        /// </summary>
        public bool IsThrottled(int pid)
        {
            return _registry.ContainsKey(pid);
        }

        /// <summary>
        /// Obtém a prioridade original salva para um PID (para inspeção/debug).
        /// </summary>
        public ProcessPriorityClass? GetOriginalPriority(int pid)
        {
            return _registry.TryGetValue(pid, out var entry) ? entry.OriginalPriority : null;
        }

        /// <summary>
        /// Limpa todos os registros (chamado no shutdown do sistema).
        /// Retorna snapshots para que os módulos possam restaurar processos.
        /// </summary>
        public IReadOnlyList<ThrottleEntry> ClearAll()
        {
            lock (_lock)
            {
                var snapshot = new List<ThrottleEntry>(_registry.Values);
                _registry.Clear();
                _logger.LogInfo($"[SharedThrottleRegistry] Registry limpo. {snapshot.Count} entrada(s) removida(s).");
                return snapshot;
            }
        }

        public class ThrottleEntry
        {
            public int ProcessId { get; set; }
            public string PrimaryOwner { get; set; } = string.Empty;
            public ProcessPriorityClass OriginalPriority { get; set; }
            public DateTime ThrottledAt { get; set; }
            public HashSet<string> Owners { get; set; } = new();
        }
    }
}
